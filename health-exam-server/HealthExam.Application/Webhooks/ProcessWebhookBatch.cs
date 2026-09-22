using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using HealthExam.Application.Common;
using HealthExam.Application.ExamRecords;
using HealthExam.Application.Paraclinical;
using HealthExam.Domain.Common;
using HealthExam.Domain.ExamRecords;
using HealthExam.Domain.Paraclinical;
using HealthExam.Domain.Webhooks;

namespace HealthExam.Application.Webhooks;

public interface IProcessWebhookBatchHandler
{
    Task<ApplicationResult<ProcessWebhookBatchResult>> HandleAsync(
        ProcessWebhookBatchCommand command, CancellationToken ct = default);
}

public class ProcessWebhookBatchHandler : IProcessWebhookBatchHandler
{
    private readonly IWebhookInboxRepository _inboxRepository;
    private readonly IExamRecordRepository _recordRepository;
    private readonly IParaclinicalRepository _paraclinicalRepository;
    private readonly IUnitOfWork _uow;
    private readonly IAuditRepository _auditRepository;
    private readonly IWebhookMetricsTracker _tracker;
    private readonly IClock _clock;

    public ProcessWebhookBatchHandler(
        IWebhookInboxRepository inboxRepository,
        IExamRecordRepository recordRepository,
        IParaclinicalRepository paraclinicalRepository,
        IUnitOfWork uow,
        IAuditRepository auditRepository,
        IWebhookMetricsTracker tracker,
        IClock clock)
    {
        _inboxRepository = inboxRepository;
        _recordRepository = recordRepository;
        _paraclinicalRepository = paraclinicalRepository;
        _uow = uow;
        _auditRepository = auditRepository;
        _tracker = tracker;
        _clock = clock;
    }

    public async Task<ApplicationResult<ProcessWebhookBatchResult>> HandleAsync(
        ProcessWebhookBatchCommand command, CancellationToken ct = default)
    {
        var batchSize = command.BatchSize <= 0 ? 20 : command.BatchSize;
        var processed = 0;

        for (var i = 0; i < batchSize; i++)
        {
            if (ct.IsCancellationRequested) break;

            var handled = await ProcessOneAsync(ct);
            if (!handled) break;

            processed++;
        }

        return ApplicationResult<ProcessWebhookBatchResult>.Success(new ProcessWebhookBatchResult(processed));
    }

    private async Task<bool> ProcessOneAsync(CancellationToken ct)
    {
        var nowUtc = _clock.UtcNow;
        WebhookInbox row = null;

        await using var tx = await _uow.BeginAsync(ct);
        try
        {
            row = await _inboxRepository.ClaimNextAsync(nowUtc, ct);
            if (row == null)
            {
                await tx.CommitAsync(ct);
                return false;
            }

            var result = await ProcessRowAsync(row, nowUtc, ct);

            if (result.Outcome == WebhookOutcome.Failed)
            {
                await tx.RollbackAsync(ct);
                _uow.DiscardPendingChanges();

                await using var failTx = await _uow.BeginAsync(ct);
                var failRow = await _inboxRepository.LockAsync(row.InboxID, ct);
                if (failRow != null && failRow.ProcessState is WebhookProcessState.New or WebhookProcessState.Failed)
                {
                    failRow.Stamp(WebhookProcessState.Failed, result.Detail, nowUtc);
                    await _uow.SaveChangesAsync(ct);
                    await failTx.CommitAsync(ct);

                    CountMetrics(result, failRow);
                }
                return true;
            }

            row.Stamp(
                result.Outcome == WebhookOutcome.Applied ? WebhookProcessState.Processed : WebhookProcessState.Skipped,
                result.Detail,
                nowUtc);

            await _uow.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            CountMetrics(result, row);
            return true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            if (row == null) throw;

            try
            {
                await tx.RollbackAsync(ct);
                _uow.DiscardPendingChanges();

                await using var failTx = await _uow.BeginAsync(ct);
                var failRow = await _inboxRepository.LockAsync(row.InboxID, ct);
                if (failRow != null && failRow.ProcessState is WebhookProcessState.New or WebhookProcessState.Failed)
                {
                    failRow.Stamp(WebhookProcessState.Failed, ex.Message, nowUtc);
                    await _uow.SaveChangesAsync(ct);
                    await failTx.CommitAsync(ct);

                    _tracker.CountFailed();
                    if (failRow.ProcessState == WebhookProcessState.Failed && failRow.RetryCount >= WebhookInbox.MaxRetry)
                        _tracker.CountDeadLettered();
                }
            }
            catch
            {
                // Suppress secondary failures
            }

            return true;
        }
    }

