using System;
using System.Linq;
using System.Threading.Tasks;
using HealthExam.Domain.Catalogs;
using HealthExam.Domain.ExamRecords;
using HealthExam.Domain.Patients;
using HealthExam.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace HealthExam.Tests.Infrastructure;

public sealed class RegistrationPersistenceTests
{
    private static HealthExamDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<HealthExamDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new HealthExamDbContext(options);
    }

    [Fact]
    public async Task Insert_and_read_patient_with_child_facts_and_exam_record()
    {
        var divisionId = "DIV-01";
        var patientRefId = Guid.NewGuid();
        var insuranceRefId = Guid.NewGuid();
        var employmentRefId = Guid.NewGuid();
        var relativeRefId = Guid.NewGuid();
        var recordId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();

        var patient = new Patient
        {
            PatientRefID = patientRefId,
            DivisionID = divisionId,
            HisPatientID = 1001,
            PatientCode = "PAT-1001",
            FullName = "Nguyễn Văn An",
            Dob = new DateOnly(1990, 1, 15),
            BirthYear = 1990,
            GenderID = 1,
            IdentityNumber = "001090123456",
            PhoneNumber = "0987654321",
            Email = "an.nguyen@example.com",
            Address = "Hà Nội",
            BloodAboCode = "O",
            BloodRhCode = "+"
        };

        var insurance = new PatientInsurance
        {
            InsuranceRefID = insuranceRefId,
            DivisionID = divisionId,
            PatientRefID = patientRefId,
            InsuranceNumber = "DN4010123456789",
            ValidFrom = new DateOnly(2026, 1, 1),
            ValidTo = new DateOnly(2026, 12, 31),
            IsActive = true
        };

        var employment = new PatientEmployment
        {
            EmploymentRefID = employmentRefId,
            DivisionID = divisionId,
            PatientRefID = patientRefId,
            StaffCode = "NV-001",
            OrgDeptName = "Phòng Kỹ thuật",
            JobTitle = "Kỹ sư phần mềm",
            IsActive = true
        };

        var relative = new PatientRelative
        {
            RelativeRefID = relativeRefId,
            DivisionID = divisionId,
            PatientRefID = patientRefId,
            RelationshipCode = "VO",
            FullName = "Trần Thị Bình",
            PhoneNumber = "0912345678",
            IdentityNumber = "001192123456",
            IsActive = true
        };

        var record = new ExamRecord
        {
            RecordID = recordId,
            DivisionID = divisionId,
            SessionID = sessionId,
            RecordCode = "DK-2026-0001",
            PatientRefID = patientRefId,
            InsuranceRefID = insuranceRefId,
            EmploymentRefID = employmentRefId,
            RelativeRefID = relativeRefId,
            VariantCode = "DTK_01",
            ExamReason = "Khám tuyển dụng",
            State = HealthExam.Domain.Common.ExamRecordState.NotRegistered
        };

        using (var db = CreateContext())
        {
            db.Patients.Add(patient);
            db.PatientInsurances.Add(insurance);
            db.PatientEmployments.Add(employment);
            db.PatientRelatives.Add(relative);
            db.ExamRecords.Add(record);
            await db.SaveChangesAsync();
        }

        using (var readDb = CreateContext())
        {
            // Seed into read context or test query
        }

        // Test with same context instance to verify tracking / navigation
        using (var db = CreateContext())
        {
            db.Patients.Add(patient);
            db.PatientInsurances.Add(insurance);
            db.PatientEmployments.Add(employment);
            db.PatientRelatives.Add(relative);
            db.ExamRecords.Add(record);
            await db.SaveChangesAsync();

            var loadedRecord = await db.ExamRecords
                .Include(r => r.Patient)
                .Include(r => r.Insurance)
                .Include(r => r.Employment)
                .Include(r => r.Relative)
                .FirstOrDefaultAsync(r => r.RecordID == recordId);

            Assert.NotNull(loadedRecord);
            Assert.NotNull(loadedRecord.Patient);
            Assert.Equal("Nguyễn Văn An", loadedRecord.Patient.FullName);
            Assert.Equal("PAT-1001", loadedRecord.Patient.PatientCode);

            Assert.NotNull(loadedRecord.Insurance);
            Assert.Equal("DN4010123456789", loadedRecord.Insurance.InsuranceNumber);

            Assert.NotNull(loadedRecord.Employment);
            Assert.Equal("NV-001", loadedRecord.Employment.StaffCode);

            Assert.NotNull(loadedRecord.Relative);
            Assert.Equal("Trần Thị Bình", loadedRecord.Relative.FullName);
        }
    }

    [Fact]
    public void Model_verifies_registration_place_navigation_on_patient_insurance()
    {
        using var db = CreateContext();
        var entity = db.Model.FindEntityType(typeof(PatientInsurance));
        Assert.NotNull(entity);
        var nav = entity.FindNavigation(nameof(PatientInsurance.RegistrationPlaceOption));
        Assert.NotNull(nav);
        Assert.Equal(typeof(MasterDataOption), nav.TargetEntityType.ClrType);
    }

    [Fact]
    public void Patient_model_has_version_link_and_filtered_active_indexes()
    {
        using var db = CreateContext();
        var entity = db.Model.FindEntityType(typeof(Patient))!;

        Assert.NotNull(entity.FindProperty(nameof(Patient.IsActive)));
        Assert.NotNull(entity.FindProperty(nameof(Patient.ProfileLineageID)));
        Assert.NotNull(entity.FindProperty(nameof(Patient.VersionNumber)));
        Assert.NotNull(entity.FindProperty(nameof(Patient.PreviousPatientRefID)));
        Assert.Contains(entity.GetForeignKeys(), fk =>
            fk.Properties.Single().Name == nameof(Patient.PreviousPatientRefID));

        var identity = entity.GetIndexes().Single(x =>
            x.GetDatabaseName() == "UX_HEX_Patient_Division_ActiveIdentity");
        Assert.True(identity.IsUnique);
        Assert.Equal("\"IsActive\" AND \"IdentityNumber\" <> ''", identity.GetFilter());

        var his = entity.GetIndexes().Single(x =>
            x.GetDatabaseName() == "UX_HEX_Patient_Division_HisPatientID");
        Assert.Equal("\"IsActive\" AND \"HisPatientID\" IS NOT NULL AND \"HisPatientID\" > 0", his.GetFilter());

        var version = entity.GetIndexes().Single(x =>
            x.GetDatabaseName() == "UX_HEX_Patient_Division_Lineage_Version");
        Assert.True(version.IsUnique);
        Assert.Equal(
            new[] { nameof(Patient.DivisionID), nameof(Patient.ProfileLineageID), nameof(Patient.VersionNumber) },
            version.Properties.Select(x => x.Name));
    }

    [Fact]
    public async Task ExamRecord_persists_and_roundtrips_sign_fields()
    {
        using var db = CreateContext();
        var recordId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        var record = new ExamRecord
        {
            RecordID = recordId,
            DivisionID = "DIV-01",
            RecordCode = "REC-SIGN-01",
            SignedFilePath = "2026/09/17/file.pdf",
            SignStatus = ExamRecordSignStatus.Signed,
            HisSignedByEmployeeID = 1274,
            HisSignedAt = now
        };

        db.ExamRecords.Add(record);
        await db.SaveChangesAsync();

        var loaded = await db.ExamRecords.AsNoTracking().FirstOrDefaultAsync(x => x.RecordID == recordId);
        Assert.NotNull(loaded);
        Assert.Equal("2026/09/17/file.pdf", loaded.SignedFilePath);
        Assert.Equal(ExamRecordSignStatus.Signed, loaded.SignStatus);
        Assert.Equal(1274, loaded.HisSignedByEmployeeID);
        Assert.Equal(now, loaded.HisSignedAt);
    }
}

