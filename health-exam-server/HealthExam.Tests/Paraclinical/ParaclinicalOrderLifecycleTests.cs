using System;
using HealthExam.Domain.Common;
using HealthExam.Domain.Paraclinical;
using Xunit;

namespace HealthExam.Tests.Paraclinical;

public class ParaclinicalOrderLifecycleTests
{
    private ParaclinicalOrder CreateDraftOrder()
    {
        return new ParaclinicalOrder
        {
            DivisionID = "DIV1",
            RecordID = Guid.NewGuid(),
            Status = ParaclinicalOrderStatus.Draft
        };
    }

    [Fact]
    public void Submit_from_Draft_succeeds_and_sets_status_and_timestamp()
    {
        var order = CreateDraftOrder();
        var submitTime = DateTime.UtcNow;

        var result = order.Submit(submitTime);

        Assert.True(result.IsSuccess);
        Assert.Equal(ParaclinicalOrderStatus.Submitted, order.Status);
        Assert.Equal(submitTime, order.SubmittedAt);
        Assert.Equal(2, order.Version);
    }

    [Fact]
    public void Submit_when_not_Draft_returns_failure()
    {
        var order = CreateDraftOrder();
        order.Submit(DateTime.UtcNow);

        var result = order.Submit(DateTime.UtcNow);
        Assert.False(result.IsSuccess);
        Assert.NotNull(result.Failure);
        Assert.Equal(DomainFailureCode.InvalidTransition, result.Failure.Code);
    }

    [Fact]
    public void MarkOrdered_from_Submitted_sets_HisParaClinReqId_and_status()
    {
        var order = CreateDraftOrder();
        var now = DateTime.UtcNow;
        order.Submit(now);

        var result = order.MarkOrdered(123456, now);

        Assert.True(result.IsSuccess);
        Assert.Equal(ParaclinicalOrderStatus.Ordered, order.Status);
        Assert.Equal(123456, order.HisParaClinReqId);
        Assert.Equal(now, order.LastSyncAt);
    }

    [Fact]
    public void MarkOrdered_when_Draft_returns_failure()
    {
        var order = CreateDraftOrder();

        var result = order.MarkOrdered(123456, DateTime.UtcNow);
        Assert.False(result.IsSuccess);
        Assert.NotNull(result.Failure);
        Assert.Equal(DomainFailureCode.InvalidTransition, result.Failure.Code);
    }

    [Fact]
    public void MarkInProgress_from_Ordered_sets_status()
    {
        var order = CreateDraftOrder();
        var now = DateTime.UtcNow;
        order.Submit(now);
        order.MarkOrdered(123456, now);

        var result = order.MarkInProgress(now);

        Assert.True(result.IsSuccess);
        Assert.Equal(ParaclinicalOrderStatus.InProgress, order.Status);
    }

    [Fact]
    public void MarkInProgress_when_Draft_returns_failure()
    {
        var order = CreateDraftOrder();

        var result = order.MarkInProgress(DateTime.UtcNow);
        Assert.False(result.IsSuccess);
        Assert.NotNull(result.Failure);
        Assert.Equal(DomainFailureCode.InvalidTransition, result.Failure.Code);
    }

    [Fact]
    public void MarkCompleted_from_InProgress_sets_status()
    {
        var order = CreateDraftOrder();
        var now = DateTime.UtcNow;
        order.Submit(now);
        order.MarkOrdered(123456, now);
        order.MarkInProgress(now);

        var result = order.MarkCompleted(now);

        Assert.True(result.IsSuccess);
        Assert.Equal(ParaclinicalOrderStatus.Completed, order.Status);
    }

    [Fact]
    public void MarkCompleted_directly_from_Ordered_sets_status()
    {
        var order = CreateDraftOrder();
        var now = DateTime.UtcNow;
        order.Submit(now);
        order.MarkOrdered(123456, now);

        var result = order.MarkCompleted(now);

        Assert.True(result.IsSuccess);
        Assert.Equal(ParaclinicalOrderStatus.Completed, order.Status);
    }

    [Fact]
    public void MarkCompleted_when_Cancelled_returns_failure()
    {
        var order = CreateDraftOrder();
        order.Cancel("Test cancel", DateTime.UtcNow);

        var result = order.MarkCompleted(DateTime.UtcNow);
        Assert.False(result.IsSuccess);
        Assert.NotNull(result.Failure);
        Assert.Equal(DomainFailureCode.InvalidTransition, result.Failure.Code);
    }

    [Fact]
    public void Cancel_from_Draft_or_Submitted_or_Ordered_succeeds()
    {
        var order = CreateDraftOrder();
        var cancelTime = DateTime.UtcNow;

        var result = order.Cancel("Patient left", cancelTime);

        Assert.True(result.IsSuccess);
        Assert.Equal(ParaclinicalOrderStatus.Cancelled, order.Status);
        Assert.Equal(cancelTime, order.CancelledAt);
        Assert.Contains("Patient left", order.Note);
    }

    [Fact]
    public void Cancel_when_Completed_returns_failure()
    {
        var order = CreateDraftOrder();
        var now = DateTime.UtcNow;
        order.Submit(now);
        order.MarkOrdered(123456, now);
        order.MarkCompleted(now);

        var result = order.Cancel("Too late", now);
        Assert.False(result.IsSuccess);
        Assert.NotNull(result.Failure);
        Assert.Equal(DomainFailureCode.InvalidTransition, result.Failure.Code);
    }
}
