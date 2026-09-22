using System;
using System.Linq;
using System.Threading.Tasks;
using HealthExam.Application.Common;
using HealthExam.Application.Patients;
using HealthExam.Domain.Patients;
using Xunit;

namespace HealthExam.Tests.Application;

public class SearchPatientsTests
{
    private static Patient CreatePatient(
        string divisionId,
        string fullName = "Nguyễn Văn An",
        string identityNumber = "079123456789",
        string phoneNumber = "0901234567",
        string patientCode = "BN000125001",
        bool isActive = true,
        short? birthYear = 1990,
        DateOnly? dob = null)
    {
        return new Patient
        {
            PatientRefID = Guid.NewGuid(),
            DivisionID = divisionId,
            FullName = fullName,
            Dob = dob ?? new DateOnly(1990, 5, 20),
            BirthYear = birthYear,
            GenderID = 1,
            IdentityNumber = identityNumber,
            PhoneNumber = phoneNumber,
            PatientCode = patientCode,
            IsActive = isActive
        };
    }

    private static SearchPatientsHandler CreateHandler(params Patient[] patients)
        => new(new FakePatientRepository(patients));

    [Theory]
    [InlineData("code", "PHASE1")]
    [InlineData("phone", "985")]
    [InlineData("identity", "201")]
    [InlineData("auto", "0042")]
    [InlineData("auto", "985")]
    [InlineData("auto", "201")]
    public async Task Search_matches_middle_of_selected_fields(string type, string keyword)
    {
        var patient = CreatePatient("D01", patientCode: "HEX-PHASE1-DEFAULT-0042",
            phoneNumber: "0985230227", identityNumber: "079201006605");
        var result = await CreateHandler(patient).HandleAsync(
            new SearchPatientsQuery("D01", type, keyword));
        Assert.True(result.IsSuccess);
        Assert.Equal(patient.PatientRefID, Assert.Single(result.Value).PatientRefID);
    }

    [Fact]
    public async Task Identity_mode_returns_summary_of_active_patient_in_division()
    {
        var wanted = CreatePatient("D01");
        var sut = CreateHandler(
            wanted,
            CreatePatient("D01", isActive: false),
            CreatePatient("D02"));

        var result = await sut.HandleAsync(new SearchPatientsQuery("D01", "identity", " 079123456789 "));

        Assert.True(result.IsSuccess);
        var item = Assert.Single(result.Value);
        Assert.Equal(wanted.PatientRefID, item.PatientRefID);
        Assert.Equal("BN000125001", item.PatientCode);
        Assert.Equal("Nguyễn Văn An", item.FullName);
        Assert.Equal((short)1990, item.BirthYear);
        Assert.Equal((short)1, item.GenderID);
        Assert.Equal("079123456789", item.IdentityNumber);
    }

