using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using HealthExam.Application.Common;
using HealthExam.Application.Paraclinical;
using HealthExam.Domain.Common;
using HealthExam.Domain.ExamRecords;
using HealthExam.Domain.ExamSessions;
using HealthExam.Domain.Paraclinical;
using HealthExam.Tests.Application;
using Xunit;

namespace HealthExam.Tests.Paraclinical;

public class ParaclinicalLocalOrderApiTests
{
    private readonly FakeParaclinicalRepository _paraclinicalRepo = new();
    private readonly FakeExamRecordRepository _recordRepo = new();
    private readonly FakeExamSessionRepository _sessionRepo = new();
    private readonly FakeOrderNoAllocator _orderNoAllocator = new();
    private readonly FakeUnitOfWork _uow = new();
    private readonly FakeAuditRepository _auditRepo = new();
    private readonly FakeClock _clock = new(new DateTime(2026, 9, 18, 10, 0, 0, DateTimeKind.Utc));

    private CreateOrdersHandler CreateOrdersHandler()
    {
        return new CreateOrdersHandler(
            _paraclinicalRepo,
            _recordRepo,
            _sessionRepo,
            _orderNoAllocator,
            _uow,
            _auditRepo,
            _clock);
    }

    [Fact]
    public async Task CreateOrders_creates_order_in_Draft_status_with_his_linkage_prepared()
    {
        var divisionId = "DEV";
        var recordId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();

        var session = new ExamSession
        {
            SessionID = sessionId,
            DivisionID = divisionId,
            SessionCode = "DOT-01",
            State = ExamSessionState.Open
        };
        _sessionRepo.Sessions[sessionId] = session;

        var record = new ExamRecord
        {
            RecordID = recordId,
            DivisionID = divisionId,
            SessionID = sessionId,
            RecordCode = "HS-001",
            AdmissionID = 999123,
            State = ExamRecordState.Waiting
        };
        _recordRepo.Records[recordId] = record;

        _paraclinicalRepo.ServiceSnapshots[101] = new HealthExam.Domain.Catalogs.ExamPackageService
        {
            ServiceID = 101,
            ServiceCode = "XN01",
            ServiceName = "Tổng phân tích máu",
            ServiceGroupCode = "XN_HUYETHOC",
            ParaclinicalKind = "XN",
            Quantity = 1
        };

        var handler = CreateOrdersHandler();
        var command = new CreateOrdersCommand(
            DivisionId: divisionId,
            ActorId: "55",
            ActorName: "BS. Hoàng",
            ActorKind: ActorKind.Employee,
            RecordId: recordId,
            ServiceIds: new[] { 101L },
            Note: "Chỉ định định kỳ",
            RoomId: 12);

        var result = await handler.HandleAsync(command);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value);

        var created = result.Value[0];
        Assert.Equal("Draft", created.StatusName);
        Assert.Equal((short)ParaclinicalOrderStatus.Draft, created.Status);
        Assert.Equal(999123, created.HisAdmissionId);
        Assert.Equal($"HEX-{recordId:N}", created.HisAdmissionCode);
        Assert.Single(created.Items);
        Assert.Equal(101, created.Items[0].ServiceID);
    }

    [Fact]
    public async Task GetOrderResults_and_GetResult_return_persisted_results()
    {
        var divisionId = "DEV";
        var orderId = Guid.NewGuid();
        var resultId = Guid.NewGuid();

        var order = new ParaclinicalOrder
        {
            OrderID = orderId,
            DivisionID = divisionId,
            RecordID = Guid.NewGuid(),
            OrderNo = "ORD-001",
            Status = ParaclinicalOrderStatus.Completed
        };
        _paraclinicalRepo.Orders[orderId] = order;

        var result = new ParaclinicalResult
        {
            ResultId = resultId,
            OrderId = orderId,
            DivisionID = divisionId,
            HisResultId = "HIS-RES-777",
            ResultDate = DateTime.UtcNow,
            Status = "Completed",
            Items = new List<ParaclinicalResultItem>
            {
                new()
                {
                    ResultItemId = Guid.NewGuid(),
                    ResultId = resultId,
                    DivisionID = divisionId,
                    HisDetailId = "HIS-DTL-1",
                    Value = "5.2",
                    Unit = "mmol/L",
                    ReferenceRange = "3.9 - 6.4",
                    AbnormalFlag = "NORMAL"
                }
            }
        };
        _paraclinicalRepo.AddResult(result);

        var orderResultsHandler = new GetOrderResultsHandler(_paraclinicalRepo);
        var orderResultsRes = await orderResultsHandler.HandleAsync(new GetOrderResultsQuery(divisionId, orderId));

        Assert.True(orderResultsRes.IsSuccess);
        Assert.Single(orderResultsRes.Value);
        Assert.Equal("HIS-RES-777", orderResultsRes.Value[0].HisResultId);
        Assert.Single(orderResultsRes.Value[0].Items);
        Assert.Equal("5.2", orderResultsRes.Value[0].Items[0].Value);

        var singleResultHandler = new GetResultHandler(_paraclinicalRepo);
        var singleResultRes = await singleResultHandler.HandleAsync(new GetResultQuery(divisionId, resultId));

        Assert.True(singleResultRes.IsSuccess);
        Assert.Equal(resultId, singleResultRes.Value.ResultId);
        Assert.Equal("HIS-RES-777", singleResultRes.Value.HisResultId);
    }
}
