using System;
using System.Threading;
using System.Threading.Tasks;
using HealthExam.Application.Common;

namespace HealthExam.Application.ExamRecords;

public interface IGetExamRecordHandler
{
    Task<ApplicationResult<ExamRecordResult>> HandleAsync(
        GetExamRecordQuery query, CancellationToken ct = default);
}

public sealed class GetExamRecordHandler : IGetExamRecordHandler
{
    private readonly IExamRecordRepository _repository;

    public GetExamRecordHandler(IExamRecordRepository repository)
    {
        _repository = repository;
    }

    public async Task<ApplicationResult<ExamRecordResult>> HandleAsync(
        GetExamRecordQuery query, CancellationToken ct = default)
    {
        var record = await _repository.GetResultAsync(query.DivisionId, query.RecordId, ct);
        if (record == null)
        {
            return ApplicationResult<ExamRecordResult>.Fail(
                ApplicationFailureCode.NotFound, "Không tìm thấy hồ sơ khám");
        }

        return ApplicationResult<ExamRecordResult>.Success(record);
    }
}
