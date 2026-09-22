using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using HealthExam.Application.Common;
using HealthExam.Application.Patients;
using HealthExam.Domain.Common;
using HealthExam.Domain.Patients;
using Xunit;

namespace HealthExam.Tests.Application;

public class PatientRegistrationWriterTests
{
    private static readonly DateTime FixedNow = new(2026, 9, 16, 10, 0, 0, DateTimeKind.Utc);

    private static Patient ActivePatient(
        string divisionId = "D01",
        string identityNumber = "079123456789",
        string fullName = "Nguyễn Văn An",
        DateOnly? dob = null,
        string phone = "0901234567")
    {
        return new Patient
        {
            PatientRefID = Guid.NewGuid(),
            DivisionID = divisionId,
            FullName = fullName,
            Dob = dob ?? new DateOnly(1990, 1, 1),
            BirthYear = 1990,
            GenderID = 1,
            IdentityNumber = identityNumber,
            PhoneNumber = phone,
            IsActive = true,
            CreatedDate = FixedNow.AddDays(-10),
            ModifiedDate = FixedNow.AddDays(-10)
        };
    }

    private static PatientWriteRequest Request(
        Guid? patientRefId,
        string divisionId = "D01",
        string identityNumber = "079123456789",
        string fullName = "Nguyễn Văn An",
        DateOnly? dob = null,
        string phone = "0901234567",
        bool? setActive = null,
        string provinceCode = "",
        string wardCode = "",
        Guid? ethnicityOptionId = null,
        Guid? identityIssuerOptionId = null)
    {
        return new PatientWriteRequest(
            DivisionId: divisionId,
            ActorId: 100,
            ActorKind: ActorKind.Employee,
            PatientRefID: patientRefId,
            HisPatientID: null,
            PatientCode: "",
            FullName: fullName,
            Dob: dob ?? new DateOnly(1990, 1, 1),
            BirthYear: 1990,
            GenderID: 1,
            IdentityNumber: identityNumber,
            IdentityIssuedDate: null,
            IdentityIssuerOptionID: identityIssuerOptionId,
            PhoneNumber: phone,
            Email: "",
            Address: "",
            EthnicityOptionID: ethnicityOptionId,
            BloodAboCode: "",
            BloodRhCode: "",
            Insurance: null,
            Employment: null,
            Relative: null,
            SetAsActiveProfile: setActive,
            ProvinceCode: provinceCode,
            WardCode: wardCode);
    }

    private static (PatientRegistrationWriter Sut, FakePatientRepository Repo) CreateWriterWithRepository(params Patient[] initial)
    {
        var repo = new FakePatientRepository(initial);
        var uow = new FakeUnitOfWork();
        var clock = new FixedClock(FixedNow);
        var sut = new PatientRegistrationWriter(repo, uow, clock);
        return (sut, repo);
    }

    private static PatientRegistrationWriter CreateWriter(params Patient[] initial)
        => CreateWriterWithRepository(initial).Sut;

    [Fact]
    public async Task Changed_profile_without_decision_returns_changed_fields()
    {
        var active = ActivePatient(fullName: "Nguyễn  Văn An");
        var sut = CreateWriter(active);
        var result = await sut.UpsertAsync(Request(active.PatientRefID,
            fullName: "nguyễn văn an", phone: "0912000000", setActive: null));

        Assert.False(result.IsSuccess);
        Assert.Equal(ApplicationFailureCode.PatientProfileChanged, result.Failure.Code);
        var payload = Assert.IsType<PatientProfileChangedPayload>(result.Failure.Payload);
        Assert.Equal(new[] { nameof(Patient.PhoneNumber) }, payload.ChangedFields);
    }

