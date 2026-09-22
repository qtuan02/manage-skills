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
using Newtonsoft.Json.Linq;
using Xunit;

namespace HealthExam.Tests;

/// <summary>
/// H-3b — chốt chặn của đường gọi nội bộ, kiểm qua HTTP thật.
///
/// Vì sao phải đi qua HTTP chứ không gọi thẳng service: thứ cần chốt ở đây là AI ĐƯỢC VÀO,
/// mà câu trả lời đó do middleware + controller quyết định. Gọi thẳng PortalCredentialService
/// là bỏ qua đúng phần đang kiểm.
///
/// Các test này CỐ Ý chỉ dùng request không chạm DB (thiếu trường → 4001 ném trước mọi truy
/// vấn): host test không có PostgreSQL, nên chốt chặn phải chặn được TRƯỚC khi cần dữ liệu.
/// Phần đối chiếu dữ liệu do PortalCredentialTests chốt trên DB trong bộ nhớ.
/// </summary>
public class InternalEndpointTests : IClassFixture<AuthTestHost>
{
    private const string Path = "/v1/internal/verify-portal-credentials";

    private readonly AuthTestHost _host;

    public InternalEndpointTests(AuthTestHost host) => _host = host;

    /// <summary>Body đủ hình dạng — dùng cho các test chỉ quan tâm chốt chặn xác thực.</summary>
    private static StringContent Body() => new(
        """{"PatientCode":"NB0001","IdentityNumber":"079201000123","InsuranceNumber":""}""",
        Encoding.UTF8, "application/json");

    [Fact]
    public async Task Khong_kem_token_tra_ve_4010()
    {
        var response = await _host.CreateAnonymousClient().PostAsync(Path, Body());
        var body = JObject.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(ErrorCodes.Unauthorized, body["ErrorCode"]!.Value<int>());
    }

    /// <summary>
    /// Bí mật sai là chuỗi thô, KHÔNG phải JWT — JwtBearer không nhận ra nó, nhánh
    /// TryAttachServiceIdentity phải là chỗ từ chối. Gate của brief: secret sai → 4010.
    /// </summary>
    [Fact]
    public async Task Secret_sai_tra_ve_4010()
    {
        var client = _host.CreateAnonymousClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", "khong-phai-secret-that");

        var response = await client.PostAsync(Path, Body());
        var body = JObject.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(ErrorCodes.Unauthorized, body["ErrorCode"]!.Value<int>());
    }

    /// <summary>
    /// ★ Token NHÂN VIÊN hợp lệ vẫn không vào được. Endpoint này tra định danh theo Mã NB;
    /// mở cho mọi tài khoản nhân viên là mở một đường dò dữ liệu người bệnh không đi qua màn
    /// hình có phân quyền nào. AuthGuardMiddleware nhận token nhân viên, nên chốt chặn phải
    /// nằm ở controller — đây là test canh cho việc đó không bị gỡ đi.
    /// </summary>
    [Fact]
    public async Task Token_nhan_vien_bi_chan_bang_4030()
    {
        var response = await _host.CreateEmployeeClient().PostAsync(Path, Body());
        var body = JObject.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(ErrorCodes.Forbidden, body["ErrorCode"]!.Value<int>());
    }

    /// <summary>Thiếu tenant vẫn là 4001 — đường nội bộ không được miễn chốt chặn DivisionID.</summary>
    [Fact]
    public async Task Thieu_X_Division_Id_tra_ve_4001()
    {
        var client = _host.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", AuthTestHost.ServiceToken);

        var response = await client.PostAsync(Path, Body());
        var body = JObject.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(ErrorCodes.BadRequest, body["ErrorCode"]!.Value<int>());
    }

    /// <summary>
    /// Token service đi qua được chốt chặn: dừng ở 4001 vì thiếu dữ kiện, KHÔNG phải 4010/4030.
    /// Chốt điều này để "cả bốn test trên đều đỏ đúng" không thể do endpoint đơn giản là chặn tất.
    /// </summary>
    [Fact]
    public async Task Token_service_di_qua_duoc_chot_chan_va_dung_o_4001()
    {
        var client = _host.CreateServiceClient();
        var thieuDuKien = new StringContent(
            """{"PatientCode":"NB0001"}""", Encoding.UTF8, "application/json");

        var response = await client.PostAsync(Path, thieuDuKien);
        var body = JObject.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(ErrorCodes.BadRequest, body["ErrorCode"]!.Value<int>());
        Assert.False(string.IsNullOrWhiteSpace(body["TraceID"]!.Value<string>()));
    }
}
