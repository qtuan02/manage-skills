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

public interface IChangeOrderStateHandler
{
    Task<ApplicationResult<ParaclinicalOrderResult>> HandleAsync(
        ChangeOrderStateCommand command, CancellationToken ct = default);
}

public class ChangeOrderStateHandler : IChangeOrderStateHandler
{
    private readonly IParaclinicalRepository _paraclinicalRepository;
    private readonly IExamRecordRepository _recordRepository;
    private readonly IExamSessionRepository _sessionRepository;
    private readonly IUnitOfWork _uow;
    private readonly IAuditRepository _auditRepository;
    private readonly IClock _clock;

    public ChangeOrderStateHandler(
        IParaclinicalRepository paraclinicalRepository,
        IExamRecordRepository recordRepository,
        IExamSessionRepository sessionRepository,
        IUnitOfWork uow,
        IAuditRepository auditRepository,
        IClock clock = null)
    {
        _paraclinicalRepository = paraclinicalRepository;
        _recordRepository = recordRepository;
        _sessionRepository = sessionRepository;
        _uow = uow;
        _auditRepository = auditRepository;
        _clock = clock ?? new SystemClock();
    }

    public async Task<ApplicationResult<ParaclinicalOrderResult>> HandleAsync(
        ChangeOrderStateCommand command, CancellationToken ct = default)
    {
        if (!Enum.IsDefined(typeof(ParaclinicalItemState), command.State))
        {
            return ApplicationResult<ParaclinicalOrderResult>.Fail(
                ApplicationFailureCode.BadRequest,
                "Trạng thái không hợp lệ",
                ApplicationValidationErrors.Of("State", "Chỉ nhận 0..4 theo ParaclinicalItemState"));
        }

        var target = (ParaclinicalItemState)command.State;
        if (target == ParaclinicalItemState.Cancelled)
        {
            return ApplicationResult<ParaclinicalOrderResult>.Fail(
                ApplicationFailureCode.BadRequest,
                "Huỷ chỉ định đi đường riêng: POST /v1/orders/{orderId}/cancel (cần lý do, và có chốt chặn 4094)");
        }

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

        var named = command.OrderItemIds != null && command.OrderItemIds.Count > 0;
        List<ParaclinicalOrderItem> selected;

        if (named)
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
            selected = picked;
        }
        else
        {
            selected = order.Items.ToList();
        }

        var alive = selected
            .Where(x => x.State != ParaclinicalItemState.Cancelled)
            .ToList();

        if (alive.Count == 0)
        {
            return ApplicationResult<ParaclinicalOrderResult>.Fail(
                ApplicationFailureCode.InvalidState,
                "Phiếu không còn dòng dịch vụ nào để đổi trạng thái");
        }

        var shielded = named
            ? new List<ParaclinicalOrderItem>()
            : alive.Where(x => x.State == ParaclinicalItemState.Done).ToList();
        var targets = named ? alive : alive.Where(x => x.State != ParaclinicalItemState.Done).ToList();

        if (targets.Count == 0)
        {
            return ApplicationResult<ParaclinicalOrderResult>.Fail(
                ApplicationFailureCode.ParaclinicalResultExists,
                $"Cả {shielded.Count} dịch vụ của phiếu đều đã có kết quả — lệnh không nêu dòng nào thì "
                + "không đụng tới chúng. Nêu đích danh OrderItemIDs nếu thực sự muốn huỷ kết quả.",
                new
                {
                    OrderNo = order.OrderNo,
                    Items = shielded.Select(x => new { x.OrderItemID, x.ServiceCode, x.ServiceName, State = (short)x.State })
                });
        }

        var single = alive.Count == 1;
        var carriesResultFields = command.IsAbnormal.HasValue || !string.IsNullOrWhiteSpace(command.ResultRefId);

        if (target == ParaclinicalItemState.Done && carriesResultFields && !single)
        {
            return ApplicationResult<ParaclinicalOrderResult>.Fail(
                ApplicationFailureCode.BadRequest,
                "IsAbnormal và ResultRefID là thông tin của MỘT dòng dịch vụ, không phải của cả phiếu — "
                + $"lệnh này chạm {alive.Count} dòng. Nêu đích danh OrderItemIDs đúng một dòng.",
                ApplicationValidationErrors.Of("OrderItemIDs",
                    "Bắt buộc, và đúng một dòng, khi gửi kèm IsAbnormal hoặc ResultRefID"));
        }

        var now = _clock.UtcNow;
        var actorId = long.TryParse(command.ActorId, out var parsedActorId) ? parsedActorId : 0L;
        var rejected = new List<string>();

        foreach (var item in targets)
        {
            var from = item.State;
            var result = item.TransitionTo(
                target,
                ParaclinicalStateSource.Internal,
                now,
                resultSourceKind: ParaclinicalResultSources.Manual,
                isAbnormal: command.IsAbnormal,
                resultRefId: command.ResultRefId);

            if (result.Outcome == ParaclinicalTransitionOutcome.Rejected)
            {
                rejected.Add($"{item.ServiceCode}: {result.Reason}");
                continue;
            }
            if (!result.Applied) continue;

            item.ModifiedDate = now;
            item.ModifiedBy = actorId;
            item.ModifiedActorKind = command.ActorKind;

            _auditRepository.Add(new AuditEntry(
                command.DivisionId,
                AuditEntityTypes.OrderItem,
                item.OrderItemID,
                AuditActions.StateChange,
                command.ActorId,
                (short)from,
                (short)item.State,
                new { order.OrderNo, item.ServiceCode, Source = "MANUAL" },
                command.ActorKind));
        }

        if (rejected.Count == targets.Count)
        {
            return ApplicationResult<ParaclinicalOrderResult>.Fail(
                ApplicationFailureCode.InvalidState,
                string.Join(" · ", rejected));
        }

        await _uow.SaveChangesAsync(ct);
        return ApplicationResult<ParaclinicalOrderResult>.Success(order.ToResult(includeCancelled: true));
    }
}
