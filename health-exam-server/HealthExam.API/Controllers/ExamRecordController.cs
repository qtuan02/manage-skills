using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HealthExam.Application.Paraclinical;
using HealthExam.API.Contracts;
using HealthExam.Application.Common;
using HealthExam.Application.ExamRecords;
using HealthExam.Application.His;
using HealthExam.Application.Integrations;
using HealthExam.Application.Signing;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Swashbuckle.AspNetCore.Annotations;
using ExamRecordCancelRequest = HealthExam.API.Contracts.ExamRecordCancelRequest;
using ExamRecordFilterOptions = HealthExam.API.Contracts.ExamRecordFilterOptions;
using ExamRecordItem = HealthExam.API.Contracts.ExamRecordItem;
using ExamRecordWriteRequest = HealthExam.API.Contracts.ExamRecordWriteRequest;

namespace HealthExam.API.Controllers;

/// <summary>
/// Hồ sơ người khám — 02-api-spec §3.2.
/// </summary>
[Route("v1/exam-records")]
public class ExamRecordController : HealthExamControllerBase
{
    private readonly IListExamRecordsHandler _listHandler;
    private readonly IGetExamRecordHandler _getHandler;
    private readonly ICreateExamRecordHandler _createHandler;
    private readonly IUpdateExamRecordHandler _updateHandler;
    private readonly IConfirmExamRecordHandler _confirmHandler;
    private readonly ICancelExamRecordHandler _cancelHandler;
    private readonly IGetExamFormDraftHandler _formDraft;
    private readonly IGetRecordOrdersHandler _getRecordOrdersHandler;
    private readonly ICreateOrdersHandler _createOrdersHandler;
    private readonly ICreateOrdersFromPackageHandler _createOrdersFromPackageHandler;
    private readonly IGetConclusionEligibilityHandler _getConclusionEligibilityHandler;
    private readonly ISignConclusionHandler _signConclusionHandler;
    private readonly ICancelConclusionSignHandler _cancelConclusionSignHandler;
    private readonly ISignExamSectionHandler _signExamSectionHandler;
    private readonly ICancelExamSectionSignHandler _cancelExamSectionSignHandler;
    private readonly IHisCredentialOptions _credentialOptions;
    private readonly IHisEmrClient _hisClient;

    public ExamRecordController(
        IListExamRecordsHandler listHandler,
        IGetExamRecordHandler getHandler,
        ICreateExamRecordHandler createHandler,
        IUpdateExamRecordHandler updateHandler,
        IConfirmExamRecordHandler confirmHandler,
        ICancelExamRecordHandler cancelHandler,
        IGetExamFormDraftHandler formDraft,
        IGetRecordOrdersHandler getRecordOrdersHandler,
        ICreateOrdersHandler createOrdersHandler,
        ICreateOrdersFromPackageHandler createOrdersFromPackageHandler,
        IGetConclusionEligibilityHandler getConclusionEligibilityHandler,
        ISignConclusionHandler signConclusionHandler,
        ICancelConclusionSignHandler cancelConclusionSignHandler,
        ISignExamSectionHandler signExamSectionHandler,
        ICancelExamSectionSignHandler cancelExamSectionSignHandler,
        IHisCredentialOptions credentialOptions,
        IHisEmrClient hisClient)
    {
        _listHandler = listHandler;
        _getHandler = getHandler;
        _createHandler = createHandler;
        _updateHandler = updateHandler;
        _confirmHandler = confirmHandler;
        _cancelHandler = cancelHandler;
        _formDraft = formDraft;
        _getRecordOrdersHandler = getRecordOrdersHandler;
        _createOrdersHandler = createOrdersHandler;
        _createOrdersFromPackageHandler = createOrdersFromPackageHandler;
        _getConclusionEligibilityHandler = getConclusionEligibilityHandler;
        _signConclusionHandler = signConclusionHandler;
        _cancelConclusionSignHandler = cancelConclusionSignHandler;
        _signExamSectionHandler = signExamSectionHandler;
        _cancelExamSectionSignHandler = cancelExamSectionSignHandler;
        _credentialOptions = credentialOptions;
        _hisClient = hisClient;
    }

    private string Credential => Request.Headers[_credentialOptions.CredentialHeaderName].ToString();

