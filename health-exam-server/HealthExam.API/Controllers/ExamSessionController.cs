using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HealthExam.API.Contracts;
using HealthExam.Application.Common;
using HealthExam.Application.ExamSessions;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;
using ExamSessionCloseRequest = HealthExam.API.Contracts.ExamSessionCloseRequest;
using ExamSessionItem = HealthExam.API.Contracts.ExamSessionItem;
using ExamSessionReopenRequest = HealthExam.API.Contracts.ExamSessionReopenRequest;
using ExamSessionSaveRequest = HealthExam.API.Contracts.ExamSessionSaveRequest;
using ExamRecordItem = HealthExam.API.Contracts.ExamRecordItem;
using ExamSessionProgressItem = HealthExam.API.Contracts.ExamSessionProgressItem;
using ExamSessionProgressCounts = HealthExam.API.Contracts.ExamSessionProgressCounts;

using HealthExam.Application.ExamRecords;

namespace HealthExam.API.Controllers;

/// <summary>
/// Đợt khám — 02-api-spec §3.1.
///
/// PHASE ĐĂNG KÝ + NỐI FORM-SERVER: GET/POST/PUT + POST /close + POST /reopen + danh sách hồ
/// sơ trong đợt + GET /progress. CHƯA có DELETE (soft-delete) — xem cutCorners.
/// </summary>
[Route("v1/exam-sessions")]
public class ExamSessionController : HealthExamControllerBase
{
    private readonly IListExamSessionsHandler _listHandler;
    private readonly IGetExamSessionHandler _getHandler;
    private readonly ICreateExamSessionHandler _createHandler;
    private readonly IUpdateExamSessionHandler _updateHandler;
    private readonly ICloseExamSessionHandler _closeHandler;
    private readonly IReopenExamSessionHandler _reopenHandler;
    private readonly IListExamRecordsHandler _listRecordsHandler;
    private readonly IGetExamSessionProgressHandler _progressHandler;

    public ExamSessionController(
        IListExamSessionsHandler listHandler,
        IGetExamSessionHandler getHandler,
        ICreateExamSessionHandler createHandler,
        IUpdateExamSessionHandler updateHandler,
        ICloseExamSessionHandler closeHandler,
        IReopenExamSessionHandler reopenHandler,
        IListExamRecordsHandler listRecordsHandler,
        IGetExamSessionProgressHandler progressHandler)
    {
        _listHandler = listHandler;
        _getHandler = getHandler;
        _createHandler = createHandler;
        _updateHandler = updateHandler;
        _closeHandler = closeHandler;
        _reopenHandler = reopenHandler;
        _listRecordsHandler = listRecordsHandler;
        _progressHandler = progressHandler;
    }

    [HttpGet]
    [SwaggerOperation(
        Summary = "Tra cứu các đợt khám",
        Description = "Trả về danh sách đợt khám có phân trang, lọc theo từ khóa, đơn vị, gói khám, trạng thái và khoảng ngày.")]
    public async Task<ActionResult<ResultData<PaginationData<ExamSessionItem>>>> List(
        [FromQuery] string keyword = null,
        [FromQuery] Guid? organizationId = null,
        [FromQuery] Guid? packageId = null,
        [FromQuery] short? state = null,
        [FromQuery] DateOnly? from = null,
        [FromQuery] DateOnly? to = null,
        [FromQuery] int page = 1, [FromQuery] int size = 20, CancellationToken ct = default)
    {
        var filter = new ExamSessionFilter(keyword, organizationId, packageId, state, from, to, page, size);
        var result = await _listHandler.HandleAsync(
            new ListExamSessionsQuery(HealthExamContext.DivisionId, filter), ct);

        if (!result.IsSuccess)
        {
            return ToActionResult(ApplicationResult<PaginationData<ExamSessionItem>>.Fail(
                result.Failure.Code, result.Failure.Message, result.Failure.Payload));
        }

        var pageResult = result.Value;
        var items = pageResult.Items.Select(MapToItem).ToList();
        var paginated = new PaginationData<ExamSessionItem>(items, pageResult.Page, pageResult.Size, pageResult.Total);
        return ToActionResult(ApplicationResult<PaginationData<ExamSessionItem>>.Success(paginated));
    }

