using HealthExam.API.Extensions;
using HealthExam.API.Middlewares;
using HealthExam.Application.Common;
using HealthExam.Infrastructure;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.OpenApi.Models;
using Newtonsoft.Json.Serialization;
using Serilog;
using Serilog.Events;
LoadEnvFile(".env.local");
LoadEnvFile("../.env.local");
LoadEnvFile(".env");

var builder = WebApplication.CreateBuilder(args);

var division = Environment.GetEnvironmentVariable("DIVISION") ?? "Unknown";
var elasticUrl = Environment.GetEnvironmentVariable("ELASTIC_LOG_SERVER_URL") ?? "";

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Component", "HealthExamServer")
    .Enrich.WithProperty("Division", division)
    .WriteTo.Conditional(_ => !string.IsNullOrEmpty(elasticUrl), wt => wt.Http(elasticUrl, null))
    .WriteTo.File(
        path: "Logs/health_exam_server.log",
        outputTemplate: "{Timestamp:o} [{Level:u3}] ({SourceContext}) {Message}{NewLine}{Exception}",
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 7,
        restrictedToMinimumLevel: LogEventLevel.Warning)
    .WriteTo.Console()
    .CreateLogger();

builder.Host.UseSerilog();

builder.Services.AddHealthExamInfrastructure(builder.Configuration);
builder.Services.AddHealthExamApplication();

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IHealthExamContext, HealthExamRequestContext>();
builder.Services.AddHealthExamJwt();
builder.Services.AddCors();

builder.Services
    .AddControllers()
    .AddNewtonsoftJson(o => o.SerializerSettings.ContractResolver = new HealthExamContractResolver());

builder.Services.AddHealthChecks()
    .AddCheck("Server", () => HealthCheckResult.Healthy("Health exam server is running."));

var healthExamDb = Environment.GetEnvironmentVariable("HEALTHEXAM_DB") ?? "";
if (!string.IsNullOrWhiteSpace(healthExamDb))
{
    builder.Services.AddHealthChecks()
        .AddNpgSql(healthExamDb, name: "PostgreSQL", timeout: TimeSpan.FromSeconds(5));
}

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Health Exam Service API",
        Version = "v1",
        Description = "Nghiệp vụ Khám sức khoẻ (KSK) — đợt khám, hồ sơ người khám, danh mục."
    });
    options.EnableAnnotations();
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "JWT Authorization header. Ví dụ: \"Bearer {token}\" hoặc \"Bearer {SECRET_INTER}\"",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.ApiKey,
        Scheme = "Bearer"
    });
    options.AddSecurityDefinition("Division", new OpenApiSecurityScheme
    {
        Description = "Tenant Header bắt buộc: X-Division-Id. Ví dụ: DEV hoặc DHTESTING",
        Name = "X-Division-Id",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.ApiKey
    });
    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
            },
            Array.Empty<string>()
        },
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Division" }
            },
            Array.Empty<string>()
        }
    });
    var swaggerUrl = Environment.GetEnvironmentVariable("SWAGGER_URL");
    if (!string.IsNullOrWhiteSpace(swaggerUrl))
        options.AddServer(new OpenApiServer { Url = swaggerUrl });
});

var app = builder.Build();

app.UseCors(x => x.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());

// Thứ tự có ý nghĩa: TraceID phải có trước để mọi phản hồi lỗi sau đó đều mang được nó;
// ExceptionMiddleware bọc ngoài Division để lỗi 4001 vẫn đi qua đúng một đường ghi envelope.
//
// UseAuthentication chỉ ĐỌC token và gắn danh tính, nó KHÔNG chặn ai — việc chặn nằm ở
// AuthGuardMiddleware để phản hồi 4010 đi ra dưới dạng envelope thay vì 401 rỗng.
// Division đứng trước Auth: thiếu cả hai thì báo thiếu tenant trước, và thứ tự cố định thì
// thông báo lỗi mới đoán trước được.
app.UseMiddleware<TraceIdMiddleware>();
app.UseMiddleware<ExceptionMiddleware>();
app.UseAuthentication();
// Nhánh /v1/integration/vendor/* xác thực bằng Basic và lấy TENANT TỪ ĐƯỜNG DẪN: vendor RIS
// không có tài khoản IAM, không ký HMAC, và hợp đồng dây của họ đúng ba trường nên không có
// chỗ nhét X-Division-Id. Đặt TRƯỚC Division vì nó DỰNG LẠI chính header đó từ URL — đặt sau
// thì mọi gói của vendor chết ở 4001 "thiếu X-Division-Id"; và trước AuthGuard vì nó tự dựng
// danh tính cho gói đã xác thực.
app.UseMiddleware<VendorCallbackAuthMiddleware>();
app.UseMiddleware<DivisionMiddleware>();
// Nhánh /v1/hooks/* xác thực bằng CHỮ KÝ trên thân gói, không bằng token: form-server không
// có tài khoản IAM nào, và một Bearer token đúng vẫn cho phép sửa SubjectID trên đường đi.
// Đặt trước AuthGuard vì nó tự dựng danh tính cho gói đã ký hợp lệ; đặt sau Division vì
// tenant của phiếu là thứ phải có trước khi tra bất cứ gì.
app.UseMiddleware<WebhookSignatureMiddleware>();
app.UseMiddleware<AuthGuardMiddleware>();

app.MapHealthChecks("/health", new HealthCheckOptions
{
    ResponseWriter = HealthResponse.WriteAsync
});

app.MapControllers();

if (Environment.GetEnvironmentVariable("ENABLE_SWAGGER") == "true")
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.DefaultModelsExpandDepth(-1);
        options.SwaggerEndpoint("./v1/swagger.json", "Health Exam Service API");
    });
}

app.MapGet("/", () => Results.Redirect("swagger"));

try
{
    Log.Information("Khởi động health-exam-server");
    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "health-exam-server dừng bất thường");
}
finally
{
    Log.CloseAndFlush();
}

public partial class Program
{
    private static void LoadEnvFile(string path)
    {
        try
        {
            if (!File.Exists(path)) return;
            foreach (var line in File.ReadAllLines(path))
            {
                var trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith('#')) continue;
                var eqIndex = trimmed.IndexOf('=');
                if (eqIndex <= 0) continue;
                var key = trimmed[..eqIndex].Trim();
                var val = trimmed[(eqIndex + 1)..].Trim();
                if ((val.StartsWith('"') && val.EndsWith('"')) || (val.StartsWith('\'') && val.EndsWith('\'')))
                {
                    val = val[1..^1];
                }
                if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(key)))
                {
                    Environment.SetEnvironmentVariable(key, val);
                }
            }
        }
        catch
        {
            // Ignored
        }
    }
}
