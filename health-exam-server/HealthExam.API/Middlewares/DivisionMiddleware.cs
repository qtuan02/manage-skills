using HealthExam.API.Contracts;
using HealthExam.Application.Common;
namespace HealthExam.API.Middlewares;

/// <summary>
/// Chốt chặn tenant: thiếu X-Division-Id thì chặn ngay ở tầng middleware với 4001.
///
/// Lý do đặt ở đây chứ không để từng handler tự kiểm: hệ thống chạy nhiều DB tenant, pilot
/// dùng chung DB với prod, và cùng một mã ở hai tenant có nghĩa khác nhau. Một request lọt
/// qua mà không có Division sẽ trả về dữ liệu trông vẫn hợp lý nhưng của đơn vị khác.
/// </summary>
public class DivisionMiddleware
{
    private readonly RequestDelegate _next;

    /// <summary>Đường dẫn hạ tầng, không mang dữ liệu nghiệp vụ nên không cần tenant.</summary>
    private static readonly string[] ExemptPrefixes =
    {
        "/health", "/swagger", "/favicon.ico"
    };

    public DivisionMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? "";
        var exempt = path == "/" ||
                     ExemptPrefixes.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase));

        if (!exempt)
        {
            var division = context.Request.Headers[HealthExamRequestContext.DivisionHeader].ToString();
            if (string.IsNullOrWhiteSpace(division))
            {
                await ErrorResponse.WriteAsync(
                    context,
                    ErrorCodes.BadRequest,
                    $"Thiếu header {HealthExamRequestContext.DivisionHeader}",
                    ValidationErrors.Of(HealthExamRequestContext.DivisionHeader, "Bỏ trống (bắt buộc)"));
                return;
            }
        }

        await _next(context);
    }
}
