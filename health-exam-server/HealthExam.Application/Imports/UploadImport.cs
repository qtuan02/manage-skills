using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using HealthExam.Application.Common;
using HealthExam.Application.ExamSessions;
using HealthExam.Domain.Common;
using HealthExam.Domain.ExamSessions;
using HealthExam.Domain.Imports;

namespace HealthExam.Application.Imports;

public interface IUploadImportHandler
{
    Task<ApplicationResult<ImportBatchResult>> HandleAsync(
        UploadImportCommand command, CancellationToken ct = default);
}

public sealed class UploadImportHandler : IUploadImportHandler
{
    private readonly IImportRepository _importRepository;
    private readonly IExamSessionRepository _sessionRepository;
    private readonly IExamWorkbookReader _workbookReader;
    private readonly IUnitOfWork _uow;
    private readonly IAuditRepository _audit;
    private readonly IClock _clock;

    public UploadImportHandler(
        IImportRepository importRepository,
        IExamSessionRepository sessionRepository,
        IExamWorkbookReader workbookReader,
        IUnitOfWork uow,
        IAuditRepository audit,
        IClock clock)
    {
        _importRepository = importRepository;
        _sessionRepository = sessionRepository;
        _workbookReader = workbookReader;
        _uow = uow;
        _audit = audit;
        _clock = clock;
    }

    public async Task<ApplicationResult<ImportBatchResult>> HandleAsync(
        UploadImportCommand command, CancellationToken ct = default)
    {
        if (command.File == null || command.File.Length == 0)
        {
            return ApplicationResult<ImportBatchResult>.Fail(
                ApplicationFailureCode.FileInvalid, "File rỗng");
        }

        var session = await _sessionRepository.GetAsync(command.DivisionId, command.SessionId, forUpdate: false, ct);
        if (session == null)
        {
            return ApplicationResult<ImportBatchResult>.Fail(
                ApplicationFailureCode.NotFound, "Không tìm thấy đợt khám");
        }

        if (session.State == ExamSessionState.Closed || session.State == ExamSessionState.Cancelled)
        {
            return ApplicationResult<ImportBatchResult>.Fail(
                ApplicationFailureCode.SessionClosed,
                $"Đợt khám {session.SessionCode} đã đóng, cần mở lại đợt trước khi tải lên");
        }

        ParsedImportSheet sheet;
        try
        {
            sheet = await _workbookReader.ReadAsync(command.File, command.FileName, ct);
        }
        catch (Exception ex)
        {
            return ApplicationResult<ImportBatchResult>.Fail(
                ApplicationFailureCode.FileInvalid, ex.Message);
        }

        if (sheet == null || sheet.Rows == null || sheet.Rows.Count == 0)
        {
            return ApplicationResult<ImportBatchResult>.Fail(
                ApplicationFailureCode.FileInvalid, "File không có dòng dữ liệu nào");
        }

        var existing = await _importRepository.ListExistingKeysInSessionAsync(
            command.DivisionId, command.SessionId, ct);

        var seenPatientCodes = new HashSet<string>(
            existing.Where(x => !string.IsNullOrEmpty(x.PatientCode)).Select(x => x.PatientCode),
            StringComparer.OrdinalIgnoreCase);
        var seenIdentities = new HashSet<string>(
            existing.Where(x => !string.IsNullOrEmpty(x.IdentityNumber)).Select(x => x.IdentityNumber),
            StringComparer.OrdinalIgnoreCase);

        var now = _clock.UtcNow;
        var actorId = long.TryParse(command.ActorId, out var parsedActorId) ? parsedActorId : 0L;

        var batch = new ImportBatch
        {
            BatchID = Guid.NewGuid(),
            DivisionID = command.DivisionId,
            SessionID = command.SessionId,
            FileName = Truncate(command.FileName, 500),
            SheetName = Truncate(sheet.Name, 100),
            State = ImportBatchState.Pending,
            StartedAt = now,
            TotalRow = sheet.Rows.Count,
            CreatedDate = now,
            CreatedBy = actorId,
            CreatedActorKind = command.ActorKind,
            ModifiedDate = now,
            ModifiedBy = actorId,
            ModifiedActorKind = command.ActorKind
        };

        var rowEntities = new List<ImportBatchRow>();

        foreach (var row in sheet.Rows)
        {
            var entity = ValidateRow(row, session, seenPatientCodes, seenIdentities, now.Year);
            entity.BatchID = batch.BatchID;
            rowEntities.Add(entity);

            if (entity.IsValid)
            {
                batch.SuccessRow++;
                var pc = row.Fields.GetValueOrDefault("PatientCode", "").Trim();
                var idn = row.Fields.GetValueOrDefault("IdentityNumber", "").Trim();
                if (pc.Length > 0) seenPatientCodes.Add(pc);
                if (idn.Length > 0) seenIdentities.Add(idn);
            }
            else
            {
                batch.ErrorRow++;
            }
        }

        batch.ErrorSummary = batch.ErrorRow == 0
            ? ""
            : Truncate($"{batch.ErrorRow}/{batch.TotalRow} dòng không đạt", 1000);
        batch.Rows = rowEntities;

        _importRepository.Add(batch);
        _importRepository.AddRows(rowEntities);

        _audit.Add(new AuditEntry(
            command.DivisionId,
            AuditEntityTypes.Import,
            batch.BatchID,
            AuditActions.Import,
            command.ActorId,
            null,
            (short)batch.State,
            new
            {
                batch.FileName,
                batch.TotalRow,
                batch.SuccessRow,
                batch.ErrorRow,
                Phase = "UPLOAD"
            },
            command.ActorKind,
            command.TraceId));

        await _uow.SaveChangesAsync(ct);

        var result = await _importRepository.GetResultAsync(
            command.DivisionId, batch.BatchID, command.Page, command.Size, onlyInvalid: false, ct);

        if (result == null)
        {
            result = GetImportHandler.MapToResult(batch, session.SessionCode, command.Page, command.Size, onlyInvalid: false);
        }

        return ApplicationResult<ImportBatchResult>.Success(result);
    }

