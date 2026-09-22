using System.Threading;
using System.Threading.Tasks;

namespace HealthExam.Application.Integrations;

public interface IRisClient
{
    Task<RisSendResult> SendOrderAsync(
        string payload, long outboxId, CancellationToken ct = default);
}
