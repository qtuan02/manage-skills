using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HealthExam.Application.Common;
using HealthExam.Application.ExamRecords;
using HealthExam.Application.Integrations;
using HealthExam.Application.Paraclinical;
using HealthExam.Application.Webhooks;
using HealthExam.Domain.Common;
using HealthExam.Domain.ExamRecords;
using HealthExam.Domain.ExamSessions;
using HealthExam.Domain.Paraclinical;
using HealthExam.Domain.Webhooks;
using Newtonsoft.Json;
using Xunit;

namespace HealthExam.Tests.Application;

public class WebhookHandlerTests
{
    private const string DivisionId = "D01";

    [Fact]
    public async Task Duplicate_webhook_ingestion_returns_success_with_duplicated_true_without_second_insert()
    {
        var inboxRepo = new FakeWebhookInboxRepository();
        inboxRepo.ExistingEventIds.Add((DivisionId, "EVT-001"));
        var tracker = new FakeWebhookMetricsTracker();
        var uow = new FakeUnitOfWork();
        var handler = new IngestWebhookHandler(inboxRepo, uow, tracker);

        var cmd = new IngestWebhookCommand(
            EventID: "EVT-001",
            EventType: FormEventNames.SectionSigned,
            DivisionID: DivisionId,
            SubmissionID: Guid.NewGuid(),
            HostRefType: ModuleCodes.HostRefType,
            HostRefID: "SES-001",
            SubjectID: "REC-001",
            OccurredAt: DateTimeOffset.UtcNow,
            RawPayload: "{\"EventID\":\"EVT-001\"}");

        var result = await handler.HandleAsync(cmd);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.Duplicated);
        Assert.Empty(inboxRepo.AddedRows);
        Assert.Equal(1, tracker.Duplicated);
    }

    [Fact]
    public async Task Unknown_event_types_are_marked_skipped_without_retrying()
    {
        var inboxRepo = new FakeWebhookInboxRepository();
        var tracker = new FakeWebhookMetricsTracker();
        var uow = new FakeUnitOfWork();
        var handler = new IngestWebhookHandler(inboxRepo, uow, tracker);

        var cmd = new IngestWebhookCommand(
            EventID: "EVT-UNKNOWN",
            EventType: "custom.unknown.event",
            DivisionID: DivisionId,
            SubmissionID: Guid.NewGuid(),
            HostRefType: ModuleCodes.HostRefType,
            HostRefID: "SES-001",
            SubjectID: "REC-001",
            OccurredAt: DateTimeOffset.UtcNow,
            RawPayload: "{\"EventID\":\"EVT-UNKNOWN\"}");

        var result = await handler.HandleAsync(cmd);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.Duplicated);
        Assert.False(result.Value.Accepted);
        var inserted = Assert.Single(inboxRepo.AddedRows);
        Assert.Equal(WebhookProcessState.Skipped, inserted.ProcessState);
        Assert.Equal(1, tracker.UnknownEvent);
    }

    [Fact]
    public async Task Stale_events_occurred_before_last_event_at_are_marked_skipped()
    {
        var now = new DateTime(2026, 9, 10, 10, 0, 0, DateTimeKind.Utc);
        var record = new ExamRecord
        {
            RecordID = Guid.NewGuid(),
            DivisionID = DivisionId,
            RecordCode = "REC-001",
            State = ExamRecordState.InProgress,
            LastEventAt = now
        };

        var row = new WebhookInbox
        {
            InboxID = 1,
            EventID = "EVT-STALE",
            EventType = FormEventNames.SectionSigned,
            DivisionID = DivisionId,
            OccurredAt = now.AddMinutes(-5),
            Payload = JsonConvert.SerializeObject(new FormWebhookEvent
            {
                EventID = "EVT-STALE",
                Event = FormEventNames.SectionSigned,
                SubjectID = "REC-001",
                Data = new FormWebhookData { SectionKind = SectionKinds.ClinicalExam }
            }),
            ProcessState = WebhookProcessState.New
        };

        var inboxRepo = new FakeWebhookInboxRepository(row);
        var recordRepo = new FakeExamRecordRepository(record);
        var paraclinicalRepo = new FakeParaclinicalRepository();
        var uow = new FakeUnitOfWork();
        var audit = new FakeAuditRepository();
        var tracker = new FakeWebhookMetricsTracker();
        var clock = new FakeClock(now);

        var handler = new ProcessWebhookBatchHandler(inboxRepo, recordRepo, paraclinicalRepo, uow, audit, tracker, clock);

        var result = await handler.HandleAsync(new ProcessWebhookBatchCommand(1));

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value.ProcessedCount);
        Assert.Equal(WebhookProcessState.Skipped, row.ProcessState);
        Assert.Equal(1, tracker.StaleSkipped);
    }

    [Fact]
    public async Task Record_not_found_fails_with_retry_scheduled()
    {
        var now = new DateTime(2026, 9, 10, 10, 0, 0, DateTimeKind.Utc);
        var row = new WebhookInbox
        {
            InboxID = 2,
            EventID = "EVT-NO-REC",
            EventType = FormEventNames.StateChanged,
            DivisionID = DivisionId,
            OccurredAt = now,
            Payload = JsonConvert.SerializeObject(new FormWebhookEvent
            {
                EventID = "EVT-NO-REC",
                Event = FormEventNames.StateChanged,
                SubjectID = "NON-EXISTENT-REC",
                Data = new FormWebhookData { State = 1 }
            }),
            ProcessState = WebhookProcessState.New
        };

        var inboxRepo = new FakeWebhookInboxRepository(row);
        var recordRepo = new FakeExamRecordRepository(); // empty
        var paraclinicalRepo = new FakeParaclinicalRepository();
        var uow = new FakeUnitOfWork();
        var audit = new FakeAuditRepository();
        var tracker = new FakeWebhookMetricsTracker();
        var clock = new FakeClock(now);

        var handler = new ProcessWebhookBatchHandler(inboxRepo, recordRepo, paraclinicalRepo, uow, audit, tracker, clock);

        var result = await handler.HandleAsync(new ProcessWebhookBatchCommand(1));

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value.ProcessedCount);
        Assert.Equal(WebhookProcessState.Failed, row.ProcessState);
        Assert.Equal(1, row.RetryCount);
        Assert.NotNull(row.NextAttemptAt);
        Assert.True(row.NextAttemptAt > now);
        Assert.Equal(1, tracker.RecordNotFound);
        Assert.Equal(1, tracker.Failed);
    }

    [Fact]
    public async Task Batch_processing_consumes_pending_webhooks_applies_transitions_and_records_metrics()
    {
        var now = new DateTime(2026, 9, 10, 10, 0, 0, DateTimeKind.Utc);
        var record = new ExamRecord
        {
            RecordID = Guid.NewGuid(),
            DivisionID = DivisionId,
            RecordCode = "REC-001",
            State = ExamRecordState.Waiting
        };

        var row = new WebhookInbox
        {
            InboxID = 3,
            EventID = "EVT-VALID",
            EventType = FormEventNames.StateChanged,
            DivisionID = DivisionId,
            OccurredAt = now,
            Payload = JsonConvert.SerializeObject(new FormWebhookEvent
            {
                EventID = "EVT-VALID",
                Event = FormEventNames.StateChanged,
                SubjectID = "REC-001",
                Data = new FormWebhookData { State = 1 } // InProgress
            }),
            ProcessState = WebhookProcessState.New
        };

        var inboxRepo = new FakeWebhookInboxRepository(row);
        var recordRepo = new FakeExamRecordRepository(record);
        var paraclinicalRepo = new FakeParaclinicalRepository();
        var uow = new FakeUnitOfWork();
        var audit = new FakeAuditRepository();
        var tracker = new FakeWebhookMetricsTracker();
        var clock = new FakeClock(now);

        var handler = new ProcessWebhookBatchHandler(inboxRepo, recordRepo, paraclinicalRepo, uow, audit, tracker, clock);

        var result = await handler.HandleAsync(new ProcessWebhookBatchCommand(1));

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value.ProcessedCount);
        Assert.Equal(WebhookProcessState.Processed, row.ProcessState);
        Assert.Equal(ExamRecordState.InProgress, record.State);
        Assert.Equal(1, tracker.Applied);
    }

    [Fact]
    public async Task ProcessWebhookBatch_recovers_from_exception_stamps_failed_in_clean_transaction()
    {
        var now = new DateTime(2026, 9, 10, 10, 0, 0, DateTimeKind.Utc);
        var row = new WebhookInbox
        {
            InboxID = 42,
            EventID = "EVT-EX",
            EventType = FormEventNames.StateChanged,
            DivisionID = DivisionId,
            OccurredAt = now,
            Payload = JsonConvert.SerializeObject(new FormWebhookEvent
            {
                EventID = "EVT-EX",
                Event = FormEventNames.StateChanged,
                SubjectID = "REC-FAIL",
                Data = new FormWebhookData { State = 1 }
            }),
            ProcessState = WebhookProcessState.New
        };

        var inboxRepo = new FakeWebhookInboxRepository(row);
        var recordRepo = new ThrowingExamRecordRepository();
        var paraclinicalRepo = new FakeParaclinicalRepository();
        var uow = new FakeUnitOfWork();
        var audit = new FakeAuditRepository();
        var tracker = new FakeWebhookMetricsTracker();
        var clock = new FakeClock(now);

        var handler = new ProcessWebhookBatchHandler(inboxRepo, recordRepo, paraclinicalRepo, uow, audit, tracker, clock);

        var result = await handler.HandleAsync(new ProcessWebhookBatchCommand(1));

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value.ProcessedCount);
        Assert.Equal(WebhookProcessState.Failed, row.ProcessState);
        Assert.Equal(1, row.RetryCount);
        Assert.Equal("Database explosion", row.LastError);
        Assert.Equal(1, tracker.Failed);
    }

    [Fact]
    public async Task RequeueWebhooks_calculates_remaining_using_filter_event_id()
    {
        var row1 = new WebhookInbox
        {
            InboxID = 10,
            EventID = "EVT-TARGET",
            DivisionID = DivisionId,
            ProcessState = WebhookProcessState.Failed,
            RetryCount = WebhookInbox.MaxRetry
        };
        var row2 = new WebhookInbox
        {
            InboxID = 11,
            EventID = "EVT-OTHER",
            DivisionID = DivisionId,
            ProcessState = WebhookProcessState.Failed,
            RetryCount = WebhookInbox.MaxRetry
        };

        var inboxRepo = new FakeWebhookInboxRepository(row1, row2);
        var uow = new FakeUnitOfWork();

        var handler = new RequeueWebhooksHandler(inboxRepo, uow);
        var result = await handler.HandleAsync(new RequeueWebhooksCommand(DivisionId, "EVT-TARGET", 100));

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value.Requeued);
        Assert.Equal(0, result.Value.Remaining);
        Assert.Equal(WebhookProcessState.New, row1.ProcessState);
        Assert.Equal(WebhookProcessState.Failed, row2.ProcessState);
    }
}

