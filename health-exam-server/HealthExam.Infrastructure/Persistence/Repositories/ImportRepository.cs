using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HealthExam.Application.Common;
using HealthExam.Application.Imports;
using HealthExam.Domain.Common;
using HealthExam.Domain.ExamRecords;
using HealthExam.Domain.ExamSessions;
using HealthExam.Domain.Imports;
using Microsoft.EntityFrameworkCore;

namespace HealthExam.Infrastructure.Persistence.Repositories;

public sealed class ImportRepository : IImportRepository
{
    private readonly HealthExamDbContext _db;

    public ImportRepository(HealthExamDbContext db)
    {
        _db = db;
    }

    public async Task<ImportBatch> GetAsync(string divisionId, Guid batchId, bool forUpdate = false, CancellationToken ct = default)
    {
        var q = _db.ImportBatches.Include(b => b.Rows)
            .Where(b => b.BatchID == batchId && b.DivisionID == divisionId);

        if (forUpdate)
        {
            q = q.AsTracking();
        }
        else
        {
            q = q.AsNoTracking();
        }

        return await q.FirstOrDefaultAsync(ct);
    }

    public async Task<bool> TryClaimForCommitAsync(string divisionId, Guid batchId, DateTime staleBeforeUtc, CancellationToken ct = default)
    {
        const string sql = """
            UPDATE "HEX_ImportBatch"
               SET "State" = {0}, "ModifiedDate" = {1}
             WHERE "BatchID" = {2} AND "DivisionID" = {3}
               AND ("State" = {4} OR ("State" = {0} AND "ModifiedDate" < {5}))
            """;

        var affected = await _db.Database.ExecuteSqlRawAsync(
            sql,
            new object[]
            {
                (short)ImportBatchState.Committing,
                DateTime.UtcNow,
                batchId,
                divisionId,
                (short)ImportBatchState.Pending,
                staleBeforeUtc
            },
            ct);

        return affected == 1;
    }

    public async Task ReleaseClaimAsync(string divisionId, Guid batchId, CancellationToken ct = default)
    {
        const string sql = """
            UPDATE "HEX_ImportBatch"
               SET "State" = {0}, "ModifiedDate" = {1}
             WHERE "BatchID" = {2} AND "DivisionID" = {3} AND "State" = {4}
            """;

        try
        {
            await _db.Database.ExecuteSqlRawAsync(
                sql,
                new object[]
                {
                    (short)ImportBatchState.Pending,
                    DateTime.UtcNow,
                    batchId,
                    divisionId,
                    (short)ImportBatchState.Committing
                },
                ct);
        }
        catch
        {
            // Swallow intentional as in legacy code
        }
    }

    public async Task<ImportBatchResult> GetResultAsync(string divisionId, Guid batchId, int page, int size, bool onlyInvalid, CancellationToken ct = default)
    {
        var batch = await _db.ImportBatches.AsNoTracking()
            .FirstOrDefaultAsync(b => b.BatchID == batchId && b.DivisionID == divisionId, ct);
        if (batch == null) return null;

        var sessionCode = await _db.ExamSessions.AsNoTracking()
            .Where(s => s.SessionID == batch.SessionID)
            .Select(s => s.SessionCode)
            .FirstOrDefaultAsync(ct) ?? "";

        page = page < 1 ? 1 : page;
        size = size < 1 ? 50 : (size > 200 ? 200 : size);

        var q = _db.ImportBatchRows.AsNoTracking().Where(r => r.BatchID == batchId);
        if (onlyInvalid)
        {
            q = q.Where(r => !r.IsValid);
        }

        var total = await q.CountAsync(ct);
        var pagedEntities = await q.OrderBy(r => r.RowNo)
            .Skip((page - 1) * size)
            .Take(size)
            .ToListAsync(ct);

        var validRawData = await _db.ImportBatchRows.AsNoTracking()
            .Where(r => r.BatchID == batchId && r.IsValid)
            .Select(r => r.RawData)
            .ToListAsync(ct);

        var noPortalCredential = validRawData.Count(GetImportHandler.LacksPortalCredential);
        var pagedRows = pagedEntities.Select(GetImportHandler.MapToRowResult).ToList();

        return new ImportBatchResult(
            ImportID: batch.BatchID,
            SessionID: batch.SessionID,
            SessionCode: sessionCode,
            FileName: batch.FileName ?? "",
            SheetName: batch.SheetName ?? "",
            TotalRows: batch.TotalRow,
            ValidRows: batch.SuccessRow,
            InvalidRows: batch.ErrorRow,
            State: batch.State,
            StateName: ImportBatchStateNames.Of(batch.State),
            StartedAt: batch.StartedAt,
            FinishedAt: batch.FinishedAt,
            ExpiresAt: batch.StartedAt + ImportBatch.BatchLifetime,
            ErrorSummary: batch.ErrorSummary ?? "",
            CreatedRecordCount: batch.CreatedRecordCount,
            NoPortalCredentialRows: noPortalCredential,
            Rows: new PageResult<ImportRowResult>(pagedRows, page, size, total));
    }

