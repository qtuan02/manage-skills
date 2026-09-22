using System.Threading;
using HealthExam.Application.Webhooks;

namespace HealthExam.Infrastructure.Services;

public class InMemoryWebhookMetricsTracker : IWebhookMetricsTracker
{
    private long _received;
    private long _duplicated;
    private long _unknownEvent;
    private long _ignoredEvent;
    private long _applied;
    private long _staleSkipped;
    private long _invalidTransition;
    private long _recordNotFound;
    private long _failed;
    private long _deadLettered;
    private long _reconcileHealed;

    public long Received => Interlocked.Read(ref _received);
    public long Duplicated => Interlocked.Read(ref _duplicated);
    public long UnknownEvent => Interlocked.Read(ref _unknownEvent);
    public long IgnoredEvent => Interlocked.Read(ref _ignoredEvent);
    public long Applied => Interlocked.Read(ref _applied);
    public long StaleSkipped => Interlocked.Read(ref _staleSkipped);
    public long InvalidTransition => Interlocked.Read(ref _invalidTransition);
    public long RecordNotFound => Interlocked.Read(ref _recordNotFound);
    public long Failed => Interlocked.Read(ref _failed);
    public long DeadLettered => Interlocked.Read(ref _deadLettered);
    public long ReconcileHealed => Interlocked.Read(ref _reconcileHealed);

    public void CountReceived() => Interlocked.Increment(ref _received);
    public void CountDuplicated() => Interlocked.Increment(ref _duplicated);
    public void CountUnknownEvent() => Interlocked.Increment(ref _unknownEvent);
    public void CountIgnoredEvent() => Interlocked.Increment(ref _ignoredEvent);
    public void CountApplied() => Interlocked.Increment(ref _applied);
    public void CountStaleSkipped() => Interlocked.Increment(ref _staleSkipped);
    public void CountInvalidTransition() => Interlocked.Increment(ref _invalidTransition);
    public void CountRecordNotFound() => Interlocked.Increment(ref _recordNotFound);
    public void CountFailed() => Interlocked.Increment(ref _failed);
    public void CountDeadLettered() => Interlocked.Increment(ref _deadLettered);
    public void CountReconcileHealed(int n = 1) => Interlocked.Add(ref _reconcileHealed, n);

    public WebhookMetricsSnapshot GetSnapshot() => new(
        Received,
        Duplicated,
        UnknownEvent,
        IgnoredEvent,
        Applied,
        StaleSkipped,
        InvalidTransition,
        RecordNotFound,
        Failed,
        DeadLettered,
        ReconcileHealed);
}
