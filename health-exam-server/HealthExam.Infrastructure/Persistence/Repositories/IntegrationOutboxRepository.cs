using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HealthExam.Application.Paraclinical;
using HealthExam.Domain.Common;
using HealthExam.Domain.Integrations;
using Microsoft.EntityFrameworkCore;

namespace HealthExam.Infrastructure.Persistence.Repositories;

public sealed class IntegrationOutboxRepository : IIntegrationOutboxRepository
{
    private readonly HealthExamDbContext _db;

    public IntegrationOutboxRepository(HealthExamDbContext db)
    {
        _db = db;
    }

    public Task<IntegrationOutbox> ClaimNextAsync(
        string divisionId, DateTime nowUtc, CancellationToken ct = default)
        => ClaimNextAsync(divisionId, nowUtc, null, ct);

    public async Task<IntegrationOutbox> ClaimNextAsync(
        string divisionId, DateTime nowUtc, string vendor, CancellationToken ct = default)
    {
        if (!_db.Database.IsRelational())
        {
            var candidates = await _db.IntegrationOutboxes
                .Where(o => o.DivisionID == divisionId
                         && (vendor == null || o.Vendor == vendor)
                         && (o.State == OutboxState.Pending
                             || (o.State == OutboxState.Failed && (o.NextAttemptAt == null || o.NextAttemptAt <= nowUtc))))
                .ToListAsync(ct);

            var sorted = candidates.OrderBy(o => o.CreatedAt).ThenBy(o => o.OutboxID).ToList();
            foreach (var o in sorted)
            {
                var hasPrior = await _db.IntegrationOutboxes.AnyAsync(p =>
                    p.DivisionID == o.DivisionID
                    && p.OrderID == o.OrderID
                    && (vendor == null || p.Vendor == vendor)
                    && (p.State == OutboxState.Pending || p.State == OutboxState.Failed)
                    && (p.CreatedAt < o.CreatedAt || (p.CreatedAt == o.CreatedAt && p.OutboxID < o.OutboxID)), ct);
                if (!hasPrior) return o;
            }
            return null;
        }

        if (string.IsNullOrEmpty(vendor))
        {
            return await _db.IntegrationOutboxes
                .FromSqlRaw(
                    """
                    SELECT o.* FROM "HEX_IntegrationOutbox" o
                    WHERE o."DivisionID" = {0}
                      AND (o."State" = 0
                           OR (o."State" = 2
                               AND (o."NextAttemptAt" IS NULL OR o."NextAttemptAt" <= {1})))
                      AND NOT EXISTS (
                            SELECT 1 FROM "HEX_IntegrationOutbox" p
                            WHERE p."DivisionID" = o."DivisionID"
                              AND p."OrderID" = o."OrderID"
                              AND p."State" IN (0, 2)
                              AND (p."CreatedAt", p."OutboxID") < (o."CreatedAt", o."OutboxID"))
                    ORDER BY o."CreatedAt", o."OutboxID"
                    LIMIT 1
                    FOR UPDATE SKIP LOCKED
                    """,
                    divisionId, nowUtc)
                .AsTracking()
                .FirstOrDefaultAsync(ct);
        }

        return await _db.IntegrationOutboxes
            .FromSqlRaw(
                """
                SELECT o.* FROM "HEX_IntegrationOutbox" o
                WHERE o."DivisionID" = {0}
                  AND o."Vendor" = {2}
                  AND (o."State" = 0
                       OR (o."State" = 2
                           AND (o."NextAttemptAt" IS NULL OR o."NextAttemptAt" <= {1})))
                  AND NOT EXISTS (
                        SELECT 1 FROM "HEX_IntegrationOutbox" p
                        WHERE p."DivisionID" = o."DivisionID"
                          AND p."OrderID" = o."OrderID"
                          AND p."Vendor" = {2}
                          AND p."State" IN (0, 2)
                          AND (p."CreatedAt", p."OutboxID") < (o."CreatedAt", o."OutboxID"))
                ORDER BY o."CreatedAt", o."OutboxID"
                LIMIT 1
                FOR UPDATE SKIP LOCKED
                """,
                divisionId, nowUtc, vendor)
            .AsTracking()
            .FirstOrDefaultAsync(ct);
    }

    public async Task<IntegrationOutbox> LockAsync(
        long outboxId, CancellationToken ct = default)
    {
        if (!_db.Database.IsRelational())
        {
            return await _db.IntegrationOutboxes
                .AsTracking()
                .FirstOrDefaultAsync(o => o.OutboxID == outboxId, ct);
        }

        return await _db.IntegrationOutboxes
            .FromSqlRaw(
                """
                SELECT o.* FROM "HEX_IntegrationOutbox" o WHERE o."OutboxID" = {0} FOR UPDATE
                """,
                outboxId)
            .AsTracking()
            .FirstOrDefaultAsync(ct);
    }

    public async Task<bool> WasSentAsync(
        Guid orderId, string messageType, CancellationToken ct = default)
    {
        return await _db.IntegrationOutboxes
            .AnyAsync(o => o.OrderID == orderId && o.Operation == messageType && o.State == OutboxState.Sent, ct);
    }

    public async Task<IntegrationOutbox> FindByDedupKeyAsync(
        string divisionId, string dedupKey, CancellationToken ct = default)
    {
        return await _db.IntegrationOutboxes
            .AsTracking()
            .FirstOrDefaultAsync(o => o.DivisionID == divisionId && o.DedupKey == dedupKey, ct);
    }

    public async Task<bool> HasSentCancelAsync(
        string divisionId, Guid orderId, CancellationToken ct = default)
    {
        return await _db.IntegrationOutboxes
            .AnyAsync(o => o.DivisionID == divisionId
                        && o.OrderID == orderId
                        && o.Operation == "CANCELLED"
                        && o.State == OutboxState.Sent, ct);
    }

    public Task<int> CountPendingAsync(
        string divisionId, DateTime nowUtc, CancellationToken ct = default)
        => CountPendingAsync(divisionId, nowUtc, null, ct);

    public async Task<int> CountPendingAsync(
        string divisionId, DateTime nowUtc, string vendor, CancellationToken ct = default)
    {
        return await _db.IntegrationOutboxes
            .CountAsync(o => o.DivisionID == divisionId
                          && (vendor == null || o.Vendor == vendor)
                          && (o.State == OutboxState.Pending
                              || (o.State == OutboxState.Failed && (o.NextAttemptAt == null || o.NextAttemptAt <= nowUtc))), ct);
    }

    public async Task<int> CountDeadLetterAsync(
        string divisionId, CancellationToken ct = default)
    {
        return await _db.IntegrationOutboxes
            .CountAsync(o => o.DivisionID == divisionId && o.State == OutboxState.DeadLetter, ct);
    }

    public void Add(IntegrationOutbox row)
    {
        _db.IntegrationOutboxes.Add(row);
    }
}
