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

public interface ICreateOrdersHandler
{
    Task<ApplicationResult<IReadOnlyList<ParaclinicalOrderResult>>> HandleAsync(
        CreateOrdersCommand command, CancellationToken ct = default);
}

public class CreateOrdersHandler : ICreateOrdersHandler
{
    private readonly IParaclinicalRepository _paraclinicalRepository;
    private readonly IExamRecordRepository _recordRepository;
    private readonly IExamSessionRepository _sessionRepository;
    private readonly IOrderNoAllocator _orderNoAllocator;
    private readonly IUnitOfWork _uow;
    private readonly IAuditRepository _auditRepository;
    private readonly IClock _clock;

    public CreateOrdersHandler(
        IParaclinicalRepository paraclinicalRepository,
        IExamRecordRepository recordRepository,
        IExamSessionRepository sessionRepository,
        IOrderNoAllocator orderNoAllocator,
        IUnitOfWork uow,
        IAuditRepository auditRepository,
        IClock clock = null)
    {
        _paraclinicalRepository = paraclinicalRepository;
        _recordRepository = recordRepository;
        _sessionRepository = sessionRepository;
        _orderNoAllocator = orderNoAllocator;
        _uow = uow;
        _auditRepository = auditRepository;
        _clock = clock ?? new SystemClock();
    }

    public async Task<ApplicationResult<IReadOnlyList<ParaclinicalOrderResult>>> HandleAsync(
        CreateOrdersCommand command, CancellationToken ct = default)
    {
        var ids = (command.ServiceIds ?? Array.Empty<long>()).Where(x => x > 0).Distinct().ToList();
        if (ids.Count == 0)
        {
            return ApplicationResult<IReadOnlyList<ParaclinicalOrderResult>>.Fail(
                ApplicationFailureCode.BadRequest,
                "Chưa chọn dịch vụ nào",
                ApplicationValidationErrors.Of("ServiceIDs", "Bỏ trống (bắt buộc)"));
        }

        var record = await _recordRepository.GetAsync(command.DivisionId, command.RecordId, forUpdate: true, ct);
        if (record == null)
        {
            return ApplicationResult<IReadOnlyList<ParaclinicalOrderResult>>.Fail(
                ApplicationFailureCode.NotFound, "Không tìm thấy hồ sơ khám");
        }

        var session = await _sessionRepository.GetAsync(command.DivisionId, record.SessionID, forUpdate: false, ct);
        if (session != null && (session.State == ExamSessionState.Closed || session.State == ExamSessionState.Cancelled))
        {
            return ApplicationResult<IReadOnlyList<ParaclinicalOrderResult>>.Fail(
                ApplicationFailureCode.SessionClosed,
                $"Đợt khám {session.SessionCode} đã đóng, cần mở lại đợt trước khi ghi hồ sơ");
        }

        if (ExamRecordStates.IsCancelled(record.State))
        {
            return ApplicationResult<IReadOnlyList<ParaclinicalOrderResult>>.Fail(
                ApplicationFailureCode.InvalidState,
                $"Hồ sơ {record.RecordCode} đã huỷ, không chỉ định thêm được");
        }

        var snapshot = await _paraclinicalRepository.SnapshotServicesAsync(command.DivisionId, ids, ct);

        var unknown = ids.Where(x => !snapshot.ContainsKey(x)).ToList();
        if (unknown.Count > 0)
        {
            return ApplicationResult<IReadOnlyList<ParaclinicalOrderResult>>.Fail(
                ApplicationFailureCode.BadRequest,
                $"Dịch vụ không có trong danh mục của đơn vị: {string.Join(", ", unknown)}",
                ApplicationValidationErrors.Of("ServiceIDs",
                    "Chỉ chỉ định được dịch vụ đang có trong gói khám của đơn vị (xem ParaclinicalCatalogService)"));
        }

        var now = _clock.UtcNow;
        var actorId = long.TryParse(command.ActorId, out var parsedActorId) ? parsedActorId : 0L;
        var created = new List<ParaclinicalOrder>();

        var lines = ids.Select(id => snapshot[id]).ToList();

        foreach (var group in lines.GroupBy(x => (x.ParaclinicalKind ?? "").Trim().ToUpperInvariant()))
        {
            var orderNo = await _orderNoAllocator.NextAsync(ct);
            var order = new ParaclinicalOrder
            {
                OrderID = Guid.NewGuid(),
                DivisionID = command.DivisionId,
                RecordID = record.RecordID,
                SessionID = record.SessionID,
                OrderNo = orderNo,
                ParaclinicalKind = group.Key,
                SourcePackageID = null,
                Status = ParaclinicalOrderStatus.Draft,
                HisPtId = record.Patient?.HisPatientID,
                HisPtCode = record.Patient?.PatientCode ?? "",
                HisAdmissionId = record.AdmissionID,
                HisAdmissionCode = record.AdmissionID.HasValue ? $"HEX-{record.RecordID:N}" : "",
                OrderedByID = actorId,
                OrderedByName = command.ActorName ?? "",
                OrderedAt = now,
                RoomID = command.RoomId,
                TargetSystem = ParaclinicalTargets.None,
                SentStatus = OrderSentStatus.NotSent,
                Note = Trim(command.Note, ParaclinicalFieldLengths.Note),
                IsActive = true,
                CreatedDate = now,
                CreatedBy = actorId,
                CreatedActorKind = command.ActorKind,
                ModifiedDate = now,
                ModifiedBy = actorId,
                ModifiedActorKind = command.ActorKind
            };

            foreach (var itemSnapshot in group)
            {
                order.Items.Add(new ParaclinicalOrderItem
                {
                    OrderItemID = Guid.NewGuid(),
                    OrderID = order.OrderID,
                    RecordID = record.RecordID,
                    DivisionID = command.DivisionId,
                    ServiceID = itemSnapshot.ServiceID,
                    ServiceCode = Trim(itemSnapshot.ServiceCode, ParaclinicalFieldLengths.ServiceCode),
                    ServiceName = Trim(itemSnapshot.ServiceName, ParaclinicalFieldLengths.ServiceName),
                    ServiceGroupCode = Trim(itemSnapshot.ServiceGroupCode, ParaclinicalFieldLengths.ServiceGroupCode),
                    Quantity = itemSnapshot.Quantity <= 0 ? (short)1 : itemSnapshot.Quantity,
                    State = ParaclinicalItemState.Ordered,
                    SourcePackageID = null,
                    CreatedDate = now,
                    CreatedBy = actorId,
                    CreatedActorKind = command.ActorKind,
                    ModifiedDate = now,
                    ModifiedBy = actorId,
                    ModifiedActorKind = command.ActorKind
                });
            }

            _paraclinicalRepository.Add(order);
            _auditRepository.Add(new AuditEntry(
                command.DivisionId,
                AuditEntityTypes.Order,
                order.OrderID,
                AuditActions.Create,
                command.ActorId,
                null,
                null,
                new
                {
                    order.OrderNo,
                    order.ParaclinicalKind,
                    order.SourcePackageID,
                    RecordCode = record.RecordCode,
                    Services = order.Items.Select(i => new { i.ServiceID, i.ServiceCode }).ToList()
                },
                command.ActorKind));

            created.Add(order);
        }

        await _uow.SaveChangesAsync(ct);

        var result = created.Select(o => o.ToResult(includeCancelled: true)).ToList();
        return ApplicationResult<IReadOnlyList<ParaclinicalOrderResult>>.Success(result);
    }

    private static string Trim(string value, int max)
    {
        var v = (value ?? "").Trim();
        return v.Length <= max ? v : v[..max];
    }
}
