using System;
using System.Threading;
using System.Threading.Tasks;
using HealthExam.Application.Common;
using HealthExam.Domain.Patients;

namespace HealthExam.Application.Patients;

public class PatientRegistrationWriter
{
    private readonly IPatientRepository _patientRepository;
    private readonly IUnitOfWork _uow;
    private readonly IClock _clock;

    public PatientRegistrationWriter(
        IPatientRepository patientRepository,
        IUnitOfWork uow,
        IClock clock)
    {
        _patientRepository = patientRepository;
        _uow = uow;
        _clock = clock;
    }

    public async Task<ApplicationResult<PatientWriteResult>> UpsertAsync(
        PatientWriteRequest request, CancellationToken ct = default)
    {
        Patient source = null;

        if (request.PatientRefID.HasValue && request.PatientRefID.Value != Guid.Empty)
        {
            source = await _patientRepository.FindForWriteAsync(
                request.DivisionId, request.PatientRefID.Value, null, ct);

            if (source == null)
            {
                return ApplicationResult<PatientWriteResult>.Fail(
                    ApplicationFailureCode.InvalidState,
                    "Hồ sơ người bệnh không tồn tại hoặc không còn hiệu lực.");
            }
        }

        if (source == null && request.HisPatientID.HasValue && request.HisPatientID.Value > 0)
        {
            source = await _patientRepository.FindForWriteAsync(
                request.DivisionId, null, request.HisPatientID.Value, ct);
        }

        var now = _clock.UtcNow;

        if (source == null)
        {
            var hasPositiveHisId = request.HisPatientID.HasValue && request.HisPatientID.Value > 0;
            var patientRefID = request.PatientRefID.HasValue && request.PatientRefID.Value != Guid.Empty
                ? request.PatientRefID.Value
                : Guid.NewGuid();
            var newPatient = new Patient
            {
                PatientRefID = patientRefID,
                DivisionID = request.DivisionId,
                HisPatientID = hasPositiveHisId ? request.HisPatientID : null,
                PatientCode = (request.PatientCode ?? "").Trim(),
                FullName = (request.FullName ?? "").Trim(),
                Dob = request.Dob,
                BirthYear = request.BirthYear,
                GenderID = request.GenderID,
                IdentityNumber = (request.IdentityNumber ?? "").Trim(),
                IdentityIssuedDate = request.IdentityIssuedDate,
                IdentityIssuerOptionID = request.IdentityIssuerOptionID,
                PhoneNumber = (request.PhoneNumber ?? "").Trim(),
                Email = (request.Email ?? "").Trim(),
                Address = (request.Address ?? "").Trim(),
                ProvinceCode = (request.ProvinceCode ?? "").Trim(),
                WardCode = (request.WardCode ?? "").Trim(),
                EthnicityOptionID = request.EthnicityOptionID,
                BloodAboCode = (request.BloodAboCode ?? "").Trim(),
                BloodRhCode = (request.BloodRhCode ?? "").Trim(),
                HisSyncStatus = hasPositiveHisId ? "Linked" : "Pending",
                HisSyncError = "",
                IsActive = true,
                ProfileLineageID = patientRefID,
                VersionNumber = 1,
                PreviousPatientRefID = null,
                CreatedDate = now,
                CreatedBy = request.ActorId,
                CreatedActorKind = request.ActorKind,
                ModifiedDate = now,
                ModifiedBy = request.ActorId,
                ModifiedActorKind = request.ActorKind
            };
            _patientRepository.Add(newPatient);

            var writeResult = await AttachChildFactsAsync(newPatient, request, now, ct);
            return ApplicationResult<PatientWriteResult>.Success(writeResult);
        }

        var changedFields = PatientProfileComparer.Compare(source, PatientProfileValues.From(request));
        if (changedFields.Count == 0)
        {
            var writeResult = await AttachChildFactsAsync(source, request, now, ct);
            return ApplicationResult<PatientWriteResult>.Success(writeResult);
        }

        if (request.SetAsActiveProfile is null)
        {
            return ApplicationResult<PatientWriteResult>.Fail(
                ApplicationFailureCode.PatientProfileChanged,
                "Thông tin người bệnh đã thay đổi",
                new PatientProfileChangedPayload(changedFields));
        }

        var profileLineageID = source.ProfileLineageID == Guid.Empty
            ? source.PatientRefID
            : source.ProfileLineageID;
        var versionNumber = await _patientRepository.AllocateNextVersionAsync(
            request.DivisionId, profileLineageID, source.PatientRefID, ct);
        if (!versionNumber.HasValue)
        {
            return ApplicationResult<PatientWriteResult>.Fail(
                ApplicationFailureCode.InvalidState,
                "Hồ sơ người bệnh đã được cập nhật, vui lòng tìm lại.");
        }

        if (request.SetAsActiveProfile == true)
        {
            var deactivated = await _patientRepository.DeactivateAsync(
                request.DivisionId, source.PatientRefID, now, request.ActorId, request.ActorKind, ct);
            if (!deactivated)
            {
                return ApplicationResult<PatientWriteResult>.Fail(
                    ApplicationFailureCode.InvalidState,
                    "Hồ sơ người bệnh đã được cập nhật, vui lòng tìm lại.");
            }
        }

        var version = new Patient
        {
            PatientRefID = Guid.NewGuid(),
            DivisionID = request.DivisionId,
            HisPatientID = source.HisPatientID,
            PatientCode = source.PatientCode,
            FullName = (request.FullName ?? "").Trim(),
            Dob = request.Dob,
            BirthYear = request.BirthYear,
            GenderID = request.GenderID,
            IdentityNumber = (request.IdentityNumber ?? "").Trim(),
            IdentityIssuedDate = request.IdentityIssuedDate,
            IdentityIssuerOptionID = request.IdentityIssuerOptionID,
            PhoneNumber = (request.PhoneNumber ?? "").Trim(),
            Email = (request.Email ?? "").Trim(),
            Address = (request.Address ?? "").Trim(),
            ProvinceCode = (request.ProvinceCode ?? "").Trim(),
            WardCode = (request.WardCode ?? "").Trim(),
            EthnicityOptionID = request.EthnicityOptionID,
            BloodAboCode = (request.BloodAboCode ?? "").Trim(),
            BloodRhCode = (request.BloodRhCode ?? "").Trim(),
            HisSyncStatus = source.HisSyncStatus,
            HisSyncError = source.HisSyncError,
            IsActive = request.SetAsActiveProfile == true,
            ProfileLineageID = profileLineageID,
            VersionNumber = versionNumber.Value,
            PreviousPatientRefID = source.PatientRefID,
            CreatedDate = now,
            CreatedBy = request.ActorId,
            CreatedActorKind = request.ActorKind,
            ModifiedDate = now,
            ModifiedBy = request.ActorId,
            ModifiedActorKind = request.ActorKind
        };
        _patientRepository.Add(version);

        var versionResult = await AttachChildFactsAsync(version, request, now, ct);
        return ApplicationResult<PatientWriteResult>.Success(versionResult);
    }