    public Task<WebhookProcessResult> ProcessAsync(WebhookInbox row, CancellationToken ct = default)
        => ProcessRowAsync(row, _clock.UtcNow, ct);

    public async Task<WebhookProcessResult> ProcessRowAsync(WebhookInbox row, DateTime nowUtc, CancellationToken ct = default)
    {
        if (!FormEventNames.IsKnown(row.EventType))
            return WebhookProcessResult.Skipped(
                FormEventNames.IsKnownIgnored(row.EventType)
                    ? "Sự kiện thuộc danh mục form-server nhưng KSK không tiêu thụ"
                    : "Tên sự kiện không thuộc bảng 02-api-spec §5.2");

        FormWebhookEvent evt;
        try
        {
            evt = ParseEvent(row.Payload);
        }
        catch (Exception ex)
        {
            return WebhookProcessResult.Skipped($"Payload không đọc được: {ex.Message}");
        }

        if (evt == null)
            return WebhookProcessResult.Skipped("Payload rỗng");

        var record = await _recordRepository.ResolveForWebhookAsync(
            row.DivisionID,
            evt.SubmissionID ?? row.SubmissionID,
            evt.SubjectID,
            evt.HostRefID,
            forUpdate: true,
            ct);

        if (record == null)
        {
            return new WebhookProcessResult
            {
                Outcome = WebhookOutcome.Failed,
                RecordNotFound = true,
                Detail = $"Không tra được hồ sơ cho HostRefID={evt.HostRefID}, SubjectID={evt.SubjectID}, "
                         + $"SubmissionID={evt.SubmissionID}, DivisionID={row.DivisionID}"
            };
        }

        row.RecordID = record.RecordID;

        if (!IsAttachmentEvent(row.EventType)
            && record.LastEventAt.HasValue && row.OccurredAt < record.LastEventAt.Value)
        {
            return new WebhookProcessResult
            {
                Outcome = WebhookOutcome.Skipped,
                StaleSkipped = true,
                Detail = "Sự kiện đến muộn hơn trạng thái hiện tại của hồ sơ"
            };
        }

        CopySubmissionId(row, evt, record);

        var applied = await ApplyAsync(row, evt, record, nowUtc, ct);

        if (applied.Outcome == WebhookOutcome.Applied && !IsAttachmentEvent(row.EventType))
        {
            record.LastEventAt = row.OccurredAt;
            record.ModifiedDate = nowUtc;
            record.ModifiedActorKind = ActorKind.Integration;
        }

        return applied;
    }

    private void CopySubmissionId(WebhookInbox row, FormWebhookEvent evt, ExamRecord record)
    {
        if (!evt.SubmissionID.HasValue || evt.SubmissionID.Value == Guid.Empty) return;

        if (!record.SubmissionID.HasValue || record.SubmissionID.Value == Guid.Empty)
        {
            record.SubmissionID = evt.SubmissionID;
        }
    }

