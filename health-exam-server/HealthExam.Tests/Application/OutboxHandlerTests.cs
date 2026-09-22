using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HealthExam.Application.Common;
using HealthExam.Application.Integrations;
using HealthExam.Application.Paraclinical;
using HealthExam.Domain.Common;
using HealthExam.Domain.ExamRecords;
using HealthExam.Domain.Integrations;
using HealthExam.Domain.Paraclinical;
using Xunit;

namespace HealthExam.Tests.Application;

public class OutboxHandlerTests
{
    private const string DivisionId = "tenant-test";

    [Fact]
    public async Task Failed_send_schedules_retry_without_losing_row()
    {
        var row = new IntegrationOutbox
        {
            OutboxID = 101,
            DivisionID = DivisionId,
            OrderID = Guid.NewGuid(),
            Vendor = "RIS",
            Operation = "NEW",
            DedupKey = "RIS:NEW:CD0001",
            Payload = "{\"test\":true}",
            State = OutboxState.Pending,
            RetryCount = 0
        };

        var repo = new FakeOutboxRepository(row);
        var paraclinicalRepo = new FakeParaclinicalRepositoryForOutbox();
        var client = new FakeRisClient(RisSendResult.DependencyFailure("timeout"));
        var clock = new FakeClock(new DateTime(2026, 9, 10, 8, 0, 0, DateTimeKind.Utc));
        var uow = new FakeUnitOfWork();
        var audit = new FakeAuditRepository();

        var handler = new ProcessOutboxBatchHandler(repo, paraclinicalRepo, client, uow, audit, clock);

        var result = await handler.HandleAsync(new ProcessOutboxBatchCommand(1, DivisionId));

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value.Failed);
        Assert.Equal(OutboxState.Failed, row.State);
        Assert.Equal(1, row.RetryCount);
        Assert.NotNull(row.NextAttemptAt);
        Assert.True(row.NextAttemptAt > clock.UtcNow);
    }

    [Fact]
    public async Task Successful_send_advances_ordered_items_to_waiting_and_stamps_sent()
    {
        var orderId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var row = new IntegrationOutbox
        {
            OutboxID = 102,
            DivisionID = DivisionId,
            OrderID = orderId,
            Vendor = "RIS",
            Operation = "NEW",
            DedupKey = "RIS:NEW:CD0002",
            Payload = "{\"test\":true}",
            State = OutboxState.Pending,
            RetryCount = 0
        };

        var order = new ParaclinicalOrder
        {
            OrderID = orderId,
            DivisionID = DivisionId,
            OrderNo = "CD0002",
            SentStatus = OrderSentStatus.NotSent,
            Items = new List<ParaclinicalOrderItem>
            {
                new()
                {
                    OrderItemID = itemId,
                    OrderID = orderId,
                    DivisionID = DivisionId,
                    ServiceCode = "XQ01",
                    VendorLineNo = 1001,
                    State = ParaclinicalItemState.Ordered
                }
            }
        };

        var repo = new FakeOutboxRepository(row);
        var paraclinicalRepo = new FakeParaclinicalRepositoryForOutbox(order);
        var client = new FakeRisClient(RisSendResult.Sent("OK"));
        var clock = new FakeClock(new DateTime(2026, 9, 10, 8, 0, 0, DateTimeKind.Utc));
        var uow = new FakeUnitOfWork();
        var audit = new FakeAuditRepository();

        var handler = new ProcessOutboxBatchHandler(repo, paraclinicalRepo, client, uow, audit, clock);

        var result = await handler.HandleAsync(new ProcessOutboxBatchCommand(1, DivisionId));

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value.Sent);
        Assert.Equal(OutboxState.Sent, row.State);
        Assert.NotNull(row.SentAt);
        Assert.Null(row.NextAttemptAt);
        Assert.Equal(OrderSentStatus.Sent, order.SentStatus);
        Assert.Equal(ParaclinicalItemState.Waiting, order.Items.First().State);
    }

    [Fact]
    public async Task Outbox_deadletters_when_max_retry_exceeded()
    {
        var row = new IntegrationOutbox
        {
            OutboxID = 103,
            DivisionID = DivisionId,
            OrderID = Guid.NewGuid(),
            Vendor = "RIS",
            Operation = "NEW",
            DedupKey = "RIS:NEW:CD0003",
            Payload = "{\"test\":true}",
            State = OutboxState.Failed,
            RetryCount = 5
        };

        var repo = new FakeOutboxRepository(row);
        var paraclinicalRepo = new FakeParaclinicalRepositoryForOutbox();
        var client = new FakeRisClient(RisSendResult.DependencyFailure("timeout"));
        var clock = new FakeClock(new DateTime(2026, 9, 10, 8, 0, 0, DateTimeKind.Utc));
        var uow = new FakeUnitOfWork();
        var audit = new FakeAuditRepository();

        var handler = new ProcessOutboxBatchHandler(repo, paraclinicalRepo, client, uow, audit, clock);

        var result = await handler.HandleAsync(new ProcessOutboxBatchCommand(1, DivisionId));

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value.DeadLettered);
        Assert.Equal(OutboxState.DeadLetter, row.State);
        Assert.Null(row.NextAttemptAt);
        Assert.Equal(0, client.SendCallCount);
    }
}

