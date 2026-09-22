#nullable enable

using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace HealthExam.API.Contracts;

public sealed class HisFormDefinition
{
    public Guid TemplateId { get; init; }
    public string TemplateCode { get; init; } = "";
    public string TemplateName { get; init; } = "";
    public int FileDocTypeId { get; init; }
    public string VersionCode { get; init; } = "";
    public bool IsDraft { get; init; }
    public bool Active { get; init; }
    public JArray Details { get; init; } = new();
    public JArray Layout { get; init; } = new();
}

public sealed record ExamRecordHisForm(Guid RecordId, long? AdmissionId, HisFormDefinition Definition);
public sealed record SignerChoice(long EmployeeID, string EmployeeCode, string EmployeeName);
public sealed record ExamFormSection(
    Guid RecordId,
    long? AdmissionId,
    int ItemGroupId,
    JArray Layout,
    short RecordState = 1,
    string? CurrentStepStatus = null,
    long? CurrentStepSignedByEmployeeID = null,
    DateTime? CurrentStepSignedAt = null,
    int SigningProgressDone = 0,
    int SigningProgressTotal = 0,
    string? CurrentStepSignedByEmployeeName = null,
    long? CurrentStepPerformedByEmployeeID = null,
    string? CurrentStepPerformedByEmployeeName = null,
    System.Collections.Generic.IReadOnlyList<SignerChoice>? Signers = null);
public sealed record FormSectionFieldValue(Guid ItemId, string? Value, string? Text = null);
public sealed record FormSectionSaveRequest(System.Collections.Generic.List<FormSectionFieldValue>? Fields, bool IsDraft = true);

public sealed class Icd10ChoiceItem
{
    public string Code { get; set; } = "";
    public string Label { get; set; } = "";
    public string? CodeName { get; set; }
    public string? DisplayName { get; set; }

    public Icd10ChoiceItem() { }

    public Icd10ChoiceItem(string code, string label, string? codeName = null, string? displayName = null)
    {
        Code = code;
        Label = label;
        CodeName = codeName;
        DisplayName = displayName;
    }
}

/// <summary>
/// Body TÙY CHỌN của POST sections/{itemGroupId}/sign (PROJ-2374). Không gửi ⇒ người ký = người bấm.
/// SignedAt là DateTimeOffset (không phải DateTime) để không mơ hồ Kind khi handler ToUniversalTime().
/// </summary>
public sealed record ExamSectionSignRequest(long? ConfirmedByEmployeeID = null, DateTimeOffset? SignedAt = null);
