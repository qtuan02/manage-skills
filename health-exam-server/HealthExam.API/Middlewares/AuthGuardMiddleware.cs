using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using HealthExam.API.Contracts;
namespace HealthExam.API.Middlewares;

/// <summary>
/// Chốt chặn xác thực: request không mang token hợp lệ thì dừng ở đây với <c>4010</c>.
///
/// Vì sao cần middleware riêng thay vì [Authorize]: JwtBearer trả 401 RỖNG (challenge của
/// framework), còn hợp đồng với FE là MỌI phản hồi đều là envelope ResultData kèm TraceID.
/// Một phản hồi 401 không có thân sẽ làm FE rơi vào nhánh "lỗi mạng" thay vì nhánh "hết phiên
/// đăng nhập", tức người dùng thấy báo lỗi sai.
///
/// Đặt SAU DivisionMiddleware: thiếu cả hai thứ thì báo thiếu tenant trước. X-Division-Id
/// không phải bí mật nên trả 4001 trước khi kiểm token không lộ gì, mà thứ tự cố định thì
/// thông báo lỗi mới đoán trước được.
/// </summary>
public class AuthGuardMiddleware
{
    private readonly RequestDelegate _next;

    /// <summary>Đường hạ tầng — không mang dữ liệu nghiệp vụ nên không cần danh tính.</summary>
    private static readonly string[] ExemptPrefixes =
    {
        "/health", "/swagger", "/favicon.ico"
    };

    public AuthGuardMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? "";
        var exempt = path == "/" ||
                     ExemptPrefixes.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase)) ||
                     IsAnonymousEndpoint(context);

        if (!exempt && context.User?.Identity?.IsAuthenticated != true)
        {
            // Token nội bộ giữa các service: SECRET_INTER là chuỗi bí mật dùng thẳng, không
            // phải JWT, nên JwtBearer ở trên không nhận ra. Dựng danh tính Integration để mọi
            // dòng audit của đường gọi này vẫn phân biệt được với thao tác của nhân viên.
            if (!TryAttachServiceIdentity(context))
            {
                await ErrorResponse.WriteAsync(
                    context,
                    ErrorCodes.Unauthorized,
                    "Thiếu hoặc sai token xác thực");
                return;
            }
        }

        await _next(context);
    }

    private static bool IsAnonymousEndpoint(HttpContext context)
        => HttpMethods.IsPost(context.Request.Method) &&
           context.Request.Path.Equals("/v1/auth/login", StringComparison.OrdinalIgnoreCase);

    private static bool TryAttachServiceIdentity(HttpContext context)
    {
        var secret = Environment.GetEnvironmentVariable(JwtSetup.ServiceTokenEnv);
        if (string.IsNullOrWhiteSpace(secret)) return false;

        var token = context.Request.Headers.Authorization.ToString();
        if (string.IsNullOrWhiteSpace(token)) return false;
        if (token.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            token = token["Bearer ".Length..].Trim();

        // So constant-time, không dùng `!=`: đây là bí mật dùng chung cả stack (iam-server và
        // form-server đều mang nó), và so chuỗi thường thoát ra ở byte đầu tiên khác nhau.
        // Thực tế jitter mạng át tín hiệu đó, nhưng phép so đúng chỉ tốn một dòng — còn đoán
        // được bí mật này là vào được cả /v1/internal lẫn nhánh webhook.
        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(token), Encoding.UTF8.GetBytes(secret)))
            return false;

        var identity = new ClaimsIdentity(
            new[] { new Claim(ClaimNames.Scope, Scopes.Service) },
            authenticationType: "ServiceToken");
        context.User = new ClaimsPrincipal(identity);
        return true;
    }
}