    [HttpGet("{sessionId:guid}")]
    [SwaggerOperation(
        Summary = "Lấy chi tiết đợt khám",
        Description = "Trả về thông tin cấu hình, trạng thái và thời gian của một đợt khám theo mã đợt.")]
    public async Task<ActionResult<ResultData<ExamSessionItem>>> Get(Guid sessionId, CancellationToken ct = default)
    {
        var result = await _getHandler.HandleAsync(
            new GetExamSessionQuery(HealthExamContext.DivisionId, sessionId), ct);

        if (!result.IsSuccess)
        {
            return ToActionResult(ApplicationResult<ExamSessionItem>.Fail(
                result.Failure.Code, result.Failure.Message, result.Failure.Payload));
        }

        return ToActionResult(ApplicationResult<ExamSessionItem>.Success(MapToItem(result.Value)));
    }

    [HttpPost]
    [SwaggerOperation(
        Summary = "Tạo đợt khám",
        Description = "Tạo một đợt khám mới ở trạng thái Nháp để chuẩn bị tiếp nhận hồ sơ đăng ký.")]
    public async Task<ActionResult<ResultData<ExamSessionItem>>> Create(
        [FromBody] ExamSessionSaveRequest request, CancellationToken ct = default)
    {
        request ??= new ExamSessionSaveRequest();
        var command = new CreateExamSessionCommand(
            HealthExamContext.DivisionId,
            HealthExamContext.ActorId.ToString(),
            HealthExamContext.ActorKind,
            request.SessionCode,
            request.SessionName,
            request.OrganizationID,
            request.ContractNo,
            request.ContractDate,
            request.ExamDate,
            request.ExamDateTo,
            request.ExamPlace,
            request.DepartmentID,
            request.PackageID,
            request.VariantCode,
            request.ExpectedCount,
            request.Note,
            request.ExtraFields?.Keys);

        var result = await _createHandler.HandleAsync(command, ct);

        if (!result.IsSuccess)
        {
            return ToActionResult(ApplicationResult<ExamSessionItem>.Fail(
                result.Failure.Code, result.Failure.Message, result.Failure.Payload));
        }

        return ToActionResult(ApplicationResult<ExamSessionItem>.Success(MapToItem(result.Value)));
    }

    [HttpPut("{sessionId:guid}")]
    [SwaggerOperation(
        Summary = "Cập nhật đợt khám",
        Description = "Cập nhật thông tin đợt khám theo mã đợt; các quy tắc trạng thái và quyền ghi được server kiểm tra.")]
    public async Task<ActionResult<ResultData<ExamSessionItem>>> Update(
        Guid sessionId, [FromBody] ExamSessionSaveRequest request, CancellationToken ct = default)
    {
        request ??= new ExamSessionSaveRequest();
        var command = new UpdateExamSessionCommand(
            HealthExamContext.DivisionId,
            sessionId,
            HealthExamContext.ActorId.ToString(),
            HealthExamContext.ActorKind,
            request.SessionCode,
            request.SessionName,
            request.OrganizationID,
            request.ContractNo,
            request.ContractDate,
            request.ExamDate,
            request.ExamDateTo,
            request.ExamPlace,
            request.DepartmentID,
            request.PackageID,
            request.VariantCode,
            request.ExpectedCount,
            request.Note,
            request.ExtraFields?.Keys);

        var result = await _updateHandler.HandleAsync(command, ct);

        if (!result.IsSuccess)
        {
            return ToActionResult(ApplicationResult<ExamSessionItem>.Fail(
                result.Failure.Code, result.Failure.Message, result.Failure.Payload));
        }

        return ToActionResult(ApplicationResult<ExamSessionItem>.Success(MapToItem(result.Value)));
    }

