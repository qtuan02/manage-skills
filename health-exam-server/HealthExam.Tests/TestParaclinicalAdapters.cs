using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HealthExam.API.Contracts;
using HealthExam.API.Middlewares;
using HealthExam.Application.Catalogs;
using HealthExam.Application.Common;
using HealthExam.Application.Paraclinical;
using HealthExam.Domain.Catalogs;
using HealthExam.Domain.Common;
using HealthExam.Domain.ExamRecords;
using HealthExam.Domain.Integrations;
using HealthExam.Domain.Paraclinical;
using HealthExam.Infrastructure.Integrations.Ris;
using HealthExam.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HealthExam.Tests;

public class TestCatalogService
{
    private readonly ICatalogRepository _repo;
    private readonly FakeHealthExamContext _ctx;

    public TestCatalogService(ICatalogRepository repo, FakeHealthExamContext ctx)
    {
        _repo = repo;
        _ctx = ctx;
    }

    public async Task<IReadOnlyList<ServiceCategoryItem>> ListCategoriesAsync(CancellationToken ct = default)
    {
        var list = await _repo.ListServiceCategoriesAsync(_ctx.DivisionId, ct);
        return list.Select(x => new ServiceCategoryItem
        {
            CategoryCode = x.CategoryCode,
            CategoryName = x.CategoryName,
            ServiceCount = x.ServiceCount
        }).ToList();
    }

    public async Task<IReadOnlyList<ServiceGroupItem>> ListGroupsAsync(
        string categoryCode, string keyword, CancellationToken ct = default)
    {
        var list = await _repo.ListServiceGroupsAsync(_ctx.DivisionId, categoryCode, keyword, ct);
        return list.Select(x => new ServiceGroupItem
        {
            GroupCode = x.GroupCode,
            GroupName = x.GroupName,
            CategoryCode = x.CategoryCode,
            ServiceCount = x.ServiceCount
        }).ToList();
    }

    public async Task<PaginationData<ServiceCatalogItem>> ListServicesAsync(
        string categoryCode, string groupCode, string keyword, int page, int size,
        CancellationToken ct = default)
    {
        var filter = new ServiceCatalogFilter(categoryCode, groupCode, keyword, page, size);
        var paged = await _repo.ListServicesAsync(_ctx.DivisionId, filter, ct);
        var items = paged.Items.Select(x => new ServiceCatalogItem
        {
            ServiceID = x.ServiceID,
            ServiceCode = x.ServiceCode,
            ServiceName = x.ServiceName,
            CategoryCode = x.CategoryCode,
            GroupCode = x.GroupCode,
            InPackages = x.InPackages.ToList()
        }).ToList();
        return new PaginationData<ServiceCatalogItem>(items, paged.Page, paged.Size, paged.Total);
    }
}

public class TestParaclinicalOrderService
{
    private readonly IGetRecordOrdersHandler _getRecordOrdersHandler;
    private readonly ICreateOrdersHandler _createOrdersHandler;
    private readonly ICreateOrdersFromPackageHandler _createOrdersFromPackageHandler;
    private readonly IGetOrderHandler _getOrderHandler;
    private readonly ICancelOrderHandler _cancelOrderHandler;
    private readonly IChangeOrderStateHandler _changeOrderStateHandler;
    private readonly FakeHealthExamContext _ctx;

    public TestParaclinicalOrderService(
        IGetRecordOrdersHandler getRecordOrdersHandler,
        ICreateOrdersHandler createOrdersHandler,
        ICreateOrdersFromPackageHandler createOrdersFromPackageHandler,
        IGetOrderHandler getOrderHandler,
        ICancelOrderHandler cancelOrderHandler,
        IChangeOrderStateHandler changeOrderStateHandler,
        FakeHealthExamContext ctx)
    {
        _getRecordOrdersHandler = getRecordOrdersHandler;
        _createOrdersHandler = createOrdersHandler;
        _createOrdersFromPackageHandler = createOrdersFromPackageHandler;
        _getOrderHandler = getOrderHandler;
        _cancelOrderHandler = cancelOrderHandler;
        _changeOrderStateHandler = changeOrderStateHandler;
        _ctx = ctx;
    }

