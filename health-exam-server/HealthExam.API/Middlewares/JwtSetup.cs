using System.Security.Cryptography;
using System.Text;
using HealthExam.API.Contracts;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

namespace HealthExam.API.Middlewares;

/// <summary>
/// Nối xác thực JWT của IAM. KHÔNG đẻ token riêng cho KSK (02-api-spec §6.1): nhân viên đăng
/// nhập một lần ở IAM rồi dùng chung token cho mọi service.
///
/// Tham số ký chép ĐÚNG theo his-server (HIS.Core/Helpers/JwtMiddleware.cs) — sai một chi tiết
/// nào ở đây thì token thật của IAM bị từ chối và trông y hệt "chưa cấu hình":
///   • khoá = SHA256(SECRET_KEY) chứ không phải SECRET_KEY thô
///   • audience cố định "DHS"
///   • issuer = SECRET_ISSUER
///   • ClockSkew = 0
/// </summary>
public static class JwtSetup
{
    public const string ServiceTokenEnv = "SECRET_INTER";

    public static IServiceCollection AddHealthExamJwt(this IServiceCollection services)
    {
        var issuer = Environment.GetEnvironmentVariable("SECRET_ISSUER") ?? "";
        var issuers = new[] { issuer, "DHTESTING", "DHS" }
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct()
            .ToArray();
        var keyString = Environment.GetEnvironmentVariable("SECRET_KEY") ?? "";
        byte[] keyBytes;
        using (var sha256 = SHA256.Create())
            keyBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(keyString));

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                // Giữ NGUYÊN tên claim như trong token. Mặc định .NET ánh xạ "sub" thành
                // ClaimTypes.NameIdentifier và đổi tên một loạt claim khác; bộ claim của IAM
                // là tên tự đặt (EmployeeID, UserName…) nên ánh xạ chỉ gây nhiễu.
                options.MapInboundClaims = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidIssuers = issuers,
                    ValidateIssuer = true,
                    ValidateIssuerSigningKey = true,
                    ValidateLifetime = true,
                    ValidAudience = "DHS",
                    ValidateAudience = true,
                    IssuerSigningKey = new SymmetricSecurityKey(keyBytes),
                    ClockSkew = TimeSpan.Zero
                };
            });

        return services;
    }
}
