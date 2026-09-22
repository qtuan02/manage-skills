using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HealthExam.Application.RegistrationForms;
using HealthExam.Domain.ExamForms;
using HealthExam.Domain.ExamRecords;
using Microsoft.EntityFrameworkCore;

namespace HealthExam.Infrastructure.Persistence.Repositories;

public class RegistrationFormRepository : IRegistrationFormRepository
{
    private readonly HealthExamDbContext _db;

    public RegistrationFormRepository(HealthExamDbContext db)
    {
        _db = db;
    }

    public Task<ExamGroupFormMapping> GetActiveMappingAsync(
        string divisionId, string variantCode, CancellationToken ct = default)
    {
        return _db.ExamGroupFormMappings
            .Include(m => m.Sections)
            .FirstOrDefaultAsync(m => m.DivisionID == divisionId && m.VariantCode == variantCode && m.IsActive, ct);
    }

    public async Task<IReadOnlyList<string>> ListActiveVariantCodesAsync(
        string divisionId, CancellationToken ct = default)
    {
        var list = await _db.ExamGroupFormMappings
            .Where(m => m.DivisionID == divisionId && m.IsActive)
            .Select(m => m.VariantCode)
            .Distinct()
            .ToListAsync(ct);
        return list;
    }

    public Task<ExamRecord> GetRecordAsync(
        string divisionId, Guid recordId, bool forUpdate = false, CancellationToken ct = default)
    {
        var q = forUpdate ? _db.ExamRecords.AsTracking() : _db.ExamRecords.AsNoTracking();
        return q.Include(r => r.Patient)
            .FirstOrDefaultAsync(r => r.RecordID == recordId && r.DivisionID == divisionId, ct);
    }

    public Task<ExamRecord> GetRecordWithSessionAsync(
        string divisionId, Guid recordId, bool forUpdate = false, CancellationToken ct = default)
    {
        var q = forUpdate ? _db.ExamRecords.AsTracking() : _db.ExamRecords.AsNoTracking();
        return q.Include(r => r.Session)
            .Include(r => r.Patient)
            .FirstOrDefaultAsync(r => r.RecordID == recordId && r.DivisionID == divisionId, ct);
    }
}
