#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using HealthExam.Domain.Patients;
using HealthExam.Infrastructure.Persistence.Legacy;
using IHealthExamContext = HealthExam.Application.Common.IHealthExamContext;
using Microsoft.EntityFrameworkCore;

namespace HealthExam.Server.Service;

public class PatientInsuranceData
{
    public string InsuranceNumber { get; set; } = "";
    public Guid? InsuranceObjectOptionID { get; set; }
    public Guid? RegistrationPlaceOptionID { get; set; }
    public DateOnly? ValidFrom { get; set; }
    public DateOnly? ValidTo { get; set; }

    public bool HasValue => !string.IsNullOrWhiteSpace(InsuranceNumber) ||
                            InsuranceObjectOptionID.HasValue ||
                            RegistrationPlaceOptionID.HasValue ||
                            ValidFrom.HasValue ||
                            ValidTo.HasValue;
}

public class PatientEmploymentData
{
    public Guid? OccupationOptionID { get; set; }
    public string StaffCode { get; set; } = "";
    public string OrgDeptName { get; set; } = "";
    public string JobTitle { get; set; } = "";

    public bool HasValue => OccupationOptionID.HasValue ||
                            !string.IsNullOrWhiteSpace(StaffCode) ||
                            !string.IsNullOrWhiteSpace(OrgDeptName) ||
                            !string.IsNullOrWhiteSpace(JobTitle);
}

public class PatientRelativeData
{
    public string RelationshipCode { get; set; } = "";
    public Guid? RelationshipOptionID { get; set; }
    public string FullName { get; set; } = "";
    public string IdentityNumber { get; set; } = "";
    public string PhoneNumber { get; set; } = "";

    public bool HasValue => !string.IsNullOrWhiteSpace(RelationshipCode) ||
                            RelationshipOptionID.HasValue ||
                            !string.IsNullOrWhiteSpace(FullName) ||
                            !string.IsNullOrWhiteSpace(IdentityNumber) ||
                            !string.IsNullOrWhiteSpace(PhoneNumber);
}

public class PatientWriteData
{
    public Guid? PatientRefID { get; set; }
    public long? HisPatientID { get; set; }
    public string PatientCode { get; set; } = "";
    public string FullName { get; set; } = "";
    public DateOnly? Dob { get; set; }
    public short? BirthYear { get; set; }
    public short GenderID { get; set; }
    public string IdentityNumber { get; set; } = "";
    public DateOnly? IdentityIssuedDate { get; set; }
    public Guid? IdentityIssuerOptionID { get; set; }
    public string PhoneNumber { get; set; } = "";
    public string Email { get; set; } = "";
    public string Address { get; set; } = "";
    public Guid? EthnicityOptionID { get; set; }
    public string BloodAboCode { get; set; } = "";
    public string BloodRhCode { get; set; } = "";

    public PatientInsuranceData? Insurance { get; set; }
    public PatientEmploymentData? Employment { get; set; }
    public PatientRelativeData? Relative { get; set; }
}

public class PatientWriteResult
{
    public Guid PatientRefID { get; set; }
    public long? HisPatientID { get; set; }
    public string PatientCode { get; set; } = "";
    public Guid? InsuranceRefID { get; set; }
    public Guid? EmploymentRefID { get; set; }
    public Guid? RelativeRefID { get; set; }
    public Patient Patient { get; set; } = null!;
    public PatientInsurance? Insurance { get; set; }
    public PatientEmployment? Employment { get; set; }
    public PatientRelative? Relative { get; set; }
}

public interface IPatientService
{
    Task<PatientWriteResult> UpsertLocalAsync(PatientWriteData data, CancellationToken ct = default);
    Task<Guid?> GetOrCreateInsuranceAsync(Guid patientRefId, PatientInsuranceData data, CancellationToken ct = default);
    Task<Guid?> GetOrCreateEmploymentAsync(Guid patientRefId, PatientEmploymentData data, CancellationToken ct = default);
    Task<Guid?> GetOrCreateRelativeAsync(Guid patientRefId, PatientRelativeData data, CancellationToken ct = default);
}

