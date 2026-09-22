using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using HealthExam.Application.Common;

namespace HealthExam.Application.Catalogs;

public sealed record ListServiceGroupsQuery(
    string DivisionId,
    string CategoryCode = null,
    string Keyword = null);

public interface IListServiceGroupsHandler
{
    Task<ApplicationResult<IReadOnlyList<ServiceGroupResult>>> HandleAsync(
        ListServiceGroupsQuery query, CancellationToken ct = default);
}

public sealed class ListServiceGroupsHandler : IListServiceGroupsHandler
{
    private readonly ICatalogRepository _repository;

    public ListServiceGroupsHandler(ICatalogRepository repository)
    {
        _repository = repository;
    }

    public async Task<ApplicationResult<IReadOnlyList<ServiceGroupResult>>> HandleAsync(
        ListServiceGroupsQuery query, CancellationToken ct = default)
    {
        var result = await _repository.ListServiceGroupsAsync(
            query.DivisionId, query.CategoryCode, query.Keyword, ct);

        return ApplicationResult<IReadOnlyList<ServiceGroupResult>>.Success(result);
    }
}