    [HttpGet]
    [SwaggerOperation(
        Summary = "Tra cứu hồ sơ khám sức khỏe",
        Description = "Trả về danh sách hồ sơ mới tạo trước, hỗ trợ lọc theo mã KSK, mã người bệnh, tên, số điện thoại, CCCD, trạng thái, đối tượng khám, ngày tạo hồ sơ và đợt khám.")]
    public async Task<ActionResult<ResultData<PaginationData<ExamRecordItem>>>> List(
        [FromQuery] Guid? sessionId = null,
        [FromQuery] string keyword = null,
        [FromQuery] short? state = null,
        [FromQuery] string variantCode = null,
        [FromQuery] string recordCode = null,
        [FromQuery] string patientCode = null,
        [FromQuery] string fullName = null,
        [FromQuery] string identityNumber = null,
        [FromQuery] string phoneNumber = null,
        [FromQuery] DateOnly? fromDate = null,
        [FromQuery] DateOnly? toDate = null,
        [FromQuery] int page = 1, [FromQuery] int size = 20, CancellationToken ct = default)
    {
        var filter = new ExamRecordFilter(
            SessionID: sessionId,
            Keyword: keyword,
            State: state,
            VariantCode: variantCode,
            RecordCode: recordCode,
            PatientCode: patientCode,
            FullName: fullName,
            IdentityNumber: identityNumber,
            PhoneNumber: phoneNumber,
            From: fromDate,
            To: toDate,
            Page: page,
            Size: size);

        var result = await _listHandler.HandleAsync(
            new ListExamRecordsQuery(HealthExamContext.DivisionId, filter), ct);

        if (!result.IsSuccess)
        {
            return ToActionResult(ApplicationResult<PaginationData<ExamRecordItem>>.Fail(
                result.Failure.Code, result.Failure.Message, result.Failure.Payload));
        }

        var pageResult = result.Value;
        var items = pageResult.Items.Select(MapToItem).ToList();
        var paginated = new PaginationData<ExamRecordItem>(items, pageResult.Page, pageResult.Size, pageResult.Total);
        return ToActionResult(ApplicationResult<PaginationData<ExamRecordItem>>.Success(paginated));
    }

    /// <summary>Danh sách option tĩnh cho các bộ lọc trên màn danh sách hồ sơ.</summary>
    [HttpGet("filter-options")]
    [SwaggerOperation(
        Summary = "Lấy tùy chọn bộ lọc hồ sơ",
        Description = "Trả về các option tĩnh như trạng thái hồ sơ và đối tượng khám để hiển thị bộ lọc trên màn tra cứu.")]
    public ActionResult<ResultData<ExamRecordFilterOptions>> FilterOptions()
        => Success(ExamRecordFilterOptions.Create());

    [HttpGet("{recordId:guid}")]
    [SwaggerOperation(
        Summary = "Lấy chi tiết hồ sơ khám",
        Description = "Trả về toàn bộ thông tin hành chính và trạng thái của một hồ sơ theo mã hồ sơ.")]
    public async Task<ActionResult<ResultData<ExamRecordItem>>> Get(Guid recordId, CancellationToken ct = default)
    {
        var result = await _getHandler.HandleAsync(
            new GetExamRecordQuery(HealthExamContext.DivisionId, recordId), ct);

        if (!result.IsSuccess)
        {
            return ToActionResult(ApplicationResult<ExamRecordItem>.Fail(
                result.Failure.Code, result.Failure.Message, result.Failure.Payload));
        }

        return ToActionResult(ApplicationResult<ExamRecordItem>.Success(MapToItem(result.Value)));
    }