    [Fact]
    public async Task Name_mode_matches_contains_case_insensitive()
    {
        var sut = CreateHandler(
            CreatePatient("D01", fullName: "Nguyễn Văn An"),
            CreatePatient("D01", fullName: "Lê Thị Bình", identityNumber: "079000000001"));

        var result = await sut.HandleAsync(new SearchPatientsQuery("D01", "name", "văn an"));

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value);
        Assert.Equal("Nguyễn Văn An", result.Value[0].FullName);
    }

    [Fact]
    public async Task Phone_and_code_modes_match_substring()
    {
        var sut = CreateHandler(
            CreatePatient("D01", phoneNumber: "0901234567", patientCode: "HEX-KSK001"),
            CreatePatient("D01", phoneNumber: "0987654321", patientCode: "BN000125002", identityNumber: "079000000002"));

        var byPhone = await sut.HandleAsync(new SearchPatientsQuery("D01", "phone", "1234"));
        var byCode = await sut.HandleAsync(new SearchPatientsQuery("D01", "code", "ksk"));

        Assert.Single(byPhone.Value);
        Assert.Equal("HEX-KSK001", byPhone.Value[0].PatientCode);
        Assert.Single(byCode.Value);
        Assert.Equal("HEX-KSK001", byCode.Value[0].PatientCode);
    }

    [Fact]
    public async Task Auto_mode_with_letters_searches_name_and_code()
    {
        var sut = CreateHandler(
            CreatePatient("D01", fullName: "Nguyễn Văn An", patientCode: "BN1"),
            CreatePatient("D01", fullName: "Lê Thị Bình", patientCode: "AN-002", identityNumber: "079000000003"));

        var result = await sut.HandleAsync(new SearchPatientsQuery("D01", null, "an"));

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Count);
    }

    [Fact]
    public async Task Auto_mode_searches_across_identity_phone_code_and_name()
    {
        var sut = CreateHandler(
            CreatePatient("D01", identityNumber: "079123456789", phoneNumber: "0909999999", patientCode: "P1", fullName: "A"),
            CreatePatient("D01", identityNumber: "079000000000", phoneNumber: "0981234567", patientCode: "P2", fullName: "B"),
            CreatePatient("D01", identityNumber: "079000000001", phoneNumber: "0909999998", patientCode: "HEX-1234-X", fullName: "C"));

        var result = await sut.HandleAsync(new SearchPatientsQuery("D01", "auto", "1234"));

        Assert.Equal(3, result.Value.Count);
    }

    [Fact]
    public async Task BirthYear_falls_back_to_dob_year_when_null()
    {
        var sut = CreateHandler(CreatePatient("D01", birthYear: null, dob: new DateOnly(1985, 3, 2)));

        var result = await sut.HandleAsync(new SearchPatientsQuery("D01", "identity", "079123456789"));

        Assert.Equal((short)1985, result.Value[0].BirthYear);
    }

    [Fact]
    public async Task Results_are_capped_at_max_results()
    {
        var patients = Enumerable.Range(0, SearchPatientsHandler.MaxResults + 5)
            .Select(i => CreatePatient("D01", fullName: $"Nguyễn Văn An {i:00}", identityNumber: $"0790000000{i:00}"))
            .ToArray();
        var sut = CreateHandler(patients);

        var result = await sut.HandleAsync(new SearchPatientsQuery("D01", "name", "nguyễn"));

        Assert.Equal(SearchPatientsHandler.MaxResults, result.Value.Count);
    }

    [Fact]
    public async Task No_match_returns_empty_list_not_failure()
    {
        var sut = CreateHandler(CreatePatient("D01"));

        var result = await sut.HandleAsync(new SearchPatientsQuery("D01", "identity", "999"));

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Blank_keyword_returns_latest_active_patients(string keyword)
    {
        var older = CreatePatient("D01", identityNumber: "079000000001");
        older.ModifiedDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var newer = CreatePatient("D01", identityNumber: "079000000002");
        newer.ModifiedDate = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc);
        var inactive = CreatePatient("D01", identityNumber: "079000000003", isActive: false);
        inactive.ModifiedDate = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc);
        var sut = CreateHandler(older, newer, inactive, CreatePatient("D02"));

        var result = await sut.HandleAsync(new SearchPatientsQuery("D01", "auto", keyword));

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Count);
        Assert.Equal(newer.PatientRefID, result.Value[0].PatientRefID);
        Assert.Equal(older.PatientRefID, result.Value[1].PatientRefID);
    }

    [Fact]
    public async Task Blank_keyword_is_capped_at_max_results()
    {
        var patients = Enumerable.Range(0, SearchPatientsHandler.MaxResults + 5)
            .Select(i => CreatePatient("D01", identityNumber: $"0790000000{i:00}"))
            .ToArray();
        var sut = CreateHandler(patients);

        var result = await sut.HandleAsync(new SearchPatientsQuery("D01", null, null));

        Assert.Equal(SearchPatientsHandler.MaxResults, result.Value.Count);
    }

    [Fact]
    public async Task Keyword_results_are_sorted_by_modified_date_descending()
    {
        var older = CreatePatient("D01", fullName: "Nguyễn Văn An", identityNumber: "079000000001");
        older.ModifiedDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var newer = CreatePatient("D01", fullName: "Nguyễn Văn Bình", identityNumber: "079000000002");
        newer.ModifiedDate = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc);
        var sut = CreateHandler(older, newer);

        var result = await sut.HandleAsync(new SearchPatientsQuery("D01", "name", "nguyễn"));

        Assert.Equal(newer.PatientRefID, result.Value[0].PatientRefID);
        Assert.Equal(older.PatientRefID, result.Value[1].PatientRefID);
    }

    [Fact]
    public async Task Unknown_type_is_bad_request()
    {
        var sut = CreateHandler();

        var result = await sut.HandleAsync(new SearchPatientsQuery("D01", "email", "an"));

        Assert.False(result.IsSuccess);
        Assert.Equal(ApplicationFailureCode.BadRequest, result.Failure.Code);
    }
}
