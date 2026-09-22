using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HealthExam.Domain.Common;
using HealthExam.Domain.Imports;
using HealthExam.Infrastructure.Persistence;
using HealthExam.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HealthExam.Tests.Infrastructure;

[Collection("PostgresTestDb")]
public class ImportPersistenceTests
{
    private static HealthExamDbContext CreateContext(string connectionString)
    {
        return new HealthExamDbContext(new DbContextOptionsBuilder<HealthExamDbContext>()
            .UseNpgsql(connectionString)
            .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking)
            .Options);
    }

    [Fact]
    public async Task Competing_commit_claims_allow_one_owner()
    {
        if (!PostgresTestDb.Enabled) return;

        using var db = new PostgresTestDb();
        var session = db.SeedSession();
        var batchId = Guid.NewGuid();
        var divisionId = db.Ctx.DivisionId;

        var batch = new ImportBatch
        {
            BatchID = batchId,
            DivisionID = divisionId,
            SessionID = session.SessionID,
            FileName = "test.xlsx",
            SheetName = "DanhSach",
            TotalRow = 1,
            SuccessRow = 1,
            ErrorRow = 0,
            State = ImportBatchState.Pending,
            StartedAt = DateTime.UtcNow,
            ModifiedDate = DateTime.UtcNow
        };
        db.Db.ImportBatches.Add(batch);
        await db.Db.SaveChangesAsync();
        db.Db.ChangeTracker.Clear();

        var staleThreshold = DateTime.UtcNow - ImportBatch.StaleCommitAfter;

        async Task<bool> ClaimAsync()
        {
            await using var ctx = CreateContext(db.ConnectionString);
            var repo = new ImportRepository(ctx);
            return await repo.TryClaimForCommitAsync(divisionId, batchId, staleThreshold);
        }

        var claims = await Task.WhenAll(ClaimAsync(), ClaimAsync());
        Assert.Equal(1, claims.Count(x => x));

        await using var verifyCtx = CreateContext(db.ConnectionString);
        var loaded = await verifyCtx.ImportBatches.FirstOrDefaultAsync(b => b.BatchID == batchId);
        Assert.NotNull(loaded);
        Assert.Equal(ImportBatchState.Committing, loaded.State);
    }

    [Fact]
    public async Task Stale_claim_can_be_reclaimed_after_stale_threshold()
    {
        if (!PostgresTestDb.Enabled) return;

        using var db = new PostgresTestDb();
        var session = db.SeedSession();
        var batchId = Guid.NewGuid();
        var divisionId = db.Ctx.DivisionId;

        var batch = new ImportBatch
        {
            BatchID = batchId,
            DivisionID = divisionId,
            SessionID = session.SessionID,
            FileName = "stale.xlsx",
            SheetName = "DanhSach",
            TotalRow = 1,
            SuccessRow = 1,
            ErrorRow = 0,
            State = ImportBatchState.Committing,
            StartedAt = DateTime.UtcNow - TimeSpan.FromMinutes(30),
            ModifiedDate = DateTime.UtcNow - TimeSpan.FromMinutes(10)
        };
        db.Db.ImportBatches.Add(batch);
        await db.Db.SaveChangesAsync();
        db.Db.ChangeTracker.Clear();

        var staleThreshold = DateTime.UtcNow - TimeSpan.FromMinutes(5);

        await using var ctx = CreateContext(db.ConnectionString);
        var repo = new ImportRepository(ctx);
        var claimed = await repo.TryClaimForCommitAsync(divisionId, batchId, staleThreshold);

        Assert.True(claimed);

        await using var verifyCtx = CreateContext(db.ConnectionString);
        var loaded = await verifyCtx.ImportBatches.FirstOrDefaultAsync(b => b.BatchID == batchId);
        Assert.NotNull(loaded);
        Assert.Equal(ImportBatchState.Committing, loaded.State);
        Assert.True(loaded.ModifiedDate > DateTime.UtcNow - TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task Active_claim_cannot_be_reclaimed_before_stale_threshold()
    {
        if (!PostgresTestDb.Enabled) return;

        using var db = new PostgresTestDb();
        var session = db.SeedSession();
        var batchId = Guid.NewGuid();
        var divisionId = db.Ctx.DivisionId;

        var batch = new ImportBatch
        {
            BatchID = batchId,
            DivisionID = divisionId,
            SessionID = session.SessionID,
            FileName = "active.xlsx",
            SheetName = "DanhSach",
            TotalRow = 1,
            SuccessRow = 1,
            ErrorRow = 0,
            State = ImportBatchState.Committing,
            StartedAt = DateTime.UtcNow - TimeSpan.FromMinutes(2),
            ModifiedDate = DateTime.UtcNow - TimeSpan.FromMinutes(1)
        };
        db.Db.ImportBatches.Add(batch);
        await db.Db.SaveChangesAsync();
        db.Db.ChangeTracker.Clear();

        var staleThreshold = DateTime.UtcNow - TimeSpan.FromMinutes(5);

        await using var ctx = CreateContext(db.ConnectionString);
        var repo = new ImportRepository(ctx);
        var claimed = await repo.TryClaimForCommitAsync(divisionId, batchId, staleThreshold);

        Assert.False(claimed);
    }

    [Fact]
    public async Task Release_claim_returns_state_to_Pending()
    {
        if (!PostgresTestDb.Enabled) return;

        using var db = new PostgresTestDb();
        var session = db.SeedSession();
        var batchId = Guid.NewGuid();
        var divisionId = db.Ctx.DivisionId;

        var batch = new ImportBatch
        {
            BatchID = batchId,
            DivisionID = divisionId,
            SessionID = session.SessionID,
            FileName = "release.xlsx",
            SheetName = "DanhSach",
            TotalRow = 1,
            SuccessRow = 1,
            ErrorRow = 0,
            State = ImportBatchState.Committing,
            StartedAt = DateTime.UtcNow,
            ModifiedDate = DateTime.UtcNow
        };
        db.Db.ImportBatches.Add(batch);
        await db.Db.SaveChangesAsync();
        db.Db.ChangeTracker.Clear();

        await using var ctx = CreateContext(db.ConnectionString);
        var repo = new ImportRepository(ctx);
        await repo.ReleaseClaimAsync(divisionId, batchId);

        await using var verifyCtx = CreateContext(db.ConnectionString);
        var loaded = await verifyCtx.ImportBatches.FirstOrDefaultAsync(b => b.BatchID == batchId);
        Assert.NotNull(loaded);
        Assert.Equal(ImportBatchState.Pending, loaded.State);
    }

    [Fact]
    public async Task Row_failure_rolls_back_to_savepoint_without_losing_prior_valid_rows()
    {
        if (!PostgresTestDb.Enabled) return;

        using var db = new PostgresTestDb();
        var session = db.SeedSession();
        var batchId = Guid.NewGuid();
        var divisionId = db.Ctx.DivisionId;

        var batch = new ImportBatch
        {
            BatchID = batchId,
            DivisionID = divisionId,
            SessionID = session.SessionID,
            FileName = "rows.xlsx",
            SheetName = "DanhSach",
            TotalRow = 2,
            SuccessRow = 2,
            ErrorRow = 0,
            State = ImportBatchState.Pending,
            StartedAt = DateTime.UtcNow,
            ModifiedDate = DateTime.UtcNow
        };
        db.Db.ImportBatches.Add(batch);
        await db.Db.SaveChangesAsync();
        db.Db.ChangeTracker.Clear();

        await using var ctx = CreateContext(db.ConnectionString);
        await using var tx = await ctx.Database.BeginTransactionAsync();

        var row1 = new ImportBatchRow
        {
            BatchID = batchId,
            RowNo = 1,
            RawData = "{\"FullName\":\"Nguyen A\"}",
            IsValid = true,
            ErrorCode = "",
            ErrorMessage = ""
        };
        ctx.ImportBatchRows.Add(row1);
        await ctx.SaveChangesAsync();
        await tx.CreateSavepointAsync("after_row1");

        try
        {
            // Cause unique constraint violation on Primary Key
            await ctx.Database.ExecuteSqlRawAsync(
                "INSERT INTO \"HEX_ImportBatchRow\" (\"ImportRowID\", \"BatchID\", \"RowNo\", \"RawData\", \"IsValid\", \"ErrorCode\", \"ErrorMessage\") VALUES ({0}, {1}, {2}, {3}, {4}, {5}, {6})",
                row1.ImportRowID, batchId, 2, "{}", true, "", "");
        }
        catch
        {
            await tx.RollbackToSavepointAsync("after_row1");
        }

        await tx.CommitAsync();

        await using var verifyCtx = CreateContext(db.ConnectionString);
        var rows = await verifyCtx.ImportBatchRows.Where(r => r.BatchID == batchId).ToListAsync();
        Assert.Single(rows);
        Assert.Equal(1, rows[0].RowNo);
    }

    [Fact]
    public async Task Outer_transaction_rollback_leaves_batch_uncommitted()
    {
        if (!PostgresTestDb.Enabled) return;

        using var db = new PostgresTestDb();
        var session = db.SeedSession();
        var batchId = Guid.NewGuid();
        var divisionId = db.Ctx.DivisionId;

        var batch = new ImportBatch
        {
            BatchID = batchId,
            DivisionID = divisionId,
            SessionID = session.SessionID,
            FileName = "rollback.xlsx",
            SheetName = "DanhSach",
            TotalRow = 1,
            SuccessRow = 1,
            ErrorRow = 0,
            State = ImportBatchState.Pending,
            StartedAt = DateTime.UtcNow,
            ModifiedDate = DateTime.UtcNow
        };
        db.Db.ImportBatches.Add(batch);
        await db.Db.SaveChangesAsync();
        db.Db.ChangeTracker.Clear();

        await using (var ctx = CreateContext(db.ConnectionString))
        {
            await using var tx = await ctx.Database.BeginTransactionAsync();
            var repo = new ImportRepository(ctx);
            var claimed = await repo.TryClaimForCommitAsync(divisionId, batchId, DateTime.UtcNow - TimeSpan.FromMinutes(5));
            Assert.True(claimed);

            await tx.RollbackAsync();
        }

        await using var verifyCtx = CreateContext(db.ConnectionString);
        var loaded = await verifyCtx.ImportBatches.FirstOrDefaultAsync(b => b.BatchID == batchId);
        Assert.NotNull(loaded);
        Assert.Equal(ImportBatchState.Pending, loaded.State);
    }
}
