using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using HealthExam.API.Contracts;
using HealthExam.Application.Imports;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace HealthExam.API.Controllers;

/// <summary>
/// Nạp Excel danh sách đăng ký — UC03.5 / 02-api-spec §3.3.
/// </summary>
public class ExamImportController : HealthExamControllerBase
{
    private readonly IDownloadImportTemplateHandler _downloadTemplateHandler;
    private readonly IUploadImportHandler _uploadHandler;
    private readonly IGetImportHandler _getHandler;
    private readonly IListImportErrorsHandler _listErrorsHandler;
    private readonly ICommitImportHandler _commitHandler;
    private readonly IDiscardImportHandler _discardHandler;

    public ExamImportController(
        IDownloadImportTemplateHandler downloadTemplateHandler,
        IUploadImportHandler uploadHandler,
        IGetImportHandler getHandler,
        IListImportErrorsHandler listErrorsHandler,
        ICommitImportHandler commitHandler,
        IDiscardImportHandler discardHandler)
    {
        _downloadTemplateHandler = downloadTemplateHandler;
        _uploadHandler = uploadHandler;
        _getHandler = getHandler;
        _listErrorsHandler = listErrorsHandler;
        _commitHandler = commitHandler;
        _discardHandler = discardHandler;
    }

    /// <summary>File mẫu — đúng bộ cột server đang chờ.</summary>
    [HttpGet("exam-sessions/{sessionId:guid}/imports/template")]
    [SwaggerOperation(
        Summary = "Tải file mẫu nhập hồ sơ",
        Description = "Tải workbook Excel đúng cấu trúc cột mà API nhận cho một đợt khám đã chọn.")]
    public async Task<IActionResult> Template(Guid sessionId, CancellationToken ct = default)
    {
        var res = await _downloadTemplateHandler.HandleAsync(
            new DownloadImportTemplateQuery(HealthExamContext.DivisionId, sessionId), ct);
        if (!res.IsSuccess)
        {
            return ToActionResult(res).Result;
        }
        return File(res.Value.Content, res.Value.ContentType, res.Value.FileName);
    }

    /// <summary>
    /// Pha 1 — tải file lên, kiểm tra từng dòng. KHÔNG ghi hồ sơ nào.
    /// </summary>
    [HttpPost("exam-sessions/{sessionId:guid}/imports")]
    [RequestSizeLimit(30 * 1024 * 1024)]
    [SwaggerOperation(
        Summary = "Tải Excel và kiểm tra hồ sơ",
        Description = "Pha 1 của nhập Excel: kiểm tra từng dòng và trả kết quả xem trước, chưa ghi bất kỳ hồ sơ nào vào hệ thống; sessionId xác định đợt nhận hồ sơ.")]
    public async Task<ActionResult<ResultData<ImportBatchResult>>> Upload(
        Guid sessionId, IFormFile file,
        [FromQuery] int page = 1, [FromQuery] int size = 50, CancellationToken ct = default)
    {
        if (file == null || file.Length == 0)
            return Failure<ImportBatchResult>(ErrorCodes.FileInvalid, "Chưa chọn file");

        await using var stream = file.OpenReadStream();
        var command = new UploadImportCommand(
            DivisionId: HealthExamContext.DivisionId,
            SessionId: sessionId,
            File: stream,
            FileName: file.FileName,
            ActorId: HealthExamContext.ActorId.ToString(),
            ActorKind: HealthExamContext.ActorKind,
            Page: page,
            Size: size,
            TraceId: HealthExamContext.TraceId);

        var res = await _uploadHandler.HandleAsync(command, ct);
        return ToActionResult(res);
    }

    /// <summary>Đọc lại kết quả kiểm tra. <paramref name="onlyInvalid"/> cho màn "chỉ xem dòng hỏng".</summary>
    [HttpGet("imports/{importId:guid}")]
    [SwaggerOperation(
        Summary = "Xem kết quả nhập Excel",
        Description = "Đọc lại lô nhập đã upload với phân trang; có thể chỉ lấy các dòng không hợp lệ bằng onlyInvalid.")]
    public async Task<ActionResult<ResultData<ImportBatchResult>>> Get(
        Guid importId, [FromQuery] bool onlyInvalid = false,
        [FromQuery] int page = 1, [FromQuery] int size = 50, CancellationToken ct = default)
    {
        var res = await _getHandler.HandleAsync(
            new GetImportQuery(HealthExamContext.DivisionId, importId, page, size, onlyInvalid), ct);
        return ToActionResult(res);
    }

    /// <summary>Tải các dòng lỗi về dạng Excel, có cột "Lý do" — người dùng sửa rồi nạp lại.</summary>
    [HttpGet("imports/{importId:guid}/errors")]
    [SwaggerOperation(
        Summary = "Tải các dòng Excel bị lỗi",
        Description = "Xuất các dòng không hợp lệ của lô nhập ra file Excel kèm lý do để người dùng sửa và tải lại.")]
    public async Task<IActionResult> Errors(Guid importId, CancellationToken ct = default)
    {
        var res = await _listErrorsHandler.HandleAsync(
            new ListImportErrorsQuery(HealthExamContext.DivisionId, importId), ct);
        if (!res.IsSuccess)
        {
            return ToActionResult(res).Result;
        }
        return File(res.Value.Content, res.Value.ContentType, res.Value.FileName);
    }

    /// <summary>
    /// Pha 2 — xác nhận nạp. IDEMPOTENT theo importId.
    /// </summary>
    [HttpPost("imports/{importId:guid}/commit")]
    [SwaggerOperation(
        Summary = "Xác nhận ghi hồ sơ từ Excel",
        Description = "Pha 2 của nhập Excel: ghi các dòng hợp lệ thành hồ sơ. Thao tác idempotent theo importId nên gọi lại không tạo hồ sơ trùng.")]
    public async Task<ActionResult<ResultData<ImportBatchResult>>> Commit(
        Guid importId, [FromBody] ImportCommitRequest request,
        [FromQuery] int page = 1, [FromQuery] int size = 50, CancellationToken ct = default)
    {
        var command = new CommitImportCommand(
            DivisionId: HealthExamContext.DivisionId,
            ImportId: importId,
            ActorId: HealthExamContext.ActorId.ToString(),
            ActorKind: HealthExamContext.ActorKind,
            SkipInvalidRows: request?.SkipInvalidRows ?? false,
            Page: page,
            Size: size,
            TraceId: HealthExamContext.TraceId);

        var res = await _commitHandler.HandleAsync(command, ct);
        return ToActionResult(res);
    }

    /// <summary>Bỏ lô đã upload. Lô đã nạp xong thì không bỏ được — 4090.</summary>
    [HttpDelete("imports/{importId:guid}")]
    [SwaggerOperation(
        Summary = "Hủy lô nhập Excel",
        Description = "Bỏ lô Excel đã upload nhưng chưa commit; lô đã ghi hồ sơ thì không thể discard.")]
    public async Task<ActionResult<ResultData<bool>>> Discard(Guid importId, CancellationToken ct = default)
    {
        var command = new DiscardImportCommand(
            DivisionId: HealthExamContext.DivisionId,
            ImportId: importId,
            ActorId: HealthExamContext.ActorId.ToString(),
            ActorKind: HealthExamContext.ActorKind,
            TraceId: HealthExamContext.TraceId);

        var res = await _discardHandler.HandleAsync(command, ct);
        return ToActionResult(res);
    }
}