    public async Task<IReadOnlyList<ParaclinicalOrderView>> ListByRecordAsync(
        Guid recordId, bool includeCancelled = true, CancellationToken ct = default)
    {
        var res = await _getRecordOrdersHandler.HandleAsync(
            new GetRecordOrdersQuery(_ctx.DivisionId, recordId, includeCancelled), ct);
        Unpack(res);
        return res.Value.Select(ToView).ToList();
    }

    public async Task<ParaclinicalOrderView> GetAsync(Guid orderId, CancellationToken ct = default)
    {
        var res = await _getOrderHandler.HandleAsync(
            new GetOrderQuery(_ctx.DivisionId, orderId), ct);
        Unpack(res);
        return ToView(res.Value);
    }

    public async Task<IReadOnlyList<ParaclinicalOrderView>> CreateManualAsync(
        Guid recordId, ParaclinicalOrderCreateRequest req, CancellationToken ct = default)
    {
        var res = await _createOrdersHandler.HandleAsync(
            new CreateOrdersCommand(
                _ctx.DivisionId,
                _ctx.ActorId.ToString(),
                _ctx.ActorName,
                _ctx.ActorKind,
                recordId,
                req?.ServiceIDs,
                req?.Note,
                req?.RoomID ?? 0), ct);
        Unpack(res);
        return res.Value.Select(ToView).ToList();
    }

    public async Task<IReadOnlyList<ParaclinicalOrderView>> CreateFromPackageAsync(
        Guid recordId, ParaclinicalOrderFromPackageRequest req, CancellationToken ct = default)
    {
        var res = await _createOrdersFromPackageHandler.HandleAsync(
            new CreateOrdersFromPackageCommand(
                _ctx.DivisionId,
                _ctx.ActorId.ToString(),
                _ctx.ActorName,
                _ctx.ActorKind,
                recordId,
                req?.PackageID,
                req?.Note,
                req?.RoomID ?? 0), ct);
        Unpack(res);
        return res.Value.Select(ToView).ToList();
    }

    public async Task<ParaclinicalOrderView> CancelAsync(
        Guid orderId, ParaclinicalCancelRequest req, CancellationToken ct = default)
    {
        var res = await _cancelOrderHandler.HandleAsync(
            new CancelOrderCommand(
                _ctx.DivisionId,
                _ctx.ActorId.ToString(),
                _ctx.ActorKind,
                orderId,
                req?.OrderItemIDs,
                req?.Reason), ct);
        Unpack(res);
        return ToView(res.Value);
    }

    public async Task<ParaclinicalOrderView> ChangeStateAsync(
        Guid orderId, ParaclinicalStateRequest req, CancellationToken ct = default)
    {
        var res = await _changeOrderStateHandler.HandleAsync(
            new ChangeOrderStateCommand(
                _ctx.DivisionId,
                _ctx.ActorId.ToString(),
                _ctx.ActorKind,
                orderId,
                req?.State ?? 0,
                req?.OrderItemIDs,
                req?.IsAbnormal,
                req?.ResultRefID), ct);
        Unpack(res);
        return ToView(res.Value);
    }

    private static void Unpack<T>(ApplicationResult<T> res)
    {
        if (!res.IsSuccess)
        {
            throw new HealthExamException(
                ApplicationResultMapper.ToErrorCode(res.Failure.Code),
                res.Failure.Message,
                res.Failure.Payload);
        }
    }

    private static ParaclinicalOrderView ToView(ParaclinicalOrderResult r)
    {
        return new ParaclinicalOrderView
        {
            OrderID = r.OrderID,
            RecordID = r.RecordID,
            SessionID = r.SessionID,
            OrderNo = r.OrderNo,
            ParaclinicalKind = r.ParaclinicalKind,
            SourcePackageID = r.SourcePackageID,
            OrderedByID = r.OrderedByID,
            OrderedByName = r.OrderedByName,
            OrderedAt = r.OrderedAt,
            TargetSystem = r.TargetSystem,
            SentStatus = r.SentStatus,
            SentAt = r.SentAt,
            Note = r.Note,
            IsActive = r.IsActive,
            Items = (r.Items ?? Array.Empty<ParaclinicalOrderItemResult>()).Select(i => new ParaclinicalOrderItemView
            {
                OrderItemID = i.OrderItemID,
                OrderID = i.OrderID,
                OrderNo = i.OrderNo,
                ServiceID = i.ServiceID,
                ServiceCode = i.ServiceCode,
                ServiceName = i.ServiceName,
                ServiceGroupCode = i.ServiceGroupCode,
                Quantity = i.Quantity,
                State = i.State,
                StateName = i.StateName,
                PerformedAt = i.PerformedAt,
                ResultAt = i.ResultAt,
                ResultSourceKind = i.ResultSourceKind,
                ResultRefID = i.ResultRefID,
                AttachmentID = i.AttachmentID,
                IsAbnormal = i.IsAbnormal,
                SourcePackageID = i.SourcePackageID,
                CancelledAt = i.CancelledAt,
                CancelReason = i.CancelReason
            }).ToList()
        };
    }
}