    private async Task<WebhookProcessResult> ApplyAsync(
        WebhookInbox row, FormWebhookEvent evt, ExamRecord record, DateTime nowUtc, CancellationToken ct)
    {
        var data = evt.Data ?? new FormWebhookData();
        var kind = (data.SectionKind ?? "").Trim().ToUpperInvariant();

        switch (row.EventType)
        {
            case FormEventNames.SectionSigned when kind == SectionKinds.History:
                return Transition(row, record, ExamRecordState.NotRegistered, ExamRecordState.Waiting,
                    r => { r.RegisteredAt = row.OccurredAt; });

            case FormEventNames.StateChanged:
                if (data.StateOrToState != FormSubmissionStates.InProgress)
                    return WebhookProcessResult.Skipped(
                        $"submission.state.changed với State={data.StateOrToState} không thuộc bảng §5.2");
                return Transition(row, record, ExamRecordState.Waiting, ExamRecordState.InProgress,
                    r => { StampExamStarted(row, r); },
                    fillWhenAlreadyThere: r => StampExamStarted(row, r));

            case FormEventNames.SectionSigned when kind == SectionKinds.ClinicalExam:
                return UpdateProgress(row, data, record, nowUtc);

            case FormEventNames.SectionSigned when kind == SectionKinds.Conclusion:
                return Transition(row, record, ExamRecordState.InProgress, ExamRecordState.Completed,
                    r =>
                    {
                        r.ExamFinishedAt = row.OccurredAt;
                        r.ExamFinishedAtEstimated = false;
                        StampHealthClass(row, data, r);
                    },
                    fillWhenAlreadyThere: r =>
                    {
                        var filled = false;
                        if (string.IsNullOrWhiteSpace(r.HealthClassCode) && HealthClassOf(data) is { } hc)
                        {
                            r.HealthClassCode = hc;
                            filled = true;
                        }
                        filled |= StampExamFinished(row, r);
                        return filled;
                    });

            case FormEventNames.SectionSignCancelled when kind == SectionKinds.Conclusion:
                return Transition(row, record, ExamRecordState.Completed, ExamRecordState.InProgress,
                    r =>
                    {
                        r.ExamFinishedAt = null;
                        r.ExamFinishedAtEstimated = false;
                        r.HealthClassCode = "";
                    });

            case FormEventNames.AttachmentAdded when IsScanResult(data):
                return await ApplyScanResultAsync(row, data, record, attached: true, nowUtc, ct);

            case FormEventNames.AttachmentRemoved when IsScanResult(data):
                return await ApplyScanResultAsync(row, data, record, attached: false, nowUtc, ct);

            default:
                return WebhookProcessResult.Skipped(
                    $"Tổ hợp Event={row.EventType} / SectionKind={data.SectionKind} / AttachKind={data.AttachKind} "
                    + "không thuộc bảng 02-api-spec §5.2");
        }
    }

    private async Task<WebhookProcessResult> ApplyScanResultAsync(
        WebhookInbox row, FormWebhookData data, ExamRecord record, bool attached, DateTime nowUtc, CancellationToken ct)
    {
        var orders = await _paraclinicalRepository.ListByRecordAsync(
            row.DivisionID, record.RecordID, includeCancelled: true, forUpdate: true, ct);
        var items = orders.SelectMany(x => x.Items).ToList();

        var match = ResolveScanTargets(data, record, items, attached);

        if (match.Ambiguous)
            return WebhookProcessResult.Skipped(match.Detail);

        if (match.Items.Count == 0)
            return await NoScanTargetAsync(row, data, record, attached, ct);

        var how = match.How;
        var target = attached ? ParaclinicalItemState.Done : ParaclinicalItemState.Waiting;
        var applied = 0;
        var noop = 0;

        foreach (var item in match.Items)
        {
            var from = item.State;
            var result = item.TransitionTo(
                target, ParaclinicalStateSource.Internal, nowUtc,
                attachmentId: attached ? data.AttachmentID : null,
                resultSourceKind: attached ? ParaclinicalResultSources.Scan : null,
                isAbnormal: attached ? data.IsAbnormal : null);

            if (result.Outcome == ParaclinicalTransitionOutcome.NoOp) { noop++; continue; }
            if (!result.Applied) continue;

            applied++;
            item.ModifiedActorKind = ActorKind.Integration;

            _auditRepository.Add(new AuditEntry(
                row.DivisionID,
                AuditEntityTypes.OrderItem,
                item.OrderItemID,
                AuditActions.Webhook,
                "0",
                (short)from,
                (short)item.State,
                new
                {
                    row.EventID,
                    row.EventType,
                    data.AttachmentID,
                    item.ServiceCode,
                    Matched = how
                },
                ActorKind.Integration,
                row.TraceID));
        }

        if (applied == 0)
            return WebhookProcessResult.Skipped(
                $"{noop} dòng dịch vụ đã ở '{ParaclinicalItemStateNames.Of(target)}' — không có gì để đổi");

        return WebhookProcessResult.Applied(
            $"{applied} dòng dịch vụ → '{ParaclinicalItemStateNames.Of(target)}' (khớp theo {how})");
    }

