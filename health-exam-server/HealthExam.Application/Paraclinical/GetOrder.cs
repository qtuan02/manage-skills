using System.Threading;
using System.Threading.Tasks;
using HealthExam.Application.Common;

namespace HealthExam.Application.Paraclinical;

public interface IGetOrderHandler
{
    Task<ApplicationResult<ParaclinicalOrderResult>> HandleAsync(
        GetOrderQuery query, CancellationToken ct = default);
}

public class GetOrderHandler : IGetOrderHandler
{
    private readonly IParaclinicalRepository _paraclinicalRepository;

    public GetOrderHandler(IParaclinicalRepository paraclinicalRepository)
    {
        _paraclinicalRepository = paraclinicalRepository;
    }

    public async Task<ApplicationResult<ParaclinicalOrderResult>> HandleAsync(
        GetOrderQuery query, CancellationToken ct = default)
    {
        var order = await _paraclinicalRepository.GetAsync(
            query.DivisionId, query.OrderId, includeItems: true, forUpdate: false, ct);

        if (order == null)
        {
            return ApplicationResult<ParaclinicalOrderResult>.Fail(
                ApplicationFailureCode.NotFound, "Không tìm thấy phiếu chỉ định");
        }

        return ApplicationResult<ParaclinicalOrderResult>.Success(order.ToResult(includeCancelled: true));
    }
}
