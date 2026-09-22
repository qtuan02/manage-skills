using System.Net;
using System.Text;
using HealthExam.Domain.Common;
using HealthExam.Domain.ExamSessions;
using HealthExam.Domain.ExamRecords;
using HealthExam.Domain.Imports;
using HealthExam.Domain.Paraclinical;
using HealthExam.Domain.Webhooks;
using HealthExam.Domain.Integrations;
using HealthExam.Domain.Catalogs;
using HealthExam.Domain.Patients;
using HealthExam.Application.ExamRecords;
using HealthExam.Application.Integrations;
using HealthExam.API.Contracts;
using HealthExam.API.Middlewares;
using HealthExam.Application.Common;
using HealthExam.Infrastructure.Integrations.FormServer;
using HealthExam.Server.Service;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;
using Xunit;

namespace HealthExam.Tests;

/// <summary>
/// Cầu nối sang form-server (H-3).
///
/// KHÔNG chạm form-server thật và KHÔNG chạm DB: phần dễ sai của việc này là hợp đồng —
/// đúng 4 khoá ContextValues, ngày ở dạng yyyy-MM-dd, effectiveOn lấy theo ngày khám của
/// ĐỢT chứ không phải hôm nay, và lỗi mạng phải thành 5020 chứ không thành dữ liệu rỗng.
/// Tất cả đều kiểm được bằng handler HTTP giả, nên test không phụ thuộc mạng hay môi trường.
/// </summary>
public class FormDraftTests
{
    private static readonly Guid FormId = Guid.Parse("11111111-2222-3333-4444-555555555555");

    // ---------------------------------------------------------------- ContextValues

    [Fact]
    public void ContextValues_day_dung_4_khoa_cua_dot_kham()
    {
        var session = Session();
        var record = Record();

        var values = GetExamFormDraftHandler.BuildContextValues(session, record);

        var provider = values[FormContextKeys.SessionProvider];
        Assert.Equal(
            new[] { "SessionCode", "OrganizationName", "ExamDate", "PackageName" }.OrderBy(x => x),
            provider.Keys.OrderBy(x => x));
        Assert.Equal("KSK-2026-001", provider[FormContextKeys.SessionCode]);
        Assert.Equal("Công ty TNHH ABC", provider[FormContextKeys.OrganizationName]);
        Assert.Equal("2026-01-15", provider[FormContextKeys.ExamDate]);
    }

    /// <summary>
    /// Khoá InheritSourceID của dòng seed (HEALTH_EXAM_SESSION_CODE…) KHÔNG được xuất hiện
    /// trong payload. Gửi nhầm thì form-server im lặng prefill rỗng, không báo lỗi — nên
    /// chốt bằng test chứ không trông vào mắt người review.
    /// </summary>
    [Fact]
    public void ContextValues_khong_dung_ma_InheritSourceID_cua_seed()
    {
        var values = GetExamFormDraftHandler.BuildContextValues(Session(), Record());

        var provider = values[FormContextKeys.SessionProvider];
        Assert.False(provider.ContainsKey("HEALTH_EXAM_SESSION_CODE"));
        Assert.False(provider.ContainsKey("HEALTH_EXAM_ORGANIZATION_NAME"));
        Assert.False(provider.ContainsKey("HEALTH_EXAM_DATE"));
        Assert.False(provider.ContainsKey("HEALTH_EXAM_PACKAGE_NAME"));
    }

    /// <summary>Đợt chưa có tên đơn vị/gói khám vẫn phải gửi đủ khoá — xem BuildContextValues.</summary>
    [Fact]
    public void ContextValues_van_du_4_khoa_khi_dot_con_thieu_du_lieu()
    {
        var session = Session();
        session.OrganizationName = "";
        session.PackageName = "";
        var record = Record();
        record.PackageName = "";

        var provider = GetExamFormDraftHandler.BuildContextValues(session, record)[FormContextKeys.SessionProvider];

        Assert.Equal(4, provider.Count);
        Assert.Equal("", provider[FormContextKeys.OrganizationName]);
        Assert.Equal("", provider[FormContextKeys.PackageName]);
    }

    [Fact]
    public void ContextValues_uu_tien_goi_kham_rieng_cua_ho_so()
    {
        var session = Session();
        session.PackageName = "Gói cơ bản của đợt";
        var record = Record();
        record.PackageName = "Gói nâng cao của người này";

        var provider = GetExamFormDraftHandler.BuildContextValues(session, record)[FormContextKeys.SessionProvider];

        Assert.Equal("Gói nâng cao của người này", provider[FormContextKeys.PackageName]);
    }

