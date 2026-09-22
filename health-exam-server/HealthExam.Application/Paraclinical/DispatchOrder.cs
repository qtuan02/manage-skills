using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HealthExam.Application.Common;
using HealthExam.Application.ExamRecords;
using HealthExam.Application.Integrations;
using HealthExam.Domain.Common;
using HealthExam.Domain.ExamRecords;
using HealthExam.Domain.Integrations;
using HealthExam.Domain.Paraclinical;

namespace HealthExam.Application.Paraclinical;

public sealed record DispatchOrderCommand(
    string DivisionId,
    Guid OrderId,
    string ActorId,
    ActorKind ActorKind,
    string TraceId);

public sealed record DispatchOrderResult(
    Guid OrderID,
    string OrderNo,
    string Vendor,
    string Operation,
    long OutboxID,
    bool AlreadyQueued,
    IReadOnlyList<string> LineIDs);

public interface IDispatchOrderHandler
{
    Task<ApplicationResult<DispatchOrderResult>> HandleAsync(
        DispatchOrderCommand command, CancellationToken ct = default);
}

public class DispatchOrderHandler : IDispatchOrderHandler
{
    private const string LineIdPrefix = "CL";
    private const int LineIdDigits = 10;

    private readonly IParaclinicalRepository _paraclinicalRepository;
    private readonly IExamRecordRepository _recordRepository;
    private readonly IIntegrationOutboxRepository _outboxRepository;
    private readonly IVendorLineNoAllocator _lineNos;
    private readonly IRisPayloadBuilder _risPayloadBuilder;
    private readonly IUnitOfWork _uow;
    private readonly IAuditRepository _auditRepository;
    private readonly IClock _clock;

    public DispatchOrderHandler(
        IParaclinicalRepository paraclinicalRepository,
        IExamRecordRepository recordRepository,
        IIntegrationOutboxRepository outboxRepository,
        IVendorLineNoAllocator lineNos,
        IRisPayloadBuilder risPayloadBuilder,
        IUnitOfWork uow,
        IAuditRepository auditRepository,
        IClock clock)
    {
        _paraclinicalRepository = paraclinicalRepository;
        _recordRepository = recordRepository;
        _outboxRepository = outboxRepository;
        _lineNos = lineNos;
        _risPayloadBuilder = risPayloadBuilder;
        _uow = uow;
        _auditRepository = auditRepository;
        _clock = clock;
    }

