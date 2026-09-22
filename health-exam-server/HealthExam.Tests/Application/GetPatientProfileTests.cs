using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using HealthExam.Application.Common;
using HealthExam.Application.Patients;
using HealthExam.Domain.Catalogs;
using HealthExam.Domain.Patients;
using Xunit;

namespace HealthExam.Tests.Application;

public class GetPatientProfileTests
{
    private static Patient CreatePatient(string divisionId, bool isActive = true) => new()
    {
        PatientRefID = Guid.NewGuid(),
        DivisionID = divisionId,
        HisPatientID = 125001,
        PatientCode = "BN000125001",
        FullName = "Nguyễn Văn An",
        Dob = new DateOnly(1990, 5, 20),
        BirthYear = 1990,
        GenderID = 1,
        IdentityNumber = "079123456789",
        IdentityIssuedDate = new DateOnly(2021, 6, 15),
        PhoneNumber = "0901234567",
        Email = "an@example.com",
        Address = "12 Nguyễn Huệ",
        ProvinceCode = "79",
        WardCode = "26734",
        BloodAboCode = "A",
        BloodRhCode = "+",
        IsActive = isActive
    };

    [Fact]
    public async Task Returns_full_profile_of_active_patient_in_division()
    {
        var patient = CreatePatient("D01");
        var sut = new GetPatientProfileHandler(new FakePatientRepository(patient));

        var result = await sut.HandleAsync(new GetPatientProfileQuery("D01", patient.PatientRefID));

        Assert.True(result.IsSuccess);
        var profile = result.Value;
        Assert.Equal(patient.PatientRefID, profile.PatientRefID);
        Assert.Equal("D01", profile.DivisionID);
        Assert.Equal(125001, profile.HisPatientID);
        Assert.Equal("BN000125001", profile.PatientCode);
        Assert.Equal("Nguyễn Văn An", profile.FullName);
        Assert.Equal(new DateOnly(1990, 5, 20), profile.Dob);
        Assert.Equal((short)1990, profile.BirthYear);
        Assert.Equal((short)1, profile.GenderID);
        Assert.Equal("079123456789", profile.IdentityNumber);
        Assert.Equal(new DateOnly(2021, 6, 15), profile.IdentityIssuedDate);
        Assert.Equal("0901234567", profile.PhoneNumber);
        Assert.Equal("an@example.com", profile.Email);
        Assert.Equal("12 Nguyễn Huệ", profile.Address);
        Assert.Equal("79", profile.ProvinceCode);
        Assert.Equal("26734", profile.WardCode);
        Assert.Equal("A", profile.BloodAboCode);
        Assert.Equal("+", profile.BloodRhCode);
        Assert.True(profile.IsActive);
    }