    [Fact]
    public async Task Changed_profile_declined_creates_inactive_version()
    {
        var active = ActivePatient();
        var (sut, repo) = CreateWriterWithRepository(active);
        var result = await sut.UpsertAsync(Request(active.PatientRefID,
            phone: "0912000000", setActive: false));

        Assert.True(result.IsSuccess);
        Assert.True(active.IsActive);
        Assert.False(result.Value.Patient.IsActive);
        Assert.Equal(active.PatientRefID, result.Value.Patient.PreviousPatientRefID);
        Assert.Equal(2, repo.Patients.Count);
    }

    [Fact]
    public async Task Changed_profile_accepted_replaces_active_version()
    {
        var active = ActivePatient();
        var (sut, repo) = CreateWriterWithRepository(active);
        var result = await sut.UpsertAsync(Request(active.PatientRefID,
            phone: "0912000000", setActive: true));

        Assert.True(result.IsSuccess);
        Assert.False(active.IsActive);
        Assert.True(result.Value.Patient.IsActive);
        Assert.NotEqual(active.PatientRefID, result.Value.PatientRefID);
    }

    [Fact]
    public async Task Unchanged_profile_reuses_existing_patient_id()
    {
        var active = ActivePatient(fullName: "Nguyễn   Văn   An ");
        var (sut, repo) = CreateWriterWithRepository(active);
        var result = await sut.UpsertAsync(Request(active.PatientRefID,
            fullName: "  nguyễn  văn an  ", setActive: null));

        Assert.True(result.IsSuccess);
        Assert.Equal(active.PatientRefID, result.Value.PatientRefID);
        Assert.Single(repo.Patients);
    }

