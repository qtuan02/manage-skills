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
using HealthExam.Infrastructure.Integrations.Ris;
using HealthExam.Server.Service;
using Newtonsoft.Json.Linq;
using Xunit;

namespace HealthExam.Tests;

/// <summary>
/// P3b — đường ĐI, kiểm qua HTTP thật: <c>POST /v1/orders/{id}/dispatch</c>.
///
/// Nghiệm thu vòng 3 chỉ ra rằng KHÔNG ca nào đi qua HTTP tới endpoint này, nên ba thứ chỉ
/// tồn tại lúc chạy thật đều không được ghim: đường dẫn, đăng ký DI của
/// <c>IntegrationOutboxService</c>, và TÊN các biến môi trường mà 5021 nhắc tới. Cả ba đều
/// hỏng theo kiểu biên dịch vẫn xanh — DI thiếu thì lỗi nổ ở request đầu tiên trên môi trường
/// thật, và tên biến sai thì người triển khai khai đúng theo thông báo lỗi mà cầu vẫn không
/// lên.
///
/// Host test CỐ Ý không khai <c>RIS_BASE_URL</c> (xem <see cref="AuthTestHost"/>: cầu vendor
/// phải mặc định TẮT), nên mọi ca ở đây dừng ở 5021 — TRƯỚC khi chạm DB, thứ host test không
/// có. Đó vừa là giới hạn vừa là điều đang chốt: chưa cấu hình thì dịch vụ NÓI RA.
/// </summary>
public class DispatchEndpointTests : IClassFixture<AuthTestHost>
{
    private readonly AuthTestHost _host;

    public DispatchEndpointTests(AuthTestHost host) => _host = host;

    private static string Path(Guid orderId) => $"/v1/orders/{orderId}/dispatch";

    /// <summary>
    /// 🔴 Chưa cấu hình cầu RIS ⇒ <c>5021</c> / HTTP 503, và thông báo phải nêu ĐÚNG TÊN các
    /// biến môi trường cần khai — kể cả <c>RIS_DIVISION_ID</c> vừa thêm.
    ///
    /// Vì sao ghim tên biến: người triển khai đọc đúng dòng này rồi đi khai ConfigMap. Một cái
    /// tên lệch ở đây nghĩa là họ khai một biến không ai đọc, cầu vẫn tắt, và triệu chứng phía
    /// người dùng chỉ là "phòng chụp không thấy chỉ định".
    ///
    /// Và ca này đi QUA toàn bộ ống thật: route → xác thực → tenant → DI. Nó đỏ nếu
    /// <c>IntegrationOutboxService</c> chưa được đăng ký, thứ không test đơn vị nào bắt được.
    /// </summary>
    [Fact]
    public async Task Chua_cau_hinh_cau_RIS_thi_5021_va_neu_dung_ten_bien_moi_truong()
    {
        var client = _host.CreateEmployeeClient();

        var response = await client.PostAsync(Path(Guid.NewGuid()), null);
        var body = JObject.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(ErrorCodes.VendorNotConfigured, body["ErrorCode"]!.Value<int>());

        var message = body["Message"]!.Value<string>() ?? "";
        foreach (var envName in new[]
                 {
                     RisOptions.BaseUrlEnv, RisOptions.UsernameEnv,
                     RisOptions.PasswordEnv, RisOptions.DivisionEnv
                 })
            Assert.Contains(envName, message);

        // FE và trực hệ thống đọc trường này để biết đích nào đang hỏng.
        Assert.Equal(RisClient.DependencyName, body["Data"]!["Dependency"]!.Value<string>());
    }

    /// <summary>Không có token thì KHÔNG vào được — endpoint này ghi vào hồ sơ và phát một lệnh
    /// ra hệ thống bên thứ ba, nên nó phải đứng sau đúng cùng cánh cửa với mọi thao tác khác
    /// của nhân viên. Chốt TRƯỚC 5021: thứ tự hai chốt chặn cũng là một khẳng định.</summary>
    [Fact]
    public async Task Khong_co_token_thi_4010_chu_khong_phai_5021()
    {
        var response = await _host.CreateAnonymousClient().PostAsync(Path(Guid.NewGuid()), null);
        var body = JObject.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(ErrorCodes.Unauthorized, body["ErrorCode"]!.Value<int>());
    }

    /// <summary>Thiếu <c>X-Division-Id</c> ⇒ 4001 ngay ở middleware. Đường ĐI khác hẳn đường
    /// VỀ ở điểm này: nhân viên có header, còn vendor thì tenant dựng từ URL.</summary>
    [Fact]
    public async Task Thieu_header_don_vi_thi_4001()
    {
        var client = _host.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", AuthTestHost.EmployeeToken());

        var response = await client.PostAsync(Path(Guid.NewGuid()), null);
        var body = JObject.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(ErrorCodes.BadRequest, body["ErrorCode"]!.Value<int>());
        Assert.Equal(TestHeaders.Division, body["Data"]!["Errors"]![0]!["Field"]!.Value<string>());
    }
}
