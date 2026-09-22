using System;
using System.Threading;
using System.Threading.Tasks;
using HealthExam.Application.Paraclinical;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace HealthExam.Infrastructure.Persistence;

public class SequenceOrderNoAllocator : IOrderNoAllocator
{
    public const string Prefix = "CD";
    public const int Digits = 10;

    private readonly HealthExamDbContext _db;

    public SequenceOrderNoAllocator(HealthExamDbContext db)
    {
        _db = db;
    }

    public async Task<string> NextAsync(CancellationToken ct = default)
    {
        var db = _db.Database;
        await db.OpenConnectionAsync(ct);
        try
        {
            await using var cmd = db.GetDbConnection().CreateCommand();
            cmd.CommandText = $"SELECT nextval('\"{HealthExamDbContext.ParaclinicalOrderNoSequence}\"')";
            cmd.Transaction = db.CurrentTransaction?.GetDbTransaction();

            var next = await cmd.ExecuteScalarAsync(ct);
            return Compose(Convert.ToInt64(next));
        }
        finally
        {
            await db.CloseConnectionAsync();
        }
    }

    public static string Compose(long value) => Prefix + value.ToString(new string('0', Digits));
}
