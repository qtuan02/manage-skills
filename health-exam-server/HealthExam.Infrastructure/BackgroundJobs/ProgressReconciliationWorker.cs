using System;
using System.Threading;
using System.Threading.Tasks;
using HealthExam.Application.Webhooks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HealthExam.Infrastructure.BackgroundJobs;

public class ProgressReconciliationWorker : BackgroundService
{
    public const string IntervalEnv = "HEALTHEXAM_RECONCILE_INTERVAL_MINUTES";
    private const int DefaultIntervalMinutes = 10;
    private static readonly TimeSpan StartupDelay = TimeSpan.FromMinutes(1);

    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<ProgressReconciliationWorker> _logger;

    public ProgressReconciliationWorker(IServiceScopeFactory scopes, ILogger<ProgressReconciliationWorker> logger)
    {
        _scopes = scopes;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var interval = IntervalFromEnvironment();
        _logger.LogInformation("Job đối soát tiến độ khởi động — chu kỳ {Interval} phút.", interval.TotalMinutes);

        try
        {
            await Task.Delay(StartupDelay, ct);

            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await using var scope = _scopes.CreateAsyncScope();
                    var handler = scope.ServiceProvider.GetRequiredService<IReconcileProgressHandler>();
                    await handler.HandleAsync(new ReconcileProgressCommand(), ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Lượt đối soát tiến độ hỏng, sẽ chạy lại ở chu kỳ sau.");
                }

                await Task.Delay(interval, ct);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // App stopping
        }
    }

    private TimeSpan IntervalFromEnvironment()
    {
        var raw = (Environment.GetEnvironmentVariable(IntervalEnv) ?? "").Trim();
        if (int.TryParse(raw, out var minutes) && minutes > 0)
            return TimeSpan.FromMinutes(minutes);

        if (raw.Length > 0)
        {
            _logger.LogWarning(
                "{Env}=\"{Raw}\" không phải số phút hợp lệ — dùng mặc định {Default} phút.",
                IntervalEnv, raw, DefaultIntervalMinutes);
        }

        return TimeSpan.FromMinutes(DefaultIntervalMinutes);
    }
}