public class PatientService : IPatientService
{
    private readonly IUnitOfWork _uow;
    private readonly IHealthExamContext _ctx;

    public PatientService(IUnitOfWork uow, IHealthExamContext ctx)
    {
        _uow = uow;
        _ctx = ctx;
    }

    public async Task<PatientWriteResult> UpsertLocalAsync(PatientWriteData data, CancellationToken ct = default)
    {
        Patient? patient = null;

        if (data.PatientRefID.HasValue && data.PatientRefID.Value != Guid.Empty)
        {
            patient = await _uow.Patients.Query().AsTracking()
                .FirstOrDefaultAsync(p => p.DivisionID == _ctx.DivisionId && p.PatientRefID == data.PatientRefID.Value, ct);
        }

        // Chỉ query reuse theo (DivisionID, HisPatientID) khi HisPatientID có giá trị dương
        if (patient == null && data.HisPatientID.HasValue && data.HisPatientID.Value > 0)
        {
            patient = await _uow.Patients.Query().AsTracking()
                .FirstOrDefaultAsync(p => p.DivisionID == _ctx.DivisionId && p.HisPatientID == data.HisPatientID.Value, ct);
        }

        var now = DateTime.UtcNow;

        if (patient != null)
        {
            if (!string.IsNullOrWhiteSpace(data.FullName)) patient.FullName = data.FullName.Trim();
            if (data.Dob.HasValue) patient.Dob = data.Dob;
            if (data.BirthYear.HasValue) patient.BirthYear = data.BirthYear;
            if (data.GenderID != 0) patient.GenderID = data.GenderID;
            if (data.IdentityNumber != null) patient.IdentityNumber = data.IdentityNumber.Trim();
            if (data.IdentityIssuedDate.HasValue) patient.IdentityIssuedDate = data.IdentityIssuedDate;
            patient.IdentityIssuerOptionID = data.IdentityIssuerOptionID;
            if (data.PhoneNumber != null) patient.PhoneNumber = data.PhoneNumber.Trim();
            if (data.Email != null) patient.Email = data.Email.Trim();
            if (data.Address != null) patient.Address = data.Address.Trim();
            patient.EthnicityOptionID = data.EthnicityOptionID;
            if (data.BloodAboCode != null) patient.BloodAboCode = data.BloodAboCode.Trim();
            if (data.BloodRhCode != null) patient.BloodRhCode = data.BloodRhCode.Trim();
            if (!string.IsNullOrWhiteSpace(data.PatientCode)) patient.PatientCode = data.PatientCode.Trim();

            if (data.HisPatientID.HasValue && data.HisPatientID.Value > 0)
            {
                patient.HisPatientID = data.HisPatientID.Value;
                patient.HisSyncStatus = "Linked";
            }

            patient.ModifiedDate = now;
            patient.ModifiedBy = _ctx.ActorId;
            patient.ModifiedActorKind = _ctx.ActorKind;
        }
        else
        {
            var hasPositiveHisId = data.HisPatientID.HasValue && data.HisPatientID.Value > 0;
            patient = new Patient
            {
                PatientRefID = Guid.NewGuid(),
                DivisionID = _ctx.DivisionId,
                HisPatientID = hasPositiveHisId ? data.HisPatientID : null,
                PatientCode = (data.PatientCode ?? "").Trim(),
                FullName = (data.FullName ?? "").Trim(),
                Dob = data.Dob,
                BirthYear = data.BirthYear,
                GenderID = data.GenderID,
                IdentityNumber = (data.IdentityNumber ?? "").Trim(),
                IdentityIssuedDate = data.IdentityIssuedDate,
                IdentityIssuerOptionID = data.IdentityIssuerOptionID,
                PhoneNumber = (data.PhoneNumber ?? "").Trim(),
                Email = (data.Email ?? "").Trim(),
                Address = (data.Address ?? "").Trim(),
                EthnicityOptionID = data.EthnicityOptionID,
                BloodAboCode = (data.BloodAboCode ?? "").Trim(),
                BloodRhCode = (data.BloodRhCode ?? "").Trim(),
                HisSyncStatus = hasPositiveHisId ? "Linked" : "Pending",
                HisSyncError = "",
                CreatedDate = now,
                CreatedBy = _ctx.ActorId,
                CreatedActorKind = _ctx.ActorKind,
                ModifiedDate = now,
                ModifiedBy = _ctx.ActorId,
                ModifiedActorKind = _ctx.ActorKind
            };
            _uow.Patients.Add(patient);
        }

        PatientInsurance? insurance = null;
        if (data.Insurance != null && data.Insurance.HasValue)
        {
            insurance = await GetOrCreateInsuranceEntityAsync(patient.PatientRefID, data.Insurance, ct);
        }

        PatientEmployment? employment = null;
        if (data.Employment != null && data.Employment.HasValue)
        {
            employment = await GetOrCreateEmploymentEntityAsync(patient.PatientRefID, data.Employment, ct);
        }

        PatientRelative? relative = null;
        if (data.Relative != null && data.Relative.HasValue)
        {
            relative = await GetOrCreateRelativeEntityAsync(patient.PatientRefID, data.Relative, ct);
        }

        await _uow.SaveChangesAsync(ct);

        return new PatientWriteResult
        {
            PatientRefID = patient.PatientRefID,
            HisPatientID = patient.HisPatientID,
            PatientCode = patient.PatientCode,
            InsuranceRefID = insurance?.InsuranceRefID,
            EmploymentRefID = employment?.EmploymentRefID,
            RelativeRefID = relative?.RelativeRefID,
            Patient = patient,
            Insurance = insurance,
            Employment = employment,
            Relative = relative
        };
    }

