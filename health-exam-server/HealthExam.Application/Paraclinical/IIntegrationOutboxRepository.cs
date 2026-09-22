using System;
using System.Threading;
using System.Threading.Tasks;
using HealthExam.Domain.Integrations;

namespace HealthExam.Application.Paraclinical;

public interface IIntegrationOutboxRepository
{
    Task<IntegrationOutbox> ClaimNextAsync(
        string divisionId, DateTime nowUtc, CancellationToken ct = default);

    Task<IntegrationOutbox> ClaimNextAsync(
        string divisionId, DateTime nowUtc, string vendor, CancellationToken ct = default);

    Task<IntegrationOutbox> LockAsync(
        long outboxId, CancellationToken ct = default);

    Task<bool> WasSentAsync(
        Guid orderId, string messageType, CancellationToken ct = default);

    Task<IntegrationOutbox> FindByDedupKeyAsync(
        string divisionId, string dedupKey, CancellationToken ct = default);

    Task<bool> HasSentCancelAsync(
        string divisionId, Guid orderId, CancellationToken ct = default);

    Task<int> CountPendingAsync(
        string divisionId, DateTime nowUtc, CancellationToken ct = default);

    Task<int> CountPendingAsync(
        string divisionId, DateTime nowUtc, string vendor, CancellationToken ct = default);

    Task<int> CountDeadLetterAsync(
        string divisionId, CancellationToken ct = default);

    void Add(IntegrationOutbox row);
}
