using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using HealthExam.API.Contracts;
using HealthExam.Application.Common;
using HealthExam.Application.Integrations;

namespace HealthExam.API.Middlewares;

/// <summary>
/// Chốt chặn của nhánh <c>/v1/integration/vendor/{division}/*</c>: chỉ gói mang Basic đúng
/// mới đi tiếp, và tenant được lấy TỪ ĐƯỜNG DẪN.
///
/// Vì sao là middleware chứ không phải một dòng kiểm trong controller — cùng hai lý do với
/// <see cref="WebhookSignatureMiddleware"/>: (1) endpoint vendor TIẾP THEO mà ai đó thêm vào
/// sẽ mặc định CÓ phép kiểm, muốn bỏ thì phải sửa đúng file này; (2) tenant phải có mặt
/// TRƯỚC <see cref="DivisionMiddleware"/>, tức trước khi tới controller.
///
/// Vì sao Basic mà không dùng lại một trong hai cơ chế sẵn có: vendor không có tài khoản IAM
/// nào để mang JWT, và cũng không ký HMAC lên thân gói. Basic là thứ chính họ dùng ở chiều
/// ngược lại (ta gọi sang họ), nên đối xứng và không đẻ thêm một bí mật kiểu mới.
///
/// 🔴 FAIL CLOSED. Chưa cấu hình <c>RIS_CALLBACK_USER/PASSWORD</c> ⇒ 4010 cho mọi gói. KHÔNG
/// có nhánh "chưa cấu hình thì cho qua": nhánh đó biến một lần quên khai biến thành một
/// endpoint sửa được trạng thái hồ sơ y tế cho bất kỳ ai gọi tới.
/// </summary>
public class VendorCallbackAuthMiddleware
{
    /// <summary>Tiền tố nhánh. Tách riêng để reverse proxy chặn được bằng một luật.</summary>
    public const string PathPrefix = "/v1/integration/vendor";

    private readonly RequestDelegate _next;
    private readonly ILogger<VendorCallbackAuthMiddleware> _logger;

    public VendorCallbackAuthMiddleware(RequestDelegate next, ILogger<VendorCallbackAuthMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, IVendorCallbackAuthOptions options)
    {
        var path = context.Request.Path.Value ?? "";
        if (!path.StartsWith(PathPrefix, StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        if (!options.IsCallbackConfigured)
        {
            _logger.LogWarning(
                "Từ chối gói gọi về của vendor: chưa cấu hình {UserEnv}/{PassEnv} trên môi trường này.",
                options.CallbackUserEnv, options.CallbackPasswordEnv);
            await ErrorResponse.WriteAsync(context, ErrorCodes.Unauthorized, "Đường gọi về của vendor chưa được cấu hình");
            return;
        }

        if (!IsAuthorized(context, options))
        {
            // KHÔNG nói rõ sai ở đâu (tài khoản hay mật khẩu, hay thiếu hẳn header): câu trả
            // lời chi tiết chỉ giúp người đang dò.
            _logger.LogWarning("Gói gọi về của vendor mang Basic sai — từ chối. Path={Path}", path);
            await ErrorResponse.WriteAsync(context, ErrorCodes.Unauthorized, "Sai thông tin xác thực");
            return;
        }

        // ★ Dựng lại header tenant từ đoạn đường dẫn. Middleware phía sau và
        // HealthExamRequestContext vì thế KHÔNG phải biết đường vendor là ngoại lệ — chỉ có
        // đúng một chỗ trong service hiểu "tenant có thể tới từ URL", và nó là chỗ này.
        var division = DivisionFrom(path);
        if (string.IsNullOrWhiteSpace(division))
        {
            await ErrorResponse.WriteAsync(
                context, ErrorCodes.BadRequest,
                "Thiếu mã đơn vị trong đường dẫn: /v1/integration/vendor/{division}/…",
                ValidationErrors.Of("division", "Bỏ trống (bắt buộc)"));
            return;
        }
        context.Request.Headers[HealthExamRequestContext.DivisionHeader] = division;

        // Danh tính riêng để mọi dòng audit của đường này phân biệt được với thao tác của
        // nhân viên — cùng khuôn với ServiceToken và với gói webhook đã ký.
        context.User = new ClaimsPrincipal(new ClaimsIdentity(
            new[] { new Claim(ClaimNames.Scope, Scopes.Service) }, authenticationType: "VendorBasic"));

        await _next(context);
    }

    private static bool IsAuthorized(HttpContext context, IVendorCallbackAuthOptions options)
    {
        var header = context.Request.Headers.Authorization.ToString();
        if (string.IsNullOrWhiteSpace(header)) return false;
        if (!header.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase)) return false;

        string decoded;
        try
        {
            decoded = Encoding.UTF8.GetString(Convert.FromBase64String(header["Basic ".Length..].Trim()));
        }
        catch (FormatException)
        {
            return false;
        }

        var split = decoded.IndexOf(':');
        if (split < 0) return false;

        // So constant-time cả hai vế: mật khẩu này mở đúng một cửa, nhưng cửa đó ghi được vào
        // hồ sơ y tế. So chuỗi thường thoát ra ở byte đầu tiên khác nhau.
        return FixedEquals(decoded[..split], options.CallbackUser)
             & FixedEquals(decoded[(split + 1)..], options.CallbackPassword);
    }

    private static bool FixedEquals(string a, string b)
        => CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(a), Encoding.UTF8.GetBytes(b));

    /// <summary>
    /// Đoạn ngay sau tiền tố nhánh là mã đơn vị.
    ///
    /// CÔNG KHAI để test ghim được GIÁ TRỊ tách ra, không chỉ ghim bằng phủ định ("gói không
    /// bị chặn ở 4001"). Một phép tách sai vẫn đi qua mọi chốt chặn rồi ghi vào ĐƠN VỊ KHÁC —
    /// kết quả trông hoàn toàn hợp lý nên sẽ không ai phát hiện. Đó là thứ phải đỏ được.
    /// </summary>
    public static string DivisionFrom(string path)
    {
        var rest = path[PathPrefix.Length..].TrimStart('/');
        var slash = rest.IndexOf('/');
        return Uri.UnescapeDataString(slash < 0 ? rest : rest[..slash]).Trim();
    }
}
