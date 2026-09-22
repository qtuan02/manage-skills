using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HealthExam.Application.Signing;
using HealthExam.Domain.ExamForms;
using Microsoft.EntityFrameworkCore;

namespace HealthExam.Infrastructure.Persistence.Repositories;

public class SignStepMapRepository : ISignStepMapRepository
{
    private readonly HealthExamDbContext _db;

    public SignStepMapRepository(HealthExamDbContext db) => _db = db;

    public async Task<IReadOnlyList<SignStepMap>> ListAsync(
        string divisionId, string variantCode, CancellationToken ct = default)
        => await _db.Set<SignStepMap>()
            .AsNoTracking()
            .Where(x => x.DivisionID == divisionId && x.VariantCode == variantCode && x.IsActive)
            .OrderBy(x => x.SWStep)
            .ToListAsync(ct);
}
