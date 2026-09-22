using System.Security.Claims;
using System.Text;
using HealthExam.API.Contracts;
namespace HealthExam.API.Middlewares;

/// <summary>
/// Chốt chặn của nhánh <c>/v1/hooks/*</c>: chỉ gói mang chữ ký HMAC đúng mới đi tiếp.
///
/// Vì sao là MIDDLEWARE chứ không phải một dòng kiểm trong controller: chữ ký tính trên
/// NGUYÊN VĂN thân gói, mà tới lượt controller thì thân đã bị model binder đọc và dựng lại —
/// serialize lại để tính chữ ký là tính trên một chuỗi khác (thứ tự khoá, khoảng trắng,
/// định dạng số đều có thể lệch). Đọc ở đây, một lần, rồi tua lại cho model binder.
///
/// Lý do thứ hai quan trọng hơn: đặt phép kiểm trong controller thì endpoint hook TIẾP THEO
/// mà ai đó thêm vào sẽ mặc định KHÔNG có phép kiểm. Ở đây thì mặc định là có, và muốn bỏ
/// phải sửa đúng file này.
///
/// Đứng TRƯỚC AuthGuardMiddleware và tự dựng danh tính: form-server không có tài khoản IAM
/// nào để mang token nhân viên, còn dùng SECRET_INTER trần thì mất phần "thân gói không bị
/// sửa" — một Bearer token đúng vẫn cho phép sửa SubjectID trên đường đi.
/// </summary>
public class WebhookSignatureMiddleware
{
    /// <summary>Tiền tố nhánh nhận sự kiện. Tách riêng để reverse proxy chặn được bằng một luật.</summary>
    public const string PathPrefix = "/v1/hooks";

    /// <summary>Nguyên văn thân gói, để controller ghi vào cột Payload mà không phải đọc lại.</summary>
    public const string RawBodyItemKey = "HEALTHEXAM_WEBHOOK_RAW_BODY";

    /// <summary>
    /// Trần kích thước thân gói. Chữ ký buộc phải đọc TOÀN BỘ thân vào bộ nhớ trước khi biết
    /// gói có hợp lệ không — không có trần thì một request 1GB từ người lạ là một lần OOM.
    /// Gói sự kiện thật cỡ vài KB.
    /// </summary>
    private const int MaxBodyBytes = 256 * 1024;

    private readonly RequestDelegate _next;
    private readonly ILogger<WebhookSignatureMiddleware> _logger;

    public WebhookSignatureMiddleware(RequestDelegate next, ILogger<WebhookSignatureMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? "";
        if (!path.StartsWith(PathPrefix, StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        var secret = WebhookSignature.SecretFromEnvironment();
        if (string.IsNullOrWhiteSpace(secret))
        {
            // FAIL CLOSED. Không có nhánh "chưa cấu hình thì cho qua": nhánh đó biến một lần
            // quên đặt biến môi trường thành một endpoint mà bất kỳ ai gọi được cũng đẩy được
            // hồ sơ sang "Đã khám", và triệu chứng là mọi thứ vẫn chạy.
            _logger.LogError(
                "Nhận webhook nhưng {Env} chưa được đặt — từ chối toàn bộ nhánh {Prefix}. "
                + "Đây là lỗi cấu hình triển khai, phải đặt biến môi trường rồi khởi động lại.",
                WebhookSignature.SecretEnv, PathPrefix);
            await Reject(context, "Điểm nhận webhook chưa được cấu hình bí mật chữ ký");
            return;
        }

        var raw = await ReadBodyAsync(context);
        if (raw == null)
        {
            _logger.LogWarning("Gói webhook vượt {Max} byte — từ chối trước khi đọc hết.", MaxBodyBytes);
            await Reject(context, "Gói sự kiện quá lớn");
            return;
        }

        var signature = context.Request.Headers[WebhookSignature.HeaderName].ToString();
        if (!WebhookSignature.Verify(raw, signature, secret))
        {
            // KHÔNG nói sai ở đâu (thiếu header hay sai chữ ký): endpoint này mở ra Internet
            // qua gateway, và phân biệt hai vế là chỉ đường cho người dò.
            _logger.LogWarning(
                "Từ chối gói webhook: chữ ký {Header} không khớp (TraceID={TraceId}).",
                WebhookSignature.HeaderName, context.Items[HealthExamRequestContext.TraceIdItemKey]);
            await Reject(context, "Chữ ký gói sự kiện không hợp lệ");
            return;
        }

        context.Items[RawBodyItemKey] = raw;

        // Danh tính của một HỆ THỐNG, không của người: mọi dòng audit sinh ra từ nhánh này
        // phải phân biệt được với thao tác của nhân viên. Gán ở đây thì AuthGuardMiddleware
        // ở sau thấy request đã xác thực và không chặn nữa.
        context.User = new ClaimsPrincipal(new ClaimsIdentity(
            new[] { new Claim(ClaimNames.Scope, Scopes.Service) },
            authenticationType: "WebhookSignature"));

        await _next(context);
    }

    /// <summary>
    /// Đọc nguyên văn thân gói rồi TUA LẠI về đầu. Thiếu bước tua thì model binder của MVC
    /// đọc phải một luồng đã cạn và mọi trường của gói về null — trông y hệt "form-server gửi
    /// gói rỗng". Trả null nếu vượt trần kích thước.
    /// </summary>
    private static async Task<string> ReadBodyAsync(HttpContext context)
    {
        context.Request.EnableBuffering();

        var buffer = new byte[MaxBodyBytes + 1];
        var read = 0;
        while (read < buffer.Length)
        {
            var n = await context.Request.Body.ReadAsync(buffer.AsMemory(read, buffer.Length - read));
            if (n == 0) break;
            read += n;
        }

        context.Request.Body.Position = 0;

        if (read > MaxBodyBytes) return null;
        return Encoding.UTF8.GetString(buffer, 0, read);
    }

    /// <summary>
    /// 4030 chứ không 4010: gate của 03-task H2-04 ghi rõ "sai X-Signature → 4030". Khác
    /// nghĩa thật — 4010 là "chưa xác thực, đăng nhập đi", còn ở đây bên gọi ĐÃ tự xưng danh
    /// và cái sai là chữ ký, tức gói không đáng tin. Và 403 thì bên phát không retry.
    /// </summary>
    private static Task Reject(HttpContext context, string message)
        => ErrorResponse.WriteAsync(context, ErrorCodes.Forbidden, message);
}
