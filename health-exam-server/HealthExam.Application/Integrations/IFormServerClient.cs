using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using HealthExam.Application.Webhooks;

namespace HealthExam.Application.Integrations;

public sealed record FormResolveResult(
    Guid FormID,
    string FormCode,
    string FormName,
    string VersionCode,
    int MatchCount = 1);

public sealed record FormDraftResult(
    Guid FormID,
    string HostRefType,
    string HostRefID,
    object Layout,
    object PrefillValues,
    object MissingContext);

public interface IFormServerClient
{
    Task<FormResolveResult> ResolveFormAsync(string variantCode, DateOnly effectiveOn, CancellationToken ct = default);
    Task<FormDraftResult> CreateDraftAsync(
        Guid formId,
        string hostRefType,
        string hostRefId,
        Dictionary<string, Dictionary<string, string>> contextValues,
        CancellationToken ct = default);
    Task<FormSubmissionProgressDto> GetProgressAsync(
        Guid submissionId,
        ServiceCallOrigin origin = null,
        CancellationToken ct = default);
}

public static class FormContextKeys
{
    public const string SessionProvider = "HEALTH_EXAM_SESSION";
    public const string SessionCode = "SessionCode";
    public const string OrganizationName = "OrganizationName";
    public const string ExamDate = "ExamDate";
    public const string PackageName = "PackageName";

    public const string SystemProvider = "SYSTEM";
    public const string CurrentDate = "CurrentDate";

    public const string PatientProvider = "PATIENT";
    public const string FullName = "FullName";
    public const string Dob = "Dob";

    public const string DateFormat = "yyyy-MM-dd";
}
