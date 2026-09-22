using System.Threading;
using System.Threading.Tasks;
using HealthExam.Application.Common;
using HealthExam.Application.ExamSessions;

namespace HealthExam.Application.ExamRecords;

public interface IGetExamSessionProgressHandler
{
    Task<ApplicationResult<ExamSessionProgressResult>> HandleAsync(
        GetExamSessionProgressQuery query, CancellationToken ct = default);
}

public sealed class GetExamSessionProgressHandler : IGetExamSessionProgressHandler
{
    private readonly IExamRecordRepository _recordRepository;
    private readonly IExamSessionRepository _sessionRepository;

    public GetExamSessionProgressHandler(
        IExamRecordRepository recordRepository,
        IExamSessionRepository sessionRepository)
    {
        _recordRepository = recordRepository;
        _sessionRepository = sessionRepository;
    }

    public async Task<ApplicationResult<ExamSessionProgressResult>> HandleAsync(
        GetExamSessionProgressQuery query, CancellationToken ct = default)
    {
        var session = await _sessionRepository.GetAsync(query.DivisionId, query.SessionId, forUpdate: false, ct);
        if (session == null)
        {
            return ApplicationResult<ExamSessionProgressResult>.Fail(
                ApplicationFailureCode.NotFound, "Không tìm thấy đợt khám");
        }

        var progress = await _recordRepository.GetProgressAsync(query.DivisionId, query.SessionId, ct);
        if (progress == null)
        {
            return ApplicationResult<ExamSessionProgressResult>.Fail(
                ApplicationFailureCode.NotFound, "Không tìm thấy đợt khám");
        }

        return ApplicationResult<ExamSessionProgressResult>.Success(progress);
    }
}
