using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using HealthExam.Application.Paraclinical;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace HealthExam.Infrastructure.Persistence;

public class SequenceVendorLineNoAllocator : IVendorLineNoAllocator
{
    private readonly HealthExamDbContext _db;

    public SequenceVendorLineNoAllocator(HealthExamDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<long>> NextAsync(int count, CancellationToken ct = default)
    {
        if (count <= 0) return Array.Empty<long>();

        var db = _db.Database;
        var values = new List<long>(count);

        await db.OpenConnectionAsync(ct);
        try
        {
            await using var cmd = db.GetDbConnection().CreateCommand();
            cmd.CommandText =
                $"SELECT nextval('\"{HealthExamDbContext.VendorLineNoSequence}\"') FROM generate_series(1, @count)";
            cmd.Transaction = db.CurrentTransaction?.GetDbTransaction();

            var p = cmd.CreateParameter();
            p.ParameterName = "@count";
            p.Value = count;
            cmd.Parameters.Add(p);

            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct)) values.Add(Convert.ToInt64(reader.GetValue(0)));
        }
        finally
        {
            await db.CloseConnectionAsync();
        }

        return values;
    }
}
