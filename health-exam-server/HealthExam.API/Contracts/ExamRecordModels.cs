using HealthExam.Domain.Common;

namespace HealthExam.API.Contracts;

/// <summary>
/// Hồ sơ KSK của một người trong đợt — dùng cho cả danh sách và chi tiết.
/// Một shape thay vì hai vì màn danh sách và màn wizard đọc gần trọn bộ cùng một tập cột;
/// tách đôi chỉ để bớt vài trường thì FE phải nhớ "màn nào có trường nào".
/// </summary>
public class ExamRecordItem
{
    public Guid RecordID { get; set; }
    public Guid SessionID { get; set; }
    public string SessionCode { get; set; } = "";
    public DateOnly ExamDate { get; set; }
    public string RecordCode { get; set; } = "";

    // Bước 1 — hành chính
    public long PatientID { get; set; }
    public long? AdmissionID { get; set; }
    public string HisAdmissionLinkStatus { get; set; } = "Pending";
    public string PatientCode { get; set; } = "";
    public string FullName { get; set; } = "";
    public DateOnly? Dob { get; set; }
    public short? BirthYear { get; set; }
    public short GenderID { get; set; }
    public string IdentityNumber { get; set; } = "";
    public string InsuranceNumber { get; set; } = "";
    public string PhoneNumber { get; set; } = "";
    public string Email { get; set; } = "";
    public string Address { get; set; } = "";
    public string StaffCode { get; set; } = "";
    public string OrgDeptName { get; set; } = "";
    public string JobTitle { get; set; } = "";

    // Bước 2 — nhóm khám + con trỏ sang form-server
    public string VariantCode { get; set; } = "";
    public string VariantName { get; set; } = "";
    public Guid? PackageID { get; set; }
    public string PackageName { get; set; } = "";
    public Guid? FormID { get; set; }
    public string FormCode { get; set; } = "";
    public Guid? SubmissionID { get; set; }

    // Máy trạng thái
    public ExamRecordState State { get; set; }
    public string StateName { get; set; } = "";
    public DateTime? RegisteredAt { get; set; }
    public DateTime? ExamStartedAt { get; set; }
    public DateTime? ExamFinishedAt { get; set; }
    public DateTime? CancelledAt { get; set; }
    public string CancelReason { get; set; } = "";
    public string HisSignStatus { get; set; } = "";
    public DateTime? HisSignedAt { get; set; }
    public string HisSignedFilePath { get; set; } = "";

    // Cache tiến độ do webhook cập nhật — phase này luôn 0/0 vì webhook chưa làm
    public short ProgressDone { get; set; }
    public short ProgressTotal { get; set; }
    public string HealthClassCode { get; set; } = "";
    public string Note { get; set; }

    // Master data mở rộng (đăng ký KSK)
    public string EthnicityCode { get; set; } = "";
    public string EthnicityName { get; set; } = "";
    public string OccupationCode { get; set; } = "";
    public string OccupationName { get; set; } = "";
    public string BloodAboCode { get; set; } = "";
    public string BloodAboName { get; set; } = "";
    public string BloodRhCode { get; set; } = "";
    public string BloodRhName { get; set; } = "";
    public string ProvinceCode { get; set; } = "";
    public string ProvinceName { get; set; } = "";
    public string WardCode { get; set; } = "";
    public string WardName { get; set; } = "";
    public DateOnly? IdentityIssuedDate { get; set; }
    public string IdentityIssuerCode { get; set; } = "";
    public string IdentityIssuerName { get; set; } = "";
    public string RelativeRelationshipCode { get; set; } = "";
    public string RelativeRelationshipName { get; set; } = "";
    public string RelativeFullName { get; set; } = "";
    public string RelativeIdentityNumber { get; set; } = "";
    public string RelativePhoneNumber { get; set; } = "";
    public string InsuranceObjectCode { get; set; } = "";
    public string InsuranceObjectName { get; set; } = "";
    public DateOnly? InsuranceValidFrom { get; set; }
    public DateOnly? InsuranceValidTo { get; set; }
    public string ExamReason { get; set; } = "";
    public string PatientTypeCode { get; set; } = "";
    public string PatientTypeName { get; set; } = "";
    public string PatientSubjectCode { get; set; } = "";
    public string PatientSubjectName { get; set; } = "";
    public string PaymentSourceCode { get; set; } = "";
    public string PaymentSourceName { get; set; } = "";
    public string PaymentSourceOther { get; set; } = "";
    public string ExamLocationCode { get; set; } = "";
    public string ExamLocationName { get; set; } = "";

