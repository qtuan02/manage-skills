using System;
using HealthExam.Infrastructure;
using HealthExam.Infrastructure.Integrations.HisEmr;
using HealthExam.Infrastructure.Integrations.SignServer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HealthExam.Tests.Signing;

[Collection("env")]
public class SigningOptionsValidatorTests
{
    [Fact]
    public void Enabled_with_all_values_empty_throws_listing_all_six_names()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            SigningOptionsValidator.Validate(
                enabled: true,
                signServerBaseUrl: "",
                ssmBaseUrl: "",
                minioEndpoint: "",
                minioBucket: "",
                minioAccessKey: "",
                minioSecretKey: ""));

        Assert.Contains("SIGN_SERVER_BASE_URL", ex.Message);
        Assert.Contains("SSM_BASE_URL", ex.Message);
        Assert.Contains("MINIO_PATH", ex.Message);
        Assert.Contains("MINIO_BUCKET_HEALTH_EXAM", ex.Message);
        Assert.Contains("MINIO_USERNAME", ex.Message);
        Assert.Contains("MINIO_PASS", ex.Message);
    }

    [Fact]
    public void Disabled_with_all_values_empty_does_not_throw()
    {
        SigningOptionsValidator.Validate(
            enabled: false,
            signServerBaseUrl: "",
            ssmBaseUrl: "",
            minioEndpoint: "",
            minioBucket: "",
            minioAccessKey: "",
            minioSecretKey: "");
    }

    [Fact]
    public void Enabled_with_all_values_present_does_not_throw()
    {
        SigningOptionsValidator.Validate(
            enabled: true,
            signServerBaseUrl: "http://sign-server:9940",
            ssmBaseUrl: "http://ssm-server:9930",
            minioEndpoint: "minio:9000",
            minioBucket: "health-exam",
            minioAccessKey: "minio",
            minioSecretKey: "minio-secret");
    }

    /// <summary>
    /// MinIO client không được dựng lúc resolve IExamFileStore: HIS tắt ⇒ không validator nào chạy,
    /// nhưng ExamRecordController vẫn phải dựng được (mọi endpoint hồ sơ đi qua nó). Client chỉ
    /// được dựng ở lượt Upload/Download đầu tiên.
    /// </summary>
    [Fact]
    public void HIS_disabled_and_empty_MINIO_env_still_resolves_IExamFileStore()
    {
        using var env = new HealthExam.Tests.TemporaryEnvironment(
            (HisEmrOptions.EnabledEnv, "false"),
            ("MINIO_PATH", ""),
            ("MINIO_USERNAME", ""),
            ("MINIO_PASS", ""),
            ("MINIO_BUCKET_HEALTH_EXAM", ""));

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHealthExamInfrastructure(new ConfigurationBuilder().Build());
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var store = scope.ServiceProvider.GetRequiredService<HealthExam.Application.Integrations.IExamFileStore>();

        Assert.IsType<HealthExam.Infrastructure.Storage.MinioExamFileStore>(store);
    }

    /// <summary>
    /// Tra cert chạy BÊN TRONG giao dịch giữ row lock của ký kết luận — ssm-server treo không được
    /// giữ lock 100s (mặc định HttpClient). Typed client đăng ký theo tên interface.
    /// </summary>
    [Fact]
    public void Certificate_gateway_http_client_times_out_after_10_seconds()
    {
        using var env = new HealthExam.Tests.TemporaryEnvironment((HisEmrOptions.EnabledEnv, "false"));

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHealthExamInfrastructure(new ConfigurationBuilder().Build());
        using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<System.Net.Http.IHttpClientFactory>();

        var client = factory.CreateClient(nameof(HealthExam.Application.Integrations.ICertificateGateway));

        Assert.Equal(TimeSpan.FromSeconds(10), client.Timeout);
    }

    /// <summary>
    /// Xác nhận validator THẬT SỰ được gọi từ AddHealthExamInfrastructure, không chỉ tồn tại như một
    /// hàm tĩnh không ai đụng tới. Ba test phía trên chỉ gọi thẳng SigningOptionsValidator.Validate —
    /// test này đi qua đúng đường một pod thật đi qua lúc khởi động (Program.cs không bọc try/catch
    /// quanh lệnh gọi này, xem AuthTestHost.cs).
    /// </summary>
    [Fact]
    public void AddHealthExamInfrastructure_throws_when_enabled_and_sign_server_base_url_missing()
    {
        using var env = new HealthExam.Tests.TemporaryEnvironment(
            (HisEmrOptions.EnabledEnv, "true"),
            ("SIGN_SERVER_BASE_URL", ""),
            ("SSM_BASE_URL", "http://ssm.wiring-test"),
            ("MINIO_PATH", "minio.wiring-test:9000"),
            ("MINIO_USERNAME", "wiring-access"),
            ("MINIO_PASS", "wiring-secret"),
            ("MINIO_BUCKET_HEALTH_EXAM", "wiring-test"));

        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().Build();

        var ex = Assert.Throws<InvalidOperationException>(() =>
            services.AddHealthExamInfrastructure(configuration));

        Assert.Contains("SIGN_SERVER_BASE_URL", ex.Message);
    }
}
