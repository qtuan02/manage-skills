using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using HealthExam.Application.Common;
using HealthExam.Application.ExamRecords;
using HealthExam.Application.ExamSessions;
using HealthExam.Application.Imports;
using HealthExam.Domain.Common;
using HealthExam.Domain.ExamSessions;
using HealthExam.Domain.Imports;
using Xunit;

namespace HealthExam.Tests.Application;

public class ImportHandlerTests
{
    private readonly FakeImportRepository _importRepo = new();
    private readonly FakeImportSessionRepository _sessionRepo = new();
    private readonly FakeExamWorkbookReader _reader = new();
    private readonly FakeImportUnitOfWork _uow = new();
    private readonly FakeImportAuditRepository _audit = new();
    private readonly FakeImportClock _clock = new();
    private readonly FakeCreateExamRecordHandler _createRecord = new();

    private readonly Guid _sessionId = Guid.NewGuid();

    public ImportHandlerTests()
    {
        _sessionRepo.Sessions[_sessionId] = new ExamSession
        {
            SessionID = _sessionId,
            DivisionID = "D01",
            SessionCode = "DK-01",
            SessionName = "Đợt khám mẫu",
            VariantCode = "DTK_01",
            State = ExamSessionState.Open
        };
    }

    // =================================================================== TEMPLATE
    [Fact]
    public async Task DownloadImportTemplate_succeeds_for_valid_session()
    {
        var handler = new DownloadImportTemplateHandler(_sessionRepo, _reader);
        var result = await handler.HandleAsync(new DownloadImportTemplateQuery("D01", _sessionId));

        Assert.True(result.IsSuccess);
        Assert.Equal("MauDanhSachKSK.xlsx", result.Value.FileName);
        Assert.Equal(new byte[] { 1, 2, 3 }, result.Value.Content);
    }

    [Fact]
    public async Task DownloadImportTemplate_returns_not_found_when_session_missing()
    {
        var handler = new DownloadImportTemplateHandler(_sessionRepo, _reader);
        var result = await handler.HandleAsync(new DownloadImportTemplateQuery("D01", Guid.NewGuid()));

        Assert.False(result.IsSuccess);
        Assert.Equal(ApplicationFailureCode.NotFound, result.Failure.Code);
    }

    // =================================================================== UPLOAD
    [Fact]
    public async Task UploadImport_validates_null_or_empty_file()
    {
        var handler = new UploadImportHandler(_importRepo, _sessionRepo, _reader, _uow, _audit, _clock);

        var nullResult = await handler.HandleAsync(new UploadImportCommand("D01", _sessionId, null, "test.xlsx", "1"));
        Assert.False(nullResult.IsSuccess);
        Assert.Equal(ApplicationFailureCode.FileInvalid, nullResult.Failure.Code);

        using var emptyStream = new MemoryStream();
        var emptyResult = await handler.HandleAsync(new UploadImportCommand("D01", _sessionId, emptyStream, "test.xlsx", "1"));
        Assert.False(emptyResult.IsSuccess);
        Assert.Equal(ApplicationFailureCode.FileInvalid, emptyResult.Failure.Code);
    }

    [Fact]
    public async Task UploadImport_checks_session_closed()
    {
        var closedId = Guid.NewGuid();
        _sessionRepo.Sessions[closedId] = new ExamSession
        {
            SessionID = closedId,
            DivisionID = "D01",
            SessionCode = "DK-CLOSED",
            State = ExamSessionState.Closed
        };

        using var stream = new MemoryStream(new byte[] { 1 });
        var handler = new UploadImportHandler(_importRepo, _sessionRepo, _reader, _uow, _audit, _clock);
        var result = await handler.HandleAsync(new UploadImportCommand("D01", closedId, stream, "test.xlsx", "1"));

        Assert.False(result.IsSuccess);
        Assert.Equal(ApplicationFailureCode.SessionClosed, result.Failure.Code);
    }

