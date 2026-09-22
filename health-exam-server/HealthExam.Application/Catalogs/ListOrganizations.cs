using System.Threading;
using System.Threading.Tasks;
using HealthExam.Application.Common;

namespace HealthExam.Application.Catalogs;

public sealed record ListOrganizationsQuery(
    string DivisionId,
    string Keyword = null,
    bool? IsActive = null,
    int Page = 1,
    int Size = 20);

public interface IListOrganizationsHandler
{
    Task<ApplicationResult<PageResult<OrganizationResult>>> HandleAsync(
        ListOrganizationsQuery query, CancellationToken ct = default);
}

public sealed class ListOrganizationsHandler : IListOrganizationsHandler
{
    private readonly ICatalogRepository _repository;

    public ListOrganizationsHandler(ICatalogRepository repository)
    {
        _repository = repository;
    }

    public async Task<ApplicationResult<PageResult<OrganizationResult>>> HandleAsync(
        ListOrganizationsQuery query, CancellationToken ct = default)
    {
        var page = query.Page < 1 ? 1 : query.Page;
        var size = query.Size < 1 ? 20 : (query.Size > 200 ? 200 : query.Size);

        var result = await _repository.ListOrganizationsAsync(
            query.DivisionId, query.Keyword, query.IsActive, page, size, ct);

        return ApplicationResult<PageResult<OrganizationResult>>.Success(result);
    }
}
