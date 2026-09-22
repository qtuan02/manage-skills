using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HealthExam.Application.Common;
using HealthExam.Application.ExamRecords;
using HealthExam.Application.Signing;
using HealthExam.Domain.Common;
using HealthExam.Domain.ExamForms;
using HealthExam.Domain.ExamRecords;

namespace HealthExam.Application.Paraclinical;

public interface ICancelConclusionSignHandler
{
    Task<ApplicationResult<ConclusionSignResult>> HandleAsync(
        CancelConclusionSignCommand command, CancellationToken ct = default);
}

public class CancelConclusionSignHandler : ICancelConclusionSignHandler
{
    private readonly IExamRecordRepository _records;
    private readonly ISignStepMapRepository _map;
    private readonly IUnitOfWork _uow;
    private readonly IAuditRepository _audit;

    public CancelConclusionSignHandler(
        IExamRecordRepository records,
        ISignStepMapRepository map,
        IUnitOfWork uow,
        IAuditRepository audit)
    {
        _records = records;
        _map = map;
        _uow = uow;
        _audit = audit;
    }

    public async Task<ApplicationResult<ConclusionSignResult>> HandleAsync(
        CancelConclusionSignCommand command, CancellationToken ct = default)
    {
        await using var tx = await _uow.BeginAsync(ct);
        var locked = await _records.LockRecordAsync(command.RecordId, ct);
        if (locked == null || locked.DivisionID != command.DivisionId)
            return ApplicationResult<ConclusionSignResult>.Fail(
                ApplicationFailureCode.NotFound, "Không tìm thấy hồ sơ khám");

        var record = await _records.GetAsync(command.DivisionId, command.RecordId, forUpdate: true, ct);
        if (record == null)
            return ApplicationResult<ConclusionSignResult>.Fail(
                ApplicationFailureCode.NotFound, "Không tìm thấy hồ sơ khám");

        var mapSteps = await _map.ListAsync(command.DivisionId, record.VariantCode, ct);
        record.SignSteps ??= new List<ExamRecordSignStep>();

        if (record.SignStatus != ExamRecordSignStatus.Signed)
        {
            return ApplicationResult<ConclusionSignResult>.Fail(
                ApplicationFailureCode.InvalidState,
                "Hồ sơ chưa ký kết luận",
                Build(record, mapSteps));
        }

        if (command.ActorKind != ActorKind.Employee
            || !long.TryParse(command.ActorId, out var actorEmployeeId)
            || actorEmployeeId <= 0)
        {
            return ApplicationResult<ConclusionSignResult>.Fail(
                ApplicationFailureCode.Forbidden,
                "Chỉ nhân viên mới được hủy ký kết luận",
                Build(record, mapSteps));
        }

        if (record.HisSignedByEmployeeID.HasValue && record.HisSignedByEmployeeID.Value != actorEmployeeId)
        {
            return ApplicationResult<ConclusionSignResult>.Fail(
                ApplicationFailureCode.Forbidden,
                "Chỉ người đã ký kết luận mới được hủy ký",
                Build(record, mapSteps));
        }

        var now = DateTime.UtcNow;
        var previousFilePath = record.SignedFilePath;

        // Reset thông tin ký kết luận trên record
        record.State = ExamRecordState.InProgress;
        record.SignStatus = ExamRecordSignStatus.New;
        record.SignedFilePath = null;
        record.HisSignedByEmployeeID = null;
        record.HisSignedAt = null;

        // Cập nhật bước ký kết luận
        var conclusionStep = mapSteps.FirstOrDefault(m => m.IsConclusionStep);
        if (conclusionStep != null)
        {
            var conclusionSnapshot = record.SignSteps.FirstOrDefault(
                s => s.VariantCode == record.VariantCode && s.SWStep == conclusionStep.SWStep);
            if (conclusionSnapshot != null)
            {
                conclusionSnapshot.Status = ExamRecordSignStepStatus.InProgress;
                conclusionSnapshot.SignedByEmployeeID = null;
                conclusionSnapshot.SignedByEmployeeCode = "";
                conclusionSnapshot.SignedByEmployeeName = "";
                conclusionSnapshot.SignedAt = null;
                conclusionSnapshot.ModifiedDate = now;
            }
        }

        _audit.Add(new AuditEntry(command.DivisionId, AuditEntityTypes.Record, record.RecordID,
            AuditActions.StateChange, command.ActorId, null, null,
            new { Action = "CONCLUSION_SIGN_CANCELLED", PreviousFilePath = previousFilePath },
            command.ActorKind));

        var save = await _uow.SaveChangesAsync(ct);
        if (save.Outcome == PersistenceSaveOutcome.UniqueConflict)
        {
            _uow.DiscardPendingChanges();
            return ApplicationResult<ConclusionSignResult>.Fail(
                ApplicationFailureCode.InvalidState,
                "Hồ sơ đang có thao tác khác đang chạy",
                Build(record, mapSteps));
        }

        await tx.CommitAsync(ct);

        return ApplicationResult<ConclusionSignResult>.Success(Build(record, mapSteps));
    }

    private static ConclusionSignResult Build(ExamRecord record, IReadOnlyList<SignStepMap> mapSteps)
    {
        var snapshots = (record.SignSteps ?? new List<ExamRecordSignStep>())
            .Where(s => s.VariantCode == record.VariantCode).ToList();

        var steps = mapSteps.Select(m =>
        {
            var snap = snapshots.FirstOrDefault(s => s.SWStep == m.SWStep);
            return new ConclusionSignStepResult(
                m.SWStep, m.StepName, m.ItemGroupID, m.SWRoleID,
                snap?.Status ?? "", snap?.SignedByEmployeeID, snap?.SignedAt,
                snap?.SignedByEmployeeName ?? "",
                snap?.PerformedByEmployeeID,
                snap?.PerformedByEmployeeName ?? "");
        }).ToList();

        return new ConclusionSignResult(
            record.RecordID,
            record.SignStatus,
            record.SignedFilePath,
            record.HisSignedByEmployeeID,
            record.HisSignedAt,
            Array.Empty<ConclusionConditionResult>(),
            steps,
            Array.Empty<int>());
    }
}
