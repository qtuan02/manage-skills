using System;
using System.Linq;
using System.Threading.Tasks;
using HealthExam.Domain.Common;
using HealthExam.Domain.ExamRecords;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HealthExam.Tests.Signing;

public class SectionSigningProgressTests
{
    [Fact]
    public void BeginExam_only_moves_waiting_record_and_stamps_once()
    {
        var record = new ExamRecord { State = ExamRecordState.Waiting };
        var first = new DateTime(2026, 9, 18, 8, 0, 0, DateTimeKind.Utc);
        var second = first.AddMinutes(5);

        Assert.True(record.BeginExam(first).IsSuccess);
        Assert.Equal(ExamRecordState.InProgress, record.State);
        Assert.Equal(first, record.ExamStartedAt);

        Assert.True(record.BeginExam(second).IsSuccess);
        Assert.Equal(first, record.ExamStartedAt);
        Assert.Equal("InProgress", ExamRecordSignStepStatus.InProgress);
    }

    [Fact]
    public void BeginExam_rejects_non_waiting_or_non_inprogress_states()
    {
        var record = new ExamRecord { State = ExamRecordState.Completed };
        var now = new DateTime(2026, 9, 18, 8, 0, 0, DateTimeKind.Utc);

        var result = record.BeginExam(now);
        Assert.False(result.IsSuccess);
    }

    [Fact]
    public void Calculate_counts_only_signed_clinical_steps()
    {
        var maps = new[]
        {
            new HealthExam.Domain.ExamForms.SignStepMap { SWStep = 1, ItemGroupID = 101, IsConclusionStep = false, IsActive = true },
            new HealthExam.Domain.ExamForms.SignStepMap { SWStep = 2, ItemGroupID = 102, IsConclusionStep = false, IsActive = true },
            new HealthExam.Domain.ExamForms.SignStepMap { SWStep = 3, IsConclusionStep = true, IsActive = true }
        };
        var snapshots = new[]
        {
            new ExamRecordSignStep { SWStep = 1, Status = ExamRecordSignStepStatus.Signed },
            new ExamRecordSignStep { SWStep = 2, Status = ExamRecordSignStepStatus.InProgress },
            new ExamRecordSignStep { SWStep = 3, Status = ExamRecordSignStepStatus.Signed }
        };

        var progress = HealthExam.Application.Signing.SigningProgressCalculator.Calculate(maps, snapshots);
        Assert.Equal(1, progress.Done);
        Assert.Equal(2, progress.Total);
    }

    [Fact]
    public async Task Save_first_section_moves_record_to_inprogress_and_creates_inprogress_step()
    {
        var db = new InMemoryTestDb();
        var session = db.SeedSession(divisionId: "DIV01");
        var record = db.SeedRecord(session.SessionID, ExamRecordState.Waiting, divisionId: "DIV01");
        db.SetVariantCode(record.RecordID, "KSK06-18T");

        var mapRepo = new FakeSignStepMapRepository();
        mapRepo.Steps.Add(new HealthExam.Domain.ExamForms.SignStepMap
        {
            ID = Guid.NewGuid(), DivisionID = "DIV01", VariantCode = "KSK06-18T",
            SWStep = 1, ItemGroupID = 101, StepName = "Khám thể lực",
            SignTitle = "Khám thể lực", SWRoleID = 45, IsConclusionStep = false, IsActive = true
        });
        mapRepo.Steps.Add(new HealthExam.Domain.ExamForms.SignStepMap
        {
            ID = Guid.NewGuid(), DivisionID = "DIV01", VariantCode = "KSK06-18T",
            SWStep = 2, ItemGroupID = 102, StepName = "Khám mắt",
            SignTitle = "Khám mắt", SWRoleID = 45, IsConclusionStep = false, IsActive = true
        });

        var defHandler = new FakeDefinitionHandler(101);
        var ensureAdmission = new FakeEnsureAdmission(999);
        var getSectionHandler = new FakeGetSectionHandler();
        var client = new FakeHisClientForSave();

        var handler = new HealthExam.Application.His.SaveHisFormSectionHandler(
            new HealthExam.Infrastructure.Persistence.Repositories.ExamRecordRepository(db.Db),
            client,
            defHandler,
            ensureAdmission,
            getSectionHandler,
            new HealthExam.Infrastructure.Persistence.UnitOfWork(db.Db),
            mapRepo);

        var cmd = new HealthExam.Application.His.SaveHisFormSectionCommand(
            "DIV01", record.RecordID, 101,
            new HealthExam.Application.His.HisFormSectionSaveRequest(Array.Empty<HealthExam.Application.His.HisFormSectionFieldValue>()),
            "Bearer t", "trace", "1274", ActorKind.Employee);

        var res = await handler.HandleAsync(cmd);
        Assert.True(res.IsSuccess);

        var reloaded = db.Db.ExamRecords.Include(r => r.SignSteps).Single(r => r.RecordID == record.RecordID);
        Assert.Equal(ExamRecordState.InProgress, reloaded.State);
        Assert.NotNull(reloaded.ExamStartedAt);

        var step = Assert.Single(reloaded.SignSteps);
        Assert.Equal(ExamRecordSignStepStatus.InProgress, step.Status);
        Assert.Equal(1, step.SWStep);
        Assert.Equal(101, step.ItemGroupID);
    }