    /// <summary>Bước 1 wizard — hành chính. Hồ sơ ra đời ở trạng thái Chờ khám(1).</summary>
    [HttpPost]
    [SwaggerOperation(
        Summary = "Tạo hồ sơ khám sức khỏe",
        Description = "Tạo hồ sơ hành chính mới ở trạng thái Chờ khám (Waiting). Phase 1 không nhận sessionId; backend tự gắn hoặc tái sử dụng đợt mặc định PHASE1-DEFAULT.")]
    public async Task<ActionResult<ResultData<ExamRecordItem>>> Create(
        [FromBody] ExamRecordWriteRequest request, CancellationToken ct = default)
    {
        request ??= new ExamRecordWriteRequest();
        var command = new CreateExamRecordCommand(
            DivisionId: HealthExamContext.DivisionId,
            ActorId: HealthExamContext.ActorId.ToString(),
            ActorKind: HealthExamContext.ActorKind,
            SessionID: null,
            RecordCode: request.RecordCode,
            PatientID: request.PatientID,
            AdmissionID: request.AdmissionID,
            PatientCode: request.PatientCode,
            FullName: request.FullName,
            Dob: request.Dob,
            BirthYear: request.BirthYear,
            GenderID: request.GenderID,
            IdentityNumber: request.IdentityNumber,
            InsuranceNumber: request.InsuranceNumber,
            PhoneNumber: request.PhoneNumber,
            Email: request.Email,
            Address: request.Address,
            StaffCode: request.StaffCode,
            OrgDeptName: request.OrgDeptName,
            JobTitle: request.JobTitle,
            VariantCode: request.VariantCode,
            PackageID: request.PackageID,
            Note: request.Note,
            EthnicityCode: request.EthnicityCode,
            OccupationCode: request.OccupationCode,
            BloodAboCode: request.BloodAboCode,
            BloodRhCode: request.BloodRhCode,
            ProvinceCode: request.ProvinceCode,
            WardCode: request.WardCode,
            IdentityIssuedDate: request.IdentityIssuedDate,
            IdentityIssuerCode: request.IdentityIssuerCode,
            RelativeRelationshipCode: request.RelativeRelationshipCode,
            RelativeFullName: request.RelativeFullName,
            RelativeIdentityNumber: request.RelativeIdentityNumber,
            RelativePhoneNumber: request.RelativePhoneNumber,
            InsuranceObjectCode: request.InsuranceObjectCode,
            InsuranceValidFrom: request.InsuranceValidFrom,
            InsuranceValidTo: request.InsuranceValidTo,
            ExamReason: request.ExamReason,
            PatientTypeCode: request.PatientTypeCode,
            PatientSubjectCode: request.PatientSubjectCode,
            PaymentSourceCode: request.PaymentSourceCode,
            PaymentSourceOther: request.PaymentSourceOther,
            ExamLocationCode: request.ExamLocationCode,
            RegistrationPlaceCode: request.RegistrationPlaceCode,
            TraceId: HealthExamContext.TraceId,
            Credential: Credential,
            PatientRefID: request.PatientRefID,
            SetAsActiveProfile: request.SetAsActiveProfile);

        var result = await _createHandler.HandleAsync(command, ct);
        if (!result.IsSuccess)
        {
            return ToActionResult(ApplicationResult<ExamRecordItem>.Fail(
                result.Failure.Code, result.Failure.Message, result.Failure.Payload));
        }

        return ToActionResult(ApplicationResult<ExamRecordItem>.Success(MapToItem(result.Value)));
    }

