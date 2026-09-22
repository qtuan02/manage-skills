using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using HealthExam.Application.Common;

namespace HealthExam.Application.Patients;

public sealed record MatchPatientProfileQuery(
    string DivisionId,
    PatientProfileValues Profile);

public sealed record MatchPatientProfileResult(
    Guid? PatientRefID,
    bool Matched,
    IReadOnlyList<string> ChangedFields);

public interface IMatchPatientProfileHandler
{
    Task<ApplicationResult<MatchPatientProfileResult>> HandleAsync(
        MatchPatientProfileQuery query, CancellationToken ct = default);
}

public sealed class MatchPatientProfileHandler : IMatchPatientProfileHandler
{
    private readonly IPatientRepository _patientRepository;

    public MatchPatientProfileHandler(IPatientRepository patientRepository)
    {
        _patientRepository = patientRepository;
    }

    public async Task<ApplicationResult<MatchPatientProfileResult>> HandleAsync(
        MatchPatientProfileQuery query, CancellationToken ct = default)
    {
        if (query.Profile == null || string.IsNullOrWhiteSpace(query.Profile.IdentityNumber))
        {
            return ApplicationResult<MatchPatientProfileResult>.Fail(
                ApplicationFailureCode.BadRequest, "Thông tin hồ sơ người bệnh không hợp lệ.");
        }

        var patient = await _patientRepository.FindActiveByIdentityNumberAsync(
            query.DivisionId, query.Profile.IdentityNumber.Trim(), ct);
        if (patient == null)
        {
            return ApplicationResult<MatchPatientProfileResult>.Success(
                new MatchPatientProfileResult(null, false, Array.Empty<string>()));
        }

        var changedFields = PatientProfileComparer.Compare(patient, query.Profile);
        return ApplicationResult<MatchPatientProfileResult>.Success(
            new MatchPatientProfileResult(patient.PatientRefID, changedFields.Count == 0, changedFields));
    }
}
