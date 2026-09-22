using System;
using System.Collections.Generic;
using System.Linq;
using HealthExam.Application.Common;
using HealthExam.Application.Integrations;
using HealthExam.Application.Paraclinical;
using HealthExam.Domain.Common;
using HealthExam.Domain.ExamRecords;
using HealthExam.Domain.Integrations;
using HealthExam.Domain.Paraclinical;
using Microsoft.Extensions.Logging;

namespace HealthExam.Infrastructure.Integrations.Ris;

public class ParaclinicalOutboxDispatcher : IParaclinicalOutboxDispatcher
{
    private readonly IIntegrationOutboxRepository _outboxRepository;
    private readonly IRisPayloadBuilder _payloadBuilder;
    private readonly IAuditRepository _audit;
    private readonly ILogger<ParaclinicalOutboxDispatcher> _logger;

    public ParaclinicalOutboxDispatcher(
        IIntegrationOutboxRepository outboxRepository,
        IRisPayloadBuilder payloadBuilder,
        IAuditRepository audit,
        ILogger<ParaclinicalOutboxDispatcher> logger)
    {
        _outboxRepository = outboxRepository;
        _payloadBuilder = payloadBuilder;
        _audit = audit;
        _logger = logger;
    }

    public void EnqueueCancel(
        ParaclinicalOrder order,
        ExamRecord record,
        IReadOnlyList<ParaclinicalOrderItem> cancelledLines,
        DateTime nowUtc)
    {
        var sent = cancelledLines.Where(x => x.VendorLineNo.HasValue).ToList();
        if (sent.Count == 0) return;

        if (!_payloadBuilder.ServesDivision(order.DivisionID))
        {
            _logger.LogError(
                "Phiếu {OrderNo} huỷ {Count} dịch vụ ĐÃ GỬI sang vendor nhưng cầu RIS không phục vụ "
                + "đơn vị {DivisionID} (đang khai {ConfiguredDivision}) — gói CANCELLED KHÔNG được "
                + "xếp hàng. Vendor vẫn đang giữ chỉ định này.",
                order.OrderNo, sent.Count, order.DivisionID, _payloadBuilder.GetConfiguredDivisionId());
            return;
        }

        var dedupKey = IntegrationOutbox.DedupKeyFor(
            ParaclinicalTargets.Ris, "CANCELLED", order.OrderNo, sent.Select(x => x.VendorLineNo.Value));

        if (!_payloadBuilder.TryBuildPayload(
            order, sent, record, "CANCELLED", nowUtc, out var payload, out var missing))
        {
            _logger.LogError(
                "Không dựng được gói CANCELLED cho phiếu {OrderNo}: thiếu {Fields}. "
                + "Vendor vẫn đang giữ chỉ định đã huỷ — cần huỷ tay bên vendor.",
                order.OrderNo, string.Join(", ", missing.Select(x => x.Field)));
            return;
        }

        var row = new IntegrationOutbox
        {
            DivisionID = order.DivisionID,
            OrderID = order.OrderID,
            Vendor = ParaclinicalTargets.Ris,
            Operation = "CANCELLED",
            DedupKey = dedupKey,
            Payload = payload,
            State = OutboxState.Pending,
            CreatedAt = nowUtc,
            TraceID = ""
        };
        _outboxRepository.Add(row);

        _audit.Add(new AuditEntry(
            order.DivisionID,
            AuditEntityTypes.Order,
            order.OrderID,
            AuditActions.Update,
            "0",
            null,
            null,
            new { order.OrderNo, Vendor = ParaclinicalTargets.Ris, Operation = "CANCELLED", dedupKey },
            ActorKind.Integration));
    }
}
