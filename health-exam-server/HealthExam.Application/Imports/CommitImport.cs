using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HealthExam.Application.Common;
using HealthExam.Application.ExamRecords;
using HealthExam.Application.ExamSessions;
using HealthExam.Domain.Common;
using HealthExam.Domain.ExamSessions;
using HealthExam.Domain.Imports;

namespace HealthExam.Application.Imports;

public interface ICommitImportHandler
{
    Task<ApplicationResult<ImportBatchResult>> HandleAsync(
        CommitImportCommand command, CancellationToken ct = default);
}

public sealed class CommitImportHandler : ICommitImportHandler
{
    private readonly IImportRepository _importRepository;
    private readonly IExamSessionRepository _sessionRepository;
    private readonly ICreateExamRecordHandler _createRecordHandler;
    private readonly IUnitOfWork _uow;
    private readonly IAuditRepository _audit;
    private readonly IClock _clock;

    public CommitImportHandler(
        IImportRepository importRepository,
        IExamSessionRepository sessionRepository,
        ICreateExamRecordHandler createRecordHandler,
        IUnitOfWork uow,
        IAuditRepository audit,
        IClock clock)
    {
        _importRepository = importRepository;
        _sessionRepository = sessionRepository;
        _createRecordHandler = createRecordHandler;
        _uow = uow;
        _audit = audit;
        _clock = clock;
    }

    public async Task<ApplicationResult<ImportBatchResult>> HandleAsync(
        CommitImportCommand command, CancellationToken ct = default)
    {
        var batch = await _importRepository.GetAsync(command.DivisionId, command.ImportId, forUpdate: true, ct);
        if (batch == null)
        {
            return ApplicationResult<ImportBatchResult>.Fail(
                ApplicationFailureCode.NotFound, "Không tìm thấy lô nạp");
        }

        var session = await _sessionRepository.GetAsync(command.DivisionId, batch.SessionID, forUpdate: false, ct);
        if (session == null)
        {
            return ApplicationResult<ImportBatchResult>.Fail(
                ApplicationFailureCode.NotFound, "Không tìm thấy đợt khám");
        }

        if (batch.State == ImportBatchState.Completed)
        {
            var already = await _importRepository.GetResultAsync(
                command.DivisionId, command.ImportId, command.Page, command.Size, onlyInvalid: false, ct);
            if (already == null)
            {
                already = GetImportHandler.MapToResult(batch, session.SessionCode, command.Page, command.Size, onlyInvalid: false);
            }
            return ApplicationResult<ImportBatchResult>.Success(already with { AlreadyCommitted = true });
        }

        if (batch.State == ImportBatchState.Discarded)
        {
            return ApplicationResult<ImportBatchResult>.Fail(
                ApplicationFailureCode.InvalidState, "Lô nạp đã bị bỏ, không nạp lại được");
        }

        if (_clock.UtcNow > batch.StartedAt + ImportBatch.BatchLifetime)
        {
            return ApplicationResult<ImportBatchResult>.Fail(
                ApplicationFailureCode.InvalidState,
                $"Lô nạp đã quá hạn {ImportBatch.BatchLifetime.TotalMinutes:0} phút, vui lòng tải lên lại");
        }

        if (batch.State != ImportBatchState.Pending && batch.State != ImportBatchState.Committing)
        {
            return ApplicationResult<ImportBatchResult>.Fail(
                ApplicationFailureCode.InvalidState,
                $"Không thể chốt lô ở trạng thái \"{batch.State}\"");
        }

        if (session.State == ExamSessionState.Closed || session.State == ExamSessionState.Cancelled)
        {
            return ApplicationResult<ImportBatchResult>.Fail(
                ApplicationFailureCode.SessionClosed,
                $"Đợt khám {session.SessionCode} đã đóng, cần mở lại đợt trước khi ghi hồ sơ");
        }

        if (batch.Rows == null)
        {
            throw new InvalidOperationException($"Lô nạp {batch.BatchID} chưa tải danh sách dòng dữ liệu");
        }

        var rows = batch.Rows.OrderBy(x => x.RowNo).ToList();

        var resuming = rows.Any(x => x.RecordID.HasValue);
        var blocking = rows.Count(x => !x.IsValid && !ImportRowErrors.IsCommitPhase(x.ErrorCode));

        if (!command.SkipInvalidRows && !resuming && blocking > 0)
        {
            return ApplicationResult<ImportBatchResult>.Fail(
                ApplicationFailureCode.BadRequest,
                $"Lô còn {blocking} dòng không đạt. Sửa file rồi tải lên lại, hoặc gọi lại với SkipInvalidRows=true để bỏ qua các dòng đó.");
        }

        var staleBefore = _clock.UtcNow - ImportBatch.StaleCommitAfter;
        var claimed = await _importRepository.TryClaimForCommitAsync(command.DivisionId, batch.BatchID, staleBefore, ct);
        if (!claimed)
        {
            var refreshed = await _importRepository.GetAsync(command.DivisionId, command.ImportId, forUpdate: false, ct);
            if (refreshed?.State == ImportBatchState.Completed)
            {
                var already = await _importRepository.GetResultAsync(
                    command.DivisionId, command.ImportId, command.Page, command.Size, onlyInvalid: false, ct);
                if (already == null)
                {
                    already = GetImportHandler.MapToResult(refreshed, session.SessionCode, command.Page, command.Size, onlyInvalid: false);
                }
                return ApplicationResult<ImportBatchResult>.Success(already with { AlreadyCommitted = true });
            }

            return ApplicationResult<ImportBatchResult>.Fail(
                ApplicationFailureCode.InvalidState,
                "Lô nạp đang được ghi bởi một lượt khác, vui lòng đợi rồi mở lại để xem kết quả");
        }

        batch.State = ImportBatchState.Committing;
        batch.ModifiedDate = _clock.UtcNow;

        var created = 0;
        try
        {
            foreach (var row in rows)
            {
                if (!row.IsValid || row.RecordID.HasValue) continue;

                var raw = GetImportHandler.ParseRaw(row.RawData);
                var fields = GetImportHandler.ToFields(raw);

                var createCmd = ToCreateRecordCommand(command, batch, session, fields);

                var createResult = await _createRecordHandler.HandleAsync(createCmd, ct);
                if (createResult.IsSuccess)
                {
                    row.RecordID = createResult.Value.RecordID;
                    created++;
                    batch.CreatedRecordCount++;
                    await _uow.SaveChangesAsync(ct);
                }
                else if (createResult.Failure.Code == ApplicationFailureCode.DuplicateInSession)
                {
                    MarkRowFailed(batch, row, ImportRowErrors.DuplicateAtCommit, createResult.Failure.Message);
                    await _uow.SaveChangesAsync(ct);
                }
                else
                {
                    MarkRowFailed(batch, row, ImportRowErrors.WriteFailed, $"Tầng ghi từ chối dòng này: {createResult.Failure.Message}");
                    await _uow.SaveChangesAsync(ct);
                }
            }

            var domainCommit = batch.CompleteCommit(_clock.UtcNow);
            if (!domainCommit.IsSuccess)
            {
                return ApplicationResult<ImportBatchResult>.Fail(
                    ApplicationFailureCode.InvalidState, domainCommit.Failure.Message);
            }

            var actorId = long.TryParse(command.ActorId, out var pId) ? pId : 0L;
            batch.ModifiedBy = actorId;
            batch.ModifiedActorKind = command.ActorKind;

            var noPortal = rows.Count(x => x.IsValid && GetImportHandler.LacksPortalCredential(x.RawData));

            _audit.Add(new AuditEntry(
                command.DivisionId,
                AuditEntityTypes.Import,
                batch.BatchID,
                AuditActions.StateChange,
                command.ActorId,
                (short)ImportBatchState.Committing,
                (short)ImportBatchState.Completed,
                new
                {
                    Created = created,
                    TotalCreated = batch.CreatedRecordCount,
                    NoPortalCredential = noPortal,
                    command.SkipInvalidRows,
                    Resumed = resuming,
                    Phase = "COMMIT"
                },
                command.ActorKind,
                command.TraceId));

            await _uow.SaveChangesAsync(ct);
        }
        catch
        {
            await _importRepository.ReleaseClaimAsync(command.DivisionId, batch.BatchID, CancellationToken.None);
            throw;
        }

        var result = await _importRepository.GetResultAsync(
            command.DivisionId, batch.BatchID, command.Page, command.Size, onlyInvalid: false, ct);

        if (result == null)
        {
            result = GetImportHandler.MapToResult(batch, session.SessionCode, command.Page, command.Size, onlyInvalid: false);
        }

        return ApplicationResult<ImportBatchResult>.Success(result);
    }

