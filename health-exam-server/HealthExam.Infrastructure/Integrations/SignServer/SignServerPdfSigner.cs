using System;
using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HealthExam.Application.Integrations;

namespace HealthExam.Infrastructure.Integrations.SignServer;

/// <summary>
/// Gọi sign-server POST api/SIGN/Sign. Gửi 'file' và KHÔNG gửi 'filePath' nên sign-server
/// trả về bytes PDF đã ký thay vì ghi MinIO — cho phép nối chuỗi nhiều chữ ký trong bộ nhớ.
/// sign-server dùng UseAppendMode() nên chữ ký trước được giữ nguyên và thứ tự ký không quan trọng.
/// </summary>
public class SignServerPdfSigner : IPdfSigner
{
    private readonly HttpClient _http;
    private readonly SignServerOptions _options;

    public SignServerPdfSigner(HttpClient http, SignServerOptions options)
    {
        _http = http;
        _options = options;
    }

    public async Task<PdfSignOutcome> SignAsync(PdfSignRequest request, CancellationToken ct = default)
    {
        if (request.Pdf == null || request.Pdf.Length < 5)
            return new PdfSignOutcome(false, null, "Không có dữ liệu PDF để ký");

        var uri = $"{_options.SignServerBaseUrl.TrimEnd('/')}/api/SIGN/Sign";
        using var form = new MultipartFormDataContent();
        var pdfContent = new ByteArrayContent(request.Pdf);
        pdfContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/pdf");
        form.Add(pdfContent, "file", "health-exam.pdf");

        void Add(string name, string value) => form.Add(new StringContent(value ?? "", Encoding.UTF8), name);

        Add("company", request.Certificate.Company);
        Add("certName", request.Certificate.CertName);
        Add("copCode", request.Certificate.CoPCode);
        Add("pin", request.Certificate.Pin);
        Add("empCode", request.Certificate.EmployeeCode);
        Add("name", request.EmployeeName);
        Add("title", request.SignTitle);
        Add("date", request.DisplayDate.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture));
        Add("signType", request.SignType.ToString());
        Add("signLocationType", request.SignLocationType.ToString());
        Add("searchPattern", request.SearchPattern);
        Add("mode", "0");
        Add("page", request.Page.ToString());
        Add("positionX", request.PositionX.ToString(CultureInfo.InvariantCulture));
        Add("positionY", request.PositionY.ToString(CultureInfo.InvariantCulture));

        HttpResponseMessage response;
        try
        {
            response = await _http.PostAsync(uri, form, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return new PdfSignOutcome(false, null, "Không kết nối được dịch vụ ký số");
        }

        var bytes = await response.Content.ReadAsByteArrayAsync(ct);

        if (!response.IsSuccessStatusCode)
            return new PdfSignOutcome(false, null, Truncate(Encoding.UTF8.GetString(bytes), 500));

        var isPdf = bytes.Length >= 5 && bytes[0] == '%' && bytes[1] == 'P'
                    && bytes[2] == 'D' && bytes[3] == 'F' && bytes[4] == '-';
        if (!isPdf)
            return new PdfSignOutcome(false, null, "Dịch vụ ký số không trả về PDF hợp lệ");

        return new PdfSignOutcome(true, bytes, "");
    }

    /// <summary>Body lỗi của sign-server có thể là cả trang HTML — không đẩy nguyên lên FE.</summary>
    private static string Truncate(string value, int max)
        => value.Length <= max ? value : value.Substring(0, max) + "…";
}
