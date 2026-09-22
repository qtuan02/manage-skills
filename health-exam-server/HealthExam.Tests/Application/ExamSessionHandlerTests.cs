using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HealthExam.Application.Common;
using HealthExam.Application.ExamSessions;
using HealthExam.Domain.Catalogs;
using HealthExam.Domain.Common;
using HealthExam.Domain.ExamSessions;
using Xunit;

namespace HealthExam.Tests.Application;

public class ExamSessionHandlerTests
{
    private readonly FakeExamSessionRepository _repo = new();
    private readonly FakeUnitOfWork _uow = new();
    private readonly FakeAuditRepository _audit = new();

    // ---------------------------------------------------------------- LIST
    [Fact]
    public async Task ListExamSessions_normalizes_paging_and_forwards_filter()
    {
        var handler = new ListExamSessionsHandler(_repo);
        var filter = new ExamSessionFilter(Keyword: "test", Page: 0, Size: 500);

        var result = await handler.HandleAsync(new ListExamSessionsQuery("D01", filter));

        Assert.True(result.IsSuccess);
        Assert.Equal("D01", _repo.LastListDivisionId);
        Assert.NotNull(_repo.LastListFilter);
        Assert.Equal(1, _repo.LastListFilter.Page);
        Assert.Equal(200, _repo.LastListFilter.Size);
        Assert.Equal("test", _repo.LastListFilter.Keyword);
    }

    // ---------------------------------------------------------------- GET
    [Fact]
    public async Task GetExamSession_returns_mapped_result_when_found()
    {
        var sessionId = Guid.NewGuid();
        var session = new ExamSession
        {
            SessionID = sessionId,
            DivisionID = "D01",
            SessionCode = "DK-01",
            SessionName = "Đợt 1",
            ExamDate = new DateOnly(2026, 9, 10),
            State = ExamSessionState.Open,
            IsActive = true
        };
        _repo.Sessions[sessionId] = session;
        _repo.RecordCounts[sessionId] = 42;

        var handler = new GetExamSessionHandler(_repo);
        var result = await handler.HandleAsync(new GetExamSessionQuery("D01", sessionId));

        Assert.True(result.IsSuccess);
        Assert.Equal("DK-01", result.Value.SessionCode);
        Assert.Equal("Đang mở", result.Value.StateName);
        Assert.Equal(42, result.Value.RecordCount);
    }

    [Fact]
    public async Task GetExamSession_returns_not_found_when_missing()
    {
        var handler = new GetExamSessionHandler(_repo);
        var result = await handler.HandleAsync(new GetExamSessionQuery("D01", Guid.NewGuid()));

        Assert.False(result.IsSuccess);
        Assert.Equal(ApplicationFailureCode.NotFound, result.Failure.Code);
    }

    // ---------------------------------------------------------------- CREATE
    [Fact]
    public async Task CreateExamSession_rejects_state_field_in_extra_keys()
    {
        var handler = new CreateExamSessionHandler(_repo, _uow);
        var command = new CreateExamSessionCommand(
            "D01", "1", ActorKind.Employee, "DK-NEW", "Đợt", null, null, null,
            new DateOnly(2026, 9, 10), null, null, null, null, null, 10, null,
            ExtraFieldKeys: new[] { "state" });

        var result = await handler.HandleAsync(command);

        Assert.False(result.IsSuccess);
        Assert.Equal(ApplicationFailureCode.BadRequest, result.Failure.Code);
        Assert.Contains("/close", result.Failure.Message);
        Assert.Empty(_repo.Sessions);
    }

    [Fact]
    public async Task CreateExamSession_validates_required_fields()
    {
        var handler = new CreateExamSessionHandler(_repo, _uow);
        var command = new CreateExamSessionCommand(
            "D01", "1", ActorKind.Employee, "", "Đợt", null, null, null,
            null, null, null, null, null, null, 10, null);

        var result = await handler.HandleAsync(command);

        Assert.False(result.IsSuccess);
        Assert.Equal(ApplicationFailureCode.BadRequest, result.Failure.Code);
    }

    [Fact]
    public async Task CreateExamSession_validates_date_range()
    {
        var handler = new CreateExamSessionHandler(_repo, _uow);
        var command = new CreateExamSessionCommand(
            "D01", "1", ActorKind.Employee, "DK-01", "Đợt", null, null, null,
            new DateOnly(2026, 9, 10), new DateOnly(2026, 9, 5), null, null, null, null, 10, null);

        var result = await handler.HandleAsync(command);

        Assert.False(result.IsSuccess);
        Assert.Equal(ApplicationFailureCode.BadRequest, result.Failure.Code);
    }

