using System.Threading;
using System.Threading.Tasks;
using HealthExam.Application.Common;

namespace HealthExam.Application.ExamSessions;

public interface IListExamSessionsHandler
{
    Task<ApplicationResult<PageResult<ExamSessionResult>>> HandleAsync(
        ListExamSessionsQuery query, CancellationToken ct = default);
}

public sealed class ListExamSessionsHandler : IListExamSessionsHandler
{
    private readonly IExamSessionRepository _repository;

    public ListExamSessionsHandler(IExamSessionRepository repository)
    {
        _repository = repository;
    }

    public async Task<ApplicationResult<PageResult<ExamSessionResult>>> HandleAsync(
        ListExamSessionsQuery query, CancellationToken ct = default)
    {
        var filter = query.Filter ?? new ExamSessionFilter();
        var page = filter.Page < 1 ? 1 : filter.Page;
        var size = filter.Size < 1 ? 20 : (filter.Size > 200 ? 200 : filter.Size);
        var normalizedFilter = filter with { Page = page, Size = size };

        var result = await _repository.ListAsync(query.DivisionId, normalizedFilter, ct);
        return ApplicationResult<PageResult<ExamSessionResult>>.Success(result);
    }
}
