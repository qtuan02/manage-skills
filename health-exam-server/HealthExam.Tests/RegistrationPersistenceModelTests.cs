using System;
using System.Linq;
using HealthExam.Domain.Catalogs;
using HealthExam.Domain.ExamRecords;
using HealthExam.Domain.Patients;
using HealthExam.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace HealthExam.Tests;

public class RegistrationPersistenceModelTests
{
    private static HealthExamDbContext Db()
    {
        var options = new DbContextOptionsBuilder<HealthExamDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new HealthExamDbContext(options);
    }

    [Fact]
    public void Master_data_has_tenant_unique_key_and_ward_index()
    {
        using var db = Db();
        var type = db.Model.FindEntityType(typeof(MasterDataOption));
        Assert.Equal("HEX_MasterDataOption", type!.GetTableName());
        Assert.Contains(type.GetIndexes(), x => x.IsUnique &&
            x.Properties.Select(p => p.Name).SequenceEqual(new[] { "DivisionID", "Category", "Code" }));
        Assert.Contains(type.GetIndexes(), x =>
            x.Properties.Select(p => p.Name).SequenceEqual(new[] { "DivisionID", "Category", "ParentCode", "IsActive" }));
    }

    [Fact]
    public void Active_person_indexes_exclude_both_cancel_states()
    {
        using var db = Db();
        var filters = db.Model.FindEntityType(typeof(ExamRecord))!.GetIndexes()
            .Select(x => x.GetFilter()).Where(x => x != null);
        Assert.Contains(filters, x => x!.Contains("NOT IN (4, 5)"));
    }

    [Theory]
    [InlineData("ProvinceName")]
    [InlineData("ExamReason")]
    [InlineData("PatientRefID")]
    [InlineData("InsuranceRefID")]
    [InlineData("EmploymentRefID")]
    [InlineData("RelativeRefID")]
    [InlineData("PatientTypeOptionID")]
    [InlineData("PaymentSourceOptionID")]
    [InlineData("ExamLocationOptionID")]
    public void ExamRecord_contains_expected_properties(string propertyName)
    {
        using var db = Db();
        var type = db.Model.FindEntityType(typeof(ExamRecord));
        Assert.NotNull(type!.FindProperty(propertyName));
    }

    [Theory]
    [InlineData("EthnicityCode")]
    [InlineData("IdentityIssuedDate")]
    [InlineData("ExamLocationName")]
    public void ExamRecord_does_not_contain_dropped_properties(string propertyName)
    {
        using var db = Db();
        var type = db.Model.FindEntityType(typeof(ExamRecord));
        Assert.Null(type!.FindProperty(propertyName));
    }

    [Theory]
    [InlineData("EthnicityOptionID")]
    [InlineData("IdentityIssuedDate")]
    public void Patient_contains_expected_properties(string propertyName)
    {
        using var db = Db();
        var type = db.Model.FindEntityType(typeof(Patient));
        Assert.NotNull(type!.FindProperty(propertyName));
    }

    [Fact]
    public void Normalized_entities_mapped_to_expected_tables_and_indexes()
    {
        using var db = Db();

        var patient = db.Model.FindEntityType(typeof(Patient));
        Assert.NotNull(patient);
        Assert.Equal("HEX_Patient", patient.GetTableName());
        Assert.Contains(patient.GetIndexes(), x => x.IsUnique &&
            x.Properties.Select(p => p.Name).SequenceEqual(new[] { "DivisionID", "HisPatientID" }));

        var insurance = db.Model.FindEntityType(typeof(PatientInsurance));
        Assert.NotNull(insurance);
        Assert.Equal("HEX_PatientInsurance", insurance.GetTableName());
        Assert.NotNull(insurance.FindProperty("RegistrationPlaceOptionID"));
        Assert.Contains(insurance.GetForeignKeys(), x =>
            x.Properties.Any(p => p.Name == "RegistrationPlaceOptionID") &&
            x.DeleteBehavior == DeleteBehavior.Restrict);

        var employment = db.Model.FindEntityType(typeof(PatientEmployment));
        Assert.NotNull(employment);
        Assert.Equal("HEX_PatientEmployment", employment.GetTableName());

        var relative = db.Model.FindEntityType(typeof(PatientRelative));
        Assert.NotNull(relative);
        Assert.Equal("HEX_PatientRelative", relative.GetTableName());

        // Foreign keys from ExamRecord
        var record = db.Model.FindEntityType(typeof(ExamRecord))!;
        var recordPatientFk = record.GetForeignKeys().FirstOrDefault(k => k.Properties.Any(p => p.Name == "PatientRefID"));
        Assert.NotNull(recordPatientFk);
        Assert.Equal(DeleteBehavior.Restrict, recordPatientFk.DeleteBehavior);

        var recordInsuranceFk = record.GetForeignKeys().FirstOrDefault(k => k.Properties.Any(p => p.Name == "InsuranceRefID"));
        Assert.NotNull(recordInsuranceFk);
        Assert.Equal(DeleteBehavior.Restrict, recordInsuranceFk.DeleteBehavior);

        var recordEmploymentFk = record.GetForeignKeys().FirstOrDefault(k => k.Properties.Any(p => p.Name == "EmploymentRefID"));
        Assert.NotNull(recordEmploymentFk);
        Assert.Equal(DeleteBehavior.Restrict, recordEmploymentFk.DeleteBehavior);

        var recordRelativeFk = record.GetForeignKeys().FirstOrDefault(k => k.Properties.Any(p => p.Name == "RelativeRefID"));
        Assert.NotNull(recordRelativeFk);
        Assert.Equal(DeleteBehavior.Restrict, recordRelativeFk.DeleteBehavior);

        // Foreign keys from child facts to Patient cannot cascade delete
        var insurancePatientFk = insurance.GetForeignKeys().FirstOrDefault(k => k.Properties.Any(p => p.Name == "PatientRefID"));
        Assert.NotNull(insurancePatientFk);
        Assert.True(insurancePatientFk.DeleteBehavior == DeleteBehavior.Restrict || insurancePatientFk.DeleteBehavior == DeleteBehavior.NoAction);

        var employmentPatientFk = employment.GetForeignKeys().FirstOrDefault(k => k.Properties.Any(p => p.Name == "PatientRefID"));
        Assert.NotNull(employmentPatientFk);
        Assert.True(employmentPatientFk.DeleteBehavior == DeleteBehavior.Restrict || employmentPatientFk.DeleteBehavior == DeleteBehavior.NoAction);

        var relativePatientFk = relative.GetForeignKeys().FirstOrDefault(k => k.Properties.Any(p => p.Name == "PatientRefID"));
        Assert.NotNull(relativePatientFk);
        Assert.True(relativePatientFk.DeleteBehavior == DeleteBehavior.Restrict || relativePatientFk.DeleteBehavior == DeleteBehavior.NoAction);
    }
}
