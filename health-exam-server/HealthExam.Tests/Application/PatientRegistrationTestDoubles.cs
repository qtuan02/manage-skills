using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HealthExam.Application.Common;
using HealthExam.Application.Integrations;
using HealthExam.Application.Patients;
using HealthExam.Domain.Common;
using HealthExam.Domain.Patients;

namespace HealthExam.Tests.Application;

public class FixedClock : IClock
{
    public DateTime UtcNow { get; set; } = new(2026, 9, 12, 12, 0, 0, DateTimeKind.Utc);
    public FixedClock() { }
    public FixedClock(DateTime utcNow) => UtcNow = utcNow;
}

public class FakeHisCredentialOptions : IHisCredentialOptions
{
    public string CredentialHeaderName { get; set; } = "Authorization";
    public int? KskDepartmentId { get; set; } = 10;
    public string KskDepartmentCode { get; set; } = "K01";
    public string PatientCreateRoute { get; set; } = "api/M02F00000/CUCommonPatient";
}

public class FakePatientRepository : IPatientRepository
{
    public Action BeforeAllocateVersion { get; set; }
    public List<Patient> Patients { get; } = new();
    public List<PatientInsurance> Insurances { get; } = new();
    public List<PatientEmployment> Employments { get; } = new();
    public List<PatientRelative> Relatives { get; } = new();

    public FakePatientRepository(params Patient[] initial)
    {
        if (initial != null) Patients.AddRange(initial);
    }

    public Task<Patient> FindForWriteAsync(string divisionId, Guid? patientRefID, long? hisPatientID, CancellationToken ct = default)
    {
        Patient patient = null;
        if (patientRefID.HasValue && patientRefID.Value != Guid.Empty)
        {
            patient = Patients.FirstOrDefault(p => p.DivisionID == divisionId && p.PatientRefID == patientRefID.Value && p.IsActive);
        }
        if (patient == null && hisPatientID.HasValue && hisPatientID.Value > 0)
        {
            patient = Patients.FirstOrDefault(p => p.DivisionID == divisionId && p.HisPatientID == hisPatientID.Value && p.IsActive);
        }
        return Task.FromResult(patient);
    }