    /// <summary>
    /// Đóng đợt khám — khoá mọi đường ghi thuộc đợt.
    ///
    /// Hành động riêng, KHÔNG phải `PUT State`: đóng sổ một đợt khám và sửa tên đợt không được
    /// đi chung một đường quyền, một dòng audit (02-api-spec §3.1).
    /// </summary>
    [HttpPost("{sessionId:guid}/close")]
    [SwaggerOperation(
        Summary = "Đóng đợt khám",
        Description = "Chuyển đợt khám sang trạng thái đóng và khóa các thao tác ghi hồ sơ thuộc đợt; lý do đóng được lưu audit nếu có.")]
    public async Task<ActionResult<ResultData<ExamSessionItem>>> Close(
        Guid sessionId, [FromBody] ExamSessionCloseRequest request = null, CancellationToken ct = default)
    {
        var command = new CloseExamSessionCommand(
            HealthExamContext.DivisionId,
            sessionId,
            HealthExamContext.ActorId.ToString(),
            HealthExamContext.ActorKind,
            HealthExamContext.TraceId,
            request?.Reason);

        var result = await _closeHandler.HandleAsync(command, ct);

        if (!result.IsSuccess)
        {
            return ToActionResult(ApplicationResult<ExamSessionItem>.Fail(
                result.Failure.Code, result.Failure.Message, result.Failure.Payload));
        }

        return ToActionResult(ApplicationResult<ExamSessionItem>.Success(MapToItem(result.Value)));
    }

    /// <summary>Mở lại đợt đã đóng. Body <c>{ "Reason": "…" }</c> — BẮT BUỘC, vào audit.</summary>
    [HttpPost("{sessionId:guid}/reopen")]
    [SwaggerOperation(
        Summary = "Mở lại đợt khám",
        Description = "Mở lại một đợt đã đóng để tiếp tục nghiệp vụ; body lý do mở lại là bắt buộc và được ghi audit.")]
    public async Task<ActionResult<ResultData<ExamSessionItem>>> Reopen(
        Guid sessionId, [FromBody] ExamSessionReopenRequest request = null, CancellationToken ct = default)
    {
        var command = new ReopenExamSessionCommand(
            HealthExamContext.DivisionId,
            sessionId,
            HealthExamContext.ActorId.ToString(),
            HealthExamContext.ActorKind,
            HealthExamContext.TraceId,
            request?.Reason);

        var result = await _reopenHandler.HandleAsync(command, ct);

        if (!result.IsSuccess)
        {
            return ToActionResult(ApplicationResult<ExamSessionItem>.Fail(
                result.Failure.Code, result.Failure.Message, result.Failure.Payload));
        }

        return ToActionResult(ApplicationResult<ExamSessionItem>.Success(MapToItem(result.Value)));
    }

    /// <summary>
    /// Danh sách người trong đợt, có phân trang.
    ///
    /// Đường lồng này tồn tại song song với GET /v1/exam-records?sessionId=… vì màn "chi tiết
    /// đợt" luôn biết mã đợt, còn màn tra cứu ở quầy thì xuất phát từ người bệnh
    /// (02-api-spec §3.2). Cả hai gọi cùng một hàm, không có nhánh logic thứ hai.
    /// </summary>
    [HttpGet("{sessionId:guid}/records")]
    [SwaggerOperation(
        Summary = "Tra cứu hồ sơ trong đợt khám",
        Description = "Trả về hồ sơ thuộc một đợt khám với phân trang và bộ lọc từ khóa, trạng thái, mã biến thể; đợt không tồn tại trả lỗi.")]
    public async Task<ActionResult<ResultData<PaginationData<ExamRecordItem>>>> Records(
        Guid sessionId,
        [FromQuery] string keyword = null,
        [FromQuery] short? state = null,
        [FromQuery] string variantCode = null,
        [FromQuery] int page = 1, [FromQuery] int size = 20, CancellationToken ct = default)
    {
        var sessionResult = await _getHandler.HandleAsync(
            new GetExamSessionQuery(HealthExamContext.DivisionId, sessionId), ct);

        if (!sessionResult.IsSuccess)
        {
            return ToActionResult(ApplicationResult<PaginationData<ExamRecordItem>>.Fail(
                sessionResult.Failure.Code, sessionResult.Failure.Message, sessionResult.Failure.Payload));
        }

        var filter = new ExamRecordFilter(
            SessionID: sessionId,
            Keyword: keyword,
            State: state,
            VariantCode: variantCode,
            RecordCode: null,
            PatientCode: null,
            FullName: null,
            IdentityNumber: null,
            PhoneNumber: null,
            From: null,
            To: null,
            Page: page,
            Size: size);

        var result = await _listRecordsHandler.HandleAsync(
            new ListExamRecordsQuery(HealthExamContext.DivisionId, filter), ct);

        if (!result.IsSuccess)
        {
            return ToActionResult(ApplicationResult<PaginationData<ExamRecordItem>>.Fail(
                result.Failure.Code, result.Failure.Message, result.Failure.Payload));
        }

        var pageResult = result.Value;
        var items = pageResult.Items.Select(ExamRecordItem.From).ToList();
        var paginated = new PaginationData<ExamRecordItem>(items, pageResult.Page, pageResult.Size, pageResult.Total);
        return ToActionResult(ApplicationResult<PaginationData<ExamRecordItem>>.Success(paginated));
    }

