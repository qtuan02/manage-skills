#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using HealthExam.Application.Common;
using HealthExam.Application.ExamRecords;
using HealthExam.Application.Integrations;
using HealthExam.Application.Signing;
using HealthExam.Domain.Common;
using HealthExam.Domain.ExamRecords;

namespace HealthExam.Application.His;

public interface ISaveHisFormSectionHandler
{
    Task<ApplicationResult<HisRecordSectionResult>> HandleAsync(
        SaveHisFormSectionCommand command, CancellationToken ct = default);
}

public sealed class SaveHisFormSectionHandler : ISaveHisFormSectionHandler
{
    private readonly IExamRecordRepository _repo;
    private readonly IHisEmrClient _client;
    private readonly IGetHisFormDefinitionHandler _defHandler;
    private readonly IEnsureHisAdmission _ensureAdmission;
    private readonly IGetHisRecordSectionHandler _getSectionHandler;
    private readonly IUnitOfWork _uow;
    private readonly ISignStepMapRepository? _signStepMap;

    public SaveHisFormSectionHandler(
        IExamRecordRepository repo,
        IHisEmrClient client,
        IGetHisFormDefinitionHandler defHandler,
        IEnsureHisAdmission ensureAdmission,
        IGetHisRecordSectionHandler getSectionHandler,
        IUnitOfWork uow,
        ISignStepMapRepository? signStepMap = null)
    {
        _repo = repo;
        _client = client;
        _defHandler = defHandler;
        _ensureAdmission = ensureAdmission;
        _getSectionHandler = getSectionHandler;
        _uow = uow;
        _signStepMap = signStepMap;
    }

    public async Task<ApplicationResult<HisRecordSectionResult>> HandleAsync(
        SaveHisFormSectionCommand command, CancellationToken ct = default)
    {
        var record = await _repo.GetWithRegistrationAsync(command.DivisionId, command.RecordId, forUpdate: true, ct)
            ?? await _repo.GetAsync(command.DivisionId, command.RecordId, forUpdate: true, ct);

        if (record == null)
        {
            return ApplicationResult<HisRecordSectionResult>.Fail(
                ApplicationFailureCode.NotFound, "Không tìm thấy hồ sơ khám");
        }

        // Spec §2/§5.2: hồ sơ Signed bất biến; mục khám bị khóa ⇔ có snapshot ký của bộ biểu mẫu
        // hiện tại. Chặn ở đây, TRƯỚC mọi lượt gọi HIS, để mục đã khóa không sinh lưu lượng HIS.
        if (record.SignStatus == ExamRecordSignStatus.Signed)
        {
            return ApplicationResult<HisRecordSectionResult>.Fail(
                ApplicationFailureCode.InvalidState, "Hồ sơ đã ký kết luận, không sửa được");
        }

        var actorId = long.TryParse(command.ActorId, out var parsedActorId) ? parsedActorId : 0L;
        var existingStepForActor = record.SignSteps?.FirstOrDefault(s =>
            s.VariantCode == record.VariantCode && s.ItemGroupID == command.ItemGroupId);
        if (existingStepForActor?.PerformedByEmployeeID is long performedBy && performedBy != actorId)
        {
            return ApplicationResult<HisRecordSectionResult>.Fail(
                ApplicationFailureCode.Forbidden, "Chỉ người thực hiện bước này mới được chỉnh sửa");
        }

        if (record.SignSteps?.Any(s => s.VariantCode == record.VariantCode && s.ItemGroupID == command.ItemGroupId && s.Status != ExamRecordSignStepStatus.InProgress) == true)
        {
            return ApplicationResult<HisRecordSectionResult>.Fail(
                ApplicationFailureCode.InvalidState, "Mục khám đã ký số, hủy ký trước khi sửa");
        }

        var defRes = await _defHandler.HandleAsync(
            new GetHisFormDefinitionQuery(
                HisConstants.TargetTemplateCode,
                command.DivisionId,
                command.Credential,
                command.TraceId),
            ct);

        if (!defRes.IsSuccess)
        {
            return ApplicationResult<HisRecordSectionResult>.Fail(
                defRes.Failure.Code, defRes.Failure.Message, defRes.Failure.Payload);
        }

        var hisDef = defRes.Value;
        var detailsFound = false;
        try
        {
            using var detailsDoc = JsonDocument.Parse(hisDef.DetailsJson ?? "[]");
            if (detailsDoc.RootElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var d in detailsDoc.RootElement.EnumerateArray())
                {
                    if (d.TryGetProperty("ItemGroupID", out var gidProp) &&
                        gidProp.TryGetInt32(out var gid) &&
                        gid == command.ItemGroupId)
                    {
                        detailsFound = true;
                        break;
                    }
                }
            }
        }
        catch
        {
        }

