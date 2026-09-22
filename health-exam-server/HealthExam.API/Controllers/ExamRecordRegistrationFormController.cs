#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;
using HealthExam.API.Contracts;
using HealthExam.Application.Integrations;
using HealthExam.Application.RegistrationForms;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace HealthExam.API.Controllers;

/// <summary>
/// Endpoint quản lý giá trị biểu mẫu đăng ký KSK Bước 2 gắn với từng hồ sơ.
/// Các phân đoạn ngữ nghĩa hợp lệ: HISTORY (Tiền sử bệnh) và EXTRA_INFO (Thông tin bổ sung).
/// Dữ liệu lưu xuống HIS EMR thông qua read-merge-write an toàn.
/// </summary>
[Route("v1/exam-records/{recordId:guid}/registration-form")]
public class ExamRecordRegistrationFormController : HealthExamControllerBase
{
    private readonly IGetRegistrationFormHandler _getFormHandler;
    private readonly ISaveRegistrationFormSectionHandler _saveSectionHandler;
    private readonly IPreviewRegistrationFormPdfHandler _previewPdfHandler;
    private readonly IHisCredentialOptions _options;

    public ExamRecordRegistrationFormController(
        IGetRegistrationFormHandler getFormHandler,
        ISaveRegistrationFormSectionHandler saveSectionHandler,
        IPreviewRegistrationFormPdfHandler previewPdfHandler,
        IHisCredentialOptions options)
    {
        _getFormHandler = getFormHandler;
        _saveSectionHandler = saveSectionHandler;
        _previewPdfHandler = previewPdfHandler;
        _options = options;
    }

    private string Credential => Request.Headers[_options.CredentialHeaderName].ToString();

    [HttpGet]
    [SwaggerOperation(
        Summary = "Lấy biểu mẫu đăng ký KSK và các giá trị đã lưu của hồ sơ",
        Description = "Trả về biểu mẫu chuẩn hoá (gồm phân đoạn HISTORY và EXTRA_INFO) cùng các giá trị trường đã lưu trên HIS EMR.")]
    public async Task<ActionResult<ResultData<RegistrationRecordForm>>> GetForm(
        [FromRoute] Guid recordId,
        CancellationToken ct = default)
    {
        var res = await _getFormHandler.HandleAsync(
            new GetRegistrationFormQuery(HealthExamContext.DivisionId, recordId, Credential, HealthExamContext.TraceId), ct);
        return ToActionResult(res);
    }

    [HttpPut("sections/{sectionKind}")]
    [SwaggerOperation(
        Summary = "Lưu giá trị của một phân đoạn biểu mẫu đăng ký KSK",
        Description = "Lưu các giá trị của phân đoạn ngữ nghĩa (HISTORY hoặc EXTRA_INFO) vào HIS EMR, tự động tạo lượt tiếp nhận HIS nếu chưa có, bảo toàn toàn bộ giá trị của phân đoạn còn lại và trả về biểu mẫu đã cập nhật.")]
    public async Task<ActionResult<ResultData<RegistrationRecordForm>>> SaveSection(
        [FromRoute] Guid recordId,
        [FromRoute] string sectionKind,
        [FromBody] RegistrationSectionSaveRequest request,
        CancellationToken ct = default)
    {
        var res = await _saveSectionHandler.HandleAsync(
            new SaveRegistrationFormSectionCommand(
                HealthExamContext.DivisionId,
                recordId,
                sectionKind,
                request ?? new(),
                HealthExamContext.ActorId.ToString(),
                HealthExamContext.ActorKind,
                Credential,
                HealthExamContext.TraceId),
            ct);
        return ToActionResult(res);
    }

    [HttpGet("preview")]
    [SwaggerOperation(
        Summary = "Xem trước PDF biểu mẫu đăng ký KSK",
        Description = "Trả về file PDF của snapshot HIS EMR đã lưu tương ứng với hồ sơ khám mà không submit hay ký biểu mẫu.")]
    [ProducesResponseType(typeof(FileContentResult), 200, "application/pdf")]
    public async Task<IActionResult> Preview(
        [FromRoute] Guid recordId,
        CancellationToken ct = default)
    {
        var res = await _previewPdfHandler.HandleAsync(
            new PreviewRegistrationFormPdfQuery(
                HealthExamContext.DivisionId,
                recordId,
                Credential,
                HealthExamContext.TraceId,
                HealthExamContext.ActorId.ToString(),
                HealthExamContext.ActorKind),
            ct);

        if (!res.IsSuccess)
        {
            return ToActionResult(res).Result!;
        }

        return File(res.Value, "application/pdf", enableRangeProcessing: false);
    }
}