    public async Task<ApplicationResult<DispatchOrderResult>> HandleAsync(
        DispatchOrderCommand command, CancellationToken ct = default)
    {
        if (!_risPayloadBuilder.IsDispatchConfigured())
        {
            return ApplicationResult<DispatchOrderResult>.Fail(
                ApplicationFailureCode.VendorNotConfigured,
                $"Chưa cấu hình cầu nối RIS: cần {_risPayloadBuilder.GetDispatchEnvNames()}",
                new { Dependency = "ris", Reason = "RIS_BASE_URL chưa đặt" });
        }

        if (!_risPayloadBuilder.ServesDivision(command.DivisionId))
        {
            var configured = _risPayloadBuilder.GetConfiguredDivisionId();
            return ApplicationResult<DispatchOrderResult>.Fail(
                ApplicationFailureCode.VendorNotConfigured,
                $"Cầu nối RIS đang khai cho đơn vị {configured}, không phải {command.DivisionId}. "
                + "Đơn vị này muốn gửi chỉ định sang RIS thì cần cấu hình RIS_DIVISION_ID "
                + "và đích RIS của riêng nó.",
                new
                {
                    Dependency = "ris",
                    Reason = $"RIS_DIVISION_ID={configured}",
                    Requested = command.DivisionId
                });
        }

        var order = await _paraclinicalRepository.GetAsync(
            command.DivisionId, command.OrderId, includeItems: true, forUpdate: true, ct: ct);

        if (order == null)
        {
            return ApplicationResult<DispatchOrderResult>.Fail(
                ApplicationFailureCode.NotFound,
                "Không tìm thấy phiếu chỉ định");
        }

        if (!order.IsActive)
        {
            return ApplicationResult<DispatchOrderResult>.Fail(
                ApplicationFailureCode.InvalidState,
                "Phiếu đã bị huỷ — không gửi sang vendor được nữa");
        }

        var lines = (order.Items ?? new List<ParaclinicalOrderItem>())
            .Where(x => x.State != ParaclinicalItemState.Cancelled)
            .OrderBy(x => x.ServiceCode)
            .ToList();

        if (lines.Count == 0)
        {
            return ApplicationResult<DispatchOrderResult>.Fail(
                ApplicationFailureCode.InvalidState,
                "Phiếu không còn dòng dịch vụ nào để gửi");
        }

        var record = await _recordRepository.GetAsync(
            command.DivisionId, order.RecordID, ct: ct);

        if (record == null)
        {
            return ApplicationResult<DispatchOrderResult>.Fail(
                ApplicationFailureCode.NotFound,
                "Không tìm thấy hồ sơ của phiếu chỉ định");
        }

        var dedupKey = IntegrationOutbox.DedupKeyFor(ParaclinicalTargets.Ris, "NEW", order.OrderNo);

        var existing = await _outboxRepository.FindByDedupKeyAsync(command.DivisionId, dedupKey, ct);
        if (existing != null && existing.State != OutboxState.DeadLetter)
        {
            return ApplicationResult<DispatchOrderResult>.Success(
                ViewOf(order, lines, existing, alreadyQueued: true));
        }

        if (existing != null)
        {
            var cancelSent = await _outboxRepository.HasSentCancelAsync(command.DivisionId, order.OrderID, ct);
            if (cancelSent)
            {
                return ApplicationResult<DispatchOrderResult>.Fail(
                    ApplicationFailureCode.InvalidState,
                    $"Phiếu {order.OrderNo} đã báo HUỶ sang vendor rồi — không nạp lại lệnh gửi được nữa. "
                    + "Tạo một phiếu chỉ định mới nếu vẫn cần thực hiện.",
                    new { order.OrderNo, Vendor = ParaclinicalTargets.Ris });
            }

            IntegrationOutbox.Retire(existing);
        }

        var pending = lines.Where(x => !x.VendorLineNo.HasValue).ToList();
        if (pending.Count > 0)
        {
            var numbers = await _lineNos.NextAsync(pending.Count, ct);
            for (var i = 0; i < pending.Count && i < numbers.Count; i++)
            {
                pending[i].VendorLineNo = numbers[i];
            }
        }

        var now = _clock.UtcNow;
        if (!_risPayloadBuilder.TryBuildPayload(
            order, lines, record, "NEW", now, out var payloadStr, out var missing))
        {
            return ApplicationResult<DispatchOrderResult>.Fail(
                ApplicationFailureCode.VendorPayloadIncomplete,
                $"Phiếu {order.OrderNo} thiếu {missing.Count} trường bắt buộc của hợp đồng RIS — bổ sung rồi gửi lại",
                new
                {
                    order.OrderNo,
                    Vendor = ParaclinicalTargets.Ris,
                    Missing = missing.Select(x => new { x.Field, x.Reason }).ToList()
                });
        }

        var row = new IntegrationOutbox
        {
            DivisionID = order.DivisionID,
            OrderID = order.OrderID,
            Vendor = ParaclinicalTargets.Ris,
            Operation = "NEW",
            DedupKey = dedupKey,
            Payload = payloadStr,
            State = OutboxState.Pending,
            CreatedAt = now,
            TraceID = command.TraceId ?? ""
        };
        _outboxRepository.Add(row);

        order.TargetSystem = ParaclinicalTargets.Ris;
        order.ModifiedDate = now;
        order.ModifiedBy = long.TryParse(command.ActorId, out var aid) ? aid : 0L;
        order.ModifiedActorKind = command.ActorKind;

        _auditRepository.Add(new AuditEntry(
            command.DivisionId,
            AuditEntityTypes.Order,
            order.OrderID,
            AuditActions.Update,
            command.ActorId,
            null,
            null,
            new { order.OrderNo, Vendor = ParaclinicalTargets.Ris, Operation = "NEW", dedupKey },
            command.ActorKind,
            TraceId: command.TraceId));

        var saveResult = await _uow.SaveChangesAsync(ct);
        if (saveResult.Outcome == PersistenceSaveOutcome.UniqueConflict)
        {
            _uow.DiscardPendingChanges();
            var winner = await _outboxRepository.FindByDedupKeyAsync(command.DivisionId, dedupKey, ct);
            if (winner != null)
            {
                var reloadedOrder = await _paraclinicalRepository.GetAsync(
                    command.DivisionId, command.OrderId, includeItems: true, ct: ct);
                var reloadedLines = (reloadedOrder?.Items ?? new List<ParaclinicalOrderItem>())
                    .Where(x => x.State != ParaclinicalItemState.Cancelled)
                    .OrderBy(x => x.ServiceCode)
                    .ToList();
                return ApplicationResult<DispatchOrderResult>.Success(
                    ViewOf(reloadedOrder ?? order, reloadedLines, winner, alreadyQueued: true));
            }
        }

        return ApplicationResult<DispatchOrderResult>.Success(
            ViewOf(order, lines, row, alreadyQueued: false));
    }

    private static string ComposeLineId(long vendorLineNo)
        => LineIdPrefix + vendorLineNo.ToString(new string('0', LineIdDigits));

    private static DispatchOrderResult ViewOf(
        ParaclinicalOrder order,
        IReadOnlyList<ParaclinicalOrderItem> lines,
        IntegrationOutbox row,
        bool alreadyQueued)
        => new(
            order.OrderID,
            order.OrderNo,
            row.Vendor,
            row.Operation,
            row.OutboxID,
            alreadyQueued,
            lines
                .Where(x => x.VendorLineNo.HasValue)
                .Select(x => ComposeLineId(x.VendorLineNo.Value))
                .ToList());
}
