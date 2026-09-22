using HealthExam.API.Contracts;
using HealthExam.API.Middlewares;
using HealthExam.Application.Common;
using HealthExam.Domain.Common;
using HealthExam.Domain.ExamSessions;
using HealthExam.Domain.ExamRecords;
using HealthExam.Domain.Imports;
using HealthExam.Domain.Paraclinical;
using HealthExam.Domain.Webhooks;
using HealthExam.Domain.Integrations;
using HealthExam.Domain.Catalogs;
using HealthExam.API.Controllers;
using HealthExam.Server.Service;
using Xunit;

namespace HealthExam.Tests;

public class ExamRecordDefaultSessionTests
{
    [Fact]
    public async Task Create_for_phase_one_without_session_uses_an_internal_default_session()
    {
        using var db = new InMemoryTestDb();

        var created = await db.Records.CreateInDefaultSessionAsync(new ExamRecordWriteRequest
        {
            FullName = "Nguyễn Văn A",
            VariantCode = "DTK_01"
        });

        var session = Assert.Single(db.Db.ExamSessions);
        Assert.Equal("PHASE1-DEFAULT", session.SessionCode);
        Assert.Equal(ExamSessionState.Open, session.State);
        Assert.Equal(db.Ctx.DivisionId, session.DivisionID);
        Assert.Equal(session.SessionID, created.SessionID);
        Assert.Equal("PHASE1-DEFAULT-0001", created.RecordCode);
    }

    [Fact]
    public async Task Create_for_phase_one_without_patient_code_generates_a_stable_patient_code()
    {
        using var db = new InMemoryTestDb();

        var created = await db.Records.CreateInDefaultSessionAsync(new ExamRecordWriteRequest
        {
            FullName = "Nguyễn Văn A",
            VariantCode = "DTK_01"
        });

        Assert.Equal("PHASE1-DEFAULT-0001", created.RecordCode);
        Assert.Equal("HEX-PHASE1-DEFAULT-0001", created.PatientCode);
        Assert.Equal(created.PatientCode, db.RecordOf(created.RecordID).Patient?.PatientCode);
    }

    [Fact]
    public async Task Consecutive_phase_one_creates_reuse_one_default_session()
    {
        using var db = new InMemoryTestDb();

        var first = await db.Records.CreateInDefaultSessionAsync(new ExamRecordWriteRequest
        {
            FullName = "Nguyễn Văn A",
            VariantCode = "DTK_01"
        });
        db.Db.ChangeTracker.Clear(); // mô phỏng request kế tiếp với DbContext scoped mới
        var second = await db.Records.CreateInDefaultSessionAsync(new ExamRecordWriteRequest
        {
            FullName = "Trần Thị B",
            VariantCode = "DTK_02"
        });

        Assert.Single(db.Db.ExamSessions);
        Assert.Equal(first.SessionID, second.SessionID);
        Assert.Equal("PHASE1-DEFAULT-0001", first.RecordCode);
        Assert.Equal("PHASE1-DEFAULT-0002", second.RecordCode);
    }

    [Fact]
    public async Task Phase_one_default_session_is_scoped_by_division()
    {
        using var db = new InMemoryTestDb();

        var first = await db.Records.CreateInDefaultSessionAsync(new ExamRecordWriteRequest
        {
            FullName = "Nguyễn Văn A",
            VariantCode = "DTK_01"
        });

        db.Ctx.DivisionId = "BV02";
        var second = await db.Records.CreateInDefaultSessionAsync(new ExamRecordWriteRequest
        {
            FullName = "Trần Thị B",
            VariantCode = "DTK_02"
        });

        Assert.Equal(2, db.Db.ExamSessions.Count());
        Assert.NotEqual(first.SessionID, second.SessionID);
        Assert.Equal("PHASE1-DEFAULT-0001", first.RecordCode);
        Assert.Equal("PHASE1-DEFAULT-0001", second.RecordCode);
    }

    [Fact]
    public async Task Explicit_session_create_keeps_using_the_supplied_session()
    {
        using var db = new InMemoryTestDb();
        var selectedSession = db.SeedSession(sessionCode: "IMPORT-2026-001");

        var created = await db.Records.CreateAsync(new ExamRecordSaveRequest
        {
            SessionID = selectedSession.SessionID,
            RecordCode = "IMPORT-2026-001-0001",
            FullName = "Nguyễn Văn A",
            VariantCode = "DTK_01"
        }, importBatchId: Guid.NewGuid());

        Assert.Equal(selectedSession.SessionID, created.SessionID);
        Assert.DoesNotContain(db.Db.ExamSessions,
            x => x.SessionCode == ExamSessionService.PhaseOneDefaultSessionCode);
    }
}

public class ExamRecordCreateContractTests
{
    [Fact]
    public void Create_contract_does_not_expose_session_id_in_phase_one()
    {
        var action = typeof(ExamRecordController).GetMethod(nameof(ExamRecordController.Create));
        var bodyType = action!.GetParameters()[0].ParameterType;

        Assert.Equal(typeof(HealthExam.API.Contracts.ExamRecordWriteRequest), bodyType);
        Assert.Null(bodyType.GetProperty("SessionID"));
    }

    [Fact]
    public void Update_contract_does_not_expose_session_id_in_phase_one()
    {
        var action = typeof(ExamRecordController).GetMethod(nameof(ExamRecordController.Update));
        var bodyType = action!.GetParameters()[1].ParameterType;

        Assert.Equal(typeof(HealthExam.API.Contracts.ExamRecordWriteRequest), bodyType);
        Assert.Null(bodyType.GetProperty("SessionID"));
    }
}
