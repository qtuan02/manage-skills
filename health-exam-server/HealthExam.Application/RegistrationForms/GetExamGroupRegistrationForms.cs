using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using HealthExam.Application.Common;
using HealthExam.Application.His;
using HealthExam.Application.Integrations;
using HealthExam.Domain.ExamSessions;

namespace HealthExam.Application.RegistrationForms;

public interface IGetExamGroupRegistrationFormsHandler
{
    Task<ApplicationResult<RegistrationFormDefinition>> HandleAsync(
        GetExamGroupRegistrationFormsQuery query, CancellationToken ct = default);
}

public class GetExamGroupRegistrationFormsHandler : IGetExamGroupRegistrationFormsHandler
{
    private readonly IRegistrationFormRepository _repo;
    private readonly IHisEmrClient _client;
    private readonly IHisFormDefinitionCache _cache;

    public GetExamGroupRegistrationFormsHandler(
        IRegistrationFormRepository repo,
        IHisEmrClient client,
        IHisFormDefinitionCache cache)
    {
        _repo = repo;
        _client = client;
        _cache = cache;
    }

    public async Task<ApplicationResult<RegistrationFormDefinition>> HandleAsync(
        GetExamGroupRegistrationFormsQuery query, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query.VariantCode))
        {
            return ApplicationResult<RegistrationFormDefinition>.Fail(
                ApplicationFailureCode.NotFound, "Mã nhóm khám không hợp lệ");
        }

        var trimmedVariant = query.VariantCode.Trim();
        if (!ExamGroups.IsValid(trimmedVariant))
        {
            return ApplicationResult<RegistrationFormDefinition>.Fail(
                ApplicationFailureCode.NotFound, $"Nhóm khám {trimmedVariant} không thuộc danh mục theo quy định");
        }

        var mapping = await _repo.GetActiveMappingAsync(query.DivisionId, trimmedVariant, ct);
        if (mapping == null)
        {
            return ApplicationResult<RegistrationFormDefinition>.Fail(
                ApplicationFailureCode.NotFound, $"Nhóm khám {trimmedVariant} chưa được cấu hình biểu mẫu hoặc đang bị vô hiệu hoá");
        }

        var historySection = mapping.Sections.SingleOrDefault(s => s.SectionKind == "HISTORY");
        var extraInfoSection = mapping.Sections.SingleOrDefault(s => s.SectionKind == "EXTRA_INFO");

        if (historySection == null || extraInfoSection == null)
        {
            return ApplicationResult<RegistrationFormDefinition>.Fail(
                ApplicationFailureCode.InvalidState, $"Cấu hình biểu mẫu cho nhóm {trimmedVariant} thiếu phân đoạn HISTORY hoặc EXTRA_INFO");
        }

        HisFormDefinitionResult hisDef;
        try
        {
            hisDef = await HisDefinitionResolver.GetDefinitionAsync(
                _client, _cache, query.DivisionId, mapping.TemplateCode, query.Credential, query.TraceId, ct);
        }
        catch (HisApplicationException ex)
        {
            return ApplicationResult<RegistrationFormDefinition>.Fail(ex.Code, ex.Message, ex.Payload);
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

        if (historyNodesRaw.Count == 0 || extraInfoNodesRaw.Count == 0)
        {
            return ApplicationResult<RegistrationFormDefinition>.Fail(
                ApplicationFailureCode.HisBadGateway,
                $"Biểu mẫu {mapping.TemplateCode} trên HIS thiếu phân đoạn được cấu hình (HISTORY ID={historySection.ItemGroupID}, EXTRA_INFO ID={extraInfoSection.ItemGroupID})");
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
                Nodes = RegistrationFormNormalizer.NormalizeNodes(historyNodesRaw, historySection.ItemGroupID)
            },
            new()
            {
                Kind = "EXTRA_INFO",
                ItemGroupID = extraInfoSection.ItemGroupID,
                Title = string.IsNullOrWhiteSpace(extraTitle) ? "Thông tin bổ sung" : extraTitle,
                Nodes = RegistrationFormNormalizer.NormalizeNodes(extraInfoNodesRaw, extraInfoSection.ItemGroupID)
            }
        };

        return ApplicationResult<RegistrationFormDefinition>.Success(new RegistrationFormDefinition
        {
            VariantCode = trimmedVariant,
            TemplateCode = hisDef.TemplateCode,
            TemplateName = hisDef.TemplateName,
            Sections = sections
        });
    }
}
