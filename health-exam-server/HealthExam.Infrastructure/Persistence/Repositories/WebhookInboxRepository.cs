using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using HealthExam.Application.Webhooks;
using HealthExam.Domain.Common;
using HealthExam.Domain.Webhooks;
using Microsoft.EntityFrameworkCore;

namespace HealthExam.Infrastructure.Persistence.Repositories;

public class WebhookInboxRepository : IWebhookInboxRepository
{
    private readonly HealthExamDbContext _db;

    public WebhookInboxRepository(HealthExamDbContext db)
    {
        _db = db;
    }

    public async Task<bool> ExistsAsync(string divisionId, string eventId, CancellationToken ct = default)
    {
        return await _db.WebhookInboxes.AnyAsync(
            x => x.DivisionID == divisionId && x.EventID == eventId, ct);
    }

    public async Task<bool> ExistsAsync(string eventId, CancellationToken ct = default)
    {
        return await _db.WebhookInboxes.AnyAsync(x => x.EventID == eventId, ct);
    }

    public async Task<WebhookInbox> ClaimNextAsync(DateTime nowUtc, CancellationToken ct = default)
    {
        if (_db.Database.IsNpgsql())
        {
            return await _db.WebhookInboxes
                .FromSqlRaw("""
                    SELECT * FROM "HEX_WebhookInbox"
                    WHERE "ProcessState" = 0
                       OR ("ProcessState" = 2 AND "RetryCount" < 5 AND ("NextAttemptAt" IS NULL OR "NextAttemptAt" <= {0}))
                    ORDER BY "ReceivedAt"
                    LIMIT 1
                    FOR UPDATE SKIP LOCKED
                    """, nowUtc)
                .AsTracking()
                .FirstOrDefaultAsync(ct);
        }

        return await _db.WebhookInboxes
            .AsTracking()
            .Where(x => x.ProcessState == WebhookProcessState.New
                     || (x.ProcessState == WebhookProcessState.Failed
                         && x.RetryCount < WebhookInbox.MaxRetry
                         && (x.NextAttemptAt == null || x.NextAttemptAt <= nowUtc)))
            .OrderBy(x => x.ReceivedAt)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<WebhookInbox> LockAsync(long inboxId, CancellationToken ct = default)
    {
        if (_db.Database.IsNpgsql())
        {
            return await _db.WebhookInboxes
                .FromSqlRaw("""SELECT * FROM "HEX_WebhookInbox" WHERE "InboxID" = {0} FOR UPDATE""", inboxId)
                .AsTracking()
                .FirstOrDefaultAsync(ct);
        }

        return await _db.WebhookInboxes
            .AsTracking()
            .FirstOrDefaultAsync(x => x.InboxID == inboxId, ct);
    }

    public async Task<IReadOnlyList<WebhookInbox>> FindForRequeueAsync(
        WebhookRequeueFilter filter, CancellationToken ct = default)
    {
        var q = BuildRequeueQuery(filter).AsTracking();

        var take = filter.Take <= 0 ? 100 : filter.Take;
        return await q
            .OrderBy(x => x.ReceivedAt)
            .Take(take)
            .ToListAsync(ct);
    }

    public async Task<int> CountForRequeueAsync(
        WebhookRequeueFilter filter, CancellationToken ct = default)
    {
        var q = BuildRequeueQuery(filter);
        return await q.CountAsync(ct);
    }

    public async Task<int> CountPendingAsync(string divisionId = null, CancellationToken ct = default)
    {
        var q = _db.WebhookInboxes.Where(
            x => x.ProcessState == WebhookProcessState.New
              || (x.ProcessState == WebhookProcessState.Failed && x.RetryCount < WebhookInbox.MaxRetry));

        if (!string.IsNullOrWhiteSpace(divisionId))
            q = q.Where(x => x.DivisionID == divisionId);

        return await q.CountAsync(ct);
    }

    public async Task<int> CountDeadLetterAsync(string divisionId = null, CancellationToken ct = default)
    {
        var q = _db.WebhookInboxes.Where(
            x => x.ProcessState == WebhookProcessState.Failed && x.RetryCount >= WebhookInbox.MaxRetry);

        if (!string.IsNullOrWhiteSpace(divisionId))
            q = q.Where(x => x.DivisionID == divisionId);

        return await q.CountAsync(ct);
    }

    public async Task<bool> HasAttachmentAddedBeenProcessedAsync(
        string divisionId, Guid recordId, Guid attachmentId, long currentInboxId, CancellationToken ct = default)
    {
        var payloads = await _db.WebhookInboxes
            .Where(x => x.RecordID == recordId
                     && x.DivisionID == divisionId
                     && x.EventType == FormEventNames.AttachmentAdded
                     && x.InboxID != currentInboxId
                     && (x.ProcessState == WebhookProcessState.Processed || x.ProcessState == WebhookProcessState.Skipped))
            .Select(x => x.Payload)
            .ToListAsync(ct);

        foreach (var payload in payloads)
        {
            try
            {
                using var doc = JsonDocument.Parse(payload ?? "{}");
                var root = doc.RootElement;
                if (root.TryGetProperty("Data", out var dataEl) || root.TryGetProperty("data", out dataEl))
                {
                    if (dataEl.TryGetProperty("AttachmentID", out var attEl) || dataEl.TryGetProperty("attachmentId", out attEl))
                    {
                        if (attEl.TryGetGuid(out var gid) && gid == attachmentId) return true;
                        if (Guid.TryParse(attEl.GetString(), out var sGid) && sGid == attachmentId) return true;
                    }
                }
            }
            catch
            {
                // Ignore malformed payloads
            }
        }

        return false;
    }

    public void Add(WebhookInbox inbox)
    {
        _db.WebhookInboxes.Add(inbox);
    }

    private IQueryable<WebhookInbox> BuildRequeueQuery(WebhookRequeueFilter filter)
    {
        var q = _db.WebhookInboxes.AsQueryable();

        if (!string.IsNullOrWhiteSpace(filter.DivisionId))
            q = q.Where(x => x.DivisionID == filter.DivisionId);

        if (filter.ProcessState.HasValue)
            q = q.Where(x => x.ProcessState == filter.ProcessState.Value);

        if (filter.MinRetryCount.HasValue)
            q = q.Where(x => x.RetryCount >= filter.MinRetryCount.Value);

        if (!string.IsNullOrWhiteSpace(filter.EventId))
        {
            var wanted = filter.EventId.Trim();
            q = q.Where(x => x.EventID == wanted);
        }

        if (filter.EventTypes != null && filter.EventTypes.Count > 0)
            q = q.Where(x => filter.EventTypes.Contains(x.EventType));

        if (filter.ProcessedBefore.HasValue)
            q = q.Where(x => x.ProcessedAt == null || x.ProcessedAt < filter.ProcessedBefore.Value);

        return q;
    }
}
