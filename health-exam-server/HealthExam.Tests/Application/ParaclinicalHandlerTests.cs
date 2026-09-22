using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HealthExam.Application.Common;
using HealthExam.Application.ExamRecords;
using HealthExam.Application.ExamSessions;
using HealthExam.Application.Paraclinical;
using HealthExam.Domain.Catalogs;
using HealthExam.Domain.Common;
using HealthExam.Domain.ExamRecords;
using HealthExam.Domain.ExamSessions;
using HealthExam.Domain.Paraclinical;
using Xunit;

namespace HealthExam.Tests.Application;

public class ParaclinicalHandlerTests
{
    private readonly FakeParaclinicalRepository _paraclinicalRepo = new();
    private readonly FakeExamRecordRepository _recordRepo = new();
    private readonly FakeExamSessionRepository _sessionRepo = new();
    private readonly FakeOrderNoAllocator _orderNoAllocator = new();
    private readonly FakeFormServerClient _formServerClient = new();
    private readonly FakeParaclinicalOutboxDispatcher _outboxDispatcher = new();
    private readonly FakeUnitOfWork _uow = new();
    private readonly FakeAuditRepository _audit = new();
    private readonly FakeClock _clock = new(new DateTime(2026, 9, 10, 10, 0, 0, DateTimeKind.Utc));

    private const string DivisionId = "D01";
    private const string ActorId = "1001";
    private const string ActorName = "BS. Nguyễn Văn A";
    private const ActorKind CurrentActorKind = ActorKind.Employee;

    // ───────────────────────────── GetRecordOrders ───────────────────────────────────

    [Fact]
    public async Task GetRecordOrders_returns_not_found_when_record_missing()
    {
        var handler = new GetRecordOrdersHandler(_paraclinicalRepo, _recordRepo);
        var result = await handler.HandleAsync(new GetRecordOrdersQuery(DivisionId, Guid.NewGuid()));

        Assert.False(result.IsSuccess);
        Assert.Equal(ApplicationFailureCode.NotFound, result.Failure.Code);
    }

