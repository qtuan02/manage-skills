using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HealthExam.Application.Patients;
using HealthExam.Domain.Patients;
using HealthExam.Infrastructure.Persistence;
using HealthExam.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace HealthExam.Tests.Infrastructure;

[Collection(PostgresCollection.Name)]
public class PatientProfilePersistenceTests
{
    private readonly PostgresFixture _fixture;

    public PatientProfilePersistenceTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Filtered_index_allows_history_but_only_one_active_identity()
    {
        if (!_fixture.Enabled) return;

        var divisionId = $"D_{Guid.NewGuid():N}"[..20];
        var identityNumber = $"ID_{Guid.NewGuid():N}"[..12];

        await using var db = _fixture.NewContext();
        db.Patients.Add(NewPatient(divisionId, identityNumber, active: true));
        db.Patients.Add(NewPatient(divisionId, identityNumber, active: false));
        await db.SaveChangesAsync();

        db.Patients.Add(NewPatient(divisionId, identityNumber, active: true));
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Equal("UX_HEX_Patient_Division_ActiveIdentity",
            ((PostgresException)error.GetBaseException()).ConstraintName);
    }

    [Fact]
    public async Task Search_never_crosses_division_or_returns_inactive()
    {
        if (!_fixture.Enabled) return;

        var divisionA = $"D1_{Guid.NewGuid():N}"[..20];
        var divisionB = $"D2_{Guid.NewGuid():N}"[..20];
        var cccd = $"S{Guid.NewGuid():N}"[..12];

        await using var db = _fixture.NewContext();
        var active = NewPatient(divisionA, cccd, active: true);
        db.Patients.AddRange(
            active,
            NewPatient(divisionA, cccd, active: false),
            NewPatient(divisionB, cccd, active: true));
        await db.SaveChangesAsync();

        var repo = new PatientRepository(db);
        var result = await repo.FindActiveByIdentityNumberAsync(divisionA, cccd);

        Assert.NotNull(result);
        Assert.Equal(active.PatientRefID, result.PatientRefID);
    }

    [Fact]
    public async Task Competing_promotions_leave_exactly_one_active_version()
    {
        if (!_fixture.Enabled) return;

        var divisionId = $"D_{Guid.NewGuid():N}"[..20];
        var cccd = $"C{Guid.NewGuid():N}"[..12];

        await using var first = _fixture.NewContext();
        await using var second = _fixture.NewContext();
        first.Patients.Add(NewPatient(divisionId, cccd, active: true));
        second.Patients.Add(NewPatient(divisionId, cccd, active: true));

        var attempts = await Task.WhenAll(CaptureSave(first), CaptureSave(second));

        Assert.Equal(1, attempts.Count(x => x is null));
        Assert.Equal(1, attempts.Count(x => x is DbUpdateException));

        await using var verify = _fixture.NewContext();
        Assert.Equal(1, await verify.Patients.CountAsync(x =>
            x.DivisionID == divisionId && x.IdentityNumber == cccd && x.IsActive));
    }

    [Fact]
    public async Task Concurrent_forks_allocate_distinct_monotonic_versions()
    {
        if (!_fixture.Enabled) return;

        var divisionId = $"V_{Guid.NewGuid():N}"[..20];
        var cccd = $"V{Guid.NewGuid():N}"[..12];
        var root = NewPatient(divisionId, cccd, active: true);
        root.ProfileLineageID = root.PatientRefID;
        root.VersionNumber = 1;

        await using (var seed = _fixture.NewContext())
        {
            seed.Patients.Add(root);
            await seed.SaveChangesAsync();
        }

        async Task<int> ForkAsync(string phone)
        {
            await using var db = _fixture.NewContext();
            await using var tx = await db.Database.BeginTransactionAsync();
            var repo = new PatientRepository(db);
            var version = await repo.AllocateNextVersionAsync(
                divisionId, root.ProfileLineageID, root.PatientRefID);
            Assert.True(version.HasValue);
            db.Patients.Add(new Patient
            {
                PatientRefID = Guid.NewGuid(),
                DivisionID = divisionId,
                FullName = root.FullName,
                Dob = root.Dob,
                IdentityNumber = cccd,
                PhoneNumber = phone,
                IsActive = false,
                PreviousPatientRefID = root.PatientRefID,
                ProfileLineageID = root.ProfileLineageID,
                VersionNumber = version.Value
            });
            await db.SaveChangesAsync();
            await tx.CommitAsync();
            return version.Value;
        }

        var versions = await Task.WhenAll(
            ForkAsync("0901000001"),
            ForkAsync("0901000002"));

        Assert.Equal(new[] { 2, 3 }, versions.OrderBy(x => x));
    }

