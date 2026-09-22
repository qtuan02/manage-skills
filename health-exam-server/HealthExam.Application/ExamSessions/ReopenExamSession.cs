using System;
using System.Threading;
using System.Threading.Tasks;
using HealthExam.Application.Common;
using HealthExam.Domain.Common;
using HealthExam.Domain.ExamSessions;

namespace HealthExam.Application.ExamSessions;

public interface IReopenExamSessionHandler
{
    Task<ApplicationResult<ExamSessionResult>> HandleAsync(
        ReopenExamSessionCommand command, CancellationToken ct = default);
}

public sealed class ReopenExamSessionHandler : IReopenExamSessionHandler
{
    private readonly IExamSessionRepository _repository;
    private readonly IUnitOfWork _uow;
    private readonly IAuditRepository _audit;

    public ReopenExamSessionHandler(
        IExamSessionRepository repository,
        IUnitOfWork uow,
        IAuditRepository audit)
    {
        _repository = repository;
        _uow = uow;
        _audit = audit;
    }

    public async Task<ApplicationResult<ExamSessionResult>> HandleAsync(
        ReopenExamSessionCommand command, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(command.Reason))
        {
            return ApplicationResult<ExamSessionResult>.Fail(
                ApplicationFailureCode.BadRequest,
                "Mở lại đợt khám phải có lý do",
                ApplicationValidationErrors.Of("Reason", "Bỏ trống (bắt buộc)"));
        }

        var session = await _repository.GetAsync(command.DivisionId, command.SessionId, forUpdate: true, ct);
        if (session == null)
        {
            return ApplicationResult<ExamSessionResult>.Fail(
                ApplicationFailureCode.NotFound, "Không tìm thấy đợt khám");
        }

        var fromState = (short)session.State;
        var domainResult = session.Reopen(command.Reason);
        if (!domainResult.IsSuccess)
        {
            return ApplicationResult<ExamSessionResult>.Fail(
                ApplicationFailureCode.InvalidState, domainResult.Failure.Message);
        }

        var actorId = long.TryParse(command.ActorId, out var parsedActorId) ? parsedActorId : 0;
        session.ModifiedDate = DateTime.UtcNow;
        session.ModifiedBy = actorId;
        session.ModifiedActorKind = command.ActorKind;

        _audit.Add(new AuditEntry(
            command.DivisionId,
            AuditEntityTypes.Session,
            session.SessionID,
            AuditActions.StateChange,
            command.ActorId,
            fromState,
            (short)session.State,
            new { Reason = command.Reason.Trim(), session.SessionCode },
            command.ActorKind,
            command.TraceId));

        await _uow.SaveChangesAsync(ct);

        var recordCount = await _repository.GetRecordCountAsync(command.DivisionId, command.SessionId, ct);
        var result = GetExamSessionHandler.MapToResult(session, recordCount);
        return ApplicationResult<ExamSessionResult>.Success(result);
    }
}
