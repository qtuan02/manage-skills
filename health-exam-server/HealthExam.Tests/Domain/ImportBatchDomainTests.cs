using System;
using HealthExam.Domain.Common;
using HealthExam.Domain.Imports;
using Xunit;

namespace HealthExam.Tests.Domain;

public static class ImportBatchTestData
{
    public static ImportBatch Create(
        ImportBatchState state = ImportBatchState.Pending,
        DateTime? startedAt = null,
        DateTime? modifiedDate = null)
    {
        var now = DateTime.UtcNow;
        return new ImportBatch
        {
            BatchID = Guid.NewGuid(),
            DivisionID = "D01",
            SessionID = Guid.NewGuid(),
            FileName = "Test.xlsx",
            SheetName = "Sheet1",
            State = state,
            StartedAt = startedAt ?? now,
            CreatedDate = startedAt ?? now,
            ModifiedDate = modifiedDate ?? startedAt ?? now,
            TotalRow = 10,
            SuccessRow = 8,
            ErrorRow = 2
        };
    }

    public static ImportBatch Pending() => Create(ImportBatchState.Pending);
    public static ImportBatch Completed() => Create(ImportBatchState.Completed);
    public static ImportBatch Discarded() => Create(ImportBatchState.Discarded);
    public static ImportBatch Committing(DateTime? modifiedDate = null) => Create(ImportBatchState.Committing, modifiedDate: modifiedDate);
}

public class ImportBatchDomainTests
{
    [Fact]
    public void Completed_batch_commit_is_idempotent()
    {
        var batch = ImportBatchTestData.Completed();
        var result = batch.BeginCommit(DateTime.UtcNow);
        Assert.Equal(DomainOperationOutcome.NoOp, result.Outcome);
        Assert.Equal(ImportBatchState.Completed, batch.State);
    }

    [Fact]
    public void Discarded_batch_cannot_commit()
    {
        var batch = ImportBatchTestData.Discarded();
        var result = batch.BeginCommit(DateTime.UtcNow);
        Assert.False(result.IsSuccess);
        Assert.Equal(DomainFailureCode.InvalidTransition, result.Failure.Code);
        Assert.Equal("Lô nạp đã bị bỏ, không nạp lại được", result.Failure.Message);
        Assert.Equal(ImportBatchState.Discarded, batch.State);
    }

    [Fact]
    public void Expired_batch_cannot_commit()
    {
        var now = DateTime.UtcNow;
        var batch = ImportBatchTestData.Create(
            ImportBatchState.Pending,
            startedAt: now - ImportBatch.BatchLifetime - TimeSpan.FromMinutes(1));

        var result = batch.BeginCommit(now);
        Assert.False(result.IsSuccess);
        Assert.Equal(DomainFailureCode.InvalidState, result.Failure.Code);
        Assert.Contains("quá hạn", result.Failure.Message);
        Assert.Equal(ImportBatchState.Pending, batch.State);
    }

    [Fact]
    public void Active_committing_batch_cannot_commit()
    {
        var now = DateTime.UtcNow;
        var batch = ImportBatchTestData.Committing(modifiedDate: now - TimeSpan.FromMinutes(1));

        var result = batch.BeginCommit(now);
        Assert.False(result.IsSuccess);
        Assert.Equal(DomainFailureCode.InvalidState, result.Failure.Code);
        Assert.Contains("đang được ghi bởi một lượt khác", result.Failure.Message);
        Assert.Equal(ImportBatchState.Committing, batch.State);
    }

    [Fact]
    public void Stale_committing_batch_can_reclaim()
    {
        var now = DateTime.UtcNow;
        var batch = ImportBatchTestData.Committing(
            modifiedDate: now - ImportBatch.StaleCommitAfter - TimeSpan.FromMinutes(1));

        var result = batch.BeginCommit(now);
        Assert.True(result.IsSuccess);
        Assert.True(result.Applied);
        Assert.Equal(ImportBatchState.Committing, batch.State);
        Assert.Equal(now, batch.ModifiedDate);
    }

