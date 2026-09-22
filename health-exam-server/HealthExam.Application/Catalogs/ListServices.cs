using System.Threading;
using System.Threading.Tasks;
using HealthExam.Application.Common;

namespace HealthExam.Application.Catalogs;

public sealed record ListServicesQuery(
    string DivisionId,
    string CategoryCode = null,
    string GroupCode = null,
    string Keyword = null,
    int Page = 1,
    int Size = 20);

public interface IListServicesHandler
{
    Task<ApplicationResult<PageResult<ServiceCatalogResult>>> HandleAsync(
        ListServicesQuery query, CancellationToken ct = default);
}

public sealed class ListServicesHandler : IListServicesHandler
{
    private readonly ICatalogRepository _repository;

    public ListServicesHandler(ICatalogRepository repository)
    {
        _repository = repository;
    }

    public async Task<ApplicationResult<PageResult<ServiceCatalogResult>>> HandleAsync(
        ListServicesQuery query, CancellationToken ct = default)
    {
        var page = query.Page < 1 ? 1 : query.Page;
        var size = query.Size < 1 ? 20 : (query.Size > 200 ? 200 : query.Size);

        var filter = new ServiceCatalogFilter(
            query.CategoryCode, query.GroupCode, query.Keyword, page, size);

        var result = await _repository.ListServicesAsync(query.DivisionId, filter, ct);
        return ApplicationResult<PageResult<ServiceCatalogResult>>.Success(result);
    }
}
