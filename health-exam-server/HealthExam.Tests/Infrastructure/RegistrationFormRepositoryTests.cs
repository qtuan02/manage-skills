using System;
using System.Linq;
using System.Threading.Tasks;
using HealthExam.Domain.ExamForms;
using HealthExam.Domain.ExamRecords;
using HealthExam.Infrastructure.Persistence;
using HealthExam.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HealthExam.Tests.Infrastructure;

[Collection("PostgresTestDb")]
public class RegistrationFormRepositoryTests
{
    [Fact]
    public async Task GetActiveMappingAsync_returns_mapping_with_sections()
    {
        if (!PostgresTestDb.Enabled) return;
        using var db = new PostgresTestDb();
        var repo = new RegistrationFormRepository(db.Db);

        var mapping = await repo.GetActiveMappingAsync("DEV", "DTK_03");

        Assert.NotNull(mapping);
        Assert.Equal("KSK-TREN18TUOI", mapping.TemplateCode);
        Assert.NotEmpty(mapping.Sections);
        Assert.Contains(mapping.Sections, s => s.SectionKind == "HISTORY");
    }

    [Fact]
    public async Task ListActiveVariantCodesAsync_returns_active_variants()
    {
        if (!PostgresTestDb.Enabled) return;
        using var db = new PostgresTestDb();
        var repo = new RegistrationFormRepository(db.Db);

        var list = await repo.ListActiveVariantCodesAsync("DEV");

        Assert.NotNull(list);
        Assert.Contains("DTK_03", list);
    }

    [Fact]
    public async Task GetRecordAsync_and_GetRecordWithSessionAsync_return_record()
    {
        if (!PostgresTestDb.Enabled) return;
        using var db = new PostgresTestDb();
        var session = db.SeedSession();
        var recordId = Guid.NewGuid();

        var patient = new HealthExam.Domain.Patients.Patient
        {
            PatientRefID = Guid.NewGuid(),
            DivisionID = session.DivisionID,
            FullName = "Tran Thi Repo",
            PatientCode = "PAT-REPO-01"
        };
        db.Db.Patients.Add(patient);

        var record = new ExamRecord
        {
            RecordID = recordId,
            DivisionID = session.DivisionID,
            SessionID = session.SessionID,
            RecordCode = $"REC-{Guid.NewGuid():N}"[..20],
            PatientRefID = patient.PatientRefID,
            VariantCode = "DTK_03"
        };
        db.Db.ExamRecords.Add(record);
        await db.Db.SaveChangesAsync();

        var repo = new RegistrationFormRepository(db.Db);
        var loaded = await repo.GetRecordAsync(session.DivisionID, recordId);
        Assert.NotNull(loaded);
        Assert.Equal(record.RecordCode, loaded.RecordCode);

        var loadedWithSession = await repo.GetRecordWithSessionAsync(session.DivisionID, recordId);
        Assert.NotNull(loadedWithSession);
        Assert.NotNull(loadedWithSession.Session);
        Assert.NotNull(loadedWithSession.Patient);
        Assert.Equal("Tran Thi Repo", loadedWithSession.Patient.FullName);
        Assert.Equal(session.SessionID, loadedWithSession.Session.SessionID);
    }
}
