namespace HealthExam.Domain.Common;

/// <summary>
/// Trần độ dài các cột chuỗi của HEX_ExamRecord — nguồn DUY NHẤT cho cả cấu hình EF
/// (<c>HealthExamDbContext</c>) lẫn mọi phép kiểm ở tầng service.
///
/// Vì sao phải là hằng số dùng chung chứ không phải hai con số viết tay ở hai chỗ: pha 1 của
/// nạp Excel kiểm độ dài còn DDL khai độ dài. Hai con số lệch nhau thì pha 1 duyệt một ô mà
/// tầng ghi từ chối, và triệu chứng KHÔNG phải "dòng 4 sai" mà là HTTP 500 giữa lô, nửa số
/// hồ sơ đã vào DB, lô kẹt Pending, bấm lại thì đẻ thêm hồ sơ trùng (F2 của review MR !4).
///
/// Cùng lý do đó cho <see cref="HealthClassCode"/>: giá trị chép từ gói webhook của
/// form-server, bên phát không bảo đảm được nó nằm trong I..V (danh mục
/// HEALTH_EXAM_HealthClass chưa tồn tại như dữ liệu IAM), nên bên nhận phải tự đo trước khi
/// gán — nếu không, một ô 30 ký tự làm ĐỨNG cả hàng đợi webhook (BLOCKER 1 review MR !5).
/// </summary>
public static class RecordFieldLengths
{
    public const int RecordCode = 50;
    public const int PatientCode = 50;
    public const int FullName = 255;
    public const int IdentityNumber = 20;
    public const int InsuranceNumber = 20;
    public const int PhoneNumber = 20;
    public const int Email = 100;
    public const int Address = 500;
    public const int StaffCode = 50;
    public const int OrgDeptName = 255;
    public const int JobTitle = 255;
    public const int VariantCode = 50;
    public const int PackageName = 500;
    public const int FormCode = 50;
    public const int CancelReason = 500;
    public const int HealthClassCode = 20;
    public const int HisSyncStatus = 50;
    public const int HisSyncError = 1000;
    public const int HisSignedFilePath = 1000;
    public const int HisSignStatus = 50;

    public const int EthnicityCode = 50;
    public const int EthnicityName = 500;
    public const int OccupationCode = 50;
    public const int OccupationName = 500;
    public const int BloodAboCode = 50;
    public const int BloodAboName = 500;
    public const int BloodRhCode = 50;
    public const int BloodRhName = 500;
    public const int ProvinceCode = 50;
    public const int ProvinceName = 500;
    public const int WardCode = 50;
    public const int WardName = 500;
    public const int IdentityIssuerCode = 50;
    public const int IdentityIssuerName = 500;
    public const int RelativeRelationshipCode = 50;
    public const int RelativeRelationshipName = 500;
    public const int RelativeFullName = 255;
    public const int RelativeIdentityNumber = 20;
    public const int RelativePhoneNumber = 20;
    public const int InsuranceObjectCode = 50;
    public const int InsuranceObjectName = 500;
    public const int ExamReason = 500;
    public const int PatientTypeCode = 50;
    public const int PatientTypeName = 500;
    public const int PaymentSourceCode = 50;
    public const int PaymentSourceName = 500;
    public const int PaymentSourceOther = 500;
    public const int ExamLocationCode = 50;
    public const int ExamLocationName = 500;

    /// <summary>
    /// Trần của một TRƯỜNG NGHIỆP VỤ theo tên (tên trường của ExamRecordSaveRequest, cũng là
    /// khoá của ExamImportColumns). NULL = trường không có trần chuỗi (số, ngày, hoặc không
    /// ánh xạ thẳng vào một cột).
    /// </summary>
    public static int? MaxOf(string field) => field switch
    {
        nameof(RecordCode) => RecordCode,
        nameof(PatientCode) => PatientCode,
        nameof(FullName) => FullName,
        nameof(IdentityNumber) => IdentityNumber,
        nameof(InsuranceNumber) => InsuranceNumber,
        nameof(PhoneNumber) => PhoneNumber,
        nameof(Email) => Email,
        nameof(Address) => Address,
        nameof(StaffCode) => StaffCode,
        nameof(OrgDeptName) => OrgDeptName,
        nameof(JobTitle) => JobTitle,
        nameof(VariantCode) => VariantCode,
        nameof(EthnicityCode) => EthnicityCode,
        nameof(OccupationCode) => OccupationCode,
        nameof(BloodAboCode) => BloodAboCode,
        nameof(BloodRhCode) => BloodRhCode,
        nameof(ProvinceCode) => ProvinceCode,
        nameof(WardCode) => WardCode,
        nameof(IdentityIssuerCode) => IdentityIssuerCode,
        nameof(RelativeRelationshipCode) => RelativeRelationshipCode,
        nameof(RelativeFullName) => RelativeFullName,
        nameof(RelativeIdentityNumber) => RelativeIdentityNumber,
        nameof(RelativePhoneNumber) => RelativePhoneNumber,
        nameof(InsuranceObjectCode) => InsuranceObjectCode,
        nameof(ExamReason) => ExamReason,
        nameof(PatientTypeCode) => PatientTypeCode,
        nameof(PaymentSourceCode) => PaymentSourceCode,
        nameof(PaymentSourceOther) => PaymentSourceOther,
        nameof(ExamLocationCode) => ExamLocationCode,
        _ => null
    };
}