    [Fact]
    public async Task CreateExamSession_rejects_duplicate_code()
    {
        _repo.ExistingCodes.Add(("D01", "DK-01"));

        var handler = new CreateExamSessionHandler(_repo, _uow);
        var command = new CreateExamSessionCommand(
            "D01", "1", ActorKind.Employee, "DK-01", "Đợt", null, null, null,
            new DateOnly(2026, 9, 10), null, null, null, null, null, 10, null);

        var result = await handler.HandleAsync(command);

        Assert.False(result.IsSuccess);
        Assert.Equal(ApplicationFailureCode.BadRequest, result.Failure.Code);
        Assert.Contains("đã tồn tại", result.Failure.Message);
    }

    [Fact]
    public async Task CreateExamSession_snapshots_org_and_package_and_saves()
    {
        var orgId = Guid.NewGuid();
        var pkgId = Guid.NewGuid();
        _repo.Organizations[( "D01", orgId )] = new Organization { OrganizationID = orgId, OrgName = "Công ty ABC" };
        _repo.Packages[( "D01", pkgId )] = new ExamPackage { PackageID = pkgId, PackageName = "Gói Vip" };

        var handler = new CreateExamSessionHandler(_repo, _uow);
        var command = new CreateExamSessionCommand(
            "D01", "123", ActorKind.Employee, "DK-01", "Đợt ABC", orgId, "HD-01",
            new DateOnly(2026, 1, 1), new DateOnly(2026, 9, 10), new DateOnly(2026, 9, 12),
            "Hà Nội", 1, pkgId, "DTK_01", 100, "Ghi chú");

        var result = await handler.HandleAsync(command);

        Assert.True(result.IsSuccess);
        Assert.Equal("DK-01", result.Value.SessionCode);
        Assert.Equal("Công ty ABC", result.Value.OrganizationName);
        Assert.Equal("Gói Vip", result.Value.PackageName);
        Assert.Equal(ExamSessionState.Draft, result.Value.State);
        Assert.Equal(1, _uow.SaveCount);
    }

    // ---------------------------------------------------------------- UPDATE
    [Fact]
    public async Task UpdateExamSession_rejects_state_field()
    {
        var handler = new UpdateExamSessionHandler(_repo, _uow);
        var command = new UpdateExamSessionCommand(
            "D01", Guid.NewGuid(), "1", ActorKind.Employee, "DK-01", "Tên mới",
            null, null, null, null, null, null, null, null, null, null, null,
            ExtraFieldKeys: new[] { "State" });

        var result = await handler.HandleAsync(command);

        Assert.False(result.IsSuccess);
        Assert.Equal(ApplicationFailureCode.BadRequest, result.Failure.Code);
    }

    [Fact]
    public async Task UpdateExamSession_returns_not_found_when_missing()
    {
        var handler = new UpdateExamSessionHandler(_repo, _uow);
        var command = new UpdateExamSessionCommand(
            "D01", Guid.NewGuid(), "1", ActorKind.Employee, "DK-01", "Tên mới",
            null, null, null, null, null, null, null, null, null, null, null);

        var result = await handler.HandleAsync(command);

        Assert.False(result.IsSuccess);
        Assert.Equal(ApplicationFailureCode.NotFound, result.Failure.Code);
    }

    [Fact]
    public async Task UpdateExamSession_applies_changes_and_saves()
    {
        var sessionId = Guid.NewGuid();
        var session = new ExamSession
        {
            SessionID = sessionId,
            DivisionID = "D01",
            SessionCode = "DK-OLD",
            SessionName = "Tên cũ",
            ExamDate = new DateOnly(2026, 9, 1),
            State = ExamSessionState.Open,
            IsActive = true
        };
        _repo.Sessions[sessionId] = session;

        var handler = new UpdateExamSessionHandler(_repo, _uow);
        var command = new UpdateExamSessionCommand(
            "D01", sessionId, "123", ActorKind.Employee, "DK-NEW", "Tên mới",
            null, "HD-02", null, new DateOnly(2026, 9, 20), null, "HCM",
            2, null, "DTK_02", 50, "Cập nhật");

        var result = await handler.HandleAsync(command);

        Assert.True(result.IsSuccess);
        Assert.Equal("DK-NEW", session.SessionCode);
        Assert.Equal("Tên mới", session.SessionName);
        Assert.Equal(1, _uow.SaveCount);
    }

