using System;
using System.Threading.Tasks;
using HealthExam.Application.Patients;
using HealthExam.Domain.Catalogs;
using HealthExam.Domain.Common;
using HealthExam.Domain.ExamRecords;
using HealthExam.Domain.ExamSessions;
using HealthExam.Domain.Patients;
using HealthExam.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HealthExam.Tests.Infrastructure;

/// <summary>
/// `FindActiveProfileAsync`: một phiên bản Patient có thể có NHIỀU dòng active
/// PatientInsurance/PatientEmployment/PatientRelative (mỗi bộ giá trị khác là một dòng, dòng
/// cũ được tái dùng khi trùng). Quy tắc chọn: theo hồ sơ khám GẦN NHẤT của phiên bản; chưa có
/// hồ sơ khám nào thì dòng active mới nhất theo CreatedDate. Chạy InMemory — chốt nhánh LINQ.
/// </summary>
public sealed class PatientRepositoryProfileTests
{
    private static readonly DateTime T0 = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Tra_null_khi_phien_ban_khong_active_hoac_khac_don_vi()
    {
        using var db = new InMemoryTestDb();
        var inactive = SeedPatient(db, isActive: false);
        var otherDivision = SeedPatient(db, divisionId: "OTHER");
        var repo = new PatientRepository(db.Db);

        Assert.Null(await repo.FindActiveProfileAsync(db.Ctx.DivisionId, inactive.PatientRefID));
        Assert.Null(await repo.FindActiveProfileAsync(db.Ctx.DivisionId, otherDivision.PatientRefID));
        Assert.Null(await repo.FindActiveProfileAsync(db.Ctx.DivisionId, Guid.Empty));
    }

    [Fact]
    public async Task Nap_san_danh_muc_cua_patient_va_khong_co_dong_con_thi_ba_khoi_null()
    {
        using var db = new InMemoryTestDb();
        var issuer = db.SeedMaster(MasterDataCategories.IdentityIssuer, "CCS", "Cục Cảnh sát QLHC về TTXH");
        var ethnicity = db.SeedMaster(MasterDataCategories.Ethnicity, "KINH", "Kinh");
        var patient = SeedPatient(db, identityIssuerOptionID: issuer.OptionID, ethnicityOptionID: ethnicity.OptionID);
        var repo = new PatientRepository(db.Db);

        var snap = await repo.FindActiveProfileAsync(db.Ctx.DivisionId, patient.PatientRefID);

        Assert.NotNull(snap);
        Assert.Equal(patient.PatientRefID, snap.Patient.PatientRefID);
        Assert.Equal("CCS", snap.Patient.IdentityIssuerOption.Code);
        Assert.Equal("KINH", snap.Patient.EthnicityOption.Code);
        Assert.Null(snap.Insurance);
        Assert.Null(snap.Employment);
        Assert.Null(snap.Relative);
    }

    [Fact]
    public async Task Chon_dong_con_theo_ho_so_kham_gan_nhat_ke_ca_khi_no_tai_dung_dong_cu()
    {
        using var db = new InMemoryTestDb();
        var insObj = db.SeedMaster(MasterDataCategories.InsuranceObject, "HT", "Hưu trí");
        var place = db.SeedMaster(MasterDataCategories.RegistrationPlace, "79001", "BV Quận 1");
        var occ = db.SeedMaster(MasterDataCategories.Occupation, "GV", "Giáo viên");
        var patient = SeedPatient(db);

        // Dòng A tạo trước, dòng B tạo sau. Hồ sơ khám mới nhất lại dùng A (tái dùng dòng cũ).
        var insA = SeedInsurance(db, patient.PatientRefID, "DN4790000001", insObj.OptionID, place.OptionID, T0);
        var insB = SeedInsurance(db, patient.PatientRefID, "DN4790000002", null, null, T0.AddDays(1));
        var empA = SeedEmployment(db, patient.PatientRefID, occ.OptionID, "NV01", T0);
        var empB = SeedEmployment(db, patient.PatientRefID, null, "NV02", T0.AddDays(1));
        var relA = SeedRelative(db, patient.PatientRefID, "SPOUSE", "Trần Thị B", T0);
        var relB = SeedRelative(db, patient.PatientRefID, "CHILD", "Nguyễn Văn C", T0.AddDays(1));

        var session = db.SeedSession();
        SeedRecord(db, session.SessionID, patient.PatientRefID, insB.InsuranceRefID, empB.EmploymentRefID, relB.RelativeRefID, T0.AddDays(2));
        SeedRecord(db, session.SessionID, patient.PatientRefID, insA.InsuranceRefID, empA.EmploymentRefID, relA.RelativeRefID, T0.AddDays(3));

        var repo = new PatientRepository(db.Db);
        var snap = await repo.FindActiveProfileAsync(db.Ctx.DivisionId, patient.PatientRefID);

        Assert.NotNull(snap);
        Assert.Equal(insA.InsuranceRefID, snap.Insurance.InsuranceRefID);
        Assert.Equal("HT", snap.Insurance.InsuranceObjectOption.Code);
        Assert.Equal("79001", snap.Insurance.RegistrationPlaceOption.Code);
        Assert.Equal(empA.EmploymentRefID, snap.Employment.EmploymentRefID);
        Assert.Equal("GV", snap.Employment.OccupationOption.Code);
        Assert.Equal(relA.RelativeRefID, snap.Relative.RelativeRefID);
    }

