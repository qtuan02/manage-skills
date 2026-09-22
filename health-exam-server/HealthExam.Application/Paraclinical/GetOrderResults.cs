using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HealthExam.Application.Common;

namespace HealthExam.Application.Paraclinical;

public sealed record GetOrderResultsQuery(
    string DivisionId,
    Guid OrderId);

public sealed record GetResultQuery(
    string DivisionId,
    Guid ResultId);

public interface IGetOrderResultsHandler
{
    Task<ApplicationResult<IReadOnlyList<ParaclinicalResultDto>>> HandleAsync(
        GetOrderResultsQuery query, CancellationToken ct = default);
}

public interface IGetResultHandler
{
    Task<ApplicationResult<ParaclinicalResultDto>> HandleAsync(
        GetResultQuery query, CancellationToken ct = default);
}

public class GetOrderResultsHandler : IGetOrderResultsHandler
{
    private readonly IParaclinicalRepository _paraclinicalRepository;

    public GetOrderResultsHandler(IParaclinicalRepository paraclinicalRepository)
    {
        _paraclinicalRepository = paraclinicalRepository;
    }

    public async Task<ApplicationResult<IReadOnlyList<ParaclinicalResultDto>>> HandleAsync(
        GetOrderResultsQuery query, CancellationToken ct = default)
    {
        var order = await _paraclinicalRepository.GetAsync(
            query.DivisionId, query.OrderId, includeItems: false, forUpdate: false, ct);
        if (order == null)
        {
            return ApplicationResult<IReadOnlyList<ParaclinicalResultDto>>.Fail(
                ApplicationFailureCode.NotFound, "Không tìm thấy phiếu chỉ định");
        }

        var results = await _paraclinicalRepository.ListResultsByOrderAsync(
            query.DivisionId, query.OrderId, ct);

        var list = results.Select(x => x.ToResult()).ToList();
        return ApplicationResult<IReadOnlyList<ParaclinicalResultDto>>.Success(list);
    }
}

public class GetResultHandler : IGetResultHandler
{
    private readonly IParaclinicalRepository _paraclinicalRepository;

    public GetResultHandler(IParaclinicalRepository paraclinicalRepository)
    {
        _paraclinicalRepository = paraclinicalRepository;
    }

    public async Task<ApplicationResult<ParaclinicalResultDto>> HandleAsync(
        GetResultQuery query, CancellationToken ct = default)
    {
        var result = await _paraclinicalRepository.GetResultAsync(
            query.DivisionId, query.ResultId, ct);
        if (result == null)
        {
            return ApplicationResult<ParaclinicalResultDto>.Fail(
                ApplicationFailureCode.NotFound, "Không tìm thấy kết quả cận lâm sàng");
        }

        return ApplicationResult<ParaclinicalResultDto>.Success(result.ToResult());
    }
}
