using HealthExam.Application.Common;
using HealthExam.Application.Paraclinical;
using HealthExam.API.Contracts;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace HealthExam.API.Controllers;

/// <summary>
/// Thao tác trên MỘT phiếu chỉ định đã có — 02-api-spec §3.4.
///
/// Đường tạo chỉ định nằm ở ExamRecordController (nó thuộc về hồ sơ), còn huỷ/đổi trạng thái
/// thì ở đây vì FE thao tác từ bảng chỉ định và chỉ có trong tay OrderID.
///
/// <c>POST /v1/orders/{id}/dispatch</c> (P3b) gửi phiếu sang RIS. Đường LIS thì CHƯA CÓ và cố
/// ý chưa dựng: toàn monorepo không có <c>lis-connect-server</c> hay một cấu hình <c>LIS_*</c>
/// nào (chốt §6), nên xét nghiệm đi bằng SCAN_RESULT qua form-server. Dựng sẵn một endpoint
/// LIS là mời FE nối vào một đường không tồn tại.
/// </summary>
[Route("v1/paraclinical-orders")]
public class ParaclinicalOrderController : HealthExamControllerBase
{
    private readonly IGetOrderHandler _getOrderHandler;
    private readonly ICancelOrderHandler _cancelOrderHandler;
    private readonly IChangeOrderStateHandler _changeOrderStateHandler;
    private readonly IDispatchOrderHandler _dispatchOrderHandler;
    private readonly IGetOrderResultsHandler _getOrderResultsHandler;

    public ParaclinicalOrderController(
        IGetOrderHandler getOrderHandler,
        ICancelOrderHandler cancelOrderHandler,
        IChangeOrderStateHandler changeOrderStateHandler,
        IDispatchOrderHandler dispatchOrderHandler,
        IGetOrderResultsHandler getOrderResultsHandler = null)
    {
        _getOrderHandler = getOrderHandler;
        _cancelOrderHandler = cancelOrderHandler;
        _changeOrderStateHandler = changeOrderStateHandler;
        _dispatchOrderHandler = dispatchOrderHandler;
        _getOrderResultsHandler = getOrderResultsHandler;
    }

    [HttpGet("{orderId:guid}")]
    [SwaggerOperation(
        Summary = "Lấy chi tiết phiếu chỉ định",
        Description = "Trả về thông tin phiếu chỉ định và trạng thái các dịch vụ thuộc phiếu theo mã order.")]
    public async Task<ActionResult<ResultData<ParaclinicalOrderResult>>> Get(
        Guid orderId, CancellationToken ct = default)
    {
        var res = await _getOrderHandler.HandleAsync(
            new GetOrderQuery(HealthExamContext.DivisionId, orderId), ct);
        return ToActionResult(res);
    }

    /// <summary>Huỷ cả phiếu hoặc vài dòng dịch vụ. <c>4094</c> nếu dòng đã có kết quả.</summary>
    [HttpPost("{orderId:guid}/cancel")]
    [SwaggerOperation(
        Summary = "Hủy phiếu chỉ định",
        Description = "Hủy toàn bộ phiếu hoặc các dòng dịch vụ được chọn; không cho hủy dòng đã có kết quả.")]
    public async Task<ActionResult<ResultData<ParaclinicalOrderResult>>> Cancel(
        Guid orderId, [FromBody] ParaclinicalCancelRequest request, CancellationToken ct = default)
    {
        var cmd = new CancelOrderCommand(
            HealthExamContext.DivisionId,
            HealthExamContext.ActorId.ToString(),
            HealthExamContext.ActorKind,
            orderId,
            request?.OrderItemIDs,
            request?.Reason);
        var res = await _cancelOrderHandler.HandleAsync(cmd, ct);
        return ToActionResult(res);
    }

