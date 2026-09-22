using HealthExam.API.Contracts;
using HealthExam.API.Middlewares;
using HealthExam.Application.Paraclinical;
using Microsoft.AspNetCore.Mvc;

namespace HealthExam.API.Controllers;

/// <summary>
/// ĐƯỜNG VỀ của vendor RIS — P3b, chiều còn lại của gate G-P3b-1.
///
/// ★ TENANT NẰM TRONG ĐƯỜNG DẪN, không ở header. Vendor không gửi được
/// <c>X-Division-Id</c>: hợp đồng dây của họ có đúng ba trường và không có chỗ cho header
/// riêng của mình. Nhưng bỏ tenant thì tra nhầm đơn vị — số hiệu dòng chỉ duy nhất TRONG một
/// đơn vị. Đặt vào URL là cách duy nhất còn lại, và nó khớp với cách vendor vốn đã làm: họ
/// cấu hình MỘT URL cho mỗi cơ sở (bản thân gốc URL của họ cũng mang mã cơ sở —
/// <c>…/hisris/ris/84006</c>). <see cref="VendorCallbackAuthMiddleware"/> dựng lại header từ
/// đoạn đường dẫn này để phần còn lại của stack không phải biết ngoại lệ.
///
/// Xác thực: Basic, đối xứng với chiều đi. KHÔNG dùng JWT của IAM (vendor không có tài khoản)
/// và KHÔNG dùng chữ ký HMAC của form-server (họ không ký gì cả).
///
/// ⚠️ CHƯA GIẢI QUYẾT — RIS chỉ cấu hình được MỘT URL gọi về cho mỗi tenant của họ. Nếu KSK
/// và HIS cùng bắn vào một tenant vendor thì gói gọi về đi hết về một phía. Cần tenant RIS
/// riêng cho KSK, hoặc một bộ định tuyến đứng trước hai service. Xem
/// docs/handoff/20260826-235-p3b-dispatch-ris.md §5.
/// </summary>
[Route("v1/integration/vendor")]
public class VendorIntegrationController : HealthExamControllerBase
{
    private readonly IUpdateVendorStatusHandler _handler;

    public VendorIntegrationController(IUpdateVendorStatusHandler handler) => _handler = handler;

    /// <summary>
    /// RIS báo dòng dịch vụ đã sang trạng thái nào. Sao đúng hình dạng
    /// <c>POST UpdateParaClinProcess</c> của pacs-connect-server: ba trường, không hơn.
    ///
    /// <c>NewStatus</c>: 0 → Chờ thực hiện · 1 → Đang thực hiện. Không có giá trị nào đưa
    /// được dòng lên "Đã trả KQ" — đó là giới hạn CỦA VENDOR, không phải lựa chọn của ta
    /// (docs/handoff/20260826-236-chot-p3-cls.md §5.1).
    ///
    /// Gửi lại cùng một cờ ⇒ <c>200</c> kèm <c>Data.Duplicated = true</c>, KHÔNG phải lỗi:
    /// đây là cờ trạng thái chứ không phải sự kiện. Đòi LÙI trạng thái ⇒ <c>4090</c>.
    /// </summary>
    [HttpPost("{division}/paraclinical-status")]
    public async Task<ActionResult<ResultData<VendorStatusResult>>> UpdateParaclinicalStatus(
        string division, [FromBody] VendorStatusRequest request, CancellationToken ct = default)
    {
        var cmd = new UpdateVendorStatusCommand(
            division, request?.OrderID, request?.VoucherType, request?.NewStatus ?? 0, HealthExamContext.TraceId);
        var res = await _handler.HandleAsync(cmd, ct);
        return ToActionResult(res, r => new VendorStatusResult
        {
            OrderItemID = r.OrderItemID,
            ServiceCode = r.ServiceCode,
            State = r.State,
            Duplicated = r.Duplicated,
            Detail = r.Detail
        });
    }
}
