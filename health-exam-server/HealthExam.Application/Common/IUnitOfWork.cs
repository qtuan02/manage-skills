using System;
using System.Threading;
using System.Threading.Tasks;

namespace HealthExam.Application.Common;

public interface IApplicationTransaction : IAsyncDisposable
{
    Task CommitAsync(CancellationToken ct = default);
    Task RollbackAsync(CancellationToken ct = default);
    bool SupportsSavepoints => true;
    Task CreateSavepointAsync(string name, CancellationToken ct = default) => Task.CompletedTask;
    Task RollbackToSavepointAsync(string name, CancellationToken ct = default) => Task.CompletedTask;
}

public enum PersistenceSaveOutcome { Saved, UniqueConflict }

public readonly record struct PersistenceSaveResult(
    PersistenceSaveOutcome Outcome, string ConstraintName = null);

public interface IUnitOfWork
{
    Task<IApplicationTransaction> BeginAsync(CancellationToken ct = default);
    Task<PersistenceSaveResult> SaveChangesAsync(CancellationToken ct = default);
    void DiscardPendingChanges();
}
