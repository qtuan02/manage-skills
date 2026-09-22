namespace HealthExam.Domain.Common;

public enum DomainFailureCode
{
    InvalidTransition,
    RequiredValue,
    Conflict,
    InvalidState
}

public enum DomainOperationOutcome
{
    Applied,
    NoOp,
    Rejected
}

public sealed record DomainFailure(DomainFailureCode Code, string Message);

public readonly record struct DomainResult(
    DomainOperationOutcome Outcome,
    DomainFailure Failure = null)
{
    public bool IsSuccess => Outcome is not DomainOperationOutcome.Rejected;
    public bool Applied => Outcome is DomainOperationOutcome.Applied;

    public static DomainResult Apply() => new(DomainOperationOutcome.Applied);
    public static DomainResult NoOp() => new(DomainOperationOutcome.NoOp);
    public static DomainResult Reject(DomainFailureCode code, string message)
        => new(DomainOperationOutcome.Rejected, new DomainFailure(code, message));
}
