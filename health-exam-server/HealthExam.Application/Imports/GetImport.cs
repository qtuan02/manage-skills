using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using HealthExam.Application.Common;
using HealthExam.Domain.Imports;

namespace HealthExam.Application.Imports;

public interface IGetImportHandler
{
    Task<ApplicationResult<ImportBatchResult>> HandleAsync(
        GetImportQuery query, CancellationToken ct = default);
}

public sealed class GetImportHandler : IGetImportHandler
{
    private readonly IImportRepository _importRepository;

    private static readonly string[] KnownFields =
    {
        "FullName", "PatientCode", "IdentityNumber", "InsuranceNumber",
        "Dob", "BirthYear", "GenderID", "PhoneNumber", "Email", "Address",
        "StaffCode", "OrgDeptName", "JobTitle", "VariantCode"
    };

    public GetImportHandler(IImportRepository importRepository)
    {
        _importRepository = importRepository;
    }

    public async Task<ApplicationResult<ImportBatchResult>> HandleAsync(
        GetImportQuery query, CancellationToken ct = default)
    {
        var result = await _importRepository.GetResultAsync(
            query.DivisionId, query.ImportId, query.Page, query.Size, query.OnlyInvalid, ct);

        if (result == null)
        {
            return ApplicationResult<ImportBatchResult>.Fail(
                ApplicationFailureCode.NotFound, "Không tìm thấy lô nạp");
        }

        return ApplicationResult<ImportBatchResult>.Success(result);
    }

    public static ImportBatchResult MapToResult(
        ImportBatch batch,
        string sessionCode,
        int page,
        int size,
        bool onlyInvalid)
    {
        page = page < 1 ? 1 : page;
        size = size < 1 ? 50 : (size > 200 ? 200 : size);

        var rows = batch.Rows != null
            ? batch.Rows.OrderBy(x => x.RowNo).ToList()
            : new List<ImportBatchRow>();

        var filtered = onlyInvalid
            ? rows.Where(x => !x.IsValid).ToList()
            : rows;

        var total = filtered.Count;
        var pagedRows = filtered
            .Skip((page - 1) * size)
            .Take(size)
            .Select(MapToRowResult)
            .ToList();

        var validRows = rows.Where(x => x.IsValid).ToList();
        var noPortalCount = validRows.Count(r => LacksPortalCredential(r.RawData));

        return new ImportBatchResult(
            ImportID: batch.BatchID,
            SessionID: batch.SessionID,
            SessionCode: sessionCode ?? "",
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
            NoPortalCredentialRows: noPortalCount,
            Rows: new PageResult<ImportRowResult>(pagedRows, page, size, total));
    }

    public static ImportRowResult MapToRowResult(ImportBatchRow x)
    {
        var raw = ParseRaw(x.RawData);
        var fields = ToFields(raw);

        var status = x.IsValid
            ? ImportRowStatuses.Valid
            : x.ErrorCode == ImportRowErrors.Duplicate || x.ErrorCode == ImportRowErrors.DuplicateAtCommit
                ? ImportRowStatuses.Duplicate
                : ImportRowStatuses.Invalid;

        var warnings = x.IsValid && LacksPortalCredential(raw)
            ? new List<string> { ImportRowWarnings.NoPortalCredential }
            : new List<string>();

        var errors = string.IsNullOrEmpty(x.ErrorMessage)
            ? new List<ImportValidationError>()
            : x.ErrorMessage.Split(" · ").Select(part =>
            {
                var i = part.IndexOf(": ", StringComparison.Ordinal);
                return i < 0
                    ? new ImportValidationError("", x.RowNo, part)
                    : new ImportValidationError(part[..i], x.RowNo, part[(i + 2)..]);
            }).ToList();

        return new ImportRowResult(
            Row: x.RowNo,
            PatientCode: fields.GetValueOrDefault("PatientCode", ""),
            FullName: fields.GetValueOrDefault("FullName", ""),
            Status: status,
            ErrorCode: x.ErrorCode ?? "",
            Errors: errors,
            Warnings: warnings,
            RawData: raw,
            RecordID: x.RecordID);
    }

    public static bool LacksPortalCredential(string rawData)
        => LacksPortalCredential(ParseRaw(rawData));

    public static bool LacksPortalCredential(IReadOnlyDictionary<string, string> raw)
    {
        var fields = ToFields(raw);
        return fields.GetValueOrDefault("IdentityNumber", "").Trim().Length == 0
            && fields.GetValueOrDefault("InsuranceNumber", "").Trim().Length == 0;
    }

    public static Dictionary<string, string> ParseRaw(string rawData)
    {
        if (string.IsNullOrWhiteSpace(rawData)) return new Dictionary<string, string>();
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(rawData)
                   ?? new Dictionary<string, string>();
        }
        catch
        {
            return new Dictionary<string, string>();
        }
    }

    public static Dictionary<string, string> ToFields(IReadOnlyDictionary<string, string> raw)
    {
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (raw == null) return fields;

        foreach (var (header, value) in raw)
        {
            if (string.IsNullOrWhiteSpace(value)) continue;
            var h = header.Trim();
            var field = KnownFields.FirstOrDefault(f => string.Equals(f, h, StringComparison.OrdinalIgnoreCase));
            if (field != null && !fields.ContainsKey(field))
            {
                fields[field] = value.Trim();
            }
        }
        return fields;
    }
}