    [Fact]
    public void ContextValues_them_PATIENT_khi_co_du_lieu_va_bo_qua_khi_khong()
    {
        var withPatient = GetExamFormDraftHandler.BuildContextValues(Session(), Record());
        Assert.Equal("Nguyễn Văn A", withPatient[FormContextKeys.PatientProvider][FormContextKeys.FullName]);
        Assert.Equal("1990-03-02", withPatient[FormContextKeys.PatientProvider][FormContextKeys.Dob]);

        var empty = Record();
        empty.Patient!.FullName = "";
        empty.Patient!.Dob = null;
        Assert.False(GetExamFormDraftHandler.BuildContextValues(Session(), empty)
            .ContainsKey(FormContextKeys.PatientProvider));
    }

    // ---------------------------------------------------------------- effectiveOn / hostRefId

    /// <summary>BR-08: nhập bổ sung đợt cũ phải ra bộ mẫu đang hiệu lực LÚC ĐÓ.</summary>
    [Fact]
    public void EffectiveOn_lay_ngay_kham_cua_dot_chu_khong_phai_hom_nay()
    {
        var session = Session();
        session.ExamDate = new DateOnly(2025, 6, 30);

        var effectiveOn = GetExamFormDraftHandler.EffectiveOnOf(session);

        Assert.Equal(new DateOnly(2025, 6, 30), effectiveOn);
        Assert.NotEqual(DateOnly.FromDateTime(DateTime.Now), effectiveOn);
    }

    /// <summary>Hôm nay là SessionCode; đổi sang RecordCode là thay đổi phối hợp hai bên.</summary>
    [Fact]
    public void HostRefId_hien_dang_la_ma_dot_kham()
    {
        var session = Session();
        var record = Record();

        Assert.Equal(session.SessionCode, GetExamFormDraftHandler.HostRefIdOf(session, record));
        Assert.NotEqual(record.RecordCode, GetExamFormDraftHandler.HostRefIdOf(session, record));
        Assert.Equal("HEALTH_EXAM_SESSION", ModuleCodes.HostRefType);
    }

    // ---------------------------------------------------------------- FormServerClient

    [Fact]
    public async Task Resolve_goi_dung_duong_dan_va_truyen_tiep_header_doi_soat()
    {
        var handler = FakeHandler.Ok(new { FormID = FormId, FormCode = "KSK-V1-DTK_03", MatchCount = 1 });

        await Client(handler).ResolveFormAsync("DTK_03", new DateOnly(2026, 1, 15));

        var call = Assert.Single(handler.Calls);
        Assert.Equal(HttpMethod.Get, call.Method);
        Assert.Contains("https://form.test/v1/forms/resolve", call.Url);
        Assert.Contains("docTypeId=990001", call.Url);
        Assert.Contains("variantCode=DTK_03", call.Url);
        // Ngày phải là ngày lịch, không kèm giờ/offset.
        Assert.Contains("effectiveOn=2026-01-15", call.Url);

        Assert.Equal("TRACE-GOC", call.Headers["X-Trace-Id"]);
        Assert.Equal("DEV", call.Headers["X-Division-Id"]);
        Assert.Equal("HEALTH_EXAM", call.Headers["X-Module-Code"]);
    }

