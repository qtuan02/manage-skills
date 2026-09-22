using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using HealthExam.Application.Common;
using HealthExam.Application.ExamRecords;
using HealthExam.Application.Integrations;
using HealthExam.Domain.Common;
using HealthExam.Domain.ExamRecords;

namespace HealthExam.Application.His;

public sealed record ResetHisFormSectionCommand(string DivisionId, Guid RecordId, int ItemGroupId, string Credential, string TraceId, long ActorId);

public interface IResetHisFormSectionHandler
{
    Task<ApplicationResult<HisRecordSectionResult>> HandleAsync(ResetHisFormSectionCommand command, CancellationToken ct = default);
}

public sealed class ResetHisFormSectionHandler : IResetHisFormSectionHandler
{
    private readonly IExamRecordRepository _records;
    private readonly IUnitOfWork _uow;
    private readonly IHisEmrClient _his;
    private readonly IGetHisFormDefinitionHandler _definitions;
    private readonly IGetHisRecordSectionHandler _getSectionHandler;

    public ResetHisFormSectionHandler(
        IExamRecordRepository records,
        IUnitOfWork uow,
        IHisEmrClient his,
        IGetHisFormDefinitionHandler definitions,
        IGetHisRecordSectionHandler getSectionHandler)
        => (_records, _uow, _his, _definitions, _getSectionHandler) = (records, uow, his, definitions, getSectionHandler);

