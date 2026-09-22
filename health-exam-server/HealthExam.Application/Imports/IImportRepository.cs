using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using HealthExam.Application.Common;
using HealthExam.Domain.Imports;

namespace HealthExam.Application.Imports;

public interface IImportRepository
{
    Task<ImportBatch> GetAsync(string divisionId, Guid batchId, bool forUpdate = false, CancellationToken ct = default);
    Task<bool> TryClaimForCommitAsync(string divisionId, Guid batchId, DateTime staleBeforeUtc, CancellationToken ct = default);
    Task ReleaseClaimAsync(string divisionId, Guid batchId, CancellationToken ct = default);
    Task<IReadOnlyList<ImportRowData>> ListRowsAsync(Guid batchId, CancellationToken ct = default);
    Task<PageResult<ImportErrorResult>> ListErrorsAsync(string divisionId, Guid batchId, int page, int size, CancellationToken ct = default);
    Task<ImportBatchResult> GetResultAsync(string divisionId, Guid batchId, int page, int size, bool onlyInvalid, CancellationToken ct = default);
    Task<IReadOnlyList<ImportRowData>> LoadInvalidRowsAsync(string divisionId, Guid batchId, CancellationToken ct = default);
    Task BulkCreateRecordsAsync(IReadOnlyList<ImportRecordWrite> records, CancellationToken ct = default);
    void Add(ImportBatch batch);
    void AddRows(IEnumerable<ImportBatchRow> rows);
    Task<IReadOnlyList<(string PatientCode, string IdentityNumber)>> ListExistingKeysInSessionAsync(string divisionId, Guid sessionId, CancellationToken ct = default);
}
