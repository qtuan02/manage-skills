using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using HealthExam.Application.His;
using HealthExam.Application.Integrations;
using HealthExam.Domain.Common;
using HealthExam.Domain.ExamForms;
using HealthExam.Domain.ExamRecords;
using HealthExam.Domain.ExamSessions;
using HealthExam.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Newtonsoft.Json.Linq;
using Xunit;

namespace HealthExam.Tests.Signing;

/// <summary>
/// Hai endpoint kết luận qua HTTP thật. Spec §6.4: POST conclusion/sign có body RỖNG — FE gửi
/// <c>undefined</c> nên request không có Content-Type; controller không được khai tham số
/// [FromBody] vì [ApiController] sẽ trả 415 trước khi handler chạy. Spec §6.3: eligibility
/// chỉ đọc DB, HIS không trả được vai trò thì xuống cấp chứ không 502.
/// </summary>
public class ConclusionEndpointTests : IClassFixture<AuthTestHost>
{
    private const string DivisionId = "DEV";
    private const string VariantCode = "KSK06-18T";
    private const int ClinicalItemGroupId = 101;
    private const long ClinicalRoleId = 45;
    private const long ConclusionRoleId = 60;

    private readonly AuthTestHost _host;

    public ConclusionEndpointTests(AuthTestHost host)
    {
        _host = host;
    }

    private WebApplicationFactory<Program> BuildFactory(IHisEmrClient hisClient, string databaseName)
        => _host.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                var descriptors = services.Where(d =>
                    d.ServiceType == typeof(DbContextOptions<HealthExamDbContext>) ||
                    d.ServiceType == typeof(DbContextOptions) ||
                    d.ServiceType == typeof(HealthExamDbContext)).ToList();
                foreach (var d in descriptors)
                    services.Remove(d);

