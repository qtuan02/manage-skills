using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using HealthExam.Application.Common;
using HealthExam.Application.His;
using HealthExam.Application.Integrations;

namespace HealthExam.Application.RegistrationForms;

public interface IGetRegistrationFormHandler
{
    Task<ApplicationResult<RegistrationRecordForm>> HandleAsync(
        GetRegistrationFormQuery query, CancellationToken ct = default);
}

public class GetRegistrationFormHandler : IGetRegistrationFormHandler
{
    private readonly IRegistrationFormRepository _repo;
    private readonly IHisEmrClient _client;
    private readonly IHisFormDefinitionCache _cache;
    private readonly IUnitOfWork _uow;

    public GetRegistrationFormHandler(
        IRegistrationFormRepository repo,
        IHisEmrClient client,
        IHisFormDefinitionCache cache,
        IUnitOfWork uow)
    {
        _repo = repo;
        _client = client;
        _cache = cache;
        _uow = uow;
    }

    public async Task<ApplicationResult<RegistrationRecordForm>> HandleAsync(
        GetRegistrationFormQuery query, CancellationToken ct = default)
    {
        var record = await _repo.GetRecordAsync(query.DivisionId, query.RecordId, forUpdate: true, ct);
        if (record == null)
        {
            return ApplicationResult<RegistrationRecordForm>.Fail(
                ApplicationFailureCode.NotFound, "Không tìm thấy hồ sơ khám");
        }

        if (string.IsNullOrWhiteSpace(record.VariantCode))
        {
            return ApplicationResult<RegistrationRecordForm>.Fail(
                ApplicationFailureCode.InvalidState, "Hồ sơ chưa được gán nhóm khám");
        }

        var mapping = await _repo.GetActiveMappingAsync(query.DivisionId, record.VariantCode, ct);
        if (mapping == null)
        {
            return ApplicationResult<RegistrationRecordForm>.Fail(
                ApplicationFailureCode.NotFound, $"Nhóm khám {record.VariantCode} chưa được cấu hình biểu mẫu hoặc đang bị vô hiệu hoá");
        }

        var historySection = mapping.Sections.SingleOrDefault(s => s.SectionKind == "HISTORY");
        var extraInfoSection = mapping.Sections.SingleOrDefault(s => s.SectionKind == "EXTRA_INFO");

        if (historySection == null || extraInfoSection == null)
        {
            return ApplicationResult<RegistrationRecordForm>.Fail(
                ApplicationFailureCode.InvalidState, "Cấu hình biểu mẫu thiếu phân đoạn HISTORY hoặc EXTRA_INFO");
        }

        HisFormDefinitionResult hisDef;
        try
        {
            hisDef = await HisDefinitionResolver.GetDefinitionAsync(
                _client, _cache, query.DivisionId, mapping.TemplateCode, query.Credential, query.TraceId, ct);
        }
        catch (HisApplicationException ex)
        {
            return ApplicationResult<RegistrationRecordForm>.Fail(ex.Code, ex.Message, ex.Payload);
        }

        var currentValues = new Dictionary<Guid, (string Value, string Text)>();

        if (record.AdmissionID.HasValue && record.AdmissionID.Value > 0)
        {
            var path = $"{HisConstants.RouteREmr}?EMRDataID={record.HisEmrDataID ?? Guid.Empty}&TemplateID={hisDef.TemplateId}&AdmissionID={record.AdmissionID.Value}&IsInherit=false";
            var readRes = await _client.SendAsync(
                HisOperation.ReadFormData,
                new HisRequest(path, "GET", query.Credential, TraceId: query.TraceId, DivisionId: query.DivisionId),
                ct);

            if (readRes.IsSuccess && !string.IsNullOrWhiteSpace(readRes.Value.RawJson))
            {
                try
                {
                    using var readDoc = JsonDocument.Parse(readRes.Value.RawJson);
                    var root = readDoc.RootElement;
                    ExtractDetails(root, currentValues);

                    string returnedEmrIdStr = RegistrationFormNormalizer.GetString(root, "EMRDataID") ??
                                              RegistrationFormNormalizer.GetString(root, "Id");
                    if (Guid.TryParse(returnedEmrIdStr, out var readEmrId) && readEmrId != Guid.Empty)
                    {
                        if (!record.HisEmrDataID.HasValue || record.HisEmrDataID.Value == Guid.Empty)
                        {
                            record.HisEmrDataID = readEmrId;
                            record.HisFormTemplateID = hisDef.TemplateId;
                            record.HisFormSyncStatus = "Synced";
                            record.HisFormSyncError = "";
                            await _uow.SaveChangesAsync(ct);
                        }
                    }
                }
                catch
                {
                    // Ignore transient read errors on existing REMR
                }
            }
        }

        var historyNodesRaw = new List<JsonElement>();
        var extraInfoNodesRaw = new List<JsonElement>();

        using (var layoutDoc = JsonDocument.Parse(hisDef.LayoutJson ?? "[]"))
        {
            if (layoutDoc.RootElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var elem in layoutDoc.RootElement.EnumerateArray())
                {
                    if (elem.TryGetProperty("ItemGroupID", out var gidProp) && gidProp.TryGetInt32(out var gid))
                    {
                        if (gid == historySection.ItemGroupID) historyNodesRaw.Add(elem.Clone());
                        else if (gid == extraInfoSection.ItemGroupID) extraInfoNodesRaw.Add(elem.Clone());
                    }
                }
            }
        }

