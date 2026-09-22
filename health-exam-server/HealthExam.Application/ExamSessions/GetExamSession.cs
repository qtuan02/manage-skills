using System;
using System.Threading;
using System.Threading.Tasks;
using HealthExam.Application.Common;
using HealthExam.Domain.ExamSessions;

namespace HealthExam.Application.ExamSessions;

public interface IGetExamSessionHandler
{
    Task<ApplicationResult<ExamSessionResult>> HandleAsync(
        GetExamSessionQuery query, CancellationToken ct = default);
}

public sealed class GetExamSessionHandler : IGetExamSessionHandler
{
    private readonly IExamSessionRepository _repository;

    public GetExamSessionHandler(IExamSessionRepository repository)
    {
        _repository = repository;
    }

    public async Task<ApplicationResult<ExamSessionResult>> HandleAsync(
        GetExamSessionQuery query, CancellationToken ct = default)
    {
        var entity = await _repository.GetAsync(query.DivisionId, query.SessionId, forUpdate: false, ct);
        if (entity == null)
        {
            return ApplicationResult<ExamSessionResult>.Fail(
                ApplicationFailureCode.NotFound, "Không tìm thấy đợt khám");
        }

        var recordCount = await _repository.GetRecordCountAsync(query.DivisionId, query.SessionId, ct);

        var result = MapToResult(entity, recordCount);
        return ApplicationResult<ExamSessionResult>.Success(result);
    }

    public static ExamSessionResult MapToResult(ExamSession entity, int recordCount)
    {
        return new ExamSessionResult(
            entity.SessionID,
            entity.SessionCode,
            entity.SessionName,
            entity.OrganizationID,
            entity.OrganizationName,
            entity.ContractNo,
            entity.ContractDate,
            entity.ExamDate,
            entity.ExamDateTo,
            entity.ExamPlace,
            entity.PackageID,
            entity.PackageName,
            entity.VariantCode,
            entity.State,
            ExamSessionStateNames.Of(entity.State),
            entity.ExpectedCount,
            recordCount,
            entity.Note,
            entity.IsActive);
    }
}