    private async Task<WebhookProcessResult> NoScanTargetAsync(
        WebhookInbox row, FormWebhookData data, ExamRecord record, bool attached, CancellationToken ct)
    {
        if (!attached && data.AttachmentID.HasValue)
        {
            var consumed = await _inboxRepository.HasAttachmentAddedBeenProcessedAsync(
                row.DivisionID, record.RecordID, data.AttachmentID.Value, row.InboxID, ct);

            if (consumed)
                return WebhookProcessResult.Skipped(
                    $"Đính kèm {data.AttachmentID} chưa từng gắn được vào dòng dịch vụ nào của hồ sơ "
                    + $"{record.RecordCode} (gói .added đã xử lý và không đổi gì — nhiều khả năng dòng đã "
                    + "được nhập kết quả tay) — không có gì để lùi");

            return new WebhookProcessResult
            {
                Outcome = WebhookOutcome.Failed,
                Detail = $"Chưa dòng dịch vụ nào của hồ sơ {record.RecordCode} giữ đính kèm "
                         + $"{data.AttachmentID} và cũng chưa thấy gói .added của nó — có thể gói .added chưa "
                         + "được áp, sẽ thử lại"
            };
        }

        if (!attached)
        {
            return WebhookProcessResult.Skipped(
                "Gói gỡ đính kèm không mang AttachmentID — không xác định được dòng dịch vụ nào phải lùi "
                + "(đường .removed chỉ khớp bằng AttachmentID)");
        }

        return WebhookProcessResult.Skipped(
            "Gói đính kèm không chỉ ra dòng dịch vụ nào (cần OrderItemID | ServiceID | ServiceCode "
            + "trong Data hoặc Metadata)");
    }

    private readonly struct ScanMatch
    {
        public List<ParaclinicalOrderItem> Items { get; private init; }
        public string How { get; private init; }
        public bool Ambiguous { get; private init; }
        public string Detail { get; private init; }

        public static ScanMatch None() => new()
            { Items = new List<ParaclinicalOrderItem>(), How = "", Detail = "" };

        public static ScanMatch By(string how, List<ParaclinicalOrderItem> items) => new()
            { Items = items, How = how, Detail = "" };

        public static ScanMatch Ambiguity(string detail) => new()
            { Items = new List<ParaclinicalOrderItem>(), How = "", Ambiguous = true, Detail = detail };
    }

    private static ScanMatch ResolveScanTargets(
        FormWebhookData data, ExamRecord record, List<ParaclinicalOrderItem> items, bool attached)
    {
        var orderItemId = data.OrderItemID ?? ReadGuid(data, "OrderItemID");

        if (!attached)
        {
            if (!data.AttachmentID.HasValue) return ScanMatch.None();

            var holding = items.Where(x => x.AttachmentID == data.AttachmentID.Value).ToList();
            if (holding.Count == 0) return ScanMatch.None();

            if (orderItemId.HasValue)
            {
                var narrowed = holding.Where(x => x.OrderItemID == orderItemId.Value).ToList();
                if (narrowed.Count > 0) return ScanMatch.By("AttachmentID+OrderItemID", narrowed);
            }

            return ScanMatch.By("AttachmentID", holding);
        }

        if (orderItemId.HasValue)
        {
            var byItem = items.Where(x => x.OrderItemID == orderItemId.Value).ToList();
            if (byItem.Count > 0) return ScanMatch.By("OrderItemID", byItem);
        }

        var serviceId = data.ServiceID ?? ReadLong(data, "ServiceID");
        if (serviceId.HasValue)
        {
            var byService = items.Where(x => x.ServiceID == serviceId.Value
                                          && x.State != ParaclinicalItemState.Cancelled).ToList();
            if (byService.Count > 1) return Ambiguous("ServiceID", serviceId.Value, byService, record);
            if (byService.Count == 1) return ScanMatch.By("ServiceID", byService);
        }

        var serviceCode = (data.ServiceCode ?? ReadString(data, "ServiceCode") ?? "").Trim();
        if (serviceCode.Length > 0)
        {
            var byCode = items.Where(x => string.Equals(x.ServiceCode, serviceCode, StringComparison.OrdinalIgnoreCase)
                                       && x.State != ParaclinicalItemState.Cancelled).ToList();
            if (byCode.Count > 1) return Ambiguous("ServiceCode", serviceCode, byCode, record);
            if (byCode.Count == 1) return ScanMatch.By("ServiceCode", byCode);
        }

        return ScanMatch.None();
    }

