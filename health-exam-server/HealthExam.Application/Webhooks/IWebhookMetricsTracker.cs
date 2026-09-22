namespace HealthExam.Application.Webhooks;

public sealed record WebhookMetricsSnapshot(
    long Received,
    long Duplicated,
    long UnknownEvent,
    long IgnoredEvent,
    long Applied,
    long StaleSkipped,
    long InvalidTransition,
    long RecordNotFound,
    long Failed,
    long DeadLettered,
    long ReconcileHealed);

public interface IWebhookMetricsTracker
{
    long Received { get; }
    long Duplicated { get; }
    long UnknownEvent { get; }
    long IgnoredEvent { get; }
    long Applied { get; }
    long StaleSkipped { get; }
    long InvalidTransition { get; }
    long RecordNotFound { get; }
    long Failed { get; }
    long DeadLettered { get; }
    long ReconcileHealed { get; }

    void CountReceived();
    void CountDuplicated();
    void CountUnknownEvent();
    void CountIgnoredEvent();
    void CountApplied();
    void CountStaleSkipped();
    void CountInvalidTransition();
    void CountRecordNotFound();
    void CountFailed();
    void CountDeadLettered();
    void CountReconcileHealed(int n = 1);

    WebhookMetricsSnapshot GetSnapshot();
}