    public string RegistrationPlaceCode { get; set; } = "";
    public string RegistrationPlaceName { get; set; } = "";
    public Guid? PatientRefID { get; set; }
    public Guid? InsuranceRefID { get; set; }
    public Guid? EmploymentRefID { get; set; }
    public Guid? RelativeRefID { get; set; }
    public Guid? PatientTypeOptionID { get; set; }
    public Guid? PaymentSourceOptionID { get; set; }
    public Guid? ExamLocationOptionID { get; set; }

    public static ExamRecordItem From(HealthExam.Application.ExamRecords.ExamRecordResult x) => new()
    {
        RecordID = x.RecordID,
        SessionID = x.SessionID,
        SessionCode = x.SessionCode,
        ExamDate = x.ExamDate,
        RecordCode = x.RecordCode,
        PatientID = x.PatientID,
        AdmissionID = x.AdmissionID,
        PatientCode = x.PatientCode,
        FullName = x.FullName,
        Dob = x.Dob,
        BirthYear = x.BirthYear,
        GenderID = x.GenderID,
        IdentityNumber = x.IdentityNumber,
        InsuranceNumber = x.InsuranceNumber,
        PhoneNumber = x.PhoneNumber,
        Email = x.Email,
        Address = x.Address,
        StaffCode = x.StaffCode,
        OrgDeptName = x.OrgDeptName,
        JobTitle = x.JobTitle,
        VariantCode = x.VariantCode,
        VariantName = x.VariantName,
        PackageID = x.PackageID,
        PackageName = x.PackageName,
        FormID = x.FormID,
        FormCode = x.FormCode,
        SubmissionID = x.SubmissionID,
        State = x.State,
        StateName = x.StateName,
        RegisteredAt = x.RegisteredAt,
        ExamStartedAt = x.ExamStartedAt,
        ExamFinishedAt = x.ExamFinishedAt,
        CancelledAt = x.CancelledAt,
        CancelReason = x.CancelReason,
        HisSignStatus = x.HisSignStatus,
        HisSignedAt = x.HisSignedAt,
        HisSignedFilePath = x.HisSignedFilePath,
        ProgressDone = x.ProgressDone,
        ProgressTotal = x.ProgressTotal,
        HealthClassCode = x.HealthClassCode,
        Note = x.Note,
        EthnicityCode = x.EthnicityCode,
        EthnicityName = x.EthnicityName,
        OccupationCode = x.OccupationCode,
        OccupationName = x.OccupationName,
        BloodAboCode = x.BloodAboCode,
        BloodAboName = x.BloodAboName,
        BloodRhCode = x.BloodRhCode,
        BloodRhName = x.BloodRhName,
        ProvinceCode = x.ProvinceCode,
        ProvinceName = x.ProvinceName,
        WardCode = x.WardCode,
        WardName = x.WardName,
        IdentityIssuedDate = x.IdentityIssuedDate,
        IdentityIssuerCode = x.IdentityIssuerCode,
        IdentityIssuerName = x.IdentityIssuerName,
        RelativeRelationshipCode = x.RelativeRelationshipCode,
        RelativeRelationshipName = x.RelativeRelationshipName,
        RelativeFullName = x.RelativeFullName,
        RelativeIdentityNumber = x.RelativeIdentityNumber,
        RelativePhoneNumber = x.RelativePhoneNumber,
        InsuranceObjectCode = x.InsuranceObjectCode,
        InsuranceObjectName = x.InsuranceObjectName,
        InsuranceValidFrom = x.InsuranceValidFrom,
        InsuranceValidTo = x.InsuranceValidTo,
        ExamReason = x.ExamReason,
        PatientTypeCode = x.PatientTypeCode,
        PatientTypeName = x.PatientTypeName,
        PatientSubjectCode = x.PatientSubjectCode,
        PatientSubjectName = x.PatientSubjectName,
        PaymentSourceCode = x.PaymentSourceCode,
        PaymentSourceName = x.PaymentSourceName,
        PaymentSourceOther = x.PaymentSourceOther,
        ExamLocationCode = x.ExamLocationCode,
        ExamLocationName = x.ExamLocationName,
        RegistrationPlaceCode = x.RegistrationPlaceCode,
        RegistrationPlaceName = x.RegistrationPlaceName,
        PatientRefID = x.PatientRefID,
        InsuranceRefID = x.InsuranceRefID,
        EmploymentRefID = x.EmploymentRefID,
        RelativeRefID = x.RelativeRefID,
        PatientTypeOptionID = x.PatientTypeOptionID,
        PaymentSourceOptionID = x.PaymentSourceOptionID,
        ExamLocationOptionID = x.ExamLocationOptionID
    };
}