    [Fact]
    public async Task Draft_gui_POST_kem_ContextValues_dang_PascalCase()
    {
        var handler = FakeHandler.Ok(new
        {
            FormID = FormId,
            HostRefType = "HEALTH_EXAM_SESSION",
            HostRefID = "KSK-2026-001",
            Layout = new { Sections = new[] { new { Code = "S1" } } },
            PrefillValues = new object[0],
            // Tên trường ĐÚNG NHƯ TRÊN DÂY: form-server gọi nó là "UnresolvedSources"
            // (Form.Server/Models/SubmissionDtos.cs), không phải "MissingContext".
            // Gói giả trước đây dựng bằng tên "MissingContext" nên test vẫn xanh trong khi
            // hàng thật luôn ra null — gói giả sai thì test chỉ chứng minh nó khớp chính nó.
            UnresolvedSources = new[] { "HEALTH_EXAM_SESSION.PackageName" }
        });

        var values = GetExamFormDraftHandler.BuildContextValues(Session(), Record());
        var draft = await Client(handler).CreateDraftAsync(
            FormId, ModuleCodes.HostRefType, "KSK-2026-001", values);

        var call = Assert.Single(handler.Calls);
        // GET submissions/draft không nhận body nên không đẩy được ContextValues — phải POST.
        Assert.Equal(HttpMethod.Post, call.Method);
        Assert.Contains("/v1/submissions/draft", call.Url);
        Assert.Contains($"formId={FormId}", call.Url);
        Assert.Contains("hostRefType=HEALTH_EXAM_SESSION", call.Url);
        Assert.Contains("hostRefId=KSK-2026-001", call.Url);

        var body = JObject.Parse(call.Body);
        Assert.Equal("KSK-2026-001",
            body["ContextValues"]!["HEALTH_EXAM_SESSION"]!["SessionCode"]!.Value<string>());
        Assert.Equal("2026-01-15",
            body["ContextValues"]!["HEALTH_EXAM_SESSION"]!["ExamDate"]!.Value<string>());

        Assert.Equal("S1", ((JToken)draft.Layout)!["Sections"]![0]!["Code"]!.Value<string>());

        // Chốt hợp đồng lệch tên: "UnresolvedSources" trên dây phải rơi vào MissingContext
        // của ta. Bỏ [JsonProperty] ở FormServerModels là test này đỏ ngay, thay vì để lỗi
        // lộ ra bằng một ô trống trên phiếu in mà không ai truy được nguồn.
        Assert.NotNull(draft.MissingContext);
        Assert.Equal("HEALTH_EXAM_SESSION.PackageName",
            Assert.Single((IEnumerable<JToken>)draft.MissingContext!).Value<string>());
    }

    [Fact]
    public async Task Draft_khong_nhan_ten_MissingContext_tu_form_server()
    {
        // Mặt trái của test trên. Nếu một ngày ai đó đổi [JsonProperty] thành tên "của ta"
        // cho gọn, form-server vẫn gửi "UnresolvedSources" và trường lại ra null im lặng.
        // Test này giữ cho hướng ánh xạ chỉ có một chiều đúng.
        var handler = FakeHandler.Ok(new
        {
            FormID = FormId,
            HostRefType = "HEALTH_EXAM_SESSION",
            HostRefID = "KSK-2026-001",
            Layout = new { Sections = new object[0] },
            PrefillValues = new object[0],
            MissingContext = new[] { "khong-bao-gio-duoc-doc" }
        });

        var draft = await Client(handler).CreateDraftAsync(
            FormId, ModuleCodes.HostRefType, "KSK-2026-001",
            GetExamFormDraftHandler.BuildContextValues(Session(), Record()));

        Assert.Null(draft.MissingContext);
    }

    // ---------------------------------------------------------------- ánh xạ lỗi

    [Fact]
    public async Task form_server_tra_5xx_thanh_5020_kem_Dependency()
    {
        var handler = FakeHandler.Status(HttpStatusCode.BadGateway);

        var ex = await Assert.ThrowsAsync<HealthExamException>(
            () => Client(handler).ResolveFormAsync("DTK_03", new DateOnly(2026, 1, 15)));

        Assert.Equal(ErrorCodes.DependencyUnavailable, ex.ErrorCode);
        Assert.Equal("form-server", JObject.FromObject(ex.Payload)["Dependency"]!.Value<string>());
        // Idempotent → 1 lần gọi + 2 lần thử lại.
        Assert.Equal(3, handler.Calls.Count);
    }

    [Fact]
    public async Task form_server_timeout_thanh_5020()
    {
        var handler = FakeHandler.Throws(() => new TaskCanceledException("timeout"));

        var ex = await Assert.ThrowsAsync<HealthExamException>(
            () => Client(handler).ResolveFormAsync("DTK_03", new DateOnly(2026, 1, 15)));

        Assert.Equal(ErrorCodes.DependencyUnavailable, ex.ErrorCode);
        Assert.Equal("Timeout", JObject.FromObject(ex.Payload)["Reason"]!.Value<string>());
    }

    /// <summary>POST tạo bản nháp không idempotent — thử lại có thể sinh bản nháp thừa.</summary>
    [Fact]
    public async Task Draft_khong_thu_lai_khi_loi()
    {
        var handler = FakeHandler.Status(HttpStatusCode.ServiceUnavailable);

        var ex = await Assert.ThrowsAsync<HealthExamException>(
            () => Client(handler).CreateDraftAsync(FormId, ModuleCodes.HostRefType, "KSK-2026-001", new()));

        Assert.Equal(ErrorCodes.DependencyUnavailable, ex.ErrorCode);
        Assert.Single(handler.Calls);
    }

