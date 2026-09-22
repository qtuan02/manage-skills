using HealthExam.API.Middlewares;
using HealthExam.Application.Webhooks;
using HealthExam.API.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace HealthExam.API.Controllers;

/// <summary>
/// Điểm nhận sự kiện từ form-server — 02-api-spec §3.8.
/// </summary>
[Route("v1/hooks")]
public class HooksController : HealthExamControllerBase
{
    private readonly IIngestWebhookHandler _ingest;

    public HooksController(IIngestWebhookHandler ingest) => _ingest = ingest;

    /// <summary>
    /// Nhận một sự kiện, ghi vào hộp thư, trả 200 NGAY. Hệ quả nghiệp vụ do worker áp sau.
    /// </summary>
    [HttpPost("form-server")]
    public async Task<ActionResult<ResultData<WebhookAckResult>>> ReceiveFormServerEvent(
        [FromBody] FormWebhookEvent request, CancellationToken ct = default)
    {
        var raw = HttpContext.Items[WebhookSignatureMiddleware.RawBodyItemKey] as string;

        var command = new IngestWebhookCommand(
            request?.EventID,
            request?.Event,
            request?.DivisionID,
            request?.SubmissionID,
            request?.HostRefType,
            request?.HostRefID,
            request?.SubjectID,
            request?.OccurredAt,
            raw,
            HealthExamContext.TraceId,
            HealthExamContext.DivisionId);

        var result = await _ingest.HandleAsync(command, ct);
        return ToActionResult(result);
    }
}