    private static ScanMatch Ambiguous(
        string key, object value, List<ParaclinicalOrderItem> matched, ExamRecord record)
        => ScanMatch.Ambiguity(
            $"{key}={value} khớp {matched.Count} dòng dịch vụ còn sống của hồ sơ {record.RecordCode} "
            + $"(phiếu {string.Join(", ", matched.Select(x => x.OrderID))}) — gói đính kèm phải nêu "
            + "OrderItemID mới chỉ ra được dòng nào");

    private WebhookProcessResult Transition(
        WebhookInbox row, ExamRecord record,
        ExamRecordState from, ExamRecordState to, Action<ExamRecord> stamp,
        Func<ExamRecord, bool> fillWhenAlreadyThere = null)
    {
        if (record.State == to)
        {
            if (fillWhenAlreadyThere != null && fillWhenAlreadyThere(record))
            {
                _auditRepository.Add(new AuditEntry(
                    row.DivisionID,
                    AuditEntityTypes.Record,
                    record.RecordID,
                    AuditActions.Webhook,
                    "0",
                    (short)to,
                    (short)to,
                    new
                    {
                        row.EventID,
                        row.EventType,
                        row.OccurredAt,
                        Note = "Bổ sung dữ liệu cho hồ sơ đã ở đích"
                    },
                    ActorKind.Integration,
                    row.TraceID));

                return WebhookProcessResult.Applied(
                    $"Hồ sơ đã ở \"{ExamRecordStateNames.Of(to)}\" — bổ sung dữ liệu còn thiếu của sự kiện");
            }

            return WebhookProcessResult.Skipped($"Hồ sơ đã ở trạng thái \"{ExamRecordStateNames.Of(to)}\"");
        }

        if (record.State != from)
        {
            return new WebhookProcessResult
            {
                Outcome = WebhookOutcome.Skipped,
                InvalidTransition = true,
                Detail = $"Chuyển tiếp không hợp lệ: hồ sơ đang \"{ExamRecordStateNames.Of(record.State)}\", "
                         + $"sự kiện đòi \"{ExamRecordStateNames.Of(from)}\" → \"{ExamRecordStateNames.Of(to)}\""
            };
        }

        var fromState = record.State;
        record.State = to;
        stamp(record);

        _auditRepository.Add(new AuditEntry(
            row.DivisionID,
            AuditEntityTypes.Record,
            record.RecordID,
            AuditActions.Webhook,
            "0",
            (short)fromState,
            (short)to,
            new
            {
                row.EventID,
                row.EventType,
                row.OccurredAt,
                row.SubmissionID
            },
            ActorKind.Integration,
            row.TraceID));

        return WebhookProcessResult.Applied($"{ExamRecordStateNames.Of(fromState)} → {ExamRecordStateNames.Of(to)}");
    }

    private static WebhookProcessResult UpdateProgress(
        WebhookInbox row, FormWebhookData data, ExamRecord record, DateTime nowUtc)
    {
        if (data.Progress == null)
            return WebhookProcessResult.Skipped("Sự kiện nhóm khám lâm sàng không kèm Data.Progress — không có gì để cache");

        if (ExamRecordStates.IsCancelled(record.State))
            return WebhookProcessResult.Skipped("Hồ sơ đã huỷ — không cập nhật cache tiến độ");

        var total = data.Progress.Total;
        var done = data.Progress.Completed;

        if (total < 0 || done < 0)
            return WebhookProcessResult.Skipped(
                $"Tiến độ trong gói không hợp lệ ({done}/{total}) — không ghi đè cache");

        if (total > 0 && done > total)
            done = total;

        record.ProgressDone = done;
        record.ProgressTotal = total;
        record.ProgressSyncedAt = nowUtc;

        return WebhookProcessResult.Applied($"Tiến độ {done}/{total}");
    }

    private static bool StampExamStarted(WebhookInbox row, ExamRecord record)
        => Stamp(row.OccurredAt, record.ExamStartedAt, record.ExamStartedAtEstimated,
                 (v, est) => { record.ExamStartedAt = v; record.ExamStartedAtEstimated = est; });

    private static bool StampExamFinished(WebhookInbox row, ExamRecord record)
        => Stamp(row.OccurredAt, record.ExamFinishedAt, record.ExamFinishedAtEstimated,
                 (v, est) => { record.ExamFinishedAt = v; record.ExamFinishedAtEstimated = est; });

