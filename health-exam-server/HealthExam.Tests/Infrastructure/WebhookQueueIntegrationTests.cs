using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HealthExam.Application.Common;
using HealthExam.Domain.Common;
using HealthExam.Domain.Webhooks;
using HealthExam.Infrastructure.Persistence;
using HealthExam.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HealthExam.Tests.Infrastructure;

[Collection("PostgresTestDb")]
public class WebhookQueueIntegrationTests
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
        var divisionId = "DIV_WH_QUEUE";
        var totalRows = 5;

        for (int i = 1; i <= totalRows; i++)
        {
            db.Db.WebhookInboxes.Add(new WebhookInbox
            {
                DivisionID = divisionId,
                EventID = $"EVT_CONCURRENT_{i}_{Guid.NewGuid():N}",
                EventType = "submission.section.signed",
                Payload = "{}",
                ProcessState = WebhookProcessState.New,
                RetryCount = 0,
                OccurredAt = DateTime.UtcNow.AddMinutes(-10 + i),
                ReceivedAt = DateTime.UtcNow.AddMinutes(-10 + i)
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

            var repo = new WebhookInboxRepository(context);
            var claimed = await repo.ClaimNextAsync(DateTime.UtcNow);

            if (claimed != null)
            {
                claimedIds.Add(claimed.InboxID);
                claimed.Stamp(WebhookProcessState.Processed, "ok", DateTime.UtcNow);
                await uow.SaveChangesAsync();
            }

            await tx.CommitAsync();
        });

        await Task.WhenAll(tasks);

        Assert.Equal(concurrency, claimedIds.Count);
        Assert.Equal(concurrency, claimedIds.Distinct().Count());
    }

    [Fact]
    public async Task ClaimNextAsync_respects_NextAttemptAt_and_MaxRetry()
    {
        if (!PostgresTestDb.Enabled) return;

        using var db = new PostgresTestDb();
        var now = DateTime.UtcNow;

        // Row 1: Future attempt (should be skipped)
        var futureRow = new WebhookInbox
        {
            DivisionID = "DIV_TEST",
            EventID = $"EVT_FUTURE_{Guid.NewGuid():N}",
            EventType = "submission.section.signed",
            Payload = "{}",
            ProcessState = WebhookProcessState.Failed,
            RetryCount = 1,
            NextAttemptAt = now.AddMinutes(10),
            OccurredAt = now,
            ReceivedAt = now
        };

        // Row 2: Dead letter (exceeded MaxRetry, should be skipped)
        var deadLetterRow = new WebhookInbox
        {
            DivisionID = "DIV_TEST",
            EventID = $"EVT_DEAD_{Guid.NewGuid():N}",
            EventType = "submission.section.signed",
            Payload = "{}",
            ProcessState = WebhookProcessState.Failed,
            RetryCount = WebhookInbox.MaxRetry,
            NextAttemptAt = now.AddMinutes(-5),
            OccurredAt = now,
            ReceivedAt = now
        };

        // Row 3: Eligible retry row
        var eligibleRow = new WebhookInbox
        {
            DivisionID = "DIV_TEST",
            EventID = $"EVT_ELIGIBLE_{Guid.NewGuid():N}",
            EventType = "submission.section.signed",
            Payload = "{}",
            ProcessState = WebhookProcessState.Failed,
            RetryCount = 2,
            NextAttemptAt = now.AddMinutes(-1),
            OccurredAt = now,
            ReceivedAt = now
        };

        db.Db.WebhookInboxes.AddRange(futureRow, deadLetterRow, eligibleRow);
        await db.Db.SaveChangesAsync();

        await using var context = CreateContext(db.ConnectionString);
        var repo = new WebhookInboxRepository(context);

        var claimed = await repo.ClaimNextAsync(now);

        Assert.NotNull(claimed);
        Assert.Equal(eligibleRow.EventID, claimed.EventID);
    }

    [Fact]
    public async Task ClaimNextAsync_returns_null_when_queue_empty()
    {
        if (!PostgresTestDb.Enabled) return;

        using var db = new PostgresTestDb();
        await using var context = CreateContext(db.ConnectionString);
        var repo = new WebhookInboxRepository(context);

        var claimed = await repo.ClaimNextAsync(DateTime.UtcNow);
        Assert.Null(claimed);
    }

    [Fact]
    public async Task LockAsync_locks_existing_row_and_returns_null_for_missing()
    {
        if (!PostgresTestDb.Enabled) return;

        using var db = new PostgresTestDb();
        var row = new WebhookInbox
        {
            DivisionID = "DIV_LOCK",
            EventID = $"EVT_LOCK_{Guid.NewGuid():N}",
            EventType = "submission.state.changed",
            Payload = "{}",
            ProcessState = WebhookProcessState.New,
            OccurredAt = DateTime.UtcNow,
            ReceivedAt = DateTime.UtcNow
        };
        db.Db.WebhookInboxes.Add(row);
        await db.Db.SaveChangesAsync();

        await using var context = CreateContext(db.ConnectionString);
        var repo = new WebhookInboxRepository(context);

        var found = await repo.LockAsync(row.InboxID);
        Assert.NotNull(found);
        Assert.Equal(row.EventID, found.EventID);

        var missing = await repo.LockAsync(999999999);
        Assert.Null(missing);
    }
}