                services.AddDbContext<HealthExamDbContext>(options =>
                {
                    options.UseInMemoryDatabase(databaseName);
                    options.ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning));
                });

                var certGateway = new FakeCertificateGateway();
                certGateway.WithCertificate.Add("NV001");
                services.Replace(ServiceDescriptor.Singleton<IHisEmrClient>(hisClient));
                services.Replace(ServiceDescriptor.Singleton<ICertificateGateway>(certGateway));
            });
        });

    /// <summary>
    /// Một bước lâm sàng (chưa ký) + một bước kết luận. Không ký mục nào nên lệnh ký kết luận
    /// phải dừng ở "còn mục chưa ký" — trước khi render PDF, vì host test không có sign-server.
    /// </summary>
    private static async Task<Guid> SeedRecordWithTwoStepsAsync(WebApplicationFactory<Program> factory)
    {
        var recordId = Guid.NewGuid();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HealthExamDbContext>();

        var sessionId = Guid.NewGuid();
        db.ExamSessions.Add(new ExamSession
        {
            SessionID = sessionId,
            DivisionID = DivisionId,
            SessionCode = "DK-CONCL-EP-01",
            SessionName = "Đợt khám test endpoint kết luận",
            ExamDate = new DateOnly(2026, 9, 18),
            ExamPlace = "Test",
            VariantCode = VariantCode,
            State = ExamSessionState.Open
        });

        db.ExamRecords.Add(new ExamRecord
        {
            RecordID = recordId,
            DivisionID = DivisionId,
            SessionID = sessionId,
            RecordCode = "REC-CONCL-EP-01",
            VariantCode = VariantCode
        });

        db.Set<SignStepMap>().AddRange(
            new SignStepMap
            {
                ID = Guid.NewGuid(), DivisionID = DivisionId, VariantCode = VariantCode,
                SWStep = 1, ItemGroupID = ClinicalItemGroupId, StepName = "Khám thể lực",
                SignTitle = "Khám thể lực", SWRoleID = ClinicalRoleId, SignType = 1, SLType = 2,
                SearchPattern = "##{S1}##", IsConclusionStep = false, IsActive = true
            },
            new SignStepMap
            {
                ID = Guid.NewGuid(), DivisionID = DivisionId, VariantCode = VariantCode,
                SWStep = 2, ItemGroupID = null, StepName = "Kết luận",
                SignTitle = "Bác sĩ kết luận", SWRoleID = ConclusionRoleId, SignType = 1, SLType = 2,
                SearchPattern = "##{S2}##", IsConclusionStep = true, IsActive = true
            });

        await db.SaveChangesAsync();
        return recordId;
    }

    private static HttpClient CreateEmployeeClient(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestHeaders.Division, DivisionId);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", AuthTestHost.EmployeeToken());
        return client;
    }

    private static FakeHisEmrClientWithRoles HisWithRoles(params long[] roleIds)
        => new(HisClientResult<IReadOnlyList<long>>.Success(roleIds));

    private static FakeHisEmrClientWithRoles HisRolesFailing(string message = "HIS không phản hồi")
        => new(HisClientResult<IReadOnlyList<long>>.Fail(HisClientOutcome.BadGateway, message));

    /// <summary>
    /// POST không body, không Content-Type (đúng cách axios gửi <c>undefined</c>). Phải đi tới
    /// handler và nhận envelope 422 kèm danh sách bước thiếu — không phải 415 problem+json.
    /// </summary>
    [Fact]
    public async Task Sign_conclusion_khong_body_khong_content_type_van_toi_handler()
    {
        await using var factory = BuildFactory(HisWithRoles(ClinicalRoleId, ConclusionRoleId),
            $"conclusion-endpoint-{Guid.NewGuid():N}");
        var recordId = await SeedRecordWithTwoStepsAsync(factory);
        var client = CreateEmployeeClient(factory);

        var response = await client.PostAsync($"/v1/exam-records/{recordId}/conclusion/sign", content: null);
        var raw = await response.Content.ReadAsStringAsync();

        Assert.NotEqual(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);

        var body = JObject.Parse(raw);
        Assert.Equal(4221, body["ErrorCode"]!.Value<int>());
        Assert.Equal(new[] { 1 }, body["Data"]!["MissingSteps"]!.Values<int>().ToArray());
    }

    /// <summary>HIS không trả được vai trò → ký kết luận vẫn phải fail-closed 502, giữ nguyên message.</summary>
    [Fact]
    public async Task Sign_conclusion_tra_502_khi_HIS_khong_tra_duoc_role()
    {
        await using var factory = BuildFactory(HisRolesFailing("x"), $"conclusion-endpoint-{Guid.NewGuid():N}");
        var recordId = await SeedRecordWithTwoStepsAsync(factory);
        var client = CreateEmployeeClient(factory);

        var response = await client.PostAsync($"/v1/exam-records/{recordId}/conclusion/sign", content: null);
        var body = JObject.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Equal("x", body["Message"]!.Value<string>());
    }

    /// <summary>
    /// Spec §6.3: eligibility chỉ đọc DB. HIS không trả được vai trò → vẫn 200, chỉ
    /// <c>CanSignConclusion=false</c>; <c>Steps</c> vẫn đầy đủ để FE vẽ tiến độ.
    /// </summary>
    [Fact]
    public async Task Eligibility_xuong_cap_khi_HIS_khong_tra_duoc_role()
    {
        await using var factory = BuildFactory(HisRolesFailing(), $"conclusion-endpoint-{Guid.NewGuid():N}");
        var recordId = await SeedRecordWithTwoStepsAsync(factory);
        var client = CreateEmployeeClient(factory);

        var response = await client.GetAsync($"/v1/exam-records/{recordId}/conclusion-eligibility");
        var body = JObject.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(0, body["ErrorCode"]!.Value<int>());
        var data = body["Data"]!;
        Assert.Equal(recordId.ToString(), data["ProfileID"]!.Value<string>());
        Assert.False(data["CanSignConclusion"]!.Value<bool>());
        Assert.Equal(2, data["Steps"]!.Count());
        Assert.Equal(new[] { 1 }, data["MissingSteps"]!.Values<int>().ToArray());
    }

    [Fact]
    public async Task Eligibility_tra_200_khi_HIS_tra_duoc_role()
    {
        await using var factory = BuildFactory(HisWithRoles(ClinicalRoleId, ConclusionRoleId),
            $"conclusion-endpoint-{Guid.NewGuid():N}");
        var recordId = await SeedRecordWithTwoStepsAsync(factory);
        var client = CreateEmployeeClient(factory);

        var response = await client.GetAsync($"/v1/exam-records/{recordId}/conclusion-eligibility");
        var body = JObject.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var data = body["Data"]!;
        Assert.Equal(2, data["Conditions"]!.Count());
        Assert.Equal("A", data["Conditions"]![0]!["Code"]!.Value<string>());
        Assert.Equal(1, data["Steps"]![0]!["SwStep"]!.Value<int>());
        Assert.Equal(ClinicalItemGroupId, data["Steps"]![0]!["ItemGroupID"]!.Value<int>());
        Assert.Equal(ConclusionRoleId, data["Steps"]![1]!["SwRoleId"]!.Value<long>());
    }

    [Fact]
    public async Task CancelConclusionSign_endpoint_hoat_dong_khi_goi_POST_khong_body()
    {
        await using var factory = BuildFactory(HisWithRoles(ClinicalRoleId, ConclusionRoleId),
            $"conclusion-endpoint-{Guid.NewGuid():N}");
        var recordId = await SeedRecordWithTwoStepsAsync(factory);

        // Đặt record ở trạng thái Signed bởi nhân viên trong token (EmployeeToken của AuthTestHost có ActorId tương ứng)
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HealthExamDbContext>();
            var record = await db.ExamRecords.Include(r => r.SignSteps).FirstAsync(r => r.RecordID == recordId);
            record.State = ExamRecordState.Completed;
            record.SignStatus = ExamRecordSignStatus.Signed;
            record.HisSignedByEmployeeID = AuthTestHost.EmployeeId;
            record.HisSignedAt = DateTime.UtcNow;
            record.SignedFilePath = "minio/path.pdf";
            await db.SaveChangesAsync();
        }

        var client = CreateEmployeeClient(factory);
        var response = await client.PostAsync($"/v1/exam-records/{recordId}/conclusion/sign/cancel", content: null);
        var body = JObject.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(0, body["ErrorCode"]!.Value<int>());
        var data = body["Data"]!;
        Assert.Equal(recordId.ToString(), data["RecordID"]!.Value<string>());
        Assert.Equal(ExamRecordSignStatus.New, data["Status"]!.Value<string>());
        Assert.Null(data["SignedFilePath"]!.Value<string>());
    }
}

