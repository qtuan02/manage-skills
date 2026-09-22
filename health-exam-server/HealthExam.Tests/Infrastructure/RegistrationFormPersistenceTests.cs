using System;
using System.Linq;
using System.Threading.Tasks;
using HealthExam.Domain.ExamForms;
using HealthExam.Domain.ExamRecords;
using HealthExam.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HealthExam.Tests.Infrastructure;

[Collection("PostgresTestDb")]
public class RegistrationFormPersistenceTests
{
    [Fact]
    public async Task Seed_mappings_exist_for_dev_and_dhtesting()
    {
        if (!PostgresTestDb.Enabled) return;
        using var db = new PostgresTestDb();
        var ctx = db.Db;

        var devMapping = await ctx.ExamGroupFormMappings
            .Include(m => m.Sections)
            .FirstOrDefaultAsync(m => m.DivisionID == "DEV" && m.VariantCode == "DTK_03");

        Assert.NotNull(devMapping);
        Assert.Equal("KSK-TREN18TUOI", devMapping.TemplateCode);
        Assert.Equal(2, devMapping.Sections.Count);
        Assert.Contains(devMapping.Sections, s => s.SectionKind == "HISTORY" && s.ItemGroupID == 64);
        Assert.Contains(devMapping.Sections, s => s.SectionKind == "EXTRA_INFO" && s.ItemGroupID == 63);

        var dhTestingMapping = await ctx.ExamGroupFormMappings
            .Include(m => m.Sections)
            .FirstOrDefaultAsync(m => m.DivisionID == "DHTESTING" && m.VariantCode == "DTK_03");

        Assert.NotNull(dhTestingMapping);
        Assert.Equal("KSK-TREN18TUOI", dhTestingMapping.TemplateCode);
        Assert.Equal(2, dhTestingMapping.Sections.Count);
    }

    [Fact]
    public async Task ExamRecord_persists_and_reads_his_form_columns()
    {
        if (!PostgresTestDb.Enabled) return;
        using var db = new PostgresTestDb();
        var session = db.SeedSession();
        var ctx = db.Db;

        var emrId = Guid.NewGuid();
        var templateId = Guid.NewGuid();

        var record = new ExamRecord
        {
            DivisionID = "DEV",
            SessionID = session.SessionID,
            RecordCode = $"TEST-{Guid.NewGuid():N}"[..20],
            VariantCode = "DTK_03",
            HisEmrDataID = emrId,
            HisFormTemplateID = templateId,
            HisFormSyncStatus = "Synced",
            HisFormSyncError = ""
        };

        ctx.ExamRecords.Add(record);
        await ctx.SaveChangesAsync();

        var options = new DbContextOptionsBuilder<HealthExamDbContext>()
            .UseNpgsql(db.ConnectionString)
            .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking)
            .Options;
        await using var readCtx = new HealthExamDbContext(options);

        var loaded = await readCtx.ExamRecords.FirstOrDefaultAsync(r => r.RecordID == record.RecordID);
        Assert.NotNull(loaded);
        Assert.Equal(emrId, loaded.HisEmrDataID);
        Assert.Equal(templateId, loaded.HisFormTemplateID);
        Assert.Equal("Synced", loaded.HisFormSyncStatus);
    }
}
