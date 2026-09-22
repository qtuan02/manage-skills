using System.Collections.Generic;
using System.Linq;
using HealthExam.Domain.ExamForms;
using HealthExam.Domain.ExamRecords;

namespace HealthExam.Application.Signing;

public readonly record struct SigningProgress(int Done, int Total);

public static class SigningProgressCalculator
{
    public static SigningProgress Calculate(
        IReadOnlyList<SignStepMap> mapSteps,
        IReadOnlyCollection<ExamRecordSignStep> snapshots)
    {
        if (mapSteps == null || mapSteps.Count == 0)
            return new SigningProgress(0, 0);

        var clinicalSteps = mapSteps.Where(s => s.IsActive && !s.IsConclusionStep).ToList();
        var total = clinicalSteps.Count;

        if (total == 0 || snapshots == null || snapshots.Count == 0)
            return new SigningProgress(0, total);

        var signedSteps = snapshots
            .Where(s => s.Status == ExamRecordSignStepStatus.Signed)
            .Select(s => s.SWStep)
            .ToHashSet();

        var done = clinicalSteps.Count(s => signedSteps.Contains(s.SWStep));
        return new SigningProgress(done, total);
    }
}