/// <summary>
/// Trần độ dài các cột chuỗi của HEX_WebhookInbox — cùng lý do như
/// <see cref="RecordFieldLengths"/>: tầng nhận phải đo được gói TRƯỚC khi lệnh INSERT đo hộ,
/// vì lỗi 22001 của PostgreSQL đi ra ngoài thành HTTP 500 và bên phát đọc 5xx là "thử lại
/// được" — tức retry vĩnh viễn cho một gói không bao giờ ghi được (M1, review MR !5).
/// </summary>
public static class WebhookFieldLengths
{
    public const int EventID = 100;
    public const int EventType = 50;
    public const int DivisionID = 20;
    public const int TraceID = 50;
    public const int LastError = 1000;
}

/// <summary>
/// Trần độ dài của khối chỉ định CLS — 01-db-model §5.
///
/// Cùng lý do với <see cref="RecordFieldLengths"/>: giá trị vào đây đến từ BÊN NGOÀI (ảnh
/// chụp danh mục dịch vụ của HIS, mã phiếu vendor trả về), và viết số ở hai chỗ là cách một
/// ô dài hơn cột làm hỏng cả lượt ghi.
/// </summary>
public static class ParaclinicalFieldLengths
{
    public const int OrderNo = 50;
    public const int ParaclinicalKind = 10;
    public const int ServiceCode = 50;
    public const int ServiceName = 500;
    public const int ServiceGroupCode = 50;
    public const int TargetSystem = 20;
    public const int ExternalOrderID = 100;
    public const int ResultSourceKind = 10;
    public const int ResultRefID = 100;
    public const int CancelReason = 500;
    public const int Note = 1000;

    /// <summary>Khoá chống trùng do vendor cấp — xem WebhookInbox.MessageID.</summary>
    public const int MessageID = 100;

    public const int HisPtCode = 50;
    public const int HisAdmissionCode = 50;
    public const int HisResultId = 100;
    public const int HisDetailId = 100;
    public const int ResultValue = 500;
    public const int ResultText = 4000;
    public const int ResultUnit = 50;
    public const int ReferenceRange = 255;
    public const int AbnormalFlag = 50;
    public const int LastSyncError = 2000;
}

/// <summary>
/// Trần độ dài của hàng đợi gửi ra vendor — HEX_IntegrationOutbox (P3b).
///
/// Cùng lý do với <see cref="ParaclinicalFieldLengths"/>: chuỗi vào đây đến từ BÊN NGOÀI
/// (thân phản hồi của RIS), và một ô dài hơn cột làm hỏng cả lượt ghi — ở đây là hỏng đúng
/// lượt ghi đang cố lưu lại nguyên nhân hỏng.
/// </summary>
public static class OutboxFieldLengths
{
    /// <summary>NEW | CANCELLED.</summary>
    public const int Operation = 20;

    /// <summary>{Vendor}:{Operation}:{OrderNo} — xem IntegrationOutbox.DedupKey.</summary>
    public const int DedupKey = 120;

    /// <summary>
    /// Vài trăm ký tự đầu của thân phản hồi. Không lưu nguyên: một hệ hỏng có thể trả về cả
    /// trang HTML lỗi, và bảng hàng đợi phình theo lỗi của bên khác thì đúng lúc sự cố là lúc
    /// DB đầy. Cắt ở tầng service, trần này là chốt cuối.
    /// </summary>
    public const int ResponseSnippet = 1000;
}
