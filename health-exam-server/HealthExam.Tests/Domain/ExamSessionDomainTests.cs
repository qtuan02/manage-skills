using System;
using HealthExam.Domain.Common;
using HealthExam.Domain.ExamSessions;
using Xunit;

namespace HealthExam.Tests.Domain;

public static class ExamSessionTestData
{
    public static ExamSession Create(ExamSessionState state = ExamSessionState.Open, string sessionCode = "DK-2026-001")
        => new()
        {
            SessionID = Guid.NewGuid(),
            DivisionID = "D01",
            SessionCode = sessionCode,
            SessionName = "Đợt khám thử nghiệm",
            ExamDate = DateOnly.FromDateTime(DateTime.Today),
            State = state,
            IsActive = true
        };

    public static ExamSession Open(string sessionCode = "DK-2026-001") => Create(ExamSessionState.Open, sessionCode);
    public static ExamSession Closed(string sessionCode = "DK-2026-001") => Create(ExamSessionState.Closed, sessionCode);
    public static ExamSession Cancelled(string sessionCode = "DK-2026-001") => Create(ExamSessionState.Cancelled, sessionCode);
    public static ExamSession Draft(string sessionCode = "DK-2026-001") => Create(ExamSessionState.Draft, sessionCode);
    public static ExamSession InProgress(string sessionCode = "DK-2026-001") => Create(ExamSessionState.InProgress, sessionCode);
}

public class ExamSessionDomainTests
{
    [Theory]
    [InlineData(ExamSessionState.Draft)]
    [InlineData(ExamSessionState.Open)]
    [InlineData(ExamSessionState.InProgress)]
    public void Close_openable_session_changes_state_to_closed(ExamSessionState initialState)
    {
        var session = ExamSessionTestData.Create(initialState, "DK-01");

        var result = session.Close();

        Assert.True(result.IsSuccess);
        Assert.Equal(ExamSessionState.Closed, session.State);
    }

    [Fact]
    public void Close_already_closed_session_returns_invalid_transition()
    {
        var session = ExamSessionTestData.Closed("DK-01");

        var result = session.Close();

        Assert.False(result.IsSuccess);
        Assert.Equal(DomainFailureCode.InvalidTransition, result.Failure.Code);
        Assert.Equal("Đợt khám DK-01 đã đóng từ trước", result.Failure.Message);
        Assert.Equal(ExamSessionState.Closed, session.State);
    }

    [Fact]
    public void Close_cancelled_session_returns_invalid_transition()
    {
        var session = ExamSessionTestData.Cancelled("DK-02");

        var result = session.Close();

        Assert.False(result.IsSuccess);
        Assert.Equal(DomainFailureCode.InvalidTransition, result.Failure.Code);
        Assert.Equal("Đợt khám DK-02 đã hủy, không đóng được", result.Failure.Message);
        Assert.Equal(ExamSessionState.Cancelled, session.State);
    }

    [Fact]
    public void Reopen_closed_session_changes_state_to_open()
    {
        var session = ExamSessionTestData.Closed("DK-03");

        var result = session.Reopen("Bổ sung thêm người");

        Assert.True(result.IsSuccess);
        Assert.Equal(ExamSessionState.Open, session.State);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\n")]
    public void Reopen_with_empty_or_whitespace_reason_returns_required_value(string reason)
    {
        var session = ExamSessionTestData.Closed("DK-04");

        var result = session.Reopen(reason);

        Assert.False(result.IsSuccess);
        Assert.Equal(DomainFailureCode.RequiredValue, result.Failure.Code);
        Assert.Equal("Mở lại đợt khám phải có lý do", result.Failure.Message);
        Assert.Equal(ExamSessionState.Closed, session.State);
    }

    [Theory]
    [InlineData(ExamSessionState.Draft, "Nháp")]
    [InlineData(ExamSessionState.Open, "Đang mở")]
    [InlineData(ExamSessionState.InProgress, "Đang khám")]
    [InlineData(ExamSessionState.Cancelled, "Hủy")]
    public void Reopen_non_closed_session_returns_invalid_transition(ExamSessionState state, string stateName)
    {
        var session = ExamSessionTestData.Create(state, "DK-05");

        var result = session.Reopen("Lý do mở lại");

        Assert.False(result.IsSuccess);
        Assert.Equal(DomainFailureCode.InvalidTransition, result.Failure.Code);
        Assert.Equal($"Đợt khám đang ở trạng thái \"{stateName}\", chỉ đợt \"Đã đóng\" mới mở lại được", result.Failure.Message);
        Assert.Equal(state, session.State);
    }
}