    [Fact]
    public async Task Ho_so_kham_gan_nhat_khong_co_bhyt_thi_khong_muon_bhyt_cua_dot_cu()
    {
        using var db = new InMemoryTestDb();
        var patient = SeedPatient(db);
        var ins = SeedInsurance(db, patient.PatientRefID, "DN4790000001", null, null, T0);
        var rel = SeedRelative(db, patient.PatientRefID, "SPOUSE", "Trần Thị B", T0);
        var session = db.SeedSession();
        SeedRecord(db, session.SessionID, patient.PatientRefID, ins.InsuranceRefID, null, rel.RelativeRefID, T0.AddDays(1));
        SeedRecord(db, session.SessionID, patient.PatientRefID, null, null, rel.RelativeRefID, T0.AddDays(2));

        var repo = new PatientRepository(db.Db);
        var snap = await repo.FindActiveProfileAsync(db.Ctx.DivisionId, patient.PatientRefID);

        Assert.Null(snap.Insurance);
        Assert.Null(snap.Employment);
        Assert.Equal(rel.RelativeRefID, snap.Relative.RelativeRefID);
    }

    [Fact]
    public async Task Chua_co_ho_so_kham_thi_lay_dong_active_moi_nhat_theo_CreatedDate()
    {
        using var db = new InMemoryTestDb();
        var patient = SeedPatient(db);
        SeedInsurance(db, patient.PatientRefID, "DN4790000001", null, null, T0);
        var newer = SeedInsurance(db, patient.PatientRefID, "DN4790000002", null, null, T0.AddDays(1));
        var inactiveNewest = SeedInsurance(db, patient.PatientRefID, "DN4790000003", null, null, T0.AddDays(2), isActive: false);
        var rel = SeedRelative(db, patient.PatientRefID, "CHILD", "Nguyễn Văn C", T0);

        var repo = new PatientRepository(db.Db);
        var snap = await repo.FindActiveProfileAsync(db.Ctx.DivisionId, patient.PatientRefID);

        Assert.Equal(newer.InsuranceRefID, snap.Insurance.InsuranceRefID);
        Assert.NotEqual(inactiveNewest.InsuranceRefID, snap.Insurance.InsuranceRefID);
        Assert.Null(snap.Employment);
        Assert.Equal(rel.RelativeRefID, snap.Relative.RelativeRefID);
    }

    [Fact]
    public async Task Dong_con_cua_phien_ban_khac_khong_bi_lan_sang()
    {
        using var db = new InMemoryTestDb();
        var patient = SeedPatient(db);
        var other = SeedPatient(db);
        SeedInsurance(db, other.PatientRefID, "DN4790000009", null, null, T0.AddDays(5));

        var repo = new PatientRepository(db.Db);
        var snap = await repo.FindActiveProfileAsync(db.Ctx.DivisionId, patient.PatientRefID);

        Assert.NotNull(snap);
        Assert.Null(snap.Insurance);
    }

    [Fact]
    public async Task Profile_preserves_address_codes_after_reload()
    {
        using var db = new InMemoryTestDb();
        var p = SeedPatient(db);
        var tracked = await db.Db.Patients.AsTracking()
            .SingleAsync(x => x.PatientRefID == p.PatientRefID);
        tracked.ProvinceCode = "79";
        tracked.WardCode = "26734";
        await db.Db.SaveChangesAsync();
        db.Db.ChangeTracker.Clear();
        var result = await new PatientRepository(db.Db)
            .FindActiveProfileAsync(db.Ctx.DivisionId, p.PatientRefID);
        Assert.Equal("79", result.Patient.ProvinceCode);
        Assert.Equal("26734", result.Patient.WardCode);
    }

    [Fact]
    public async Task SearchActive_matches_substring_across_fields_in_real_db()
    {
        using var db = new InMemoryTestDb();
        var p = SeedPatient(db);
        var tracked = await db.Db.Patients.AsTracking().SingleAsync(x => x.PatientRefID == p.PatientRefID);
        tracked.PhoneNumber = "0985230227";
        tracked.IdentityNumber = "079201006605";
        tracked.PatientCode = "HEX-PHASE1-DEFAULT-0042";
        tracked.FullName = "Nguyễn Văn An";
        await db.Db.SaveChangesAsync();
        db.Db.ChangeTracker.Clear();

        var repo = new PatientRepository(db.Db);

        // Search by phone substring
        var phoneRes = await repo.SearchActiveAsync(db.Ctx.DivisionId,
            new PatientSearchCriteria("985", new[] { PatientSearchField.Phone }), 20);
        Assert.Equal(p.PatientRefID, Assert.Single(phoneRes).PatientRefID);

        // Explicit code should NOT match phone
        var codeRes = await repo.SearchActiveAsync(db.Ctx.DivisionId,
            new PatientSearchCriteria("985", new[] { PatientSearchField.Code }), 20);
        Assert.Empty(codeRes);

        // Auto mode searches all 4 fields
        var autoRes = await repo.SearchActiveAsync(db.Ctx.DivisionId,
            new PatientSearchCriteria("0042", new[]
            {
                PatientSearchField.Identity,
                PatientSearchField.Phone,
                PatientSearchField.Code,
                PatientSearchField.Name
            }), 20);
        Assert.Equal(p.PatientRefID, Assert.Single(autoRes).PatientRefID);
    }

