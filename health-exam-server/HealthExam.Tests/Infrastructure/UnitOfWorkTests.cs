using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using HealthExam.Application.Common;
using HealthExam.Domain.Catalogs;
using HealthExam.Domain.Common;
using HealthExam.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace HealthExam.Tests.Infrastructure;

public class UnitOfWorkTests
{
    [Fact]
    public async Task UnitOfWork_SaveChangesAsync_returns_saved_on_success()
    {
        using var db = InMemoryTestDb.CreateContext();
        var uow = new UnitOfWork(db);

        var org = new Organization
        {
            OrganizationID = Guid.NewGuid(),
            DivisionID = "DIV1",
            OrgCode = "ORG01",
            OrgName = "Org 01"
        };
        db.Organizations.Add(org);

        var saveResult = await uow.SaveChangesAsync();
        Assert.Equal(PersistenceSaveOutcome.Saved, saveResult.Outcome);
        Assert.Null(saveResult.ConstraintName);
    }

    [Fact]
    public void UnitOfWork_DiscardPendingChanges_clears_tracker()
    {
        using var db = InMemoryTestDb.CreateContext();
        var uow = new UnitOfWork(db);

        var org = new Organization
        {
            OrganizationID = Guid.NewGuid(),
            DivisionID = "DIV1",
            OrgCode = "ORG02",
            OrgName = "Org 02"
        };
        db.Organizations.Add(org);
        Assert.True(db.ChangeTracker.HasChanges());

        uow.DiscardPendingChanges();
        Assert.False(db.ChangeTracker.HasChanges());
    }

    [Fact]
    public void AuditRepository_Add_creates_audit_log()
    {
        using var db = InMemoryTestDb.CreateContext();
        var repo = new AuditRepository(db);

        var entityId = Guid.NewGuid();
        var entry = new AuditEntry(
            DivisionId: "DIV1",
            EntityType: AuditEntityTypes.Session,
            EntityId: entityId,
            Action: AuditActions.StateChange,
            ActorId: "123",
            FromState: 1,
            ToState: 2,
            Payload: new { Reason = "Test" });

        repo.Add(entry);

        var log = db.AuditLogs.Local.FirstOrDefault(x => x.EntityID == entityId);
        Assert.NotNull(log);
        Assert.Equal("DIV1", log.DivisionID);
        Assert.Equal(AuditEntityTypes.Session, log.EntityType);
        Assert.Equal(AuditActions.StateChange, log.Action);
        Assert.Equal(123, log.ActorID);
        Assert.Equal((short?)1, log.FromState);
        Assert.Equal((short?)2, log.ToState);
        Assert.Contains("Reason", log.Payload);
        Assert.Contains("Test", log.Payload);
    }
}
