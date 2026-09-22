using System;
using System.Collections.Generic;
using HealthExam.Domain.Common;

namespace HealthExam.Application.ExamSessions;

public static class ExamSessionStateNames
{
    public static string Of(ExamSessionState state) => state switch
    {
        ExamSessionState.Draft => "Nháp",
        ExamSessionState.Open => "Đang mở",
        ExamSessionState.InProgress => "Đang khám",
        ExamSessionState.Closed => "Đã đóng",
        ExamSessionState.Cancelled => "Hủy",
        _ => ""
    };
}

public sealed record ExamSessionResult(
    Guid SessionID,
    string SessionCode,
    string SessionName,
    Guid? OrganizationID,
    string OrganizationName,
    string ContractNo,
    DateOnly? ContractDate,
    DateOnly ExamDate,
    DateOnly? ExamDateTo,
    string ExamPlace,
    Guid? PackageID,
    string PackageName,
    string VariantCode,
    ExamSessionState State,
    string StateName,
    int ExpectedCount,
    int RecordCount,
    string Note,
    bool IsActive);

public sealed record ExamSessionFilter(
    string Keyword = null,
    Guid? OrganizationID = null,
    Guid? PackageID = null,
    short? State = null,
    DateOnly? From = null,
    DateOnly? To = null,
    int Page = 1,
    int Size = 20);

public sealed record ListExamSessionsQuery(
    string DivisionId,
    ExamSessionFilter Filter);

public sealed record GetExamSessionQuery(
    string DivisionId,
    Guid SessionId);

public sealed record CreateExamSessionCommand(
    string DivisionId,
    string ActorId,
    ActorKind ActorKind,
    string SessionCode,
    string SessionName,
    Guid? OrganizationID,
    string ContractNo,
    DateOnly? ContractDate,
    DateOnly? ExamDate,
    DateOnly? ExamDateTo,
    string ExamPlace,
    int? DepartmentID,
    Guid? PackageID,
    string VariantCode,
    int? ExpectedCount,
    string Note,
    IEnumerable<string> ExtraFieldKeys = null);

public sealed record UpdateExamSessionCommand(
    string DivisionId,
    Guid SessionId,
    string ActorId,
    ActorKind ActorKind,
    string SessionCode,
    string SessionName,
    Guid? OrganizationID,
    string ContractNo,
    DateOnly? ContractDate,
    DateOnly? ExamDate,
    DateOnly? ExamDateTo,
    string ExamPlace,
    int? DepartmentID,
    Guid? PackageID,
    string VariantCode,
    int? ExpectedCount,
    string Note,
    IEnumerable<string> ExtraFieldKeys = null);

public sealed record CloseExamSessionCommand(
    string DivisionId,
    Guid SessionId,
    string ActorId,
    ActorKind ActorKind,
    string TraceId,
    string Reason = null);

public sealed record ReopenExamSessionCommand(
    string DivisionId,
    Guid SessionId,
    string ActorId,
    ActorKind ActorKind,
    string TraceId,
    string Reason);