    public async Task<IReadOnlyList<ImportRowData>> ListRowsAsync(Guid batchId, CancellationToken ct = default)
    {
        var rows = await _db.ImportBatchRows.AsNoTracking()
            .Where(r => r.BatchID == batchId)
            .OrderBy(r => r.RowNo)
            .Select(r => new ImportRowData(
                r.ImportRowID,
                r.BatchID,
                r.RowNo,
                r.RawData,
                r.IsValid,
                r.ErrorCode,
                r.ErrorMessage,
                r.RecordID))
            .ToListAsync(ct);

        return rows;
    }

    public async Task<PageResult<ImportErrorResult>> ListErrorsAsync(string divisionId, Guid batchId, int page, int size, CancellationToken ct = default)
    {
        var batch = await _db.ImportBatches.AsNoTracking()
            .FirstOrDefaultAsync(b => b.BatchID == batchId && b.DivisionID == divisionId, ct);
        if (batch == null)
        {
            return new PageResult<ImportErrorResult>(Array.Empty<ImportErrorResult>(), page, size, 0);
        }

        page = page < 1 ? 1 : page;
        size = size < 1 ? 50 : (size > 200 ? 200 : size);

        var q = _db.ImportBatchRows.AsNoTracking()
            .Where(r => r.BatchID == batchId && !r.IsValid);

        var total = await q.CountAsync(ct);
        var pagedEntities = await q.OrderBy(r => r.RowNo)
            .Skip((page - 1) * size)
            .Take(size)
            .ToListAsync(ct);

        var items = pagedEntities.Select(r => new ImportErrorResult(
            r.RowNo,
            r.ErrorCode ?? "",
            r.ErrorMessage ?? "",
            GetImportHandler.ParseRaw(r.RawData))).ToList();

        return new PageResult<ImportErrorResult>(items, page, size, total);
    }

    public async Task<IReadOnlyList<ImportRowData>> LoadInvalidRowsAsync(string divisionId, Guid batchId, CancellationToken ct = default)
    {
        var exists = await _db.ImportBatches.AsNoTracking()
            .AnyAsync(b => b.BatchID == batchId && b.DivisionID == divisionId, ct);
        if (!exists) return Array.Empty<ImportRowData>();

        var rows = await _db.ImportBatchRows.AsNoTracking()
            .Where(r => r.BatchID == batchId && !r.IsValid)
            .OrderBy(r => r.RowNo)
            .Select(r => new ImportRowData(
                r.ImportRowID,
                r.BatchID,
                r.RowNo,
                r.RawData,
                r.IsValid,
                r.ErrorCode,
                r.ErrorMessage,
                r.RecordID))
            .ToListAsync(ct);

        return rows;
    }

    public async Task<IReadOnlyList<(string PatientCode, string IdentityNumber)>> ListExistingKeysInSessionAsync(string divisionId, Guid sessionId, CancellationToken ct = default)
    {
        var records = await _db.ExamRecords.AsNoTracking()
            .Where(x => x.SessionID == sessionId && x.DivisionID == divisionId
                     && x.State != ExamRecordState.RegistrationCancelled
                     && x.State != ExamRecordState.ExamCancelled)
            .Select(x => new { PatientCode = x.Patient.PatientCode, IdentityNumber = x.Patient.IdentityNumber })
            .ToListAsync(ct);

        return records.Select(x => (x.PatientCode ?? "", x.IdentityNumber ?? "")).ToList();
    }

    public void Add(ImportBatch batch)
    {
        _db.ImportBatches.Add(batch);
    }

    public void AddRows(IEnumerable<ImportBatchRow> rows)
    {
        _db.ImportBatchRows.AddRange(rows);
    }

    public Task BulkCreateRecordsAsync(IReadOnlyList<ImportRecordWrite> records, CancellationToken ct = default)
    {
        return Task.CompletedTask;
    }
}