    [Fact]
    public async Task GetRecordOrders_returns_orders_list()
    {
        var recordId = Guid.NewGuid();
        _recordRepo.Records[recordId] = new ExamRecord { RecordID = recordId, DivisionID = DivisionId };
        var order = new ParaclinicalOrder
        {
            OrderID = Guid.NewGuid(),
            DivisionID = DivisionId,
            RecordID = recordId,
            OrderNo = "CD0000000001",
            ParaclinicalKind = "XN",
            IsActive = true
        };
        _paraclinicalRepo.Orders[order.OrderID] = order;

        var handler = new GetRecordOrdersHandler(_paraclinicalRepo, _recordRepo);
        var result = await handler.HandleAsync(new GetRecordOrdersQuery(DivisionId, recordId));

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value);
        Assert.Equal("CD0000000001", result.Value[0].OrderNo);
    }

    // ───────────────────────────── CreateOrders ──────────────────────────────────────

    [Fact]
    public async Task CreateOrders_returns_bad_request_when_services_empty()
    {
        var handler = new CreateOrdersHandler(
            _paraclinicalRepo, _recordRepo, _sessionRepo, _orderNoAllocator, _uow, _audit, _clock);

        var result = await handler.HandleAsync(new CreateOrdersCommand(
            DivisionId, ActorId, ActorName, CurrentActorKind, Guid.NewGuid(), Array.Empty<long>(), "", 101));

        Assert.False(result.IsSuccess);
        Assert.Equal(ApplicationFailureCode.BadRequest, result.Failure.Code);
    }

    [Fact]
    public async Task CreateOrders_returns_session_closed_when_session_closed()
    {
        var sessionId = Guid.NewGuid();
        var recordId = Guid.NewGuid();
        _recordRepo.Records[recordId] = new ExamRecord { RecordID = recordId, SessionID = sessionId, DivisionID = DivisionId };
        _sessionRepo.Sessions[sessionId] = new ExamSession { SessionID = sessionId, DivisionID = DivisionId, State = ExamSessionState.Closed };

        var handler = new CreateOrdersHandler(
            _paraclinicalRepo, _recordRepo, _sessionRepo, _orderNoAllocator, _uow, _audit, _clock);

        var result = await handler.HandleAsync(new CreateOrdersCommand(
            DivisionId, ActorId, ActorName, CurrentActorKind, recordId, new[] { 100001L }, "", 101));

        Assert.False(result.IsSuccess);
        Assert.Equal(ApplicationFailureCode.SessionClosed, result.Failure.Code);
    }

    [Fact]
    public async Task CreateOrders_returns_bad_request_when_unknown_service()
    {
        var sessionId = Guid.NewGuid();
        var recordId = Guid.NewGuid();
        _recordRepo.Records[recordId] = new ExamRecord { RecordID = recordId, SessionID = sessionId, DivisionID = DivisionId, State = ExamRecordState.InProgress };
        _sessionRepo.Sessions[sessionId] = new ExamSession { SessionID = sessionId, DivisionID = DivisionId, State = ExamSessionState.Open };

        var handler = new CreateOrdersHandler(
            _paraclinicalRepo, _recordRepo, _sessionRepo, _orderNoAllocator, _uow, _audit, _clock);

        var result = await handler.HandleAsync(new CreateOrdersCommand(
            DivisionId, ActorId, ActorName, CurrentActorKind, recordId, new[] { 999999L }, "", 101));

        Assert.False(result.IsSuccess);
        Assert.Equal(ApplicationFailureCode.BadRequest, result.Failure.Code);
        Assert.Contains("999999", result.Failure.Message);
    }

    [Fact]
    public async Task CreateOrders_groups_by_kind_and_creates_orders()
    {
        var sessionId = Guid.NewGuid();
        var recordId = Guid.NewGuid();
        _recordRepo.Records[recordId] = new ExamRecord { RecordID = recordId, SessionID = sessionId, DivisionID = DivisionId, State = ExamRecordState.InProgress };
        _sessionRepo.Sessions[sessionId] = new ExamSession { SessionID = sessionId, DivisionID = DivisionId, State = ExamSessionState.Open };

        _paraclinicalRepo.ServiceSnapshots[100001L] = new ExamPackageService
        {
            ServiceID = 100001L,
            ServiceCode = "XN_CTM",
            ServiceName = "Tổng phân tích máu",
            ServiceGroupCode = "XN_HH",
            ParaclinicalKind = "XN"
        };
        _paraclinicalRepo.ServiceSnapshots[200001L] = new ExamPackageService
        {
            ServiceID = 200001L,
            ServiceCode = "CDHA_XQ",
            ServiceName = "X-quang ngực",
            ServiceGroupCode = "CDHA_XQ",
            ParaclinicalKind = "CDHA"
        };

        var handler = new CreateOrdersHandler(
            _paraclinicalRepo, _recordRepo, _sessionRepo, _orderNoAllocator, _uow, _audit, _clock);

        var result = await handler.HandleAsync(new CreateOrdersCommand(
            DivisionId, ActorId, ActorName, CurrentActorKind, recordId, new[] { 100001L, 200001L }, "Ghi chú", 101));

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Count);
        Assert.Equal(1, _uow.SaveCount);
        Assert.Equal(2, _audit.Entries.Count);
    }

    // ───────────────────────────── CreateOrdersFromPackage ───────────────────────────

    [Fact]
    public async Task CreateOrdersFromPackage_returns_bad_request_if_package_missing_on_record_and_command()
    {
        var recordId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        _recordRepo.Records[recordId] = new ExamRecord { RecordID = recordId, SessionID = sessionId, DivisionID = DivisionId, PackageID = null, State = ExamRecordState.InProgress };
        _sessionRepo.Sessions[sessionId] = new ExamSession { SessionID = sessionId, DivisionID = DivisionId, State = ExamSessionState.Open };

        var handler = new CreateOrdersFromPackageHandler(
            _paraclinicalRepo, _recordRepo, _sessionRepo, _orderNoAllocator, _uow, _audit, _clock);

        var result = await handler.HandleAsync(new CreateOrdersFromPackageCommand(
            DivisionId, ActorId, ActorName, CurrentActorKind, recordId, null, "", 101));

        Assert.False(result.IsSuccess);
        Assert.Equal(ApplicationFailureCode.BadRequest, result.Failure.Code);
    }

    [Fact]
    public async Task CreateOrdersFromPackage_rejects_duplicate_services_already_active()
    {
        var recordId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var packageId = Guid.NewGuid();
        _recordRepo.Records[recordId] = new ExamRecord { RecordID = recordId, SessionID = sessionId, DivisionID = DivisionId, PackageID = packageId, State = ExamRecordState.InProgress };
        _sessionRepo.Sessions[sessionId] = new ExamSession { SessionID = sessionId, DivisionID = DivisionId, State = ExamSessionState.Open };

        _paraclinicalRepo.Packages[packageId] = new ExamPackage { PackageID = packageId, DivisionID = DivisionId, PackageCode = "GOI_01" };
        _paraclinicalRepo.PackageServices[packageId] = new List<ExamPackageService>
        {
            new() { ServiceID = 100001L, ServiceCode = "XN_CTM", ParaclinicalKind = "XN", IsActive = true }
        };
        _paraclinicalRepo.ActiveServiceIdsOnRecord[(DivisionId, recordId)] = new List<long> { 100001L };

        var handler = new CreateOrdersFromPackageHandler(
            _paraclinicalRepo, _recordRepo, _sessionRepo, _orderNoAllocator, _uow, _audit, _clock);

        var result = await handler.HandleAsync(new CreateOrdersFromPackageCommand(
            DivisionId, ActorId, ActorName, CurrentActorKind, recordId, packageId, "", 101));

        Assert.False(result.IsSuccess);
        Assert.Equal(ApplicationFailureCode.InvalidState, result.Failure.Code);
        Assert.Contains("đã được chỉ định", result.Failure.Message);
    }

    // ───────────────────────────── CancelOrder ───────────────────────────────────────

    [Fact]
    public async Task CancelOrder_returns_result_exists_when_done_item_present()
    {
        var recordId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        _recordRepo.Records[recordId] = new ExamRecord { RecordID = recordId, SessionID = sessionId, DivisionID = DivisionId };
        _sessionRepo.Sessions[sessionId] = new ExamSession { SessionID = sessionId, DivisionID = DivisionId, State = ExamSessionState.Open };

        var order = new ParaclinicalOrder
        {
            OrderID = orderId,
            DivisionID = DivisionId,
            RecordID = recordId,
            OrderNo = "CD01",
            Items = new List<ParaclinicalOrderItem>
            {
                new() { OrderItemID = Guid.NewGuid(), OrderID = orderId, State = ParaclinicalItemState.Done, ServiceCode = "XN_CTM" },
                new() { OrderItemID = Guid.NewGuid(), OrderID = orderId, State = ParaclinicalItemState.Waiting, ServiceCode = "XN_NT" }
            }
        };
        _paraclinicalRepo.Orders[orderId] = order;

        var handler = new CancelOrderHandler(
            _paraclinicalRepo, _recordRepo, _sessionRepo, _outboxDispatcher, _uow, _audit, _clock);

        var result = await handler.HandleAsync(new CancelOrderCommand(
            DivisionId, ActorId, CurrentActorKind, orderId, null, "Hủy"));

        Assert.False(result.IsSuccess);
        Assert.Equal(ApplicationFailureCode.ParaclinicalResultExists, result.Failure.Code);
    }

    [Fact]
    public async Task CancelOrder_cancels_all_items_and_deactivates_order()
    {
        var recordId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        _recordRepo.Records[recordId] = new ExamRecord { RecordID = recordId, SessionID = sessionId, DivisionID = DivisionId };
        _sessionRepo.Sessions[sessionId] = new ExamSession { SessionID = sessionId, DivisionID = DivisionId, State = ExamSessionState.Open };

        var item1 = new ParaclinicalOrderItem { OrderItemID = Guid.NewGuid(), OrderID = orderId, State = ParaclinicalItemState.Ordered, ServiceCode = "XN_CTM" };
        var item2 = new ParaclinicalOrderItem { OrderItemID = Guid.NewGuid(), OrderID = orderId, State = ParaclinicalItemState.Waiting, ServiceCode = "XN_NT" };
        var order = new ParaclinicalOrder
        {
            OrderID = orderId,
            DivisionID = DivisionId,
            RecordID = recordId,
            OrderNo = "CD01",
            IsActive = true,
            Items = new List<ParaclinicalOrderItem> { item1, item2 }
        };
        _paraclinicalRepo.Orders[orderId] = order;

        var handler = new CancelOrderHandler(
            _paraclinicalRepo, _recordRepo, _sessionRepo, _outboxDispatcher, _uow, _audit, _clock);

        var result = await handler.HandleAsync(new CancelOrderCommand(
            DivisionId, ActorId, CurrentActorKind, orderId, null, "Người bệnh từ chối"));

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.IsActive);
        Assert.All(order.Items, i => Assert.Equal(ParaclinicalItemState.Cancelled, i.State));
        Assert.True(_outboxDispatcher.CancelEnqueued);
        Assert.Equal(1, _uow.SaveCount);
    }

    // ───────────────────────────── ChangeOrderState ───────────────────────────────────

    [Fact]
    public async Task ChangeOrderState_returns_bad_request_for_cancelled_target()
    {
        var handler = new ChangeOrderStateHandler(
            _paraclinicalRepo, _recordRepo, _sessionRepo, _uow, _audit, _clock);

        var result = await handler.HandleAsync(new ChangeOrderStateCommand(
            DivisionId, ActorId, CurrentActorKind, Guid.NewGuid(), (short)ParaclinicalItemState.Cancelled, null));

        Assert.False(result.IsSuccess);
        Assert.Equal(ApplicationFailureCode.BadRequest, result.Failure.Code);
    }

    [Fact]
    public async Task ChangeOrderState_shields_done_items_when_unnamed()
    {
        var recordId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        _recordRepo.Records[recordId] = new ExamRecord { RecordID = recordId, SessionID = sessionId, DivisionID = DivisionId };
        _sessionRepo.Sessions[sessionId] = new ExamSession { SessionID = sessionId, DivisionID = DivisionId, State = ExamSessionState.Open };

        var doneItem = new ParaclinicalOrderItem { OrderItemID = Guid.NewGuid(), OrderID = orderId, State = ParaclinicalItemState.Done, ServiceCode = "XN_CTM" };
        var waitingItem = new ParaclinicalOrderItem { OrderItemID = Guid.NewGuid(), OrderID = orderId, State = ParaclinicalItemState.Waiting, ServiceCode = "XN_NT" };
        var order = new ParaclinicalOrder
        {
            OrderID = orderId,
            DivisionID = DivisionId,
            RecordID = recordId,
            Items = new List<ParaclinicalOrderItem> { doneItem, waitingItem }
        };
        _paraclinicalRepo.Orders[orderId] = order;

        var handler = new ChangeOrderStateHandler(
            _paraclinicalRepo, _recordRepo, _sessionRepo, _uow, _audit, _clock);

        var result = await handler.HandleAsync(new ChangeOrderStateCommand(
            DivisionId, ActorId, CurrentActorKind, orderId, (short)ParaclinicalItemState.InProgress, null));

        Assert.True(result.IsSuccess);
        Assert.Equal(ParaclinicalItemState.Done, doneItem.State);
        Assert.Equal(ParaclinicalItemState.InProgress, waitingItem.State);
    }

    [Fact]
    public async Task ChangeOrderState_single_line_constraint_fails_when_multiple_lines_carry_result_fields()
    {
        var recordId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        _recordRepo.Records[recordId] = new ExamRecord { RecordID = recordId, SessionID = sessionId, DivisionID = DivisionId };
        _sessionRepo.Sessions[sessionId] = new ExamSession { SessionID = sessionId, DivisionID = DivisionId, State = ExamSessionState.Open };

        var item1 = new ParaclinicalOrderItem { OrderItemID = Guid.NewGuid(), OrderID = orderId, State = ParaclinicalItemState.Waiting, ServiceCode = "XN_CTM" };
        var item2 = new ParaclinicalOrderItem { OrderItemID = Guid.NewGuid(), OrderID = orderId, State = ParaclinicalItemState.Waiting, ServiceCode = "XN_NT" };
        var order = new ParaclinicalOrder
        {
            OrderID = orderId,
            DivisionID = DivisionId,
            RecordID = recordId,
            Items = new List<ParaclinicalOrderItem> { item1, item2 }
        };
        _paraclinicalRepo.Orders[orderId] = order;

        var handler = new ChangeOrderStateHandler(
            _paraclinicalRepo, _recordRepo, _sessionRepo, _uow, _audit, _clock);

        var result = await handler.HandleAsync(new ChangeOrderStateCommand(
            DivisionId, ActorId, CurrentActorKind, orderId, (short)ParaclinicalItemState.Done, null, IsAbnormal: true));

        Assert.False(result.IsSuccess);
        Assert.Equal(ApplicationFailureCode.BadRequest, result.Failure.Code);
    }
}

