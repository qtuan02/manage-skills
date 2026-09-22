using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HealthExam.Application.Common;
using HealthExam.Domain.Common;
using HealthExam.Domain.Webhooks;

namespace HealthExam.Application.Webhooks;

public interface IBackfillScanResultsHandler
{
    Task<ApplicationResult<WebhookRequeueResult>> HandleAsync(
        BackfillScanResultsCommand command, CancellationToken ct = default);
}

public class BackfillScanResultsHandler : IBackfillScanResultsHandler
{
    public const int DefaultRequeue = 100;
    public const int MaxRequeue = 500;

    private readonly IWebhookInboxRepository _repository;
    private readonly IUnitOfWork _uow;
    private readonly IClock _clock;

    public BackfillScanResultsHandler(
        IWebhookInboxRepository repository,
        IUnitOfWork uow,
        IClock clock)
    {
        _repository = repository;
        _uow = uow;
        _clock = clock;
    }

    public async Task<ApplicationResult<WebhookRequeueResult>> HandleAsync(
        BackfillScanResultsCommand command, CancellationToken ct = default)
    {
        var take = command.Max <= 0 ? DefaultRequeue : Math.Min(command.Max, MaxRequeue);

        var cursor = command.Before switch
        {
            null => _clock.UtcNow,
            { Kind: DateTimeKind.Utc } v => v,
            { Kind: DateTimeKind.Local } v => v.ToUniversalTime(),
            var v => DateTime.SpecifyKind(v.Value, DateTimeKind.Utc)
        };

        var filter = new WebhookRequeueFilter(
            DivisionId: command.DivisionId,
            ProcessState: WebhookProcessState.Skipped,
            EventTypes: new[] { FormEventNames.AttachmentAdded, FormEventNames.AttachmentRemoved },
            ProcessedBefore: cursor,
            Take: take);

        var rows = await _repository.FindForRequeueAsync(filter, ct);

        foreach (var row in rows)
        {
            row.ProcessState = WebhookProcessState.New;
            row.RetryCount = 0;
            row.NextAttemptAt = null;
            row.LastError = "Nạp lại bằng POST /v1/internal/scan-result-backfill (P3a)";
        }

        if (rows.Count > 0)
            await _uow.SaveChangesAsync(ct);

        var remaining = await _repository.CountForRequeueAsync(filter, ct);

        return ApplicationResult<WebhookRequeueResult>.Success(new WebhookRequeueResult
        {
            Requeued = rows.Count,
            Remaining = remaining,
            Cursor = cursor,
            EventIDs = rows.Select(x => x.EventID).ToList()
        });
    }
}
