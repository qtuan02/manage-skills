using System;
using System.Threading;
using System.Threading.Tasks;
using HealthExam.Application.Common;
using HealthExam.Application.ExamRecords;

namespace HealthExam.Application.His;

public interface IGetExamRecordHisFormHandler
{
    Task<ApplicationResult<ExamRecordHisFormResult>> HandleAsync(
        GetExamRecordHisFormQuery query, CancellationToken ct = default);
}

public class GetExamRecordHisFormHandler : IGetExamRecordHisFormHandler
{
    private readonly IExamRecordRepository _records;
    private readonly IGetHisFormDefinitionHandler _definitionHandler;

    public GetExamRecordHisFormHandler(
        IExamRecordRepository records,
        IGetHisFormDefinitionHandler definitionHandler)
    {
        _records = records;
        _definitionHandler = definitionHandler;
    }

    public async Task<ApplicationResult<ExamRecordHisFormResult>> HandleAsync(
        GetExamRecordHisFormQuery query, CancellationToken ct = default)
    {
        var record = await _records.GetAsync(query.DivisionId, query.RecordId, forUpdate: false, ct);
        if (record == null)
        {
            return ApplicationResult<ExamRecordHisFormResult>.Fail(
                ApplicationFailureCode.NotFound, "Không tìm thấy hồ sơ khám");
        }

        var defRes = await _definitionHandler.HandleAsync(
            new GetHisFormDefinitionQuery(
                HisConstants.TargetTemplateCode,
                query.DivisionId,
                query.Credential,
                query.TraceId),
            ct);

        if (!defRes.IsSuccess)
        {
            return ApplicationResult<ExamRecordHisFormResult>.Fail(
                defRes.Failure.Code, defRes.Failure.Message, defRes.Failure.Payload);
        }

        return ApplicationResult<ExamRecordHisFormResult>.Success(
            new ExamRecordHisFormResult(record.RecordID, record.AdmissionID, defRes.Value));
    }
}