/// <summary>
/// Vai trò ký của nhân viên hiện tại — dùng bởi SignConclusion/ConclusionEligibility qua
/// <see cref="IHisEmrClient.ListEmployeeSignRoleIdsAsync"/>. Tách khỏi
/// <c>FakeHisEmrClientForEndpoint</c> (PROJ-2374, xem SignExamSectionEndpointTests) vì lớp đó
/// giờ chỉ mô phỏng <see cref="IHisEmrClient.ListSignRoleEmployeesAsync"/> cho luồng ký mục khám.
/// </summary>
internal sealed class FakeHisEmrClientWithRoles : IHisEmrClient
{
    private readonly HisClientResult<IReadOnlyList<long>> _rolesResult;

    public FakeHisEmrClientWithRoles(HisClientResult<IReadOnlyList<long>> rolesResult)
        => _rolesResult = rolesResult;

    public Task<HisClientResult<HisJsonDocument>> SendAsync(
        HisOperation operation, HisRequest request, CancellationToken ct = default)
        => Task.FromResult(HisClientResult<HisJsonDocument>.Fail(HisClientOutcome.NotFound, "Not implemented"));

    public Task<HisClientResult<IReadOnlyList<long>>> ListEmployeeSignRoleIdsAsync(
        long employeeId, HisCallContext context, CancellationToken ct = default)
        => Task.FromResult(_rolesResult);
}
