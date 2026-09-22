using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HealthExam.Application.Common;
using HealthExam.Domain.Common;
using HealthExam.Domain.Integrations;
using HealthExam.Infrastructure.Persistence;
using HealthExam.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HealthExam.Tests.Infrastructure;

[Collection("PostgresTestDb")]
public class OutboxQueueIntegrationTests
{
    private static HealthExamDbContext CreateContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<HealthExamDbContext>()
            .UseNpgsql(connectionString)
            .Options;
        return new HealthExamDbContext(options);
    }

    [Fact]
    public async Task Concurrent_claims_return_different_rows_never_same_claimed_row()
    {
        if (!PostgresTestDb.Enabled) return;

        using var db = new PostgresTestDb();
        var divisionId = "DIV_QUEUE";
        var totalRows = 5;

        for (int i = 1; i <= totalRows; i++)
        {
            db.Db.IntegrationOutboxes.Add(new IntegrationOutbox
            {
                DivisionID = divisionId,
                OrderID = Guid.NewGuid(),
                Vendor = "RIS",
                Operation = "NEW",
                DedupKey = $"RIS:NEW:O{i}",
                Payload = "{}",
                State = OutboxState.Pending,
                RetryCount = 0,
                CreatedAt = DateTime.UtcNow.AddMinutes(i)
            });
        }
        await db.Db.SaveChangesAsync();

        var concurrency = 5;
        var claimedIds = new ConcurrentBag<long>();

        var tasks = Enumerable.Range(0, concurrency).Select(async _ =>
        {
            await using var context = CreateContext(db.ConnectionString);
            var uow = new UnitOfWork(context);
            await using var tx = await uow.BeginAsync();

            var repo = new IntegrationOutboxRepository(context);
            var claimed = await repo.ClaimNextAsync(divisionId, DateTime.UtcNow);

            if (claimed != null)
            {
                claimedIds.Add(claimed.OutboxID);
                claimed.Claim(DateTime.UtcNow);
                await uow.SaveChangesAsync();
            }

            await tx.CommitAsync();
        });

        await Task.WhenAll(tasks);

        // Every claimed row must be unique
        Assert.Equal(concurrency, claimedIds.Count);
        Assert.Equal(concurrency, claimedIds.Distinct().Count());
    }

    [Fact]
    public async Task ClaimNextAsync_returns_null_when_queue_empty_or_rows_locked()
    {
        if (!PostgresTestDb.Enabled) return;

        using var db = new PostgresTestDb();
        var divisionId = "DIV_EMPTY";

        await using var context = CreateContext(db.ConnectionString);
        var repo = new IntegrationOutboxRepository(context);
        var claimed = await repo.ClaimNextAsync(divisionId, DateTime.UtcNow);

        Assert.Null(claimed);
    }

    [Fact]
    public async Task LockAsync_locks_existing_row_and_returns_null_for_missing()
    {
        if (!PostgresTestDb.Enabled) return;

        using var db = new PostgresTestDb();
        var divisionId = "DIV_LOCK";
        var row = new IntegrationOutbox
        {
            DivisionID = divisionId,
            OrderID = Guid.NewGuid(),
            Vendor = "RIS",
            Operation = "NEW",
            DedupKey = "RIS:NEW:LOCK_TEST",
            Payload = "{}",
            State = OutboxState.Failed,
            RetryCount = 2,
            CreatedAt = DateTime.UtcNow
        };
        db.Db.IntegrationOutboxes.Add(row);
        await db.Db.SaveChangesAsync();

        await using var context = CreateContext(db.ConnectionString);
        var uow = new UnitOfWork(context);
        await using var tx = await uow.BeginAsync();

        var repo = new IntegrationOutboxRepository(context);
        var locked = await repo.LockAsync(row.OutboxID);
        Assert.NotNull(locked);
        Assert.Equal(row.OutboxID, locked.OutboxID);

        var missing = await repo.LockAsync(row.OutboxID + 99999);
        Assert.Null(missing);

        await tx.CommitAsync();
    }
}