// ──────────────────────────────── Fake Test Helpers ─────────────────────────────────────

public class FakeOutboxRepository : IIntegrationOutboxRepository
{
    public IntegrationOutbox Row { get; }
    public List<IntegrationOutbox> Added { get; } = new();

    public FakeOutboxRepository(IntegrationOutbox row = null)
    {
        Row = row;
    }

    public Task<IntegrationOutbox> ClaimNextAsync(string divisionId, DateTime nowUtc, CancellationToken ct = default)
        => ClaimNextAsync(divisionId, nowUtc, null, ct);

    public Task<IntegrationOutbox> ClaimNextAsync(string divisionId, DateTime nowUtc, string vendor, CancellationToken ct = default)
    {
        if (Row != null && Row.DivisionID == divisionId && (vendor == null || Row.Vendor == vendor) && (Row.State == OutboxState.Pending || Row.State == OutboxState.Failed))
            return Task.FromResult(Row);
        return Task.FromResult<IntegrationOutbox>(null);
    }

    public Task<IntegrationOutbox> LockAsync(long outboxId, CancellationToken ct = default)
    {
        if (Row != null && Row.OutboxID == outboxId) return Task.FromResult(Row);
        return Task.FromResult<IntegrationOutbox>(null);
    }

    public Task<bool> WasSentAsync(Guid orderId, string messageType, CancellationToken ct = default)
    {
        return Task.FromResult(Row != null && Row.OrderID == orderId && Row.Operation == messageType && Row.State == OutboxState.Sent);
    }

    public Task<IntegrationOutbox> FindByDedupKeyAsync(string divisionId, string dedupKey, CancellationToken ct = default)
    {
        if (Row != null && Row.DivisionID == divisionId && Row.DedupKey == dedupKey) return Task.FromResult(Row);
        return Task.FromResult(Added.FirstOrDefault(x => x.DivisionID == divisionId && x.DedupKey == dedupKey));
    }

    public Task<bool> HasSentCancelAsync(string divisionId, Guid orderId, CancellationToken ct = default)
    {
        return Task.FromResult(Row != null && Row.DivisionID == divisionId && Row.OrderID == orderId && Row.Operation == "CANCELLED" && Row.State == OutboxState.Sent);
    }

    public Task<int> CountPendingAsync(string divisionId, DateTime nowUtc, CancellationToken ct = default)
        => CountPendingAsync(divisionId, nowUtc, null, ct);

    public Task<int> CountPendingAsync(string divisionId, DateTime nowUtc, string vendor, CancellationToken ct = default)
    {
        var count = (Row != null && Row.DivisionID == divisionId && (vendor == null || Row.Vendor == vendor) && (Row.State == OutboxState.Pending || Row.State == OutboxState.Failed)) ? 1 : 0;
        return Task.FromResult(count);
    }