    [Fact]
    public async Task Save_second_section_creates_inprogress_step()
    {
        var db = new InMemoryTestDb();
        var session = db.SeedSession(divisionId: "DIV01");
        var record = db.SeedRecord(session.SessionID, ExamRecordState.Waiting, divisionId: "DIV01");
        db.SetVariantCode(record.RecordID, "KSK06-18T");

        var mapRepo = new FakeSignStepMapRepository();
        mapRepo.Steps.Add(new HealthExam.Domain.ExamForms.SignStepMap
        {
            ID = Guid.NewGuid(), DivisionID = "DIV01", VariantCode = "KSK06-18T",
            SWStep = 1, ItemGroupID = 101, StepName = "Khám thể lực",
            SignTitle = "Khám thể lực", SWRoleID = 45, IsConclusionStep = false, IsActive = true
        });
        mapRepo.Steps.Add(new HealthExam.Domain.ExamForms.SignStepMap
        {
            ID = Guid.NewGuid(), DivisionID = "DIV01", VariantCode = "KSK06-18T",
            SWStep = 2, ItemGroupID = 102, StepName = "Khám mắt",
            SignTitle = "Khám mắt", SWRoleID = 45, IsConclusionStep = false, IsActive = true
        });

        var defHandler = new FakeDefinitionHandler(102);
        var ensureAdmission = new FakeEnsureAdmission(999);
        var getSectionHandler = new FakeGetSectionHandler();
        var client = new FakeHisClientForSave();

        var handler = new HealthExam.Application.His.SaveHisFormSectionHandler(
            new HealthExam.Infrastructure.Persistence.Repositories.ExamRecordRepository(db.Db),
            client,
            defHandler,
            ensureAdmission,
            getSectionHandler,
            new HealthExam.Infrastructure.Persistence.UnitOfWork(db.Db),
            mapRepo);

        var cmd = new HealthExam.Application.His.SaveHisFormSectionCommand(
            "DIV01", record.RecordID, 102,
            new HealthExam.Application.His.HisFormSectionSaveRequest(Array.Empty<HealthExam.Application.His.HisFormSectionFieldValue>()),
            "Bearer t", "trace", "1274", ActorKind.Employee);

        var res = await handler.HandleAsync(cmd);
        Assert.True(res.IsSuccess);

        var reloaded = db.Db.ExamRecords.Include(r => r.SignSteps).Single(r => r.RecordID == record.RecordID);
        Assert.Equal(ExamRecordState.InProgress, reloaded.State);

        var step = Assert.Single(reloaded.SignSteps);
        Assert.Equal(ExamRecordSignStepStatus.InProgress, step.Status);
        Assert.Equal(2, step.SWStep);
        Assert.Equal(102, step.ItemGroupID);
    }