    private static bool Stamp(
        DateTime occurredAt, DateTime? current, bool estimated, Action<DateTime, bool> set)
    {
        if (current.HasValue && !estimated) return false;
        set(occurredAt, false);
        return true;
    }

    private static void StampHealthClass(WebhookInbox row, FormWebhookData data, ExamRecord record)
    {
        if (HealthClassOf(data) is { } hc) record.HealthClassCode = hc;
    }

    private static string HealthClassOf(FormWebhookData data)
    {
        var value = (data.HealthClassCode ?? "").Trim();
        if (value.Length == 0 || value.Length > RecordFieldLengths.HealthClassCode)
            return null;
        return value;
    }

    private static bool IsAttachmentEvent(string eventType)
        => eventType is FormEventNames.AttachmentAdded or FormEventNames.AttachmentRemoved;

    private static bool IsScanResult(FormWebhookData data)
        => string.Equals((data.AttachKind ?? "").Trim(), AttachKinds.ScanResult, StringComparison.OrdinalIgnoreCase);

    private static Guid? ReadGuid(FormWebhookData data, string key)
        => Guid.TryParse(ReadString(data, key), out var val) ? val : null;

    private static long? ReadLong(FormWebhookData data, string key)
        => long.TryParse(ReadString(data, key), out var val) ? val : null;

    private static string ReadString(FormWebhookData data, string key)
        => data?.Find(key)?.ToString();

    private void CountMetrics(WebhookProcessResult result, WebhookInbox row)
    {
        if (result.RecordNotFound) _tracker.CountRecordNotFound();
        if (result.StaleSkipped) _tracker.CountStaleSkipped();
        if (result.InvalidTransition) _tracker.CountInvalidTransition();
        if (result.Outcome == WebhookOutcome.Applied) _tracker.CountApplied();
        if (result.Outcome == WebhookOutcome.Failed) _tracker.CountFailed();
        if (row.ProcessState == WebhookProcessState.Failed && row.RetryCount >= WebhookInbox.MaxRetry)
            _tracker.CountDeadLettered();
    }

    private static FormWebhookEvent ParseEvent(string payload)
    {
        using var doc = JsonDocument.Parse(payload ?? "{}");
        var root = doc.RootElement;
        var evt = new FormWebhookEvent();

        if (TryGetProperty(root, "EventID", out var pEventId))
            evt.EventID = GetStringSafe(pEventId) ?? "";
        if (TryGetProperty(root, "Event", out var pEvent))
            evt.Event = GetStringSafe(pEvent) ?? "";
        if (TryGetProperty(root, "OccurredAt", out var pOccurredAt) && TryGetDateTimeOffsetSafe(pOccurredAt, out var dto))
            evt.OccurredAt = dto;
        if (TryGetProperty(root, "SubmissionID", out var pSubId) && TryGetGuidSafe(pSubId, out var gid))
            evt.SubmissionID = gid;
        if (TryGetProperty(root, "HostRefType", out var pHrt))
            evt.HostRefType = GetStringSafe(pHrt) ?? "";
        if (TryGetProperty(root, "DivisionID", out var pDiv))
            evt.DivisionID = GetStringSafe(pDiv) ?? "";
        if (TryGetProperty(root, "HostRefID", out var pHri))
            evt.HostRefID = GetStringSafe(pHri) ?? "";
        if (TryGetProperty(root, "SubjectID", out var pSubj))
            evt.SubjectID = GetStringSafe(pSubj) ?? "";

        if (TryGetProperty(root, "Data", out var pData) && pData.ValueKind == JsonValueKind.Object)
        {
            evt.Data = ParseData(pData);
        }

        return evt;
    }

