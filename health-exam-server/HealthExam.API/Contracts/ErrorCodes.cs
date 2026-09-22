namespace HealthExam.API.Contracts;

/// <summary>
/// Bảng mã lỗi health-exam-server — health-exam-service/02-api-spec §2.
/// </summary>
public static class ErrorCodes
{
    public const int Success = 0;

    // --- §2.1 kế thừa từ form-server
    public const int BadRequest = 4001;       // thiếu/sai dữ liệu vào, gồm thiếu X-Division-Id
    public const int Unauthorized = 4010;
    public const int Forbidden = 4030;
    public const int NotOwner = 4031;         // RULE-04: không phải người tạo/ký
    public const int NotFound = 4040;
    public const int InvalidState = 4090;     // sai trạng thái
    public const int SignPrecondition = 4221; // RULE-05: thiếu điều kiện ký kết luận
    public const int InternalError = 5000;

    // --- §2.2 riêng của KSK
    public const int FileInvalid = 4002;
    public const int SessionClosed = 4091;
    public const int NotInSession = 4092;
    public const int DuplicateInSession = 4093;
    public const int DependencyUnavailable = 5020;
    public const int ParaclinicalResultExists = 4094;
    public const int VendorPayloadIncomplete = 4095;
    public const int VendorNotConfigured = 5021;
    public const int HisBadGateway = 5022;
    public const int HisTimeout = 5040;
    public const int PatientProfileChanged = 4096;

    /// <summary>HTTP status tương ứng — envelope vẫn mang mã nghiệp vụ đầy đủ.</summary>
    public static int ToHttpStatus(int errorCode) => errorCode switch
    {
        Success => 200,
        FileInvalid => 400,
        Unauthorized => 401,
        Forbidden or NotOwner => 403,
        NotFound => 404,
        InvalidState or SessionClosed or NotInSession or DuplicateInSession
            or ParaclinicalResultExists or PatientProfileChanged => 409,
        SignPrecondition or VendorPayloadIncomplete => 422,
        InternalError => 500,
        HisBadGateway => 502,
        HisTimeout => 504,
        DependencyUnavailable or VendorNotConfigured => 503,
        _ => 400
    };

    public static string DefaultMessage(int errorCode) => errorCode switch
    {
        Success => "",
        BadRequest => "Dữ liệu không hợp lệ",
        FileInvalid => "File Excel sai định dạng hoặc thiếu cột bắt buộc",
        Unauthorized => "Chưa xác thực",
        Forbidden => "Không đủ quyền",
        NotOwner => "Dữ liệu của người khác, bạn không thể chỉnh sửa",
        NotFound => "Không tìm thấy dữ liệu",
        InvalidState => "Trạng thái không cho phép thao tác này",
        SessionClosed => "Đợt khám đã đóng",
        NotInSession => "Hồ sơ không thuộc đợt khám này",
        DuplicateInSession => "Người bệnh đã có hồ sơ trong đợt khám này",
        ParaclinicalResultExists => "Chỉ định đã có kết quả, không huỷ được",
        SignPrecondition => "Chưa đủ điều kiện để ký",
        DependencyUnavailable => "Dịch vụ phụ thuộc đang không phản hồi, vui lòng thử lại",
        HisBadGateway => "Dịch vụ HIS phản hồi không hợp lệ, vui lòng thử lại sau",
        HisTimeout => "Dịch vụ HIS xử lý quá thời gian, vui lòng thử lại sau",
        PatientProfileChanged => "Thông tin người bệnh đã thay đổi",
        _ => "Lỗi hệ thống"
    };
}