        if (!detailsFound)
        {
            return ApplicationResult<HisRecordSectionResult>.Fail(
                ApplicationFailureCode.NotFound, $"Không tìm thấy phần khám '{command.ItemGroupId}' trong biểu mẫu KSK");
        }

        if (!record.AdmissionID.HasValue || record.AdmissionID.Value <= 0)
        {
            var actorIdNum = actorId;
            var admRes = await _ensureAdmission.HandleAsync(new EnsureHisAdmissionCommand(
                command.DivisionId,
                command.RecordId,
                new HisCallContext(command.Credential, command.TraceId, command.DivisionId),
                actorIdNum,
                command.ActorKind), ct);

            if (!admRes.IsSuccess)
            {
                return ApplicationResult<HisRecordSectionResult>.Fail(
                    admRes.Failure.Code, admRes.Failure.Message, admRes.Failure.Payload);
            }

            record.AdmissionID = admRes.Value;
        }

        var existingDetails = new Dictionary<Guid, (string Value, string? Text, string DataType, string ControlStyle)>();
        Guid? existingEmrDataId = record.HisEmrDataID;

        try
        {
            var path = $"{HisConstants.RouteREmr}?EMRDataID={record.HisEmrDataID ?? Guid.Empty}&TemplateID={hisDef.TemplateId}&AdmissionID={record.AdmissionID.Value}&IsInherit=false";
            var readRes = await _client.SendAsync(
                HisOperation.ReadFormData,
                new HisRequest(path, "GET", command.Credential, TraceId: command.TraceId, DivisionId: command.DivisionId),
                ct);

            if (readRes.IsSuccess && !string.IsNullOrWhiteSpace(readRes.Value.RawJson))
            {
                using var readDoc = JsonDocument.Parse(readRes.Value.RawJson);
                var root = readDoc.RootElement;
                ExtractDetailsWithMeta(root, existingDetails);

                string? returnedEmrIdStr = GetStringProperty(root, "EMRDataID") ??
                                          GetStringProperty(root, "Id");
                if (Guid.TryParse(returnedEmrIdStr, out var readEmrId) && readEmrId != Guid.Empty)
                {
                    existingEmrDataId = readEmrId;
                }
            }
        }
        catch
        {
        }

        var sectionLeaves = new Dictionary<Guid, (string DataType, string ControlStyle)>();
        var choiceLabels = new Dictionary<Guid, Dictionary<string, string>>();

        try
        {
            using var layoutDoc = JsonDocument.Parse(hisDef.LayoutJson ?? "[]");
            if (layoutDoc.RootElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var n in layoutDoc.RootElement.EnumerateArray())
                {
                    var idStr = GetStringProperty(n, "ItemID") ?? GetStringProperty(n, "ItemId");
                    if (Guid.TryParse(idStr, out var id) && id != Guid.Empty)
                    {
                        var gid = GetIntProperty(n, "ItemGroupID") ?? 0;
                        if (gid == command.ItemGroupId)
                        {
                            var controlType = GetStringProperty(n, "ControlType") ?? "TXT";
                            var dataType = GetStringProperty(n, "DataType") ?? "S";
                            sectionLeaves[id] = (dataType, controlType);
                        }

                        var hasChoices = (n.TryGetProperty("Choices", out var choicesProp) && choicesProp.ValueKind == JsonValueKind.Array) ||
                                         (n.TryGetProperty("Options", out choicesProp) && choicesProp.ValueKind == JsonValueKind.Array);
                        if (hasChoices)
                        {
                            var labels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                            foreach (var c in choicesProp.EnumerateArray())
                            {
                                var cCode = GetStringProperty(c, "Code") ?? GetStringProperty(c, "Value");
                                var cLabel = GetStringProperty(c, "DisplayName") ??
                                             GetStringProperty(c, "CodeName") ??
                                             GetStringProperty(c, "Label") ??
                                             GetStringProperty(c, "Name");
                                if (!string.IsNullOrWhiteSpace(cCode) && !string.IsNullOrWhiteSpace(cLabel))
                                {
                                    var prefix = cCode + " - ";
                                    if (cLabel.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                                    {
                                        cLabel = cLabel[prefix.Length..].Trim();
                                    }
                                    labels[cCode] = cLabel;
                                }
                            }
                            if (labels.Count > 0)
                            {
                                choiceLabels[id] = labels;
                            }
                        }
                    }
                }
            }
        }
        catch
        {
        }

