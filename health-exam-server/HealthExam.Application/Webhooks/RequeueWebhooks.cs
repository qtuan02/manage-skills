using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HealthExam.Application.Common;
using HealthExam.Domain.Common;
using HealthExam.Domain.Webhooks;

namespace HealthExam.Application.Webhooks;

public interface IRequeueWebhooksHandler
{
    Task<ApplicationResult<WebhookRequeueResult>> HandleAsync(
        RequeueWebhooksCommand command, CancellationToken ct = default);
}

public class RequeueWebhooksHandler : IRequeueWebhooksHandler
{
    public const int DefaultRequeue = 100;
    public const int MaxRequeue = 500;

    private readonly IWebhookInboxRepository _repository;
    private readonly IUnitOfWork _uow;

    public RequeueWebhooksHandler(
        IWebhookInboxRepository repository,
        IUnitOfWork uow)
    {
        _repository = repository;
        _uow = uow;
    }

    public async Task<ApplicationResult<WebhookRequeueResult>> HandleAsync(
        RequeueWebhooksCommand command, CancellationToken ct = default)
    {
        var take = command.Max <= 0 ? DefaultRequeue : Math.Min(command.Max, MaxRequeue);

        var filter = new WebhookRequeueFilter(
            DivisionId: command.DivisionId,
            ProcessState: WebhookProcessState.Failed,
            MinRetryCount: WebhookInbox.MaxRetry,
            EventId: string.IsNullOrWhiteSpace(command.EventId) ? null : command.EventId.Trim(),
            Take: take);

        var rows = await _repository.FindForRequeueAsync(filter, ct);

        foreach (var row in rows)
        {
            row.ProcessState = WebhookProcessState.New;
            row.RetryCount = 0;
            row.NextAttemptAt = null;
            row.LastError = "Được nạp lại bằng POST /v1/internal/webhook-requeue";
        }

        if (rows.Count > 0)
            await _uow.SaveChangesAsync(ct);

        var remaining = await _repository.CountForRequeueAsync(filter, ct);

        return ApplicationResult<WebhookRequeueResult>.Success(new WebhookRequeueResult
        {
            Requeued = rows.Count,
            Remaining = remaining,
            EventIDs = rows.Select(x => x.EventID).ToList()
        });
    }
}
