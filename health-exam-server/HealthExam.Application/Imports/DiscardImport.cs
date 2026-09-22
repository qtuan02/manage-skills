using System.Threading;
using System.Threading.Tasks;
using HealthExam.Application.Common;
using HealthExam.Domain.Common;
using HealthExam.Domain.Imports;

namespace HealthExam.Application.Imports;

public interface IDiscardImportHandler
{
    Task<ApplicationResult<bool>> HandleAsync(
        DiscardImportCommand command, CancellationToken ct = default);
}

public sealed class DiscardImportHandler : IDiscardImportHandler
{
    private readonly IImportRepository _importRepository;
    private readonly IUnitOfWork _uow;
    private readonly IAuditRepository _audit;
    private readonly IClock _clock;

    public DiscardImportHandler(
        IImportRepository importRepository,
        IUnitOfWork uow,
        IAuditRepository audit,
        IClock clock)
    {
        _importRepository = importRepository;
        _uow = uow;
        _audit = audit;
        _clock = clock;
    }

    public async Task<ApplicationResult<bool>> HandleAsync(
        DiscardImportCommand command, CancellationToken ct = default)
    {
        var batch = await _importRepository.GetAsync(command.DivisionId, command.ImportId, forUpdate: true, ct);
        if (batch == null)
        {
            return ApplicationResult<bool>.Fail(
                ApplicationFailureCode.NotFound, "Không tìm thấy lô nạp");
        }

        var fromState = (short)batch.State;
        var domainResult = batch.Discard(_clock.UtcNow);
        if (!domainResult.IsSuccess)
        {
            return ApplicationResult<bool>.Fail(
                ApplicationFailureCode.InvalidState, domainResult.Failure.Message);
        }

        if (domainResult.Applied)
        {
            var actorId = long.TryParse(command.ActorId, out var parsedActorId) ? parsedActorId : 0L;
            batch.ModifiedBy = actorId;
            batch.ModifiedActorKind = command.ActorKind;

            _audit.Add(new AuditEntry(
                command.DivisionId,
                AuditEntityTypes.Import,
                batch.BatchID,
                AuditActions.StateChange,
                command.ActorId,
                fromState,
                (short)batch.State,
                null,
                command.ActorKind,
                command.TraceId));

            await _uow.SaveChangesAsync(ct);
        }

        return ApplicationResult<bool>.Success(true);
    }
}