public class TestConclusionService
{
    private readonly IParaclinicalRepository _paraclinicalRepo;
    private readonly IGetConclusionEligibilityHandler _getEligibilityHandler;
    private readonly ISignConclusionHandler _signConclusionHandler;
    private readonly FakeHealthExamContext _ctx;

    public TestConclusionService(
        IParaclinicalRepository paraclinicalRepo,
        IGetConclusionEligibilityHandler getEligibilityHandler,
        ISignConclusionHandler signConclusionHandler,
        FakeHealthExamContext ctx)
    {
        _paraclinicalRepo = paraclinicalRepo;
        _getEligibilityHandler = getEligibilityHandler;
        _signConclusionHandler = signConclusionHandler;
        _ctx = ctx;
    }

    public Task<(bool Satisfied, int Pending, int Total)> EvaluateConditionBAsync(
        Guid recordId, CancellationToken ct = default)
        => _paraclinicalRepo.EvaluateConditionBAsync(_ctx.DivisionId, recordId, ct);

    public async Task<ConclusionEligibilityItem> GetEligibilityAsync(
        Guid recordId, IReadOnlyCollection<long> roleIds = null, CancellationToken ct = default)
    {
        var res = await _getEligibilityHandler.HandleAsync(
            new GetConclusionEligibilityQuery(_ctx.DivisionId, recordId, RoleIds: roleIds), ct);
        if (!res.IsSuccess)
        {
            throw new HealthExamException(
                ApplicationResultMapper.ToErrorCode(res.Failure.Code),
                res.Failure.Message,
                res.Failure.Payload);
        }

        return new ConclusionEligibilityItem
        {
            ProfileID = res.Value.ProfileID,
            RecordCode = res.Value.RecordCode,
            SubmissionID = res.Value.SubmissionID,
            CanSignConclusion = res.Value.CanSignConclusion,
            SignedByEmployeeID = res.Value.SignedByEmployeeID,
            SignedAt = res.Value.SignedAt,
            MissingSteps = (res.Value.MissingSteps ?? Array.Empty<int>()).ToList(),
            Conditions = res.Value.Conditions.Select(c => new ConclusionConditionItem
            {
                Code = c.Code,
                Label = c.Label,
                Satisfied = c.Satisfied,
                Detail = c.Detail,
                Source = c.Source
            }).ToList(),
            Steps = (res.Value.Steps ?? Array.Empty<ConclusionSignStepResult>()).Select(s => new ConclusionSignStep
            {
                SwStep = s.SwStep,
                StepName = s.StepName,
                ItemGroupID = s.ItemGroupID,
                SwRoleId = s.SwRoleId,
                Status = s.Status,
                SignedByEmployeeID = s.SignedByEmployeeID,
                SignedAt = s.SignedAt,
                SignedByEmployeeName = s.SignedByEmployeeName
            }).ToList()
        };
    }

    public async Task<HealthExam.API.Contracts.ConclusionSignResult> SignAsync(
        Guid recordId, IReadOnlyCollection<long> roleIds = null,
        CancellationToken ct = default)
    {
        var cmd = new SignConclusionCommand(
            _ctx.DivisionId,
            _ctx.ActorId.ToString(),
            _ctx.ActorName,
            _ctx.ActorKind,
            recordId,
            "Bearer test-token",
            _ctx.TraceId,
            _ctx.ActorCode,
            roleIds ?? Array.Empty<long>(),
            DepartmentId: 101);

        var res = await _signConclusionHandler.HandleAsync(cmd, ct);
        if (!res.IsSuccess)
        {
            object payload = res.Failure.Payload;
            if (res.Failure.Payload is HealthExam.Application.Paraclinical.ConclusionSignResult appRes)
            {
                payload = ToContract(appRes);
            }
            throw new HealthExamException(
                ApplicationResultMapper.ToErrorCode(res.Failure.Code),
                res.Failure.Message,
                payload);
        }

        return ToContract(res.Value);
    }

