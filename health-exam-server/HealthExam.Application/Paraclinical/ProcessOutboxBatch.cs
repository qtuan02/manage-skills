using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HealthExam.Application.Common;
using HealthExam.Application.Integrations;
using HealthExam.Domain.Common;
using HealthExam.Domain.Integrations;
using HealthExam.Domain.Paraclinical;

namespace HealthExam.Application.Paraclinical;

public sealed record ProcessOutboxBatchCommand(
    int BatchSize = 20,
    string DivisionId = null);

public sealed record ProcessOutboxBatchResult(
    int Processed,
    int Sent,
    int Failed,
    int DeadLettered,
    int Remaining);

public interface IProcessOutboxBatchHandler
{
    Task<ApplicationResult<ProcessOutboxBatchResult>> HandleAsync(
        ProcessOutboxBatchCommand command, CancellationToken ct = default);
}

public class ProcessOutboxBatchHandler : IProcessOutboxBatchHandler
{
    private readonly IIntegrationOutboxRepository _outboxRepository;
    private readonly IParaclinicalRepository _paraclinicalRepository;
    private readonly IRisClient _risClient;
    private readonly IUnitOfWork _uow;
    private readonly IAuditRepository _auditRepository;
    private readonly IClock _clock;

    public ProcessOutboxBatchHandler(
        IIntegrationOutboxRepository outboxRepository,
        IParaclinicalRepository paraclinicalRepository,
        IRisClient risClient,
        IUnitOfWork uow,
        IAuditRepository auditRepository,
        IClock clock)
    {
        _outboxRepository = outboxRepository;
        _paraclinicalRepository = paraclinicalRepository;
        _risClient = risClient;
        _uow = uow;
        _auditRepository = auditRepository;
        _clock = clock;
    }

    public async Task<ApplicationResult<ProcessOutboxBatchResult>> HandleAsync(
        ProcessOutboxBatchCommand command, CancellationToken ct = default)
    {
        var batchSize = Math.Clamp(command.BatchSize, 1, 100);
        var divisionId = command.DivisionId ?? "";

        var processed = 0;
        var sent = 0;
        var failed = 0;
        var deadLettered = 0;

        for (var i = 0; i < batchSize; i++)
        {
            if (ct.IsCancellationRequested) break;

            var outcome = await RunOneAsync(divisionId, ct);
            if (outcome == RunOneOutcome.Empty) break;

            processed++;
            switch (outcome)
            {
                case RunOneOutcome.Sent:
                    sent++;
                    break;
                case RunOneOutcome.Failed:
                    failed++;
                    break;
                case RunOneOutcome.DeadLetter:
                    deadLettered++;
                    break;
            }
        }

        var now = _clock.UtcNow;
        var remaining = await _outboxRepository.CountPendingAsync(divisionId, now, ct);
        var totalDead = await _outboxRepository.CountDeadLetterAsync(divisionId, ct);

        return ApplicationResult<ProcessOutboxBatchResult>.Success(new ProcessOutboxBatchResult(
            processed, sent, failed, deadLettered, remaining));
    }

    private enum RunOneOutcome
    {
        Empty,
        Sent,
        Failed,
        DeadLetter
    }

    private sealed record ClaimedOutbox(
        long OutboxID,
        string DivisionID,
        Guid OrderID,
        string Vendor,
        string Operation,
        string Payload,
        short RetryCount,
        string TraceID);

    private async Task<RunOneOutcome> RunOneAsync(string divisionId, CancellationToken ct)
    {
        var now = _clock.UtcNow;
        ClaimedOutbox claimed;

        // ── Stage 1: Claim row in transaction ─────────────────────────────────────────
        await using (var tx = await _uow.BeginAsync(ct))
        {
            var row = await _outboxRepository.ClaimNextAsync(divisionId, now, ct);
            if (row == null)
            {
                await tx.CommitAsync(ct);
                return RunOneOutcome.Empty;
            }

            var live = row.Claim(now);
            await _uow.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            if (!live)
            {
                return RunOneOutcome.DeadLetter;
            }

            claimed = new ClaimedOutbox(
                row.OutboxID,
                row.DivisionID,
                row.OrderID,
                row.Vendor,
                row.Operation,
                row.Payload,
                row.RetryCount,
                row.TraceID);
        }

        // ── Stage 2: Call vendor outside transaction ──────────────────────────────────
        RisSendResult sendResult;
        try
        {
            sendResult = await _risClient.SendOrderAsync(claimed.Payload, claimed.OutboxID, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            sendResult = RisSendResult.DependencyFailure(ex.Message);
        }

        // ── Stage 3: Lock row and settle in separate transaction ──────────────────────
        await using (var tx = await _uow.BeginAsync(ct))
        {
            var row = await _outboxRepository.LockAsync(claimed.OutboxID, ct);
            if (row == null)
            {
                await tx.CommitAsync(ct);
                return sendResult.Success ? RunOneOutcome.Sent : RunOneOutcome.Failed;
            }

            if (row.State != OutboxState.Failed || row.RetryCount != claimed.RetryCount)
            {
                await tx.CommitAsync(ct);
                return sendResult.Success ? RunOneOutcome.Sent : RunOneOutcome.Failed;
            }

            var settleTime = _clock.UtcNow;
            if (sendResult.Success)
            {
                if (string.Equals(claimed.Operation, "NEW", StringComparison.OrdinalIgnoreCase))
                {
                    var order = await _paraclinicalRepository.GetAsync(
                        row.DivisionID, row.OrderID, includeItems: true, forUpdate: true, ct: ct);

                    if (order != null)
                    {
                        order.SentStatus = OrderSentStatus.Sent;
                        order.SentAt ??= settleTime;
                        order.ModifiedDate = settleTime;

                        foreach (var item in order.Items.Where(x => x.State == ParaclinicalItemState.Ordered && x.VendorLineNo.HasValue))
                        {
                            var from = item.State;
                            var transition = item.TransitionTo(
                                ParaclinicalItemState.Waiting, ParaclinicalStateSource.Internal, settleTime);
                            if (!transition.Applied) continue;

                            _auditRepository.Add(new AuditEntry(
                                order.DivisionID,
                                AuditEntityTypes.OrderItem,
                                item.OrderItemID,
                                AuditActions.StateChange,
                                "0",
                                (short)from,
                                (short)item.State,
                                new { order.OrderNo, item.ServiceCode, Source = ParaclinicalTargets.Ris },
                                ActorKind.Integration,
                                TraceId: claimed.TraceID));
                        }
                    }
                }

                row.Stamp(true, "Sent", sendResult.ResponseSnippet, settleTime);
            }
            else
            {
                row.Stamp(false, sendResult.Message, sendResult.ResponseSnippet, settleTime);
            }

            await _uow.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            return sendResult.Success ? RunOneOutcome.Sent : RunOneOutcome.Failed;
        }
    }
}
