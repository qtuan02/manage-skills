using HealthExam.Domain.Common;
using HealthExam.Domain.ExamSessions;
using HealthExam.Domain.ExamRecords;
using HealthExam.Domain.Imports;
using HealthExam.Domain.Paraclinical;
using HealthExam.Domain.Webhooks;
using HealthExam.Domain.Integrations;
using HealthExam.Domain.Catalogs;
using HealthExam.Infrastructure.Persistence.Legacy;
using IHealthExamContext = HealthExam.Application.Common.IHealthExamContext;
using Newtonsoft.Json;

namespace HealthExam.Server.Service;

/// <summary>
/// Ghi nhật ký thao tác vào HEX_AuditLog.
///
/// CỐ Ý KHÔNG tự SaveChanges: dòng nhật ký phải nằm CÙNG transaction với thay đổi nghiệp vụ
/// mà nó mô tả. Ghi riêng thì có hai đường hỏng — đợt đóng được nhưng nhật ký mất (không giải
/// trình được), hoặc nhật ký ghi "đã đóng" mà đợt thì không đóng (tra ra thông tin sai).
/// </summary>
public class AuditService
{
    private readonly IUnitOfWork _uow;
    private readonly IHealthExamContext _ctx;

    public AuditService(IUnitOfWork uow, IHealthExamContext ctx)
    {
        _uow = uow;
        _ctx = ctx;
    }

    /// <summary>Ghi một chuyển tiếp trạng thái. <paramref name="payload"/> là object bất kỳ, sẽ thành jsonb.</summary>
    public void StateChange(string entityType, Guid entityId, short? fromState, short? toState, object payload = null)
        => Add(entityType, entityId, AuditActions.StateChange, fromState, toState, payload);

    public void Add(string entityType, Guid entityId, string action,
        short? fromState = null, short? toState = null, object payload = null)
    {
        _uow.AuditLogs.Add(new AuditLog
        {
            DivisionID = _ctx.DivisionId,
            EntityType = entityType,
            EntityID = entityId,
            Action = action,
            ActorID = _ctx.ActorId,
            ActorKind = _ctx.ActorKind,
            ActorName = _ctx.ActorName,
            FromState = fromState,
            ToState = toState,
            Payload = payload == null ? null : JsonConvert.SerializeObject(payload),
            TraceID = _ctx.TraceId,
            OccurredAt = DateTime.UtcNow
        });
    }

    /// <summary>
    /// Ghi nhật ký cho luồng chạy NGOÀI request HTTP — worker webhook, job đối soát.
    ///
    /// Vì sao phải truyền tenant/trace vào thay vì đọc IHealthExamContext như đường thường:
    /// ở luồng nền không có HttpContext, nên context trả về chuỗi rỗng và dòng nhật ký sẽ
    /// mang DivisionID='' — tra theo đơn vị là không thấy gì, mà cũng chẳng có lỗi nào báo.
    /// Ngữ cảnh của luồng nền đi theo DỮ LIỆU (hàng inbox), không theo ambient.
    ///
    /// ActorKind luôn là Integration và ActorID = 0 CÓ CHỦ ĐÍCH: người bấm nút Ký ngồi bên
    /// form-server, danh tính đó không thuộc về IAM của mình nên bịa một ActorID vào đây là
    /// ghi nhật ký sai. Muốn biết ai ký thì đọc FRM_SubmissionAudit theo SubmissionID trong
    /// Payload.
    /// </summary>
    public void AddDetached(
        string divisionId, string traceId,
        string entityType, Guid entityId, string action,
        short? fromState = null, short? toState = null, object payload = null)
    {
        _uow.AuditLogs.Add(new AuditLog
        {
            DivisionID = divisionId ?? "",
            EntityType = entityType,
            EntityID = entityId,
            Action = action,
            ActorID = 0,
            ActorKind = ActorKind.Integration,
            ActorName = "",
            FromState = fromState,
            ToState = toState,
            Payload = payload == null ? null : JsonConvert.SerializeObject(payload),
            TraceID = traceId ?? "",
            OccurredAt = DateTime.UtcNow
        });
    }
}