    [HttpPut("{recordId:guid}")]
    [SwaggerOperation(
        Summary = "Cập nhật hồ sơ khám",
        Description = "Cập nhật thông tin hành chính của hồ sơ; phase 1 không nhận sessionId và server giữ liên kết đợt mặc định hiện tại.")]
    public async Task<ActionResult<ResultData<ExamRecordItem>>> Update(
        Guid recordId, [FromBody] ExamRecordWriteRequest request, CancellationToken ct = default)
    {
        request ??= new ExamRecordWriteRequest();
        var command = new UpdateExamRecordCommand(
            DivisionId: HealthExamContext.DivisionId,
            RecordId: recordId,
            ActorId: HealthExamContext.ActorId.ToString(),
            ActorKind: HealthExamContext.ActorKind,
            RecordCode: request.RecordCode,
            PatientID: request.PatientID,
            AdmissionID: request.AdmissionID,
            PatientCode: request.PatientCode,
            FullName: request.FullName,
            Dob: request.Dob,
            BirthYear: request.BirthYear,
            GenderID: request.GenderID,
            IdentityNumber: request.IdentityNumber,
            InsuranceNumber: request.InsuranceNumber,
            PhoneNumber: request.PhoneNumber,
            Email: request.Email,
            Address: request.Address,
            StaffCode: request.StaffCode,
            OrgDeptName: request.OrgDeptName,
            JobTitle: request.JobTitle,
            VariantCode: request.VariantCode,
            PackageID: request.PackageID,
            Note: request.Note,
            EthnicityCode: request.EthnicityCode,
            OccupationCode: request.OccupationCode,
            BloodAboCode: request.BloodAboCode,
            BloodRhCode: request.BloodRhCode,
            ProvinceCode: request.ProvinceCode,
            WardCode: request.WardCode,
            IdentityIssuedDate: request.IdentityIssuedDate,
            IdentityIssuerCode: request.IdentityIssuerCode,
            RelativeRelationshipCode: request.RelativeRelationshipCode,
            RelativeFullName: request.RelativeFullName,
            RelativeIdentityNumber: request.RelativeIdentityNumber,
            RelativePhoneNumber: request.RelativePhoneNumber,
            InsuranceObjectCode: request.InsuranceObjectCode,
            InsuranceValidFrom: request.InsuranceValidFrom,
            InsuranceValidTo: request.InsuranceValidTo,
            ExamReason: request.ExamReason,
            PatientTypeCode: request.PatientTypeCode,
            PatientSubjectCode: request.PatientSubjectCode,
            PaymentSourceCode: request.PaymentSourceCode,
            PaymentSourceOther: request.PaymentSourceOther,
            ExamLocationCode: request.ExamLocationCode,
            RegistrationPlaceCode: request.RegistrationPlaceCode,
            TraceId: HealthExamContext.TraceId,
            PatientRefID: request.PatientRefID,
            SetAsActiveProfile: request.SetAsActiveProfile);

        var result = await _updateHandler.HandleAsync(command, ct);
        if (!result.IsSuccess)
        {
            return ToActionResult(ApplicationResult<ExamRecordItem>.Fail(
                result.Failure.Code, result.Failure.Message, result.Failure.Payload));
        }

        return ToActionResult(ApplicationResult<ExamRecordItem>.Success(MapToItem(result.Value)));
    }

    /// <summary>
    /// Bước 2 wizard — lấy biểu mẫu KSK thật của hồ sơ từ form-server: layout + giá trị
    /// prefill, kèm FormID đã chốt vào hồ sơ.
    /// </summary>
    [HttpGet("{recordId:guid}/form-draft")]
    [SwaggerOperation(
        Summary = "Mở bản nháp biểu mẫu khám",
        Description = "Resolve FormID và lấy layout cùng dữ liệu prefill từ form-server cho bước điền phiếu; có thể gọi lại an toàn và trả cùng biểu mẫu đã chốt.")]
    public async Task<ActionResult<ResultData<ExamFormDraftResult>>> FormDraft(
        Guid recordId, CancellationToken ct = default)
    {
        var result = await _formDraft.HandleAsync(
            new GetExamFormDraftQuery(HealthExamContext.DivisionId, recordId, HealthExamContext.ActorId.ToString(), HealthExamContext.ActorKind), ct);
        return ToActionResult(result);
    }

    /// <summary>Chốt đăng ký tại quầy: Chưa đăng ký(0) → Chờ khám(1).</summary>
    [HttpPost("{recordId:guid}/confirm")]
    [SwaggerOperation(
        Summary = "Xác nhận đăng ký hồ sơ",
        Description = "Chốt hồ sơ tại quầy và chuyển trạng thái từ Chưa đăng ký (0) sang Chờ khám (1).")]
    public async Task<ActionResult<ResultData<ExamRecordItem>>> Confirm(Guid recordId, CancellationToken ct = default)
    {
        var command = new ConfirmExamRecordCommand(
            HealthExamContext.DivisionId,
            recordId,
            HealthExamContext.ActorId.ToString(),
            HealthExamContext.ActorKind,
            HealthExamContext.TraceId);

        var result = await _confirmHandler.HandleAsync(command, ct);
        if (!result.IsSuccess)
        {
            return ToActionResult(ApplicationResult<ExamRecordItem>.Fail(
                result.Failure.Code, result.Failure.Message, result.Failure.Payload));
        }

        return ToActionResult(ApplicationResult<ExamRecordItem>.Success(MapToItem(result.Value)));
    }

