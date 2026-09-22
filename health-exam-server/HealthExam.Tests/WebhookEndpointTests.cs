using System.Net;
using System.Net.Http.Headers;
using System.Text;
using HealthExam.API.Contracts;
using HealthExam.API.Middlewares;
using HealthExam.Application.Common;
using HealthExam.Domain.Common;
using HealthExam.Domain.ExamSessions;
using HealthExam.Domain.ExamRecords;
using HealthExam.Domain.Imports;
using HealthExam.Domain.Paraclinical;
using HealthExam.Domain.Webhooks;
using HealthExam.Domain.Integrations;
using HealthExam.Domain.Catalogs;
using HealthExam.Server.Service;
using Newtonsoft.Json.Linq;
using Xunit;

namespace HealthExam.Tests;

/// <summary>
/// H2-04 — chốt chặn của điểm nhận webhook, kiểm qua HTTP thật.
///
/// Phải đi qua HTTP: thứ cần chốt là AI ĐƯỢC VÀO, mà câu trả lời do middleware quyết định.
/// Gọi thẳng WebhookIngestService là bỏ qua đúng phần đang kiểm.
///
/// Các test này CỐ Ý chỉ dùng request bị chặn TRƯỚC khi chạm DB: host test không có
/// PostgreSQL. Nhánh chấp nhận (ghi hộp thư, trả Duplicated) do WebhookIngestTests chốt trên
/// DB trong bộ nhớ, và đo lại bằng curl trên PostgreSQL thật.
/// </summary>
public class WebhookEndpointTests : IClassFixture<AuthTestHost>
{
    private const string Path = "/v1/hooks/form-server";

    private const string Body = """
                                {"EventID":"EVT-HTTP-0001","Event":"submission.section.signed",
                                 "OccurredAt":"2026-08-24T03:00:00Z","HostRefID":"DK-2026-001-0001",
                                 "Data":{"SectionKind":"HISTORY"}}
                                """;

    private readonly AuthTestHost _host;

    public WebhookEndpointTests(AuthTestHost host) => _host = host;

    private static StringContent Content() => new(Body, Encoding.UTF8, "application/json");

    /// <summary>
    /// Gate 03-task H2-04: sai chữ ký → 4030. Không phải 4010: bên gọi ĐÃ tự xưng danh, cái
    /// sai là gói không đáng tin. Và 403 thì bên phát không retry vô ích.
    /// </summary>
    [Fact]
    public async Task Chu_ky_sai_tra_ve_4030()
    {
        var client = _host.CreateAnonymousClient();
        client.DefaultRequestHeaders.Add(WebhookSignature.HeaderName, "sha256=00deadbeef");

        var response = await client.PostAsync(Path, Content());
        var body = JObject.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(ErrorCodes.Forbidden, body["ErrorCode"]!.Value<int>());
    }

