using System;
using System.Collections.Generic;
using HealthExam.Domain.Common;

namespace HealthExam.Application.RegistrationForms;

public sealed class RegistrationFormDefinition
{
    public string VariantCode { get; init; } = "";
    public string TemplateCode { get; init; } = "";
    public string TemplateName { get; init; } = "";
    public IReadOnlyList<RegistrationFormSection> Sections { get; init; } = Array.Empty<RegistrationFormSection>();
}

public sealed class RegistrationFormSection
{
    public string Kind { get; init; } = "";
    public int ItemGroupID { get; init; }
    public string Title { get; init; } = "";
    public IReadOnlyList<RegistrationFormNode> Nodes { get; init; } = Array.Empty<RegistrationFormNode>();
}

public sealed class RegistrationFormNode
{
    public string NodeID { get; init; } = "";
    public string ParentNodeID { get; init; }
    public Guid? ItemID { get; init; }
    public int ItemGroupID { get; init; }
    public int Order { get; init; }
    public int Level { get; init; }
    public string Label { get; init; } = "";
    public string ControlType { get; init; } = "";
    public string DataType { get; init; } = "";
    public bool Required { get; init; }
    public bool ReadOnly { get; init; }
    public string Value { get; init; } = "";
    public string Text { get; init; }
    public IReadOnlyList<RegistrationFormChoice> Choices { get; init; }
}

public sealed class RegistrationFormChoice
{
    public string Code { get; init; } = "";
    public string Label { get; init; } = "";
}

public sealed class AvailableExamGroupItem
{
    public string VariantCode { get; init; } = "";
    public string Name { get; init; } = "";
    public int OrderNo { get; init; }
}

public sealed class RegistrationRecordForm
{
    public Guid RecordID { get; init; }
    public long? AdmissionID { get; init; }
    public Guid? HisEmrDataID { get; init; }
    public Guid? HisFormTemplateID { get; init; }
    public string HisFormSyncStatus { get; init; } = "Pending";
    public string HisFormSyncError { get; init; } = "";
    public string VariantCode { get; init; } = "";
    public string TemplateCode { get; init; } = "";
    public string TemplateName { get; init; } = "";
    public IReadOnlyList<RegistrationFormSection> Sections { get; init; } = Array.Empty<RegistrationFormSection>();
}

public sealed class RegistrationSectionSaveRequest
{
    public RegistrationSectionSaveRequest() { }
    public RegistrationSectionSaveRequest(IReadOnlyList<RegistrationFormFieldValue> fields, bool isDraft = true)
    {
        Fields = fields ?? Array.Empty<RegistrationFormFieldValue>();
        IsDraft = isDraft;
    }
    public IReadOnlyList<RegistrationFormFieldValue> Fields { get; init; } = Array.Empty<RegistrationFormFieldValue>();
    public bool IsDraft { get; init; } = true;
}

public sealed class RegistrationFormFieldValue
{
    public RegistrationFormFieldValue() { }
    public RegistrationFormFieldValue(Guid itemId, string value, string text = null)
    {
        ItemId = itemId;
        Value = value ?? "";
        Text = text;
    }
    public Guid ItemId { get; init; }
    public string Value { get; init; } = "";
    public string Text { get; init; }
}

public sealed record GetExamGroupRegistrationFormsQuery(
    string DivisionId, string VariantCode, string Credential = null, string TraceId = null);

public sealed record ListAvailableExamGroupsQuery(
    string DivisionId);

public sealed record GetRegistrationFormQuery(
    string DivisionId, Guid RecordId, string Credential = null, string TraceId = null);

public sealed record SaveRegistrationFormSectionCommand(
    string DivisionId, Guid RecordId, string SectionKind, RegistrationSectionSaveRequest Request,
    string ActorId, ActorKind ActorKind, string Credential = null, string TraceId = null);

public sealed record PreviewRegistrationFormPdfQuery(
    string DivisionId,
    Guid RecordId,
    string Credential = null,
    string TraceId = null,
    string ActorId = null,
    ActorKind ActorKind = ActorKind.Employee);
