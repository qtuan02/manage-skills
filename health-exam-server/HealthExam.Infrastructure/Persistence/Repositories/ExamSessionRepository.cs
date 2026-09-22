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
using Microsoft.EntityFrameworkCore;

namespace HealthExam.Infrastructure.Persistence.Repositories;

public sealed class ExamSessionRepository : IExamSessionRepository
{
    private readonly HealthExamDbContext _db;

    public ExamSessionRepository(HealthExamDbContext db)
    {
        _db = db;
    }

    public async Task<PageResult<ExamSessionResult>> ListAsync(
        string divisionId, ExamSessionFilter filter, CancellationToken ct = default)
    {
        var q = _db.ExamSessions.AsNoTracking()
            .Where(x => x.DivisionID == divisionId && x.IsActive);

        if (filter.OrganizationID.HasValue) q = q.Where(x => x.OrganizationID == filter.OrganizationID.Value);
        if (filter.PackageID.HasValue) q = q.Where(x => x.PackageID == filter.PackageID.Value);
        if (filter.State.HasValue) q = q.Where(x => (short)x.State == filter.State.Value);
        if (filter.From.HasValue) q = q.Where(x => x.ExamDate >= filter.From.Value);
        if (filter.To.HasValue) q = q.Where(x => x.ExamDate <= filter.To.Value);

        if (!string.IsNullOrWhiteSpace(filter.Keyword))
        {
            var kw = filter.Keyword.Trim().ToLower();
            q = q.Where(x => x.SessionCode.ToLower().Contains(kw)
                          || x.SessionName.ToLower().Contains(kw)
                          || x.OrganizationName.ToLower().Contains(kw)
                          || x.ContractNo.ToLower().Contains(kw));
        }

        var total = await q.CountAsync(ct);
        var rows = await q.OrderByDescending(x => x.ExamDate).ThenBy(x => x.SessionCode)
            .Skip((filter.Page - 1) * filter.Size).Take(filter.Size)
            .ToListAsync(ct);

        var ids = rows.Select(x => x.SessionID).ToList();
        var countDict = new Dictionary<Guid, int>();
        if (ids.Count > 0)
        {
            var counts = await _db.ExamRecords.AsNoTracking()
                .Where(r => ids.Contains(r.SessionID)
                         && r.State != ExamRecordState.RegistrationCancelled
                         && r.State != ExamRecordState.ExamCancelled)
                .GroupBy(r => r.SessionID)
                .Select(g => new { SessionID = g.Key, Count = g.Count() })
                .ToListAsync(ct);

            countDict = counts.ToDictionary(c => c.SessionID, c => c.Count);
        }

        var items = rows.Select(x => new ExamSessionResult(
            x.SessionID,
            x.SessionCode,
            x.SessionName,
            x.OrganizationID,
            x.OrganizationName,
            x.ContractNo,
            x.ContractDate,
            x.ExamDate,
            x.ExamDateTo,
            x.ExamPlace,
            x.PackageID,
            x.PackageName,
            x.VariantCode,
            x.State,
            ExamSessionStateNames.Of(x.State),
            x.ExpectedCount,
            countDict.TryGetValue(x.SessionID, out var count) ? count : 0,
            x.Note,
            x.IsActive)).ToList();

        return new PageResult<ExamSessionResult>(items, filter.Page, filter.Size, total);
    }

    public async Task<ExamSession> GetAsync(
        string divisionId, Guid sessionId, bool forUpdate = false, CancellationToken ct = default)
    {
        var q = forUpdate ? _db.ExamSessions.AsTracking() : _db.ExamSessions.AsNoTracking();
        return await q.FirstOrDefaultAsync(x => x.SessionID == sessionId && x.DivisionID == divisionId, ct);
    }

    public async Task<bool> ExistsByCodeAsync(
        string divisionId, string sessionCode, Guid? excludingSessionId = null, CancellationToken ct = default)
    {
        var q = _db.ExamSessions.AsNoTracking()
            .Where(x => x.DivisionID == divisionId && x.SessionCode == sessionCode);

        if (excludingSessionId.HasValue)
        {
            q = q.Where(x => x.SessionID != excludingSessionId.Value);
        }

        return await q.AnyAsync(ct);
    }

    public void Add(ExamSession session)
    {
        _db.ExamSessions.Add(session);
    }

    public async Task<Organization> GetOrganizationAsync(
        string divisionId, Guid organizationId, CancellationToken ct = default)
    {
        return await _db.Organizations.AsNoTracking()
            .FirstOrDefaultAsync(x => x.OrganizationID == organizationId && x.DivisionID == divisionId, ct);
    }

    public async Task<ExamPackage> GetPackageAsync(
        string divisionId, Guid packageId, CancellationToken ct = default)
    {
        return await _db.ExamPackages.AsNoTracking()
            .FirstOrDefaultAsync(x => x.PackageID == packageId && x.DivisionID == divisionId, ct);
    }

    public async Task<int> GetRecordCountAsync(
        string divisionId, Guid sessionId, CancellationToken ct = default)
    {
        return await _db.ExamRecords.AsNoTracking()
            .Where(r => r.SessionID == sessionId
                     && r.DivisionID == divisionId
                     && r.State != ExamRecordState.RegistrationCancelled
                     && r.State != ExamRecordState.ExamCancelled)
            .CountAsync(ct);
    }
}
