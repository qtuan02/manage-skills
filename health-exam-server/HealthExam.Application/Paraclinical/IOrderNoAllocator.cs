using System.Threading;
using System.Threading.Tasks;

namespace HealthExam.Application.Paraclinical;

public interface IOrderNoAllocator
{
    Task<string> NextAsync(CancellationToken ct = default);
}
