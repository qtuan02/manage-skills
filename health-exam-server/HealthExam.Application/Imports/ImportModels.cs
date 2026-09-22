using System;
using System.Collections.Generic;
using System.IO;
using HealthExam.Application.Common;
using HealthExam.Domain.Common;
using HealthExam.Domain.Imports;

namespace HealthExam.Application.Imports;

// =================================================================== DTOs

public sealed record ImportValidationError(
    string Field,
    int? Row,
    string Reason);

public static class ImportRowStatuses
{
    public const string Valid = "Valid";
    public const string Invalid = "Invalid";
    public const string Duplicate = "Duplicate";
}

public sealed record ImportRowResult(
    int Row,
    string PatientCode,
    string FullName,
    string Status,
    string ErrorCode,
    IReadOnlyList<ImportValidationError> Errors,
    IReadOnlyList<string> Warnings,
    IReadOnlyDictionary<string, string> RawData,
    Guid? RecordID);

public static class ImportBatchStateNames
{
    public static string Of(ImportBatchState state) => state switch
    {
        ImportBatchState.Pending => "Đang xử lý",
        ImportBatchState.Completed => "Hoàn tất",
        ImportBatchState.Failed => "Lỗi",
        ImportBatchState.Discarded => "Đã hủy",
        ImportBatchState.Committing => "Đang ghi hồ sơ",
        _ => ""
    };
}

public sealed record ImportBatchResult(
    Guid ImportID,
    Guid SessionID,
    string SessionCode,
    string FileName,
    string SheetName,
    int TotalRows,
    int ValidRows,
    int InvalidRows,
    ImportBatchState State,
    string StateName,
    DateTime StartedAt,
    DateTime? FinishedAt,
    DateTime ExpiresAt,
    string ErrorSummary,
    int CreatedRecordCount,
    int NoPortalCredentialRows,
    PageResult<ImportRowResult> Rows,
    bool AlreadyCommitted = false);

public sealed record ImportErrorResult(
    int RowNo,
    string ErrorCode,
    string ErrorMessage,
    IReadOnlyDictionary<string, string> RawData = null);

public sealed record ImportRowData(
    long ImportRowID,
    Guid BatchID,
    int RowNo,
    string RawData,
    bool IsValid,
    string ErrorCode,
    string ErrorMessage,
    Guid? RecordID);

public sealed record ImportRecordWrite(
    int RowNo,
    Guid BatchID,
    Guid SessionID,
    IReadOnlyDictionary<string, string> Fields,
    string FullName,
    string PatientCode = null,
    string IdentityNumber = null,
    string InsuranceNumber = null,
    string PhoneNumber = null,
    string Email = null,
    string Address = null,
    string StaffCode = null,
    string OrgDeptName = null,
    string JobTitle = null,
    string VariantCode = null,
    DateOnly? Dob = null,
    short? BirthYear = null,
    short? GenderID = null,
    Guid? RecordID = null);

public sealed record ImportTemplateResult(
    byte[] Content,
    string ContentType,
    string FileName);

public sealed record ImportErrorExportResult(
    byte[] Content,
    string ContentType,
    string FileName);

public sealed record ParsedImportRow(
    int RowNo,
    IReadOnlyDictionary<string, string> Raw,
    IReadOnlyDictionary<string, string> Fields,
    bool IsBlank = false);

public sealed record ParsedImportSheet(
    string Name,
    IReadOnlyList<ParsedImportRow> Rows);

// =================================================================== Commands & Queries

public sealed record DownloadImportTemplateQuery(
    string DivisionId,
    Guid SessionId);

public sealed record UploadImportCommand(
    string DivisionId,
    Guid SessionId,
    Stream File,
    string FileName,
    string ActorId,
    ActorKind ActorKind = ActorKind.Employee,
    int Page = 1,
    int Size = 50,
    string TraceId = null);

public sealed record GetImportQuery(
    string DivisionId,
    Guid ImportId,
    int Page = 1,
    int Size = 50,
    bool OnlyInvalid = false);

public sealed record ListImportErrorsQuery(
    string DivisionId,
    Guid ImportId);

public sealed record CommitImportCommand(
    string DivisionId,
    Guid ImportId,
    string ActorId,
    ActorKind ActorKind = ActorKind.Employee,
    bool SkipInvalidRows = false,
    int Page = 1,
    int Size = 50,
    string TraceId = null);

public sealed record DiscardImportCommand(
    string DivisionId,
    Guid ImportId,
    string ActorId,
    ActorKind ActorKind = ActorKind.Employee,
    string TraceId = null);
