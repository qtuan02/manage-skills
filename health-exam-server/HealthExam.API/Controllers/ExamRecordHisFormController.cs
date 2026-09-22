#nullable enable

using System;
using System.Linq;
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
/// Biểu mẫu KSK và các thao tác ký EMR gắn với từng hồ sơ khám.
/// Dữ liệu biểu mẫu, quy trình và chữ ký do HIS quản lý; health-exam-server
/// xác thực hồ sơ, đợt khám, quyền sở hữu và chuyển tiếp an toàn tới HIS.
/// </summary>
[Route("v1/exam-records/{recordId:guid}/his-form")]
public class ExamRecordHisFormController : HealthExamControllerBase
{
    private readonly IGetExamRecordHisFormHandler _getForm;
    private readonly IGetHisRecordSectionHandler _getSection;
    private readonly ISaveHisFormSectionHandler _saveSection;
    private readonly IResetHisFormSectionHandler _resetSection;
    private readonly IListHisProcessesHandler _listProcesses;
    private readonly IGetHisProcessHandler _getProcess;
    private readonly IHisCredentialOptions _options;

    public ExamRecordHisFormController(
        IGetExamRecordHisFormHandler getForm,
        IGetHisRecordSectionHandler getSection,
        ISaveHisFormSectionHandler saveSection,
        IResetHisFormSectionHandler resetSection,
        IListHisProcessesHandler listProcesses,
        IGetHisProcessHandler getProcess,
        IHisCredentialOptions options)
    {
        _getForm = getForm;
        _getSection = getSection;
        _saveSection = saveSection;
        _resetSection = resetSection;
        _listProcesses = listProcesses;
        _getProcess = getProcess;
        _options = options;
    }

    private string Credential => Request.Headers[_options.CredentialHeaderName].ToString();

    [HttpGet]
    [SwaggerOperation(
        Summary = "Lấy thông tin biểu mẫu KSK và lượt tiếp nhận HIS của hồ sơ",
        Description = "Trả về định nghĩa biểu mẫu KSK-TREN18TUOI và AdmissionID gắn với hồ sơ. Phase 1 chỉ đọc, giá trị biểu mẫu và chữ ký do HIS quản lý.")]
    public async Task<ActionResult<ResultData<ExamRecordHisForm>>> GetForm(
        [FromRoute] Guid recordId,
        CancellationToken ct = default)
    {
        var result = await _getForm.HandleAsync(
            new GetExamRecordHisFormQuery(HealthExamContext.DivisionId, recordId, Credential, HealthExamContext.TraceId), ct);

        if (!result.IsSuccess)
        {
            return ToActionResult(ApplicationResult<ExamRecordHisForm>.Fail(
                result.Failure.Code, result.Failure.Message, result.Failure.Payload));
        }

        var defDto = HisFormController.MapToDto(result.Value.Definition);
        var dto = new ExamRecordHisForm(result.Value.RecordId, result.Value.AdmissionId, defDto);
        return ToActionResult(ApplicationResult<ExamRecordHisForm>.Success(dto));
    }

    [HttpGet("sections/{itemGroupId:int}")]
    [SwaggerOperation(
        Summary = "Lấy các node của một phần biểu mẫu KSK",
        Description = "Chỉ trả về cây Layout thuộc ItemGroupID đã chọn. Lần gọi đầu nạp cây template từ HIS vào cache, các lần sau lọc từ cache.")]
    public async Task<ActionResult<ResultData<ExamFormSection>>> GetSection(
        [FromRoute] Guid recordId,
        [FromRoute] int itemGroupId,
        CancellationToken ct = default)
    {
        var result = await _getSection.HandleAsync(
            new GetHisRecordSectionQuery(HealthExamContext.DivisionId, recordId, itemGroupId, Credential, HealthExamContext.TraceId), ct);

        if (!result.IsSuccess)
        {
            return ToActionResult(ApplicationResult<ExamFormSection>.Fail(
                result.Failure.Code, result.Failure.Message, result.Failure.Payload));
        }

        return ToActionResult(ApplicationResult<ExamFormSection>.Success(ToExamFormSectionDto(result.Value)));
    }

    [HttpDelete("sections/{itemGroupId:int}")]
    [SwaggerOperation(
        Summary = "Xóa/reset dữ liệu của một phân đoạn biểu mẫu khám chuyên khoa KSK",
        Description = "Reset các trường thuộc phân đoạn (ItemGroupId) về giá trị mặc định của template, xóa bước ký liên quan và trả về phân đoạn đã reset.")]
    public async Task<ActionResult<ResultData<ExamFormSection>>> ResetSection(Guid recordId, int itemGroupId, CancellationToken ct = default)
    {
        var result = await _resetSection.HandleAsync(new ResetHisFormSectionCommand(
            HealthExamContext.DivisionId, recordId, itemGroupId, Credential,
            HealthExamContext.TraceId, HealthExamContext.ActorId), ct);

        if (!result.IsSuccess)
        {
            return ToActionResult(ApplicationResult<ExamFormSection>.Fail(
                result.Failure.Code, result.Failure.Message, result.Failure.Payload));
        }

        return ToActionResult(ApplicationResult<ExamFormSection>.Success(ToExamFormSectionDto(result.Value)));
    }

