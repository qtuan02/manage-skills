using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using HealthExam.Application.Common;

namespace HealthExam.Application.Catalogs;

public sealed record ListServiceCategoriesQuery(string DivisionId);

public interface IListServiceCategoriesHandler
{
    Task<ApplicationResult<IReadOnlyList<ServiceCategoryResult>>> HandleAsync(
        ListServiceCategoriesQuery query, CancellationToken ct = default);
}

public sealed class ListServiceCategoriesHandler : IListServiceCategoriesHandler
{
    private readonly ICatalogRepository _repository;

    public ListServiceCategoriesHandler(ICatalogRepository repository)
    {
        _repository = repository;
    }

    public async Task<ApplicationResult<IReadOnlyList<ServiceCategoryResult>>> HandleAsync(
        ListServiceCategoriesQuery query, CancellationToken ct = default)
    {
        var result = await _repository.ListServiceCategoriesAsync(query.DivisionId, ct);
        return ApplicationResult<IReadOnlyList<ServiceCategoryResult>>.Success(result);
    }
}
