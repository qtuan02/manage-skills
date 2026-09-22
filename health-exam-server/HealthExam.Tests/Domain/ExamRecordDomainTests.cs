using System;
using HealthExam.Domain.Common;
using HealthExam.Domain.ExamRecords;
using Xunit;

namespace HealthExam.Tests.Domain;

public static class ExamRecordTestData
{
    public static ExamRecord Create(ExamRecordState state = ExamRecordState.NotRegistered, string recordCode = "DK-2026-001-0001")
        => new()
        {
            RecordID = Guid.NewGuid(),
            DivisionID = "D01",
            SessionID = Guid.NewGuid(),
            RecordCode = recordCode,
            VariantCode = "DTK_01",
            State = state
        };

    public static ExamRecord WithState(ExamRecordState state) => Create(state);
}

public class ExamRecordDomainTests
{
    [Fact]
    public void Confirm_from_not_registered_transitions_to_waiting()
    {
        var record = ExamRecordTestData.WithState(ExamRecordState.NotRegistered);

        var result = record.Confirm();

        Assert.True(result.IsSuccess);
        Assert.Equal(ExamRecordState.Waiting, record.State);
    }

    [Theory]
    [InlineData(ExamRecordState.Waiting)]
    [InlineData(ExamRecordState.InProgress)]
    [InlineData(ExamRecordState.Completed)]
    [InlineData(ExamRecordState.RegistrationCancelled)]
    [InlineData(ExamRecordState.ExamCancelled)]
    public void Confirm_from_other_states_returns_invalid_transition(ExamRecordState state)
    {
        var record = ExamRecordTestData.WithState(state);

        var result = record.Confirm();

        Assert.False(result.IsSuccess);
        Assert.Equal(DomainFailureCode.InvalidTransition, result.Failure.Code);
        Assert.Equal("Chỉ hồ sơ ở trạng thái Chưa đăng ký mới chốt đăng ký được", result.Failure.Message);
        Assert.Equal(state, record.State);
    }

    [Theory]
    [InlineData(ExamRecordState.NotRegistered, ExamRecordState.RegistrationCancelled)]
    [InlineData(ExamRecordState.Waiting, ExamRecordState.ExamCancelled)]
    [InlineData(ExamRecordState.InProgress, ExamRecordState.ExamCancelled)]
    public void Cancel_valid_state_applies_expected_target(
        ExamRecordState source, ExamRecordState target)
    {
        var record = ExamRecordTestData.WithState(source);

        var result = record.Cancel("lý do");

        Assert.True(result.IsSuccess);
        Assert.Equal(target, record.State);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\n")]
    public void Cancel_with_empty_or_whitespace_reason_returns_required_value(string reason)
    {
        var record = ExamRecordTestData.WithState(ExamRecordState.NotRegistered);

        var result = record.Cancel(reason);

        Assert.False(result.IsSuccess);
        Assert.Equal(DomainFailureCode.RequiredValue, result.Failure.Code);
        Assert.Equal("Hủy hồ sơ phải có lý do", result.Failure.Message);
        Assert.Equal(ExamRecordState.NotRegistered, record.State);
    }

    [Theory]
    [InlineData(ExamRecordState.Completed, "Đã khám")]
    [InlineData(ExamRecordState.RegistrationCancelled, "Hủy đăng ký")]
    [InlineData(ExamRecordState.ExamCancelled, "Hủy khám")]
    public void Cancel_invalid_state_returns_invalid_transition(ExamRecordState state, string stateName)
    {
        var record = ExamRecordTestData.WithState(state);

        var result = record.Cancel("Lý do hủy");

        Assert.False(result.IsSuccess);
        Assert.Equal(DomainFailureCode.InvalidTransition, result.Failure.Code);
        Assert.Equal($"Hồ sơ đang ở trạng thái \"{stateName}\", không thể hủy", result.Failure.Message);
        Assert.Equal(state, record.State);
    }
}
