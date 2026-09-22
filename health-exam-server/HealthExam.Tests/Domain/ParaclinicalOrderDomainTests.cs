using System;
using HealthExam.Domain.Common;
using HealthExam.Domain.Paraclinical;
using Xunit;

namespace HealthExam.Tests.Domain;

public static class ParaclinicalTestData
{
    public static ParaclinicalOrderItem Item(ParaclinicalItemState state = ParaclinicalItemState.Ordered)
        => new()
        {
            OrderItemID = Guid.NewGuid(),
            OrderID = Guid.NewGuid(),
            RecordID = Guid.NewGuid(),
            DivisionID = "D01",
            ServiceID = 100001,
            ServiceCode = "XN_CTM",
            ServiceName = "Tổng phân tích tế bào máu",
            ServiceGroupCode = "XN_HUYETHOC",
            Quantity = 1,
            State = state
        };
}

public class ParaclinicalOrderDomainTests
{
    // ───────────────────────────── Manual Transitions ─────────────────────────────────

    [Theory]
    [InlineData(ParaclinicalItemState.Ordered, ParaclinicalItemState.Waiting)]
    [InlineData(ParaclinicalItemState.Waiting, ParaclinicalItemState.InProgress)]
    [InlineData(ParaclinicalItemState.InProgress, ParaclinicalItemState.Done)]
    public void Manual_transition_applies_allowed_path(
        ParaclinicalItemState source, ParaclinicalItemState target)
    {
        var item = ParaclinicalTestData.Item(source);
        var now = DateTime.UtcNow;

        var result = item.TransitionTo(target, ParaclinicalStateSource.Manual, now);

        Assert.True(result.Applied);
        Assert.True(result.IsSuccess);
        Assert.Equal(target, item.State);
        Assert.Equal(now, item.ModifiedDate);
    }

    [Fact]
    public void Manual_transition_to_in_progress_sets_performed_at_if_null()
    {
        var item = ParaclinicalTestData.Item(ParaclinicalItemState.Waiting);
        var now = DateTime.UtcNow;

        var result = item.TransitionTo(ParaclinicalItemState.InProgress, ParaclinicalStateSource.Manual, now);

        Assert.True(result.Applied);
        Assert.Equal(now, item.PerformedAt);
    }

    [Fact]
    public void Manual_transition_to_in_progress_preserves_existing_performed_at()
    {
        var existingPerformedAt = new DateTime(2026, 8, 1, 10, 0, 0, DateTimeKind.Utc);
        var item = ParaclinicalTestData.Item(ParaclinicalItemState.Waiting);
        item.PerformedAt = existingPerformedAt;
        var now = DateTime.UtcNow;

        var result = item.TransitionTo(ParaclinicalItemState.InProgress, ParaclinicalStateSource.Manual, now);

        Assert.True(result.Applied);
        Assert.Equal(existingPerformedAt, item.PerformedAt);
    }

    [Fact]
    public void Manual_transition_to_done_sets_result_fields()
    {
        var item = ParaclinicalTestData.Item(ParaclinicalItemState.InProgress);
        var now = DateTime.UtcNow;
        var attachmentId = Guid.NewGuid();

        var result = item.TransitionTo(
            ParaclinicalItemState.Done,
            ParaclinicalStateSource.Manual,
            now,
            attachmentId: attachmentId,
            resultSourceKind: ParaclinicalResultSources.Manual,
            isAbnormal: true,
            resultRefId: "REF-001",
            messageId: "MSG-001");

        Assert.True(result.Applied);
        Assert.Equal(ParaclinicalItemState.Done, item.State);
        Assert.Equal(now, item.ResultAt);
        Assert.Equal(ParaclinicalResultSources.Manual, item.ResultSourceKind);
        Assert.Equal(attachmentId, item.AttachmentID);
        Assert.True(item.IsAbnormal);
        Assert.Equal("REF-001", item.ResultRefID);
        Assert.Equal("MSG-001", item.MessageID);
    }