    /// <summary>Hủy đăng ký / Hủy khám: 0 → 4, 1/2 → 5.</summary>
    [HttpPost("{recordId:guid}/cancel")]
    [SwaggerOperation(
        Summary = "Hủy hồ sơ hoặc lượt khám",
        Description = "Hủy hồ sơ theo trạng thái hiện tại: 0 chuyển 4, 1 hoặc 2 chuyển 5; lý do hủy nằm trong request và được lưu lại.")]
    public async Task<ActionResult<ResultData<ExamRecordItem>>> Cancel(
        Guid recordId, [FromBody] ExamRecordCancelRequest request, CancellationToken ct = default)
    {
        var command = new CancelExamRecordCommand(
            HealthExamContext.DivisionId,
            recordId,
            HealthExamContext.ActorId.ToString(),
            HealthExamContext.ActorKind,
            request?.Reason,
            HealthExamContext.TraceId);

        var result = await _cancelHandler.HandleAsync(command, ct);
        if (!result.IsSuccess)
        {
            return ToActionResult(ApplicationResult<ExamRecordItem>.Fail(
                result.Failure.Code, result.Failure.Message, result.Failure.Payload));
        }

        return ToActionResult(ApplicationResult<ExamRecordItem>.Success(MapToItem(result.Value)));
    }

    // ─────────────────── Bước 3 wizard: chỉ định CLS — 02-api-spec §3.4 ───────────────────

    [HttpGet("{recordId:guid}/orders")]
    [SwaggerOperation(
        Summary = "Lấy các chỉ định cận lâm sàng của hồ sơ",
        Description = "Trả về các phiếu chỉ định thuộc hồ sơ; có thể chọn bao gồm hoặc loại các phiếu đã hủy bằng includeCancelled.")]
    public async Task<ActionResult<ResultData<IReadOnlyList<ParaclinicalOrderResult>>>> Orders(
        Guid recordId, [FromQuery] bool includeCancelled = true, CancellationToken ct = default)
    {
        var res = await _getRecordOrdersHandler.HandleAsync(
            new GetRecordOrdersQuery(HealthExamContext.DivisionId, recordId, includeCancelled), ct);
        return ToActionResult(res);
    }

    /// <summary>Chỉ định TAY. Nhiều loại CLS trong một lần bấm ⇒ nhiều phiếu, xem service.</summary>
    [HttpPost("{recordId:guid}/orders")]
    [SwaggerOperation(
        Summary = "Tạo chỉ định cận lâm sàng thủ công",
        Description = "Tạo một hoặc nhiều phiếu chỉ định từ các dịch vụ được chọn trực tiếp trong hồ sơ.")]
    public async Task<ActionResult<ResultData<IReadOnlyList<ParaclinicalOrderResult>>>> CreateOrders(
        Guid recordId, [FromBody] ParaclinicalOrderCreateRequest request, CancellationToken ct = default)
    {
        var cmd = new CreateOrdersCommand(
            HealthExamContext.DivisionId,
            HealthExamContext.ActorId.ToString(),
            HealthExamContext.ActorName,
            HealthExamContext.ActorKind,
            recordId,
            request?.ServiceIDs,
            request?.Note,
            request?.RoomID ?? 0);
        var res = await _createOrdersHandler.HandleAsync(cmd, ct);
        return ToActionResult(res);
    }

    /// <summary>
    /// Bung GÓI KHÁM thành chỉ định. Tách khỏi POST /orders vì phải truy được về sau chỉ định
    /// nào do gói nào bung ra (SourcePackageID) — đối soát chi phí với đơn vị ký hợp đồng cần
    /// đúng thông tin đó.
    /// </summary>
    [HttpPost("{recordId:guid}/orders/from-package")]
    [SwaggerOperation(
        Summary = "Tạo chỉ định từ gói khám",
        Description = "Bung các dịch vụ trong gói khám thành phiếu chỉ định và lưu nguồn gói để phục vụ đối soát sau này.")]
    public async Task<ActionResult<ResultData<IReadOnlyList<ParaclinicalOrderResult>>>> CreateOrdersFromPackage(
        Guid recordId, [FromBody] ParaclinicalOrderFromPackageRequest request, CancellationToken ct = default)
    {
        var cmd = new CreateOrdersFromPackageCommand(
            HealthExamContext.DivisionId,
            HealthExamContext.ActorId.ToString(),
            HealthExamContext.ActorName,
            HealthExamContext.ActorKind,
            recordId,
            request?.PackageID,
            request?.Note,
            request?.RoomID ?? 0);
        var res = await _createOrdersFromPackageHandler.HandleAsync(cmd, ct);
        return ToActionResult(res);
    }

