using System.Threading;
using System.Threading.Tasks;
using HealthExam.Application.Common;

namespace HealthExam.Application.Webhooks;

public interface IGetWebhookMetricsHandler
{
    Task<ApplicationResult<WebhookMetricsSnapshot>> HandleAsync(
        GetWebhookMetricsQuery query, CancellationToken ct = default);
}

public class GetWebhookMetricsHandler : IGetWebhookMetricsHandler
{
    private readonly IWebhookMetricsTracker _tracker;

    public GetWebhookMetricsHandler(IWebhookMetricsTracker tracker)
    {
        _tracker = tracker;
    }

    public Task<ApplicationResult<WebhookMetricsSnapshot>> HandleAsync(
        GetWebhookMetricsQuery query, CancellationToken ct = default)
    {
        var snapshot = _tracker.GetSnapshot();
        return Task.FromResult(ApplicationResult<WebhookMetricsSnapshot>.Success(snapshot));
    }
}
