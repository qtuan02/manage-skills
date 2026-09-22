using System;
using System.Threading.Tasks;
using HealthExam.Application.Common;
using HealthExam.Domain.Catalogs;
using HealthExam.Infrastructure.Persistence;
using Xunit;

namespace HealthExam.Tests.Infrastructure;

[Collection(PostgresCollection.Name)]
public class UnitOfWorkPostgresTests
{
    private readonly PostgresFixture _pg;

    public UnitOfWorkPostgresTests(PostgresFixture pg) => _pg = pg;

    [Fact]
    public async Task UnitOfWork_SaveChangesAsync_returns_UniqueConflict_on_duplicate_key()
    {
        if (!_pg.Enabled) return;

        var divisionId = $"D_{Guid.NewGuid():N}"[..20];
        var orgCode = $"O_{Guid.NewGuid():N}"[..20];

        // Seed initial entity in its own scope/context
        var uow1 = _pg.NewUow(out var db1);
        await using (db1)
        {
            var org1 = new Organization
            {
                OrganizationID = Guid.NewGuid(),
                DivisionID = divisionId,
                OrgCode = orgCode,
                OrgName = "Tổ chức kiểm thử 1"
            };
            db1.Organizations.Add(org1);
            var res1 = await uow1.SaveChangesAsync();
            Assert.Equal(PersistenceSaveOutcome.Saved, res1.Outcome);
        }

        // Attempt to add duplicate entity violating unique constraint in a separate scope/context
        var uow2 = _pg.NewUow(out var db2);
        await using (db2)
        {
            var org2 = new Organization
            {
                OrganizationID = Guid.NewGuid(),
                DivisionID = divisionId,
                OrgCode = orgCode,
                OrgName = "Tổ chức kiểm thử 2"
            };
            db2.Organizations.Add(org2);
            var saveResult = await uow2.SaveChangesAsync();

            Assert.Equal(PersistenceSaveOutcome.UniqueConflict, saveResult.Outcome);
            Assert.False(string.IsNullOrWhiteSpace(saveResult.ConstraintName));
        }
    }
}