public class ThrowingExamRecordRepository : FakeExamRecordRepository, IExamRecordRepository
{
    Task<ExamRecord> IExamRecordRepository.ResolveForWebhookAsync(
        string divisionId, Guid? submissionId, string subjectId, string hostRefId, bool forUpdate, CancellationToken ct)
    {
        throw new InvalidOperationException("Database explosion");
    }
}

public class FakeWebhookInboxRepository : IWebhookInboxRepository
{
    public HashSet<(string DivisionId, string EventId)> ExistingEventIds { get; } = new();
    public List<WebhookInbox> AddedRows { get; } = new();
    public List<WebhookInbox> Queue { get; } = new();

    public FakeWebhookInboxRepository(params WebhookInbox[] rows)
    {
        Queue.AddRange(rows);
    }

    public Task<bool> ExistsAsync(string divisionId, string eventId, CancellationToken ct = default)
    {
        return Task.FromResult(ExistingEventIds.Contains((divisionId, eventId)) ||
                               Queue.Any(x => x.DivisionID == divisionId && x.EventID == eventId) ||
                               AddedRows.Any(x => x.DivisionID == divisionId && x.EventID == eventId));
    }

    public Task<bool> ExistsAsync(string eventId, CancellationToken ct = default)
    {
        return Task.FromResult(ExistingEventIds.Any(x => x.EventId == eventId) ||
                               Queue.Any(x => x.EventID == eventId) ||
                               AddedRows.Any(x => x.EventID == eventId));
    }