    [Fact]
    public void Manual_transition_from_done_to_waiting_clears_result_fields()
    {
        var item = ParaclinicalTestData.Item(ParaclinicalItemState.Done);
        item.ResultAt = DateTime.UtcNow.AddHours(-1);
        item.ResultSourceKind = ParaclinicalResultSources.Scan;
        item.AttachmentID = Guid.NewGuid();
        item.IsAbnormal = true;
        item.ResultRefID = "REF-OLD";

        var now = DateTime.UtcNow;
        var result = item.TransitionTo(ParaclinicalItemState.Waiting, ParaclinicalStateSource.Manual, now);

        Assert.True(result.Applied);
        Assert.Equal(ParaclinicalItemState.Waiting, item.State);
        Assert.Null(item.ResultAt);
        Assert.Empty(item.ResultSourceKind);
        Assert.Null(item.AttachmentID);
        Assert.Null(item.IsAbnormal);
        Assert.Null(item.ResultRefID);
    }

    [Fact]
    public void Transition_to_same_state_is_noop()
    {
        var item = ParaclinicalTestData.Item(ParaclinicalItemState.Waiting);
        var now = DateTime.UtcNow;

        var result = item.TransitionTo(ParaclinicalItemState.Waiting, ParaclinicalStateSource.Manual, now);

        Assert.False(result.Applied);
        Assert.True(result.IsSuccess);
        Assert.Equal(ParaclinicalTransitionOutcome.NoOp, result.Outcome);
        Assert.Equal(ParaclinicalItemState.Waiting, item.State);
    }

    // ───────────────────────────── Rejected Transitions ───────────────────────────────

    [Theory]
    [InlineData(ParaclinicalItemState.Ordered)]
    [InlineData(ParaclinicalItemState.Waiting)]
    [InlineData(ParaclinicalItemState.InProgress)]
    [InlineData(ParaclinicalItemState.Done)]
    public void Transition_from_cancelled_is_always_rejected(ParaclinicalItemState target)
    {
        var item = ParaclinicalTestData.Item(ParaclinicalItemState.Cancelled);
        var now = DateTime.UtcNow;

        var result = item.TransitionTo(target, ParaclinicalStateSource.Internal, now);

        Assert.False(result.Applied);
        Assert.False(result.IsSuccess);
        Assert.Equal(ParaclinicalTransitionOutcome.Rejected, result.Outcome);
        Assert.Equal(ParaclinicalItemState.Cancelled, item.State);
    }

    [Theory]
    [InlineData(ParaclinicalItemState.Ordered)]
    [InlineData(ParaclinicalItemState.Waiting)]
    [InlineData(ParaclinicalItemState.InProgress)]
    [InlineData(ParaclinicalItemState.Done)]
    public void Transition_to_cancelled_is_rejected(ParaclinicalItemState source)
    {
        var item = ParaclinicalTestData.Item(source);
        var now = DateTime.UtcNow;

        var result = item.TransitionTo(ParaclinicalItemState.Cancelled, ParaclinicalStateSource.Internal, now);

        Assert.False(result.Applied);
        Assert.False(result.IsSuccess);
        Assert.Equal(ParaclinicalTransitionOutcome.Rejected, result.Outcome);
        Assert.Equal(source, item.State);
    }

    [Theory]
    [InlineData(ParaclinicalItemState.Ordered)]
    [InlineData(ParaclinicalItemState.Done)]
    [InlineData(ParaclinicalItemState.Cancelled)]
    public void Vendor_transition_to_disallowed_target_is_rejected(ParaclinicalItemState target)
    {
        var item = ParaclinicalTestData.Item(ParaclinicalItemState.Waiting);
        var now = DateTime.UtcNow;

        var result = item.TransitionTo(target, ParaclinicalStateSource.Vendor, now);

        Assert.False(result.Applied);
        Assert.False(result.IsSuccess);
        Assert.Equal(ParaclinicalTransitionOutcome.Rejected, result.Outcome);
    }

