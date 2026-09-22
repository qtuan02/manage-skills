using System;
using System.Linq;
using HealthExam.Domain.ExamRecords;
using HealthExam.Domain.Patients;
using HealthExam.Tests;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HealthExam.Tests.Infrastructure;

public class PersistenceModelTests
{
    [Fact]
    public void Model_keeps_critical_table_names()
    {
        using var db = InMemoryTestDb.CreateContext();
        var tables = db.Model.GetEntityTypes()
            .Select(x => x.GetTableName())
            .Where(x => x is not null)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Contains("HEX_ExamSession", tables);
        Assert.Contains("HEX_ExamRecord", tables);
        Assert.Contains("HEX_ImportBatch", tables);
        Assert.Contains("HEX_WebhookInbox", tables);
        Assert.Contains("HEX_IntegrationOutbox", tables);
    }

    [Fact]
    public void Model_contains_normalized_registration_entities_and_record_links()
    {
        using var db = InMemoryTestDb.CreateContext();
        var model = db.Model;

        Assert.NotNull(model.FindEntityType(typeof(Patient)));
        Assert.NotNull(model.FindEntityType(typeof(PatientInsurance)));
        Assert.NotNull(model.FindEntityType(typeof(PatientEmployment)));
        Assert.NotNull(model.FindEntityType(typeof(PatientRelative)));

        var record = model.FindEntityType(typeof(ExamRecord));
        Assert.NotNull(record?.FindProperty(nameof(ExamRecord.PatientRefID)));
        Assert.NotNull(record?.FindProperty(nameof(ExamRecord.InsuranceRefID)));
        Assert.NotNull(record?.FindProperty(nameof(ExamRecord.EmploymentRefID)));
        Assert.NotNull(record?.FindProperty(nameof(ExamRecord.RelativeRefID)));
    }
}
