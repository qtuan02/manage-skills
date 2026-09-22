using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace HealthExam.Application.Paraclinical;

public interface IVendorLineNoAllocator
{
    Task<IReadOnlyList<long>> NextAsync(int count, CancellationToken ct = default);
}
