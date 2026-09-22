#nullable enable

using System;
using HealthExam.Application.Common;
using HealthExam.Application.Integrations;
using HealthExam.Domain.Common;

namespace HealthExam.Application.His;

public sealed record HisFormDefinitionResult(
    Guid TemplateId,
    string TemplateCode,
    string TemplateName,
    int FileDocTypeId,
    string VersionCode,
    bool IsDraft,
    bool Active,
    string DetailsJson,
    string LayoutJson);

public sealed record GetHisFormDefinitionQuery(
    string TemplateCode,
    string DivisionId,
    string Credential,
    string TraceId);

public sealed record ExamRecordHisFormResult(
    Guid RecordId,
    long? AdmissionId,
    HisFormDefinitionResult Definition);

public sealed record GetExamRecordHisFormQuery(
    string DivisionId,
    Guid RecordId,
    string Credential,
    string TraceId);

public sealed record ListHisProcessesQuery(
    string DivisionId,
    Guid RecordId,
    string Credential,
    string TraceId);

public sealed record GetHisProcessQuery(
    string DivisionId,
    Guid RecordId,
    Guid ProcessId,
    string Credential,
    string TraceId);

public sealed record HisRecordSectionResult(
    Guid RecordId,
    long? AdmissionId,
    int ItemGroupId,
    string LayoutJson,
    ExamRecordState RecordState = ExamRecordState.Waiting,
    string? CurrentStepStatus = null,
    long? CurrentStepSignedByEmployeeID = null,
    DateTime? CurrentStepSignedAt = null,
    int SigningProgressDone = 0,
    int SigningProgressTotal = 0,
    string? CurrentStepSignedByEmployeeName = null,
    long? CurrentStepPerformedByEmployeeID = null,
    string? CurrentStepPerformedByEmployeeName = null,
    System.Collections.Generic.IReadOnlyList<SignRoleEmployee>? Signers = null);

public sealed record GetHisRecordSectionQuery(
    string DivisionId,
    Guid RecordId,
    int ItemGroupId,
    string Credential,
    string TraceId);

public sealed record HisPatientCreateRequest(
    string PatientCode, string FirstName, string LastName, string FullName,
    byte Gender, DateTime? BirthDate, int BirthYear, string IdentityNumber,
    string PhoneNumber, string Email, string Address);

public sealed record HisAdmissionCreateRequest(
    byte IsOutPatient, string AdmissionCode, DateTime AdmissionDate,
    int DepartmentID, string DepartmentCode, HisPatientCreateRequest Patient);

public sealed record MedicalProcessCreateRequest(
    string MedicalTypeCode,
    long AdmissionID,
    string MedicalTypeCodeOld = null);

public sealed record HisCallContext(string Credential, string TraceId, string DivisionId);

public sealed record EnsureHisPatientCommand(
    string DivisionId, Guid PatientRefID, HisCallContext Context,
    long ActorId, ActorKind ActorKind);

public sealed record EnsureHisAdmissionCommand(
    string DivisionId, Guid RecordId, HisCallContext Context,
    long ActorId, ActorKind ActorKind);

public sealed record Icd10Choice(string Code, string Label, string CodeName = null, string DisplayName = null);

public sealed record GetIcd10ChoicesQuery(
    string DivisionId, string Filter, int Amount, string Credential, string TraceId);

public sealed record DepartmentCatalogResult(
    long DepartmentId, string DepartmentCode, string DepartmentName, long ParentDepartmentId, bool IsTraditional);

public sealed record ListEmployeeDepartmentsQuery(
    string DivisionId, long EmployeeId, string Credential, string TraceId);

/// <summary>Một nhân viên giữ vai trò ký của bước — nguồn HIS GetCodeList key=EmployeeRole.</summary>
public sealed record SignRoleEmployee(long EmployeeID, string EmployeeCode, string EmployeeName);

public sealed record HisFormSectionFieldValue(Guid ItemId, string Value, string Text = null);

public sealed record HisFormSectionSaveRequest(System.Collections.Generic.IReadOnlyList<HisFormSectionFieldValue> Fields, bool IsDraft = true);

public sealed record SaveHisFormSectionCommand(
    string DivisionId, Guid RecordId, int ItemGroupId,
    HisFormSectionSaveRequest Request, string Credential, string TraceId,
    string ActorId, string ActorName, ActorKind ActorKind)
{
    public SaveHisFormSectionCommand(
        string divisionId, Guid recordId, int itemGroupId,
        HisFormSectionSaveRequest request, string credential, string traceId,
        string actorId, ActorKind actorKind)
        : this(divisionId, recordId, itemGroupId, request, credential, traceId, actorId, "", actorKind)
    {
    }
}