    public Task<WebhookInbox> ClaimNextAsync(DateTime nowUtc, CancellationToken ct = default)
    {
        var candidate = Queue.FirstOrDefault(x =>
            x.ProcessState == WebhookProcessState.New ||
            (x.ProcessState == WebhookProcessState.Failed && x.RetryCount < WebhookInbox.MaxRetry &&
             (x.NextAttemptAt == null || x.NextAttemptAt <= nowUtc)));

        return Task.FromResult(candidate);
    }

    public Task<WebhookInbox> LockAsync(long inboxId, CancellationToken ct = default)
    {
        var row = Queue.FirstOrDefault(x => x.InboxID == inboxId) ?? AddedRows.FirstOrDefault(x => x.InboxID == inboxId);
        if (row == null && Queue.Count == 1) row = Queue[0];
        return Task.FromResult(row);
    }

    public Task<IReadOnlyList<WebhookInbox>> FindForRequeueAsync(WebhookRequeueFilter filter, CancellationToken ct = default)
    {
        IEnumerable<WebhookInbox> q = Queue.Concat(AddedRows);
        if (!string.IsNullOrEmpty(filter.DivisionId))
            q = q.Where(x => x.DivisionID == filter.DivisionId);
        if (filter.ProcessState.HasValue)
            q = q.Where(x => x.ProcessState == filter.ProcessState.Value);
        if (filter.MinRetryCount.HasValue)
            q = q.Where(x => x.RetryCount >= filter.MinRetryCount.Value);
        if (!string.IsNullOrEmpty(filter.EventId))
            q = q.Where(x => x.EventID == filter.EventId);
        if (filter.EventTypes != null && filter.EventTypes.Count > 0)
            q = q.Where(x => filter.EventTypes.Contains(x.EventType));
        if (filter.ProcessedBefore.HasValue)
            q = q.Where(x => x.ProcessedAt == null || x.ProcessedAt < filter.ProcessedBefore.Value);

        var list = q.OrderBy(x => x.ReceivedAt).Take(filter.Take).ToList();
        return Task.FromResult<IReadOnlyList<WebhookInbox>>(list);
    }

