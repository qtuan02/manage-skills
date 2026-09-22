using System;
using System.Threading;
using System.Threading.Tasks;
using HealthExam.Application.Common;
using HealthExam.Domain.Common;
using HealthExam.Domain.Paraclinical;

namespace HealthExam.Application.Paraclinical;

public sealed record UpdateVendorStatusCommand(
    string DivisionId,
    string OrderId,
    string VoucherType,
    byte NewStatus,
    string TraceId);

public sealed record UpdateVendorStatusResult(
    Guid OrderItemID,
    string ServiceCode,
    short State,
    bool Duplicated,
    string Detail);

public interface IUpdateVendorStatusHandler
{
    Task<ApplicationResult<UpdateVendorStatusResult>> HandleAsync(
        UpdateVendorStatusCommand command, CancellationToken ct = default);
}

public class UpdateVendorStatusHandler : IUpdateVendorStatusHandler
{
    private const string LineIdPrefix = "CL";

    private readonly IParaclinicalRepository _paraclinicalRepository;
    private readonly IUnitOfWork _uow;
    private readonly IAuditRepository _auditRepository;
    private readonly IClock _clock;

    public UpdateVendorStatusHandler(
        IParaclinicalRepository paraclinicalRepository,
        IUnitOfWork uow,
        IAuditRepository auditRepository,
        IClock clock)
    {
        _paraclinicalRepository = paraclinicalRepository;
        _uow = uow;
        _auditRepository = auditRepository;
        _clock = clock;
    }

    public static ParaclinicalItemState? MapVendorStatus(byte newStatus) => newStatus switch
    {
        0 => ParaclinicalItemState.Waiting,
        1 => ParaclinicalItemState.InProgress,
        _ => null
    };

    public static long? ParseLineNo(string vendorOrderId)
    {
        if (string.IsNullOrWhiteSpace(vendorOrderId)) return null;

        var raw = vendorOrderId.Trim();
        if (!raw.StartsWith(LineIdPrefix, StringComparison.OrdinalIgnoreCase)) return null;

        var digits = raw[LineIdPrefix.Length..];
        return long.TryParse(digits, out var value) && value > 0 ? value : null;
    }

    public async Task<ApplicationResult<UpdateVendorStatusResult>> HandleAsync(
        UpdateVendorStatusCommand command, CancellationToken ct = default)
    {
        var target = MapVendorStatus(command.NewStatus);
        if (!target.HasValue)
        {
            return ApplicationResult<UpdateVendorStatusResult>.Fail(
                ApplicationFailureCode.BadRequest,
                $"NewStatus={command.NewStatus} không nằm trong hợp đồng: vendor chỉ báo được 0 (Chờ thực hiện) "
                + "hoặc 1 (Đang thực hiện)",
                ApplicationValidationErrors.Of("NewStatus", "Chỉ nhận 0 hoặc 1"));
        }

        var lineNo = ParseLineNo(command.OrderId);
        if (!lineNo.HasValue)
        {
            return ApplicationResult<UpdateVendorStatusResult>.Fail(
                ApplicationFailureCode.BadRequest,
                $"OrderID \"{command.OrderId}\" không đúng quy ước: tiền tố \"{LineIdPrefix}\" rồi phần thuần số",
                ApplicationValidationErrors.Of("OrderID",
                    $"Sai định dạng — số hiệu dòng của KSK bắt đầu bằng \"{LineIdPrefix}\""));
        }

        var item = await _paraclinicalRepository.GetItemByVendorLineNoAsync(
            command.DivisionId, lineNo.Value, includeOrder: true, forUpdate: true, ct: ct);

        if (item == null)
        {
            return ApplicationResult<UpdateVendorStatusResult>.Fail(
                ApplicationFailureCode.NotFound,
                $"Không tìm thấy dịch vụ mang số hiệu {command.OrderId} trong đơn vị {command.DivisionId}");
        }

        var from = item.State;
        var now = _clock.UtcNow;

        var transition = item.TransitionTo(
            target.Value, ParaclinicalStateSource.Vendor, now);

        switch (transition.Outcome)
        {
            case ParaclinicalTransitionOutcome.Applied:
                item.ModifiedDate = now;
                item.ModifiedBy = 0;
                item.ModifiedActorKind = ActorKind.Integration;

                _auditRepository.Add(new AuditEntry(
                    item.DivisionID,
                    AuditEntityTypes.OrderItem,
                    item.OrderItemID,
                    AuditActions.StateChange,
                    "0",
                    (short)from,
                    (short)item.State,
                    new { item.Order?.OrderNo, item.ServiceCode, Source = ParaclinicalTargets.Ris, command.NewStatus },
                    ActorKind.Integration,
                    TraceId: command.TraceId));

                await _uow.SaveChangesAsync(ct);
                break;

            case ParaclinicalTransitionOutcome.NoOp:
                break;

            default:
                return ApplicationResult<UpdateVendorStatusResult>.Fail(
                    ApplicationFailureCode.InvalidState,
                    transition.Reason,
                    new { command.OrderId, item.ServiceCode, State = (short)item.State });
        }

        return ApplicationResult<UpdateVendorStatusResult>.Success(new UpdateVendorStatusResult(
            item.OrderItemID,
            item.ServiceCode,
            (short)item.State,
            transition.Outcome == ParaclinicalTransitionOutcome.NoOp,
            transition.Reason));
    }
}
