#nullable enable

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using HealthExam.Application.Common;
using HealthExam.Application.ExamRecords;
using HealthExam.Application.Integrations;
using HealthExam.Application.Signing;
using HealthExam.Domain.Common;
using HealthExam.Domain.ExamRecords;

namespace HealthExam.Application.His;

public interface IGetHisRecordSectionHandler
{
    Task<ApplicationResult<HisRecordSectionResult>> HandleAsync(
        GetHisRecordSectionQuery query, CancellationToken ct = default);
}

public class GetHisRecordSectionHandler : IGetHisRecordSectionHandler
{
    private readonly IExamRecordRepository _records;
    private readonly IGetHisFormDefinitionHandler _definitionHandler;
    private readonly IHisEmrClient? _client;
    private readonly IUnitOfWork? _uow;
    private readonly IGetIcd10ChoicesHandler? _icd10Handler;
    private readonly ISignStepMapRepository? _signStepMap;

    public GetHisRecordSectionHandler(
        IExamRecordRepository records,
        IGetHisFormDefinitionHandler definitionHandler,
        IHisEmrClient? client = null,
        IUnitOfWork? uow = null,
        IGetIcd10ChoicesHandler? icd10Handler = null,
        ISignStepMapRepository? signStepMap = null)
    {
        _records = records;
        _definitionHandler = definitionHandler;
        _client = client;
        _uow = uow;
        _icd10Handler = icd10Handler;
        _signStepMap = signStepMap;
    }