    public async Task<PatientInsurance?> GetOrCreateInsuranceEntityAsync(Guid patientRefId, PatientInsuranceData data, CancellationToken ct = default)
    {
        if (!data.HasValue) return null;

        var normNumber = (data.InsuranceNumber ?? "").Trim();

        var existing = await _uow.PatientInsurances.Query().AsTracking()
            .FirstOrDefaultAsync(x => x.DivisionID == _ctx.DivisionId &&
                                      x.PatientRefID == patientRefId &&
                                      x.IsActive &&
                                      x.InsuranceNumber == normNumber &&
                                      x.InsuranceObjectOptionID == data.InsuranceObjectOptionID &&
                                      x.RegistrationPlaceOptionID == data.RegistrationPlaceOptionID &&
                                      x.ValidFrom == data.ValidFrom &&
                                      x.ValidTo == data.ValidTo, ct);

        if (existing != null) return existing;

        var now = DateTime.UtcNow;
        var row = new PatientInsurance
        {
            InsuranceRefID = Guid.NewGuid(),
            DivisionID = _ctx.DivisionId,
            PatientRefID = patientRefId,
            InsuranceNumber = normNumber,
            InsuranceObjectOptionID = data.InsuranceObjectOptionID,
            RegistrationPlaceOptionID = data.RegistrationPlaceOptionID,
            ValidFrom = data.ValidFrom,
            ValidTo = data.ValidTo,
            IsActive = true,
            CreatedDate = now,
            CreatedBy = _ctx.ActorId,
            CreatedActorKind = _ctx.ActorKind,
            ModifiedDate = now,
            ModifiedBy = _ctx.ActorId,
            ModifiedActorKind = _ctx.ActorKind
        };
        _uow.PatientInsurances.Add(row);
        return row;
    }

    public async Task<Guid?> GetOrCreateInsuranceAsync(Guid patientRefId, PatientInsuranceData data, CancellationToken ct = default)
    {
        var row = await GetOrCreateInsuranceEntityAsync(patientRefId, data, ct);
        if (row != null && _uow.Context.Entry(row).State == EntityState.Added)
        {
            await _uow.SaveChangesAsync(ct);
        }
        return row?.InsuranceRefID;
    }

