using System;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HealthExam.Application.Integrations;
using HealthExam.Infrastructure.Integrations.SignServer;
using Xunit;

namespace HealthExam.Tests.Signing;

public class SignServerPdfSignerTests
{
    private sealed class CapturingHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly byte[] _body;
        public string CapturedForm { get; private set; }

        public CapturingHandler(HttpStatusCode status, byte[] body)
        {
            _status = status;
            _body = body;
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CapturedForm = await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(_status) { Content = new ByteArrayContent(_body) };
        }
    }

    private static readonly byte[] SignedPdf = Encoding.ASCII.GetBytes("%PDF-1.7 signed");
    private static readonly byte[] DraftPdf = Encoding.ASCII.GetBytes("%PDF-1.7 draft");

    private static SignServerOptions Options => new()
    {
        SsmBaseUrl = "http://ssm.internal",
        SignServerBaseUrl = "http://sign.internal",
        InternalSecret = "s3cr3t",
        TimeoutSeconds = 60
    };

    private static PdfSignRequest Request(byte[] pdf) => new(
        pdf,
        new EmployeeCertificate("NV001", "cn=bs-a", "VIS", "CCHN01", "1234", DateTime.MaxValue),
        "BS Nguyễn Văn A",
        new DateTime(2026, 9, 18, 9, 30, 0, DateTimeKind.Local),
        "Bác sĩ khám thể lực",
        SignType: 1,
        SignLocationType: 2,
        SearchPattern: "##{S1}##",
        Page: 0,
        PositionX: 0,
        PositionY: 0);

    /// <summary>
    /// Gửi 'file' mà KHÔNG gửi 'filePath' thì sign-server trả bytes PDF đã ký. Đó là điều kiện
    /// để nối chuỗi 8 chữ ký trong bộ nhớ, chỉ ghi MinIO một lần ở cuối.
    /// </summary>
    [Fact]
    public async Task Gui_file_khong_gui_filePath_thi_nhan_lai_bytes_da_ky()
    {
        var handler = new CapturingHandler(HttpStatusCode.OK, SignedPdf);
        var signer = new SignServerPdfSigner(new HttpClient(handler), Options);

        var res = await signer.SignAsync(Request(DraftPdf));

        Assert.True(res.Succeeded);
        Assert.Equal(SignedPdf, res.Pdf);
        Assert.DoesNotContain("name=\"filePath\"", handler.CapturedForm);
    }

    /// <summary>
    /// Ngày hiển thị cạnh chữ ký phải là lúc bác sĩ BẤM ký, định dạng yyyy-MM-dd HH:mm
    /// giống payload his-server vẫn gửi, chứ không phải lúc chạy vòng lặp ký.
    /// </summary>
    [Fact]
    public async Task Gui_dung_marker_va_ngay_hien_thi_cua_snapshot()
    {
        var handler = new CapturingHandler(HttpStatusCode.OK, SignedPdf);
        var signer = new SignServerPdfSigner(new HttpClient(handler), Options);

        await signer.SignAsync(Request(DraftPdf));

        Assert.Contains("##{S1}##", handler.CapturedForm);
        Assert.Contains("2026-09-18 09:30", handler.CapturedForm);
        Assert.Contains("cn=bs-a", handler.CapturedForm);
        Assert.Contains("NV001", handler.CapturedForm);
    }

    [Fact]
    public async Task Sign_server_loi_thi_tra_that_bai_va_giu_nguyen_pdf_cu()
    {
        var handler = new CapturingHandler(HttpStatusCode.BadRequest,
            Encoding.UTF8.GetBytes("không tìm thấy chuỗi ##{S1}##"));
        var signer = new SignServerPdfSigner(new HttpClient(handler), Options);

        var res = await signer.SignAsync(Request(DraftPdf));

        Assert.False(res.Succeeded);
        Assert.Null(res.Pdf);
        Assert.Contains("##{S1}##", res.Message);
    }

    /// <summary>Body lỗi của sign-server có thể là cả trang HTML — message trả về FE cắt ở 500 ký tự.</summary>
    [Fact]
    public async Task Body_loi_dai_thi_cat_con_500_ky_tu()
    {
        var handler = new CapturingHandler(HttpStatusCode.InternalServerError,
            Encoding.UTF8.GetBytes(new string('x', 5000)));
        var signer = new SignServerPdfSigner(new HttpClient(handler), Options);

        var res = await signer.SignAsync(Request(DraftPdf));

        Assert.False(res.Succeeded);
        Assert.True(res.Message.Length <= 500 + 1, $"message dài {res.Message.Length}");
    }

    /// <summary>
    /// sign-server trả 200 kèm JSON lỗi trong vài nhánh. Bytes không mở đầu bằng %PDF-
    /// thì coi là thất bại, tuyệt đối không nối tiếp vào lượt ký sau.
    /// </summary>
    [Fact]
    public async Task Body_khong_phai_pdf_thi_coi_la_that_bai()
    {
        var handler = new CapturingHandler(HttpStatusCode.OK,
            Encoding.UTF8.GetBytes("{\"ErrorCode\":1}"));
        var signer = new SignServerPdfSigner(new HttpClient(handler), Options);

        var res = await signer.SignAsync(Request(DraftPdf));

        Assert.False(res.Succeeded);
    }

    /// <summary>
    /// vi-VN dùng dấu phẩy làm dấu thập phân. Toạ độ ký gửi sang sign-server phải luôn
    /// dùng dấu chấm bất kể culture của thread đang chạy, nếu không sign-server đọc sai vị trí.
    /// </summary>
    [Fact]
    public async Task Toa_do_ky_dung_dau_cham_bat_ke_culture()
    {
        var original = Thread.CurrentThread.CurrentCulture;
        Thread.CurrentThread.CurrentCulture = new CultureInfo("vi-VN");
        try
        {
            var handler = new CapturingHandler(HttpStatusCode.OK, SignedPdf);
            var signer = new SignServerPdfSigner(new HttpClient(handler), Options);

            var request = Request(DraftPdf) with
            {
                PositionX = 1.5f,
                PositionY = 20.25f,
                SignLocationType = 1
            };

            await signer.SignAsync(request);

            Assert.Contains("1.5", handler.CapturedForm);
            Assert.Contains("20.25", handler.CapturedForm);
            Assert.DoesNotContain("1,5", handler.CapturedForm);
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = original;
        }
    }
}
