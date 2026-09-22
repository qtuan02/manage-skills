using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
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
/// Gate H-1: không có token hợp lệ thì không đi được vào nghiệp vụ.
///
/// Trước H-1 service chưa gắn UseAuthentication, nên MỌI request nặc danh đều lọt và mọi cột
/// audit ghi 0. Bộ test này chốt cả hai đầu: chặn đúng, và khi qua được thì danh tính đọc ra
/// đúng người (claim EmployeeID của IAM, không phải sub).
/// </summary>
public class AuthGuardTests : IClassFixture<AuthTestHost>
{
    private readonly AuthTestHost _host;

    public AuthGuardTests(AuthTestHost host) => _host = host;

    [Fact]
    public async Task Khong_kem_token_tra_ve_4010()
    {
        var client = _host.CreateAnonymousClient();

        var response = await client.GetAsync("/v1/meta/ping");
        var body = JObject.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(ErrorCodes.Unauthorized, body["ErrorCode"]!.Value<int>());
    }

    /// <summary>Phản hồi 4010 vẫn phải là envelope có TraceID, không phải 401 rỗng của framework.</summary>
    [Fact]
    public async Task Phan_hoi_4010_van_la_envelope_co_TraceID()
    {
        var client = _host.CreateAnonymousClient();

        var response = await client.GetAsync("/v1/exam-groups");
        var body = JObject.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(ErrorCodes.Unauthorized, body["ErrorCode"]!.Value<int>());
        Assert.False(string.IsNullOrWhiteSpace(body["TraceID"]!.Value<string>()));
    }

    [Fact]
    public async Task Token_ky_bang_khoa_khac_bi_tu_choi()
    {
        var client = _host.CreateAnonymousClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", AuthTestHost.TokenSignedWithWrongKey());

        var response = await client.GetAsync("/v1/meta/ping");
        var body = JObject.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(ErrorCodes.Unauthorized, body["ErrorCode"]!.Value<int>());
    }

    [Fact]
    public async Task Token_hop_le_thi_di_qua_duoc()
    {
        var client = _host.CreateEmployeeClient();

        var response = await client.GetAsync("/v1/meta/ping");
        var body = JObject.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(ErrorCodes.Success, body["ErrorCode"]!.Value<int>());
    }

    /// <summary>
    /// Đường hạ tầng vẫn mở: /health là thứ k8s gọi, nó không có token và không được phép
    /// vì thế mà báo pod chết.
    /// </summary>
    [Fact]
    public async Task Health_khong_can_token()
    {
        var response = await _host.CreateClient().GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>
    /// Token nội bộ SECRET_INTER đi được và mang danh tính Integration(4) — để dòng audit của
    /// đường gọi service-to-service phân biệt được với thao tác của nhân viên.
    /// </summary>
    [Fact]
    public async Task Token_noi_bo_SECRET_INTER_di_qua_voi_scope_service()
    {
        var client = _host.CreateAnonymousClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", AuthTestHost.ServiceToken);

        var response = await client.GetAsync("/v1/meta/ping");
        var body = JObject.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(Scopes.Service, body["Data"]!["Scope"]!.Value<string>());
    }

    /// <summary>
    /// ⚠️ Test then chốt của H-1: danh tính phải đọc ra từ claim EmployeeID của IAM.
    /// Bản cũ đọc ClaimTypes.NameIdentifier/sub — token IAM không có hai claim đó nên ActorId
    /// luôn 0 và mọi cột CreatedBy/RegisteredBy ghi 0 mà không hề báo lỗi.
    /// </summary>
    [Fact]
    public async Task Danh_tinh_doc_dung_EmployeeID_va_UserName()
    {
        var client = _host.CreateEmployeeClient();

        var response = await client.GetAsync("/v1/meta/whoami");
        var body = JObject.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(AuthTestHost.EmployeeId, body["Data"]!["ActorId"]!.Value<long>());
        Assert.Equal(AuthTestHost.UserName, body["Data"]!["ActorName"]!.Value<string>());
        Assert.Equal((short)ActorKind.Employee, body["Data"]!["ActorKind"]!.Value<short>());
    }

    [Fact]
    public async Task Anonymous_login_reaches_controller_instead_of_4010()
    {
        var response = await _host.CreateAnonymousClient().PostAsJsonAsync(
            "/v1/auth/login",
            new { Username = "doctor", Password = "wrong", ConfirmLogin = false });

        Assert.NotEqual(ErrorCodes.Unauthorized,
            JObject.Parse(await response.Content.ReadAsStringAsync())["ErrorCode"]!.Value<int>());
    }

    [Theory]
    [InlineData("/v1/auth/me", "GET")]
    [InlineData("/v1/auth/logout", "POST")]
    public async Task Other_auth_routes_are_not_anonymous(string path, string method)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        var response = await _host.CreateAnonymousClient().SendAsync(request);
        var body = JObject.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(ErrorCodes.Unauthorized, body["ErrorCode"]!.Value<int>());
    }

    [Fact]
    public async Task Anonymous_login_without_division_returns_4001()
    {
        var client = _host.CreateClient();
        var response = await client.PostAsJsonAsync(
            "/v1/auth/login",
            new { Username = "doctor", Password = "wrong", ConfirmLogin = false });

        var body = JObject.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(ErrorCodes.BadRequest, body["ErrorCode"]!.Value<int>());
    }
}
