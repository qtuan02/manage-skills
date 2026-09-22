using System;
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
/// R8 qua HTTP thật (PROJ-2374): action không tự giải vai trò nào — handler tra người giữ vai
/// trò ký của bước qua <see cref="IHisEmrClient.ListSignRoleEmployeesAsync"/> (api/GetCodeList?key=EmployeeRole)
/// và kiểm Người xác nhận (hoặc chính người bấm, nếu body rỗng) có nằm trong đó. Body là TÙY CHỌN.
/// </summary>
public class SignExamSectionEndpointTests : IClassFixture<AuthTestHost>
{
    private const string DivisionId = "DEV";
    private const string VariantCode = "KSK06-18T";
    private const int ItemGroupId = 101;
    private const long RoleId = 45;

    private readonly AuthTestHost _host;

    public SignExamSectionEndpointTests(AuthTestHost host)
    {
        _host = host;
    }

    private WebApplicationFactory<Program> BuildFactory(
        IHisEmrClient hisClient, ICertificateGateway certGateway, string databaseName)
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

                services.Replace(ServiceDescriptor.Singleton<IHisEmrClient>(hisClient));
                services.Replace(ServiceDescriptor.Singleton<ICertificateGateway>(certGateway));
            });
        });

    /// <summary>
    /// Nạp một đợt + hồ sơ + một bước ký của bảng map. Phải có ExamSession thật: quan hệ
    /// ExamRecord.Session là bắt buộc (SessionID không null) nên Include(x =&gt; x.Session) của
    /// ExamRecordRepository.GetAsync dịch thành INNER JOIN — thiếu đợt thì hồ sơ rơi khỏi kết
    /// quả một cách im lặng dù bản thân hồ sơ vẫn nằm trong bảng.
    /// </summary>
    private static async Task<Guid> SeedRecordWithSignStepMapAsync(WebApplicationFactory<Program> factory)
    {
        var recordId = Guid.NewGuid();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HealthExamDbContext>();

        var sessionId = Guid.NewGuid();
        db.ExamSessions.Add(new ExamSession
        {
            SessionID = sessionId,
            DivisionID = DivisionId,
            SessionCode = "DK-SIGN-EP-01",
            SessionName = "Đợt khám test endpoint",
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
            RecordCode = "REC-SIGN-EP-01",
            VariantCode = VariantCode
        });

        db.Set<SignStepMap>().Add(new SignStepMap
        {
            ID = Guid.NewGuid(),
            DivisionID = DivisionId,
            VariantCode = VariantCode,
            SWStep = 1,
            ItemGroupID = ItemGroupId,
            StepName = "Khám thể lực",
            SignTitle = "Khám thể lực",
            SWRoleID = RoleId,
            SignType = 1,
            SLType = 2,
            SearchPattern = "##{S1}##",
            IsConclusionStep = false,
            IsActive = true
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

    /// <summary>
    /// Vai trò ký đến từ HIS (api/M02F30000/GetPermissionGroup), không phải claim token.
    /// Ký thành công thì có đúng một dòng snapshot mang mã nhân viên của token.
    /// </summary>
    [Fact]
    public async Task Endpoint_lay_role_tu_HIS_va_ghi_snapshot()
    {
        var hisClient = FakeHisEmrClientForEndpoint.With(
            new SignRoleEmployee(AuthTestHost.EmployeeId, "NV001", "BS Token"));
        var certGateway = new FakeCertificateGateway();
        certGateway.WithCertificate.Add("NV001");

        await using var factory = BuildFactory(hisClient, certGateway, $"sign-section-endpoint-{Guid.NewGuid():N}");
        var recordId = await SeedRecordWithSignStepMapAsync(factory);

        var client = CreateEmployeeClient(factory);
        var response = await client.PostAsync($"/v1/exam-records/{recordId}/sections/{ItemGroupId}/sign", null);
        var body = JObject.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // Tên trường trên dây đúng như docs/api/conclusion-pdf-signing-guide.md — FE code theo đây.
        Assert.Equal(ItemGroupId, body["Data"]!["ItemGroupId"]!.Value<int>());
        Assert.Equal(1, body["Data"]!["SwStep"]!.Value<int>());
        Assert.Equal(AuthTestHost.EmployeeId, body["Data"]!["SignedByEmployeeID"]!.Value<long>());
        Assert.Equal(ExamRecordSignStepStatus.Signed, body["Data"]!["Status"]!.Value<string>());
        Assert.Equal(1, body["Data"]!["SigningProgressDone"]!.Value<int>());
        Assert.Equal(1, body["Data"]!["SigningProgressTotal"]!.Value<int>());
        Assert.Equal(AuthTestHost.EmployeeId, body["Data"]!["PerformedByEmployeeID"]!.Value<long>());

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HealthExamDbContext>();
        var snapshot = await db.ExamRecordSignSteps.SingleAsync(x => x.RecordID == recordId);
        Assert.Equal("NV001", snapshot.SignedByEmployeeCode);
        Assert.Equal(ItemGroupId, snapshot.ItemGroupID);
        Assert.Equal(ExamRecordSignStepStatus.Signed, snapshot.Status);
    }

    /// <summary>HIS không trả được vai trò ⇒ 502, và KHÔNG ghi snapshot nào.</summary>
    [Fact]
    public async Task Endpoint_tra_502_khi_HIS_khong_tra_duoc_role()
    {
        var hisClient = new FakeHisEmrClientForEndpoint(
            HisClientResult<System.Collections.Generic.IReadOnlyList<SignRoleEmployee>>.Fail(HisClientOutcome.BadGateway, "x"));
        var certGateway = new FakeCertificateGateway();
        certGateway.WithCertificate.Add("NV001");

        await using var factory = BuildFactory(hisClient, certGateway, $"sign-section-endpoint-{Guid.NewGuid():N}");
        var recordId = await SeedRecordWithSignStepMapAsync(factory);

        var client = CreateEmployeeClient(factory);
        var response = await client.PostAsync($"/v1/exam-records/{recordId}/sections/{ItemGroupId}/sign", null);
        var body = JObject.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Equal("x", body["Message"]!.Value<string>());

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HealthExamDbContext>();
        Assert.Empty(await db.ExamRecordSignSteps.Where(x => x.RecordID == recordId).ToListAsync());
    }

    /// <summary>Body JSON (FE 19/09): người ký = ConfirmedByEmployeeID, giờ ký = SignedAt, người thực hiện = token.</summary>
    [Fact]
    public async Task Endpoint_nhan_body_chon_nguoi_xac_nhan()
    {
        var hisClient = FakeHisEmrClientForEndpoint.With(
            new SignRoleEmployee(AuthTestHost.EmployeeId, "NV001", "BS Token"),
            new SignRoleEmployee(40, "40", "BS. Nguyễn Văn An"));
        var certGateway = new FakeCertificateGateway();
        certGateway.WithCertificate.Add("40");

        await using var factory = BuildFactory(hisClient, certGateway, $"sign-section-endpoint-{Guid.NewGuid():N}");
        var recordId = await SeedRecordWithSignStepMapAsync(factory);

        var client = CreateEmployeeClient(factory);
        var response = await client.PostAsync(
            $"/v1/exam-records/{recordId}/sections/{ItemGroupId}/sign",
            new StringContent("{\"ConfirmedByEmployeeID\":40,\"SignedAt\":\"2026-09-19T01:30:00.000Z\"}",
                System.Text.Encoding.UTF8, "application/json"));
        var body = JObject.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(40, body["Data"]!["SignedByEmployeeID"]!.Value<long>());
        Assert.Equal("BS. Nguyễn Văn An", body["Data"]!["SignedByEmployeeName"]!.Value<string>());
        Assert.Equal(AuthTestHost.EmployeeId, body["Data"]!["PerformedByEmployeeID"]!.Value<long>());
        Assert.Equal(new DateTime(2026, 9, 19, 1, 30, 0, DateTimeKind.Utc),
            body["Data"]!["SignedAt"]!.Value<DateTime>().ToUniversalTime());

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HealthExamDbContext>();
        var snapshot = await db.ExamRecordSignSteps.SingleAsync(x => x.RecordID == recordId);
        Assert.Equal("40", snapshot.SignedByEmployeeCode);
        Assert.Equal(40, snapshot.SignedByEmployeeID);
        Assert.Equal(AuthTestHost.EmployeeId, snapshot.PerformedByEmployeeID);
    }

    /// <summary>SignedAt kèm offset (+07:00, giờ VN) phải quy đổi đúng UTC trước khi lưu và trả về.</summary>
    [Fact]
    public async Task Endpoint_SignedAt_co_offset_thi_luu_va_tra_dung_gio_UTC()
    {
        var hisClient = FakeHisEmrClientForEndpoint.With(
            new SignRoleEmployee(AuthTestHost.EmployeeId, "NV001", "BS Token"),
            new SignRoleEmployee(40, "40", "BS. Nguyễn Văn An"));
        var certGateway = new FakeCertificateGateway();
        certGateway.WithCertificate.Add("40");

        await using var factory = BuildFactory(hisClient, certGateway, $"sign-section-endpoint-{Guid.NewGuid():N}");
        var recordId = await SeedRecordWithSignStepMapAsync(factory);

        var client = CreateEmployeeClient(factory);
        var response = await client.PostAsync(
            $"/v1/exam-records/{recordId}/sections/{ItemGroupId}/sign",
            new StringContent("{\"ConfirmedByEmployeeID\":40,\"SignedAt\":\"2026-09-19T08:30:00+07:00\"}",
                System.Text.Encoding.UTF8, "application/json"));
        var body = JObject.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(new DateTime(2026, 9, 19, 1, 30, 0, DateTimeKind.Utc),
            body["Data"]!["SignedAt"]!.Value<DateTime>().ToUniversalTime());

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HealthExamDbContext>();
        var snapshot = await db.ExamRecordSignSteps.SingleAsync(x => x.RecordID == recordId);
        Assert.Equal(new DateTime(2026, 9, 19, 1, 30, 0, DateTimeKind.Utc), snapshot.SignedAt!.Value.ToUniversalTime());
    }

    [Fact]
    public async Task Endpoint_tra_403_khi_nguoi_xac_nhan_khong_co_vai_tro()
    {
        var hisClient = FakeHisEmrClientForEndpoint.With(
            new SignRoleEmployee(AuthTestHost.EmployeeId, "NV001", "BS Token"));
        var certGateway = new FakeCertificateGateway();
        certGateway.WithCertificate.Add("NV001");

        await using var factory = BuildFactory(hisClient, certGateway, $"sign-section-endpoint-{Guid.NewGuid():N}");
        var recordId = await SeedRecordWithSignStepMapAsync(factory);

        var client = CreateEmployeeClient(factory);
        var response = await client.PostAsync(
            $"/v1/exam-records/{recordId}/sections/{ItemGroupId}/sign",
            new StringContent("{\"ConfirmedByEmployeeID\":9999}", System.Text.Encoding.UTF8, "application/json"));
        var body = JObject.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        // Chốt đúng nhánh 403 "Người xác nhận không có vai trò" — không phải nhánh ActorKind ở đầu handler.
        Assert.Contains("Người xác nhận", body["Message"]!.Value<string>());
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HealthExamDbContext>();
        Assert.Empty(await db.ExamRecordSignSteps.Where(x => x.RecordID == recordId).ToListAsync());
    }
}

/// <summary>
/// Chỉ nạp <see cref="IHisEmrClient.ListSignRoleEmployeesAsync"/> — mọi phương thức khác dùng
/// default interface implementation (báo NotFound), không endpoint nào trong bài test này chạm tới.
/// </summary>
internal sealed class FakeHisEmrClientForEndpoint : IHisEmrClient
{
    private readonly HisClientResult<System.Collections.Generic.IReadOnlyList<SignRoleEmployee>> _members;

    public FakeHisEmrClientForEndpoint(HisClientResult<System.Collections.Generic.IReadOnlyList<SignRoleEmployee>> members)
        => _members = members;

    public static FakeHisEmrClientForEndpoint With(params SignRoleEmployee[] members)
        => new(HisClientResult<System.Collections.Generic.IReadOnlyList<SignRoleEmployee>>.Success(members));

    public Task<HisClientResult<HisJsonDocument>> SendAsync(
        HisOperation operation, HisRequest request, CancellationToken ct = default)
        => Task.FromResult(HisClientResult<HisJsonDocument>.Fail(HisClientOutcome.NotFound, "Not implemented"));

    public Task<HisClientResult<System.Collections.Generic.IReadOnlyList<SignRoleEmployee>>> ListSignRoleEmployeesAsync(
        long roleId, HisCallContext context, CancellationToken ct = default)
        => Task.FromResult(_members);
}
