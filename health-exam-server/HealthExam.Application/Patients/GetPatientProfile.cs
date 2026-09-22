using System;
using System.Threading;
using System.Threading.Tasks;
using HealthExam.Application.Common;
using HealthExam.Domain.Patients;

namespace HealthExam.Application.Patients;

public sealed record GetPatientProfileQuery(
    string DivisionId,
    Guid PatientRefID);

/// <summary>
/// Hồ sơ cá nhân đầy đủ để frontend điền vào form đăng ký khám. 18 field đầu giữ nguyên;
/// các field từ IdentityIssuerCode trở đi TRÙNG TÊN với ExamRecordResult để FE dùng lại
/// cùng một mapping (form đọc Mã, không đọc OptionID). Dòng BHYT / nghề nghiệp / thân nhân
/// là dòng của lần đăng ký gần nhất (xem IPatientRepository.FindActiveProfileAsync).
/// </summary>
public sealed record PatientProfileResult(
    Guid PatientRefID,
    string DivisionID,
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
    string ProvinceCode,
    string WardCode,
    Guid? EthnicityOptionID,
    string BloodAboCode,
    string BloodRhCode,
    bool IsActive,
    string IdentityIssuerCode,
    string IdentityIssuerName,
    string EthnicityCode,
    string EthnicityName,
    Guid? InsuranceRefID,
    string InsuranceNumber,
    string InsuranceObjectCode,
    string InsuranceObjectName,
    DateOnly? InsuranceValidFrom,
    DateOnly? InsuranceValidTo,
    string RegistrationPlaceCode,
    string RegistrationPlaceName,
    Guid? EmploymentRefID,
    string OccupationCode,
    string OccupationName,
    string StaffCode,
    string OrgDeptName,
    string JobTitle,
    Guid? RelativeRefID,
    string RelativeRelationshipCode,
    string RelativeRelationshipName,
    string RelativeFullName,
    string RelativeIdentityNumber,
    string RelativePhoneNumber)
{
    public static PatientProfileResult From(PatientProfileSnapshot snapshot)
    {
        var patient = snapshot.Patient;
        var insurance = snapshot.Insurance;
        var employment = snapshot.Employment;
        var relative = snapshot.Relative;

        return new(
            PatientRefID: patient.PatientRefID,
            DivisionID: patient.DivisionID,
            HisPatientID: patient.HisPatientID,
            PatientCode: patient.PatientCode,
            FullName: patient.FullName,
            Dob: patient.Dob,
            BirthYear: patient.BirthYear,
            GenderID: patient.GenderID,
            IdentityNumber: patient.IdentityNumber,
            IdentityIssuedDate: patient.IdentityIssuedDate,
            IdentityIssuerOptionID: patient.IdentityIssuerOptionID,
            PhoneNumber: patient.PhoneNumber,
            Email: patient.Email,
            Address: patient.Address,
            ProvinceCode: patient.ProvinceCode,
            WardCode: patient.WardCode,
            EthnicityOptionID: patient.EthnicityOptionID,
            BloodAboCode: patient.BloodAboCode,
            BloodRhCode: patient.BloodRhCode,
            IsActive: patient.IsActive,
            IdentityIssuerCode: patient.IdentityIssuerOption?.Code ?? "",
            IdentityIssuerName: patient.IdentityIssuerOption?.Name ?? "",
            EthnicityCode: patient.EthnicityOption?.Code ?? "",
            EthnicityName: patient.EthnicityOption?.Name ?? "",
            InsuranceRefID: insurance?.InsuranceRefID,
            InsuranceNumber: insurance?.InsuranceNumber ?? "",
            InsuranceObjectCode: insurance?.InsuranceObjectOption?.Code ?? "",
            InsuranceObjectName: insurance?.InsuranceObjectOption?.Name ?? "",
            InsuranceValidFrom: insurance?.ValidFrom,
            InsuranceValidTo: insurance?.ValidTo,
            RegistrationPlaceCode: insurance?.RegistrationPlaceOption?.Code ?? "",
            RegistrationPlaceName: insurance?.RegistrationPlaceOption?.Name ?? "",
            EmploymentRefID: employment?.EmploymentRefID,
            OccupationCode: employment?.OccupationOption?.Code ?? "",
            OccupationName: employment?.OccupationOption?.Name ?? "",
            StaffCode: employment?.StaffCode ?? "",
            OrgDeptName: employment?.OrgDeptName ?? "",
            JobTitle: employment?.JobTitle ?? "",
            RelativeRefID: relative?.RelativeRefID,
            RelativeRelationshipCode: relative?.RelationshipCode ?? "",
            RelativeRelationshipName: RelationshipNames.Of(relative?.RelationshipCode),
            RelativeFullName: relative?.FullName ?? "",
            RelativeIdentityNumber: relative?.IdentityNumber ?? "",
            RelativePhoneNumber: relative?.PhoneNumber ?? "");
    }
}

public interface IGetPatientProfileHandler
{
    Task<ApplicationResult<PatientProfileResult>> HandleAsync(
        GetPatientProfileQuery query, CancellationToken ct = default);
}

public sealed class GetPatientProfileHandler : IGetPatientProfileHandler
{
    private readonly IPatientRepository _patientRepository;

    public GetPatientProfileHandler(IPatientRepository patientRepository)
    {
        _patientRepository = patientRepository;
    }

    public async Task<ApplicationResult<PatientProfileResult>> HandleAsync(
        GetPatientProfileQuery query, CancellationToken ct = default)
    {
        if (query.PatientRefID == Guid.Empty)
        {
            return ApplicationResult<PatientProfileResult>.Fail(
                ApplicationFailureCode.BadRequest,
                "PatientRefID không hợp lệ.");
        }

        var snapshot = await _patientRepository.FindActiveProfileAsync(
            query.DivisionId, query.PatientRefID, ct);

        if (snapshot == null)
        {
            return ApplicationResult<PatientProfileResult>.Fail(
                ApplicationFailureCode.NotFound,
                "Không tìm thấy hồ sơ người bệnh.");
        }

        return ApplicationResult<PatientProfileResult>.Success(PatientProfileResult.From(snapshot));
    }
}