    private static HealthExam.API.Contracts.ConclusionSignResult ToContract(HealthExam.Application.Paraclinical.ConclusionSignResult r)
    {
        return new HealthExam.API.Contracts.ConclusionSignResult
        {
            RecordID = r.RecordID,
            Status = r.Status,
            SignedFilePath = r.SignedFilePath,
            SignedByEmployeeID = r.SignedByEmployeeID,
            SignedAt = r.SignedAt,
            MissingSteps = (r.MissingSteps ?? Array.Empty<int>()).ToList(),
            Conditions = (r.Conditions ?? Array.Empty<ConclusionConditionResult>()).Select(c => new ConclusionConditionItem
            {
                Code = c.Code,
                Label = c.Label,
                Satisfied = c.Satisfied,
                Detail = c.Detail,
                Source = c.Source
            }).ToList(),
            Steps = (r.Steps ?? Array.Empty<ConclusionSignStepResult>()).Select(s => new ConclusionSignStep
            {
                SwStep = s.SwStep,
                StepName = s.StepName,
                ItemGroupID = s.ItemGroupID,
                SwRoleId = s.SwRoleId,
                Status = s.Status,
                SignedByEmployeeID = s.SignedByEmployeeID,
                SignedAt = s.SignedAt,
                SignedByEmployeeName = s.SignedByEmployeeName
            }).ToList()
        };
    }
}

public record OutboxSendResult(bool Success, string Detail, string ResponseSnippet);

public class IntegrationOutboxService
{
    public const int MaxRetry = IntegrationOutbox.MaxRetry;
    public static TimeSpan BackoffFor(int retryCount) => IntegrationOutbox.BackoffFor((short)retryCount);
    public static string DedupKeyFor(string vendor, string operation, string orderNo, IEnumerable<long> lineNos = null)
        => IntegrationOutbox.DedupKeyFor(vendor, operation, orderNo, lineNos);
    public static void Retire(IntegrationOutbox dead) => IntegrationOutbox.Retire(dead);

    private readonly IDispatchOrderHandler _dispatchOrderHandler;
    private readonly IParaclinicalOutboxDispatcher _outboxDispatcher;
    private readonly RisClient _risClient;
    private readonly IParaclinicalRepository _paraclinicalRepository;
    private readonly IAuditRepository _auditRepository;
    private readonly HealthExamDbContext _db;
    private readonly FakeHealthExamContext _ctx;

    public IntegrationOutboxService(
        IDispatchOrderHandler dispatchOrderHandler,
        IParaclinicalOutboxDispatcher outboxDispatcher,
        RisClient risClient,
        IParaclinicalRepository paraclinicalRepository,
        IAuditRepository auditRepository,
        HealthExamDbContext db,
        FakeHealthExamContext ctx)
    {
        _dispatchOrderHandler = dispatchOrderHandler;
        _outboxDispatcher = outboxDispatcher;
        _risClient = risClient;
        _paraclinicalRepository = paraclinicalRepository;
        _auditRepository = auditRepository;
        _db = db;
        _ctx = ctx;
    }

    public async Task<OrderDispatchResult> EnqueueOrderAsync(Guid orderId, CancellationToken ct = default)
    {
        var cmd = new DispatchOrderCommand(
            _ctx.DivisionId,
            orderId,
            _ctx.ActorId.ToString(),
            _ctx.ActorKind,
            _ctx.TraceId);
        var res = await _dispatchOrderHandler.HandleAsync(cmd, ct);
        if (!res.IsSuccess)
        {
            throw new HealthExamException(
                ApplicationResultMapper.ToErrorCode(res.Failure.Code),
                res.Failure.Message,
                res.Failure.Payload);
        }

        return new OrderDispatchResult
        {
            OrderID = res.Value.OrderID,
            OrderNo = res.Value.OrderNo,
            Vendor = res.Value.Vendor,
            Operation = res.Value.Operation,
            OutboxID = res.Value.OutboxID,
            AlreadyQueued = res.Value.AlreadyQueued,
            LineIDs = res.Value.LineIDs.ToList()
        };
    }