    [Fact]
    public async Task Profile_carries_option_codes_insurance_employment_and_relative()
    {
        var patient = CreatePatient("D01");
        patient.IdentityIssuerOptionID = Guid.NewGuid();
        patient.IdentityIssuerOption = new MasterDataOption { OptionID = patient.IdentityIssuerOptionID.Value, Code = "CCS", Name = "Cục Cảnh sát" };
        patient.EthnicityOptionID = Guid.NewGuid();
        patient.EthnicityOption = new MasterDataOption { OptionID = patient.EthnicityOptionID.Value, Code = "KINH", Name = "Kinh" };

        var repo = new FakePatientRepository(patient);
        repo.Insurances.Add(new PatientInsurance
        {
            InsuranceRefID = Guid.NewGuid(),
            DivisionID = "D01",
            PatientRefID = patient.PatientRefID,
            InsuranceNumber = "DN4790000001",
            InsuranceObjectOptionID = Guid.NewGuid(),
            InsuranceObjectOption = new MasterDataOption { Code = "HT", Name = "Hưu trí" },
            RegistrationPlaceOptionID = Guid.NewGuid(),
            RegistrationPlaceOption = new MasterDataOption { Code = "79001", Name = "BV Quận 1" },
            ValidFrom = new DateOnly(2026, 1, 1),
            ValidTo = new DateOnly(2026, 12, 31),
            IsActive = true
        });
        repo.Employments.Add(new PatientEmployment
        {
            EmploymentRefID = Guid.NewGuid(),
            DivisionID = "D01",
            PatientRefID = patient.PatientRefID,
            OccupationOptionID = Guid.NewGuid(),
            OccupationOption = new MasterDataOption { Code = "GV", Name = "Giáo viên" },
            StaffCode = "NV01",
            OrgDeptName = "Phòng Hành chính",
            JobTitle = "Chuyên viên",
            IsActive = true
        });
        repo.Relatives.Add(new PatientRelative
        {
            RelativeRefID = Guid.NewGuid(),
            DivisionID = "D01",
            PatientRefID = patient.PatientRefID,
            RelationshipCode = "SPOUSE",
            FullName = "Trần Thị B",
            IdentityNumber = "079000000001",
            PhoneNumber = "0912345678",
            IsActive = true
        });
        var sut = new GetPatientProfileHandler(repo);

        var result = await sut.HandleAsync(new GetPatientProfileQuery("D01", patient.PatientRefID));

        Assert.True(result.IsSuccess);
        var p = result.Value;
        Assert.Equal("CCS", p.IdentityIssuerCode);
        Assert.Equal("Cục Cảnh sát", p.IdentityIssuerName);
        Assert.Equal("KINH", p.EthnicityCode);
        Assert.Equal("Kinh", p.EthnicityName);

        Assert.Equal(repo.Insurances[0].InsuranceRefID, p.InsuranceRefID);
        Assert.Equal("DN4790000001", p.InsuranceNumber);
        Assert.Equal("HT", p.InsuranceObjectCode);
        Assert.Equal("Hưu trí", p.InsuranceObjectName);
        Assert.Equal(new DateOnly(2026, 1, 1), p.InsuranceValidFrom);
        Assert.Equal(new DateOnly(2026, 12, 31), p.InsuranceValidTo);
        Assert.Equal("79001", p.RegistrationPlaceCode);
        Assert.Equal("BV Quận 1", p.RegistrationPlaceName);

        Assert.Equal(repo.Employments[0].EmploymentRefID, p.EmploymentRefID);
        Assert.Equal("GV", p.OccupationCode);
        Assert.Equal("Giáo viên", p.OccupationName);
        Assert.Equal("NV01", p.StaffCode);
        Assert.Equal("Phòng Hành chính", p.OrgDeptName);
        Assert.Equal("Chuyên viên", p.JobTitle);

        Assert.Equal(repo.Relatives[0].RelativeRefID, p.RelativeRefID);
        Assert.Equal("SPOUSE", p.RelativeRelationshipCode);
        Assert.Equal("Vợ-chồng", p.RelativeRelationshipName);
        Assert.Equal("Trần Thị B", p.RelativeFullName);
        Assert.Equal("079000000001", p.RelativeIdentityNumber);
        Assert.Equal("0912345678", p.RelativePhoneNumber);
    }

    [Fact]
    public async Task Profile_without_child_rows_returns_empty_strings_and_null_refs()
    {
        var patient = CreatePatient("D01");
        var sut = new GetPatientProfileHandler(new FakePatientRepository(patient));

        var result = await sut.HandleAsync(new GetPatientProfileQuery("D01", patient.PatientRefID));

        Assert.True(result.IsSuccess);
        var p = result.Value;
        Assert.Equal("", p.IdentityIssuerCode);
        Assert.Equal("", p.EthnicityCode);
        Assert.Null(p.InsuranceRefID);
        Assert.Equal("", p.InsuranceNumber);
        Assert.Equal("", p.InsuranceObjectCode);
        Assert.Null(p.InsuranceValidFrom);
        Assert.Equal("", p.RegistrationPlaceCode);
        Assert.Null(p.EmploymentRefID);
        Assert.Equal("", p.OccupationCode);
        Assert.Equal("", p.StaffCode);
        Assert.Null(p.RelativeRefID);
        Assert.Equal("", p.RelativeRelationshipCode);
        Assert.Equal("", p.RelativeRelationshipName);
        Assert.Equal("", p.RelativeFullName);
    }

    [Fact]
    public async Task Inactive_version_is_not_found()
    {
        var patient = CreatePatient("D01", isActive: false);
        var sut = new GetPatientProfileHandler(new FakePatientRepository(patient));

        var result = await sut.HandleAsync(new GetPatientProfileQuery("D01", patient.PatientRefID));

        Assert.False(result.IsSuccess);
        Assert.Equal(ApplicationFailureCode.NotFound, result.Failure.Code);
    }

    [Fact]
    public async Task Other_division_is_not_found()
    {
        var patient = CreatePatient("D02");
        var sut = new GetPatientProfileHandler(new FakePatientRepository(patient));

        var result = await sut.HandleAsync(new GetPatientProfileQuery("D01", patient.PatientRefID));

        Assert.False(result.IsSuccess);
        Assert.Equal(ApplicationFailureCode.NotFound, result.Failure.Code);
    }

    [Fact]
    public async Task Empty_guid_is_bad_request()
    {
        var sut = new GetPatientProfileHandler(new FakePatientRepository());

        var result = await sut.HandleAsync(new GetPatientProfileQuery("D01", Guid.Empty));

        Assert.False(result.IsSuccess);
        Assert.Equal(ApplicationFailureCode.BadRequest, result.Failure.Code);
    }
}