    [Fact]
    public void Completed_batch_cannot_discard()
    {
        var batch = ImportBatchTestData.Completed();
        var result = batch.Discard(DateTime.UtcNow);
        Assert.False(result.IsSuccess);
        Assert.Equal(DomainFailureCode.InvalidState, result.Failure.Code);
        Assert.Contains("Lô đã nạp xong, không bỏ được", result.Failure.Message);
        Assert.Equal(ImportBatchState.Completed, batch.State);
    }

    [Fact]
    public void Discarded_batch_discard_is_idempotent()
    {
        var batch = ImportBatchTestData.Discarded();
        var result = batch.Discard(DateTime.UtcNow);
        Assert.Equal(DomainOperationOutcome.NoOp, result.Outcome);
        Assert.Equal(ImportBatchState.Discarded, batch.State);
    }

    [Fact]
    public void Active_committing_batch_cannot_discard()
    {
        var now = DateTime.UtcNow;
        var batch = ImportBatchTestData.Committing(modifiedDate: now - TimeSpan.FromMinutes(1));

        var result = batch.Discard(now);
        Assert.False(result.IsSuccess);
        Assert.Equal(DomainFailureCode.InvalidState, result.Failure.Code);
        Assert.Contains("đang được ghi, không bỏ được", result.Failure.Message);
        Assert.Equal(ImportBatchState.Committing, batch.State);
    }

    [Fact]
    public void Stale_committing_batch_can_discard()
    {
        var now = DateTime.UtcNow;
        var batch = ImportBatchTestData.Committing(
            modifiedDate: now - ImportBatch.StaleCommitAfter - TimeSpan.FromMinutes(1));

        var result = batch.Discard(now);
        Assert.True(result.IsSuccess);
        Assert.True(result.Applied);
        Assert.Equal(ImportBatchState.Discarded, batch.State);
        Assert.Equal(now, batch.FinishedAt);
        Assert.Equal(now, batch.ModifiedDate);
    }

    [Fact]
    public void ReleaseClaim_reverts_committing_to_pending()
    {
        var now = DateTime.UtcNow;
        var batch = ImportBatchTestData.Committing(modifiedDate: now - TimeSpan.FromMinutes(2));

        var result = batch.ReleaseClaim(now);
        Assert.True(result.IsSuccess);
        Assert.True(result.Applied);
        Assert.Equal(ImportBatchState.Pending, batch.State);
        Assert.Equal(now, batch.ModifiedDate);

        var noOpResult = batch.ReleaseClaim(now);
        Assert.Equal(DomainOperationOutcome.NoOp, noOpResult.Outcome);
        Assert.Equal(ImportBatchState.Pending, batch.State);
    }

    [Fact]
    public void CompleteCommit_succeeds_when_committing()
    {
        var now = DateTime.UtcNow;
        var batch = ImportBatchTestData.Committing(modifiedDate: now - TimeSpan.FromMinutes(1));

        var result = batch.CompleteCommit(now);
        Assert.True(result.IsSuccess);
        Assert.True(result.Applied);
        Assert.Equal(ImportBatchState.Completed, batch.State);
        Assert.Equal(now, batch.FinishedAt);
        Assert.Equal(now, batch.ModifiedDate);

        // Idempotent on completed
        var idempResult = batch.CompleteCommit(now);
        Assert.Equal(DomainOperationOutcome.NoOp, idempResult.Outcome);
    }

    [Fact]
    public void CompleteCommit_fails_when_pending()
    {
        var now = DateTime.UtcNow;
        var batch = ImportBatchTestData.Pending();

        var result = batch.CompleteCommit(now);
        Assert.False(result.IsSuccess);
        Assert.Equal(DomainFailureCode.InvalidTransition, result.Failure.Code);
        Assert.Equal(ImportBatchState.Pending, batch.State);
    }
}