    public async Task<ApplicationResult<HisRecordSectionResult>> HandleAsync(ResetHisFormSectionCommand command, CancellationToken ct = default)
    {
        var record = await _records.GetWithRegistrationAsync(command.DivisionId, command.RecordId, forUpdate: true, ct);
        if (record == null) return ApplicationResult<HisRecordSectionResult>.Fail(ApplicationFailureCode.NotFound, "Không tìm thấy hồ sơ khám");
        var step = record.SignSteps?.FirstOrDefault(s => s.VariantCode == record.VariantCode && s.ItemGroupID == command.ItemGroupId);
        if (step?.PerformedByEmployeeID is long owner && owner != command.ActorId)
            return ApplicationResult<HisRecordSectionResult>.Fail(ApplicationFailureCode.Forbidden, "Không thể xóa dữ liệu của người khác");

        if (record.SignStatus == ExamRecordSignStatus.Signed)
            return ApplicationResult<HisRecordSectionResult>.Fail(ApplicationFailureCode.InvalidState, "Hồ sơ đã ký kết luận, không thể xóa");

        if (!record.AdmissionID.HasValue || !record.HisEmrDataID.HasValue)
            return ApplicationResult<HisRecordSectionResult>.Fail(ApplicationFailureCode.InvalidState, "Hồ sơ chưa có dữ liệu HIS để xóa");

        var definition = await _definitions.HandleAsync(new GetHisFormDefinitionQuery(
            HisConstants.TargetTemplateCode, command.DivisionId, command.Credential, command.TraceId), ct);
        if (!definition.IsSuccess) return ApplicationResult<HisRecordSectionResult>.Fail(definition.Failure.Code, definition.Failure.Message);

        var read = await _his.SendAsync(HisOperation.ReadFormData,
            new HisRequest($"{HisConstants.RouteREmr}?EMRDataID={record.HisEmrDataID}&TemplateID={definition.Value.TemplateId}&AdmissionID={record.AdmissionID}&IsInherit=false",
                "GET", command.Credential, TraceId: command.TraceId, DivisionId: command.DivisionId), ct);
        if (!read.IsSuccess) return ApplicationResult<HisRecordSectionResult>.Fail(ApplicationFailureCode.HisBadGateway, read.Message);

        var groupItems = GetGroupDefaults(definition.Value.LayoutJson, command.ItemGroupId);
        var details = ReadDetails(read.Value.RawJson);
        foreach (var item in details.Where(x => groupItems.ContainsKey(x.ItemId)))
        {
            var reset = groupItems[item.ItemId];
            item.Value = reset.Value;
            item.Text = reset.Text;
        }
        var payload = new
        {
            EMRDataID = record.HisEmrDataID,
            TemplateID = definition.Value.TemplateId,
            AdmissionID = record.AdmissionID,
            PatientID = record.Patient?.HisPatientID ?? 0,
            PatientCode = record.Patient?.PatientCode ?? "",
            DepartmentID = 1,
            VoucherDate = DateTime.UtcNow,
            IsDraft = true,
            Details = details.Select(x => new { ItemID = x.ItemId, Value = x.Value, Text = x.Text, DataType = x.DataType, ControlStyle = x.ControlStyle })
        };
        var save = await _his.SendAsync(HisOperation.SaveFormData,
            new HisRequest(HisConstants.RouteCueEmr, "POST", command.Credential, JsonSerializer.Serialize(payload), command.TraceId, command.DivisionId), ct);
        if (!save.IsSuccess) return ApplicationResult<HisRecordSectionResult>.Fail(ApplicationFailureCode.HisBadGateway, save.Message);

        if (step != null) record.SignSteps.Remove(step);

        var hasActiveSteps = record.SignSteps != null && record.SignSteps.Any(s =>
            s.Status == ExamRecordSignStepStatus.InProgress ||
            s.Status == ExamRecordSignStepStatus.Snapshot ||
            s.Status == ExamRecordSignStepStatus.Signed);

        if (!hasActiveSteps && record.State == ExamRecordState.InProgress)
        {
            record.State = ExamRecordState.Waiting;
            record.ExamStartedAt = null;
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

    private sealed class Detail
    {
        public Guid ItemId; public string Value = ""; public string Text = ""; public string DataType = "S"; public string ControlStyle = "TXT";
    }

    private static Dictionary<Guid, (string Value, string Text)> GetGroupDefaults(string layoutJson, int groupId)
    {
        var result = new Dictionary<Guid, (string Value, string Text)>();
        using var doc = JsonDocument.Parse(layoutJson ?? "[]");
        foreach (var node in doc.RootElement.EnumerateArray()) Add(node, result, groupId);
        return result;
    }

    private static void Add(JsonElement node, Dictionary<Guid, (string Value, string Text)> result, int groupId)
    {
        var gid = ReadInt(node, "ItemGroupID");
        var id = ReadString(node, "ItemID") ?? ReadString(node, "ItemId");
        if (gid == groupId && Guid.TryParse(id, out var parsed))
        {
            var value = ReadString(node, "DefaultValue") ?? ReadString(node, "Default") ?? ReadString(node, "Value") ?? "";
            var text = ReadString(node, "DefaultText") ?? ReadString(node, "Text") ?? value;
            result[parsed] = (value, text);
        }
        if (node.TryGetProperty("Children", out var children) && children.ValueKind == JsonValueKind.Array)
            foreach (var child in children.EnumerateArray()) Add(child, result, groupId);
    }

    private static List<Detail> ReadDetails(string raw)
    {
        using var doc = JsonDocument.Parse(raw ?? "[]");
        var root = doc.RootElement;
        var source = root.ValueKind == JsonValueKind.Array ? root : root.TryGetProperty("Details", out var d) ? d : root.GetProperty("Data");
        if (source.ValueKind == JsonValueKind.Object && source.TryGetProperty("Details", out var nested)) source = nested;
        var result = new List<Detail>();
        foreach (var item in source.EnumerateArray())
        {
            if (!Guid.TryParse(ReadString(item, "ItemID") ?? ReadString(item, "ItemId"), out var id)) continue;
            result.Add(new Detail { ItemId = id, Value = ReadString(item, "Value") ?? "", Text = ReadString(item, "Text") ?? "", DataType = ReadString(item, "DataType") ?? "S", ControlStyle = ReadString(item, "ControlStyle") ?? "TXT" });
        }
        return result;
    }

    private static string ReadString(JsonElement e, string name) => e.TryGetProperty(name, out var p) ? p.ToString() : null;
    private static int ReadInt(JsonElement e, string name) => int.TryParse(ReadString(e, name), out var value) ? value : 0;
}
