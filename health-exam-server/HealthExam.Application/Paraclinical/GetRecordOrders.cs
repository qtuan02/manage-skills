using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HealthExam.Application.Common;
using HealthExam.Application.ExamRecords;

namespace HealthExam.Application.Paraclinical;

public interface IGetRecordOrdersHandler
{
    Task<ApplicationResult<IReadOnlyList<ParaclinicalOrderResult>>> HandleAsync(
        GetRecordOrdersQuery query, CancellationToken ct = default);
}

public class GetRecordOrdersHandler : IGetRecordOrdersHandler
{
    private readonly IParaclinicalRepository _paraclinicalRepository;
    private readonly IExamRecordRepository _recordRepository;

    public GetRecordOrdersHandler(
        IParaclinicalRepository paraclinicalRepository,
        IExamRecordRepository recordRepository)
    {
        _paraclinicalRepository = paraclinicalRepository;
        _recordRepository = recordRepository;
    }

    public async Task<ApplicationResult<IReadOnlyList<ParaclinicalOrderResult>>> HandleAsync(
        GetRecordOrdersQuery query, CancellationToken ct = default)
    {
        var record = await _recordRepository.GetAsync(query.DivisionId, query.RecordId, forUpdate: false, ct);
        if (record == null)
        {
            return ApplicationResult<IReadOnlyList<ParaclinicalOrderResult>>.Fail(
                ApplicationFailureCode.NotFound, "Không tìm thấy hồ sơ khám");
        }

        var orders = await _paraclinicalRepository.ListByRecordAsync(
            query.DivisionId, query.RecordId, query.IncludeCancelled, forUpdate: false, ct);

        var result = orders
            .Select(o => o.ToResult(query.IncludeCancelled))
            .ToList();

        return ApplicationResult<IReadOnlyList<ParaclinicalOrderResult>>.Success(result);
    }
}
