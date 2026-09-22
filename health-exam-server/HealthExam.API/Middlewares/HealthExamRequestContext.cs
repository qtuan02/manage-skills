using HealthExam.Application.Common;
using HealthExam.Domain.Common;
namespace HealthExam.API.Middlewares;

/// <summary>
/// Hiện thực IHealthExamContext đọc từ HttpContext.
/// DivisionID lấy từ header X-Division-Id, KHÔNG lấy từ token: hệ thống chạy nhiều DB
/// tenant dùng chung mã, và token do service khác cấp thì không phải nguồn đáng tin để
/// chốt chặn tenant.
///
/// Ngược lại, ActorId thì CHỈ lấy từ token đã ký — không bao giờ từ body hay header. Ai đang
/// thao tác là thứ quyết định mọi kiểm tra chủ sở hữu (RULE-04), nên để client tự khai là
/// bỏ ngỏ toàn bộ hàng rào đó.
/// </summary>
public class HealthExamRequestContext : IHealthExamContext
{
    public const string TraceIdItemKey = "HEALTHEXAM_TRACE_ID";
    public const string DivisionHeader = "X-Division-Id";
    public const string ModuleHeader = "X-Module-Code";
    public const string TraceHeader = "X-Trace-Id";

    private readonly IHttpContextAccessor _accessor;

    public HealthExamRequestContext(IHttpContextAccessor accessor) => _accessor = accessor;

    private HttpContext Ctx => _accessor.HttpContext;

    public string TraceId => Ctx?.Items[TraceIdItemKey] as string ?? "";

    public string DivisionId => Ctx?.Request.Headers[DivisionHeader].ToString() ?? "";

    public string ModuleCode => Ctx?.Request.Headers[ModuleHeader].ToString() ?? "";

    public string Scope => Claim(ClaimNames.Scope);

    /// <summary>
    /// Người thao tác. Token IAM để EmployeeID ở claim TỰ ĐẶT chứ không ở <c>sub</c>
    /// (xem ClaimNames) — nên đọc EmployeeID trước, <c>sub</c> chỉ để dành cho token cổng
    /// người bệnh, nơi <c>sub</c> là Mã NB.
    /// </summary>
    public long ActorId
    {
        get
        {
            if (long.TryParse(Claim(ClaimNames.EmployeeId), out var employeeId)) return employeeId;
            if (long.TryParse(Claim(ClaimNames.Subject), out var subject)) return subject;
            return 0;
        }
    }

    /// <summary>
    /// Loại chủ thể. Suy theo claim CÓ THẬT trong token, không theo <c>scope</c> đơn thuần:
    /// token nhân viên của IAM KHÔNG mang claim <c>scope</c>, nên nếu chỉ nhìn scope thì mọi
    /// thao tác của nhân viên sẽ bị ghi audit là System(3) thay vì Employee(1).
    /// </summary>
    public ActorKind ActorKind => Scope switch
    {
        Scopes.Patient => ActorKind.Patient,            // cổng web người bệnh (UC04)
        Scopes.Service => ActorKind.Integration,        // service-to-service (SECRET_INTER)
        _ => Claim(ClaimNames.EmployeeId).Length > 0 ? ActorKind.Employee : ActorKind.System
    };

    /// <summary>Tên hiển thị để ghi vào nhật ký. Token IAM đặt ở claim <c>UserName</c>.</summary>
    public string ActorName => Claim(ClaimNames.UserName);

    /// <summary>Mã nhân viên. sign-server và ssm-server tra chứng thư theo mã, không theo ID.</summary>
    public string ActorCode => Claim(ClaimNames.EmployeeCode);

    public string GetRequestHeader(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "";
        return Ctx?.Request.Headers[name].ToString() ?? "";
    }

    private string Claim(string type) => Ctx?.User?.FindFirst(type)?.Value ?? "";
}