    public Task<int> CountDeadLetterAsync(string divisionId, CancellationToken ct = default)
    {
        var count = (Row != null && Row.DivisionID == divisionId && Row.State == OutboxState.DeadLetter) ? 1 : 0;
        return Task.FromResult(count);
    }

    public void Add(IntegrationOutbox row)
    {
        Added.Add(row);
    }
}

public class FakeParaclinicalRepositoryForOutbox : IParaclinicalRepository
{
    private readonly ParaclinicalOrder _order;

    public FakeParaclinicalRepositoryForOutbox(ParaclinicalOrder order = null)
    {
        _order = order;
    }

    public Task<ParaclinicalOrder> GetAsync(string divisionId, Guid orderId, bool includeItems = true, bool forUpdate = false, CancellationToken ct = default)
    {
        if (_order != null && _order.DivisionID == divisionId && _order.OrderID == orderId) return Task.FromResult(_order);
        return Task.FromResult<ParaclinicalOrder>(null);
    }

    public Task<IReadOnlyList<ParaclinicalOrder>> ListByRecordAsync(string divisionId, Guid recordId, bool includeCancelled = true, bool forUpdate = false, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<ParaclinicalOrder>>(new List<ParaclinicalOrder>());

    public Task<IReadOnlyList<HealthExam.Domain.Catalogs.ExamPackageService>> ListActivePackageServicesAsync(string divisionId, Guid packageId, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<HealthExam.Domain.Catalogs.ExamPackageService>>(new List<HealthExam.Domain.Catalogs.ExamPackageService>());

    public Task<IReadOnlyDictionary<long, HealthExam.Domain.Catalogs.ExamPackageService>> SnapshotServicesAsync(string divisionId, IReadOnlyCollection<long> serviceIds, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyDictionary<long, HealthExam.Domain.Catalogs.ExamPackageService>>(new Dictionary<long, HealthExam.Domain.Catalogs.ExamPackageService>());

    public Task<HealthExam.Domain.Catalogs.ExamPackage> GetPackageAsync(string divisionId, Guid packageId, CancellationToken ct = default)
        => Task.FromResult<HealthExam.Domain.Catalogs.ExamPackage>(null);

    public Task<IReadOnlyList<long>> ListActiveServiceIdsByRecordAsync(string divisionId, Guid recordId, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<long>>(new List<long>());

    public Task<(bool Satisfied, int Pending, int Total)> EvaluateConditionBAsync(string divisionId, Guid recordId, CancellationToken ct = default)
        => Task.FromResult((true, 0, 0));

    public Task<ParaclinicalOrderItem> GetItemByVendorLineNoAsync(string divisionId, long vendorLineNo, bool includeOrder = true, bool forUpdate = false, CancellationToken ct = default)
    {
        var item = _order?.Items?.FirstOrDefault(x => x.VendorLineNo == vendorLineNo);
        return Task.FromResult(item);
    }

    public Task<IReadOnlyList<ParaclinicalResult>> ListResultsByOrderAsync(string divisionId, Guid orderId, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<ParaclinicalResult>>(new List<ParaclinicalResult>());

    public Task<ParaclinicalResult> GetResultAsync(string divisionId, Guid resultId, CancellationToken ct = default)
        => Task.FromResult<ParaclinicalResult>(null);

    public void Add(ParaclinicalOrder order) { }
    public void AddResult(ParaclinicalResult result) { }
}

public class FakeRisClient : IRisClient
{
    private readonly RisSendResult _result;
    public int SendCallCount { get; private set; }

    public FakeRisClient(RisSendResult result)
    {
        _result = result;
    }

    public Task<RisSendResult> SendOrderAsync(string payload, long outboxId, CancellationToken ct = default)
    {
        SendCallCount++;
        return Task.FromResult(_result);
    }
}
