using System;
using HealthExam.Domain.Common;
using HealthExam.Domain.Patients;

namespace HealthExam.Application.Patients;

public sealed record PatientInsuranceWrite(
    string InsuranceNumber,
    Guid? InsuranceObjectOptionID,
    Guid? RegistrationPlaceOptionID,
    DateOnly? ValidFrom,
    DateOnly? ValidTo)
{
    public bool HasValue => !string.IsNullOrWhiteSpace(InsuranceNumber) ||
                            InsuranceObjectOptionID.HasValue ||
                            RegistrationPlaceOptionID.HasValue ||
                            ValidFrom.HasValue ||
                            ValidTo.HasValue;
}

public sealed record PatientEmploymentWrite(
    Guid? OccupationOptionID,
    string StaffCode,
    string OrgDeptName,
    string JobTitle)
{
    public bool HasValue => OccupationOptionID.HasValue ||
                            !string.IsNullOrWhiteSpace(StaffCode) ||
                            !string.IsNullOrWhiteSpace(OrgDeptName) ||
                            !string.IsNullOrWhiteSpace(JobTitle);
}

public sealed record PatientRelativeWrite(
    string RelationshipCode,
    Guid? RelationshipOptionID,
    string FullName,
    string IdentityNumber,
    string PhoneNumber)
{
    public bool HasValue => !string.IsNullOrWhiteSpace(RelationshipCode) ||
                            RelationshipOptionID.HasValue ||
                            !string.IsNullOrWhiteSpace(FullName) ||
                            !string.IsNullOrWhiteSpace(IdentityNumber) ||
                            !string.IsNullOrWhiteSpace(PhoneNumber);
}

public sealed record PatientWriteRequest(
    string DivisionId,
    long ActorId,
    ActorKind ActorKind,
    Guid? PatientRefID,
    long? HisPatientID,
    string PatientCode,
    string FullName,
    DateOnly? Dob,
    short? BirthYear,
    short GenderID,
    string IdentityNumber,
    DateOnly? IdentityIssuedDate,
    Guid? IdentityIssuerOptionID,
    string PhoneNumber,
    string Email,
    string Address,
    Guid? EthnicityOptionID,
    string BloodAboCode,
    string BloodRhCode,
    PatientInsuranceWrite Insurance,
    PatientEmploymentWrite Employment,
    PatientRelativeWrite Relative,
    bool? SetAsActiveProfile = null,
    string ProvinceCode = "",
    string WardCode = "");

public sealed record PatientProfileValues(
    string FullName,
    DateOnly? Dob,
    short? BirthYear,
    short GenderID,
    string IdentityNumber,
    DateOnly? IdentityIssuedDate,
    Guid? IdentityIssuerOptionID,
    string PhoneNumber,
    string Email,
    string Address,
    Guid? EthnicityOptionID,
    string BloodAboCode,
    string BloodRhCode,
    string ProvinceCode = "",
    string WardCode = "")
{
    public static PatientProfileValues From(PatientWriteRequest request) => new(
        request.FullName,
        request.Dob,
        request.BirthYear,
        request.GenderID,
        request.IdentityNumber,
        request.IdentityIssuedDate,
        request.IdentityIssuerOptionID,
        request.PhoneNumber,
        request.Email,
        request.Address,
        request.EthnicityOptionID,
        request.BloodAboCode,
        request.BloodRhCode,
        request.ProvinceCode,
        request.WardCode);
}

public sealed record PatientProfileChangedPayload(
    IReadOnlyList<string> ChangedFields);

public sealed record PatientWriteResult(
    Guid PatientRefID,
    long? HisPatientID,
    string PatientCode,
    Guid? InsuranceRefID,
    Guid? EmploymentRefID,
    Guid? RelativeRefID,
    Patient Patient,
    PatientInsurance Insurance,
    PatientEmployment Employment,
    PatientRelative Relative);

/// <summary>
/// Hồ sơ người bệnh active kèm ba dòng con ĐÃ CHỌN để điền form: BHYT / nghề nghiệp / thân nhân
/// như lần đăng ký gần nhất của phiên bản này. Từng dòng con có thể null.
/// </summary>
public sealed record PatientProfileSnapshot(
    Patient Patient,
    PatientInsurance Insurance,
    PatientEmployment Employment,
    PatientRelative Relative);