    [Fact]
    public async Task Save_his_failure_leaves_record_waiting_and_no_steps()
    {
        var db = new InMemoryTestDb();
        var session = db.SeedSession(divisionId: "DIV01");
        var record = db.SeedRecord(session.SessionID, ExamRecordState.Waiting, divisionId: "DIV01");
        db.SetVariantCode(record.RecordID, "KSK06-18T");

        var mapRepo = new FakeSignStepMapRepository();
        mapRepo.Steps.Add(new HealthExam.Domain.ExamForms.SignStepMap
        {
            ID = Guid.NewGuid(), DivisionID = "DIV01", VariantCode = "KSK06-18T",
            SWStep = 1, ItemGroupID = 101, StepName = "Khám thể lực",
            SignTitle = "Khám thể lực", SWRoleID = 45, IsConclusionStep = false, IsActive = true
        });

        var defHandler = new FakeDefinitionHandler(101);
        var ensureAdmission = new FakeEnsureAdmission(999);
        var getSectionHandler = new FakeGetSectionHandler();
        var client = new FakeHisClientForSave(shouldFail: true);

        var handler = new HealthExam.Application.His.SaveHisFormSectionHandler(
            new HealthExam.Infrastructure.Persistence.Repositories.ExamRecordRepository(db.Db),
            client,
            defHandler,
            ensureAdmission,
            getSectionHandler,
            new HealthExam.Infrastructure.Persistence.UnitOfWork(db.Db),
            mapRepo);

        var cmd = new HealthExam.Application.His.SaveHisFormSectionCommand(
            "DIV01", record.RecordID, 101,
            new HealthExam.Application.His.HisFormSectionSaveRequest(Array.Empty<HealthExam.Application.His.HisFormSectionFieldValue>()),
            "Bearer t", "trace", "1274", ActorKind.Employee);

        var res = await handler.HandleAsync(cmd);
        Assert.False(res.IsSuccess);

        var reloaded = db.Db.ExamRecords.Include(r => r.SignSteps).Single(r => r.RecordID == record.RecordID);
        Assert.Equal(ExamRecordState.Waiting, reloaded.State);
        Assert.Null(reloaded.ExamStartedAt);
        Assert.Empty(reloaded.SignSteps);
    }

    private sealed class FakeDefinitionHandler : HealthExam.Application.His.IGetHisFormDefinitionHandler
    {
        private readonly int _itemGroupId;
        public FakeDefinitionHandler(int itemGroupId) => _itemGroupId = itemGroupId;

        public Task<HealthExam.Application.Common.ApplicationResult<HealthExam.Application.His.HisFormDefinitionResult>> HandleAsync(
            HealthExam.Application.His.GetHisFormDefinitionQuery query, System.Threading.CancellationToken ct = default)
        {
            var res = new HealthExam.Application.His.HisFormDefinitionResult(
                Guid.NewGuid(), "KSK-TREN18TUOI", "Mẫu", 1, "V1", false, true,
                $"[{{\"ItemGroupID\":{_itemGroupId}}}]",
                "[]");
            return Task.FromResult(HealthExam.Application.Common.ApplicationResult<HealthExam.Application.His.HisFormDefinitionResult>.Success(res));
        }
    }

    private sealed class FakeEnsureAdmission : HealthExam.Application.His.IEnsureHisAdmission
    {
        private readonly long _admissionId;
        public FakeEnsureAdmission(long admissionId) => _admissionId = admissionId;

        public Task<HealthExam.Application.Common.ApplicationResult<long>> HandleAsync(
            HealthExam.Application.His.EnsureHisAdmissionCommand command, System.Threading.CancellationToken ct = default)
            => Task.FromResult(HealthExam.Application.Common.ApplicationResult<long>.Success(_admissionId));
    }