    /// <summary>
    /// Lỗi NGHIỆP VỤ của form-server (bên kia còn sống) không được biến thành 5020: 5020 kêu
    /// trực hệ thống đi cứu dịch vụ, còn 4040 ở đây là "chưa cấu hình biểu mẫu cho nhóm khám".
    /// </summary>
    [Fact]
    public async Task Loi_nghiep_vu_cua_form_server_duoc_chuyen_tiep_nguyen_ma()
    {
        var handler = FakeHandler.Envelope(HttpStatusCode.NotFound, new
        {
            ErrorCode = ErrorCodes.NotFound,
            Message = "Không tìm thấy biểu mẫu hiệu lực",
            Data = (object)null
        });

        var ex = await Assert.ThrowsAsync<HealthExamException>(
            () => Client(handler).ResolveFormAsync("DTK_03", new DateOnly(2026, 1, 15)));

        Assert.Equal(ErrorCodes.NotFound, ex.ErrorCode);
        Assert.StartsWith("form-server:", ex.Message);
        // 4xx không thử lại: gửi lại đúng request sai vẫn sai.
        Assert.Single(handler.Calls);
    }

    [Fact]
    public async Task Thieu_bien_moi_truong_bao_ro_bien_nao_thay_vi_goi_vao_chuoi_rong()
    {
        var handler = FakeHandler.Ok(new { FormID = FormId });
        var client = Client(handler, new FormServerOptions { BaseUrl = "", DocTypeId = 990001 });

        var ex = await Assert.ThrowsAsync<HealthExamException>(
            () => client.ResolveFormAsync("DTK_03", new DateOnly(2026, 1, 15)));

        Assert.Equal(ErrorCodes.DependencyUnavailable, ex.ErrorCode);
        Assert.Contains("FORM_SERVER_BASE_URL", ex.Message);
        Assert.Empty(handler.Calls);
    }

    /// <summary>DocTypeID rác KHÔNG được rơi êm về 990001 — sẽ resolve nhầm gáy hồ sơ khác.</summary>
    [Fact]
    public async Task DocTypeID_khong_parse_duoc_thi_bao_loi_cau_hinh()
    {
        var options = new FormServerOptions { BaseUrl = "https://form.test", DocTypeId = 0, DocTypeIdRaw = "abc" };
        var handler = FakeHandler.Ok(new { FormID = FormId });

        var ex = await Assert.ThrowsAsync<HealthExamException>(
            () => Client(handler, options).ResolveFormAsync("DTK_03", new DateOnly(2026, 1, 15)));

        Assert.Contains("HEALTH_EXAM_DOCTYPE_ID", ex.Message);
        Assert.Empty(handler.Calls);
    }

    /// <summary>MatchCount &gt; 1 = cấu hình mơ hồ: cảnh báo, KHÔNG chặn đợt khám đang chạy.</summary>
    [Fact]
    public async Task MatchCount_lon_hon_1_van_dung_ket_qua()
    {
        var handler = FakeHandler.Ok(new { FormID = FormId, FormCode = "KSK-V1-DTK_03", MatchCount = 2 });

        var dto = await Client(handler).ResolveFormAsync("DTK_03", new DateOnly(2026, 1, 15));

        Assert.Equal(FormId, dto.FormID);
        Assert.Equal(2, dto.MatchCount);
    }

    /// <summary>
    /// 5020 → 503 chứ không 500: gateway/monitor đọc 503 là "tạm thời, thử lại được".
    /// (Bảng mã đầy đủ nằm ở ErrorCodeTests — file đó thuộc track khác nên chốt ở đây.)
    /// </summary>
    [Fact]
    public void Ma_5020_anh_xa_503_va_co_thong_bao_rieng()
    {
        Assert.Equal(5020, ErrorCodes.DependencyUnavailable);
        Assert.Equal(503, ErrorCodes.ToHttpStatus(ErrorCodes.DependencyUnavailable));
        Assert.Equal("Dịch vụ phụ thuộc đang không phản hồi, vui lòng thử lại",
            ErrorCodes.DefaultMessage(ErrorCodes.DependencyUnavailable));
    }

    // ---------------------------------------------------------------- đồ nghề

    private static ExamSession Session() => new()
    {
        SessionID = Guid.NewGuid(),
        DivisionID = "DEV",
        SessionCode = "KSK-2026-001",
        OrganizationName = "Công ty TNHH ABC",
        PackageName = "Gói khám định kỳ 2026",
        ExamDate = new DateOnly(2026, 1, 15)
    };

