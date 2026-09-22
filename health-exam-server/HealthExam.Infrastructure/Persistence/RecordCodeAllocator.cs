using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using HealthExam.Application.ExamRecords;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace HealthExam.Infrastructure.Persistence;

public class RecordCodeAllocator : IRecordCodeAllocator
{
    private readonly HealthExamDbContext _db;

    public RecordCodeAllocator(HealthExamDbContext db)
    {
        _db = db;
    }

    public async Task<string> NextAsync(string divisionId, Guid sessionId, string sessionCode, CancellationToken ct = default)
    {
        if (!_db.Database.IsRelational())
        {
            var session = await _db.ExamSessions
                .FirstOrDefaultAsync(x => x.SessionID == sessionId && x.DivisionID == divisionId, ct);
            if (session == null)
                throw new InvalidOperationException("Không tìm thấy đợt khám để cấp mã hồ sơ");
            session.LastRecordNo++;
            await _db.SaveChangesAsync(ct);
            return $"{sessionCode}-{session.LastRecordNo:D4}";
        }

        const string sql = """
            UPDATE "HEX_ExamSession"
               SET "LastRecordNo" = "LastRecordNo" + 1
             WHERE "SessionID" = @sid AND "DivisionID" = @div
            RETURNING "LastRecordNo"
            """;

        var db = _db.Database;
        await db.OpenConnectionAsync(ct);
        try
        {
            await using var cmd = db.GetDbConnection().CreateCommand();
            cmd.CommandText = sql;
            cmd.Transaction = db.CurrentTransaction?.GetDbTransaction();

            var pSid = cmd.CreateParameter();
            pSid.ParameterName = "sid";
            pSid.Value = sessionId;
            cmd.Parameters.Add(pSid);

            var pDiv = cmd.CreateParameter();
            pDiv.ParameterName = "div";
            pDiv.Value = divisionId;
            cmd.Parameters.Add(pDiv);

            var allocated = await cmd.ExecuteScalarAsync(ct);
            if (allocated is null or DBNull)
                throw new InvalidOperationException("Không tìm thấy đợt khám để cấp mã hồ sơ");

            var recordNo = Convert.ToInt32(allocated);
            return $"{sessionCode}-{recordNo:D4}";
        }
        finally
        {
            await db.CloseConnectionAsync();
        }
    }
}