    [HttpPut("sections/{itemGroupId:int}")]
    [SwaggerOperation(
        Summary = "Lưu giá trị của một phân đoạn biểu mẫu khám chuyên khoa KSK",
        Description = "Cập nhật các giá trị của các node thuộc phân đoạn (ItemGroupId), ghi nhận xuống HIS EMR Data và lưu vết vào hồ sơ.")]
    public async Task<ActionResult<ResultData<ExamFormSection>>> SaveSection(
        [FromRoute] Guid recordId,
        [FromRoute] int itemGroupId,
        [FromBody] FormSectionSaveRequest? request,
        CancellationToken ct = default)
    {
        var fields = request?.Fields != null
            ? System.Linq.Enumerable.ToList(System.Linq.Enumerable.Select(request.Fields, f => new HisFormSectionFieldValue(f.ItemId, f.Value ?? "", f.Text)))
            : new System.Collections.Generic.List<HisFormSectionFieldValue>();
        var command = new SaveHisFormSectionCommand(
            HealthExamContext.DivisionId,
            recordId,
            itemGroupId,
            new HisFormSectionSaveRequest(fields, request?.IsDraft ?? true),
            Credential,
            HealthExamContext.TraceId,
            HealthExamContext.ActorId.ToString(),
            HealthExamContext.ActorName,
            HealthExamContext.ActorKind);

        var result = await _saveSection.HandleAsync(command, ct);
        if (!result.IsSuccess)
        {
            return ToActionResult(ApplicationResult<ExamFormSection>.Fail(
                result.Failure.Code, result.Failure.Message, result.Failure.Payload));
        }

        return ToActionResult(ApplicationResult<ExamFormSection>.Success(ToExamFormSectionDto(result.Value)));
    }

    private static ExamFormSection ToExamFormSectionDto(HisRecordSectionResult section)
    {
        var layout = JArray.Parse(section.LayoutJson);
        return new ExamFormSection(
            section.RecordId,
            section.AdmissionId,
            section.ItemGroupId,
            layout,
            (short)section.RecordState,
            section.CurrentStepStatus,
            section.CurrentStepSignedByEmployeeID,
            section.CurrentStepSignedAt,
            section.SigningProgressDone,
            section.SigningProgressTotal,
            section.CurrentStepSignedByEmployeeName,
            section.CurrentStepPerformedByEmployeeID,
            section.CurrentStepPerformedByEmployeeName,
            (section.Signers ?? Array.Empty<SignRoleEmployee>())
                .Select(s => new SignerChoice(s.EmployeeID, s.EmployeeCode, s.EmployeeName)).ToList());
    }

    [HttpGet("processes")]
    [SwaggerOperation(
        Summary = "Lấy danh sách quy trình khám của hồ sơ từ HIS",
        Description = "Lấy danh sách quy trình khám (M02_MedicalProcess) thuộc lượt tiếp nhận HIS gắn với hồ sơ, đã lọc theo biểu mẫu KSK.")]
    public async Task<ActionResult<ResultData<JToken>>> GetProcesses(
        [FromRoute] Guid recordId,
        CancellationToken ct = default)
    {
        var result = await _listProcesses.HandleAsync(
            new ListHisProcessesQuery(HealthExamContext.DivisionId, recordId, Credential, HealthExamContext.TraceId), ct);

        return ToJsonActionResult(result);
    }

    [HttpGet("processes/{processId:guid}")]
    [SwaggerOperation(
        Summary = "Chi tiết quy trình khám và các phần khám chuyên khoa",
        Description = "Lấy chi tiết quy trình khám kèm danh sách các phần khám chuyên khoa (M02_MedicalProcessDetail).")]
    public async Task<ActionResult<ResultData<JToken>>> GetProcess(
        [FromRoute] Guid recordId,
        [FromRoute] Guid processId,
        CancellationToken ct = default)
    {
        var result = await _getProcess.HandleAsync(
            new GetHisProcessQuery(HealthExamContext.DivisionId, recordId, processId, Credential, HealthExamContext.TraceId), ct);

        return ToJsonActionResult(result);
    }

    private ActionResult<ResultData<JToken>> ToJsonActionResult(ApplicationResult<HisJsonDocument> result)
    {
        if (!result.IsSuccess)
        {
            return ToActionResult(ApplicationResult<JToken>.Fail(
                result.Failure.Code, result.Failure.Message, result.Failure.Payload));
        }

        JToken token;
        try
        {
            token = JToken.Parse(result.Value.RawJson ?? "null");
        }
        catch
        {
            token = JValue.CreateNull();
        }

        return ToActionResult(ApplicationResult<JToken>.Success(token));
    }
}
