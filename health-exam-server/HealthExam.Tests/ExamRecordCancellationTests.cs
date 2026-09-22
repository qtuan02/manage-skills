using HealthExam.Domain.Common;
using HealthExam.Domain.ExamSessions;
using HealthExam.Domain.ExamRecords;
using HealthExam.Domain.Imports;
using HealthExam.Domain.Paraclinical;
using HealthExam.Domain.Webhooks;
using HealthExam.Domain.Integrations;
using HealthExam.Domain.Catalogs;
using HealthExam.API.Contracts;
using HealthExam.API.Middlewares;
using HealthExam.Application.Common;
using Xunit;

namespace HealthExam.Tests;

public class ExamRecordCancellationTests
{
    [Theory]
    [InlineData(ExamRecordState.NotRegistered, ExamRecordState.RegistrationCancelled)]
    [InlineData(ExamRecordState.Waiting, ExamRecordState.ExamCancelled)]
    [InlineData(ExamRecordState.InProgress, ExamRecordState.ExamCancelled)]
    public async Task Cancel_derives_target(ExamRecordState from, ExamRecordState expected)
    {
        using var db = new InMemoryTestDb();
        var session = db.SeedSession();
        var record = db.SeedRecord(session.SessionID, from);
        var item = await db.Records.CancelAsync(record.RecordID,
            new ExamRecordCancelRequest { Reason = "Người bệnh yêu cầu" });
        Assert.Equal(expected, item.State);
        Assert.NotNull(item.CancelledAt);
        Assert.Equal("Người bệnh yêu cầu", item.CancelReason);
        var audit = Assert.Single(db.Db.AuditLogs.Where(x => x.EntityID == record.RecordID));
        Assert.Equal(AuditActions.StateChange, audit.Action);
        Assert.Equal(AuditEntityTypes.Record, audit.EntityType);
        Assert.Equal((short)from, audit.FromState);
        Assert.Equal((short)expected, audit.ToState);
        Assert.Contains("Người bệnh yêu cầu", audit.Payload);
    }

    [Fact]
    public async Task Cancel_completed_record_throws_InvalidState()
    {
        using var db = new InMemoryTestDb();
        var session = db.SeedSession();
        var record = db.SeedRecord(session.SessionID, ExamRecordState.Completed);

        var ex = await Assert.ThrowsAsync<HealthExamException>(() =>
            db.Records.CancelAsync(record.RecordID, new ExamRecordCancelRequest { Reason = "Muốn hủy" }));

        Assert.Equal(ErrorCodes.InvalidState, ex.ErrorCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Cancel_blank_reason_throws_BadRequest(string reason)
    {
        using var db = new InMemoryTestDb();
        var session = db.SeedSession();
        var record = db.SeedRecord(session.SessionID, ExamRecordState.Waiting);

        var ex = await Assert.ThrowsAsync<HealthExamException>(() =>
            db.Records.CancelAsync(record.RecordID, new ExamRecordCancelRequest { Reason = reason }));

        Assert.Equal(ErrorCodes.BadRequest, ex.ErrorCode);
        var errors = Assert.IsType<ValidationErrors>(ex.Payload);
        Assert.Contains(errors.Errors, e => e.Field == nameof(ExamRecordCancelRequest.Reason) || e.Field == "Reason");
    }

    [Fact]
    public async Task Cancel_nonexistent_record_throws_NotFound()
    {
        using var db = new InMemoryTestDb();
        var ex = await Assert.ThrowsAsync<HealthExamException>(() =>
            db.Records.CancelAsync(Guid.NewGuid(), new ExamRecordCancelRequest { Reason = "Lý do" }));

        Assert.Equal(ErrorCodes.NotFound, ex.ErrorCode);
    }

    [Theory]
    [InlineData(ExamSessionState.Closed)]
    [InlineData(ExamSessionState.Cancelled)]
    public async Task Cancel_in_closed_session_throws_SessionClosed(ExamSessionState sessionState)
    {
        using var db = new InMemoryTestDb();
        var session = db.SeedSession(state: sessionState);
        var record = db.SeedRecord(session.SessionID, ExamRecordState.NotRegistered);

        var ex = await Assert.ThrowsAsync<HealthExamException>(() =>
            db.Records.CancelAsync(record.RecordID, new ExamRecordCancelRequest { Reason = "Lý do" }));

        Assert.Equal(ErrorCodes.SessionClosed, ex.ErrorCode);
    }

    [Fact]
    public async Task Cancel_idempotent_when_already_cancelled()
    {
        using var db = new InMemoryTestDb();
        var session = db.SeedSession();
        var record = db.SeedRecord(session.SessionID, ExamRecordState.NotRegistered);

        var first = await db.Records.CancelAsync(record.RecordID,
            new ExamRecordCancelRequest { Reason = "Lý do đầu tiên" });
        Assert.Equal(ExamRecordState.RegistrationCancelled, first.State);
        Assert.Equal("Lý do đầu tiên", first.CancelReason);
        Assert.Single(db.Db.AuditLogs.Where(x => x.EntityID == record.RecordID));

        // Repeat cancel with different reason (or blank reason)
        var second = await db.Records.CancelAsync(record.RecordID,
            new ExamRecordCancelRequest { Reason = "Lý do khác" });
        Assert.Equal(ExamRecordState.RegistrationCancelled, second.State);
        Assert.Equal("Lý do đầu tiên", second.CancelReason);
        Assert.Single(db.Db.AuditLogs.Where(x => x.EntityID == record.RecordID));
    }
}