    /// <summary>
    /// Đổi trạng thái TAY tại phòng thực hiện. Thân gói nêu được từng dòng — xem
    /// ParaclinicalStateRequest về chỗ lệch tài liệu có chủ ý.
    /// </summary>
    [HttpPut("{orderId:guid}/state")]
    [SwaggerOperation(
        Summary = "Cập nhật trạng thái phiếu chỉ định",
        Description = "Cho phép phòng thực hiện cập nhật thủ công trạng thái phiếu hoặc từng dòng dịch vụ theo request.")]
    public async Task<ActionResult<ResultData<ParaclinicalOrderResult>>> ChangeState(
        Guid orderId, [FromBody] ParaclinicalStateRequest request, CancellationToken ct = default)
    {
        var cmd = new ChangeOrderStateCommand(
            HealthExamContext.DivisionId,
            HealthExamContext.ActorId.ToString(),
            HealthExamContext.ActorKind,
            orderId,
            request?.State ?? 0,
            request?.OrderItemIDs,
            request?.IsAbnormal,
            request?.ResultRefID);
        var res = await _changeOrderStateHandler.HandleAsync(cmd, ct);
        return ToActionResult(res);
    }

    /// <summary>
    /// Gửi phiếu sang RIS — P3b, hướng (a) (docs/handoff/20260826-236-chot-p3-cls.md §4).
    ///
    /// XẾP HÀNG chứ không gọi vendor ngay: vòng lặp nền đẩy gói đi và thử lại 5 lần với giãn
    /// cách nhân đôi. Phản hồi <c>200</c> ở đây nghĩa là "đã nhận để gửi", KHÔNG phải "vendor
    /// đã nhận" — trạng thái thật đọc ở <c>SentStatus</c> của phiếu.
    ///
    /// Bấm nhiều lần là an toàn: gói đã xếp thì trả lại đúng gói đó kèm
    /// <c>Data.AlreadyQueued = true</c>.
    ///
    /// Mã lỗi riêng của đường này:
    /// <c>4095</c> hồ sơ/phiếu thiếu trường bắt buộc của hợp đồng RIS (Data.Missing liệt kê
    /// từng trường) · <c>5021</c> chưa cấu hình cầu RIS trên môi trường này.
    /// </summary>
    [HttpPost("{orderId:guid}/dispatch")]
    [SwaggerOperation(
        Summary = "Đẩy phiếu chỉ định sang RIS",
        Description = "Xếp phiếu vào hàng đợi tích hợp RIS để gửi bất đồng bộ; HTTP 200 chỉ xác nhận đã xếp hàng, không đồng nghĩa vendor đã nhận.")]
    public async Task<ActionResult<ResultData<OrderDispatchResult>>> Dispatch(
        Guid orderId, CancellationToken ct = default)
    {
        var cmd = new DispatchOrderCommand(
            HealthExamContext.DivisionId,
            orderId,
            HealthExamContext.ActorId.ToString(),
            HealthExamContext.ActorKind,
            HealthExamContext.TraceId);
        var res = await _dispatchOrderHandler.HandleAsync(cmd, ct);
        return ToActionResult(res, r => new OrderDispatchResult
        {
            OrderID = r.OrderID,
            OrderNo = r.OrderNo,
            Vendor = r.Vendor,
            Operation = r.Operation,
            OutboxID = r.OutboxID,
            AlreadyQueued = r.AlreadyQueued,
            LineIDs = r.LineIDs.ToList()
        });
    }

    [HttpGet("{orderId:guid}/results")]
    [SwaggerOperation(
        Summary = "Lấy kết quả cận lâm sàng của phiếu",
        Description = "Trả về danh sách các báo cáo kết quả và chỉ số chi tiết đã đồng bộ từ HIS cho phiếu chỉ định.")]
    public async Task<ActionResult<ResultData<IReadOnlyList<ParaclinicalResultDto>>>> GetResults(
        Guid orderId, CancellationToken ct = default)
    {
        if (_getOrderResultsHandler == null)
            return NotFound();

        var res = await _getOrderResultsHandler.HandleAsync(
            new GetOrderResultsQuery(HealthExamContext.DivisionId, orderId), ct);
        return ToActionResult(res);
    }
}