    public async Task<PatientEmployment?> GetOrCreateEmploymentEntityAsync(Guid patientRefId, PatientEmploymentData data, CancellationToken ct = default)
    {
        if (!data.HasValue) return null;

        var normStaffCode = (data.StaffCode ?? "").Trim();
        var normOrgDeptName = (data.OrgDeptName ?? "").Trim();
        var normJobTitle = (data.JobTitle ?? "").Trim();

        var existing = await _uow.PatientEmployments.Query().AsTracking()
            .FirstOrDefaultAsync(x => x.DivisionID == _ctx.DivisionId &&
                                      x.PatientRefID == patientRefId &&
                                      x.IsActive &&
                                      x.OccupationOptionID == data.OccupationOptionID &&
                                      x.StaffCode == normStaffCode &&
                                      x.OrgDeptName == normOrgDeptName &&
                                      x.JobTitle == normJobTitle, ct);

        if (existing != null) return existing;

        var now = DateTime.UtcNow;
        var row = new PatientEmployment
        {
            EmploymentRefID = Guid.NewGuid(),
            DivisionID = _ctx.DivisionId,
            PatientRefID = patientRefId,
            OccupationOptionID = data.OccupationOptionID,
            StaffCode = normStaffCode,
            OrgDeptName = normOrgDeptName,
            JobTitle = normJobTitle,
            IsActive = true,
            CreatedDate = now,
            CreatedBy = _ctx.ActorId,
            CreatedActorKind = _ctx.ActorKind,
            ModifiedDate = now,
            ModifiedBy = _ctx.ActorId,
            ModifiedActorKind = _ctx.ActorKind
        };
        _uow.PatientEmployments.Add(row);
        return row;
    }

    public async Task<Guid?> GetOrCreateEmploymentAsync(Guid patientRefId, PatientEmploymentData data, CancellationToken ct = default)
    {
        var row = await GetOrCreateEmploymentEntityAsync(patientRefId, data, ct);
        if (row != null && _uow.Context.Entry(row).State == EntityState.Added)
        {
            await _uow.SaveChangesAsync(ct);
        }
        return row?.EmploymentRefID;
    }

    public async Task<PatientRelative?> GetOrCreateRelativeEntityAsync(Guid patientRefId, PatientRelativeData data, CancellationToken ct = default)
    {
        if (!data.HasValue) return null;

        var normRelationshipCode = (data.RelationshipCode ?? "").Trim();
        var normFullName = (data.FullName ?? "").Trim();
        var normIdentityNumber = (data.IdentityNumber ?? "").Trim();
        var normPhoneNumber = (data.PhoneNumber ?? "").Trim();

        var existing = await _uow.PatientRelatives.Query().AsTracking()
            .FirstOrDefaultAsync(x => x.DivisionID == _ctx.DivisionId &&
                                      x.PatientRefID == patientRefId &&
                                      x.IsActive &&
                                      x.RelationshipCode == normRelationshipCode &&
                                      x.RelationshipOptionID == data.RelationshipOptionID &&
                                      x.FullName == normFullName &&
                                      x.IdentityNumber == normIdentityNumber &&
                                      x.PhoneNumber == normPhoneNumber, ct);

        if (existing != null) return existing;

        var now = DateTime.UtcNow;
        var row = new PatientRelative
        {
            RelativeRefID = Guid.NewGuid(),
            DivisionID = _ctx.DivisionId,
            PatientRefID = patientRefId,
            RelationshipCode = normRelationshipCode,
            RelationshipOptionID = data.RelationshipOptionID,
            FullName = normFullName,
            IdentityNumber = normIdentityNumber,
            PhoneNumber = normPhoneNumber,
            IsActive = true,
            CreatedDate = now,
            CreatedBy = _ctx.ActorId,
            CreatedActorKind = _ctx.ActorKind,
            ModifiedDate = now,
            ModifiedBy = _ctx.ActorId,
            ModifiedActorKind = _ctx.ActorKind
        };
        _uow.PatientRelatives.Add(row);
        return row;
    }

    public async Task<Guid?> GetOrCreateRelativeAsync(Guid patientRefId, PatientRelativeData data, CancellationToken ct = default)
    {
        var row = await GetOrCreateRelativeEntityAsync(patientRefId, data, ct);
        if (row != null && _uow.Context.Entry(row).State == EntityState.Added)
        {
            await _uow.SaveChangesAsync(ct);
        }
        return row?.RelativeRefID;
    }
}
