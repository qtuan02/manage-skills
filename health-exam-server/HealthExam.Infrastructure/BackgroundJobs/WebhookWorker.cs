using System;
using System.Threading;
using System.Threading.Tasks;
using HealthExam.Application.Webhooks;
using HealthExam.Domain.Webhooks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HealthExam.Infrastructure.BackgroundJobs;

public class WebhookWorker : BackgroundService
{
    public const int BatchSize = 20;
    public const int MaxRetry = 5;

    private static readonly TimeSpan IdleDelay = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan ErrorDelay = TimeSpan.FromSeconds(30);

    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<WebhookWorker> _logger;

    public WebhookWorker(IServiceScopeFactory scopes, ILogger<WebhookWorker> logger)
    {
        _scopes = scopes;
        _logger = logger;
    }

    public static TimeSpan BackoffFor(int retryCount)
        => WebhookInbox.BackoffFor((short)retryCount);

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        _logger.LogInformation("WebhookWorker khởi động — mỗi lượt lấy tối đa {BatchSize} sự kiện.", BatchSize);

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
                _logger.LogError(ex, "WebhookWorker lỗi một lượt, chờ {Delay}s rồi chạy tiếp.", ErrorDelay.TotalSeconds);
                await Task.Delay(ErrorDelay, ct);
                continue;
            }

            if (processed == 0)
            {
                await Task.Delay(IdleDelay, ct);
            }
        }
    }

    public async Task<int> RunOnceAsync(CancellationToken ct)
    {
        await using var scope = _scopes.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetRequiredService<IProcessWebhookBatchHandler>();
        var result = await handler.HandleAsync(new ProcessWebhookBatchCommand(BatchSize), ct);
        return result.IsSuccess ? result.Value.ProcessedCount : 0;
    }
}