    public IntegrationOutbox EnqueueCancel(
        ParaclinicalOrder order,
        ExamRecord record,
        IReadOnlyList<ParaclinicalOrderItem> cancelledLines,
        DateTime nowUtc)
    {
        _outboxDispatcher.EnqueueCancel(order, record, cancelledLines, nowUtc);
        return _db.IntegrationOutboxes.Local.LastOrDefault(x => x.OrderID == order.OrderID && x.Operation == "CANCELLED");
    }

    public Task<RisClientRawResult> CallVendorAsync(IntegrationOutbox row, CancellationToken ct = default)
        => _risClient.SendAsync(row.Payload, row.OutboxID, ct);

    public async Task<OutboxSendResult> ApplyAsync(
        IntegrationOutbox row, RisClientRawResult result, CancellationToken ct = default)
    {
        if (!result.Success) return new OutboxSendResult(false, result.Error, result.ResponseBody);

        var order = await _db.ParaclinicalOrders
            .AsTracking()
            .Include(x => x.Items)
            .FirstOrDefaultAsync(x => x.DivisionID == row.DivisionID && x.OrderID == row.OrderID, ct);

        if (order == null)
            return new OutboxSendResult(true, $"Đã gửi, nhưng không còn phiếu {row.OrderID} trong DB để áp trạng thái", result.ResponseBody);

        var now = DateTime.UtcNow;
        order.SentStatus = row.Operation == "NEW" ? OrderSentStatus.Sent : order.SentStatus;
        order.SentAt ??= row.Operation == "NEW" ? now : null;
        order.ModifiedDate = now;

        var advanced = 0;
        if (row.Operation == "NEW")
        {
            foreach (var item in order.Items.Where(x => x.State == ParaclinicalItemState.Ordered && x.VendorLineNo.HasValue))
            {
                var from = item.State;
                var transition = item.TransitionTo(
                    ParaclinicalItemState.Waiting, ParaclinicalStateSource.Internal, now);
                if (!transition.Applied) continue;

                advanced++;
                _auditRepository.Add(new AuditEntry(
                    order.DivisionID,
                    AuditEntityTypes.OrderItem,
                    item.OrderItemID,
                    AuditActions.StateChange,
                    "0",
                    (short)from,
                    (short)item.State,
                    new { order.OrderNo, item.ServiceCode, Source = ParaclinicalTargets.Ris },
                    ActorKind.Integration,
                    TraceId: row.TraceID));
            }
        }

        return new OutboxSendResult(true,
            $"RIS nhận {row.Operation} phiếu {order.OrderNo}; {advanced} dịch vụ sang 'Chờ thực hiện'",
            result.ResponseBody);
    }
}

public class VendorStatusService
{
    private readonly IUpdateVendorStatusHandler _handler;
    private readonly FakeHealthExamContext _ctx;

    public VendorStatusService(IUpdateVendorStatusHandler handler, FakeHealthExamContext ctx)
    {
        _handler = handler;
        _ctx = ctx;
    }

    public static ParaclinicalItemState? MapVendorStatus(byte newStatus)
        => UpdateVendorStatusHandler.MapVendorStatus(newStatus);

    public async Task<VendorStatusResult> ApplyAsync(VendorStatusRequest req, CancellationToken ct = default)
    {
        req ??= new VendorStatusRequest();
        var cmd = new UpdateVendorStatusCommand(
            _ctx.DivisionId,
            req.OrderID,
            req.VoucherType,
            req.NewStatus,
            _ctx.TraceId);

        var res = await _handler.HandleAsync(cmd, ct);
        if (!res.IsSuccess)
        {
            throw new HealthExamException(
                ApplicationResultMapper.ToErrorCode(res.Failure.Code),
                res.Failure.Message,
                res.Failure.Payload);
        }

        return new VendorStatusResult
        {
            OrderItemID = res.Value.OrderItemID,
            ServiceCode = res.Value.ServiceCode,
            State = res.Value.State,
            Duplicated = res.Value.Duplicated,
            Detail = res.Value.Detail
        };
    }
}

