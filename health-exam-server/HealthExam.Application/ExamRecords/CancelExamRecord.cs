using System;
using System.Threading;
using System.Threading.Tasks;
using HealthExam.Application.Common;
using HealthExam.Application.ExamSessions;
using HealthExam.Domain.Common;
using HealthExam.Domain.ExamRecords;
using HealthExam.Domain.ExamSessions;

namespace HealthExam.Application.ExamRecords;

public interface ICancelExamRecordHandler
{
    Task<ApplicationResult<ExamRecordResult>> HandleAsync(
        CancelExamRecordCommand command, CancellationToken ct = default);
}

public sealed class CancelExamRecordHandler : ICancelExamRecordHandler
{
    private readonly IExamRecordRepository _recordRepository;
    private readonly IExamSessionRepository _sessionRepository;
    private readonly IUnitOfWork _uow;
    private readonly IAuditRepository _audit;

    public CancelExamRecordHandler(
        IExamRecordRepository recordRepository,
        IExamSessionRepository sessionRepository,
        IUnitOfWork uow,
        IAuditRepository audit)
    {
        _recordRepository = recordRepository;
        _sessionRepository = sessionRepository;
        _uow = uow;
        _audit = audit;
    }

    public async Task<ApplicationResult<ExamRecordResult>> HandleAsync(
        CancelExamRecordCommand command, CancellationToken ct = default)
    {
        var record = await _recordRepository.GetAsync(command.DivisionId, command.RecordId, forUpdate: true, ct);
        if (record == null)
        {
            return ApplicationResult<ExamRecordResult>.Fail(
                ApplicationFailureCode.NotFound, "Không tìm thấy hồ sơ khám");
        }

        if (ExamRecordStates.IsCancelled(record.State))
        {
            var current = await _recordRepository.GetResultAsync(command.DivisionId, command.RecordId, ct);
            return ApplicationResult<ExamRecordResult>.Success(current);
        }

        var reason = command.Reason?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(reason))
        {
            return ApplicationResult<ExamRecordResult>.Fail(
                ApplicationFailureCode.BadRequest,
                "Lý do hủy không được để trống",
                ApplicationValidationErrors.Of("Reason", "Bỏ trống (bắt buộc)"));
        }

        var session = await _sessionRepository.GetAsync(command.DivisionId, record.SessionID, forUpdate: false, ct);
        if (session != null && (session.State == ExamSessionState.Closed || session.State == ExamSessionState.Cancelled))
        {
            return ApplicationResult<ExamRecordResult>.Fail(
                ApplicationFailureCode.SessionClosed,
                $"Đợt khám {session.SessionCode} đã đóng, cần mở lại đợt trước khi ghi hồ sơ");
        }

        var fromState = record.State;
        var domainResult = record.Cancel(reason);
        if (!domainResult.IsSuccess)
        {
            return ApplicationResult<ExamRecordResult>.Fail(
                ApplicationFailureCode.InvalidState,
                $"Hồ sơ đang ở trạng thái \"{ExamRecordStateNames.Of(fromState)}\", không thể hủy");
        }

        var now = DateTime.UtcNow;
        var actorId = long.TryParse(command.ActorId, out var parsedActorId) ? parsedActorId : 0L;

        record.CancelledAt = now;
        record.CancelledBy = actorId;
        record.CancelReason = reason;
        record.ModifiedDate = now;
        record.ModifiedBy = actorId;
        record.ModifiedActorKind = command.ActorKind;

        _audit.Add(new AuditEntry(
            command.DivisionId,
            AuditEntityTypes.Record,
            record.RecordID,
            AuditActions.StateChange,
            command.ActorId,
            (short)fromState,
            (short)record.State,
            new { Reason = reason, record.RecordCode },
            command.ActorKind,
            command.TraceId));

        await _uow.SaveChangesAsync(ct);

        var result = await _recordRepository.GetResultAsync(command.DivisionId, command.RecordId, ct);
        return ApplicationResult<ExamRecordResult>.Success(result);
    }
}