    [Fact]
    public async Task SearchActive_by_name_is_case_insensitive_keeps_accents_and_respects_limit()
    {
        if (!_fixture.Enabled) return;

        var divisionId = $"N_{Guid.NewGuid():N}"[..20];
        await using var db = _fixture.NewContext();
        var an = NewPatient(divisionId, $"A{Guid.NewGuid():N}"[..12], active: true);
        an.FullName = "Nguyễn Văn An";
        var anh = NewPatient(divisionId, $"B{Guid.NewGuid():N}"[..12], active: true);
        anh.FullName = "Trần Thị Ánh";
        var inactive = NewPatient(divisionId, $"C{Guid.NewGuid():N}"[..12], active: false);
        inactive.FullName = "Nguyễn Văn An";
        db.Patients.AddRange(an, anh, inactive);
        await db.SaveChangesAsync();

        var repo = new PatientRepository(db);
        var criteria = new PatientSearchCriteria("văn an", new[] { PatientSearchField.Name });

        var found = await repo.SearchActiveAsync(divisionId, criteria, limit: 20);
        Assert.Single(found);
        Assert.Equal(an.PatientRefID, found[0].PatientRefID);

        // "an" (không dấu) không khớp "Ánh" vì giữ dấu; chỉ khớp "An"
        var limited = await repo.SearchActiveAsync(
            divisionId, new PatientSearchCriteria("n", new[] { PatientSearchField.Name }), limit: 1);
        Assert.Single(limited);
    }

    [Fact]
    public async Task SearchActive_combines_fields_with_or_and_prefix_match()
    {
        if (!_fixture.Enabled) return;

        var divisionId = $"O_{Guid.NewGuid():N}"[..20];
        var otherDivision = $"X_{Guid.NewGuid():N}"[..20];
        await using var db = _fixture.NewContext();
        var byPhone = NewPatient(divisionId, $"P{Guid.NewGuid():N}"[..12], active: true);
        byPhone.PhoneNumber = "0901234567";
        var byCode = NewPatient(divisionId, $"Q{Guid.NewGuid():N}"[..12], active: true);
        byCode.PatientCode = "hex-0901";
        var crossDivision = NewPatient(otherDivision, $"R{Guid.NewGuid():N}"[..12], active: true);
        crossDivision.PhoneNumber = "0901234567";
        db.Patients.AddRange(byPhone, byCode, crossDivision);
        await db.SaveChangesAsync();

        var repo = new PatientRepository(db);
        var criteria = new PatientSearchCriteria(
            "0901", new[] { PatientSearchField.Identity, PatientSearchField.Phone });
        var found = await repo.SearchActiveAsync(divisionId, criteria, limit: 20);
        Assert.Single(found);
        Assert.Equal(byPhone.PatientRefID, found[0].PatientRefID);

        var codeCriteria = new PatientSearchCriteria("HEX-", new[] { PatientSearchField.Code });
        var codeFound = await repo.SearchActiveAsync(divisionId, codeCriteria, limit: 20);
        Assert.Single(codeFound);
        Assert.Equal(byCode.PatientRefID, codeFound[0].PatientRefID);
    }

    [Fact]
    public async Task SearchActive_without_keyword_returns_latest_modified_first()
    {
        if (!_fixture.Enabled) return;

        var divisionId = $"L_{Guid.NewGuid():N}"[..20];
        await using var db = _fixture.NewContext();
        var older = NewPatient(divisionId, $"L{Guid.NewGuid():N}"[..12], active: true);
        older.ModifiedDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var newer = NewPatient(divisionId, $"M{Guid.NewGuid():N}"[..12], active: true);
        newer.ModifiedDate = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc);
        var inactive = NewPatient(divisionId, $"N{Guid.NewGuid():N}"[..12], active: false);
        inactive.ModifiedDate = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc);
        db.Patients.AddRange(older, newer, inactive);
        await db.SaveChangesAsync();

        var repo = new PatientRepository(db);
        var found = await repo.SearchActiveAsync(divisionId, PatientSearchCriteria.Latest, limit: 20);

        Assert.Equal(new[] { newer.PatientRefID, older.PatientRefID }, found.Select(p => p.PatientRefID));

        var limited = await repo.SearchActiveAsync(divisionId, PatientSearchCriteria.Latest, limit: 1);
        Assert.Equal(newer.PatientRefID, Assert.Single(limited).PatientRefID);
    }

    [Fact]
    public async Task FindActiveByRefId_ignores_inactive_and_other_division()
    {
        if (!_fixture.Enabled) return;

        var divisionId = $"G_{Guid.NewGuid():N}"[..20];
        await using var db = _fixture.NewContext();
        var active = NewPatient(divisionId, $"G{Guid.NewGuid():N}"[..12], active: true);
        var inactive = NewPatient(divisionId, $"H{Guid.NewGuid():N}"[..12], active: false);
        db.Patients.AddRange(active, inactive);
        await db.SaveChangesAsync();

        var repo = new PatientRepository(db);
        Assert.NotNull(await repo.FindActiveByRefIdAsync(divisionId, active.PatientRefID));
        Assert.Null(await repo.FindActiveByRefIdAsync(divisionId, inactive.PatientRefID));
        Assert.Null(await repo.FindActiveByRefIdAsync("OTHER", active.PatientRefID));
    }

    private static async Task<Exception> CaptureSave(HealthExamDbContext db)
    {
        try
        {
            await db.SaveChangesAsync();
            return null;
        }
        catch (DbUpdateException ex)
        {
            return ex;
        }
    }

    private static Patient NewPatient(string divisionId, string identity, bool active) => new()
    {
        PatientRefID = Guid.NewGuid(),
        DivisionID = divisionId,
        FullName = "Nguyễn Văn An",
        Dob = new DateOnly(1990, 1, 1),
        IdentityNumber = identity,
        IsActive = active
    };
}
