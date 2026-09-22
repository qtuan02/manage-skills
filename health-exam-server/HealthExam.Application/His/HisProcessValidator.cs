using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using HealthExam.Application.Common;
using HealthExam.Application.ExamRecords;
using HealthExam.Application.Integrations;

namespace HealthExam.Application.His;

public interface IHisProcessValidator
{
    Task<ApplicationResult<HisJsonDocument>> ValidateAndGetOwnedProcessAsync(
        string divisionId,
        Guid recordId,
        Guid processId,
        string credential,
        string traceId,
        CancellationToken ct = default);

    ApplicationResult<bool> ValidateSection(
        HisJsonDocument processDetail,
        string sectionKey);
}

public class HisProcessValidator : IHisProcessValidator
{
    private readonly IExamRecordRepository _records;
    private readonly IGetHisFormDefinitionHandler _definitionHandler;
    private readonly IHisEmrClient _client;

    public HisProcessValidator(
        IExamRecordRepository records,
        IGetHisFormDefinitionHandler definitionHandler,
        IHisEmrClient client)
    {
        _records = records;
        _definitionHandler = definitionHandler;
        _client = client;
    }

    public async Task<ApplicationResult<HisJsonDocument>> ValidateAndGetOwnedProcessAsync(
        string divisionId,
        Guid recordId,
        Guid processId,
        string credential,
        string traceId,
        CancellationToken ct = default)
    {
        var record = await _records.GetAsync(divisionId, recordId, forUpdate: false, ct);
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
                divisionId,
                credential,
                traceId),
            ct);

        if (!defRes.IsSuccess)
        {
            return ApplicationResult<HisJsonDocument>.Fail(
                defRes.Failure.Code, defRes.Failure.Message, defRes.Failure.Payload);
        }

        var listRes = await _client.SendAsync(
            HisOperation.ListProcesses,
            new HisRequest(
                $"{HisConstants.RouteRMedicalProcess}?admissionID={record.AdmissionID.Value}",
                "GET",
                credential,
                TraceId: traceId,
                DivisionId: divisionId),
            ct);

        if (!listRes.IsSuccess)
        {
            return HisOutcomeMapper.ToApplicationResult(listRes);
        }

        var isOwned = false;
        using (var doc = JsonDocument.Parse(listRes.Value.RawJson ?? "[]"))
        {
            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var p in doc.RootElement.EnumerateArray())
                {
                    string pidStr = null;
                    if (p.TryGetProperty("ID", out var pUpperId)) pidStr = pUpperId.GetString();
                    else if (p.TryGetProperty("Id", out var pId)) pidStr = pId.GetString();
                    else if (p.TryGetProperty("MedicalProcessID", out var pPid)) pidStr = pPid.GetString();
                    else if (p.TryGetProperty("MedicalProcessId", out var pPid2)) pidStr = pPid2.GetString();

                    if (!Guid.TryParse(pidStr, out var pid) || pid != processId)
                        continue;

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
                        isOwned = true;
                        break;
                    }
                }
            }
        }

        if (!isOwned)
        {
            return ApplicationResult<HisJsonDocument>.Fail(
                ApplicationFailureCode.NotFound,
                "Quy trình khám không thuộc hồ sơ này hoặc không đúng biểu mẫu KSK");
        }

        var detailRes = await _client.SendAsync(
            HisOperation.GetProcess,
            new HisRequest(
                $"{HisConstants.RouteRMedicalProcessById}?id={processId}",
                "GET",
                credential,
                TraceId: traceId,
                DivisionId: divisionId),
            ct);

        return HisOutcomeMapper.ToApplicationResult(detailRes);
    }

    public ApplicationResult<bool> ValidateSection(
        HisJsonDocument processDetail,
        string sectionKey)
    {
        if (string.IsNullOrWhiteSpace(sectionKey))
        {
            return ApplicationResult<bool>.Fail(
                ApplicationFailureCode.BadRequest,
                "Chưa chỉ định chuyên khoa/phần khám",
                ApplicationValidationErrors.Of("sectionKey", "Bắt buộc nhập"));
        }

        using var doc = JsonDocument.Parse(processDetail?.RawJson ?? "{}");
        var root = doc.RootElement;

        JsonElement detailsElement = default;
        var found = false;

        if (root.TryGetProperty("MedicalProcessDetails", out var pD1) && pD1.ValueKind == JsonValueKind.Array)
        {
            detailsElement = pD1;
            found = true;
        }
        else if (root.TryGetProperty("Details", out var pD2) && pD2.ValueKind == JsonValueKind.Array)
        {
            detailsElement = pD2;
            found = true;
        }
        else if (root.TryGetProperty("M02_MedicalProcessDetail", out var pD3) && pD3.ValueKind == JsonValueKind.Array)
        {
            detailsElement = pD3;
            found = true;
        }
        else if (root.ValueKind == JsonValueKind.Array)
        {
            detailsElement = root;
            found = true;
        }

        var matched = false;
        if (found)
        {
            foreach (var row in detailsElement.EnumerateArray())
            {
                if (row.TryGetProperty("ItemGroupID", out var pGroup) &&
                    string.Equals(pGroup.GetString(), sectionKey, StringComparison.Ordinal))
                {
                    matched = true;
                    break;
                }
            }
        }

        if (!matched)
        {
            return ApplicationResult<bool>.Fail(
                ApplicationFailureCode.NotFound,
                $"Không tìm thấy phần khám '{sectionKey}' trong quy trình");
        }

        return ApplicationResult<bool>.Success(true);
    }
}