// ───────────────────────────── Test Fakes ────────────────────────────────────────────

public class FakeParaclinicalRepository : IParaclinicalRepository
{
    public Dictionary<Guid, ParaclinicalOrder> Orders { get; } = new();
    public Dictionary<Guid, ExamPackage> Packages { get; } = new();
    public Dictionary<Guid, List<ExamPackageService>> PackageServices { get; } = new();
    public Dictionary<long, ExamPackageService> ServiceSnapshots { get; } = new();
    public Dictionary<(string DivisionId, Guid RecordId), List<long>> ActiveServiceIdsOnRecord { get; } = new();
    public (bool Satisfied, int Pending, int Total) ConditionBResult { get; set; } = (true, 0, 0);

    public Task<ParaclinicalOrder> GetAsync(
        string divisionId, Guid orderId, bool includeItems = true, bool forUpdate = false, CancellationToken ct = default)
    {
        Orders.TryGetValue(orderId, out var order);
        return Task.FromResult(order);
    }

    public Task<IReadOnlyList<ParaclinicalOrder>> ListByRecordAsync(
        string divisionId, Guid recordId, bool includeCancelled = true, bool forUpdate = false, CancellationToken ct = default)
    {
        var list = Orders.Values
            .Where(o => o.RecordID == recordId && o.DivisionID == divisionId && o.IsActive)
            .ToList();
        return Task.FromResult<IReadOnlyList<ParaclinicalOrder>>(list);
    }

