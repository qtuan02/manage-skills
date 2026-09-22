namespace HealthExam.Application.Common;

public enum ApplicationFailureCode
{
    BadRequest,
    Unauthorized,
    Forbidden,
    NotOwner,
    NotFound,
    InvalidState,
    FileInvalid,
    SessionClosed,
    NotInSession,
    DuplicateInSession,
    ParaclinicalResultExists,
    VendorPayloadIncomplete,
    VendorNotConfigured,
    DependencyUnavailable,
    HisTimeout,
    HisBadGateway,
    SignPrecondition,
    PatientProfileChanged
}

public sealed record ApplicationFailure(
    ApplicationFailureCode Code,
    string Message,
    object Payload = null);

public sealed record ApplicationValidationErrors(System.Collections.Generic.IReadOnlyList<ApplicationValidationError> Errors)
{
    public static ApplicationValidationErrors Of(string field, string reason, int? row = null)
        => new(new[] { new ApplicationValidationError(field, reason, row) });
}

public sealed record ApplicationValidationError(string Field, string Reason, int? Row = null);
