using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HealthExam.Application.Patients;
using HealthExam.Domain.Common;
using HealthExam.Domain.Patients;
using Microsoft.EntityFrameworkCore;

namespace HealthExam.Infrastructure.Persistence.Repositories;

public class PatientRepository : IPatientRepository
{
    private readonly HealthExamDbContext _db;

    public PatientRepository(HealthExamDbContext db)
    {
        _db = db;
    }

    public async Task<Patient> FindForWriteAsync(string divisionId, Guid? patientRefID, long? hisPatientID, CancellationToken ct = default)
    {
        if (patientRefID.HasValue && patientRefID.Value != Guid.Empty)
        {
            var patient = await _db.Patients.AsTracking()
                .FirstOrDefaultAsync(p => p.DivisionID == divisionId && p.PatientRefID == patientRefID.Value && p.IsActive, ct);
            if (patient != null) return patient;
        }

        if (hisPatientID.HasValue && hisPatientID.Value > 0)
        {
            return await _db.Patients.AsTracking()
                .FirstOrDefaultAsync(p => p.DivisionID == divisionId && p.HisPatientID == hisPatientID.Value && p.IsActive, ct);
        }

        return null;
    }

    public async Task<Patient> FindActiveByIdentityNumberAsync(string divisionId, string identityNumber, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(identityNumber)) return null;
        var normIdentity = identityNumber.Trim();

