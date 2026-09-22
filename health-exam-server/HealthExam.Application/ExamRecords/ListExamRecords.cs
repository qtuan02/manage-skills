using System.Threading;
using System.Threading.Tasks;
using HealthExam.Application.Common;

namespace HealthExam.Application.ExamRecords;

public interface IListExamRecordsHandler
{
    Task<ApplicationResult<PageResult<ExamRecordResult>>> HandleAsync(
        ListExamRecordsQuery query, CancellationToken ct = default);
}

public sealed class ListExamRecordsHandler : IListExamRecordsHandler
{
    private readonly IExamRecordRepository _repository;

    public ListExamRecordsHandler(IExamRecordRepository repository)
    {
        _repository = repository;
    }

    public async Task<ApplicationResult<PageResult<ExamRecordResult>>> HandleAsync(
        ListExamRecordsQuery query, CancellationToken ct = default)
    {
        var filter = query.Filter ?? new ExamRecordFilter();

        if (filter.From.HasValue && filter.To.HasValue && filter.From.Value > filter.To.Value)
        {
            return ApplicationResult<PageResult<ExamRecordResult>>.Fail(
                ApplicationFailureCode.BadRequest,
                "Khoảng ngày tạo hồ sơ không hợp lệ",
                ApplicationValidationErrors.Of("fromDate", "Phải nhỏ hơn hoặc bằng toDate"));
        }

        var page = filter.Page < 1 ? 1 : filter.Page;
        var size = filter.Size < 1 ? 20 : (filter.Size > 200 ? 200 : filter.Size);
        var normalizedFilter = filter with { Page = page, Size = size };

        var result = await _repository.ListAsync(query.DivisionId, normalizedFilter, ct);
        return ApplicationResult<PageResult<ExamRecordResult>>.Success(result);
    }
}