    // ---------------------------------------------------------------- CLOSE
    [Fact]
    public async Task CloseExamSession_returns_not_found_when_missing()
    {
        var handler = new CloseExamSessionHandler(_repo, _uow, _audit);
        var result = await handler.HandleAsync(
            new CloseExamSessionCommand("D01", Guid.NewGuid(), "1", ActorKind.Employee, "TRACE-1"));

        Assert.False(result.IsSuccess);
        Assert.Equal(ApplicationFailureCode.NotFound, result.Failure.Code);
    }

    [Fact]
    public async Task CloseExamSession_returns_session_closed_when_already_closed()
    {
        var sessionId = Guid.NewGuid();
        _repo.Sessions[sessionId] = new ExamSession
        {
            SessionID = sessionId,
            DivisionID = "D01",
            SessionCode = "DK-01",
            State = ExamSessionState.Closed
        };

        var handler = new CloseExamSessionHandler(_repo, _uow, _audit);
        var result = await handler.HandleAsync(
            new CloseExamSessionCommand("D01", sessionId, "1", ActorKind.Employee, "TRACE-1"));

        Assert.False(result.IsSuccess);
        Assert.Equal(ApplicationFailureCode.SessionClosed, result.Failure.Code);
        Assert.Empty(_audit.Entries);
    }

    [Fact]
    public async Task CloseExamSession_returns_invalid_state_when_cancelled()
    {
        var sessionId = Guid.NewGuid();
        _repo.Sessions[sessionId] = new ExamSession
        {
            SessionID = sessionId,
            DivisionID = "D01",
            SessionCode = "DK-01",
            State = ExamSessionState.Cancelled
        };

        var handler = new CloseExamSessionHandler(_repo, _uow, _audit);
        var result = await handler.HandleAsync(
            new CloseExamSessionCommand("D01", sessionId, "1", ActorKind.Employee, "TRACE-1"));

        Assert.False(result.IsSuccess);
        Assert.Equal(ApplicationFailureCode.InvalidState, result.Failure.Code);
        Assert.Empty(_audit.Entries);
    }

    [Fact]
    public async Task CloseExamSession_closes_session_writes_audit_and_saves()
    {
        var sessionId = Guid.NewGuid();
        var session = new ExamSession
        {
            SessionID = sessionId,
            DivisionID = "D01",
            SessionCode = "DK-01",
            State = ExamSessionState.Open
        };
        _repo.Sessions[sessionId] = session;

        var handler = new CloseExamSessionHandler(_repo, _uow, _audit);
        var result = await handler.HandleAsync(
            new CloseExamSessionCommand("D01", sessionId, "9137", ActorKind.Employee, "TRACE-XYZ", "Khám xong"));

        Assert.True(result.IsSuccess);
        Assert.Equal(ExamSessionState.Closed, session.State);
        Assert.Equal(1, _uow.SaveCount);

        var audit = Assert.Single(_audit.Entries);
        Assert.Equal(AuditEntityTypes.Session, audit.EntityType);
        Assert.Equal(AuditActions.StateChange, audit.Action);
        Assert.Equal((short)ExamSessionState.Open, audit.FromState);
        Assert.Equal((short)ExamSessionState.Closed, audit.ToState);
        Assert.Equal("9137", audit.ActorId);
        Assert.Equal("TRACE-XYZ", audit.TraceId);
    }

    // ---------------------------------------------------------------- REOPEN
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ReopenExamSession_validates_reason_required(string reason)
    {
        var handler = new ReopenExamSessionHandler(_repo, _uow, _audit);
        var result = await handler.HandleAsync(
            new ReopenExamSessionCommand("D01", Guid.NewGuid(), "1", ActorKind.Employee, "TRACE-1", reason));

        Assert.False(result.IsSuccess);
        Assert.Equal(ApplicationFailureCode.BadRequest, result.Failure.Code);
    }

    [Fact]
    public async Task ReopenExamSession_returns_invalid_state_when_not_closed()
    {
        var sessionId = Guid.NewGuid();
        _repo.Sessions[sessionId] = new ExamSession
        {
            SessionID = sessionId,
            DivisionID = "D01",
            SessionCode = "DK-01",
            State = ExamSessionState.Open
        };

        var handler = new ReopenExamSessionHandler(_repo, _uow, _audit);
        var result = await handler.HandleAsync(
            new ReopenExamSessionCommand("D01", sessionId, "1", ActorKind.Employee, "TRACE-1", "Lý do"));

        Assert.False(result.IsSuccess);
        Assert.Equal(ApplicationFailureCode.InvalidState, result.Failure.Code);
        Assert.Contains("Đang mở", result.Failure.Message);
        Assert.Empty(_audit.Entries);
    }

