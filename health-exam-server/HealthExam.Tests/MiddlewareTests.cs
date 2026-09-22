using System.Net;
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
/// Gate hạ tầng: chốt chặn tenant và envelope.
/// KHÔNG cần DB — các endpoint ở đây không chạm tầng dữ liệu, và HEALTHEXAM_DB để trống thì
/// health check PostgreSQL tự bỏ qua.
///
/// Từ khi nối xác thực (H-1), request nghiệp vụ phải kèm token: các test dưới đây dùng
/// client của AuthTestHost thay vì CreateClient() trần. Gate 4010 nằm ở AuthGuardTests.
/// </summary>
public class MiddlewareTests : IClassFixture<AuthTestHost>
{
    private readonly AuthTestHost _host;

    public MiddlewareTests(AuthTestHost host) => _host = host;

    [Fact]
    public async Task Thieu_XDivisionId_tra_ve_4001()
    {
        var client = _host.CreateClient();

        var response = await client.GetAsync("/v1/meta/ping");
        var body = JObject.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(ErrorCodes.BadRequest, body["ErrorCode"]!.Value<int>());
        Assert.Equal("X-Division-Id", body["Data"]!["Errors"]![0]!["Field"]!.Value<string>());
    }

    [Fact]
    public async Task Co_XDivisionId_tra_ve_ErrorCode_0()
    {
        var client = _host.CreateEmployeeClient();
        client.DefaultRequestHeaders.Add(TestHeaders.Module, ModuleCodes.HealthExam);

        var response = await client.GetAsync("/v1/meta/ping");
        var body = JObject.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(ErrorCodes.Success, body["ErrorCode"]!.Value<int>());
        Assert.Equal("DEV", body["Data"]!["DivisionId"]!.Value<string>());
    }

    [Fact]
    public async Task Health_khong_can_header_tenant()
    {
        var client = _host.CreateClient();

        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Moi_phan_hoi_deu_mang_TraceID()
    {
        var failedClient = _host.CreateClient();
        var failed = await failedClient.GetAsync("/v1/meta/ping");
        var failedBody = JObject.Parse(await failed.Content.ReadAsStringAsync());

        var okClient = _host.CreateEmployeeClient();
        var ok = await okClient.GetAsync("/v1/meta/ping");
        var okBody = JObject.Parse(await ok.Content.ReadAsStringAsync());

        Assert.False(string.IsNullOrWhiteSpace(failedBody["TraceID"]!.Value<string>()));
        Assert.False(string.IsNullOrWhiteSpace(okBody["TraceID"]!.Value<string>()));
        Assert.True(failed.Headers.Contains("X-Trace-Id"));
    }

    [Fact]
    public async Task TraceID_gui_vao_duoc_giu_nguyen_de_doi_soat_log()
    {
        var client = _host.CreateEmployeeClient();
        client.DefaultRequestHeaders.Add(TestHeaders.Trace, "TRACE-FROM-GATEWAY");

        var response = await client.GetAsync("/v1/meta/ping");
        var body = JObject.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal("TRACE-FROM-GATEWAY", body["TraceID"]!.Value<string>());
    }

    /// <summary>
    /// Danh mục Nhóm khám đọc từ hằng số nên gọi được cả khi không có DB — đây cũng là
    /// bằng chứng nó không phụ thuộc seed.
    /// </summary>
    [Fact]
    public async Task Exam_groups_tra_du_10_nhom_khong_can_DB()
    {
        var client = _host.CreateEmployeeClient();

        var response = await client.GetAsync("/v1/exam-groups");
        var body = JObject.Parse(await response.Content.ReadAsStringAsync());
        var items = (JArray)body["Data"]!;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(10, items.Count);
        Assert.Equal("DTK_01", items[0]!["VariantCode"]!.Value<string>());
        Assert.Equal("KSK-V1-DTK_10", items[9]!["FormCode"]!.Value<string>());
    }
}