    private static ExamRecord Record()
    {
        var patient = new Patient
        {
            PatientRefID = Guid.NewGuid(),
            DivisionID = "DEV",
            FullName = "Nguyễn Văn A",
            Dob = new DateOnly(1990, 3, 2)
        };
        return new ExamRecord
        {
            RecordID = Guid.NewGuid(),
            DivisionID = "DEV",
            RecordCode = "KSK-2026-001-0007",
            PatientRefID = patient.PatientRefID,
            Patient = patient,
            VariantCode = "DTK_03"
        };
    }

    private static FormServerClient Client(FakeHandler handler, FormServerOptions options = null)
        => new(
            new HttpClient(handler),
            options ?? new FormServerOptions { BaseUrl = "https://form.test", DocTypeId = 990001 },
            new FakeContext(),
            NullLogger<FormServerClient>.Instance);

    /// <summary>Ngữ cảnh request giả — chỉ cần TraceId/DivisionId để kiểm việc truyền tiếp header.</summary>
    private sealed class FakeContext : IHealthExamContext
    {
        public string TraceId => "TRACE-GOC";
        public string DivisionId => "DEV";
        public string ModuleCode => ModuleCodes.HealthExam;
        public long ActorId => 7;
        public ActorKind ActorKind => ActorKind.Employee;
        public string ActorName => "Test";
        public string ActorCode => "NV001";
        public string Scope => "health-exam:employee";
        public string GetRequestHeader(string name) => "";
    }

    private sealed record Call(HttpMethod Method, string Url, string Body, Dictionary<string, string> Headers);

    /// <summary>
    /// Handler HTTP giả. Ghi lại từng lượt gọi NGAY trong SendAsync vì client dispose
    /// HttpRequestMessage sau mỗi lượt — giữ tham chiếu tới request rồi đọc sau sẽ ra rỗng.
    /// </summary>
    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly Func<HttpResponseMessage> _responder;
        public List<Call> Calls { get; } = new();

        private FakeHandler(Func<HttpResponseMessage> responder) => _responder = responder;

        public static FakeHandler Ok(object data)
            => Envelope(HttpStatusCode.OK, new { ErrorCode = 0, Message = "", Data = data, TraceID = "FORM-TRACE" });

        public static FakeHandler Envelope(HttpStatusCode status, object envelope)
            => new(() => new HttpResponseMessage(status)
            {
                Content = new StringContent(
                    Newtonsoft.Json.JsonConvert.SerializeObject(envelope), Encoding.UTF8, "application/json")
            });

        public static FakeHandler Status(HttpStatusCode status)
            => new(() => new HttpResponseMessage(status) { Content = new StringContent("") });

        public static FakeHandler Throws(Func<Exception> factory)
            => new(() => throw factory());

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls.Add(new Call(
                request.Method,
                request.RequestUri!.ToString(),
                request.Content == null ? null : await request.Content.ReadAsStringAsync(cancellationToken),
                request.Headers.ToDictionary(h => h.Key, h => string.Join(",", h.Value))));

            return _responder();
        }
    }
}

/// <summary>
/// Chốt dây DI của cầu nối. Thiếu một đăng ký (FormServerOptions, AddHttpClient, service)
/// thì build vẫn xanh và mọi test logic ở trên vẫn xanh — chỉ endpoint thật là chết lúc
/// chạy, tức là lỗi chỉ lộ ra khi FE gọi vào.
/// KHÔNG cần DB: chỉ dựng đối tượng, không chạy truy vấn nào.
/// </summary>
public class FormDraftWiringTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public FormDraftWiringTests(WebApplicationFactory<Program> factory) => _factory = factory;

    // CreateAsyncScope chứ không CreateScope: scope này giữ UnitOfWork (chỉ có
    // IAsyncDisposable), dispose kiểu đồng bộ sẽ ném ngay ở cuối test.
    [Fact]
    public async Task Cau_noi_form_server_dung_duoc_tu_container_that()
    {
        await using var scope = _factory.Services.CreateAsyncScope();

        Assert.NotNull(scope.ServiceProvider.GetRequiredService<FormServerClient>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IGetExamFormDraftHandler>());
    }

    /// <summary>Timeout phải là 3s như đã chốt, không phải mặc định 100s của HttpClient.</summary>
    [Fact]
    public async Task HttpClient_cua_form_server_giu_timeout_ngan()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var http = scope.ServiceProvider.GetRequiredService<IHttpClientFactory>()
            .CreateClient(nameof(FormServerClient));

        Assert.Equal(FormServerOptions.DefaultTimeout, http.Timeout);
    }
}