    private static FormWebhookData ParseData(JsonElement el)
    {
        var data = new FormWebhookData();

        if (TryGetProperty(el, "SectionID", out var pSecId) && TryGetInt64Safe(pSecId, out var secId))
            data.SectionID = secId;
        if (TryGetProperty(el, "SectionKind", out var pSecKind))
            data.SectionKind = GetStringSafe(pSecKind) ?? "";
        if (TryGetProperty(el, "AttachKind", out var pAttKind))
            data.AttachKind = GetStringSafe(pAttKind) ?? "";
        if (TryGetProperty(el, "State", out var pState) && TryGetInt16Safe(pState, out var st))
            data.State = st;
        if (TryGetProperty(el, "ToState", out var pToState) && TryGetInt16Safe(pToState, out var toSt))
            data.ToState = toSt;
        if (TryGetProperty(el, "ActorKind", out var pActKind) && TryGetInt16Safe(pActKind, out var ak))
            data.ActorKind = ak;
        if (TryGetProperty(el, "ActorID", out var pActId) && TryGetInt64Safe(pActId, out var aid))
            data.ActorID = aid;
        if (TryGetProperty(el, "HealthClassCode", out var pHc))
            data.HealthClassCode = GetStringSafe(pHc) ?? "";
        if (TryGetProperty(el, "AttachmentID", out var pAttId) && TryGetGuidSafe(pAttId, out var attGuid))
            data.AttachmentID = attGuid;

        if (TryGetProperty(el, "Progress", out var pProg) && pProg.ValueKind == JsonValueKind.Object)
        {
            var prog = new FormProgressCount();
            if (TryGetProperty(pProg, "Completed", out var pComp) && TryGetInt16Safe(pComp, out var c))
                prog.Completed = c;
            if (TryGetProperty(pProg, "Total", out var pTot) && TryGetInt16Safe(pTot, out var t))
                prog.Total = t;
            data.Progress = prog;
        }

        foreach (var prop in el.EnumerateObject())
        {
            data.Extra[prop.Name] = ElementToObject(prop.Value);
        }

        if (TryGetProperty(el, "Metadata", out var pMeta) && pMeta.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in pMeta.EnumerateObject())
            {
                data.Metadata[prop.Name] = ElementToObject(prop.Value);
            }
        }

        if (data.Find("OrderItemID") is { } oidStr && Guid.TryParse(oidStr.ToString(), out var oid))
            data.OrderItemID = oid;
        if (data.Find("ServiceID") is { } sidStr && long.TryParse(sidStr.ToString(), out var sid))
            data.ServiceID = sid;
        if (data.Find("ServiceCode") is { } scStr)
            data.ServiceCode = scStr.ToString();
        if (data.Find("isAbnormal") is { } abStr && bool.TryParse(abStr.ToString(), out var ab))
            data.IsAbnormal = ab;

        return data;
    }

    private static bool TryGetProperty(JsonElement element, string propertyName, out JsonElement value)
    {
        if (element.TryGetProperty(propertyName, out value)) return true;

        var camel = char.ToLowerInvariant(propertyName[0]) + propertyName[1..];
        if (element.TryGetProperty(camel, out value)) return true;

        var pascal = char.ToUpperInvariant(propertyName[0]) + propertyName[1..];
        return element.TryGetProperty(pascal, out value);
    }

    private static object ElementToObject(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => element.GetString(),
        JsonValueKind.Number => element.TryGetInt64(out var l) ? l : element.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null => null,
        _ => element.GetRawText()
    };

    private static string GetStringSafe(JsonElement el) => el.ValueKind switch
    {
        JsonValueKind.String => el.GetString(),
        JsonValueKind.Number => el.GetRawText(),
        _ => null
    };

    private static bool TryGetInt16Safe(JsonElement el, out short val)
    {
        if (el.ValueKind == JsonValueKind.Number && el.TryGetInt16(out val)) return true;
        if (el.ValueKind == JsonValueKind.String && short.TryParse(el.GetString(), out val)) return true;
        val = 0;
        return false;
    }

    private static bool TryGetInt64Safe(JsonElement el, out long val)
    {
        if (el.ValueKind == JsonValueKind.Number && el.TryGetInt64(out val)) return true;
        if (el.ValueKind == JsonValueKind.String && long.TryParse(el.GetString(), out val)) return true;
        val = 0;
        return false;
    }

    private static bool TryGetGuidSafe(JsonElement el, out Guid val)
    {
        if (el.ValueKind == JsonValueKind.String && el.TryGetGuid(out val)) return true;
        val = Guid.Empty;
        return false;
    }

    private static bool TryGetDateTimeOffsetSafe(JsonElement el, out DateTimeOffset val)
    {
        if (el.ValueKind == JsonValueKind.String && el.TryGetDateTimeOffset(out val)) return true;
        val = default;
        return false;
    }
}
