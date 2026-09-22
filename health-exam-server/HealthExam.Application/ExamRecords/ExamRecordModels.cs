using System;
using System.Collections.Generic;
using HealthExam.Application.Common;
using HealthExam.Domain.Catalogs;
using HealthExam.Domain.Common;

namespace HealthExam.Application.ExamRecords;

public static class ExamRecordStateNames
{
    public static string Of(ExamRecordState state) => state switch
    {
        ExamRecordState.NotRegistered => "Chưa đăng ký",
        ExamRecordState.Waiting => "Chờ khám",
        ExamRecordState.InProgress => "Đang khám",
        ExamRecordState.Completed => "Đã khám",
        ExamRecordState.RegistrationCancelled => "Hủy đăng ký",
        ExamRecordState.ExamCancelled => "Hủy khám",
        _ => ""
    };
}

public sealed record ExamRecordResult(
    Guid RecordID,
    Guid SessionID,
    string SessionCode,
    DateOnly ExamDate,
    string RecordCode,
    long PatientID,
    long? AdmissionID,
    string PatientCode,
    string FullName,
    DateOnly? Dob,
    short? BirthYear,
    short GenderID,
    string IdentityNumber,
    string InsuranceNumber,
    string PhoneNumber,
    string Email,
    string Address,
    string StaffCode,
    string OrgDeptName,
    string JobTitle,
    string VariantCode,
    string VariantName,
    Guid? PackageID,
    string PackageName,
    Guid? FormID,
    string FormCode,
    Guid? SubmissionID,
    ExamRecordState State,
    string StateName,
    DateTime? RegisteredAt,
    DateTime? ExamStartedAt,
    DateTime? ExamFinishedAt,
    DateTime? CancelledAt,
    string CancelReason,
    short ProgressDone,
    short ProgressTotal,
    string HealthClassCode,
    string Note,
    string EthnicityCode,
    string EthnicityName,
    string OccupationCode,
    string OccupationName,
    string BloodAboCode,
    string BloodAboName,
    string BloodRhCode,
    string BloodRhName,
    string ProvinceCode,
    string ProvinceName,
    string WardCode,
    string WardName,
    DateOnly? IdentityIssuedDate,
    string IdentityIssuerCode,
    string IdentityIssuerName,
    string RelativeRelationshipCode,
    string RelativeRelationshipName,
    string RelativeFullName,
    string RelativeIdentityNumber,
    string RelativePhoneNumber,
    string InsuranceObjectCode,
    string InsuranceObjectName,
    DateOnly? InsuranceValidFrom,
    DateOnly? InsuranceValidTo,
    string ExamReason,
    string PatientTypeCode,
    string PatientTypeName,
    string PaymentSourceCode,
    string PaymentSourceName,
    string PaymentSourceOther,
    string ExamLocationCode,
    string ExamLocationName,
    DateTime CreatedDate,
    DateTime ModifiedDate,
    Guid? PatientRefID = null,
    Guid? InsuranceRefID = null,
    Guid? EmploymentRefID = null,
    Guid? RelativeRefID = null,
    Guid? PatientTypeOptionID = null,
    Guid? PatientSubjectOptionID = null,
    Guid? PaymentSourceOptionID = null,
    Guid? ExamLocationOptionID = null,
    string RegistrationPlaceCode = "",
    string RegistrationPlaceName = "",
    string PatientSubjectCode = "",
    string PatientSubjectName = "",

    /// <summary>
    /// Trạng thái ký kết luận trên HIS: "" (chưa ký) | "InProcessing" | "Signed".
    /// Đặt ở CUỐI record kèm giá trị mặc định để mọi chỗ dựng positional sẵn có không vỡ.
    /// </summary>
    string HisSignStatus = "",

    /// <summary>Giờ ký kết luận hoàn tất; chỉ có giá trị khi HisSignStatus = "Signed".</summary>
    DateTime? HisSignedAt = null,

    /// <summary>
    /// Đường dẫn file PDF đã ký trên HIS (HIS trả về sau khi ký xong). "" khi chưa có.
    /// FE KHÔNG mở trực tiếp được (endpoint xem file của HIS cần Bearer) — dùng nó làm điều kiện
    /// hiện nút và gọi GET /v1/exam-records/{id}/registration-form/preview để lấy PDF.
    /// </summary>
    string HisSignedFilePath = "");

public sealed record ExamRecordFilter(
    Guid? SessionID = null,
    string Keyword = null,
    short? State = null,
    string VariantCode = null,
    string RecordCode = null,
    string PatientCode = null,
    string FullName = null,
    string IdentityNumber = null,
    string PhoneNumber = null,
    DateOnly? From = null,
    DateOnly? To = null,
    int Page = 1,
    int Size = 20);

public sealed record ListExamRecordsQuery(
    string DivisionId,
    ExamRecordFilter Filter);

public sealed record GetExamRecordQuery(
    string DivisionId,
    Guid RecordId);

