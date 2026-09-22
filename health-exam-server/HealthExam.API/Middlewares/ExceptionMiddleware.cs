using HealthExam.API.Contracts;
using HealthExam.Domain.Common;

namespace HealthExam.API.Middlewares;

/// <summary>
/// Bọc mọi lỗi chưa bắt thành envelope ResultData, kèm TraceID để tra log.
/// Chi tiết exception chỉ vào log, không lọt ra response.
/// </summary>
public class ExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionMiddleware> _logger;

    public ExceptionMiddleware(RequestDelegate next, ILogger<ExceptionMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (HealthExamException ex)
        {
            _logger.LogWarning(ex, "Lỗi nghiệp vụ {ErrorCode} tại {Path}", ex.ErrorCode, context.Request.Path);
            await SafeWrite(context, ex.ErrorCode, ex.CustomMessage, ex.Payload);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi chưa xử lý tại {Path}", context.Request.Path);
            await SafeWrite(context, ErrorCodes.InternalError);
        }
    }

    private static async Task SafeWrite(HttpContext context, int code, string message = null, object payload = null)
    {
        // Response đã bắt đầu gửi thì không ghi đè được nữa — ghi tiếp sẽ ném lỗi thứ hai
        // che mất lỗi gốc.
        if (context.Response.HasStarted) return;
        await ErrorResponse.WriteAsync(context, code, message, payload);
    }
}
