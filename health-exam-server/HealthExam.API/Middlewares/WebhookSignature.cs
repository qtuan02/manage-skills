using System;
using System.Security.Cryptography;
using System.Text;

namespace HealthExam.API.Middlewares;

/// <summary>
/// Chữ ký HMAC-SHA256 của gói webhook — 02-api-spec §5.1.
///
/// Vì sao endpoint này KHÔNG dùng token như /v1/internal: bên gọi là form-server chạy tự
/// động, gói đi qua reverse proxy, và thứ cần bảo đảm không chỉ là "ai gọi" mà là "THÂN GÓI
/// KHÔNG BỊ SỬA". Một Bearer token đúng vẫn cho phép sửa SubjectID trên đường đi; chữ ký trên
/// nguyên văn thân gói thì không.
///
/// Bí mật dùng CHUNG với SECRET_INTER, cố ý không đẻ thêm biến môi trường thứ hai.
/// </summary>
public static class WebhookSignature
{
    public const string HeaderName = "X-Signature";

    /// <summary>Cùng biến với chiều gọi RA form-server.</summary>
    public const string SecretEnv = "SECRET_INTER";

    /// <summary>Tiền tố chuẩn của header, ghi rõ thuật toán để sau này đổi không mơ hồ.</summary>
    public const string Prefix = "sha256=";

    /// <summary>Chữ ký chuẩn của một thân gói: "sha256=" + hex thường của HMAC-SHA256.</summary>
    public static string Compute(string rawBody, string secret)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret ?? ""));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(rawBody ?? ""));
        return Prefix + Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>
    /// Đối chiếu chữ ký nhận được với chữ ký tính lại.
    /// </summary>
    public static bool Verify(string rawBody, string headerValue, string secret)
    {
        if (string.IsNullOrWhiteSpace(secret)) return false;
        if (string.IsNullOrWhiteSpace(headerValue)) return false;

        var expected = Compute(rawBody, secret);

        var received = headerValue.Trim();
        if (!received.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
            received = Prefix + received;

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(received.ToLowerInvariant()),
            Encoding.UTF8.GetBytes(expected));
    }

    public static string SecretFromEnvironment()
        => (Environment.GetEnvironmentVariable(SecretEnv) ?? "").Trim();
}