    // ---- seed helpers: dựng thẳng entity, chỉ những cột InMemory cần ----

    private static Patient SeedPatient(
        InMemoryTestDb db,
        bool isActive = true,
        string divisionId = null,
        Guid? identityIssuerOptionID = null,
        Guid? ethnicityOptionID = null)
    {
        var id = Guid.NewGuid();
        var patient = new Patient
        {
            PatientRefID = id,
            DivisionID = divisionId ?? db.Ctx.DivisionId,
            FullName = "Nguyễn Văn An",
            IdentityNumber = "079123456789",
            GenderID = 1,
            IdentityIssuerOptionID = identityIssuerOptionID,
            EthnicityOptionID = ethnicityOptionID,
            IsActive = isActive,
            ProfileLineageID = id,
            CreatedDate = T0
        };
        db.Db.Patients.Add(patient);
        db.Db.SaveChanges();
        db.Db.ChangeTracker.Clear();
        return patient;
    }

    private static PatientInsurance SeedInsurance(
        InMemoryTestDb db, Guid patientRefID, string number, Guid? objectOptionID, Guid? placeOptionID,
        DateTime createdDate, bool isActive = true)
    {
        var row = new PatientInsurance
        {
            InsuranceRefID = Guid.NewGuid(),
            DivisionID = db.Ctx.DivisionId,
            PatientRefID = patientRefID,
            InsuranceNumber = number,
            InsuranceObjectOptionID = objectOptionID,
            RegistrationPlaceOptionID = placeOptionID,
            ValidFrom = new DateOnly(2026, 1, 1),
            ValidTo = new DateOnly(2026, 12, 31),
            IsActive = isActive,
            CreatedDate = createdDate
        };
        db.Db.PatientInsurances.Add(row);
        db.Db.SaveChanges();
        db.Db.ChangeTracker.Clear();
        return row;
    }

    private static PatientEmployment SeedEmployment(
        InMemoryTestDb db, Guid patientRefID, Guid? occupationOptionID, string staffCode, DateTime createdDate)
    {
        var row = new PatientEmployment
        {
            EmploymentRefID = Guid.NewGuid(),
            DivisionID = db.Ctx.DivisionId,
            PatientRefID = patientRefID,
            OccupationOptionID = occupationOptionID,
            StaffCode = staffCode,
            OrgDeptName = "Phòng Hành chính",
            JobTitle = "Chuyên viên",
            IsActive = true,
            CreatedDate = createdDate
        };
        db.Db.PatientEmployments.Add(row);
        db.Db.SaveChanges();
        db.Db.ChangeTracker.Clear();
        return row;
    }

    private static PatientRelative SeedRelative(
        InMemoryTestDb db, Guid patientRefID, string relationshipCode, string fullName, DateTime createdDate)
    {
        var row = new PatientRelative
        {
            RelativeRefID = Guid.NewGuid(),
            DivisionID = db.Ctx.DivisionId,
            PatientRefID = patientRefID,
            RelationshipCode = relationshipCode,
            FullName = fullName,
            IdentityNumber = "079000000001",
            PhoneNumber = "0912345678",
            IsActive = true,
            CreatedDate = createdDate
        };
        db.Db.PatientRelatives.Add(row);
        db.Db.SaveChanges();
        db.Db.ChangeTracker.Clear();
        return row;
    }

    private static void SeedRecord(
        InMemoryTestDb db, Guid sessionID, Guid patientRefID,
        Guid? insuranceRefID, Guid? employmentRefID, Guid? relativeRefID, DateTime createdDate)
    {
        db.Db.ExamRecords.Add(new ExamRecord
        {
            RecordID = Guid.NewGuid(),
            DivisionID = db.Ctx.DivisionId,
            SessionID = sessionID,
            RecordCode = "KSK-" + Guid.NewGuid().ToString("N")[..8],
            PatientRefID = patientRefID,
            InsuranceRefID = insuranceRefID,
            EmploymentRefID = employmentRefID,
            RelativeRefID = relativeRefID,
            VariantCode = "DTK_01",
            FormCode = ExamGroups.FormCodePrefix + "DTK_01",
            State = ExamRecordState.Completed,
            CreatedDate = createdDate
        });
        db.Db.SaveChanges();
        db.Db.ChangeTracker.Clear();
    }
}