    /// <summary>
    /// Tiến độ toàn đợt — 02-api-spec §3.1.
    ///
    /// Đọc HOÀN TOÀN từ cache do webhook cập nhật, KHÔNG gọi form-server lần nào: đây là màn
    /// hình người dùng để mở suốt buổi khám và bấm làm mới liên tục, một đợt 320 người mà mỗi
    /// lần lại bắn 320 request sang form-server thì cả hai service cùng chết.
    ///
    /// Vì thế phản hồi luôn kèm UpdatedAt — số liệu này là ẢNH CHỤP bất đồng bộ, và FE phải
    /// nói được cho người dùng biết ảnh chụp lúc nào.
    /// </summary>
    [HttpGet("{sessionId:guid}/progress")]
    [SwaggerOperation(
        Summary = "Xem tiến độ đợt khám",
        Description = "Đọc số liệu tiến độ đã tổng hợp trong cache từ các sự kiện; phản hồi kèm thời điểm cập nhật và không gọi form-server trực tiếp.")]
    public async Task<ActionResult<ResultData<ExamSessionProgressItem>>> Progress(
        Guid sessionId, CancellationToken ct = default)
    {
        var result = await _progressHandler.HandleAsync(
            new GetExamSessionProgressQuery(HealthExamContext.DivisionId, sessionId), ct);

        if (!result.IsSuccess)
        {
            return ToActionResult(ApplicationResult<ExamSessionProgressItem>.Fail(
                result.Failure.Code, result.Failure.Message, result.Failure.Payload));
        }

        var progressItem = new ExamSessionProgressItem
        {
            SessionID = result.Value.SessionID,
            SessionCode = result.Value.SessionCode,
            State = result.Value.State,
            Profiles = new ExamSessionProgressCounts
            {
                Total = result.Value.Profiles.Total,
                NotRegistered = result.Value.Profiles.NotRegistered,
                Waiting = result.Value.Profiles.Waiting,
                InProgress = result.Value.Profiles.InProgress,
                Completed = result.Value.Profiles.Completed,
                Cancelled = result.Value.Profiles.Cancelled,
                RegistrationCancelled = result.Value.Profiles.RegistrationCancelled,
                ExamCancelled = result.Value.Profiles.ExamCancelled
            },
            ConclusionReady = result.Value.ConclusionReady,
            UpdatedAt = result.Value.UpdatedAt
        };

        return ToActionResult(ApplicationResult<ExamSessionProgressItem>.Success(progressItem));
    }

    private static ExamSessionItem MapToItem(ExamSessionResult x) => new()
    {
        SessionID = x.SessionID,
        SessionCode = x.SessionCode,
        SessionName = x.SessionName,
        OrganizationID = x.OrganizationID,
        OrganizationName = x.OrganizationName,
        ContractNo = x.ContractNo,
        ContractDate = x.ContractDate,
        ExamDate = x.ExamDate,
        ExamDateTo = x.ExamDateTo,
        ExamPlace = x.ExamPlace,
        PackageID = x.PackageID,
        PackageName = x.PackageName,
        VariantCode = x.VariantCode,
        State = x.State,
        StateName = x.StateName,
        ExpectedCount = x.ExpectedCount,
        RecordCount = x.RecordCount,
        Note = x.Note,
        IsActive = x.IsActive
    };
}