/// <summary>
/// Bộ lọc độc lập cho danh sách hồ sơ. Các chuỗi tìm gần đúng, còn trạng thái/nhóm khám
/// và đợt khám là điều kiện chính xác. From/To là khoảng ngày tạo hồ sơ, hai đầu đều bao gồm.
/// </summary>
public sealed class ExamRecordListFilter
{
    public Guid? SessionID { get; init; }
    public string Keyword { get; init; }
    public short? State { get; init; }
    public string VariantCode { get; init; }
    public string RecordCode { get; init; }
    public string PatientCode { get; init; }
    public string FullName { get; init; }
    public string IdentityNumber { get; init; }
    public string PhoneNumber { get; init; }
    public DateOnly? From { get; init; }
    public DateOnly? To { get; init; }
}

/// <summary>
/// Body bước 1 wizard (POST) và sửa bước 1 (PUT) của phase 1. FE chưa quản lý đợt khám nên
/// không nhận SessionID; server tự neo hồ sơ vào đợt kỹ thuật mặc định của tenant. DTO cũng
/// không có State: trạng thái thuộc state-machine do server sở hữu; đường thủ công là /confirm.
/// </summary>
public class ExamRecordWriteRequest
{
    /// <summary>Bỏ trống khi POST thì server tự sinh theo {SessionCode}-{số thứ tự}.</summary>
    public string RecordCode { get; set; }

    public long? PatientID { get; set; }
    public long? AdmissionID { get; set; }
    public string PatientCode { get; set; }
    public string FullName { get; set; }
    public DateOnly? Dob { get; set; }
    public short? BirthYear { get; set; }
    public short? GenderID { get; set; }
    public string IdentityNumber { get; set; }
    public string InsuranceNumber { get; set; }
    public string PhoneNumber { get; set; }
    public string Email { get; set; }
    public string Address { get; set; }
    public string StaffCode { get; set; }
    public string OrgDeptName { get; set; }
    public string JobTitle { get; set; }

    /// <summary>DTK_01..DTK_10. Bắt buộc khi POST — biểu mẫu bên form-server chọn theo mã này.</summary>
    public string VariantCode { get; set; }
    public Guid? PackageID { get; set; }
    public string Note { get; set; }

    // Master data mở rộng (đăng ký KSK)
    public string EthnicityCode { get; set; }
    public string OccupationCode { get; set; }
    public string BloodAboCode { get; set; }
    public string BloodRhCode { get; set; }
    public string ProvinceCode { get; set; }
    public string WardCode { get; set; }
    public DateOnly? IdentityIssuedDate { get; set; }
    public string IdentityIssuerCode { get; set; }
    public string RelativeRelationshipCode { get; set; }
    public string RelativeFullName { get; set; }
    public string RelativeIdentityNumber { get; set; }
    public string RelativePhoneNumber { get; set; }
    public string InsuranceObjectCode { get; set; }
    public DateOnly? InsuranceValidFrom { get; set; }
    public DateOnly? InsuranceValidTo { get; set; }
    public string ExamReason { get; set; }
    public string PatientTypeCode { get; set; }
    public string PatientSubjectCode { get; set; }
    public string PaymentSourceCode { get; set; }
    public string PaymentSourceOther { get; set; }
    public string ExamLocationCode { get; set; }
    public string RegistrationPlaceCode { get; set; }
    public Guid? PatientRefID { get; set; }
    public bool? SetAsActiveProfile { get; set; }
}

/// <summary>
/// Contract nội bộ còn SessionID cho các luồng đã neo vào một đợt cụ thể, hiện là import Excel.
/// Không dùng kiểu này ở controller phase 1; phase quản lý đợt sau có thể đưa nó trở lại API.
/// </summary>
public class ExamRecordSaveRequest : ExamRecordWriteRequest
{
    public Guid? SessionID { get; set; }
}

/// <summary>Yêu cầu hủy đăng ký hoặc hủy khám (POST /v1/exam-records/{id}/cancel).</summary>
public class ExamRecordCancelRequest
{
    public string Reason { get; set; }
}
