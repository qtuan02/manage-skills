using System;
using System.Net.Http;
using HealthExam.Application.Auth;
using HealthExam.Application.Catalogs;
using HealthExam.Application.Common;
using HealthExam.Application.ExamRecords;
using HealthExam.Application.ExamSessions;
using HealthExam.Application.Imports;
using HealthExam.Application.Integrations;
using HealthExam.Application.Paraclinical;
using HealthExam.Application.Patients;
using HealthExam.Application.RegistrationForms;
using HealthExam.Application.Signing;
using HealthExam.Application.Webhooks;
using HealthExam.Infrastructure.BackgroundJobs;
using HealthExam.Infrastructure.Caching;
using HealthExam.Infrastructure.Excel;
using HealthExam.Infrastructure.Integrations.FormServer;
using HealthExam.Infrastructure.Integrations.HisEmr;
using HealthExam.Infrastructure.Integrations.Ris;
using HealthExam.Infrastructure.Integrations.SignServer;
using HealthExam.Infrastructure.Integrations.Ssm;
using HealthExam.Infrastructure.Persistence;
using HealthExam.Infrastructure.Persistence.Repositories;
using HealthExam.Infrastructure.Services;
using HealthExam.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HealthExam.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddHealthExamInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = Environment.GetEnvironmentVariable("HEALTHEXAM_DB");
        if (string.IsNullOrWhiteSpace(connectionString) && configuration != null)
        {
            connectionString = configuration.GetConnectionString("HealthExamDb")
                ?? configuration.GetConnectionString("DefaultConnection");
        }
        connectionString ??= "";

        services.AddDbContext<HealthExamDbContext>(options =>
        {
            options.UseNpgsql(connectionString);
            options.UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);
        });

        // Unit of Work
        services.AddScoped<IUnitOfWork, UnitOfWork>();

        // Repositories
        services.AddScoped<ICatalogRepository, CatalogRepository>();
        services.AddScoped<IExamSessionRepository, ExamSessionRepository>();
        services.AddScoped<IExamRecordRepository, ExamRecordRepository>();
        services.AddScoped<IImportRepository, ImportRepository>();
        services.AddScoped<IParaclinicalRepository, ParaclinicalRepository>();
        services.AddScoped<IIntegrationOutboxRepository, IntegrationOutboxRepository>();
        services.AddScoped<IWebhookInboxRepository, WebhookInboxRepository>();
        services.AddScoped<IAuditRepository, AuditRepository>();
        services.AddScoped<IRegistrationFormRepository, RegistrationFormRepository>();
        services.AddScoped<IPatientRepository, PatientRepository>();
        services.AddScoped<PatientRegistrationWriter>();

        // Allocators
        services.AddScoped<IRecordCodeAllocator, RecordCodeAllocator>();
        services.AddScoped<IOrderNoAllocator, SequenceOrderNoAllocator>();
        services.AddScoped<IVendorLineNoAllocator, SequenceVendorLineNoAllocator>();

        // Workbook reader
        services.AddScoped<IExamWorkbookReader, ExamWorkbookReader>();

        // Caching
        services.AddMemoryCache();
        services.AddSingleton<IHisFormDefinitionCache, HisFormDefinitionCache>();

        // HTTP Clients & Integration Clients
        // HIS EMR
        var hisEmrOptions = HisEmrOptions.FromEnvironment();
        services.AddSingleton(hisEmrOptions);
        services.AddSingleton<IHisCredentialOptions>(sp => sp.GetRequiredService<HisEmrOptions>());
        services.AddHttpClient<HisEmrClient>((sp, http) =>
        {
            http.Timeout = sp.GetRequiredService<HisEmrOptions>().Timeout;
        }).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
        {
            AllowAutoRedirect = false
        });
        services.AddScoped<IHisEmrClient>(sp => sp.GetRequiredService<HisEmrClient>());

        // HIS Paraclinical Client
        services.AddHttpClient<HisParaclinicalClient>((sp, http) =>
        {
            http.Timeout = sp.GetRequiredService<HisEmrOptions>().Timeout;
        }).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
        {
            AllowAutoRedirect = false
        });
        services.AddScoped<IHisParaclinicalClient>(sp => sp.GetRequiredService<HisParaclinicalClient>());

        // Cổng xác thực HIS (IHisAuthGateway)
        services.AddHttpClient<HisHttpAuthGateway>((sp, http) =>
        {
            http.Timeout = sp.GetRequiredService<HisEmrOptions>().Timeout;
        }).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
        {
            AllowAutoRedirect = false
        });
        services.AddScoped<IHisAuthGateway>(sp => sp.GetRequiredService<HisHttpAuthGateway>());

        // RIS
        services.AddSingleton(_ => RisOptions.FromEnvironment());
        services.AddSingleton<IVendorCallbackAuthOptions>(sp => sp.GetRequiredService<RisOptions>());
        services.AddHttpClient<RisClient>(http =>
        {
            http.Timeout = RisOptions.DefaultTimeout;
        });
        services.AddScoped<IRisClient>(sp => sp.GetRequiredService<RisClient>());
        services.AddScoped<IRisPayloadBuilder, RisPayloadBuilder>();

        // Form Server
        services.AddSingleton(_ => FormServerOptions.FromEnvironment());
        services.AddHttpClient<FormServerClient>(http =>
        {
            http.Timeout = FormServerOptions.DefaultTimeout;
        });
        services.AddScoped<HealthExam.Application.Integrations.IFormServerClient>(sp =>
            sp.GetRequiredService<FormServerClient>());
        services.AddScoped<HealthExam.Application.Paraclinical.IFormServerClient>(sp =>
            sp.GetRequiredService<FormServerClient>());

        // Signing
        var signServerOptions = SignServerOptions.FromEnvironment();
        var examFileStoreOptions = ExamFileStoreOptions.FromEnvironment();
        SigningOptionsValidator.Validate(
            enabled: hisEmrOptions.Enabled,
            signServerBaseUrl: signServerOptions.SignServerBaseUrl,
            ssmBaseUrl: signServerOptions.SsmBaseUrl,
            minioEndpoint: examFileStoreOptions.Endpoint,
            minioBucket: examFileStoreOptions.Bucket,
            minioAccessKey: examFileStoreOptions.AccessKey,
            minioSecretKey: examFileStoreOptions.SecretKey);
        services.AddSingleton(signServerOptions);
        services.AddSingleton(examFileStoreOptions);
        services.AddScoped<ISignStepMapRepository, SignStepMapRepository>();
        // Tra cert chạy bên trong giao dịch giữ row lock của ký kết luận — không để ssm-server
        // treo giữ lock tới 100s (mặc định HttpClient).
        services.AddHttpClient<ICertificateGateway, SsmCertificateGateway>(http =>
            http.Timeout = TimeSpan.FromSeconds(10));
        services.AddHttpClient<IPdfSigner, SignServerPdfSigner>((sp, http) =>
            http.Timeout = TimeSpan.FromSeconds(sp.GetRequiredService<SignServerOptions>().TimeoutSeconds));
        // MinIO client dựng lazy bên trong store (xem MinioExamFileStore) — không đăng ký IMinioClient.
        // Singleton để giữ đúng một MinioClient (và HttpClient của nó) cho cả tiến trình như trước;
        // hai dependency (options instance, ILogger<T>) đều an toàn ở lifetime này.
        services.AddSingleton<IExamFileStore, MinioExamFileStore>();

        // Webhook Metrics Tracker
        services.AddSingleton<IWebhookMetricsTracker, InMemoryWebhookMetricsTracker>();

        // Paraclinical Outbox Dispatcher
        services.AddScoped<IParaclinicalOutboxDispatcher, ParaclinicalOutboxDispatcher>();

        // Background workers (only registered when connection string is not empty)
        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            services.AddHostedService<WebhookWorker>();
            services.AddHostedService<ProgressReconciliationWorker>();
            services.AddHostedService<IntegrationOutboxWorker>();
        }

        return services;
    }
}
