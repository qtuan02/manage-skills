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

public interface ICreateOrdersFromPackageHandler
{
    Task<ApplicationResult<IReadOnlyList<ParaclinicalOrderResult>>> HandleAsync(
        CreateOrdersFromPackageCommand command, CancellationToken ct = default);
}

public class CreateOrdersFromPackageHandler : ICreateOrdersFromPackageHandler
{
    private readonly IParaclinicalRepository _paraclinicalRepository;
    private readonly IExamRecordRepository _recordRepository;
    private readonly IExamSessionRepository _sessionRepository;
    private readonly IOrderNoAllocator _orderNoAllocator;
    private readonly IUnitOfWork _uow;
    private readonly IAuditRepository _auditRepository;
    private readonly IClock _clock;

    public CreateOrdersFromPackageHandler(
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
        CreateOrdersFromPackageCommand command, CancellationToken ct = default)
    {
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

        var packageId = command.PackageId ?? record.PackageID;
        if (!packageId.HasValue || packageId.Value == Guid.Empty)
        {
            return ApplicationResult<IReadOnlyList<ParaclinicalOrderResult>>.Fail(
                ApplicationFailureCode.BadRequest,
                "Hồ sơ chưa gắn gói khám nào và lời gọi cũng không nêu PackageID",
                ApplicationValidationErrors.Of("PackageID", "Bỏ trống (bắt buộc khi hồ sơ chưa có gói)"));
        }

        var package = await _paraclinicalRepository.GetPackageAsync(command.DivisionId, packageId.Value, ct);
        if (package == null)
        {
            return ApplicationResult<IReadOnlyList<ParaclinicalOrderResult>>.Fail(
                ApplicationFailureCode.NotFound, "Không tìm thấy gói khám");
        }

        var services = await _paraclinicalRepository.ListActivePackageServicesAsync(
            command.DivisionId, package.PackageID, ct);

        if (services.Count == 0)
        {
            return ApplicationResult<IReadOnlyList<ParaclinicalOrderResult>>.Fail(
                ApplicationFailureCode.InvalidState,
                $"Gói khám {package.PackageCode} không có dịch vụ nào đang hiệu lực");
        }

        var existing = await _paraclinicalRepository.ListActiveServiceIdsByRecordAsync(
            command.DivisionId, record.RecordID, ct);

        var fresh = services.Where(s => !existing.Contains(s.ServiceID)).ToList();
        if (fresh.Count == 0)
        {
            return ApplicationResult<IReadOnlyList<ParaclinicalOrderResult>>.Fail(
                ApplicationFailureCode.InvalidState,
                $"Mọi dịch vụ của gói {package.PackageCode} đã được chỉ định cho hồ sơ này");
        }

        var now = _clock.UtcNow;
        var actorId = long.TryParse(command.ActorId, out var parsedActorId) ? parsedActorId : 0L;
        var created = new List<ParaclinicalOrder>();

        foreach (var group in fresh.GroupBy(x => (x.ParaclinicalKind ?? "").Trim().ToUpperInvariant()))
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
                SourcePackageID = package.PackageID,
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

            foreach (var s in group)
            {
                order.Items.Add(new ParaclinicalOrderItem
                {
                    OrderItemID = Guid.NewGuid(),
                    OrderID = order.OrderID,
                    RecordID = record.RecordID,
                    DivisionID = command.DivisionId,
                    ServiceID = s.ServiceID,
                    ServiceCode = Trim(s.ServiceCode, ParaclinicalFieldLengths.ServiceCode),
                    ServiceName = Trim(s.ServiceName, ParaclinicalFieldLengths.ServiceName),
                    ServiceGroupCode = Trim(s.ServiceGroupCode, ParaclinicalFieldLengths.ServiceGroupCode),
                    Quantity = s.Quantity <= 0 ? (short)1 : s.Quantity,
                    State = ParaclinicalItemState.Ordered,
                    SourcePackageID = package.PackageID,
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
