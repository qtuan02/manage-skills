using System;
using System.Threading;
using System.Threading.Tasks;
using HealthExam.Application.Paraclinical;
using HealthExam.Domain.Integrations;
using HealthExam.Infrastructure.Integrations.Ris;
using HealthExam.Infrastructure.Persistence;
using HealthExam.Infrastructure.Persistence.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HealthExam.Infrastructure.BackgroundJobs;

public class IntegrationOutboxWorker : BackgroundService
{
    public const int BatchSize = 20;

    private static readonly TimeSpan IdleDelay = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan ErrorDelay = TimeSpan.FromSeconds(30);

    private readonly IServiceScopeFactory _scopes;
    private readonly RisOptions _options;
    private readonly ILogger<IntegrationOutboxWorker> _logger;

    public IntegrationOutboxWorker(
        IServiceScopeFactory scopes, RisOptions options, ILogger<IntegrationOutboxWorker> logger)
    {
        _scopes = scopes;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        if (!_options.IsDispatchConfigured)
        {
            _logger.LogInformation(
                "IntegrationOutboxWorker KHÔNG chạy: chưa cấu hình đủ {EnvNames}. "
                + "Chỉ định sẽ không được gửi sang RIS (POST …/dispatch trả 5021).",
                RisOptions.DispatchEnvNames);
            return;
        }

        _logger.LogInformation(
            "IntegrationOutboxWorker khởi động — mỗi lượt tối đa {BatchSize} gói, đích {BaseUrl}{Path}.",
            BatchSize, _options.BaseUrl, RisOptions.OrderPath);

        while (!ct.IsCancellationRequested)
        {
            int processed;
            try
            {
                processed = await RunOnceAsync(ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "IntegrationOutboxWorker lỗi một lượt, chờ {Delay}s rồi chạy tiếp.",
                    ErrorDelay.TotalSeconds);
                await Task.Delay(ErrorDelay, ct);
                continue;
            }

            if (processed == 0) await Task.Delay(IdleDelay, ct);
        }
    }

    public async Task<int> RunOnceAsync(CancellationToken ct)
    {
        await using var scope = _scopes.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetRequiredService<IProcessOutboxBatchHandler>();
        var result = await handler.HandleAsync(new ProcessOutboxBatchCommand(BatchSize, _options.DivisionId), ct);
        return result.IsSuccess ? result.Value.Processed : 0;
    }

    public static async Task<IntegrationOutbox> NextRowAsync(
        HealthExam.Infrastructure.Persistence.UnitOfWork uow, string divisionId, CancellationToken ct = default)
        => await new IntegrationOutboxRepository(uow.Context).ClaimNextAsync(divisionId, DateTime.UtcNow, ct);

    public static async Task<IntegrationOutbox> NextRowAsync(
        HealthExamDbContext db, string divisionId, CancellationToken ct = default)
        => await new IntegrationOutboxRepository(db).ClaimNextAsync(divisionId, DateTime.UtcNow, ct);

    public static async Task<IntegrationOutbox> LockRowAsync(
        HealthExam.Infrastructure.Persistence.UnitOfWork uow, long outboxId, CancellationToken ct = default)
        => await new IntegrationOutboxRepository(uow.Context).LockAsync(outboxId, ct);

    public static async Task<IntegrationOutbox> LockRowAsync(
        HealthExamDbContext db, long outboxId, CancellationToken ct = default)
        => await new IntegrationOutboxRepository(db).LockAsync(outboxId, ct);

    public static bool Claim(IntegrationOutbox row, DateTime nowUtc, ILogger logger = null)
    {
        var live = row.Claim(nowUtc);
        if (!live)
        {
            logger?.LogError(
                "Gói {OutboxID} ({Operation} phiếu, vendor {Vendor}) hỏng {RetryCount} lần, DỪNG thử lại. "
                + "VENDOR KHÔNG NHẬN ĐƯỢC CHỈ ĐỊNH NÀY — cần bấm [Gửi lại] hoặc xử lý tay: {Detail}",
                row.OutboxID, row.Operation, row.Vendor, row.RetryCount, row.LastError);
        }
        return live;
    }

    public static void Stamp(
        IntegrationOutbox row, bool success, string detail, string snippet, ILogger logger = null)
    {
        row.Stamp(success, detail, snippet, DateTime.UtcNow);
        if (!success)
        {
            logger?.LogWarning(
                "Gói outbox {OutboxID} ({Operation}) hỏng lượt {RetryCount}/{MaxRetry}, thử lại lúc {NextAttemptAt:O}: {Detail}",
                row.OutboxID, row.Operation, row.RetryCount, IntegrationOutbox.MaxRetry,
                row.NextAttemptAt, row.LastError);
        }
    }
}