    private static CreateExamRecordCommand ToCreateRecordCommand(
        CommitImportCommand command,
        ImportBatch batch,
        ExamSession session,
        IReadOnlyDictionary<string, string> fields)
    {
        var variantCode = fields.TryGetValue("VariantCode", out var vc) && !string.IsNullOrWhiteSpace(vc)
            ? vc.Trim()
            : session.VariantCode;

        DateOnly? dob = null;
        if (fields.TryGetValue("Dob", out var dobStr) && UploadImportHandler.TryParseDate(dobStr, out var d))
            dob = d;

        short? birthYear = null;
        if (fields.TryGetValue("BirthYear", out var byStr) && UploadImportHandler.TryParseBirthYear(byStr, out var by))
            birthYear = by;

        short? genderId = null;
        if (fields.TryGetValue("GenderID", out var gStr) && short.TryParse(gStr, out var g))
            genderId = g;

        return new CreateExamRecordCommand(
            DivisionId: command.DivisionId,
            ActorId: command.ActorId,
            ActorKind: command.ActorKind,
            SessionID: batch.SessionID,
            FullName: fields.GetValueOrDefault("FullName", ""),
            PatientCode: fields.GetValueOrDefault("PatientCode", ""),
            IdentityNumber: fields.GetValueOrDefault("IdentityNumber", ""),
            InsuranceNumber: fields.GetValueOrDefault("InsuranceNumber", ""),
            PhoneNumber: fields.GetValueOrDefault("PhoneNumber", ""),
            Email: fields.GetValueOrDefault("Email", ""),
            Address: fields.GetValueOrDefault("Address", ""),
            StaffCode: fields.GetValueOrDefault("StaffCode", ""),
            OrgDeptName: fields.GetValueOrDefault("OrgDeptName", ""),
            JobTitle: fields.GetValueOrDefault("JobTitle", ""),
            VariantCode: variantCode,
            Dob: dob,
            BirthYear: birthYear,
            GenderID: genderId,
            ImportBatchID: batch.BatchID,
            TraceId: command.TraceId);
    }

    private static void MarkRowFailed(ImportBatch batch, ImportBatchRow row, string errorCode, string message)
    {
        if (row.IsValid)
        {
            batch.SuccessRow--;
            batch.ErrorRow++;
        }
        row.IsValid = false;
        row.ErrorCode = errorCode;
        row.ErrorMessage = message != null && message.Length > 1000 ? message[..1000] : message ?? "";
    }
}