        return await _db.Patients.AsNoTracking()
            .FirstOrDefaultAsync(p => p.DivisionID == divisionId && p.IdentityNumber == normIdentity && p.IsActive, ct);
    }

    public async Task<Patient> FindActiveByRefIdAsync(string divisionId, Guid patientRefID, CancellationToken ct = default)
    {
        if (patientRefID == Guid.Empty) return null;

        return await _db.Patients.AsNoTracking()
            .FirstOrDefaultAsync(p => p.DivisionID == divisionId && p.PatientRefID == patientRefID && p.IsActive, ct);
    }

    public async Task<PatientProfileSnapshot> FindActiveProfileAsync(string divisionId, Guid patientRefID, CancellationToken ct = default)
    {
        if (patientRefID == Guid.Empty) return null;

        var patient = await _db.Patients.AsNoTracking()
            .Include(p => p.IdentityIssuerOption)
            .Include(p => p.EthnicityOption)
            .FirstOrDefaultAsync(p => p.DivisionID == divisionId && p.PatientRefID == patientRefID && p.IsActive, ct);
        if (patient == null) return null;

        // Dòng con đúng như lần đăng ký gần nhất của phiên bản này. Không lấy "dòng mới nhất":
        // PatientRegistrationWriter tái dùng dòng cũ khi bộ giá trị trùng, nên dòng mới nhất
        // theo CreatedDate có thể không phải dòng vừa được dùng.
        var latest = await _db.ExamRecords.AsNoTracking()
            .Where(x => x.DivisionID == divisionId && x.PatientRefID == patientRefID)
            .OrderByDescending(x => x.CreatedDate)
            .Select(x => new { x.InsuranceRefID, x.EmploymentRefID, x.RelativeRefID })
            .FirstOrDefaultAsync(ct);

        var insurances = _db.PatientInsurances.AsNoTracking()
            .Include(i => i.InsuranceObjectOption)
            .Include(i => i.RegistrationPlaceOption)
            .Where(i => i.DivisionID == divisionId && i.PatientRefID == patientRefID);
        var employments = _db.PatientEmployments.AsNoTracking()
            .Include(e => e.OccupationOption)
            .Where(e => e.DivisionID == divisionId && e.PatientRefID == patientRefID);
        var relatives = _db.PatientRelatives.AsNoTracking()
            .Where(r => r.DivisionID == divisionId && r.PatientRefID == patientRefID);

        PatientInsurance insurance;
        PatientEmployment employment;
        PatientRelative relative;
        if (latest != null)
        {
            // IsActive lọc ở cả hai nhánh: dòng con của lần đăng ký gần nhất mà từ đó đã bị vô
            // hiệu hoá (sửa dữ liệu, gộp trùng...) không còn là dòng đáng tin để điền form mới,
            // giống hệt lý do nhánh fallback bên dưới lọc IsActive.
            insurance = latest.InsuranceRefID.HasValue
                ? await insurances.FirstOrDefaultAsync(i => i.InsuranceRefID == latest.InsuranceRefID.Value && i.IsActive, ct)
                : null;
            employment = latest.EmploymentRefID.HasValue
                ? await employments.FirstOrDefaultAsync(e => e.EmploymentRefID == latest.EmploymentRefID.Value && e.IsActive, ct)
                : null;
            relative = latest.RelativeRefID.HasValue
                ? await relatives.FirstOrDefaultAsync(r => r.RelativeRefID == latest.RelativeRefID.Value && r.IsActive, ct)
                : null;
        }
        else
        {
            insurance = await insurances.Where(i => i.IsActive).OrderByDescending(i => i.CreatedDate).FirstOrDefaultAsync(ct);
            employment = await employments.Where(e => e.IsActive).OrderByDescending(e => e.CreatedDate).FirstOrDefaultAsync(ct);
            relative = await relatives.Where(r => r.IsActive).OrderByDescending(r => r.CreatedDate).FirstOrDefaultAsync(ct);
        }

        return new PatientProfileSnapshot(patient, insurance, employment, relative);
    }

    public async Task<Guid?> FindProfileLineageIdAsync(string divisionId, Guid patientRefID, CancellationToken ct = default)
    {
        if (patientRefID == Guid.Empty) return null;

        return await _db.Patients.AsNoTracking()
            .Where(p => p.DivisionID == divisionId && p.PatientRefID == patientRefID)
            .Select(p => (Guid?)p.ProfileLineageID)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<IReadOnlyList<Patient>> SearchActiveAsync(
        string divisionId, PatientSearchCriteria criteria, int limit, CancellationToken ct = default)
    {
        var query = _db.Patients.AsNoTracking()
            .Where(p => p.DivisionID == divisionId && p.IsActive);

        if (criteria != null && criteria.HasKeyword)
        {
            var kw = criteria.Keyword.Trim();
            var kwLower = kw.ToLower();
            var byIdentity = criteria.Fields.Contains(PatientSearchField.Identity);
            var byPhone = criteria.Fields.Contains(PatientSearchField.Phone);
            var byCode = criteria.Fields.Contains(PatientSearchField.Code);
            var byName = criteria.Fields.Contains(PatientSearchField.Name);

            // Các cờ bool bị EF Core parameter hoá; nhánh false sẽ không khớp dòng nào.
            // ToLower() dịch sang LOWER() của Postgres nên giữ nguyên dấu tiếng Việt.
            query = query.Where(p =>
                (byIdentity && p.IdentityNumber.Contains(kw)) ||
                (byPhone && p.PhoneNumber.Contains(kw)) ||
                (byCode && p.PatientCode.ToLower().Contains(kwLower)) ||
                (byName && p.FullName.ToLower().Contains(kwLower)));
        }

        // Không keyword = "20 hồ sơ mới cập nhật nhất"; có keyword cũng ưu tiên bản mới sửa.
        return await query
            .OrderByDescending(p => p.ModifiedDate)
            .ThenBy(p => p.PatientRefID)
            .Take(limit)
            .ToListAsync(ct);
    }

    public async Task<int?> AllocateNextVersionAsync(
        string divisionId, Guid profileLineageID, Guid sourcePatientRefID,
        CancellationToken ct = default)
    {
        var source = await _db.Patients
            .FromSqlInterpolated($@"
                SELECT * FROM ""HEX_Patient""
                WHERE ""DivisionID"" = {divisionId}
                  AND ""PatientRefID"" = {sourcePatientRefID}
                  AND ""IsActive""
                FOR UPDATE")
            .AsTracking()
            .SingleOrDefaultAsync(ct);

        if (source == null) return null;

        var maxVersion = await _db.Patients
            .Where(p => p.DivisionID == divisionId && p.ProfileLineageID == profileLineageID)
            .MaxAsync(p => (int?)p.VersionNumber, ct) ?? 0;

        return maxVersion + 1;
    }

    public async Task<bool> DeactivateAsync(
        string divisionId, Guid patientRefID, DateTime modifiedDate,
        long actorId, ActorKind actorKind, CancellationToken ct = default)
    {
        var rows = await _db.Patients
            .Where(p => p.DivisionID == divisionId && p.PatientRefID == patientRefID && p.IsActive)
            .ExecuteUpdateAsync(s => s
                .SetProperty(p => p.IsActive, false)
                .SetProperty(p => p.ModifiedDate, modifiedDate)
                .SetProperty(p => p.ModifiedBy, actorId)
                .SetProperty(p => p.ModifiedActorKind, actorKind), ct);

        return rows == 1;
    }

    public async Task<PatientInsurance> FindActiveInsuranceAsync(string divisionId, Guid patientRefID, PatientInsuranceWrite value, CancellationToken ct = default)
    {
        if (value == null || !value.HasValue) return null;

        var normNumber = (value.InsuranceNumber ?? "").Trim();

        return await _db.PatientInsurances.AsTracking()
            .FirstOrDefaultAsync(x => x.DivisionID == divisionId &&
                                      x.PatientRefID == patientRefID &&
                                      x.IsActive &&
                                      x.InsuranceNumber == normNumber &&
                                      x.InsuranceObjectOptionID == value.InsuranceObjectOptionID &&
                                      x.RegistrationPlaceOptionID == value.RegistrationPlaceOptionID &&
                                      x.ValidFrom == value.ValidFrom &&
                                      x.ValidTo == value.ValidTo, ct);
    }

    public async Task<PatientEmployment> FindActiveEmploymentAsync(string divisionId, Guid patientRefID, PatientEmploymentWrite value, CancellationToken ct = default)
    {
        if (value == null || !value.HasValue) return null;

        var normStaffCode = (value.StaffCode ?? "").Trim();
        var normOrgDeptName = (value.OrgDeptName ?? "").Trim();
        var normJobTitle = (value.JobTitle ?? "").Trim();

        return await _db.PatientEmployments.AsTracking()
            .FirstOrDefaultAsync(x => x.DivisionID == divisionId &&
                                      x.PatientRefID == patientRefID &&
                                      x.IsActive &&
                                      x.OccupationOptionID == value.OccupationOptionID &&
                                      x.StaffCode == normStaffCode &&
                                      x.OrgDeptName == normOrgDeptName &&
                                      x.JobTitle == normJobTitle, ct);
    }

    public async Task<PatientRelative> FindActiveRelativeAsync(string divisionId, Guid patientRefID, PatientRelativeWrite value, CancellationToken ct = default)
    {
        if (value == null || !value.HasValue) return null;

        var normRelationshipCode = (value.RelationshipCode ?? "").Trim();
        var normFullName = (value.FullName ?? "").Trim();
        var normIdentityNumber = (value.IdentityNumber ?? "").Trim();
        var normPhoneNumber = (value.PhoneNumber ?? "").Trim();

        return await _db.PatientRelatives.AsTracking()
            .FirstOrDefaultAsync(x => x.DivisionID == divisionId &&
                                      x.PatientRefID == patientRefID &&
                                      x.IsActive &&
                                      x.RelationshipCode == normRelationshipCode &&
                                      x.RelationshipOptionID == value.RelationshipOptionID &&
                                      x.FullName == normFullName &&
                                      x.IdentityNumber == normIdentityNumber &&
                                      x.PhoneNumber == normPhoneNumber, ct);
    }

    public void Add(Patient entity) => _db.Patients.Add(entity);
    public void Add(PatientInsurance entity) => _db.PatientInsurances.Add(entity);
    public void Add(PatientEmployment entity) => _db.PatientEmployments.Add(entity);
    public void Add(PatientRelative entity) => _db.PatientRelatives.Add(entity);
}
