using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using HealthExam.Application.Integrations;
using HealthExam.Infrastructure.Integrations.SignServer;
using Newtonsoft.Json.Linq;

namespace HealthExam.Infrastructure.Integrations.Ssm;

/// <summary>
/// Gọi thẳng ssm-server thay cho endpoint kiểm tra chứng thư cũ của his-server (đã bỏ ở
/// Task 11). Đường này chính là đường his-server vẫn dùng: URL_SSM_API/api/SSM/SignInfo +
/// Bearer SECRET_INTER.
/// </summary>
public class SsmCertificateGateway : ICertificateGateway
{
    private readonly HttpClient _http;
    private readonly SignServerOptions _options;

    public SsmCertificateGateway(HttpClient http, SignServerOptions options)
    {
        _http = http;
        _options = options;
    }

    public async Task<CertificateResult> GetAsync(string employeeCode, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(employeeCode))
            return new CertificateResult(false, null, "Thiếu mã nhân viên");

        var uri = $"{_options.SsmBaseUrl.TrimEnd('/')}/api/SSM/SignInfo?userCode={Uri.EscapeDataString(employeeCode)}";
        using var message = new HttpRequestMessage(HttpMethod.Get, uri);
        message.Headers.TryAddWithoutValidation("Authorization", $"Bearer {_options.InternalSecret}");

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(message, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return new CertificateResult(false, null, "Không kết nối được dịch vụ chứng thư số");
        }

        // KHÔNG log body: nó chứa PIN.
        if ((int)response.StatusCode >= 500)
            return new CertificateResult(false, null, "Dịch vụ chứng thư số lỗi");
        if (!response.IsSuccessStatusCode)
            return new CertificateResult(false, null, "Nhân viên chưa có chứng thư số");

        var raw = await response.Content.ReadAsStringAsync(ct);
        JToken node;
        try { node = JToken.Parse(raw); }
        catch { return new CertificateResult(false, null, "Dịch vụ chứng thư số trả dữ liệu không hợp lệ"); }

        var data = node["Data"] ?? (node is JArray arr && arr.Count > 0 ? arr[0] : node);
        var certName = data?.Value<string>("CertName");
        if (string.IsNullOrWhiteSpace(certName))
            return new CertificateResult(false, null, "Nhân viên chưa có chứng thư số");

        DateTime validUntil;
        try { validUntil = data.Value<DateTime?>("Valid") ?? DateTime.MaxValue; }
        catch (Exception ex) when (ex is FormatException or InvalidCastException or ArgumentException)
        {
            return new CertificateResult(false, null, "Chứng thư số có ngày hết hạn không hợp lệ");
        }
        if (validUntil < DateTime.Now)
            return new CertificateResult(false, null, "Chứng thư số đã hết hạn");

        return new CertificateResult(true, new EmployeeCertificate(
            employeeCode,
            certName,
            data.Value<string>("Company") ?? "",
            data.Value<string>("CoPCode") ?? "",
            data.Value<string>("Pin") ?? "",
            validUntil), "");
    }
}