    [Fact]
    public async Task UploadImport_detects_invalid_and_duplicate_rows()
    {
        _importRepo.ExistingKeys.Add(("NB-EXISTING", ""));

        _reader.SheetToReturn = new ParsedImportSheet(
            Name: "DanhSach",
            Rows: new List<ParsedImportRow>
            {
                new(2,
                    Raw: new Dictionary<string, string> { ["FullName"] = "Nguyen Van A", ["PatientCode"] = "NB01" },
                    Fields: new Dictionary<string, string> { ["FullName"] = "Nguyen Van A", ["PatientCode"] = "NB01" }),
                new(3,
                    Raw: new Dictionary<string, string> { ["FullName"] = "", ["PatientCode"] = "NB02" },
                    Fields: new Dictionary<string, string> { ["FullName"] = "", ["PatientCode"] = "NB02" }),
                new(4,
                    Raw: new Dictionary<string, string> { ["FullName"] = "Nguyen Van C", ["PatientCode"] = "NB01" },
                    Fields: new Dictionary<string, string> { ["FullName"] = "Nguyen Van C", ["PatientCode"] = "NB01" }),
                new(5,
                    Raw: new Dictionary<string, string> { ["FullName"] = "Nguyen Van D", ["PatientCode"] = "NB-EXISTING" },
                    Fields: new Dictionary<string, string> { ["FullName"] = "Nguyen Van D", ["PatientCode"] = "NB-EXISTING" })
            });

        using var stream = new MemoryStream(new byte[] { 1 });
        var handler = new UploadImportHandler(_importRepo, _sessionRepo, _reader, _uow, _audit, _clock);
        var result = await handler.HandleAsync(new UploadImportCommand("D01", _sessionId, stream, "test.xlsx", "1"));

        Assert.True(result.IsSuccess);
        Assert.Equal(4, result.Value.TotalRows);
        Assert.Equal(1, result.Value.ValidRows);
        Assert.Equal(3, result.Value.InvalidRows);
        Assert.Equal(1, _uow.SaveCount);
        Assert.Single(_audit.Entries);
    }

    // =================================================================== GET
    [Fact]
    public async Task GetImport_returns_mapped_batch_result()
    {
        var batch = new ImportBatch
        {
            BatchID = Guid.NewGuid(),
            DivisionID = "D01",
            SessionID = _sessionId,
            FileName = "Test.xlsx",
            State = ImportBatchState.Pending,
            TotalRow = 1,
            SuccessRow = 1
        };
        _importRepo.Add(batch);

        var handler = new GetImportHandler(_importRepo);
        var result = await handler.HandleAsync(new GetImportQuery("D01", batch.BatchID));

        Assert.True(result.IsSuccess);
        Assert.Equal(batch.BatchID, result.Value.ImportID);
    }

    [Fact]
    public async Task GetImport_returns_not_found_when_missing()
    {
        var handler = new GetImportHandler(_importRepo);
        var result = await handler.HandleAsync(new GetImportQuery("D01", Guid.NewGuid()));

        Assert.False(result.IsSuccess);
        Assert.Equal(ApplicationFailureCode.NotFound, result.Failure.Code);
    }

    // =================================================================== LIST ERRORS
    [Fact]
    public async Task ListImportErrors_returns_error_workbook()
    {
        var batch = new ImportBatch
        {
            BatchID = Guid.NewGuid(),
            DivisionID = "D01",
            SessionID = _sessionId,
            FileName = "DanhSach.xlsx"
        };
        _importRepo.Add(batch);

        var row = new ImportBatchRow
        {
            BatchID = batch.BatchID,
            RowNo = 2,
            IsValid = false,
            ErrorCode = ImportRowErrors.Required,
            ErrorMessage = "FullName: Bỏ trống (bắt buộc)"
        };
        _importRepo.AddRows(new[] { row });

        var handler = new ListImportErrorsHandler(_importRepo, _reader);
        var result = await handler.HandleAsync(new ListImportErrorsQuery("D01", batch.BatchID));

        Assert.True(result.IsSuccess);
        Assert.Equal("DongLoi_DanhSach.xlsx", result.Value.FileName);
        Assert.Equal(new byte[] { 4, 5, 6 }, result.Value.Content);
    }

