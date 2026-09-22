using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using HealthExam.Application.Common;

namespace HealthExam.Application.Catalogs;

public sealed record ListProvincesQuery(string DivisionId, string Keyword = null);

public interface IListProvincesHandler
{
    Task<ApplicationResult<IReadOnlyList<MasterDataOptionResult>>> HandleAsync(
        ListProvincesQuery query, CancellationToken ct = default);
}

public sealed class ListProvincesHandler : IListProvincesHandler
{
    private readonly ICatalogRepository _repository;

    public ListProvincesHandler(ICatalogRepository repository)
    {
        _repository = repository;
    }

    public async Task<ApplicationResult<IReadOnlyList<MasterDataOptionResult>>> HandleAsync(
        ListProvincesQuery query, CancellationToken ct = default)
    {
        var result = await _repository.ListProvincesAsync(query.DivisionId, query.Keyword, ct);
        return ApplicationResult<IReadOnlyList<MasterDataOptionResult>>.Success(result);
    }
}
