using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HealthExam.Application.Common;
using HealthExam.Domain.ExamSessions;

namespace HealthExam.Application.Catalogs;

public sealed record ListExamGroupsQuery();

public interface IListExamGroupsHandler
{
    Task<ApplicationResult<IReadOnlyList<ExamGroupResult>>> HandleAsync(
        ListExamGroupsQuery query = null, CancellationToken ct = default);
}

public sealed class ListExamGroupsHandler : IListExamGroupsHandler
{
    public Task<ApplicationResult<IReadOnlyList<ExamGroupResult>>> HandleAsync(
        ListExamGroupsQuery query = null, CancellationToken ct = default)
    {
        var items = ExamGroups.All
            .OrderBy(x => x.OrderNo)
            .Select(x => new ExamGroupResult(x.VariantCode, x.GroupName, x.FormCode, x.OrderNo))
            .ToList();

        return Task.FromResult(ApplicationResult<IReadOnlyList<ExamGroupResult>>.Success(items));
    }
}