    public Task<Patient> FindActiveByIdentityNumberAsync(string divisionId, string identityNumber, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(identityNumber)) return Task.FromResult<Patient>(null);
        var norm = identityNumber.Trim();
        var patient = Patients.FirstOrDefault(p => p.DivisionID == divisionId && p.IdentityNumber == norm && p.IsActive);
        return Task.FromResult(patient);
    }

    public Task<Patient> FindActiveByRefIdAsync(string divisionId, Guid patientRefID, CancellationToken ct = default)
    {
        var patient = Patients.FirstOrDefault(p => p.DivisionID == divisionId && p.PatientRefID == patientRefID && p.IsActive);
        return Task.FromResult(patient);
    }

    public Task<PatientProfileSnapshot> FindActiveProfileAsync(string divisionId, Guid patientRefID, CancellationToken ct = default)
    {
        var patient = Patients.FirstOrDefault(p => p.DivisionID == divisionId && p.PatientRefID == patientRefID && p.IsActive);
        if (patient == null) return Task.FromResult<PatientProfileSnapshot>(null);

        var insurance = Insurances.Where(i => i.DivisionID == divisionId && i.PatientRefID == patientRefID && i.IsActive)
            .OrderByDescending(i => i.CreatedDate).FirstOrDefault();
        var employment = Employments.Where(e => e.DivisionID == divisionId && e.PatientRefID == patientRefID && e.IsActive)
            .OrderByDescending(e => e.CreatedDate).FirstOrDefault();
        var relative = Relatives.Where(r => r.DivisionID == divisionId && r.PatientRefID == patientRefID && r.IsActive)
            .OrderByDescending(r => r.CreatedDate).FirstOrDefault();

        return Task.FromResult(new PatientProfileSnapshot(patient, insurance, employment, relative));
    }

    public Task<Guid?> FindProfileLineageIdAsync(string divisionId, Guid patientRefID, CancellationToken ct = default)
    {
        if (patientRefID == Guid.Empty) return Task.FromResult<Guid?>(null);

        var patient = Patients.FirstOrDefault(p => p.DivisionID == divisionId && p.PatientRefID == patientRefID);
        return Task.FromResult(patient == null ? (Guid?)null : patient.ProfileLineageID);
    }

    public Task<IReadOnlyList<Patient>> SearchActiveAsync(string divisionId, PatientSearchCriteria criteria, int limit, CancellationToken ct = default)
    {
        var query = Patients.Where(p => p.DivisionID == divisionId && p.IsActive);

        if (criteria != null && criteria.HasKeyword)
        {
            var kw = criteria.Keyword.Trim();
            var kwLower = kw.ToLowerInvariant();
            bool byIdentity = criteria.Fields.Contains(PatientSearchField.Identity);
            bool byPhone = criteria.Fields.Contains(PatientSearchField.Phone);
            bool byCode = criteria.Fields.Contains(PatientSearchField.Code);
            bool byName = criteria.Fields.Contains(PatientSearchField.Name);

            query = query.Where(p =>
                (byIdentity && (p.IdentityNumber ?? "").Contains(kw, StringComparison.Ordinal)) ||
                (byPhone && (p.PhoneNumber ?? "").Contains(kw, StringComparison.Ordinal)) ||
                (byCode && (p.PatientCode ?? "").ToLowerInvariant().Contains(kwLower, StringComparison.Ordinal)) ||
                (byName && (p.FullName ?? "").ToLowerInvariant().Contains(kwLower, StringComparison.Ordinal)));
        }

        IReadOnlyList<Patient> result = query
            .OrderByDescending(p => p.ModifiedDate)
            .ThenBy(p => p.PatientRefID)
            .Take(limit)
            .ToList();
        return Task.FromResult(result);
    }

    public Task<int?> AllocateNextVersionAsync(
        string divisionId, Guid profileLineageID, Guid sourcePatientRefID,
        CancellationToken ct = default)
    {
        BeforeAllocateVersion?.Invoke();
        if (!Patients.Any(p => p.DivisionID == divisionId &&
                               p.PatientRefID == sourcePatientRefID && p.IsActive))
            return Task.FromResult<int?>(null);

        var next = Patients
            .Where(p => p.DivisionID == divisionId && p.ProfileLineageID == profileLineageID)
            .Select(p => p.VersionNumber)
            .DefaultIfEmpty(0)
            .Max() + 1;
        return Task.FromResult<int?>(next);
    }

    public Task<bool> DeactivateAsync(
        string divisionId, Guid patientRefID, DateTime modifiedDate,
        long actorId, ActorKind actorKind, CancellationToken ct = default)
    {
        var patient = Patients.FirstOrDefault(p => p.DivisionID == divisionId && p.PatientRefID == patientRefID && p.IsActive);
        if (patient == null) return Task.FromResult(false);

        patient.IsActive = false;
        patient.ModifiedDate = modifiedDate;
        patient.ModifiedBy = actorId;
        patient.ModifiedActorKind = actorKind;
        return Task.FromResult(true);
    }

    public Task<PatientInsurance> FindActiveInsuranceAsync(string divisionId, Guid patientRefID, PatientInsuranceWrite value, CancellationToken ct = default)
    {
        if (value == null || !value.HasValue) return Task.FromResult<PatientInsurance>(null);
        var normNumber = (value.InsuranceNumber ?? "").Trim();
        var match = Insurances.FirstOrDefault(x =>
            x.DivisionID == divisionId &&
            x.PatientRefID == patientRefID &&
            x.IsActive &&
            x.InsuranceNumber == normNumber &&
            x.InsuranceObjectOptionID == value.InsuranceObjectOptionID &&
            x.RegistrationPlaceOptionID == value.RegistrationPlaceOptionID &&
            x.ValidFrom == value.ValidFrom &&
            x.ValidTo == value.ValidTo);
        return Task.FromResult(match);
    }

    public Task<PatientEmployment> FindActiveEmploymentAsync(string divisionId, Guid patientRefID, PatientEmploymentWrite value, CancellationToken ct = default)
    {
        if (value == null || !value.HasValue) return Task.FromResult<PatientEmployment>(null);
        var normStaffCode = (value.StaffCode ?? "").Trim();
        var normOrgDeptName = (value.OrgDeptName ?? "").Trim();
        var normJobTitle = (value.JobTitle ?? "").Trim();
        var match = Employments.FirstOrDefault(x =>
            x.DivisionID == divisionId &&
            x.PatientRefID == patientRefID &&
            x.IsActive &&
            x.OccupationOptionID == value.OccupationOptionID &&
            x.StaffCode == normStaffCode &&
            x.OrgDeptName == normOrgDeptName &&
            x.JobTitle == normJobTitle);
        return Task.FromResult(match);
    }

    public Task<PatientRelative> FindActiveRelativeAsync(string divisionId, Guid patientRefID, PatientRelativeWrite value, CancellationToken ct = default)
    {
        if (value == null || !value.HasValue) return Task.FromResult<PatientRelative>(null);
        var normRelationshipCode = (value.RelationshipCode ?? "").Trim();
        var normFullName = (value.FullName ?? "").Trim();
        var normIdentityNumber = (value.IdentityNumber ?? "").Trim();
        var normPhoneNumber = (value.PhoneNumber ?? "").Trim();
        var match = Relatives.FirstOrDefault(x =>
            x.DivisionID == divisionId &&
            x.PatientRefID == patientRefID &&
            x.IsActive &&
            x.RelationshipCode == normRelationshipCode &&
            x.RelationshipOptionID == value.RelationshipOptionID &&
            x.FullName == normFullName &&
            x.IdentityNumber == normIdentityNumber &&
            x.PhoneNumber == normPhoneNumber);
        return Task.FromResult(match);
    }

    public void Add(Patient entity) => Patients.Add(entity);
    public void Add(PatientInsurance entity) => Insurances.Add(entity);
    public void Add(PatientEmployment entity) => Employments.Add(entity);
    public void Add(PatientRelative entity) => Relatives.Add(entity);
}
