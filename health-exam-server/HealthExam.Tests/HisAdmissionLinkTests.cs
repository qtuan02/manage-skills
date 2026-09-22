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

public sealed class HisAdmissionLinkTests
{
    [Fact]
    public void Exam_record_contracts_expose_nullable_his_admission_id()
    {
        Assert.Equal(typeof(long?), typeof(ExamRecord).GetProperty("AdmissionID")!.PropertyType);
        Assert.Equal(typeof(long?), typeof(ExamRecordItem).GetProperty("AdmissionID")!.PropertyType);
        Assert.Equal(typeof(long?), typeof(ExamRecordWriteRequest).GetProperty("AdmissionID")!.PropertyType);
    }

    [Fact]
    public async Task Update_accepts_and_returns_his_admission_id()
    {
        using var db = new InMemoryTestDb();
        var session = db.SeedSession();
        var record = db.SeedRecord(session.SessionID);
        var item = await db.Records.UpdateAsync(record.RecordID,
            new ExamRecordWriteRequest { AdmissionID = 9000123 });
        Assert.Equal(9000123, item.AdmissionID);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task Update_rejects_non_positive_his_admission_id(long invalidAdmissionId)
    {
        using var db = new InMemoryTestDb();
        var session = db.SeedSession();
        var record = db.SeedRecord(session.SessionID);
        var ex = await Assert.ThrowsAsync<HealthExamException>(() =>
            db.Records.UpdateAsync(record.RecordID, new ExamRecordWriteRequest { AdmissionID = invalidAdmissionId }));
        Assert.Equal(ErrorCodes.BadRequest, ex.ErrorCode);
    }
}
