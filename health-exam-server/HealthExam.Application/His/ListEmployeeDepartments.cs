#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HealthExam.Application.Common;
using HealthExam.Application.Integrations;

namespace HealthExam.Application.His;

public interface IListEmployeeDepartmentsHandler
{
    Task<ApplicationResult<IReadOnlyList<DepartmentCatalogResult>>> HandleAsync(
        ListEmployeeDepartmentsQuery query, CancellationToken ct = default);
}

public sealed class ListEmployeeDepartmentsHandler : IListEmployeeDepartmentsHandler
{
    private readonly IHisEmrClient _client;

    public ListEmployeeDepartmentsHandler(IHisEmrClient client)
    {
        _client = client;
    }

    public async Task<ApplicationResult<IReadOnlyList<DepartmentCatalogResult>>> HandleAsync(
        ListEmployeeDepartmentsQuery query, CancellationToken ct = default)
    {
        if (query.EmployeeId <= 0)
        {
            return ApplicationResult<IReadOnlyList<DepartmentCatalogResult>>.Fail(
                ApplicationFailureCode.Forbidden, "Chỉ nhân viên đã xác thực mới được lấy danh sách khoa");
        }

        var hisRes = await _client.ListEmployeeDepartmentsAsync(
            query.EmployeeId,
            new HisCallContext(query.Credential, query.TraceId, query.DivisionId),
            ct);

        if (!hisRes.IsSuccess)
        {
            return HisOutcomeMapper.ToApplicationResult<IReadOnlyList<DepartmentCatalogResult>>(hisRes);
        }

        var items = hisRes.Value ?? Array.Empty<DepartmentCatalogResult>();
        var distinctSorted = items
            .GroupBy(x => x.DepartmentId)
            .Select(g => g.First())
            .OrderBy(x => x.DepartmentName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(x => x.DepartmentId)
            .ToList();

        return ApplicationResult<IReadOnlyList<DepartmentCatalogResult>>.Success(distinctSorted);
    }
}
