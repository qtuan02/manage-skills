using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using HealthExam.Application.Common;
using HealthExam.Application.His;
using HealthExam.Application.Integrations;

namespace HealthExam.Application.RegistrationForms;

public static class HisDefinitionResolver
{
    public static async Task<HisFormDefinitionResult> GetDefinitionAsync(
        IHisEmrClient client,
        IHisFormDefinitionCache cache,
        string divisionId,
        string templateCode,
        string credential,
        string traceId,
        CancellationToken ct = default)
    {
        return await cache.GetOrCreateAsync(
            divisionId,
            templateCode,
            async cancelToken =>
            {
                var listRes = await client.SendAsync(
                    HisOperation.ListDefinitions,
                    new HisRequest(
                        HisConstants.RouteGetListTemplate,
                        "GET",
                        credential,
                        TraceId: traceId,
                        DivisionId: divisionId),
                    cancelToken);

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
                        if (string.Equals(code, templateCode, StringComparison.OrdinalIgnoreCase) && active)
                        {
                            matches.Add(item.Clone());
                        }
                    }

                    if (matches.Count == 0)
                    {
                        throw new HisApplicationException(
                            ApplicationFailureCode.NotFound,
                            $"Không tìm thấy biểu mẫu {templateCode} đang hoạt động trên HIS");
                    }

                    if (matches.Count > 1)
                    {
                        throw new HisApplicationException(
                            ApplicationFailureCode.InvalidState,
                            $"Tìm thấy nhiều hơn một biểu mẫu {templateCode} đang hoạt động trên HIS");
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

                var metadataTask = client.SendAsync(
                    HisOperation.GetDefinition,
                    new HisRequest(
                        $"{HisConstants.RouteGetTemplate}?templateID={templateId}",
                        "GET",
                        credential,
                        TraceId: traceId,
                        DivisionId: divisionId),
                    cancelToken);

                var layoutTask = client.SendAsync(
                    HisOperation.GetDefinitionLayout,
                    new HisRequest(
                        $"{HisConstants.RouteGetTreeTemplate}?templateID={templateId}",
                        "GET",
                        credential,
                        TraceId: traceId,
                        DivisionId: divisionId),
                    cancelToken);

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

                using var metaDoc = JsonDocument.Parse(metaRes.Value.RawJson ?? "{}");
                var root = metaDoc.RootElement;
                var templateName = root.TryGetProperty("TemplateName", out var pName) ? pName.GetString() ?? "" : "";
                var fileDocTypeId = root.TryGetProperty("FileDocTypeID", out var pFdt) ? (pFdt.TryGetInt32(out var fdtVal) ? fdtVal : 0) : 0;
                var versionCode = root.TryGetProperty("VersionCode", out var pVer) ? pVer.GetString() ?? "" : "";
                var isDraft = root.TryGetProperty("IsDraft", out var pDraft) && pDraft.GetBoolean();
                var activeFlag = root.TryGetProperty("Active", out var pAct) && pAct.GetBoolean();
                var detailsJson = root.TryGetProperty("Details", out var pDetails) ? pDetails.GetRawText() : "[]";

                return new HisFormDefinitionResult(
                    templateId,
                    templateCode,
                    templateName,
                    fileDocTypeId,
                    versionCode,
                    isDraft,
                    activeFlag,
                    detailsJson,
                    layoutRes.Value.RawJson ?? "[]");
            },
            ct);
    }
}