    // ─────────────────── Điều kiện ký kết luận — 02-api-spec §3.7 ─────────────────────────

    /// <summary>
    /// AND điều kiện (A) và (B). CHỈ để bật/tắt nút — nó không chặn được gì; chốt chặn thật
    /// nằm ở POST bên dưới, nơi server tính lại.
    /// </summary>
    [HttpGet("{recordId:guid}/conclusion-eligibility")]
    [SwaggerOperation(
        Summary = "Kiểm tra điều kiện ký kết luận",
        Description = "Tính và trả về các điều kiện cần để ký kết luận, dùng cho hiển thị nút trên giao diện; đây không phải chốt chặn cuối cùng.")]
    public async Task<ActionResult<ResultData<ConclusionEligibilityResult>>> ConclusionEligibility(
        Guid recordId, CancellationToken ct = default)
    {
        // Spec §6.3: eligibility chỉ đọc DB. HIS không trả được vai trò thì xuống cấp
        // (CanSignConclusion=false) chứ không 502 — FE vẫn cần Steps để vẽ tiến độ.
        var roleIds = await ResolveSignRoleIdsOrEmptyAsync(ct);

        var res = await _getConclusionEligibilityHandler.HandleAsync(
            new GetConclusionEligibilityQuery(
                DivisionId: HealthExamContext.DivisionId,
                RecordId: recordId,
                Credential: Credential,
                TraceId: HealthExamContext.TraceId,
                ActorId: HealthExamContext.ActorId.ToString(),
                ActorKind: HealthExamContext.ActorKind,
                DepartmentId: int.TryParse(User.FindFirst("DepartmentID")?.Value, out var departmentId) ? departmentId : 0,
                RoleIds: roleIds), ct);
        return ToActionResult(res);
    }

    /// <summary>
    /// Phát lệnh ký kết luận. Server TÍNH LẠI (A) AND (B), không tin kết quả FE vừa đọc:
    /// giữa hai lời gọi một chỉ định CLS có thể vừa bị huỷ hoặc vừa được thêm.
    /// </summary>
    [HttpPost("{recordId:guid}/conclusion/sign")]
    [SwaggerOperation(
        Summary = "Ký kết luận khám",
        Description = "Yêu cầu server ký kết luận sau khi tính lại điều kiện tại thời điểm gọi, không sử dụng mù kết quả eligibility đã đọc trước đó.")]
    public async Task<ActionResult<ResultData<HealthExam.Application.Paraclinical.ConclusionSignResult>>> SignConclusion(
        Guid recordId, CancellationToken ct = default)
    {
        // Spec §6.4: body rỗng. KHÔNG khai [FromBody] — [ApiController] sẽ trả 415 cho POST
        // không Content-Type (axios gửi undefined) trước khi handler kịp chạy.
        var rolesRes = await ResolveSignRoleIdsAsync(ct);
        if (!rolesRes.IsSuccess)
            return ToActionResult(ApplicationResult<HealthExam.Application.Paraclinical.ConclusionSignResult>.Fail(ApplicationFailureCode.HisBadGateway, rolesRes.Message));

        var cmd = new SignConclusionCommand(
            HealthExamContext.DivisionId,
            HealthExamContext.ActorId.ToString(),
            HealthExamContext.ActorName,
            HealthExamContext.ActorKind,
            recordId,
            Credential,
            HealthExamContext.TraceId,
            HealthExamContext.ActorCode,
            rolesRes.Value,
            int.TryParse(User.FindFirst("DepartmentID")?.Value, out var departmentId) ? departmentId : 0);
        var res = await _signConclusionHandler.HandleAsync(cmd, ct);
        return ToActionResult(res);
    }