    public async Task<ApplicationResult<HisRecordSectionResult>> HandleAsync(
        GetHisRecordSectionQuery query, CancellationToken ct = default)
    {
        var record = await _records.GetAsync(query.DivisionId, query.RecordId, forUpdate: true, ct)
            ?? await _records.GetAsync(query.DivisionId, query.RecordId, forUpdate: false, ct);
        if (record == null)
        {
            return ApplicationResult<HisRecordSectionResult>.Fail(
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
            return ApplicationResult<HisRecordSectionResult>.Fail(
                defRes.Failure.Code, defRes.Failure.Message, defRes.Failure.Payload);
        }

        var def = defRes.Value;
        var detailsNode = JsonNode.Parse(def.DetailsJson) as JsonArray;
        var found = false;
        if (detailsNode != null)
        {
            foreach (var item in detailsNode)
            {
                if (item != null &&
                    int.TryParse(item["ItemGroupID"]?.ToString(), out var gid) &&
                    gid == query.ItemGroupId)
                {
                    found = true;
                    break;
                }
            }
        }

        if (!found)
        {
            return ApplicationResult<HisRecordSectionResult>.Fail(
                ApplicationFailureCode.NotFound, $"Không tìm thấy phần khám '{query.ItemGroupId}' trong biểu mẫu KSK");
        }

        var fullLayout = JsonNode.Parse(def.LayoutJson) as JsonArray;
        var sectionLayout = new JsonArray();
        if (fullLayout != null)
        {
            foreach (var node in fullLayout)
            {
                if (node != null &&
                    int.TryParse(node["ItemGroupID"]?.ToString(), out var gid) &&
                    gid == query.ItemGroupId)
                {
                    sectionLayout.Add(node.DeepClone());
                }
            }
        }

        if (_icd10Handler != null)
        {
            await PopulateChoicesAsync(sectionLayout, query, ct);
        }

        if (_client != null && record.AdmissionID.HasValue && record.AdmissionID.Value > 0)
        {
            try
            {
                var path = $"{HisConstants.RouteREmr}?EMRDataID={record.HisEmrDataID ?? Guid.Empty}&TemplateID={def.TemplateId}&AdmissionID={record.AdmissionID.Value}&IsInherit=false";
                var readRes = await _client.SendAsync(
                    HisOperation.ReadFormData,
                    new HisRequest(path, "GET", query.Credential, TraceId: query.TraceId, DivisionId: query.DivisionId),
                    ct);

                if (readRes.IsSuccess && !string.IsNullOrWhiteSpace(readRes.Value.RawJson))
                {
                    using var readDoc = JsonDocument.Parse(readRes.Value.RawJson);
                    var root = readDoc.RootElement;
                    var existingValues = new Dictionary<string, (string Value, string? Text)>(StringComparer.OrdinalIgnoreCase);
                    ExtractDetails(root, existingValues);

                    var returnedEmrIdStr = GetStringProperty(root, "EMRDataID") ?? GetStringProperty(root, "Id");
                    if (Guid.TryParse(returnedEmrIdStr, out var readEmrId) && readEmrId != Guid.Empty)
                    {
                        if (!record.HisEmrDataID.HasValue || record.HisEmrDataID.Value == Guid.Empty)
                        {
                            record.HisEmrDataID = readEmrId;
                            record.HisFormTemplateID = def.TemplateId;
                            record.HisFormSyncStatus = "Synced";
                            record.HisFormSyncError = "";
                            if (_uow != null)
                            {
                                await _uow.SaveChangesAsync(ct);
                            }
                        }
                    }

                    if (existingValues.Count > 0)
                    {
                        foreach (var node in sectionLayout)
                        {
                            if (node != null)
                            {
                                PopulateNodeValues(node, existingValues);
                            }
                        }
                    }
                }
            }
            catch
            {
                // Non-fatal if reading previous EMR data fails
            }
        }

        string? currentStepStatus = null;
        long? currentStepSignedByEmployeeId = null;
        string? currentStepSignedByEmployeeName = null;
        long? currentStepPerformedByEmployeeId = null;
        string? currentStepPerformedByEmployeeName = null;
        DateTime? currentStepSignedAt = null;
        var progressDone = 0;
        var progressTotal = 0;
        IReadOnlyList<SignRoleEmployee> signers = Array.Empty<SignRoleEmployee>();

        if (_signStepMap != null)
        {
            var mapSteps = await _signStepMap.ListAsync(query.DivisionId, record.VariantCode, ct);
            var progress = SigningProgressCalculator.Calculate(mapSteps, record.SignSteps);
            progressDone = progress.Done;
            progressTotal = progress.Total;

            var currentMapStep = mapSteps.FirstOrDefault(s => !s.IsConclusionStep && s.ItemGroupID == query.ItemGroupId);
            if (currentMapStep != null)
            {
                var snapshot = record.SignSteps?.FirstOrDefault(s =>
                    s.VariantCode == record.VariantCode && s.SWStep == currentMapStep.SWStep);
                if (snapshot != null)
                {
                    currentStepStatus = snapshot.Status;
                    currentStepSignedByEmployeeId = snapshot.SignedByEmployeeID;
                    currentStepSignedByEmployeeName = string.IsNullOrWhiteSpace(snapshot.SignedByEmployeeName)
                        ? null : snapshot.SignedByEmployeeName;
                    currentStepSignedAt = snapshot.SignedAt;
                    currentStepPerformedByEmployeeId = snapshot.PerformedByEmployeeID;
                    currentStepPerformedByEmployeeName = string.IsNullOrWhiteSpace(snapshot.PerformedByEmployeeName)
                        ? null : snapshot.PerformedByEmployeeName;
                }

                // Danh sách "Người xác nhận" cho hộp ký (PROJ-2374), chỉ khi bước chưa ký — đã ký thì hộp
                // không mở, khỏi tốn một lượt GetCodeList amount=0 trả cả bệnh viện. HIS lỗi ⇒ rỗng, không
                // hỏng màn khám — cùng chính sách với đọc REMR phía trên; FE báo "không tải được danh sách".
                if (_client != null && snapshot?.Status != ExamRecordSignStepStatus.Signed)
                {
                    var members = await _client.ListSignRoleEmployeesAsync(currentMapStep.SWRoleID,
                        new HisCallContext(query.Credential, query.TraceId, query.DivisionId), ct);
                    if (members.IsSuccess) signers = members.Value;
                }
            }
        }

        return ApplicationResult<HisRecordSectionResult>.Success(
            new HisRecordSectionResult(
                record.RecordID,
                record.AdmissionID,
                query.ItemGroupId,
                sectionLayout.ToJsonString(),
                record.State,
                currentStepStatus,
                currentStepSignedByEmployeeId,
                currentStepSignedAt,
                progressDone,
                progressTotal,
                currentStepSignedByEmployeeName,
                currentStepPerformedByEmployeeId,
                currentStepPerformedByEmployeeName,
                signers));
    }

    private static void PopulateNodeValues(
        JsonNode node,
        IReadOnlyDictionary<string, (string Value, string? Text)> existingValues)
    {
        if (node is JsonObject obj)
        {
            var itemId = obj["ItemID"]?.ToString() ?? obj["ItemId"]?.ToString() ?? obj["NodeID"]?.ToString();
            if (!string.IsNullOrEmpty(itemId) && existingValues.TryGetValue(itemId, out var val))
            {
                obj["Value"] = val.Value;
                if (val.Text != null)
                {
                    obj["Text"] = val.Text;
                }
            }

            if (obj["Children"] is JsonArray children)
            {
                foreach (var child in children)
                {
                    if (child != null)
                    {
                        PopulateNodeValues(child, existingValues);
                    }
                }
            }
        }
    }

    private async Task PopulateChoicesAsync(
        JsonArray layout,
        GetHisRecordSectionQuery query,
        CancellationToken ct)
    {
        JsonArray? icdChoices = null;

        foreach (var node in layout)
        {
            if (node is JsonObject obj)
            {
                await PopulateNodeChoicesAsync(obj);
            }
        }

        async Task PopulateNodeChoicesAsync(JsonObject obj)
        {
            var controlType = obj["ControlType"]?.ToString();
            if (string.Equals(controlType, "CMB", StringComparison.OrdinalIgnoreCase))
            {
                var existingChoices = obj["Choices"] as JsonArray ?? obj["Options"] as JsonArray;
                if (existingChoices == null || existingChoices.Count == 0)
                {
                    if (icdChoices == null && _icd10Handler != null)
                    {
                        var icdRes = await _icd10Handler.HandleAsync(
                            new GetIcd10ChoicesQuery(query.DivisionId, "", 20, query.Credential, query.TraceId), ct);
                        if (icdRes.IsSuccess)
                        {
                            icdChoices = new JsonArray();
                            foreach (var c in icdRes.Value)
                            {
                                var choiceObj = new JsonObject
                                {
                                    ["Code"] = c.Code,
                                    ["Label"] = c.Label
                                };
                                if (!string.IsNullOrWhiteSpace(c.CodeName)) choiceObj["CodeName"] = c.CodeName;
                                if (!string.IsNullOrWhiteSpace(c.DisplayName)) choiceObj["DisplayName"] = c.DisplayName;
                                icdChoices.Add(choiceObj);
                            }
                        }
                    }

                    if (icdChoices != null)
                    {
                        obj["Choices"] = icdChoices.DeepClone();
                    }
                }
            }

            if (obj["Children"] is JsonArray children)
            {
                foreach (var child in children)
                {
                    if (child is JsonObject childObj)
                    {
                        await PopulateNodeChoicesAsync(childObj);
                    }
                }
            }
        }
    }

    private static void ExtractDetails(
        JsonElement root,
        Dictionary<string, (string Value, string? Text)> existingValues)
    {
        IEnumerable<JsonElement>? detailElements = null;
        if (root.ValueKind == JsonValueKind.Array)
        {
            detailElements = root.EnumerateArray();
        }
        else if (root.ValueKind == JsonValueKind.Object)
        {
            if (root.TryGetProperty("Details", out var details) && details.ValueKind == JsonValueKind.Array)
                detailElements = details.EnumerateArray();
            else if (root.TryGetProperty("Data", out var data))
            {
                if (data.ValueKind == JsonValueKind.Array)
                    detailElements = data.EnumerateArray();
                else if (data.ValueKind == JsonValueKind.Object && data.TryGetProperty("Details", out var innerDetails) && innerDetails.ValueKind == JsonValueKind.Array)
                    detailElements = innerDetails.EnumerateArray();
            }
        }

        if (detailElements == null) return;

        foreach (var d in detailElements)
        {
            var itemIdStr = GetStringProperty(d, "ItemID") ?? GetStringProperty(d, "ItemId") ?? GetStringProperty(d, "NodeID");
            if (!string.IsNullOrEmpty(itemIdStr))
            {
                var val = GetStringProperty(d, "Value") ?? "";
                var text = GetStringProperty(d, "Text");
                existingValues[itemIdStr] = (val, text);
            }
        }
    }

    private static string? GetStringProperty(JsonElement element, string propName)
    {
        if (element.TryGetProperty(propName, out var p))
        {
            if (p.ValueKind == JsonValueKind.String) return p.GetString();
            return p.ToString();
        }
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var item in element.EnumerateObject())
            {
                if (string.Equals(item.Name, propName, StringComparison.OrdinalIgnoreCase))
                {
                    if (item.Value.ValueKind == JsonValueKind.String) return item.Value.GetString();
                    return item.Value.ToString();
                }
            }
        }
        return null;
    }
}
