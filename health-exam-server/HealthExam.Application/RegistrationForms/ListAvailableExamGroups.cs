using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HealthExam.Application.Common;
using HealthExam.Domain.ExamSessions;

namespace HealthExam.Application.RegistrationForms;

public interface IListAvailableExamGroupsHandler
{
    Task<ApplicationResult<IReadOnlyList<AvailableExamGroupItem>>> HandleAsync(
        ListAvailableExamGroupsQuery query, CancellationToken ct = default);
}

public class ListAvailableExamGroupsHandler : IListAvailableExamGroupsHandler
{
    private readonly IRegistrationFormRepository _repo;

    public ListAvailableExamGroupsHandler(IRegistrationFormRepository repo)
    {
        _repo = repo;
    }

    public async Task<ApplicationResult<IReadOnlyList<AvailableExamGroupItem>>> HandleAsync(
        ListAvailableExamGroupsQuery query, CancellationToken ct = default)
    {
        var activeVariants = await _repo.ListActiveVariantCodesAsync(query.DivisionId, ct);
        var result = new List<AvailableExamGroupItem>();

        foreach (var variant in activeVariants)
        {
            var statutoryGroup = ExamGroups.Find(variant);
            if (statutoryGroup != null)
            {
                result.Add(new AvailableExamGroupItem
                {
                    VariantCode = statutoryGroup.VariantCode,
                    Name = statutoryGroup.GroupName,
                    OrderNo = statutoryGroup.OrderNo
                });
            }
        }

        var ordered = result.OrderBy(x => x.OrderNo).ToList();
        return ApplicationResult<IReadOnlyList<AvailableExamGroupItem>>.Success(ordered);
    }
}