public sealed record CreateExamRecordCommand(
    string DivisionId,
    string ActorId,
    ActorKind ActorKind,
    Guid? SessionID = null,
    string RecordCode = null,
    long? PatientID = null,
    long? AdmissionID = null,
    string PatientCode = null,
    string FullName = null,
    DateOnly? Dob = null,
    short? BirthYear = null,
    short? GenderID = null,
    string IdentityNumber = null,
    string InsuranceNumber = null,
    string PhoneNumber = null,
    string Email = null,
    string Address = null,
    string StaffCode = null,
    string OrgDeptName = null,
    string JobTitle = null,
    string VariantCode = null,
    Guid? PackageID = null,
    string Note = null,
    string EthnicityCode = null,
    string OccupationCode = null,
    string BloodAboCode = null,
    string BloodRhCode = null,
    string ProvinceCode = null,
    string WardCode = null,
    DateOnly? IdentityIssuedDate = null,
    string IdentityIssuerCode = null,
    string RelativeRelationshipCode = null,
    string RelativeFullName = null,
    string RelativeIdentityNumber = null,
    string RelativePhoneNumber = null,
    string InsuranceObjectCode = null,
    DateOnly? InsuranceValidFrom = null,
    DateOnly? InsuranceValidTo = null,
    string ExamReason = null,
    string PatientTypeCode = null,
    string PatientSubjectCode = null,
    string PaymentSourceCode = null,
    string PaymentSourceOther = null,
    string ExamLocationCode = null,
    Guid? ImportBatchID = null,
    string TraceId = null,
    string RegistrationPlaceCode = null,
    Guid? PatientRefID = null,
    string Credential = null,
    bool? SetAsActiveProfile = null);

public sealed record UpdateExamRecordCommand(
    string DivisionId,
    Guid RecordId,
    string ActorId,
    ActorKind ActorKind,
    string RecordCode = null,
    long? PatientID = null,
    long? AdmissionID = null,
    string PatientCode = null,
    string FullName = null,
    DateOnly? Dob = null,
    short? BirthYear = null,
    short? GenderID = null,
    string IdentityNumber = null,
    string InsuranceNumber = null,
    string PhoneNumber = null,
    string Email = null,
    string Address = null,
    string StaffCode = null,
    string OrgDeptName = null,
    string JobTitle = null,
    string VariantCode = null,
    Guid? PackageID = null,
    string Note = null,
    string EthnicityCode = null,
    string OccupationCode = null,
    string BloodAboCode = null,
    string BloodRhCode = null,
    string ProvinceCode = null,
    string WardCode = null,
    DateOnly? IdentityIssuedDate = null,
    string IdentityIssuerCode = null,
    string RelativeRelationshipCode = null,
    string RelativeFullName = null,
    string RelativeIdentityNumber = null,
    string RelativePhoneNumber = null,
    string InsuranceObjectCode = null,
    DateOnly? InsuranceValidFrom = null,
    DateOnly? InsuranceValidTo = null,
    string ExamReason = null,
    string PatientTypeCode = null,
    string PatientSubjectCode = null,
    string PaymentSourceCode = null,
    string PaymentSourceOther = null,
    string ExamLocationCode = null,
    string TraceId = null,
    string RegistrationPlaceCode = null,
    Guid? PatientRefID = null,
    bool? SetAsActiveProfile = null);

public sealed record ConfirmExamRecordCommand(
    string DivisionId,
    Guid RecordId,
    string ActorId,
    ActorKind ActorKind,
    string TraceId = null);

public sealed record CancelExamRecordCommand(
    string DivisionId,
    Guid RecordId,
    string ActorId,
    ActorKind ActorKind,
    string Reason,
    string TraceId = null);

public sealed record VerifyPortalCredentialsCommand(
    string DivisionId,
    string PatientCode,
    string IdentityNumber,
    string InsuranceNumber);

public sealed record PortalCredentialsResult(
    bool IsValid,
    string RecordCode,
    string SessionCode,
    string FullName)
{
    public static PortalCredentialsResult Invalid() => new(false, "", "", "");
}

public sealed record GetExamSessionProgressQuery(
    string DivisionId,
    Guid SessionId);

public sealed record ExamSessionProgressCounts(
    int Total,
    int NotRegistered,
    int Waiting,
    int InProgress,
    int Completed,
    int RegistrationCancelled,
    int ExamCancelled,
    int Cancelled);

public sealed record ExamSessionProgressResult(
    Guid SessionID,
    string SessionCode,
    string State,
    ExamSessionProgressCounts Profiles,
    int ConclusionReady,
    DateTime? UpdatedAt);

internal sealed class ResolvedMasterData
{
    public MasterDataOption Ethnicity { get; set; }
    public MasterDataOption Occupation { get; set; }
    public (string Code, string Name)? BloodAbo { get; set; }
    public (string Code, string Name)? BloodRh { get; set; }
    public (string Code, string Name)? Province { get; set; }
    public (string Code, string Name)? Ward { get; set; }
    public MasterDataOption IdentityIssuer { get; set; }
    public (string Code, string Name)? RelativeRelationship { get; set; }
    public MasterDataOption InsuranceObject { get; set; }
    public MasterDataOption RegistrationPlace { get; set; }
    public MasterDataOption PatientType { get; set; }
    public MasterDataOption PatientSubject { get; set; }
    public MasterDataOption PaymentSource { get; set; }
    public MasterDataOption ExamLocation { get; set; }
}
