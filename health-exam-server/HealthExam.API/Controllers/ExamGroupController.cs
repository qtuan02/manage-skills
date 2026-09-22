#nullable enable

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using HealthExam.API.Contracts;
using HealthExam.Application.Integrations;
using HealthExam.Application.RegistrationForms;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace HealthExam.API.Controllers;

/// <summary>
/// Endpoint quản lý nhóm khám và biểu mẫu đăng ký Bước 2 (dựa trên cấu hình HIS).
/// </summary>
[Route("v1/exam-groups")]
public class ExamGroupController : HealthExamControllerBase
{
    private readonly IListAvailableExamGroupsHandler _listAvailableHandler;
    private readonly IGetExamGroupRegistrationFormsHandler _getFormHandler;
    private readonly IHisCredentialOptions _options;

    public ExamGroupController(
        IListAvailableExamGroupsHandler listAvailableHandler,
        IGetExamGroupRegistrationFormsHandler getFormHandler,
        IHisCredentialOptions options)
    {
        _listAvailableHandler = listAvailableHandler;
        _getFormHandler = getFormHandler;
        _options = options;
    }

    private string Credential => Request.Headers[_options.CredentialHeaderName].ToString();

    [HttpGet("available")]
    [SwaggerOperation(
        Summary = "Danh sách nhóm khám có biểu mẫu đăng ký hoạt động",
        Description = "Trả về các nhóm khám (đối tượng khám) được cấu hình biểu mẫu cho tenant, sắp xếp theo OrderNo quy định.")]
    public async Task<ActionResult<ResultData<IReadOnlyList<AvailableExamGroupItem>>>> ListAvailable(
        CancellationToken ct = default)
    {
        var res = await _listAvailableHandler.HandleAsync(
            new ListAvailableExamGroupsQuery(HealthExamContext.DivisionId), ct);
        return ToActionResult(res);
    }

    [HttpGet("{variantCode}/registration-form")]
    [SwaggerOperation(
        Summary = "Lấy biểu mẫu đăng ký KSK chuẩn hoá theo nhóm khám",
        Description = "Lấy và chuẩn hoá hai phân đoạn Tiền sử bệnh (HISTORY) và Thông tin bổ sung (EXTRA_INFO) từ HIS.")]
    public async Task<ActionResult<ResultData<RegistrationFormDefinition>>> GetRegistrationForm(
        [FromRoute] string variantCode,
        CancellationToken ct = default)
    {
        var res = await _getFormHandler.HandleAsync(
            new GetExamGroupRegistrationFormsQuery(HealthExamContext.DivisionId, variantCode, Credential, HealthExamContext.TraceId), ct);
        return ToActionResult(res);
    }
}
