using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using HealthExam.Domain.Webhooks;

namespace HealthExam.Application.Webhooks;

public interface IWebhookInboxRepository
{
    Task<bool> ExistsAsync(string divisionId, string eventId, CancellationToken ct = default);
    Task<bool> ExistsAsync(string eventId, CancellationToken ct = default);
    Task<WebhookInbox> ClaimNextAsync(DateTime nowUtc, CancellationToken ct = default);
    Task<WebhookInbox> LockAsync(long inboxId, CancellationToken ct = default);
    Task<IReadOnlyList<WebhookInbox>> FindForRequeueAsync(
        WebhookRequeueFilter filter, CancellationToken ct = default);
    Task<int> CountForRequeueAsync(
        WebhookRequeueFilter filter, CancellationToken ct = default);
    Task<int> CountPendingAsync(string divisionId = null, CancellationToken ct = default);
    Task<int> CountDeadLetterAsync(string divisionId = null, CancellationToken ct = default);
    Task<bool> HasAttachmentAddedBeenProcessedAsync(
        string divisionId, Guid recordId, Guid attachmentId, long currentInboxId, CancellationToken ct = default);
    void Add(WebhookInbox row);
}
