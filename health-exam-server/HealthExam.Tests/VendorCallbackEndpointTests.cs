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
/// P3b — chốt chặn của đường VỀ vendor, kiểm qua HTTP thật.
///
/// Phải đi qua HTTP: thứ cần chốt là AI ĐƯỢC VÀO và TENANT TỚI TỪ ĐÂU, mà cả hai do
/// middleware quyết định. Gọi thẳng VendorStatusService là bỏ qua đúng phần đang kiểm.
///
/// Các test này CỐ Ý chỉ dùng request bị chặn TRƯỚC khi chạm DB (host test không có
/// PostgreSQL). Nhánh áp trạng thái do <see cref="VendorStatusCallbackTests"/> chốt trên DB
/// trong bộ nhớ.
/// </summary>
public class VendorCallbackEndpointTests : IClassFixture<AuthTestHost>
{
    private const string Path = "/v1/integration/vendor/DEV/paraclinical-status";

    private const string Body = """
                                {"OrderID":"CD0000000007","VoucherType":"2","NewStatus":1}
                                """;

    private readonly AuthTestHost _host;

    public VendorCallbackEndpointTests(AuthTestHost host) => _host = host;

    private static StringContent Content() => new(Body, Encoding.UTF8, "application/json");

    private static AuthenticationHeaderValue Basic(string user, string password)
        => new("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{user}:{password}")));

    /// <summary>
    /// 🔴 Không mang gì thì KHÔNG vào được — kể cả khi request hợp lệ về mọi mặt khác.
    /// Endpoint này ghi được vào hồ sơ y tế; mở nó ra là mở cho bất kỳ ai gọi tới.
    /// </summary>
    [Fact]
    public async Task Khong_co_Basic_thi_4010()
    {
        var response = await _host.CreateClient().PostAsync(Path, Content());
        var body = JObject.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(ErrorCodes.Unauthorized, body["ErrorCode"]!.Value<int>());
    }

    /// <summary>Sai mật khẩu cũng ra đúng câu đó — câu trả lời chi tiết chỉ giúp người đang dò.</summary>
    [Fact]
    public async Task Sai_mat_khau_thi_4010_va_khong_noi_sai_ve_nao()
    {
        var client = _host.CreateClient();
        client.DefaultRequestHeaders.Authorization = Basic(AuthTestHost.VendorUser, "sai-mat-khau");

        var response = await client.PostAsync(Path, Content());
        var body = JObject.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.DoesNotContain("mật khẩu", body["Message"]!.Value<string>() ?? "");
    }

    /// <summary>Token nhân viên KHÔNG mở được cửa này: nhánh vendor chỉ nhận Basic, và một
    /// Bearer hợp lệ đi lạc vào đây nghĩa là ai đó đang gọi nhầm endpoint.</summary>
    [Fact]
    public async Task Bearer_cua_nhan_vien_khong_thay_the_duoc_Basic()
    {
        var client = _host.CreateEmployeeClient();

        var response = await client.PostAsync(Path, Content());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>
    /// ★ TENANT TỚI TỪ ĐƯỜNG DẪN. Vendor không gửi được X-Division-Id — hợp đồng dây của họ
    /// đúng ba trường. Gói mang Basic đúng mà KHÔNG có header tenant vẫn phải đi qua được
    /// DivisionMiddleware; nếu không thì mọi gói gọi về chết ở 4001 và không ai biết vì sao.
    ///
    /// Chốt bằng phủ định: phản hồi KHÔNG được là 4010 (chặn ở Basic) và KHÔNG được là 4001
    /// "thiếu X-Division-Id". Host test không có PostgreSQL nên đi tiếp sẽ hỏng ở tầng dữ
    /// liệu — đó chính là bằng chứng nó đã đi qua cả hai chốt chặn.
    /// </summary>
    [Fact]
    public async Task Basic_dung_thi_qua_duoc_chot_tenant_du_khong_co_header()
    {
        var client = _host.CreateClient();
        client.DefaultRequestHeaders.Authorization = Basic(AuthTestHost.VendorUser, AuthTestHost.VendorPassword);

        var response = await client.PostAsync(Path, Content());
        var body = JObject.Parse(await response.Content.ReadAsStringAsync());
        var code = body["ErrorCode"]!.Value<int>();

        Assert.NotEqual(ErrorCodes.Unauthorized, code);

        var missingTenant = code == ErrorCodes.BadRequest
            && (body["Data"]?["Errors"]?[0]?["Field"]?.Value<string>() ?? "") == "X-Division-Id";
        Assert.False(missingTenant, "Gói của vendor bị chặn vì thiếu X-Division-Id — tenant phải được dựng từ đường dẫn.");
    }
}
