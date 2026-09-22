using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using HealthExam.API.Contracts;
using HealthExam.API.Middlewares;
using HealthExam.Application.Common;
using HealthExam.Application.ExamRecords;
using HealthExam.Application.ExamSessions;
using HealthExam.Application.Imports;
using HealthExam.Application.Patients;
using HealthExam.Domain.Common;
using HealthExam.Domain.Imports;
using HealthExam.Infrastructure.Excel;
using HealthExam.Infrastructure.Persistence;
using HealthExam.Infrastructure.Persistence.Repositories;

namespace HealthExam.Tests;

public class TestImportService
{
    private readonly IUploadImportHandler _uploadHandler;
    private readonly ICommitImportHandler _commitHandler;
    private readonly IDiscardImportHandler _discardHandler;
    private readonly IGetImportHandler _getHandler;
    private readonly FakeHealthExamContext _ctx;

    public TestImportService(
        HealthExamDbContext db,
        FakeHealthExamContext ctx,
        IRecordCodeAllocator allocator = null)
    {
        _ctx = ctx;
        var importRepo = new ImportRepository(db);
        var sessionRepo = new ExamSessionRepository(db);
        var recordRepo = new ExamRecordRepository(db);
        var patientRepo = new PatientRepository(db);
        var uow = new UnitOfWork(db);
        var audit = new AuditRepository(db);
        var clock = new SystemClock();
        var writer = new PatientRegistrationWriter(patientRepo, uow, clock);
        var codeAllocator = allocator ?? new RecordCodeAllocator(db);
        var createRecordHandler = new CreateExamRecordHandler(recordRepo, sessionRepo, codeAllocator, uow, audit, writer);
        var reader = new ExamWorkbookReader();

        _uploadHandler = new UploadImportHandler(importRepo, sessionRepo, reader, uow, audit, clock);
        _commitHandler = new CommitImportHandler(importRepo, sessionRepo, createRecordHandler, uow, audit, clock);
        _discardHandler = new DiscardImportHandler(importRepo, uow, audit, clock);
        _getHandler = new GetImportHandler(importRepo);
    }

    public async Task<ImportBatchResult> UploadAsync(
        Guid sessionId, Stream file, string fileName, int page = 1, int size = 50, CancellationToken ct = default)
    {
        var cmd = new UploadImportCommand(_ctx.DivisionId, sessionId, file, fileName, _ctx.ActorId.ToString(), _ctx.ActorKind, page, size);
        var res = await _uploadHandler.HandleAsync(cmd, ct);
        if (!res.IsSuccess)
        {
            throw new HealthExamException(ApplicationResultMapper.ToErrorCode(res.Failure.Code), res.Failure.Message);
        }
        return res.Value;
    }

    public async Task<ImportBatchResult> CommitAsync(
        Guid importId, ImportCommitRequest request, int page = 1, int size = 50, CancellationToken ct = default)
    {
        var cmd = new CommitImportCommand(_ctx.DivisionId, importId, _ctx.ActorId.ToString(), _ctx.ActorKind, request?.SkipInvalidRows ?? false, page, size);
        var res = await _commitHandler.HandleAsync(cmd, ct);
        if (!res.IsSuccess)
        {
            throw new HealthExamException(ApplicationResultMapper.ToErrorCode(res.Failure.Code), res.Failure.Message);
        }
        return res.Value;
    }

    public async Task<ImportBatchResult> GetAsync(
        Guid importId, int page, int size, bool onlyInvalid, CancellationToken ct = default)
    {
        var query = new GetImportQuery(_ctx.DivisionId, importId, page, size, onlyInvalid);
        var res = await _getHandler.HandleAsync(query, ct);
        if (!res.IsSuccess)
        {
            throw new HealthExamException(ApplicationResultMapper.ToErrorCode(res.Failure.Code), res.Failure.Message);
        }
        return res.Value;
    }

    public async Task DiscardAsync(Guid importId, CancellationToken ct = default)
    {
        var cmd = new DiscardImportCommand(_ctx.DivisionId, importId, _ctx.ActorId.ToString(), _ctx.ActorKind);
        var res = await _discardHandler.HandleAsync(cmd, ct);
        if (!res.IsSuccess)
        {
            throw new HealthExamException(ApplicationResultMapper.ToErrorCode(res.Failure.Code), res.Failure.Message);
        }
    }
}