    [Fact]
    public async Task ReopenExamSession_reopens_closed_session_writes_audit_and_saves()
    {
        var sessionId = Guid.NewGuid();
        var session = new ExamSession
        {
            SessionID = sessionId,
            DivisionID = "D01",
            SessionCode = "DK-01",
            State = ExamSessionState.Closed
        };
        _repo.Sessions[sessionId] = session;

        var handler = new ReopenExamSessionHandler(_repo, _uow, _audit);
        var result = await handler.HandleAsync(
            new ReopenExamSessionCommand("D01", sessionId, "9137", ActorKind.Employee, "TRACE-XYZ", "  Bổ sung người  "));

        Assert.True(result.IsSuccess);
        Assert.Equal(ExamSessionState.Open, session.State);
        Assert.Equal(1, _uow.SaveCount);

        var audit = Assert.Single(_audit.Entries);
        Assert.Equal((short)ExamSessionState.Closed, audit.FromState);
        Assert.Equal((short)ExamSessionState.Open, audit.ToState);
        Assert.Equal("9137", audit.ActorId);
    }
}

// ---------------------------------------------------------------- FAKES
public class FakeExamSessionRepository : IExamSessionRepository
{
    public Dictionary<Guid, ExamSession> Sessions { get; } = new();
    public Dictionary<Guid, int> RecordCounts { get; } = new();
    public HashSet<(string DivisionId, string Code)> ExistingCodes { get; } = new();
    public Dictionary<(string DivisionId, Guid OrgId), Organization> Organizations { get; } = new();
    public Dictionary<(string DivisionId, Guid PkgId), ExamPackage> Packages { get; } = new();

    public string LastListDivisionId { get; private set; }
    public ExamSessionFilter LastListFilter { get; private set; }

    public Task<PageResult<ExamSessionResult>> ListAsync(
        string divisionId, ExamSessionFilter filter, CancellationToken ct = default)
    {
        LastListDivisionId = divisionId;
        LastListFilter = filter;
        return Task.FromResult(new PageResult<ExamSessionResult>(
            Array.Empty<ExamSessionResult>(), filter.Page, filter.Size, 0));
    }

    public Task<ExamSession> GetAsync(
        string divisionId, Guid sessionId, bool forUpdate = false, CancellationToken ct = default)
    {
        Sessions.TryGetValue(sessionId, out var s);
        if (s != null && s.DivisionID == divisionId) return Task.FromResult(s);
        return Task.FromResult<ExamSession>(null);
    }

    public Task<bool> ExistsByCodeAsync(
        string divisionId, string sessionCode, Guid? excludingSessionId = null, CancellationToken ct = default)
    {
        var exists = ExistingCodes.Contains((divisionId, sessionCode));
        return Task.FromResult(exists);
    }

    public void Add(ExamSession session)
    {
        Sessions[session.SessionID] = session;
    }

    public Task<Organization> GetOrganizationAsync(
        string divisionId, Guid organizationId, CancellationToken ct = default)
    {
        Organizations.TryGetValue((divisionId, organizationId), out var org);
        return Task.FromResult(org);
    }

    public Task<ExamPackage> GetPackageAsync(
        string divisionId, Guid packageId, CancellationToken ct = default)
    {
        Packages.TryGetValue((divisionId, packageId), out var pkg);
        return Task.FromResult(pkg);
    }

    public Task<int> GetRecordCountAsync(
        string divisionId, Guid sessionId, CancellationToken ct = default)
    {
        RecordCounts.TryGetValue(sessionId, out var count);
        return Task.FromResult(count);
    }
}

public class FakeUnitOfWork : IUnitOfWork
{
    public int SaveCount { get; private set; }

    public Task<IApplicationTransaction> BeginAsync(CancellationToken ct = default)
        => Task.FromResult<IApplicationTransaction>(new FakeTransaction());

    public Task<PersistenceSaveResult> SaveChangesAsync(CancellationToken ct = default)
    {
        SaveCount++;
        return Task.FromResult(new PersistenceSaveResult(PersistenceSaveOutcome.Saved));
    }

    public void DiscardPendingChanges() { }
}

public class FakeTransaction : IApplicationTransaction
{
    public Task CommitAsync(CancellationToken ct = default) => Task.CompletedTask;
    public Task RollbackAsync(CancellationToken ct = default) => Task.CompletedTask;
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

public class FakeAuditRepository : IAuditRepository
{
    public List<AuditEntry> Entries { get; } = new();

    public void Add(AuditEntry entry)
    {
        Entries.Add(entry);
    }
}
