using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
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
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.IdentityModel.Tokens;

namespace HealthExam.Tests;

/// <summary>
/// Host dùng chung cho test đi qua HTTP: đặt biến môi trường xác thực TRƯỚC khi
/// WebApplicationFactory dựng ứng dụng, rồi phát token ký bằng đúng bộ tham số đó.
///
/// Vì sao phải là fixture chứ không đặt biến trong từng test: Program.cs đọc SECRET_KEY /
/// SECRET_ISSUER MỘT LẦN lúc khởi động, và biến môi trường là của cả tiến trình. Đặt muộn
/// hoặc đặt lại giữa chừng thì token ký bằng khoá này lại đi vào ứng dụng đã nạp khoá khác —
/// test đỏ vì hạ tầng test, không phải vì code sai.
///
/// Token phát ở đây ký GIỐNG HỆT his-server (SHA256(SECRET_KEY), audience "DHS"): nếu một
/// ngày his-server đổi cách ký mà health-exam-server không đổi theo, test này phải đỏ.
/// </summary>
public class AuthTestHost : WebApplicationFactory<Program>
{
    public const string SecretKey = "health-exam-test-secret";
    public const string Issuer = "DHS-TEST";
    public const string ServiceToken = "test-inter-secret";

    /// <summary>Tài khoản Basic mà vendor RIS dùng để gọi ngược về (P3b).</summary>
    public const string VendorUser = "vietrad";
    public const string VendorPassword = "test-vendor-pass";

    /// <summary>Nhân viên mặc định của test — EmployeeID này là giá trị kỳ vọng ở cột audit.</summary>
    public const long EmployeeId = 4210;
    public const string UserName = "bs.hoa";

    static AuthTestHost()
    {
        Environment.SetEnvironmentVariable("SECRET_KEY", SecretKey);
        Environment.SetEnvironmentVariable("SECRET_ISSUER", Issuer);
        Environment.SetEnvironmentVariable("SECRET_INTER", ServiceToken);

        // Tài khoản đường VỀ của vendor (P3b). Đặt ở đây cùng lý do với ba biến trên:
        // RisOptions là singleton đọc biến môi trường MỘT LẦN lúc dựng DI, nên đặt trong
        // từng test là đặt sau khi ứng dụng đã nạp cấu hình rỗng.
        // KHÔNG đặt RIS_BASE_URL: host test không được có đích gửi nào — xem
        // IntegrationOutboxWorker về việc vì sao cầu vendor phải mặc định TẮT.
        Environment.SetEnvironmentVariable("RIS_CALLBACK_USER", VendorUser);
        Environment.SetEnvironmentVariable("RIS_CALLBACK_PASSWORD", VendorPassword);

        // SigningOptionsValidator chạy NGAY lúc AddHealthExamInfrastructure và đòi đủ 6 biến khi
        // HIS_EMR_ENABLED=true. Dựng host không được phụ thuộc test nào khác đang bật cờ đó hay
        // không (race giữa các test chạy song song), nên đặt sẵn cả bộ để host luôn dựng được.
        // MinIO client dựng lazy trong MinioExamFileStore — không test nào đụng MinIO thật (mọi
        // test ký đi qua FakeExamFileStore), giá trị ở đây chỉ để validator không ném.
        Environment.SetEnvironmentVariable("MINIO_PATH", "localhost:9000");
        Environment.SetEnvironmentVariable("MINIO_USERNAME", "test-access");
        Environment.SetEnvironmentVariable("MINIO_PASS", "test-secret");
        Environment.SetEnvironmentVariable("MINIO_BUCKET_HEALTH_EXAM", "health-exam-test");
        Environment.SetEnvironmentVariable("SIGN_SERVER_BASE_URL", "http://sign.test");
        Environment.SetEnvironmentVariable("SSM_BASE_URL", "http://ssm.test");
    }

    /// <summary>Client đã kèm tenant + token nhân viên — dùng cho phần lớn test.</summary>
    public HttpClient CreateEmployeeClient(string divisionId = "DEV")
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add(TestHeaders.Division, divisionId);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", EmployeeToken());
        return client;
    }

    /// <summary>
    /// Client mang SECRET_INTER — giả lập iam-server gọi vào /v1/internal.
    /// Bí mật là chuỗi thô, KHÔNG phải JWT: đó chính là lý do AuthGuardMiddleware phải có
    /// nhánh riêng cho nó thay vì trông vào JwtBearer.
    /// </summary>
    public HttpClient CreateServiceClient(string divisionId = "DEV")
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add(TestHeaders.Division, divisionId);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", ServiceToken);
        return client;
    }

    /// <summary>Client có tenant nhưng KHÔNG có token — dùng để chốt gate 4010.</summary>
    public HttpClient CreateAnonymousClient(string divisionId = "DEV")
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add(TestHeaders.Division, divisionId);
        return client;
    }

    public static string EmployeeToken(long employeeId = EmployeeId, string userName = UserName)
        => Sign(new[]
        {
            new Claim(ClaimNames.AccountId, "77"),
            new Claim(ClaimNames.EmployeeId, employeeId.ToString()),
            new Claim(ClaimNames.UserName, userName),
            new Claim(ClaimNames.EmployeeCode, "NV001"),
            new Claim(ClaimNames.Role, "User")
        });

    /// <summary>Token ký bằng khoá khác — giả lập token giả mạo hoặc token của hệ thống khác.</summary>
    public static string TokenSignedWithWrongKey()
        => Sign(new[] { new Claim(ClaimNames.EmployeeId, "9999") }, key: "khoa-khac");

    private static string Sign(IEnumerable<Claim> claims, string key = SecretKey, string issuer = Issuer)
    {
        byte[] keyBytes;
        using (var sha256 = SHA256.Create())
            keyBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(key));

        var handler = new JwtSecurityTokenHandler();
        var token = handler.CreateToken(new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Expires = DateTime.UtcNow.AddHours(1),
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(keyBytes), SecurityAlgorithms.HmacSha256Signature),
            Issuer = issuer,
            Audience = "DHS"
        });
        return handler.WriteToken(token);
    }
}

public static class TestHeaders
{
    public const string Division = "X-Division-Id";
    public const string Module = "X-Module-Code";
    public const string Trace = "X-Trace-Id";
}
