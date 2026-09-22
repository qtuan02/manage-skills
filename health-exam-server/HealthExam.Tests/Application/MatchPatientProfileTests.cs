using System;
using System.Threading.Tasks;
using HealthExam.Application.Common;
using HealthExam.Application.Patients;
using HealthExam.Domain.Patients;
using Xunit;

namespace HealthExam.Tests.Application;

public class MatchPatientProfileTests
{
    private static Patient CreatePatient(string divisionId = "D01", bool isActive = true) => new()
    {
        PatientRefID = Guid.NewGuid(),
        DivisionID = divisionId,
        FullName = "Nguyễn   Văn An",
        Dob = new DateOnly(1990, 5, 20),
        BirthYear = 1990,
        GenderID = 1,
        IdentityNumber = "079123456789",
        IdentityIssuedDate = new DateOnly(2021, 6, 15),
        IdentityIssuerOptionID = Guid.Parse("11111111-1111-1111-1111-111111111111"),
        PhoneNumber = "0901234567",
        Email = "AN@EXAMPLE.COM",
        Address = "12 Nguyễn Huệ",
        EthnicityOptionID = Guid.Parse("22222222-2222-2222-2222-222222222222"),
        BloodAboCode = "a",
        BloodRhCode = "+",
        IsActive = isActive
    };

    private static PatientProfileValues Values(Patient patient, string address = null) => new(
        FullName: " nguyễn văn an ",
        Dob: patient.Dob,
        BirthYear: patient.BirthYear,
        GenderID: patient.GenderID,
        IdentityNumber: " 079123456789 ",
        IdentityIssuedDate: patient.IdentityIssuedDate,
        IdentityIssuerOptionID: patient.IdentityIssuerOptionID,
        PhoneNumber: " 0901234567 ",
        Email: " an@example.com ",
        Address: address ?? patient.Address,
        EthnicityOptionID: patient.EthnicityOptionID,
        BloodAboCode: " A ",
        BloodRhCode: " + ");

    [Fact]
    public async Task All_profile_fields_match_using_registration_normalization()
    {
        var patient = CreatePatient();
        var sut = new MatchPatientProfileHandler(new FakePatientRepository(patient));

        var result = await sut.HandleAsync(
            new MatchPatientProfileQuery("D01", Values(patient)));

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.Matched);
        Assert.Equal(patient.PatientRefID, result.Value.PatientRefID);
        Assert.Empty(result.Value.ChangedFields);
    }

    [Fact]
    public async Task Different_profile_field_returns_success_with_changed_field()
    {
        var patient = CreatePatient();
        var sut = new MatchPatientProfileHandler(new FakePatientRepository(patient));

        var result = await sut.HandleAsync(
            new MatchPatientProfileQuery("D01", Values(patient, "34 Lê Lợi")));

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.Matched);
        Assert.Equal(new[] { nameof(Patient.Address) }, result.Value.ChangedFields);
    }

    [Theory]
    [InlineData("D02", true)]
    [InlineData("D01", false)]
    public async Task Inactive_or_other_division_profile_returns_no_match(string divisionId, bool isActive)
    {
        var patient = CreatePatient(divisionId, isActive);
        var sut = new MatchPatientProfileHandler(new FakePatientRepository(patient));

        var result = await sut.HandleAsync(
            new MatchPatientProfileQuery("D01", Values(patient)));

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value.PatientRefID);
        Assert.False(result.Value.Matched);
        Assert.Empty(result.Value.ChangedFields);
    }

    [Fact]
    public async Task Empty_identity_number_is_bad_request()
    {
        var patient = CreatePatient();
        var sut = new MatchPatientProfileHandler(new FakePatientRepository(patient));

        var result = await sut.HandleAsync(
            new MatchPatientProfileQuery("D01", Values(patient) with { IdentityNumber = " " }));

        Assert.False(result.IsSuccess);
        Assert.Equal(ApplicationFailureCode.BadRequest, result.Failure.Code);
    }
}