    public Task<IReadOnlyList<ExamPackageService>> ListActivePackageServicesAsync(
        string divisionId, Guid packageId, CancellationToken ct = default)
    {
        if (PackageServices.TryGetValue(packageId, out var list))
            return Task.FromResult<IReadOnlyList<ExamPackageService>>(list);
        return Task.FromResult<IReadOnlyList<ExamPackageService>>(Array.Empty<ExamPackageService>());
    }

    public Task<IReadOnlyDictionary<long, ExamPackageService>> SnapshotServicesAsync(
        string divisionId, IReadOnlyCollection<long> serviceIds, CancellationToken ct = default)
    {
        var dict = ServiceSnapshots
            .Where(kv => serviceIds.Contains(kv.Key))
            .ToDictionary(kv => kv.Key, kv => kv.Value);
        return Task.FromResult<IReadOnlyDictionary<long, ExamPackageService>>(dict);
    }

    public Task<ExamPackage> GetPackageAsync(
        string divisionId, Guid packageId, CancellationToken ct = default)
    {
        Packages.TryGetValue(packageId, out var pkg);
        return Task.FromResult(pkg);
    }

    public Task<IReadOnlyList<long>> ListActiveServiceIdsByRecordAsync(
        string divisionId, Guid recordId, CancellationToken ct = default)
    {
        if (ActiveServiceIdsOnRecord.TryGetValue((divisionId, recordId), out var list))
            return Task.FromResult<IReadOnlyList<long>>(list);
        return Task.FromResult<IReadOnlyList<long>>(Array.Empty<long>());
    }