    // =================================================================== COMMIT
    [Fact]
    public async Task CommitImport_idempotency_on_completed_batch()
    {
        var batch = new ImportBatch
        {
            BatchID = Guid.NewGuid(),
            DivisionID = "D01",
            SessionID = _sessionId,
            State = ImportBatchState.Completed,
            CreatedRecordCount = 5
        };
        _importRepo.Add(batch);

        var handler = new CommitImportHandler(_importRepo, _sessionRepo, _createRecord, _uow, _audit, _clock);
        var result = await handler.HandleAsync(new CommitImportCommand("D01", batch.BatchID, "1"));

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.AlreadyCommitted);
        Assert.Empty(_createRecord.CreatedCommands);
    }

    [Fact]
    public async Task CommitImport_rejects_discarded_or_expired_batch()
    {
        var discardedBatch = new ImportBatch
        {
            BatchID = Guid.NewGuid(),
            DivisionID = "D01",
            SessionID = _sessionId,
            State = ImportBatchState.Discarded
        };
        _importRepo.Add(discardedBatch);

        var handler = new CommitImportHandler(_importRepo, _sessionRepo, _createRecord, _uow, _audit, _clock);
        var discardResult = await handler.HandleAsync(new CommitImportCommand("D01", discardedBatch.BatchID, "1"));

        Assert.False(discardResult.IsSuccess);
        Assert.Equal(ApplicationFailureCode.InvalidState, discardResult.Failure.Code);

        var expiredBatch = new ImportBatch
        {
            BatchID = Guid.NewGuid(),
            DivisionID = "D01",
            SessionID = _sessionId,
            State = ImportBatchState.Pending,
            StartedAt = _clock.UtcNow - ImportBatch.BatchLifetime - TimeSpan.FromMinutes(1)
        };
        _importRepo.Add(expiredBatch);

        var expiredResult = await handler.HandleAsync(new CommitImportCommand("D01", expiredBatch.BatchID, "1"));
        Assert.False(expiredResult.IsSuccess);
        Assert.Equal(ApplicationFailureCode.InvalidState, expiredResult.Failure.Code);
    }

    [Fact]
    public async Task CommitImport_rejects_blocking_invalid_rows_without_SkipInvalidRows()
    {
        var batch = new ImportBatch
        {
            BatchID = Guid.NewGuid(),
            DivisionID = "D01",
            SessionID = _sessionId,
            State = ImportBatchState.Pending,
            StartedAt = _clock.UtcNow
        };
        _importRepo.Add(batch);

        var row1 = new ImportBatchRow { BatchID = batch.BatchID, RowNo = 2, IsValid = true, RawData = "{\"FullName\":\"A\"}" };
        var row2 = new ImportBatchRow { BatchID = batch.BatchID, RowNo = 3, IsValid = false, ErrorCode = ImportRowErrors.Required, RawData = "{}" };
        _importRepo.AddRows(new[] { row1, row2 });
        batch.Rows = new List<ImportBatchRow> { row1, row2 };

        var handler = new CommitImportHandler(_importRepo, _sessionRepo, _createRecord, _uow, _audit, _clock);
        var result = await handler.HandleAsync(new CommitImportCommand("D01", batch.BatchID, "1", SkipInvalidRows: false));

        Assert.False(result.IsSuccess);
        Assert.Equal(ApplicationFailureCode.BadRequest, result.Failure.Code);
        Assert.Contains("SkipInvalidRows=true", result.Failure.Message);

        var okResult = await handler.HandleAsync(new CommitImportCommand("D01", batch.BatchID, "1", SkipInvalidRows: true));
        Assert.True(okResult.IsSuccess);
        Assert.Single(_createRecord.CreatedCommands);
    }

    [Fact]
    public async Task CommitImport_handles_resuming_on_partial_commit()
    {
        var batch = new ImportBatch
        {
            BatchID = Guid.NewGuid(),
            DivisionID = "D01",
            SessionID = _sessionId,
            State = ImportBatchState.Pending,
            StartedAt = _clock.UtcNow
        };
        _importRepo.Add(batch);

        var row1 = new ImportBatchRow
        {
            BatchID = batch.BatchID,
            RowNo = 2,
            IsValid = true,
            RecordID = Guid.NewGuid(),
            RawData = "{\"FullName\":\"A\"}"
        };
        var row2 = new ImportBatchRow
        {
            BatchID = batch.BatchID,
            RowNo = 3,
            IsValid = false,
            ErrorCode = ImportRowErrors.WriteFailed,
            RawData = "{\"FullName\":\"B\"}"
        };
        var row3 = new ImportBatchRow
        {
            BatchID = batch.BatchID,
            RowNo = 4,
            IsValid = true,
            RawData = "{\"FullName\":\"C\"}"
        };
        _importRepo.AddRows(new[] { row1, row2, row3 });
        batch.Rows = new List<ImportBatchRow> { row1, row2, row3 };

        var handler = new CommitImportHandler(_importRepo, _sessionRepo, _createRecord, _uow, _audit, _clock);
        // Even with SkipInvalidRows: false, resuming batch does not reject on phase 2 errors
        var result = await handler.HandleAsync(new CommitImportCommand("D01", batch.BatchID, "1", SkipInvalidRows: false));

        Assert.True(result.IsSuccess);
        Assert.Single(_createRecord.CreatedCommands); // Only row 3 created (row 1 had RecordID, row 2 was invalid)
        Assert.Equal(ImportBatchState.Completed, batch.State);
    }

    [Fact]
    public async Task CommitImport_handles_duplicate_in_session_at_commit()
    {
        var batch = new ImportBatch
        {
            BatchID = Guid.NewGuid(),
            DivisionID = "D01",
            SessionID = _sessionId,
            State = ImportBatchState.Pending,
            StartedAt = _clock.UtcNow,
            SuccessRow = 1,
            TotalRow = 1
        };
        _importRepo.Add(batch);

        var row = new ImportBatchRow
        {
            BatchID = batch.BatchID,
            RowNo = 2,
            IsValid = true,
            RawData = "{\"FullName\":\"A\",\"PatientCode\":\"NB01\"}"
        };
        _importRepo.AddRows(new[] { row });
        batch.Rows = new List<ImportBatchRow> { row };

        _createRecord.ShouldFailDuplicate = true;

        var handler = new CommitImportHandler(_importRepo, _sessionRepo, _createRecord, _uow, _audit, _clock);
        var result = await handler.HandleAsync(new CommitImportCommand("D01", batch.BatchID, "1"));

        Assert.True(result.IsSuccess);
        Assert.Equal(ImportBatchState.Completed, batch.State);
        Assert.False(row.IsValid);
        Assert.Equal(ImportRowErrors.DuplicateAtCommit, row.ErrorCode);
    }

    // =================================================================== DISCARD
    [Fact]
    public async Task DiscardImport_succeeds_and_audits()
    {
        var batch = new ImportBatch
        {
            BatchID = Guid.NewGuid(),
            DivisionID = "D01",
            SessionID = _sessionId,
            State = ImportBatchState.Pending
        };
        _importRepo.Add(batch);

        var handler = new DiscardImportHandler(_importRepo, _uow, _audit, _clock);
        var result = await handler.HandleAsync(new DiscardImportCommand("D01", batch.BatchID, "1"));

        Assert.True(result.IsSuccess);
        Assert.True(result.Value);
        Assert.Equal(ImportBatchState.Discarded, batch.State);
        Assert.Single(_audit.Entries);
        Assert.Equal(1, _uow.SaveCount);

        // Idempotent discard
        var idempResult = await handler.HandleAsync(new DiscardImportCommand("D01", batch.BatchID, "1"));
        Assert.True(idempResult.IsSuccess);
        Assert.True(idempResult.Value);
    }

    [Fact]
    public async Task DiscardImport_rejects_completed_batch()
    {
        var batch = new ImportBatch
        {
            BatchID = Guid.NewGuid(),
            DivisionID = "D01",
            SessionID = _sessionId,
            State = ImportBatchState.Completed
        };
        _importRepo.Add(batch);

        var handler = new DiscardImportHandler(_importRepo, _uow, _audit, _clock);
        var result = await handler.HandleAsync(new DiscardImportCommand("D01", batch.BatchID, "1"));

        Assert.False(result.IsSuccess);
        Assert.Equal(ApplicationFailureCode.InvalidState, result.Failure.Code);
    }
}

