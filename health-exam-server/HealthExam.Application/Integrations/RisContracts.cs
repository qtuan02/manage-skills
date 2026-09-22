using System;

namespace HealthExam.Application.Integrations;

public enum RisSendOutcome
{
    Sent,
    DependencyFailure,
    Rejected
}

public sealed record RisSendResult(
    RisSendOutcome Outcome,
    string Message,
    string ResponseSnippet = null,
    object Payload = null)
{
    public bool Success => Outcome == RisSendOutcome.Sent;

    public static RisSendResult Sent(string responseSnippet = null, string message = "Sent")
        => new(RisSendOutcome.Sent, message, responseSnippet);

    public static RisSendResult DependencyFailure(string message, string responseSnippet = null, object payload = null)
        => new(RisSendOutcome.DependencyFailure, message, responseSnippet, payload);

    public static RisSendResult Rejected(string message, string responseSnippet = null, object payload = null)
        => new(RisSendOutcome.Rejected, message, responseSnippet, payload);
}

public sealed record VendorMissingFieldResult(string Field, string Reason);
