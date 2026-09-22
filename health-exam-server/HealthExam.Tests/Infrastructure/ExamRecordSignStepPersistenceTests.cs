using System;
using System.Linq;
using System.Threading.Tasks;
using HealthExam.Domain.ExamRecords;
using HealthExam.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HealthExam.Tests.Infrastructure;

public class ExamRecordSignStepPersistenceTests
{
    [Fact]
    public async Task Can_persist_and_retrieve_sign_steps_with_cascade()
    {
        var options = new DbContextOptionsBuilder<HealthExamDbContext>()
            .UseInMemoryDatabase($"health-exam-step-test-{Guid.NewGuid():N}")
            .Options;

        using var db = new HealthExamDbContext(options);
        var recordId = Guid.NewGuid();
        var record = new ExamRecord
        {
            RecordID = recordId,
            DivisionID = "D01",
            RecordCode = "REC-STEP-01",
            VariantCode = "VAR1"
        };
        db.ExamRecords.Add(record);

        var step1 = new ExamRecordSignStep
        {
            ID = Guid.NewGuid(),
            DivisionID = "D01",
            RecordID = recordId,
            VariantCode = "VAR1",
            SWStep = 1,
            StepName = "Bác sĩ kết luận",
            SWRoleID = 10,
            Status = "Signed",
            SignedByEmployeeID = 1274,
            SignedAt = DateTime.UtcNow
        };

        var step2 = new ExamRecordSignStep
        {
            ID = Guid.NewGuid(),
            DivisionID = "D01",
            RecordID = recordId,
            VariantCode = "VAR1",
            SWStep = 2,
            StepName = "Lãnh đạo duyệt",
            SWRoleID = 20,
            Status = "Pending"
        };

        db.ExamRecordSignSteps.AddRange(step1, step2);
        await db.SaveChangesAsync();

        db.ChangeTracker.Clear();

        var reloadedRecord = await db.ExamRecords
            .Include(r => r.SignSteps)
            .FirstOrDefaultAsync(r => r.RecordID == recordId);

        Assert.NotNull(reloadedRecord);
        Assert.Equal(2, reloadedRecord.SignSteps.Count);
        var savedStep1 = reloadedRecord.SignSteps.Single(s => s.SWStep == 1);
        Assert.Equal("Signed", savedStep1.Status);
        Assert.Equal(1274, savedStep1.SignedByEmployeeID);
        Assert.NotNull(savedStep1.SignedAt);

        var savedStep2 = reloadedRecord.SignSteps.Single(s => s.SWStep == 2);
        Assert.Equal("Pending", savedStep2.Status);
        Assert.Null(savedStep2.SignedByEmployeeID);
    }
}