    private static readonly JsonSerializerOptions RawDataJsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static ImportBatchRow ValidateRow(
        ParsedImportRow row,
        ExamSession session,
        ISet<string> seenPatientCodes,
        ISet<string> seenIdentities,
        int? currentYear = null)
    {
        var rawDict = row.Raw != null
            ? new Dictionary<string, string>(row.Raw)
            : new Dictionary<string, string>();

        var entity = new ImportBatchRow
        {
            RowNo = row.RowNo,
            RawData = JsonSerializer.Serialize(rawDict, RawDataJsonOptions),
            IsValid = true
        };

        var errors = new List<ImportValidationError>();
        string errorCode = null;

        void Fail(string code, string field, string reason)
        {
            errorCode ??= code;
            errors.Add(new ImportValidationError(field, row.RowNo, reason));
        }

        var fullName = row.Fields.GetValueOrDefault("FullName", "").Trim();
        if (fullName.Length == 0)
        {
            Fail(ImportRowErrors.Required, "FullName", "Bỏ trống (bắt buộc)");
        }

        var variantCode = row.Fields.GetValueOrDefault("VariantCode", "").Trim();
        if (variantCode.Length == 0)
        {
            variantCode = (session?.VariantCode ?? "").Trim();
        }

        if (variantCode.Length == 0)
        {
            Fail(ImportRowErrors.Required, "VariantCode", "Bỏ trống, và đợt khám cũng chưa khai Nhóm khám");
        }
        else if (!ExamGroups.IsValid(variantCode))
        {
            Fail(ImportRowErrors.UnknownVariant, "VariantCode", $"Nhóm khám \"{variantCode}\" không hợp lệ (DTK_01..DTK_10)");
        }

        foreach (var (field, raw) in row.Fields)
        {
            if (string.IsNullOrEmpty(raw)) continue;

            var max = RecordFieldLengths.MaxOf(field);
            if (max.HasValue && raw.Trim().Length > max.Value)
            {
                Fail(ImportRowErrors.TooLong, field,
                    $"Dài {raw.Trim().Length} ký tự, vượt trần {max.Value} ký tự của cột");
            }

            switch (field)
            {
                case "Dob" when !TryParseDate(raw, out _):
                    Fail(ImportRowErrors.BadFormat, field, $"Ngày sinh \"{raw}\" không đọc được (yyyy-MM-dd hoặc dd/MM/yyyy)");
                    break;
                case "BirthYear" when !TryParseBirthYear(raw, out _, currentYear):
                    Fail(ImportRowErrors.BadFormat, field, $"Năm sinh \"{raw}\" không hợp lệ");
                    break;
                case "GenderID" when !short.TryParse(raw, out _):
                    Fail(ImportRowErrors.BadFormat, field, $"Giới tính \"{raw}\" phải là số (1 Nam, 2 Nữ, 3 Khác)");
                    break;
            }
        }

        var patientCode = row.Fields.GetValueOrDefault("PatientCode", "").Trim();
        var identityNumber = row.Fields.GetValueOrDefault("IdentityNumber", "").Trim();

        if (patientCode.Length > 0 && seenPatientCodes.Contains(patientCode))
        {
            Fail(ImportRowErrors.Duplicate, "PatientCode", "Đã có hồ sơ trong đợt (4093)");
        }
        if (identityNumber.Length > 0 && seenIdentities.Contains(identityNumber))
        {
            Fail(ImportRowErrors.Duplicate, "IdentityNumber", "Đã có hồ sơ trong đợt (4093)");
        }

        if (errors.Count > 0)
        {
            entity.IsValid = false;
            entity.ErrorCode = errorCode ?? ImportRowErrors.Unknown;
            entity.ErrorMessage = Truncate(string.Join(" · ", errors.Select(e => $"{e.Field}: {e.Reason}")), 1000);
        }

        return entity;
    }

    public static bool TryParseDate(string raw, out DateOnly value)
    {
        value = default;
        raw = (raw ?? "").Trim();
        if (raw.Length == 0) return false;

        string[] formats = { "yyyy-MM-dd", "dd/MM/yyyy", "d/M/yyyy", "dd-MM-yyyy", "yyyy/MM/dd", "MM/dd/yyyy" };
        foreach (var f in formats)
        {
            if (DateTime.TryParseExact(raw, f, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
            {
                value = DateOnly.FromDateTime(dt);
                return true;
            }
        }

        if (double.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var serial)
            && serial > 0 && serial < 2958466)
        {
            value = DateOnly.FromDateTime(DateTime.FromOADate(serial));
            return true;
        }

        return false;
    }

    public static bool TryParseBirthYear(string raw, out short value, int? currentYear = null)
    {
        value = 0;
        if (!short.TryParse((raw ?? "").Trim(), out var y)) return false;
        var maxYear = currentYear ?? DateTime.UtcNow.Year;
        if (y < 1900 || y > maxYear) return false;
        value = y;
        return true;
    }

    private static string Truncate(string value, int max)
    {
        value ??= "";
        return value.Length <= max ? value : value[..max];
    }
}
