namespace HealthExam.API.Middlewares;

/// <summary>
/// Tên claim trong JWT của IAM. Chép theo đúng bộ mà his-server PHÁT HÀNH
/// (HIS.Server/Service/SAM/AuthService.cs — generateJwtToken) và ĐỌC
/// (HIS.Core/Helpers/JwtMiddleware.cs).
///
/// ⚠️ Token của IAM KHÔNG dùng claim chuẩn <c>sub</c> / <c>nameid</c>. Người thao tác nằm ở
/// claim <c>EmployeeID</c>. Đọc theo <c>ClaimTypes.NameIdentifier</c> thì luôn ra rỗng và mọi
/// cột audit ghi 0 — im lặng, không lỗi, chỉ sai dữ liệu. Đó là lý do bộ tên này được đặt
/// thành hằng số ở một chỗ thay vì rải chuỗi trong code.
/// </summary>
public static class ClaimNames
{
    public const string AccountId = "AccountID";
    public const string EmployeeId = "EmployeeID";
    public const string EmployeeCode = "EmployeeCode";
    public const string UserName = "UserName";
    public const string DivisionId = "DivisionID";
    public const string DepartmentId = "DepartmentID";
    public const string Role = "Role";

    /// <summary>Không có trong token IAM; chỉ token cổng người bệnh (§6.2) và token service mang.</summary>
    public const string Scope = "scope";

    /// <summary>Mã người bệnh trong token cổng NB — token IAM không có.</summary>
    public const string Subject = "sub";
}

/// <summary>Giá trị của claim <c>scope</c>. Token nhân viên của IAM không mang claim này.</summary>
public static class Scopes
{
    /// <summary>Cổng web người bệnh (UC04) — token do chính health-exam-server cấp (§6.2).</summary>
    public const string Patient = "form:patient";

    /// <summary>Gọi nội bộ giữa các service bằng SECRET_INTER.</summary>
    public const string Service = "health-exam:service";
}

