using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using HealthExam.Application.Common;
using HealthExam.Application.ExamRecords;
using HealthExam.Application.Integrations;

namespace HealthExam.Application.His;

public interface IListHisProcessesHandler
{
    Task<ApplicationResult<HisJsonDocument>> HandleAsync(
        ListHisProcessesQuery query, CancellationToken ct = default);
}

public class ListHisProcessesHandler : IListHisProcessesHandler
{
    private readonly IExamRecordRepository _records;
    private readonly IGetHisFormDefinitionHandler _definitionHandler;
    private readonly IHisEmrClient _client;

    public ListHisProcessesHandler(
        IExamRecordRepository records,
        IGetHisFormDefinitionHandler definitionHandler,
        IHisEmrClient client)
    {
        _records = records;
        _definitionHandler = definitionHandler;
        _client = client;
    }

    public async Task<ApplicationResult<HisJsonDocument>> HandleAsync(
        ListHisProcessesQuery query, CancellationToken ct = default)
    {
        var record = await _records.GetAsync(query.DivisionId, query.RecordId, forUpdate: false, ct);
        if (record == null)
        {
            return ApplicationResult<HisJsonDocument>.Fail(
                ApplicationFailureCode.NotFound, "Không tìm thấy hồ sơ khám");
        }

        if (!record.AdmissionID.HasValue || record.AdmissionID.Value <= 0)
        {
            return ApplicationResult<HisJsonDocument>.Fail(
                ApplicationFailureCode.BadRequest,
                "Hồ sơ chưa được liên kết với lượt tiếp nhận HIS",
                ApplicationValidationErrors.Of("AdmissionID", "Chưa có mã lượt tiếp nhận HIS hợp lệ"));
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
            return ApplicationResult<HisJsonDocument>.Fail(
                defRes.Failure.Code, defRes.Failure.Message, defRes.Failure.Payload);
        }

        var res = await _client.SendAsync(
            HisOperation.ListProcesses,
            new HisRequest(
                $"{HisConstants.RouteRMedicalProcess}?admissionID={record.AdmissionID.Value}",
                "GET",
                query.Credential,
                TraceId: query.TraceId,
                DivisionId: query.DivisionId),
            ct);

        if (!res.IsSuccess)
        {
            return HisOutcomeMapper.ToApplicationResult(res);
        }

        using var doc = JsonDocument.Parse(res.Value.RawJson ?? "[]");
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
        {
            return ApplicationResult<HisJsonDocument>.Success(new HisJsonDocument("[]"));
        }

        var filtered = new List<JsonElement>();
        foreach (var p in doc.RootElement.EnumerateArray())
        {
            var tMatch = false;
            string tStr = null;
            if (p.TryGetProperty("TemplateID", out var pTid)) tStr = pTid.GetString();
            else if (p.TryGetProperty("TemplateId", out var pTid2)) tStr = pTid2.GetString();

            if (Guid.TryParse(tStr, out var tid) && tid == defRes.Value.TemplateId)
                tMatch = true;

            var dMatch = false;
            int? did = null;
            if (p.TryGetProperty("FileDocTypeID", out var pDid))
            {
                if (pDid.ValueKind == JsonValueKind.Number) did = pDid.GetInt32();
                else if (int.TryParse(pDid.GetString(), out var parsed)) did = parsed;
            }
            else if (p.TryGetProperty("FileDocTypeId", out var pDid2))
            {
                if (pDid2.ValueKind == JsonValueKind.Number) did = pDid2.GetInt32();
                else if (int.TryParse(pDid2.GetString(), out var parsed2)) did = parsed2;
            }

            if (did.HasValue && did.Value == defRes.Value.FileDocTypeId && defRes.Value.FileDocTypeId > 0)
                dMatch = true;

            if (tMatch || dMatch)
            {
                filtered.Add(p.Clone());
            }
        }

        var json = JsonSerializer.Serialize(filtered);
        return ApplicationResult<HisJsonDocument>.Success(new HisJsonDocument(json));
    }
}
