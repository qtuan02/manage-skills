using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HealthExam.Application.Common;
using HealthExam.Application.ExamRecords;
using HealthExam.Application.ExamSessions;
using HealthExam.Domain.Common;
using HealthExam.Domain.ExamRecords;
using HealthExam.Domain.ExamSessions;
using HealthExam.Domain.Paraclinical;

namespace HealthExam.Application.Paraclinical;

public interface ICancelOrderHandler
{
    Task<ApplicationResult<ParaclinicalOrderResult>> HandleAsync(
        CancelOrderCommand command, CancellationToken ct = default);
}

public class CancelOrderHandler : ICancelOrderHandler
{
    private readonly IParaclinicalRepository _paraclinicalRepository;
    private readonly IExamRecordRepository _recordRepository;
    private readonly IExamSessionRepository _sessionRepository;
    private readonly IParaclinicalOutboxDispatcher _outboxDispatcher;
    private readonly IUnitOfWork _uow;
    private readonly IAuditRepository _auditRepository;
    private readonly IClock _clock;

    public CancelOrderHandler(
        IParaclinicalRepository paraclinicalRepository,
        IExamRecordRepository recordRepository,
        IExamSessionRepository sessionRepository,
        IParaclinicalOutboxDispatcher outboxDispatcher,
        IUnitOfWork uow,
        IAuditRepository auditRepository,
        IClock clock = null)
    {
        _paraclinicalRepository = paraclinicalRepository;
        _recordRepository = recordRepository;
        _sessionRepository = sessionRepository;
        _outboxDispatcher = outboxDispatcher;
        _uow = uow;
        _auditRepository = auditRepository;
        _clock = clock ?? new SystemClock();
    }

    public async Task<ApplicationResult<ParaclinicalOrderResult>> HandleAsync(
        CancelOrderCommand command, CancellationToken ct = default)
    {
        var order = await _paraclinicalRepository.GetAsync(
            command.DivisionId, command.OrderId, includeItems: true, forUpdate: true, ct);
        if (order == null)
        {
            return ApplicationResult<ParaclinicalOrderResult>.Fail(
                ApplicationFailureCode.NotFound, "Không tìm thấy phiếu chỉ định");
        }

        var record = await _recordRepository.GetAsync(
            command.DivisionId, order.RecordID, forUpdate: true, ct);
        if (record == null)
        {
            return ApplicationResult<ParaclinicalOrderResult>.Fail(
                ApplicationFailureCode.NotFound, "Không tìm thấy hồ sơ khám");
        }

        var session = await _sessionRepository.GetAsync(command.DivisionId, record.SessionID, forUpdate: false, ct);
        if (session != null && (session.State == ExamSessionState.Closed || session.State == ExamSessionState.Cancelled))
        {
            return ApplicationResult<ParaclinicalOrderResult>.Fail(
                ApplicationFailureCode.SessionClosed,
                $"Đợt khám {session.SessionCode} đã đóng, cần mở lại đợt trước khi ghi hồ sơ");
        }

        if (ExamRecordStates.IsCancelled(record.State))
        {
            return ApplicationResult<ParaclinicalOrderResult>.Fail(
                ApplicationFailureCode.InvalidState,
                $"Hồ sơ {record.RecordCode} đã huỷ, không chỉ định thêm được");
        }

        List<ParaclinicalOrderItem> targets;
        if (command.OrderItemIds != null && command.OrderItemIds.Count > 0)
        {
            var wanted = command.OrderItemIds.Distinct().ToHashSet();
            var picked = order.Items.Where(x => wanted.Contains(x.OrderItemID)).ToList();
            var missing = wanted.Except(picked.Select(x => x.OrderItemID)).ToList();
            if (missing.Count > 0)
            {
                return ApplicationResult<ParaclinicalOrderResult>.Fail(
                    ApplicationFailureCode.NotFound,
                    $"Dòng dịch vụ không thuộc phiếu này: {string.Join(", ", missing)}");
            }
            targets = picked;
        }
        else
        {
            targets = order.Items.ToList();
        }

        var blocked = targets.Where(x => x.State == ParaclinicalItemState.Done).ToList();
        if (blocked.Count > 0)
        {
            return ApplicationResult<ParaclinicalOrderResult>.Fail(
                ApplicationFailureCode.ParaclinicalResultExists,
                $"Đã có kết quả cho {blocked.Count} dịch vụ ({string.Join(", ", blocked.Select(x => x.ServiceCode))}) — huỷ kết quả trước rồi mới huỷ được chỉ định",
                new
                {
                    OrderNo = order.OrderNo,
                    Items = blocked.Select(x => new { x.OrderItemID, x.ServiceCode, x.ServiceName, State = (short)x.State })
                });
        }

        var now = _clock.UtcNow;
        var reason = Trim(command.Reason, ParaclinicalFieldLengths.CancelReason);
        var actorId = long.TryParse(command.ActorId, out var parsedActorId) ? parsedActorId : 0L;
        var cancelled = new List<ParaclinicalOrderItem>();

        foreach (var item in targets)
        {
            var from = item.State;
            if (!item.TryCancel(reason, now)) continue;
            if (from == item.State) continue;

            item.ModifiedDate = now;
            item.ModifiedBy = actorId;
            item.ModifiedActorKind = command.ActorKind;
            cancelled.Add(item);

            _auditRepository.Add(new AuditEntry(
                command.DivisionId,
                AuditEntityTypes.OrderItem,
                item.OrderItemID,
                AuditActions.StateChange,
                command.ActorId,
                (short)from,
                (short)item.State,
                new { order.OrderNo, item.ServiceCode, Reason = reason },
                command.ActorKind));
        }

        _outboxDispatcher.EnqueueCancel(order, record, cancelled, now);

        if (order.Items.All(x => x.State == ParaclinicalItemState.Cancelled))
        {
            order.IsActive = false;
            order.ModifiedDate = now;
            order.ModifiedBy = actorId;
            order.ModifiedActorKind = command.ActorKind;

            _auditRepository.Add(new AuditEntry(
                command.DivisionId,
                AuditEntityTypes.Order,
                order.OrderID,
                AuditActions.Delete,
                command.ActorId,
                null,
                null,
                new { order.OrderNo, Reason = reason },
                command.ActorKind));
        }

        await _uow.SaveChangesAsync(ct);
        return ApplicationResult<ParaclinicalOrderResult>.Success(order.ToResult(includeCancelled: true));
    }

    private static string Trim(string value, int max)
    {
        var v = (value ?? "").Trim();
        return v.Length <= max ? v : v[..max];
    }
}
