using HealthExam.Domain.Common;
using HealthExam.API.Contracts;
using HealthExam.API.Middlewares;
using HealthExam.Application.Common;
using HealthExam.Application.ExamRecords;
using HealthExam.Application.Webhooks;
using Microsoft.AspNetCore.Mvc;
using VerifyPortalCredentialsRequest = HealthExam.API.Contracts.VerifyPortalCredentialsRequest;
using VerifyPortalCredentialsResult = HealthExam.API.Contracts.VerifyPortalCredentialsResult;

namespace HealthExam.API.Controllers;

/// <summary>
/// Đường gọi NỘI BỘ giữa các service — không có màn hình nào của FE gọi vào đây.
///
/// Xác thực bằng <c>Authorization: Bearer {SECRET_INTER}</c> (xem AuthGuardMiddleware), không
/// phải JWT nhân viên. Tách hẳn tiền tố <c>/v1/internal</c> thay vì rải các endpoint này lẫn
/// vào controller nghiệp vụ để reverse proxy chặn được nguyên nhánh đường dẫn từ ngoài vào
/// bằng một luật, thay vì phải liệt kê từng đường.
/// </summary>
[Route("v1/internal")]
public class InternalController : HealthExamControllerBase
{
    private readonly IVerifyPortalCredentialsHandler _portalCredentials;
    private readonly IReconcileProgressHandler _reconcileHandler;
    private readonly IRequeueWebhooksHandler _requeueHandler;
    private readonly IBackfillScanResultsHandler _backfillHandler;
    private readonly IGetWebhookMetricsHandler _metricsHandler;

    public InternalController(
        IVerifyPortalCredentialsHandler portalCredentials,
        IReconcileProgressHandler reconcileHandler,
        IRequeueWebhooksHandler requeueHandler,
        IBackfillScanResultsHandler backfillHandler,
        IGetWebhookMetricsHandler metricsHandler)
    {
        _portalCredentials = portalCredentials;
        _reconcileHandler = reconcileHandler;
        _requeueHandler = requeueHandler;
        _backfillHandler = backfillHandler;
        _metricsHandler = metricsHandler;
    }

    /// <summary>
    /// Đối chiếu dữ kiện đăng nhập cổng người bệnh cho iam-server (UC04 / H-3b).
    /// </summary>
    [HttpPost("verify-portal-credentials")]
    public async Task<ActionResult<ResultData<VerifyPortalCredentialsResult>>> VerifyPortalCredentials(
        [FromBody] VerifyPortalCredentialsRequest request, CancellationToken ct = default)
    {
        GuardServiceCaller();
        request ??= new VerifyPortalCredentialsRequest();
        var command = new VerifyPortalCredentialsCommand(
            HealthExamContext.DivisionId,
            request.PatientCode,
            request.IdentityNumber,
            request.InsuranceNumber);

        var result = await _portalCredentials.HandleAsync(command, ct);
        if (!result.IsSuccess)
        {
            return ToActionResult(ApplicationResult<VerifyPortalCredentialsResult>.Fail(
                result.Failure.Code, result.Failure.Message, result.Failure.Payload));
        }

        var res = new VerifyPortalCredentialsResult
        {
            IsValid = result.Value.IsValid,
            RecordCode = result.Value.RecordCode,
            SessionCode = result.Value.SessionCode,
            FullName = result.Value.FullName
        };
        return ToActionResult(ApplicationResult<VerifyPortalCredentialsResult>.Success(res));
    }

    /// <summary>
    /// Chạy TAY một lượt đối soát tiến độ (03-task H2-06).
    /// </summary>
    [HttpPost("reconcile-progress")]
    public async Task<ActionResult<ResultData<ReconcileResult>>> ReconcileProgress(
        [FromQuery] Guid? sessionId = null,
        [FromQuery] int batchSize = 100,
        CancellationToken ct = default)
    {
        GuardServiceCaller();
        var result = await _reconcileHandler.HandleAsync(
            new ReconcileProgressCommand(HealthExamContext.DivisionId, sessionId, batchSize), ct);
        return ToActionResult(result);
    }

    /// <summary>
    /// Nạp lại sự kiện đã nằm dead-letter (M7 của review MR !5).
    /// </summary>
    [HttpPost("webhook-requeue")]
    public async Task<ActionResult<ResultData<WebhookRequeueResult>>> RequeueWebhooks(
        [FromQuery] string eventId = null, [FromQuery] int max = 100, CancellationToken ct = default)
    {
        GuardServiceCaller();
        var result = await _requeueHandler.HandleAsync(
            new RequeueWebhooksCommand(HealthExamContext.DivisionId, eventId, max), ct);
        return ToActionResult(result);
    }

    /// <summary>
    /// Nạp lại sự kiện đính kèm KQ scan đã bị bỏ qua ở giai đoạn P2.
    /// </summary>
    [HttpPost("scan-result-backfill")]
    public async Task<ActionResult<ResultData<WebhookRequeueResult>>> BackfillScanResults(
        [FromQuery] int max = 100, [FromQuery] DateTime? before = null, CancellationToken ct = default)
    {
        GuardServiceCaller();
        var result = await _backfillHandler.HandleAsync(
            new BackfillScanResultsCommand(HealthExamContext.DivisionId, max, before), ct);
        return ToActionResult(result);
    }

    [HttpGet("webhook-metrics")]
    public async Task<ActionResult<ResultData<WebhookMetricsSnapshot>>> GetWebhookMetrics(
        CancellationToken ct = default)
    {
        GuardServiceCaller();
        var result = await _metricsHandler.HandleAsync(new GetWebhookMetricsQuery(), ct);
        return ToActionResult(result);
    }

    /// <summary>
    /// Chỉ token service đi tiếp. Token nhân viên hợp lệ vẫn bị chặn ở đây bằng 4030.
    ///
    /// Vì sao không dựa vào AuthGuardMiddleware: middleware đó nhận CẢ token nhân viên lẫn
    /// SECRET_INTER, đúng cho các đường nghiệp vụ. Nhưng endpoint này tra cứu định danh theo
    /// Mã NB — mở cho mọi tài khoản nhân viên đăng nhập được là mở một đường dò dữ liệu người
    /// bệnh không đi qua bất kỳ màn hình có phân quyền nào.
    /// </summary>
    private void GuardServiceCaller()
    {
        if (HealthExamContext.Scope != Scopes.Service)
            throw HealthExamException.Forbidden("Đường gọi nội bộ, chỉ nhận token service");
    }
}