// =================================================================== TEST FAKES

public class FakeImportRepository : IImportRepository
{
    public Dictionary<Guid, ImportBatch> Batches { get; } = new();
    public Dictionary<Guid, List<ImportBatchRow>> BatchRows { get; } = new();
    public List<(string PatientCode, string IdentityNumber)> ExistingKeys { get; } = new();
    public List<ImportRecordWrite> BulkCreated { get; } = new();
    public bool ClaimShouldSucceed { get; set; } = true;

    public Task<ImportBatch> GetAsync(string divisionId, Guid batchId, bool forUpdate = false, CancellationToken ct = default)
    {
        if (Batches.TryGetValue(batchId, out var b) && b.DivisionID == divisionId)
        {
            if (forUpdate && BatchRows.TryGetValue(batchId, out var rows))
                b.Rows = rows;
            return Task.FromResult(b);
        }
        return Task.FromResult<ImportBatch>(null);
    }

    public Task<bool> TryClaimForCommitAsync(string divisionId, Guid batchId, DateTime staleBeforeUtc, CancellationToken ct = default)
    {
        if (!ClaimShouldSucceed) return Task.FromResult(false);
        if (Batches.TryGetValue(batchId, out var b))
        {
            b.State = ImportBatchState.Committing;
            return Task.FromResult(true);
        }
        return Task.FromResult(false);
    }