    private sealed class FakeGetSectionHandler : HealthExam.Application.His.IGetHisRecordSectionHandler
    {
        public Task<HealthExam.Application.Common.ApplicationResult<HealthExam.Application.His.HisRecordSectionResult>> HandleAsync(
            HealthExam.Application.His.GetHisRecordSectionQuery query, System.Threading.CancellationToken ct = default)
        {
            var res = new HealthExam.Application.His.HisRecordSectionResult(
                query.RecordId, 999, query.ItemGroupId, "[]");
            return Task.FromResult(HealthExam.Application.Common.ApplicationResult<HealthExam.Application.His.HisRecordSectionResult>.Success(res));
        }
    }

    private sealed class FakeHisClientForSave : HealthExam.Application.Integrations.IHisEmrClient
    {
        private readonly bool _shouldFail;
        public FakeHisClientForSave(bool shouldFail = false) => _shouldFail = shouldFail;

        public Task<HealthExam.Application.Integrations.HisClientResult<HealthExam.Application.Integrations.HisJsonDocument>> SendAsync(
            HealthExam.Application.Integrations.HisOperation operation, HealthExam.Application.Integrations.HisRequest request, System.Threading.CancellationToken ct = default)
        {
            if (_shouldFail)
            {
                return Task.FromResult(HealthExam.Application.Integrations.HisClientResult<HealthExam.Application.Integrations.HisJsonDocument>.Fail(
                    HealthExam.Application.Integrations.HisClientOutcome.BadGateway, "Lỗi kết nối HIS"));
            }
            return Task.FromResult(HealthExam.Application.Integrations.HisClientResult<HealthExam.Application.Integrations.HisJsonDocument>.Success(
                new HealthExam.Application.Integrations.HisJsonDocument($"{{\"EMRDataID\":\"{Guid.NewGuid()}\",\"Details\":[]}}")));
        }
    }
    [Fact]
    public async Task Reset_only_active_section_reverts_record_to_waiting()
    {
        var db = new InMemoryTestDb();
        var session = db.SeedSession(divisionId: "DIV01");
        var hisEmrDataId = Guid.NewGuid();
        var record = db.SeedRecord(session.SessionID, ExamRecordState.InProgress, divisionId: "DIV01",
            admissionId: 999, hisEmrDataId: hisEmrDataId);
        db.SetVariantCode(record.RecordID, "KSK06-18T");

        var attachedRecord = db.Db.ExamRecords.Include(r => r.SignSteps).Single(r => r.RecordID == record.RecordID);
        attachedRecord.ExamStartedAt = DateTime.UtcNow;
        var step = new ExamRecordSignStep
        {
            ID = Guid.NewGuid(),
            DivisionID = "DIV01",
            RecordID = record.RecordID,
            VariantCode = "KSK06-18T",
            ItemGroupID = 101,
            SWStep = 1,
            StepName = "Khám thể lực",
            Status = ExamRecordSignStepStatus.InProgress,
            PerformedByEmployeeID = 1274
        };
        attachedRecord.SignSteps.Add(step);
        await db.Db.SaveChangesAsync();

        var defHandler = new FakeDefinitionHandler(101);
        var getSectionHandler = new FakeGetSectionHandler();
        var client = new FakeHisClientForSave();

        var handler = new HealthExam.Application.His.ResetHisFormSectionHandler(
            new HealthExam.Infrastructure.Persistence.Repositories.ExamRecordRepository(db.Db),
            new HealthExam.Infrastructure.Persistence.UnitOfWork(db.Db),
            client,
            defHandler,
            getSectionHandler);

        var cmd = new HealthExam.Application.His.ResetHisFormSectionCommand(
            "DIV01", record.RecordID, 101, "Bearer t", "trace", 1274);

        var res = await handler.HandleAsync(cmd);
        Assert.True(res.IsSuccess, res.Failure?.Message ?? "");

        var reloaded = db.Db.ExamRecords.Include(r => r.SignSteps).Single(r => r.RecordID == record.RecordID);
        Assert.Equal(ExamRecordState.Waiting, reloaded.State);
        Assert.Null(reloaded.ExamStartedAt);
        Assert.Empty(reloaded.SignSteps);
    }
}
