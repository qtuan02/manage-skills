#nullable enable

using System.Threading;
using System.Threading.Tasks;
using HealthExam.API.Contracts;
using HealthExam.Application.Common;
using HealthExam.Application.His;
using HealthExam.Application.Integrations;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json.Linq;
using Swashbuckle.AspNetCore.Annotations;

namespace HealthExam.API.Controllers;

/// <summary>
/// Tra cứu định nghĩa biểu mẫu HIS KSK (Phase 1: KSK-TREN18TUOI).
/// Dữ liệu biểu mẫu do HIS quản lý, health-exam-server cache 300s.
/// </summary>
[Route("v1/his-forms")]
public class HisFormController : HealthExamControllerBase
{
    private readonly IGetHisFormDefinitionHandler _handler;
    private readonly IHisCredentialOptions _options;

    public HisFormController(
        IGetHisFormDefinitionHandler handler,
        IHisCredentialOptions options)
    {
        _handler = handler;
        _options = options;
    }

    [HttpGet("{templateCode}")]
    [SwaggerOperation(
        Summary = "Lấy định nghĩa biểu mẫu HIS (KSK-TREN18TUOI)",
        Description = "Trả về metadata và cấu trúc layout của biểu mẫu KSK-TREN18TUOI từ HIS (được cache 300s). Không đọc hoặc ghi form-server.")]
    public async Task<ActionResult<ResultData<HisFormDefinition>>> Get(
        [FromRoute] string templateCode,
        CancellationToken ct = default)
    {
        var credential = Request.Headers[_options.CredentialHeaderName].ToString();
        var result = await _handler.HandleAsync(
            new GetHisFormDefinitionQuery(
                templateCode,
                HealthExamContext.DivisionId,
                credential,
                HealthExamContext.TraceId),
            ct);

        if (!result.IsSuccess)
        {
            return ToActionResult(ApplicationResult<HisFormDefinition>.Fail(
                result.Failure.Code, result.Failure.Message, result.Failure.Payload));
        }

        var def = MapToDto(result.Value);
        return ToActionResult(ApplicationResult<HisFormDefinition>.Success(def));
    }

    [HttpGet("catalogs/icd10")]
    [SwaggerOperation(
        Summary = "Tra cứu danh mục chẩn đoán ICD-10 từ HIS",
        Description = "Tìm kiếm danh mục ICD-10 phục vụ chẩn đoán sơ bộ và xác định, hỗ trợ lọc theo từ khóa và giới hạn số lượng.")]
    public async Task<ActionResult<ResultData<System.Collections.Generic.IReadOnlyList<Icd10ChoiceItem>>>> GetIcd10(
        [FromServices] IGetIcd10ChoicesHandler icd10Handler,
        [FromQuery] string? filter = null,
        [FromQuery] int amount = 20,
        CancellationToken ct = default)
    {
        var credential = Request.Headers[_options.CredentialHeaderName].ToString();
        var result = await icd10Handler.HandleAsync(
            new GetIcd10ChoicesQuery(HealthExamContext.DivisionId, filter ?? "", amount, credential, HealthExamContext.TraceId), ct);

        if (!result.IsSuccess)
        {
            return ToActionResult(ApplicationResult<System.Collections.Generic.IReadOnlyList<Icd10ChoiceItem>>.Fail(
                result.Failure.Code, result.Failure.Message, result.Failure.Payload));
        }

        var items = System.Linq.Enumerable.ToList(System.Linq.Enumerable.Select(result.Value, x => new Icd10ChoiceItem(x.Code, x.Label, x.CodeName, x.DisplayName)));
        return ToActionResult(ApplicationResult<System.Collections.Generic.IReadOnlyList<Icd10ChoiceItem>>.Success(items));
    }

    public static HisFormDefinition MapToDto(HisFormDefinitionResult r)
    {
        JArray details;
        try { details = JArray.Parse(r.DetailsJson ?? "[]"); }
        catch { details = new JArray(); }

        JArray layout;
        try { layout = JArray.Parse(r.LayoutJson ?? "[]"); }
        catch { layout = new JArray(); }

        return new HisFormDefinition
        {
            TemplateId = r.TemplateId,
            TemplateCode = r.TemplateCode,
            TemplateName = r.TemplateName,
            FileDocTypeId = r.FileDocTypeId,
            VersionCode = r.VersionCode,
            IsDraft = r.IsDraft,
            Active = r.Active,
            Details = details,
            Layout = layout
        };
    }
}