    /// <summary>Không có chữ ký thì cũng 4030 — và câu thông báo KHÔNG nói sai vế nào.</summary>
    [Fact]
    public async Task Khong_co_chu_ky_tra_ve_4030_va_khong_noi_sai_ve_nao()
    {
        var response = await _host.CreateAnonymousClient().PostAsync(Path, Content());
        var body = JObject.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(ErrorCodes.Forbidden, body["ErrorCode"]!.Value<int>());

        var message = body["Message"]!.Value<string>();
        Assert.DoesNotContain("thiếu", message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("header", message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Token nhân viên hợp lệ KHÔNG thay được chữ ký. Endpoint này đẩy được hồ sơ sang
    /// "Đã khám" — mở cho mọi tài khoản nhân viên là mở một đường đổi trạng thái hồ sơ không
    /// đi qua màn hình có phân quyền nào.
    /// </summary>
    [Fact]
    public async Task Token_nhan_vien_hop_le_van_bi_chan_o_diem_nhan_webhook()
    {
        var client = _host.CreateAnonymousClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", AuthTestHost.EmployeeToken());

        var response = await client.PostAsync(Path, Content());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>
    /// SECRET_INTER dùng chung cũng không đi thẳng được: chữ ký ràng vào NGUYÊN VĂN thân gói,
    /// còn một Bearer token đúng vẫn cho phép sửa SubjectID trên đường đi.
    /// </summary>
    [Fact]
    public async Task Token_service_khong_thay_duoc_chu_ky()
    {
        var response = await _host.CreateServiceClient().PostAsync(Path, Content());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>
    /// Chữ ký đúng của thân gói KHÁC thì không dùng lại được cho thân gói này — nếu không thì
    /// bắt được một gói hợp lệ là phát lại được với nội dung tuỳ ý.
    /// </summary>
    [Fact]
    public async Task Chu_ky_cua_goi_khac_khong_dung_lai_duoc()
    {
        var client = _host.CreateAnonymousClient();
        client.DefaultRequestHeaders.Add(
            WebhookSignature.HeaderName,
            WebhookSignature.Compute("""{"EventID":"GOI-KHAC"}""", AuthTestHost.ServiceToken));

        var response = await client.PostAsync(Path, Content());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>
    /// Thiếu X-Division-Id vẫn dừng ở 4001 TRƯỚC khi tới phép kiểm chữ ký: tenant của phiếu
    /// là thứ phải có trước khi tra bất cứ gì, và thứ tự cố định thì thông báo lỗi mới đoán
    /// trước được.
    /// </summary>
    [Fact]
    public async Task Thieu_X_Division_Id_tra_ve_4001()
    {
        var client = _host.CreateClient();
        client.DefaultRequestHeaders.Add(
            WebhookSignature.HeaderName, WebhookSignature.Compute(Body, AuthTestHost.ServiceToken));

        var response = await client.PostAsync(Path, Content());
        var body = JObject.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(ErrorCodes.BadRequest, body["ErrorCode"]!.Value<int>());
    }
}

/// <summary>Phép tính chữ ký — hợp đồng mà cụm 236 phải hiện thực ở bên phát.</summary>
public class WebhookSignatureTests
{
    private const string Body = """{"EventID":"EVT-1","Event":"submission.section.signed"}""";
    private const string Secret = "bi-mat-dung-chung";

    [Fact]
    public void Chu_ky_dung_dinh_dang_sha256_hex_thuong()
    {
        var signature = WebhookSignature.Compute(Body, Secret);

        Assert.StartsWith("sha256=", signature);
        Assert.Equal(64, signature["sha256=".Length..].Length);
        Assert.Equal(signature.ToLowerInvariant(), signature);
        Assert.True(WebhookSignature.Verify(Body, signature, Secret));
    }

    /// <summary>Nhận cả dạng không tiền tố — bên phát chưa tồn tại, đây còn là hợp đồng đang mô tả.</summary>
    [Fact]
    public void Chap_nhan_ca_chu_ky_khong_kem_tien_to()
    {
        var bare = WebhookSignature.Compute(Body, Secret)["sha256=".Length..];

        Assert.True(WebhookSignature.Verify(Body, bare, Secret));
    }

    [Fact]
    public void Doi_mot_ky_tu_trong_than_goi_la_chu_ky_khong_con_dung()
    {
        var signature = WebhookSignature.Compute(Body, Secret);

        Assert.False(WebhookSignature.Verify(Body.Replace("EVT-1", "EVT-2"), signature, Secret));
    }

    /// <summary>
    /// Bí mật rỗng LUÔN trả false. Không có nhánh "chưa cấu hình thì cho qua": nhánh đó biến
    /// một lần quên đặt biến môi trường thành một endpoint mở cho cả thiên hạ, mà triệu chứng
    /// là mọi thứ vẫn chạy.
    /// </summary>
    [Fact]
    public void Bi_mat_rong_thi_khong_chu_ky_nao_hop_le()
    {
        Assert.False(WebhookSignature.Verify(Body, WebhookSignature.Compute(Body, ""), ""));
        Assert.False(WebhookSignature.Verify(Body, "sha256=bat-ky", ""));
    }
}
