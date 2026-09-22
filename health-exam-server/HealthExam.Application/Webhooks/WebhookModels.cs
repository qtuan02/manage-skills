using System;
using System.Collections.Generic;
using HealthExam.Domain.Common;
using HealthExam.Domain.Webhooks;

namespace HealthExam.Application.Webhooks;

public sealed record IngestWebhookCommand(
    string EventID,
    string EventType,
    string DivisionID,
    Guid? SubmissionID,
    string HostRefType,
    string HostRefID,
    string SubjectID,
    DateTimeOffset? OccurredAt,
    string RawPayload,
    string TraceId = "",
    string HeaderDivisionId = "");

public sealed record ProcessWebhookBatchCommand(int BatchSize = 20);

public sealed record ProcessWebhookBatchResult(int ProcessedCount);

public sealed record RequeueWebhooksCommand(
    string DivisionId,
    string EventId = null,
    int Max = 100);

public sealed record BackfillScanResultsCommand(
    string DivisionId,
    int Max = 100,
    DateTime? Before = null);

public sealed record GetWebhookMetricsQuery();

public sealed record ReconcileProgressCommand(
    string DivisionId = null,
    Guid? SessionId = null,
    int BatchSize = 100);

public sealed record WebhookRequeueFilter(
    string DivisionId,
    WebhookProcessState? ProcessState = null,
    short? MinRetryCount = null,
    string EventId = null,
    IReadOnlyList<string> EventTypes = null,
    DateTime? ProcessedBefore = null,
    int Take = 100);

public enum WebhookOutcome
{
    Applied,
    Skipped,
    Failed
}

public sealed class WebhookProcessResult
{
    public WebhookOutcome Outcome { get; init; }
    public string Detail { get; init; } = "";
    public bool RecordNotFound { get; init; }
    public bool StaleSkipped { get; init; }
    public bool InvalidTransition { get; init; }

    public static WebhookProcessResult Applied(string detail) => new()
        { Outcome = WebhookOutcome.Applied, Detail = detail };

    public static WebhookProcessResult Skipped(string detail) => new()
        { Outcome = WebhookOutcome.Skipped, Detail = detail };

    public static WebhookProcessResult Failed(string detail) => new()
        { Outcome = WebhookOutcome.Failed, Detail = detail };

    public void Deconstruct(out WebhookOutcome outcome, out string detail)
    {
        outcome = Outcome;
        detail = Detail;
    }
}

public class FormWebhookEvent
{
    public string EventID { get; set; } = "";
    public string Event { get; set; } = "";
    public DateTimeOffset? OccurredAt { get; set; }
    public Guid? SubmissionID { get; set; }
    public string HostRefType { get; set; } = "";
    public string DivisionID { get; set; } = "";
    public string HostRefID { get; set; } = "";
    public string SubjectID { get; set; } = "";
    public FormWebhookData Data { get; set; }
}

public class FormWebhookData
{
    public long SectionID { get; set; }
    public string SectionKind { get; set; } = "";
    public string AttachKind { get; set; } = "";
    public short State { get; set; }
    public short? ToState { get; set; }
    public short StateOrToState => State != 0 ? State : (ToState ?? 0);
    public short ActorKind { get; set; }
    public long ActorID { get; set; }
    public FormProgressCount Progress { get; set; }
    public string HealthClassCode { get; set; } = "";
    public Guid? AttachmentID { get; set; }
    public Guid? OrderItemID { get; set; }
    public long? ServiceID { get; set; }
    public string ServiceCode { get; set; }
    public bool? IsAbnormal { get; set; }
    public Dictionary<string, object> Extra { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, object> Metadata { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public object Find(string key)
    {
        if (Extra != null && Extra.TryGetValue(key, out var extraVal))
            return extraVal;
        if (Metadata != null && Metadata.TryGetValue(key, out var metaVal))
            return metaVal;
        return null;
    }
}

public class FormProgressCount
{
    public short Completed { get; set; }
    public short Total { get; set; }
}

public static class FormEventNames
{
    public const string SectionSigned = "submission.section.signed";
    public const string StateChanged = "submission.state.changed";
    public const string SectionSignCancelled = "submission.section.sign.cancelled";
    public const string AttachmentAdded = "submission.attachment.added";
    public const string AttachmentRemoved = "submission.attachment.removed";
    public const string SubmissionCreated = "submission.created";
    public const string SectionSaved = "submission.section.saved";

    private static readonly HashSet<string> Known = new(StringComparer.Ordinal)
    {
        SectionSigned, StateChanged, SectionSignCancelled, AttachmentAdded, AttachmentRemoved
    };

    private static readonly HashSet<string> KnownIgnored = new(StringComparer.Ordinal)
    {
        SubmissionCreated, SectionSaved
    };

    public static bool IsKnown(string eventName) => Known.Contains(eventName ?? "");
    public static bool IsKnownIgnored(string eventName) => KnownIgnored.Contains(eventName ?? "");
}

public static class SectionKinds
{
    public const string History = "HISTORY";
    public const string ClinicalExam = "CLINICAL_EXAM";
    public const string Conclusion = "CONCLUSION";
}

public static class AttachKinds
{
    public const string ScanResult = "SCAN_RESULT";
}

public static class FormSubmissionStates
{
    public const short Draft = 0;
    public const short InProgress = 1;
    public const short Completed = 2;
    public const short Cancelled = 3;
}

public class WebhookAckResult
{
    public string EventID { get; set; } = "";
    public bool Duplicated { get; set; }
    public bool Accepted { get; set; }
}

public class WebhookRequeueResult
{
    public int Requeued { get; set; }
    public int Remaining { get; set; }
    public DateTime? Cursor { get; set; }
    public List<string> EventIDs { get; set; } = new();
}

public class FormSubmissionProgressDto
{
    public FormProgressCount Sections { get; set; }
    public short? SubmissionState { get; set; }
    public bool CanSignConclusion { get; set; }
    public string HealthClassCode { get; set; }
}

public class ServiceCallOrigin
{
    public string DivisionId { get; init; } = "";
    public string TraceId { get; init; } = "";
}

public class ReconcileResult
{
    public int Examined { get; set; }
    public int Healed { get; set; }
    public int ProgressRefreshed { get; set; }
    public int Unreachable { get; set; }
    public int Undecidable { get; set; }
    public int Failed { get; set; }
}
