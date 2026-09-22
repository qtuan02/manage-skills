using System;
using System.Threading;
using System.Threading.Tasks;

namespace HealthExam.Application.Paraclinical;

public sealed record SubmissionProgressResult(
    int CompletedSections,
    int TotalSections,
    bool CanSignConclusion);

public sealed record SignSubmissionRequest(
    long SectionId,
    long ActorId,
    short ActorKind,
    string ActorName,
    object SignatoryFlows = null);

public sealed record SignSubmissionResult(
    bool Succeeded,
    string SignStatus,
    string Detail = null);

public interface IFormServerClient
{
    Task<SubmissionProgressResult> GetProgressAsync(Guid submissionId, CancellationToken ct = default);
    Task<SignSubmissionResult> SignAsync(Guid submissionId, SignSubmissionRequest request, CancellationToken ct = default);
}
