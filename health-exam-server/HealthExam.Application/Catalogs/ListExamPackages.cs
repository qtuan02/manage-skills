using System.Threading;
using System.Threading.Tasks;
using HealthExam.Application.Common;

namespace HealthExam.Application.Catalogs;

public sealed record ListExamPackagesQuery(
    string DivisionId,
    string Keyword = null,
    string VariantCode = null,
    bool? IsActive = null,
    int Page = 1,
    int Size = 20);

public interface IListExamPackagesHandler
{
    Task<ApplicationResult<PageResult<ExamPackageResult>>> HandleAsync(
        ListExamPackagesQuery query, CancellationToken ct = default);
}

public sealed class ListExamPackagesHandler : IListExamPackagesHandler
{
    private readonly ICatalogRepository _repository;

    public ListExamPackagesHandler(ICatalogRepository repository)
    {
        _repository = repository;
    }

    public async Task<ApplicationResult<PageResult<ExamPackageResult>>> HandleAsync(
        ListExamPackagesQuery query, CancellationToken ct = default)
    {
        var page = query.Page < 1 ? 1 : query.Page;
        var size = query.Size < 1 ? 20 : (query.Size > 200 ? 200 : query.Size);

        var result = await _repository.ListExamPackagesAsync(
            query.DivisionId, query.Keyword, query.VariantCode, query.IsActive, page, size, ct);

        return ApplicationResult<PageResult<ExamPackageResult>>.Success(result);
    }
}