        if (command.Request.Fields != null)
        {
            foreach (var field in command.Request.Fields)
            {
                var dataType = "S";
                var controlStyle = "TXT";
                if (sectionLeaves.TryGetValue(field.ItemId, out var meta))
                {
                    dataType = meta.DataType;
                    controlStyle = meta.ControlStyle;
                }

                var text = field.Text;
                var val = field.Value ?? "";
                if (choiceLabels.TryGetValue(field.ItemId, out var labels) && labels.TryGetValue(val.Trim(), out var choiceText))
                {
                    text = choiceText;
                }
                else if (string.IsNullOrWhiteSpace(text))
                {
                    text = val;
                }

                existingDetails[field.ItemId] = (val, text, dataType, controlStyle);
            }
        }

        foreach (var key in existingDetails.Keys.ToList())
        {
            var item = existingDetails[key];
            var value = item.Value?.Trim();
            if (!string.IsNullOrWhiteSpace(value) &&
                choiceLabels.TryGetValue(key, out var labels) &&
                labels.TryGetValue(value, out var choiceText))
            {
                if (string.IsNullOrWhiteSpace(item.Text) ||
                    string.Equals(item.Text.Trim(), value, StringComparison.OrdinalIgnoreCase))
                {
                    existingDetails[key] = (item.Value ?? "", choiceText, item.DataType, item.ControlStyle);
                }
            }
        }

        var detailsToSave = existingDetails.Select(kv => new
        {
            ItemID = kv.Key,
            Value = kv.Value.Value,
            Text = string.IsNullOrWhiteSpace(kv.Value.Text) ? kv.Value.Value : kv.Value.Text,
            DataType = kv.Value.DataType,
            ControlStyle = kv.Value.ControlStyle
        }).ToList();

        var cuemrPayload = new
        {
            EMRDataID = existingEmrDataId ?? Guid.Empty,
            TemplateID = hisDef.TemplateId,
            AdmissionID = record.AdmissionID.Value,
            PatientID = record.Patient?.HisPatientID ?? 0,
            PatientCode = record.Patient?.PatientCode ?? "",
            DepartmentID = 1,
            VoucherDate = DateTime.UtcNow,
            IsDraft = command.Request.IsDraft,
            Details = detailsToSave
        };

        var saveRes = await _client.SendAsync(
            HisOperation.SaveFormData,
            new HisRequest(
                HisConstants.RouteCueEmr,
                "POST",
                command.Credential,
                JsonSerializer.Serialize(cuemrPayload),
                TraceId: command.TraceId,
                DivisionId: command.DivisionId),
            ct);

        if (!saveRes.IsSuccess)
        {
            record.HisFormSyncStatus = "Failed";
            var msg = !string.IsNullOrWhiteSpace(saveRes.Message) ? saveRes.Message : "Lưu dữ liệu biểu mẫu lên HIS thất bại";
            record.HisFormSyncError = msg.Length > 1000 ? msg[..1000] : msg;
            await _uow.SaveChangesAsync(CancellationToken.None);

            return HisOutcomeMapper.ToApplicationResult<HisRecordSectionResult>(
                HisClientResult<HisRecordSectionResult>.Fail(saveRes.Outcome, saveRes.Message));
        }

        Guid savedEmrDataId = Guid.Empty;
        var rawGuid = saveRes.Value.RawJson?.Trim('"', ' ', '\r', '\n');
        if (Guid.TryParse(rawGuid, out var directGuid) && directGuid != Guid.Empty)
        {
            savedEmrDataId = directGuid;
        }
        else if (!string.IsNullOrWhiteSpace(saveRes.Value.RawJson))
        {
            try
            {
                using var saveDoc = JsonDocument.Parse(saveRes.Value.RawJson);
                var sRoot = saveDoc.RootElement;
                string? guidStr = null;
                if (sRoot.ValueKind == JsonValueKind.String)
                {
                    guidStr = sRoot.GetString();
                }
                else if (sRoot.ValueKind == JsonValueKind.Object)
                {
                    guidStr = GetStringProperty(sRoot, "Id") ??
                              GetStringProperty(sRoot, "EMRDataID") ??
                              GetStringProperty(sRoot, "Data");
                }
                Guid.TryParse(guidStr, out savedEmrDataId);
            }
            catch
            {
            }
        }

