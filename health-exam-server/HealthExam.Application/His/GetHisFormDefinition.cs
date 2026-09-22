using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using HealthExam.Application.Common;
using HealthExam.Application.Integrations;

namespace HealthExam.Application.His;

public interface IGetHisFormDefinitionHandler
{
    Task<ApplicationResult<HisFormDefinitionResult>> HandleAsync(
        GetHisFormDefinitionQuery query, CancellationToken ct = default);
}

public class GetHisFormDefinitionHandler : IGetHisFormDefinitionHandler
{
    private readonly IHisEmrClient _client;
    private readonly IHisFormDefinitionCache _cache;

    public GetHisFormDefinitionHandler(
        IHisEmrClient client,
        IHisFormDefinitionCache cache)
    {
        _client = client;
        _cache = cache;
    }

    public async Task<ApplicationResult<HisFormDefinitionResult>> HandleAsync(
        GetHisFormDefinitionQuery query, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query.TemplateCode) ||
            !string.Equals(query.TemplateCode.Trim(), HisConstants.TargetTemplateCode, StringComparison.OrdinalIgnoreCase))
        {
            return ApplicationResult<HisFormDefinitionResult>.Fail(
                ApplicationFailureCode.NotFound,
                $"Chỉ hỗ trợ biểu mẫu {HisConstants.TargetTemplateCode}");
        }

        if (string.IsNullOrWhiteSpace(query.Credential))
        {
            return ApplicationResult<HisFormDefinitionResult>.Fail(
                ApplicationFailureCode.Unauthorized,
                "Thiếu thông tin xác thực HIS trong request");
        }

        try
        {
            var definition = await _cache.GetOrCreateAsync(
                query.DivisionId,
                HisConstants.TargetTemplateCode,
                cancelToken => ResolveAndFetchAsync(query, cancelToken),
                ct);

            return ApplicationResult<HisFormDefinitionResult>.Success(definition);
        }
        catch (HisApplicationException ex)
        {
            return ApplicationResult<HisFormDefinitionResult>.Fail(ex.Code, ex.Message, ex.Payload);
        }
    }

    private async Task<HisFormDefinitionResult> ResolveAndFetchAsync(
        GetHisFormDefinitionQuery query, CancellationToken ct)
    {
        var listRes = await _client.SendAsync(
            HisOperation.ListDefinitions,
            new HisRequest(
                HisConstants.RouteGetListTemplate,
                "GET",
                query.Credential,
                TraceId: query.TraceId,
                DivisionId: query.DivisionId),
            ct);

        if (!listRes.IsSuccess)
        {
            var mapped = HisOutcomeMapper.ToApplicationResult(listRes);
            throw new HisApplicationException(mapped.Failure.Code, mapped.Failure.Message);
        }

        Guid templateId = Guid.Empty;
        using (var doc = JsonDocument.Parse(listRes.Value.RawJson ?? "[]"))
        {
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                throw new HisApplicationException(
                    ApplicationFailureCode.HisBadGateway,
                    "Danh sách biểu mẫu từ HIS không phải định dạng mảng");
            }

            var matches = new List<JsonElement>();
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                var code = item.TryGetProperty("TemplateCode", out var pCode) ? pCode.GetString() : null;
                var active = item.TryGetProperty("Active", out var pActive) && pActive.GetBoolean();
                if (string.Equals(code, HisConstants.TargetTemplateCode, StringComparison.OrdinalIgnoreCase) && active)
                {
                    matches.Add(item.Clone());
                }
            }

            if (matches.Count == 0)
            {
                throw new HisApplicationException(
                    ApplicationFailureCode.NotFound,
                    $"Không tìm thấy biểu mẫu {HisConstants.TargetTemplateCode} đang hoạt động trên HIS");
            }

            if (matches.Count > 1)
            {
                throw new HisApplicationException(
                    ApplicationFailureCode.InvalidState,
                    $"Tìm thấy nhiều hơn một biểu mẫu {HisConstants.TargetTemplateCode} đang hoạt động trên HIS");
            }

            var matched = matches[0];
            string idStr = null;
            if (matched.TryGetProperty("Id", out var pId)) idStr = pId.GetString();
            else if (matched.TryGetProperty("TemplateID", out var pTid)) idStr = pTid.GetString();
            else if (matched.TryGetProperty("TemplateId", out var pTid2)) idStr = pTid2.GetString();

            if (string.IsNullOrEmpty(idStr) || !Guid.TryParse(idStr, out templateId))
            {
                throw new HisApplicationException(
                    ApplicationFailureCode.HisBadGateway,
                    "Biểu mẫu trên HIS không có ID hợp lệ");
            }
        }

        var metadataTask = _client.SendAsync(
            HisOperation.GetDefinition,
            new HisRequest(
                $"{HisConstants.RouteGetTemplate}?templateID={templateId}",
                "GET",
                query.Credential,
                TraceId: query.TraceId,
                DivisionId: query.DivisionId),
            ct);

        var layoutTask = _client.SendAsync(
            HisOperation.GetDefinitionLayout,
            new HisRequest(
                $"{HisConstants.RouteGetTreeTemplate}?templateID={templateId}",
                "GET",
                query.Credential,
                TraceId: query.TraceId,
                DivisionId: query.DivisionId),
            ct);

        await Task.WhenAll(metadataTask, layoutTask);

        var metaRes = await metadataTask;
        var layoutRes = await layoutTask;

        if (!metaRes.IsSuccess)
        {
            var mapped = HisOutcomeMapper.ToApplicationResult(metaRes);
            throw new HisApplicationException(mapped.Failure.Code, mapped.Failure.Message);
        }

        if (!layoutRes.IsSuccess)
        {
            var mapped = HisOutcomeMapper.ToApplicationResult(layoutRes);
            throw new HisApplicationException(mapped.Failure.Code, mapped.Failure.Message);
        }

        var metaDoc = JsonDocument.Parse(metaRes.Value.RawJson ?? "{}");
        var root = metaDoc.RootElement;

        var resolvedId = templateId;
        if (root.TryGetProperty("Id", out var rId) && Guid.TryParse(rId.GetString(), out var parsedId))
            resolvedId = parsedId;
        else if (root.TryGetProperty("TemplateID", out var rTid) && Guid.TryParse(rTid.GetString(), out var parsedTid))
            resolvedId = parsedTid;

        var resolvedCode = root.TryGetProperty("TemplateCode", out var rCode) ? rCode.GetString() : HisConstants.TargetTemplateCode;
        var resolvedName = root.TryGetProperty("TemplateName", out var rName) ? rName.GetString() : "";

        var fileDocTypeId = 0;
        if (root.TryGetProperty("FileDocTypeID", out var rDocId))
        {
            if (rDocId.ValueKind == JsonValueKind.Number) fileDocTypeId = rDocId.GetInt32();
            else if (int.TryParse(rDocId.GetString(), out var pdi)) fileDocTypeId = pdi;
        }
        else if (root.TryGetProperty("FileDocTypeId", out var rDocId2))
        {
            if (rDocId2.ValueKind == JsonValueKind.Number) fileDocTypeId = rDocId2.GetInt32();
            else if (int.TryParse(rDocId2.GetString(), out var pdi2)) fileDocTypeId = pdi2;
        }

        var versionCode = root.TryGetProperty("VersionCode", out var rVer) ? rVer.GetString() : "";

        var isDraft = false;
        if (root.TryGetProperty("IsDraft", out var rDraft))
        {
            if (rDraft.ValueKind == JsonValueKind.True || rDraft.ValueKind == JsonValueKind.False) isDraft = rDraft.GetBoolean();
            else if (bool.TryParse(rDraft.GetString(), out var pd)) isDraft = pd;
        }

        var activeStatus = true;
        if (root.TryGetProperty("Active", out var rAct))
        {
            if (rAct.ValueKind == JsonValueKind.True || rAct.ValueKind == JsonValueKind.False) activeStatus = rAct.GetBoolean();
            else if (bool.TryParse(rAct.GetString(), out var pa)) activeStatus = pa;
        }

        string detailsJson = "[]";
        if (root.TryGetProperty("Details", out var pDet)) detailsJson = pDet.GetRawText();
        else if (root.TryGetProperty("M03_TemplateDetail", out var pDet2)) detailsJson = pDet2.GetRawText();
        else if (root.TryGetProperty("TemplateDetails", out var pDet3)) detailsJson = pDet3.GetRawText();
        else if (root.ValueKind == JsonValueKind.Array) detailsJson = root.GetRawText();

        var layoutDoc = JsonDocument.Parse(layoutRes.Value.RawJson ?? "[]");
        var layoutRoot = layoutDoc.RootElement;
        string layoutJson = "[]";
        if (layoutRoot.ValueKind == JsonValueKind.Array) layoutJson = layoutRoot.GetRawText();
        else if (layoutRoot.TryGetProperty("Layout", out var pLay)) layoutJson = pLay.GetRawText();

        return new HisFormDefinitionResult(
            resolvedId,
            resolvedCode ?? HisConstants.TargetTemplateCode,
            resolvedName ?? "",
            fileDocTypeId,
            versionCode ?? "",
            isDraft,
            activeStatus,
            detailsJson,
            layoutJson);
    }
}
