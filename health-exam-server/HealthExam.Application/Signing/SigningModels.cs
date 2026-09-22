using System;
using HealthExam.Domain.Common;
using HealthExam.Domain.ExamRecords;

namespace HealthExam.Application.Signing;

public sealed record SignExamSectionCommand(
    string DivisionId,
    Guid RecordId,
    int ItemGroupId,
    long EmployeeId,
    string EmployeeCode,
    string EmployeeName,
    ActorKind ActorKind,
    string Credential,
    string TraceId,
    long? ConfirmedByEmployeeId = null,
    DateTime? SignedAt = null);

public sealed record ExamSectionSignResult(
    int ItemGroupId,
    int SwStep,
    string StepName,
    long SignedByEmployeeID,
    DateTime SignedAt,
    string Status = ExamRecordSignStepStatus.Signed,
    int SigningProgressDone = 0,
    int SigningProgressTotal = 0,
    string SignedByEmployeeName = "",
    long? PerformedByEmployeeID = null,
    string PerformedByEmployeeName = "");

public sealed record CancelExamSectionSignCommand(
    string DivisionId,
    Guid RecordId,
    int ItemGroupId,
    long EmployeeId,
    ActorKind ActorKind);