        if (savedEmrDataId == Guid.Empty && existingEmrDataId.HasValue && existingEmrDataId.Value != Guid.Empty)
        {
            savedEmrDataId = existingEmrDataId.Value;
        }

        if (savedEmrDataId != Guid.Empty)
        {
            record.HisEmrDataID = savedEmrDataId;
        }
        record.HisFormTemplateID = hisDef.TemplateId;
        record.HisFormSyncStatus = "Synced";
        record.HisFormSyncError = "";

        if (_signStepMap != null)
        {
            var mapSteps = await _signStepMap.ListAsync(command.DivisionId, record.VariantCode, ct);
            var matchedStep = mapSteps.FirstOrDefault(s => !s.IsConclusionStep && s.ItemGroupID == command.ItemGroupId);
            if (matchedStep != null)
            {
                if (record.State == ExamRecordState.Waiting)
                {
                    record.BeginExam(DateTime.UtcNow);
                }

                record.SignSteps ??= new List<ExamRecordSignStep>();
                var existingStep = record.SignSteps.FirstOrDefault(s =>
                    s.VariantCode == record.VariantCode && s.SWStep == matchedStep.SWStep);
                if (existingStep == null)
                {
                    record.SignSteps.Add(new ExamRecordSignStep
                    {
                        DivisionID = command.DivisionId,
                        RecordID = record.RecordID,
                        VariantCode = record.VariantCode,
                        ItemGroupID = matchedStep.ItemGroupID,
                        SWStep = matchedStep.SWStep,
                        StepName = matchedStep.StepName,
                        SWRoleID = matchedStep.SWRoleID,
                        Status = ExamRecordSignStepStatus.InProgress,
                        PerformedByEmployeeID = actorId > 0 ? actorId : null,
                        PerformedByEmployeeName = command.ActorName ?? "",
                        CreatedDate = DateTime.UtcNow,
                        ModifiedDate = DateTime.UtcNow
                    });
                }
                else if (existingStep.Status != ExamRecordSignStepStatus.Signed)
                {
                    existingStep.Status = ExamRecordSignStepStatus.InProgress;
                    existingStep.PerformedByEmployeeID = actorId > 0 ? actorId : null;
                    existingStep.PerformedByEmployeeName = command.ActorName ?? "";
                    existingStep.ModifiedDate = DateTime.UtcNow;
                }
            }
        }

        await _uow.SaveChangesAsync(ct);

        return await _getSectionHandler.HandleAsync(
            new GetHisRecordSectionQuery(
                command.DivisionId,
                command.RecordId,
                command.ItemGroupId,
                command.Credential,
                command.TraceId),
            ct);
    }

    private static string? GetStringProperty(JsonElement element, string propName)
    {
        if (element.TryGetProperty(propName, out var p))
        {
            if (p.ValueKind == JsonValueKind.String) return p.GetString();
            return p.ToString();
        }
        foreach (var item in element.EnumerateObject())
        {
            if (string.Equals(item.Name, propName, StringComparison.OrdinalIgnoreCase))
            {
                if (item.Value.ValueKind == JsonValueKind.String) return item.Value.GetString();
                return item.Value.ToString();
            }
        }
        return null;
    }

    private static int? GetIntProperty(JsonElement element, string propName)
    {
        if (element.TryGetProperty(propName, out var p) && p.TryGetInt32(out var val)) return val;
        foreach (var item in element.EnumerateObject())
        {
            if (string.Equals(item.Name, propName, StringComparison.OrdinalIgnoreCase) && item.Value.TryGetInt32(out var iVal))
            {
                return iVal;
            }
        }
        return null;
    }

    private static void ExtractDetailsWithMeta(
        JsonElement root,
        Dictionary<Guid, (string Value, string? Text, string DataType, string ControlStyle)> existingDetails)
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
            var itemIdStr = GetStringProperty(d, "ItemID") ?? GetStringProperty(d, "ItemId");
            if (!string.IsNullOrEmpty(itemIdStr) && Guid.TryParse(itemIdStr, out var itemId) && itemId != Guid.Empty)
            {
                var val = GetStringProperty(d, "Value") ?? "";
                var text = GetStringProperty(d, "Text");
                var dataType = GetStringProperty(d, "DataType") ?? "S";
                var controlStyle = GetStringProperty(d, "ControlStyle") ?? "TXT";
                existingDetails[itemId] = (val, text, dataType, controlStyle);
            }
        }
    }
}