    public Task<int> CountForRequeueAsync(WebhookRequeueFilter filter, CancellationToken ct = default)
    {
        IEnumerable<WebhookInbox> q = Queue.Concat(AddedRows);
        if (!string.IsNullOrEmpty(filter.DivisionId))
            q = q.Where(x => x.DivisionID == filter.DivisionId);
        if (filter.ProcessState.HasValue)
            q = q.Where(x => x.ProcessState == filter.ProcessState.Value);
        if (filter.MinRetryCount.HasValue)
            q = q.Where(x => x.RetryCount >= filter.MinRetryCount.Value);
        if (!string.IsNullOrEmpty(filter.EventId))
            q = q.Where(x => x.EventID == filter.EventId);
        if (filter.EventTypes != null && filter.EventTypes.Count > 0)
            q = q.Where(x => filter.EventTypes.Contains(x.EventType));
        if (filter.ProcessedBefore.HasValue)
            q = q.Where(x => x.ProcessedAt == null || x.ProcessedAt < filter.ProcessedBefore.Value);

        return Task.FromResult(q.Count());
    }

    public Task<int> CountPendingAsync(string divisionId = null, CancellationToken ct = default)
    {
        var count = Queue.Concat(AddedRows).Count(x =>
            (divisionId == null || x.DivisionID == divisionId) &&
            (x.ProcessState == WebhookProcessState.New ||
             (x.ProcessState == WebhookProcessState.Failed && x.RetryCount < WebhookInbox.MaxRetry)));
        return Task.FromResult(count);
    }

    public Task<int> CountDeadLetterAsync(string divisionId = null, CancellationToken ct = default)
    {
        var count = Queue.Concat(AddedRows).Count(x =>
            (divisionId == null || x.DivisionID == divisionId) &&
            x.ProcessState == WebhookProcessState.Failed && x.RetryCount >= WebhookInbox.MaxRetry);
        return Task.FromResult(count);
    }

    public Task<bool> HasAttachmentAddedBeenProcessedAsync(
        string divisionId, Guid recordId, Guid attachmentId, long currentInboxId, CancellationToken ct = default)
    {
        return Task.FromResult(false);
    }

    public void Add(WebhookInbox row)
    {
        AddedRows.Add(row);
    }
}

public class FakeWebhookMetricsTracker : IWebhookMetricsTracker
{
    public long Received { get; private set; }
    public long Duplicated { get; private set; }
    public long UnknownEvent { get; private set; }
    public long IgnoredEvent { get; private set; }
    public long Applied { get; private set; }
    public long StaleSkipped { get; private set; }
    public long InvalidTransition { get; private set; }
    public long RecordNotFound { get; private set; }
    public long Failed { get; private set; }
    public long DeadLettered { get; private set; }
    public long ReconcileHealed { get; private set; }

    public void CountReceived() => Received++;
    public void CountDuplicated() => Duplicated++;
    public void CountUnknownEvent() => UnknownEvent++;
    public void CountIgnoredEvent() => IgnoredEvent++;
    public void CountApplied() => Applied++;
    public void CountStaleSkipped() => StaleSkipped++;
    public void CountInvalidTransition() => InvalidTransition++;
    public void CountRecordNotFound() => RecordNotFound++;
    public void CountFailed() => Failed++;
    public void CountDeadLettered() => DeadLettered++;
    public void CountReconcileHealed(int n = 1) => ReconcileHealed += n;

    public WebhookMetricsSnapshot GetSnapshot() => new(
        Received, Duplicated, UnknownEvent, IgnoredEvent, Applied, StaleSkipped,
        InvalidTransition, RecordNotFound, Failed, DeadLettered, ReconcileHealed);
}
