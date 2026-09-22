using System;
using System.Threading;
using System.Threading.Tasks;
using HealthExam.Application.His;

namespace HealthExam.Application.Integrations;

public interface IHisFormDefinitionCache
{
    Task<HisFormDefinitionResult> GetOrCreateAsync(
        string divisionId,
        string templateCode,
        Func<CancellationToken, Task<HisFormDefinitionResult>> factory,
        CancellationToken ct = default);

    Task<System.Collections.Generic.IReadOnlyList<Icd10Choice>> GetOrCreateIcd10Async(
        string divisionId,
        string filter,
        int amount,
        Func<CancellationToken, Task<System.Collections.Generic.IReadOnlyList<Icd10Choice>>> factory,
        CancellationToken ct = default)
        => factory(ct);
}
