using System;
using System.Threading;
using System.Threading.Tasks;
using HealthExam.Application.Common;
using HealthExam.Application.ExamRecords;
using HealthExam.Application.Integrations;
using HealthExam.Domain.Common;
using HealthExam.Domain.ExamRecords;

namespace HealthExam.Application.Webhooks;

public interface IReconcileProgressHandler
{
    Task<ApplicationResult<ReconcileResult>> HandleAsync(
        ReconcileProgressCommand command, CancellationToken ct = default);
}

public class ReconcileProgressHandler : IReconcileProgressHandler
{
    public const int DefaultBatchSize = 100;

    private readonly IExamRecordRepository _recordRepository;
    private readonly IFormServerClient _formServerClient;
    private readonly IUnitOfWork _uow;
    private readonly IAuditRepository _auditRepository;
    private readonly IWebhookMetricsTracker _tracker;
    private readonly IClock _clock;

    public ReconcileProgressHandler(
        IExamRecordRepository recordRepository,
        IFormServerClient formServerClient,
        IUnitOfWork uow,
        IAuditRepository auditRepository,
        IWebhookMetricsTracker tracker,
        IClock clock)
    {
        _recordRepository = recordRepository;
        _formServerClient = formServerClient;
        _uow = uow;
        _auditRepository = auditRepository;
        _tracker = tracker;
        _clock = clock;
    }

    public async Task<ApplicationResult<ReconcileResult>> HandleAsync(
        ReconcileProgressCommand command, CancellationToken ct = default)
    {
        var result = new ReconcileResult();
        var batchSize = command.BatchSize <= 0 ? DefaultBatchSize : command.BatchSize;

        var candidates = await _recordRepository.FindReconcileCandidatesAsync(
            command.DivisionId, command.SessionId, batchSize, ct);

        foreach (var candidate in candidates)
        {
            ct.ThrowIfCancellationRequested();
            result.Examined++;

            try
            {
                await ReconcileOneAsync(candidate, result, ct);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception)
            {
                result.Failed++;
                _uow.DiscardPendingChanges();
            }
        }

        _tracker.CountReconcileHealed(result.Healed);

        return ApplicationResult<ReconcileResult>.Success(result);
    }

    private void Reconcile(ExamRecord record, FormSubmissionProgressDto progress, ReconcileResult delta, DateTime now)
    {
        var healed = false;
        var target = TargetStateOf(record, progress);

        if (target.HasValue && target.Value > record.State)
        {
            var from = record.State;
            record.State = target.Value;

            if (target.Value >= ExamRecordState.InProgress && !record.ExamStartedAt.HasValue)
            {
                record.ExamStartedAt = now;
                record.ExamStartedAtEstimated = true;
            }
            if (target.Value == ExamRecordState.Completed && !record.ExamFinishedAt.HasValue)
            {
                record.ExamFinishedAt = now;
                record.ExamFinishedAtEstimated = true;
            }

            StampHealthClass(record, progress);

            record.ModifiedDate = now;
            record.ModifiedActorKind = ActorKind.Integration;

            _auditRepository.Add(new AuditEntry(
                record.DivisionID,
                AuditEntityTypes.Record,
                record.RecordID,
                AuditActions.Reconcile,
                "0",
                (short)from,
                (short)target.Value,
                new
                {
                    record.SubmissionID,
                    progress.SubmissionState,
                    Completed = progress.Sections?.Completed,
                    Total = progress.Sections?.Total,
                    Note = "Chữa bằng job đối soát kéo — sự kiện webhook tương ứng đã mất"
                },
                ActorKind.Integration));

            delta.Healed++;
            healed = true;
        }
        else
        {
            StampHealthClass(record, progress);
        }

        if (progress.Sections != null &&
            (record.ProgressDone != progress.Sections.Completed || record.ProgressTotal != progress.Sections.Total))
        {
            record.ProgressDone = progress.Sections.Completed;
            record.ProgressTotal = progress.Sections.Total;
            record.ProgressSyncedAt = now;
            if (!healed) delta.ProgressRefreshed++;
        }
        else if (progress.Sections != null)
        {
            record.ProgressSyncedAt = now;
        }

        if (!progress.SubmissionState.HasValue && progress.Sections is { Total: > 0 } sections
            && sections.Completed >= sections.Total
            && record.State == ExamRecordState.InProgress)
        {
            delta.Undecidable++;
        }
    }

    private static void StampHealthClass(ExamRecord record, FormSubmissionProgressDto progress)
    {
        var value = (progress.HealthClassCode ?? "").Trim();
        if (value.Length == 0 || !string.IsNullOrWhiteSpace(record.HealthClassCode)) return;
        if (value.Length > RecordFieldLengths.HealthClassCode) return;
        record.HealthClassCode = value;
    }

    private static ExamRecordState? TargetStateOf(ExamRecord record, FormSubmissionProgressDto progress)
    {
        if (progress.SubmissionState is { } state)
            return state switch
            {
                FormSubmissionStates.InProgress => ExamRecordState.InProgress,
                FormSubmissionStates.Completed => ExamRecordState.Completed,
                _ => null
            };

        if (record.State == ExamRecordState.Waiting && progress.Sections is { Completed: > 0 })
            return ExamRecordState.InProgress;

        return null;
    }

    private async Task ReconcileOneAsync(
        ReconcileCandidate candidate, ReconcileResult result, CancellationToken ct)
    {
        FormSubmissionProgressDto progress;
        try
        {
            progress = await _formServerClient.GetProgressAsync(
                candidate.SubmissionID,
                new ServiceCallOrigin { DivisionId = candidate.DivisionID },
                ct);
        }
        catch (Exception ex) when (IsDependencyUnavailable(ex))
        {
            result.Unreachable++;
            return;
        }
        catch (Exception ex) when (IsNotFound(ex))
        {
            return;
        }

        await using var tx = await _uow.BeginAsync(ct);
        var record = await _recordRepository.LockRecordAsync(candidate.RecordID, ct);
        if (record == null)
        {
            await tx.CommitAsync(ct);
            return;
        }

        var delta = new ReconcileResult();
        Reconcile(record, progress, delta, _clock.UtcNow);

        await _uow.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        result.Healed += delta.Healed;
        result.ProgressRefreshed += delta.ProgressRefreshed;
        result.Undecidable += delta.Undecidable;
    }

    private static int? GetErrorCode(Exception ex)
    {
        var prop = ex?.GetType().GetProperty("ErrorCode");
        if (prop != null && prop.GetValue(ex) is int code)
            return code;
        return null;
    }

    private static bool IsDependencyUnavailable(Exception ex)
        => GetErrorCode(ex) == 5020 || ex.Message.Contains("5020") || ex.GetType().Name.Contains("DependencyUnavailable");

    private static bool IsNotFound(Exception ex)
        => GetErrorCode(ex) == 4040 || ex.Message.Contains("4040") || ex.GetType().Name.Contains("NotFound");
}