    [HttpPost("{recordId:guid}/conclusion/sign/cancel")]
    [SwaggerOperation(
        Summary = "Hủy ký kết luận khám",
        Description = "Hủy chữ ký kết luận, trả trạng thái hồ sơ về đang khám (InProgress) và mở khóa các mục để sửa.")]
    public async Task<ActionResult<ResultData<HealthExam.Application.Paraclinical.ConclusionSignResult>>> CancelConclusionSign(
        Guid recordId, CancellationToken ct = default)
    {
        var cmd = new CancelConclusionSignCommand(
            HealthExamContext.DivisionId,
            HealthExamContext.ActorId.ToString(),
            HealthExamContext.ActorKind,
            recordId);
        var res = await _cancelConclusionSignHandler.HandleAsync(cmd, ct);
        return ToActionResult(res);
    }

    [HttpPost("{recordId:guid}/sections/{itemGroupId:int}/sign")]
    [SwaggerOperation(
        Summary = "Ký số một mục khám",
        Description = "Ghi nhận người ký (Người xác nhận được chọn, hoặc chính người bấm nếu không chọn) và khóa mục đó. Chữ ký chỉ được đóng lên PDF ở bước ký kết luận.")]
    public async Task<ActionResult<ResultData<ExamSectionSignResult>>> SignExamSection(
        Guid recordId, int itemGroupId,
        // EmptyBodyBehavior.Allow: client cũ POST không body/không Content-Type vẫn vào handler thay vì 415.
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] ExamSectionSignRequest request,
        CancellationToken ct = default)
    {
        var res = await _signExamSectionHandler.HandleAsync(new SignExamSectionCommand(
            HealthExamContext.DivisionId,
            recordId,
            itemGroupId,
            HealthExamContext.ActorId,
            HealthExamContext.ActorCode,
            HealthExamContext.ActorName,
            HealthExamContext.ActorKind,
            Credential,
            HealthExamContext.TraceId,
            request?.ConfirmedByEmployeeID,
            request?.SignedAt?.UtcDateTime), ct);
        return ToActionResult(res);
    }

    [HttpPost("{recordId:guid}/sections/{itemGroupId:int}/sign/cancel")]
    [SwaggerOperation(
        Summary = "Hủy ký một mục khám",
        Description = "Xóa chữ ký đã ghi nhận và mở khóa mục khám để chỉnh sửa.")]
    public async Task<ActionResult<ResultData<bool>>> CancelExamSectionSign(
        Guid recordId, int itemGroupId, CancellationToken ct = default)
    {
        var res = await _cancelExamSectionSignHandler.HandleAsync(new CancelExamSectionSignCommand(
            HealthExamContext.DivisionId, recordId, itemGroupId,
            HealthExamContext.ActorId, HealthExamContext.ActorKind), ct);
        return ToActionResult(res);
    }

    /// <summary>
    /// Vai trò ký của nhân viên hiện tại, tra từ HIS (M02F30000/GetPermissionGroup). Từ PROJ-2374,
    /// SignExamSection không còn gọi hàm này — nó tự tra danh sách người ký của bước qua
    /// <see cref="IHisEmrClient.ListSignRoleEmployeesAsync"/>. Bản này giờ chỉ còn dùng cho
    /// SignConclusion (fail-closed khi HIS lỗi: ký kết luận không được chạy với vai trò chưa xác
    /// định) và, qua bản OrEmpty bên dưới, cho ConclusionEligibility.
    /// </summary>
    private Task<HisClientResult<IReadOnlyList<long>>> ResolveSignRoleIdsAsync(CancellationToken ct)
        => _hisClient.ListEmployeeSignRoleIdsAsync(
            HealthExamContext.ActorId,
            new HisCallContext(Credential, HealthExamContext.TraceId, HealthExamContext.DivisionId), ct);

    /// <summary>Bản xuống cấp cho eligibility: HIS lỗi → coi như không giữ vai trò nào.</summary>
    private async Task<IReadOnlyCollection<long>> ResolveSignRoleIdsOrEmptyAsync(CancellationToken ct)
    {
        var rolesRes = await ResolveSignRoleIdsAsync(ct);
        return rolesRes.IsSuccess ? rolesRes.Value : Array.Empty<long>();
    }

    private static ExamRecordItem MapToItem(ExamRecordResult x) => ExamRecordItem.From(x);
}