    [Fact]
    public void Vendor_backward_transition_is_rejected()
    {
        var item = ParaclinicalTestData.Item(ParaclinicalItemState.InProgress);
        var now = DateTime.UtcNow;

        var result = item.TransitionTo(ParaclinicalItemState.Waiting, ParaclinicalStateSource.Vendor, now);

        Assert.False(result.Applied);
        Assert.False(result.IsSuccess);
        Assert.Equal(ParaclinicalTransitionOutcome.Rejected, result.Outcome);
        Assert.Equal(ParaclinicalItemState.InProgress, item.State);
    }

    [Fact]
    public void Vendor_same_state_is_noop()
    {
        var item = ParaclinicalTestData.Item(ParaclinicalItemState.InProgress);
        var now = DateTime.UtcNow;

        var result = item.TransitionTo(ParaclinicalItemState.InProgress, ParaclinicalStateSource.Vendor, now);

        Assert.False(result.Applied);
        Assert.True(result.IsSuccess);
        Assert.Equal(ParaclinicalTransitionOutcome.NoOp, result.Outcome);
    }

    // ───────────────────────────── TryCancel ──────────────────────────────────────────

    [Fact]
    public void TryCancel_returns_false_when_done()
    {
        var item = ParaclinicalTestData.Item(ParaclinicalItemState.Done);
        var now = DateTime.UtcNow;

        var cancelled = item.TryCancel("Bỏ dịch vụ", now);

        Assert.False(cancelled);
        Assert.Equal(ParaclinicalItemState.Done, item.State);
        Assert.Null(item.CancelledAt);
    }

    [Fact]
    public void TryCancel_returns_true_when_already_cancelled_idempotent()
    {
        var item = ParaclinicalTestData.Item(ParaclinicalItemState.Cancelled);
        item.CancelReason = "Lý do cũ";
        var existingCancelledAt = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc);
        item.CancelledAt = existingCancelledAt;
        var now = DateTime.UtcNow;

        var cancelled = item.TryCancel("Lý do mới", now);

        Assert.True(cancelled);
        Assert.Equal(ParaclinicalItemState.Cancelled, item.State);
        Assert.Equal("Lý do cũ", item.CancelReason);
        Assert.Equal(existingCancelledAt, item.CancelledAt);
    }

    [Theory]
    [InlineData(ParaclinicalItemState.Ordered)]
    [InlineData(ParaclinicalItemState.Waiting)]
    [InlineData(ParaclinicalItemState.InProgress)]
    public void TryCancel_sets_cancelled_state_for_pending_items(ParaclinicalItemState state)
    {
        var item = ParaclinicalTestData.Item(state);
        var now = DateTime.UtcNow;

        var cancelled = item.TryCancel("Bệnh nhân từ chối", now);

        Assert.True(cancelled);
        Assert.Equal(ParaclinicalItemState.Cancelled, item.State);
        Assert.Equal(now, item.CancelledAt);
        Assert.Equal("Bệnh nhân từ chối", item.CancelReason);
        Assert.Equal(now, item.ModifiedDate);
    }

    // ───────────────────────────── BlocksConditionB ───────────────────────────────────

    [Theory]
    [InlineData(ParaclinicalItemState.Ordered, true)]
    [InlineData(ParaclinicalItemState.Waiting, true)]
    [InlineData(ParaclinicalItemState.InProgress, true)]
    [InlineData(ParaclinicalItemState.Done, false)]
    [InlineData(ParaclinicalItemState.Cancelled, false)]
    public void BlocksConditionB_evaluates_correctly_on_item_and_static(
        ParaclinicalItemState state, bool expectedBlocks)
    {
        var item = ParaclinicalTestData.Item(state);

        Assert.Equal(expectedBlocks, item.BlocksConditionB);
        Assert.Equal(expectedBlocks, ParaclinicalOrderItem.BlocksConditionB(state));
    }
}