        string histTitle = null;
        string extraTitle = null;

        using (var detailsDoc = JsonDocument.Parse(hisDef.DetailsJson ?? "[]"))
        {
            if (detailsDoc.RootElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var elem in detailsDoc.RootElement.EnumerateArray())
                {
                    if (elem.TryGetProperty("ItemGroupID", out var gidProp) && gidProp.TryGetInt32(out var gid))
                    {
                        if (gid == historySection.ItemGroupID && elem.TryGetProperty("ItemGroupName", out var nameProp))
                            histTitle = nameProp.GetString();
                        else if (gid == extraInfoSection.ItemGroupID && elem.TryGetProperty("ItemGroupName", out var nameProp2))
                            extraTitle = nameProp2.GetString();
                    }
                }
            }
        }

        var sections = new List<RegistrationFormSection>
        {
            new()
            {
                Kind = "HISTORY",
                ItemGroupID = historySection.ItemGroupID,
                Title = string.IsNullOrWhiteSpace(histTitle) ? "Tiền sử bệnh" : histTitle,
                Nodes = RegistrationFormNormalizer.NormalizeNodes(historyNodesRaw, historySection.ItemGroupID, currentValues)
            },
            new()
            {
                Kind = "EXTRA_INFO",
                ItemGroupID = extraInfoSection.ItemGroupID,
                Title = string.IsNullOrWhiteSpace(extraTitle) ? "Thông tin bổ sung" : extraTitle,
                Nodes = RegistrationFormNormalizer.NormalizeNodes(extraInfoNodesRaw, extraInfoSection.ItemGroupID, currentValues)
            }
        };

        return ApplicationResult<RegistrationRecordForm>.Success(new RegistrationRecordForm
        {
            RecordID = record.RecordID,
            AdmissionID = record.AdmissionID,
            HisEmrDataID = record.HisEmrDataID,
            HisFormTemplateID = record.HisFormTemplateID,
            HisFormSyncStatus = record.HisFormSyncStatus,
            HisFormSyncError = record.HisFormSyncError,
            VariantCode = record.VariantCode,
            TemplateCode = hisDef.TemplateCode,
            TemplateName = hisDef.TemplateName,
            Sections = sections
        });
    }

    public static void ExtractDetails(JsonElement root, Dictionary<Guid, (string Value, string Text)> currentValues)
    {
        IEnumerable<JsonElement> detailElements = null;
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
            var itemIdStr = RegistrationFormNormalizer.GetString(d, "ItemID") ?? RegistrationFormNormalizer.GetString(d, "ItemId");
            if (!string.IsNullOrEmpty(itemIdStr) && Guid.TryParse(itemIdStr, out var itemId) && itemId != Guid.Empty)
            {
                var val = RegistrationFormNormalizer.GetString(d, "Value") ?? "";
                var text = RegistrationFormNormalizer.GetString(d, "Text");
                currentValues[itemId] = (val, text);
            }
        }
    }
}
