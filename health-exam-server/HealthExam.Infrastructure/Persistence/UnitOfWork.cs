using System;
using System.Threading;
using System.Threading.Tasks;
using HealthExam.Application.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace HealthExam.Infrastructure.Persistence;

public class UnitOfWork : IUnitOfWork
{
    private readonly HealthExamDbContext _db;

    public UnitOfWork(HealthExamDbContext db)
    {
        _db = db;
    }

    public HealthExamDbContext Context => _db;
    public HealthExamDbContext DbContext => _db;

    public async Task<IApplicationTransaction> BeginAsync(CancellationToken ct = default)
    {
        var tx = await _db.Database.BeginTransactionAsync(ct);
        return new EfApplicationTransaction(tx);
    }

    public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken ct = default)
        => _db.Database.BeginTransactionAsync(ct);

    public async Task<PersistenceSaveResult> SaveChangesAsync(CancellationToken ct = default)
    {
        try
        {
            await _db.SaveChangesAsync(ct);
            return new PersistenceSaveResult(PersistenceSaveOutcome.Saved);
        }
        catch (DbUpdateException ex) when (ex.GetBaseException() is PostgresException pg && (pg.SqlState == PostgresErrorCodes.UniqueViolation || pg.SqlState == "23505"))
        {
            var pgEx = (PostgresException)ex.GetBaseException();
            return new PersistenceSaveResult(PersistenceSaveOutcome.UniqueConflict, pgEx.ConstraintName);
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation || ex.SqlState == "23505")
        {
            return new PersistenceSaveResult(PersistenceSaveOutcome.UniqueConflict, ex.ConstraintName);
        }
    }

    public void DiscardPendingChanges()
    {
        _db.ChangeTracker.Clear();
    }
}

public sealed class EfApplicationTransaction : IApplicationTransaction
{
    private readonly IDbContextTransaction _transaction;

    public EfApplicationTransaction(IDbContextTransaction transaction)
    {
        _transaction = transaction;
    }

    public bool SupportsSavepoints => _transaction.SupportsSavepoints;

    public Task CommitAsync(CancellationToken ct = default) => _transaction.CommitAsync(ct);

    public Task RollbackAsync(CancellationToken ct = default) => _transaction.RollbackAsync(ct);

    public Task CreateSavepointAsync(string name, CancellationToken ct = default) =>
        _transaction.CreateSavepointAsync(name, ct);

    public Task RollbackToSavepointAsync(string name, CancellationToken ct = default) =>
        _transaction.RollbackToSavepointAsync(name, ct);

    public ValueTask DisposeAsync() => _transaction.DisposeAsync();
}