    public Task<(bool Satisfied, int Pending, int Total)> EvaluateConditionBAsync(
        string divisionId, Guid recordId, CancellationToken ct = default)
    {
        return Task.FromResult(ConditionBResult);
    }

    public Task<ParaclinicalOrderItem> GetItemByVendorLineNoAsync(
        string divisionId, long vendorLineNo, bool includeOrder = true, bool forUpdate = false, CancellationToken ct = default)
    {
        var item = Orders.Values
            .Where(o => o.DivisionID == divisionId)
            .SelectMany(o => o.Items ?? Enumerable.Empty<ParaclinicalOrderItem>())
            .FirstOrDefault(i => i.VendorLineNo == vendorLineNo);
        return Task.FromResult(item);
    }

    public Dictionary<Guid, ParaclinicalResult> Results { get; } = new();

    public Task<IReadOnlyList<ParaclinicalResult>> ListResultsByOrderAsync(string divisionId, Guid orderId, CancellationToken ct = default)
    {
        var list = Results.Values.Where(r => r.DivisionID == divisionId && r.OrderId == orderId).ToList();
        return Task.FromResult<IReadOnlyList<ParaclinicalResult>>(list);
    }

    public Task<ParaclinicalResult> GetResultAsync(string divisionId, Guid resultId, CancellationToken ct = default)
    {
        Results.TryGetValue(resultId, out var res);
        return Task.FromResult(res);
    }