    private async Task<PatientWriteResult> AttachChildFactsAsync(
        Patient patient, PatientWriteRequest request, DateTime now, CancellationToken ct)
    {
        PatientInsurance insurance = null;
        if (request.Insurance != null && request.Insurance.HasValue)
        {
            insurance = await _patientRepository.FindActiveInsuranceAsync(
                request.DivisionId, patient.PatientRefID, request.Insurance, ct);

            if (insurance == null)
            {
                insurance = new PatientInsurance
                {
                    InsuranceRefID = Guid.NewGuid(),
                    DivisionID = request.DivisionId,
                    PatientRefID = patient.PatientRefID,
                    InsuranceNumber = (request.Insurance.InsuranceNumber ?? "").Trim(),
                    InsuranceObjectOptionID = request.Insurance.InsuranceObjectOptionID,
                    RegistrationPlaceOptionID = request.Insurance.RegistrationPlaceOptionID,
                    ValidFrom = request.Insurance.ValidFrom,
                    ValidTo = request.Insurance.ValidTo,
                    IsActive = true,
                    CreatedDate = now,
                    CreatedBy = request.ActorId,
                    CreatedActorKind = request.ActorKind,
                    ModifiedDate = now,
                    ModifiedBy = request.ActorId,
                    ModifiedActorKind = request.ActorKind
                };
                _patientRepository.Add(insurance);
            }
        }

        PatientEmployment employment = null;
        if (request.Employment != null && request.Employment.HasValue)
        {
            employment = await _patientRepository.FindActiveEmploymentAsync(
                request.DivisionId, patient.PatientRefID, request.Employment, ct);

            if (employment == null)
            {
                employment = new PatientEmployment
                {
                    EmploymentRefID = Guid.NewGuid(),
                    DivisionID = request.DivisionId,
                    PatientRefID = patient.PatientRefID,
                    OccupationOptionID = request.Employment.OccupationOptionID,
                    StaffCode = (request.Employment.StaffCode ?? "").Trim(),
                    OrgDeptName = (request.Employment.OrgDeptName ?? "").Trim(),
                    JobTitle = (request.Employment.JobTitle ?? "").Trim(),
                    IsActive = true,
                    CreatedDate = now,
                    CreatedBy = request.ActorId,
                    CreatedActorKind = request.ActorKind,
                    ModifiedDate = now,
                    ModifiedBy = request.ActorId,
                    ModifiedActorKind = request.ActorKind
                };
                _patientRepository.Add(employment);
            }
        }

        PatientRelative relative = null;
        if (request.Relative != null && request.Relative.HasValue)
        {
            relative = await _patientRepository.FindActiveRelativeAsync(
                request.DivisionId, patient.PatientRefID, request.Relative, ct);

            if (relative == null)
            {
                relative = new PatientRelative
                {
                    RelativeRefID = Guid.NewGuid(),
                    DivisionID = request.DivisionId,
                    PatientRefID = patient.PatientRefID,
                    RelationshipCode = (request.Relative.RelationshipCode ?? "").Trim(),
                    RelationshipOptionID = request.Relative.RelationshipOptionID,
                    FullName = (request.Relative.FullName ?? "").Trim(),
                    IdentityNumber = (request.Relative.IdentityNumber ?? "").Trim(),
                    PhoneNumber = (request.Relative.PhoneNumber ?? "").Trim(),
                    IsActive = true,
                    CreatedDate = now,
                    CreatedBy = request.ActorId,
                    CreatedActorKind = request.ActorKind,
                    ModifiedDate = now,
                    ModifiedBy = request.ActorId,
                    ModifiedActorKind = request.ActorKind
                };
                _patientRepository.Add(relative);
            }
        }

        return new PatientWriteResult(
            patient.PatientRefID,
            patient.HisPatientID,
            patient.PatientCode,
            insurance?.InsuranceRefID,
            employment?.EmploymentRefID,
            relative?.RelativeRefID,
            patient,
            insurance,
            employment,
            relative);
    }
}