    public Task ReleaseClaimAsync(string divisionId, Guid batchId, CancellationToken ct = default)
    {
        if (Batches.TryGetValue(batchId, out var b) && b.State == ImportBatchState.Committing)
        {
            b.State = ImportBatchState.Pending;
        }
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ImportRowData>> ListRowsAsync(Guid batchId, CancellationToken ct = default)
    {
        if (!BatchRows.TryGetValue(batchId, out var rows))
            return Task.FromResult<IReadOnlyList<ImportRowData>>(Array.Empty<ImportRowData>());

        var res = rows.Select(r => new ImportRowData(r.ImportRowID, r.BatchID, r.RowNo, r.RawData, r.IsValid, r.ErrorCode, r.ErrorMessage, r.RecordID)).ToList();
        return Task.FromResult<IReadOnlyList<ImportRowData>>(res);
    }

    public Task<PageResult<ImportErrorResult>> ListErrorsAsync(string divisionId, Guid batchId, int page, int size, CancellationToken ct = default)
    {
        var rows = BatchRows.GetValueOrDefault(batchId, new()).Where(r => !r.IsValid).ToList();
        var paged = rows.Skip((page - 1) * size).Take(size).Select(r => new ImportErrorResult(r.RowNo, r.ErrorCode, r.ErrorMessage)).ToList();
        return Task.FromResult(new PageResult<ImportErrorResult>(paged, page, size, rows.Count));
    }

    public Task<ImportBatchResult> GetResultAsync(string divisionId, Guid batchId, int page, int size, bool onlyInvalid, CancellationToken ct = default)
    {
        if (!Batches.TryGetValue(batchId, out var b) || b.DivisionID != divisionId)
            return Task.FromResult<ImportBatchResult>(null);

        b.Rows = BatchRows.GetValueOrDefault(batchId, new());
        return Task.FromResult(GetImportHandler.MapToResult(b, "DK-01", page, size, onlyInvalid));
    }

    public Task<IReadOnlyList<ImportRowData>> LoadInvalidRowsAsync(string divisionId, Guid batchId, CancellationToken ct = default)
    {
        var rows = BatchRows.GetValueOrDefault(batchId, new()).Where(r => !r.IsValid)
            .Select(r => new ImportRowData(r.ImportRowID, r.BatchID, r.RowNo, r.RawData, r.IsValid, r.ErrorCode, r.ErrorMessage, r.RecordID)).ToList();
        return Task.FromResult<IReadOnlyList<ImportRowData>>(rows);
    }

    public Task BulkCreateRecordsAsync(IReadOnlyList<ImportRecordWrite> records, CancellationToken ct = default)
    {
        BulkCreated.AddRange(records);
        return Task.CompletedTask;
    }

    public void Add(ImportBatch batch) => Batches[batch.BatchID] = batch;

    public void AddRows(IEnumerable<ImportBatchRow> rows)
    {
        foreach (var r in rows)
        {
            if (!BatchRows.TryGetValue(r.BatchID, out var list))
            {
                list = new List<ImportBatchRow>();
                BatchRows[r.BatchID] = list;
            }
            list.Add(r);
        }
    }

    public Task<IReadOnlyList<(string PatientCode, string IdentityNumber)>> ListExistingKeysInSessionAsync(string divisionId, Guid sessionId, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<(string PatientCode, string IdentityNumber)>>(ExistingKeys);
}

public class FakeExamWorkbookReader : IExamWorkbookReader
{
    public ParsedImportSheet SheetToReturn { get; set; } = new("DanhSach", new List<ParsedImportRow>());
    public byte[] TemplateBytes { get; set; } = new byte[] { 1, 2, 3 };
    public byte[] ErrorWorkbookBytes { get; set; } = new byte[] { 4, 5, 6 };

    public Task<ParsedImportSheet> ReadAsync(Stream file, string fileName = "", CancellationToken ct = default)
        => Task.FromResult(SheetToReturn);

    public Task<byte[]> CreateTemplateAsync(CancellationToken ct = default)
        => Task.FromResult(TemplateBytes);

    public Task<byte[]> CreateErrorWorkbookAsync(IReadOnlyList<ImportRowData> invalidRows, CancellationToken ct = default)
        => Task.FromResult(ErrorWorkbookBytes);
}

public class FakeImportUnitOfWork : IUnitOfWork
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

public class FakeImportAuditRepository : IAuditRepository
{
    public List<AuditEntry> Entries { get; } = new();

    public void Add(AuditEntry entry)
    {
        Entries.Add(entry);
    }
}

public class FakeImportClock : IClock
{
    public DateTime UtcNow { get; set; } = DateTime.UtcNow;
}

public class FakeImportSessionRepository : IExamSessionRepository
{
    public Dictionary<Guid, ExamSession> Sessions { get; } = new();

    public Task<ExamSession> GetAsync(string divisionId, Guid sessionId, bool forUpdate = false, CancellationToken ct = default)
    {
        Sessions.TryGetValue(sessionId, out var s);
        if (s != null && s.DivisionID == divisionId) return Task.FromResult(s);
        return Task.FromResult<ExamSession>(null);
    }

    public Task<PageResult<ExamSessionResult>> ListAsync(string divisionId, ExamSessionFilter filter, CancellationToken ct = default)
        => Task.FromResult(new PageResult<ExamSessionResult>(Array.Empty<ExamSessionResult>(), 1, 50, 0));

    public Task<bool> ExistsByCodeAsync(string divisionId, string sessionCode, Guid? excludingSessionId = null, CancellationToken ct = default)
        => Task.FromResult(false);

    public void Add(ExamSession session) => Sessions[session.SessionID] = session;

    public Task<HealthExam.Domain.Catalogs.Organization> GetOrganizationAsync(string divisionId, Guid organizationId, CancellationToken ct = default)
        => Task.FromResult<HealthExam.Domain.Catalogs.Organization>(null);

    public Task<HealthExam.Domain.Catalogs.ExamPackage> GetPackageAsync(string divisionId, Guid packageId, CancellationToken ct = default)
        => Task.FromResult<HealthExam.Domain.Catalogs.ExamPackage>(null);

    public Task<int> GetRecordCountAsync(string divisionId, Guid sessionId, CancellationToken ct = default)
        => Task.FromResult(0);
}

public class FakeCreateExamRecordHandler : ICreateExamRecordHandler
{
    public bool ShouldFailDuplicate { get; set; }
    public bool ShouldFailWithOtherError { get; set; }
    public List<CreateExamRecordCommand> CreatedCommands { get; } = new();

    public Task<ApplicationResult<ExamRecordResult>> HandleAsync(CreateExamRecordCommand command, CancellationToken ct = default)
    {
        CreatedCommands.Add(command);
        if (ShouldFailDuplicate)
        {
            return Task.FromResult(ApplicationResult<ExamRecordResult>.Fail(
                ApplicationFailureCode.DuplicateInSession, "Người bệnh đã có hồ sơ trong đợt khám"));
        }
        if (ShouldFailWithOtherError)
        {
            return Task.FromResult(ApplicationResult<ExamRecordResult>.Fail(
                ApplicationFailureCode.BadRequest, "Dữ liệu không hợp lệ"));
        }

        var result = new ExamRecordResult(
            RecordID: Guid.NewGuid(),
            SessionID: command.SessionID ?? Guid.NewGuid(),
            SessionCode: "DK-01",
            ExamDate: DateOnly.FromDateTime(DateTime.Today),
            RecordCode: command.RecordCode ?? "DK-01-0001",
            PatientID: 0,
            AdmissionID: null,
            PatientCode: command.PatientCode ?? "",
            FullName: command.FullName ?? "",
            Dob: null, BirthYear: null, GenderID: 0, IdentityNumber: "", InsuranceNumber: "",
            PhoneNumber: "", Email: "", Address: "", StaffCode: "", OrgDeptName: "", JobTitle: "",
            VariantCode: command.VariantCode ?? "DTK_01", VariantName: "", PackageID: null,
            PackageName: "", FormID: null, FormCode: "", SubmissionID: null,
            State: ExamRecordState.NotRegistered, StateName: "Chưa đăng ký",
            RegisteredAt: null, ExamStartedAt: null, ExamFinishedAt: null, CancelledAt: null,
            CancelReason: "", ProgressDone: 0, ProgressTotal: 0, HealthClassCode: "", Note: "",
            EthnicityCode: "", EthnicityName: "", OccupationCode: "", OccupationName: "",
            BloodAboCode: "", BloodAboName: "", BloodRhCode: "", BloodRhName: "",
            ProvinceCode: "", ProvinceName: "", WardCode: "", WardName: "",
            IdentityIssuedDate: null, IdentityIssuerCode: "", IdentityIssuerName: "",
            RelativeRelationshipCode: "", RelativeRelationshipName: "", RelativeFullName: "",
            RelativeIdentityNumber: "", RelativePhoneNumber: "", InsuranceObjectCode: "",
            InsuranceObjectName: "", InsuranceValidFrom: null, InsuranceValidTo: null,
            ExamReason: "", PatientTypeCode: "", PatientTypeName: "", PaymentSourceCode: "",
            PaymentSourceName: "", PaymentSourceOther: "", ExamLocationCode: "", ExamLocationName: "",
            CreatedDate: DateTime.UtcNow, ModifiedDate: DateTime.UtcNow);

        return Task.FromResult(ApplicationResult<ExamRecordResult>.Success(result));
    }
}