    [Fact]
    public async Task New_registration_without_source_creates_active_patient()
    {
        var (sut, repo) = CreateWriterWithRepository();
        var result = await sut.UpsertAsync(Request(null, phone: "0988776655"));

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.Patient.IsActive);
        Assert.Null(result.Value.Patient.PreviousPatientRefID);
        Assert.Equal(result.Value.Patient.PatientRefID, result.Value.Patient.ProfileLineageID);
        Assert.Equal(1, result.Value.Patient.VersionNumber);
        Assert.Single(repo.Patients);
    }

    [Fact]
    public async Task Inactive_forks_receive_unique_monotonic_versions()
    {
        var active = ActivePatient();
        active.ProfileLineageID = active.PatientRefID;
        active.VersionNumber = 1;
        var (sut, repo) = CreateWriterWithRepository(active);

        var second = await sut.UpsertAsync(Request(active.PatientRefID,
            phone: "0912000000", setActive: false));
        var third = await sut.UpsertAsync(Request(active.PatientRefID,
            phone: "0923000000", setActive: false));

        Assert.True(second.IsSuccess);
        Assert.True(third.IsSuccess);
        Assert.Equal(active.ProfileLineageID, second.Value.Patient.ProfileLineageID);
        Assert.Equal(active.ProfileLineageID, third.Value.Patient.ProfileLineageID);
        Assert.Equal(2, second.Value.Patient.VersionNumber);
        Assert.Equal(3, third.Value.Patient.VersionNumber);
        Assert.Equal(3, repo.Patients.Count);
    }

    [Fact]
    public async Task Inactive_or_not_found_source_returns_invalid_state()
    {
        var inactive = ActivePatient();
        inactive.IsActive = false;
        var sut = CreateWriter(inactive);

        var result = await sut.UpsertAsync(Request(inactive.PatientRefID, setActive: true));

        Assert.False(result.IsSuccess);
        Assert.Equal(ApplicationFailureCode.InvalidState, result.Failure.Code);
    }

    [Fact]
    public async Task Vietnamese_diacritics_difference_is_detected_as_changed()
    {
        var active = ActivePatient(fullName: "Nguyễn Văn An");
        var sut = CreateWriter(active);

        // "Nguyễn Văn An" vs "Nguyen Van An" (bỏ dấu) -> coi là có thay đổi
        var result = await sut.UpsertAsync(Request(active.PatientRefID,
            fullName: "Nguyen Van An", setActive: null));

        Assert.False(result.IsSuccess);
        Assert.Equal(ApplicationFailureCode.PatientProfileChanged, result.Failure.Code);
        var payload = Assert.IsType<PatientProfileChangedPayload>(result.Failure.Payload);
        Assert.Contains(nameof(Patient.FullName), payload.ChangedFields);
    }

    [Fact]
    public void Address_codes_change_without_false_catalog_changes()
    {
        var p = ActivePatient();
        p.ProvinceCode = "79";
        p.WardCode = "26734";
        p.EthnicityOptionID = Guid.NewGuid();
        p.IdentityIssuerOptionID = Guid.NewGuid();
        var request = Request(p.PatientRefID) with
        {
            ProvinceCode = "01",
            WardCode = "00001",
            EthnicityOptionID = p.EthnicityOptionID,
            IdentityIssuerOptionID = p.IdentityIssuerOptionID
        };
        Assert.Equal(new[] { "ProvinceCode", "WardCode" },
            PatientProfileComparer.Compare(p, PatientProfileValues.From(request)));
    }

    [Fact]
    public async Task Upsert_with_address_code_changes_without_decision_returns_PatientProfileChanged()
    {
        var p = ActivePatient();
        p.ProvinceCode = "79";
        p.WardCode = "26734";
        var sut = CreateWriter(p);
        var result = await sut.UpsertAsync(Request(p.PatientRefID) with
        {
            ProvinceCode = "01",
            WardCode = "00001"
        });

        Assert.False(result.IsSuccess);
        Assert.Equal(ApplicationFailureCode.PatientProfileChanged, result.Failure.Code);
        var payload = Assert.IsType<PatientProfileChangedPayload>(result.Failure.Payload);
        Assert.Equal(new[] { "ProvinceCode", "WardCode" }, payload.ChangedFields);
        Assert.Equal("79", p.ProvinceCode);
        Assert.Equal("26734", p.WardCode);
    }

    [Fact]
    public void Address_codes_unchanged_or_whitespace_equal_returns_empty_changes()
    {
        var p = ActivePatient();
        p.ProvinceCode = "79";
        p.WardCode = "26734";
        var request = Request(p.PatientRefID) with
        {
            ProvinceCode = " 79 ",
            WardCode = "26734"
        };
        Assert.Empty(PatientProfileComparer.Compare(p, PatientProfileValues.From(request)));
    }

    [Fact]
    public void Only_province_code_change_returns_only_ProvinceCode()
    {
        var p = ActivePatient();
        p.ProvinceCode = "79";
        p.WardCode = "26734";
        var request = Request(p.PatientRefID) with
        {
            ProvinceCode = "01",
            WardCode = "26734"
        };
        Assert.Equal(new[] { "ProvinceCode" },
            PatientProfileComparer.Compare(p, PatientProfileValues.From(request)));
    }

    [Fact]
    public async Task Upsert_with_address_change_decision_true_creates_active_version_and_preserves_source()
    {
        var p = ActivePatient();
        p.ProfileLineageID = p.PatientRefID;
        p.VersionNumber = 1;
        p.ProvinceCode = "79";
        p.WardCode = "26734";
        var (sut, repo) = CreateWriterWithRepository(p);

        var result = await sut.UpsertAsync(Request(p.PatientRefID, setActive: true) with
        {
            ProvinceCode = "01",
            WardCode = "00001"
        });

        Assert.True(result.IsSuccess);
        Assert.False(p.IsActive);
        Assert.Equal("79", p.ProvinceCode);
        Assert.Equal("26734", p.WardCode);

        var newVer = result.Value.Patient;
        Assert.True(newVer.IsActive);
        Assert.Equal("01", newVer.ProvinceCode);
        Assert.Equal("00001", newVer.WardCode);
        Assert.Equal(2, newVer.VersionNumber);
        Assert.Equal(p.ProfileLineageID, newVer.ProfileLineageID);
    }
}