    public void Add(ParaclinicalOrder order)
    {
        Orders[order.OrderID] = order;
    }

    public void AddResult(ParaclinicalResult result)
    {
        Results[result.ResultId] = result;
    }
}

public class FakeOrderNoAllocator : IOrderNoAllocator
{
    private int _counter = 0;

    public Task<string> NextAsync(CancellationToken ct = default)
    {
        _counter++;
        return Task.FromResult($"CD{_counter:D10}");
    }
}

public class FakeFormServerClient : HealthExam.Application.Paraclinical.IFormServerClient
{
    public SubmissionProgressResult ProgressResult { get; set; } = new(17, 17, true);
    public SignSubmissionResult SignResult { get; set; } = new(true, "Signed");
    public SignSubmissionRequest LastSignRequest { get; private set; }

    public Task<SubmissionProgressResult> GetProgressAsync(Guid submissionId, CancellationToken ct = default)
        => Task.FromResult(ProgressResult);

    public Task<SignSubmissionResult> SignAsync(Guid submissionId, SignSubmissionRequest request, CancellationToken ct = default)
    {
        LastSignRequest = request;
        return Task.FromResult(SignResult);
    }
}

public class FakeParaclinicalOutboxDispatcher : IParaclinicalOutboxDispatcher
{
    public bool CancelEnqueued { get; private set; }

    public void EnqueueCancel(ParaclinicalOrder order, ExamRecord record, IReadOnlyList<ParaclinicalOrderItem> cancelledLines, DateTime nowUtc)
    {
        CancelEnqueued = true;
    }
}

public class FakeClock : IClock
{
    public DateTime UtcNow { get; set; }
    public FakeClock(DateTime utcNow) => UtcNow = utcNow;
}
