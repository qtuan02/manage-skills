using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using HealthExam.Application.Common;
using HealthExam.Application.ExamRecords;
using HealthExam.Application.ExamSessions;
using HealthExam.Application.Patients;
using HealthExam.Domain.Catalogs;
using HealthExam.Domain.Common;
using HealthExam.Domain.ExamRecords;
using HealthExam.Domain.ExamSessions;
using Microsoft.EntityFrameworkCore;

namespace HealthExam.Infrastructure.Persistence.Repositories;

public class ExamRecordRepository : IExamRecordRepository
{
    private readonly HealthExamDbContext _db;

    public ExamRecordRepository(HealthExamDbContext db)
    {
        _db = db;
    }

    public async Task<PageResult<ExamRecordResult>> ListAsync(
        string divisionId, ExamRecordFilter filter, CancellationToken ct = default)
    {
        filter ??= new ExamRecordFilter();

        var q = _db.ExamRecords.Where(x => x.DivisionID == divisionId);

        if (filter.SessionID.HasValue) q = q.Where(x => x.SessionID == filter.SessionID.Value);
        if (filter.State.HasValue) q = q.Where(x => (short)x.State == filter.State.Value);
        if (!string.IsNullOrWhiteSpace(filter.VariantCode))
            q = q.Where(x => x.VariantCode == filter.VariantCode.Trim());

        ApplyContainsFilter(ref q, filter.RecordCode, x => x.RecordCode);
        ApplyContainsFilter(ref q, filter.PatientCode, x => x.Patient.PatientCode);
        ApplyContainsFilter(ref q, filter.FullName, x => x.Patient.FullName);
        ApplyContainsFilter(ref q, filter.IdentityNumber, x => x.Patient.IdentityNumber);
        ApplyContainsFilter(ref q, filter.PhoneNumber, x => x.Patient.PhoneNumber);

        if (!string.IsNullOrWhiteSpace(filter.Keyword))
        {
            var kw = filter.Keyword.Trim().ToLower();
            q = q.Where(x => x.Patient.FullName.ToLower().Contains(kw)
                          || x.Patient.PatientCode.ToLower().Contains(kw)
                          || x.RecordCode.ToLower().Contains(kw)
                          || x.Patient.IdentityNumber.Contains(kw)
                          || x.Patient.PhoneNumber.Contains(kw));
        }

        if (filter.From.HasValue)
        {
            var from = new DateTimeOffset(filter.From.Value.ToDateTime(TimeOnly.MinValue), TimeSpan.FromHours(7)).UtcDateTime;
            q = q.Where(x => x.CreatedDate >= from);
        }

        if (filter.To.HasValue)
        {
            if (filter.To.Value < DateOnly.MaxValue)
            {
                var toExclusive = new DateTimeOffset(filter.To.Value.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.FromHours(7)).UtcDateTime;
                q = q.Where(x => x.CreatedDate < toExclusive);
            }
        }

        var total = await q.CountAsync(ct);
        var rows = await q
            .OrderByDescending(x => x.CreatedDate)
            .ThenBy(x => x.RecordCode)
            .Skip((filter.Page - 1) * filter.Size).Take(filter.Size)
            .Include(x => x.Session)
            .Include(x => x.Patient).ThenInclude(p => p.IdentityIssuerOption)
            .Include(x => x.Patient).ThenInclude(p => p.EthnicityOption)
            .Include(x => x.Insurance).ThenInclude(i => i.InsuranceObjectOption)
            .Include(x => x.Insurance).ThenInclude(i => i.RegistrationPlaceOption)
            .Include(x => x.Employment).ThenInclude(e => e.OccupationOption)
            .Include(x => x.Relative)
            .Include(x => x.PatientTypeOption)
            .Include(x => x.PatientSubjectOption)
            .Include(x => x.PaymentSourceOption)
            .Include(x => x.ExamLocationOption)
            .ToListAsync(ct);

        var items = rows.Select(r => MapToResult(r, r.Session?.SessionCode ?? "", r.Session?.ExamDate ?? default)).ToList();
        return new PageResult<ExamRecordResult>(items, filter.Page, filter.Size, total);
    }

    /// <summary>
    /// Trạng thái ký kết luận hoàn tất. So khớp CHÍNH XÁC chuỗi mà SignConclusion ghi —
    /// không ToLower()/StringComparison, vì cả hai chặn EF dùng chỉ mục và không cần thiết:
    /// chỉ một chỗ duy nhất trong hệ ghi giá trị này.
    /// </summary>
    private const string SignedStatus = "Signed";

    public async Task<PageResult<ExamRecordResult>> ListSignedByPatientLineageAsync(
        string divisionId, Guid profileLineageID, int page, int size, CancellationToken ct = default)
    {
        // Mọi PatientRefID cùng dòng: hồ sơ khám neo vào PHIÊN BẢN tại thời điểm khám, nên
        // lọc thẳng theo một PatientRefID sẽ mất các đợt trước lần sửa thông tin gần nhất.
        var versionIDs = _db.Patients
            .Where(p => p.DivisionID == divisionId && p.ProfileLineageID == profileLineageID)
            .Select(p => p.PatientRefID);

        var q = _db.ExamRecords
            .Where(x => x.DivisionID == divisionId
                     && x.PatientRefID.HasValue
                     && versionIDs.Contains(x.PatientRefID.Value)
                     && x.SignStatus == SignedStatus);

        var total = await q.CountAsync(ct);
        var rows = await q
            .OrderByDescending(x => x.Session.ExamDate)
            .ThenByDescending(x => x.HisSignedAt)
            .ThenBy(x => x.RecordCode)
            .Skip((page - 1) * size).Take(size)
            .Include(x => x.Session)
            .Include(x => x.Patient).ThenInclude(p => p.IdentityIssuerOption)
            .Include(x => x.Patient).ThenInclude(p => p.EthnicityOption)
            .Include(x => x.Insurance).ThenInclude(i => i.InsuranceObjectOption)
            .Include(x => x.Insurance).ThenInclude(i => i.RegistrationPlaceOption)
            .Include(x => x.Employment).ThenInclude(e => e.OccupationOption)
            .Include(x => x.Relative)
            .Include(x => x.PatientTypeOption)
            .Include(x => x.PatientSubjectOption)
            .Include(x => x.PaymentSourceOption)
            .Include(x => x.ExamLocationOption)
            .ToListAsync(ct);

        var items = rows
            .Select(r => MapToResult(r, r.Session?.SessionCode ?? "", r.Session?.ExamDate ?? default))
            .ToList();
        return new PageResult<ExamRecordResult>(items, page, size, total);
    }

    private static void ApplyContainsFilter(
        ref IQueryable<ExamRecord> query, string value,
        Expression<Func<ExamRecord, string>> field)
    {
        if (string.IsNullOrWhiteSpace(value)) return;

        var term = value.Trim().ToLower();
        var parameter = field.Parameters[0];
        var body = Expression.Call(
            Expression.Call(field.Body, nameof(string.ToLower), Type.EmptyTypes),
            nameof(string.Contains), Type.EmptyTypes,
            Expression.Constant(term));
        query = query.Where(Expression.Lambda<Func<ExamRecord, bool>>(body, parameter));
    }

    public async Task<ExamRecord> GetAsync(
        string divisionId, Guid recordId, bool forUpdate = false, CancellationToken ct = default)
    {
        var q = _db.ExamRecords.AsQueryable();
        if (forUpdate) q = q.AsTracking();
        return await q
            .Include(x => x.Session)
            .Include(x => x.Patient)
            .Include(x => x.SignSteps)
            .FirstOrDefaultAsync(x => x.DivisionID == divisionId && x.RecordID == recordId, ct);
    }

    public async Task<ExamRecord> GetWithRegistrationAsync(
        string divisionId, Guid recordId, bool forUpdate = false, CancellationToken ct = default)
    {
        var q = _db.ExamRecords.AsQueryable();
        if (forUpdate) q = q.AsTracking();
        return await q
            .Include(x => x.Session)
            .Include(x => x.Patient).ThenInclude(p => p.IdentityIssuerOption)
            .Include(x => x.Patient).ThenInclude(p => p.EthnicityOption)
            .Include(x => x.Insurance).ThenInclude(i => i.InsuranceObjectOption)
            .Include(x => x.Insurance).ThenInclude(i => i.RegistrationPlaceOption)
            .Include(x => x.Employment).ThenInclude(e => e.OccupationOption)
            .Include(x => x.Relative)
            .Include(x => x.PatientTypeOption)
            .Include(x => x.PatientSubjectOption)
            .Include(x => x.PaymentSourceOption)
            .Include(x => x.ExamLocationOption)
            .Include(x => x.SignSteps)
            .FirstOrDefaultAsync(x => x.DivisionID == divisionId && x.RecordID == recordId, ct);
    }

    public async Task<ExamRecordResult> GetResultAsync(
        string divisionId, Guid recordId, CancellationToken ct = default)
    {
        var row = await _db.ExamRecords
            .Where(x => x.DivisionID == divisionId && x.RecordID == recordId)
            .Include(x => x.Session)
            .Include(x => x.Patient).ThenInclude(p => p.IdentityIssuerOption)
            .Include(x => x.Patient).ThenInclude(p => p.EthnicityOption)
            .Include(x => x.Insurance).ThenInclude(i => i.InsuranceObjectOption)
            .Include(x => x.Insurance).ThenInclude(i => i.RegistrationPlaceOption)
            .Include(x => x.Employment).ThenInclude(e => e.OccupationOption)
            .Include(x => x.Relative)
            .Include(x => x.PatientTypeOption)
            .Include(x => x.PatientSubjectOption)
            .Include(x => x.PaymentSourceOption)
            .Include(x => x.ExamLocationOption)
            .FirstOrDefaultAsync(ct);

        if (row == null) return null;
        return MapToResult(row, row.Session?.SessionCode ?? "", row.Session?.ExamDate ?? default);
    }

    public async Task<bool> ExistsInSessionAsync(
        string divisionId, Guid sessionId, string patientCode, Guid? excludingRecordId = null, CancellationToken ct = default)
    {
        var pc = (patientCode ?? "").Trim();
        if (string.IsNullOrWhiteSpace(pc)) return false;

        return await _db.ExamRecords.AnyAsync(
            x => x.DivisionID == divisionId
              && x.SessionID == sessionId
              && x.Patient.PatientCode == pc
              && x.State != ExamRecordState.RegistrationCancelled
              && x.State != ExamRecordState.ExamCancelled
              && (excludingRecordId == null || x.RecordID != excludingRecordId), ct);
    }

    public async Task<bool> ExistsDuplicateAsync(
        string divisionId, Guid sessionId, string identityNumber, string patientCode, Guid? excludingRecordId = null, CancellationToken ct = default)
    {
        if (!string.IsNullOrWhiteSpace(identityNumber))
        {
            var id = identityNumber.Trim();
            var exists = await _db.ExamRecords.AnyAsync(
                x => x.DivisionID == divisionId
                  && x.SessionID == sessionId
                  && x.Patient.IdentityNumber == id
                  && x.State != ExamRecordState.RegistrationCancelled
                  && x.State != ExamRecordState.ExamCancelled
                  && (excludingRecordId == null || x.RecordID != excludingRecordId), ct);
            if (exists) return true;
        }

        if (!string.IsNullOrWhiteSpace(patientCode))
        {
            var pc = patientCode.Trim();
            var exists = await _db.ExamRecords.AnyAsync(
                x => x.DivisionID == divisionId
                  && x.SessionID == sessionId
                  && x.Patient.PatientCode == pc
                  && x.State != ExamRecordState.RegistrationCancelled
                  && x.State != ExamRecordState.ExamCancelled
                  && (excludingRecordId == null || x.RecordID != excludingRecordId), ct);
            if (exists) return true;
        }

        return false;
    }

    public async Task<ExamSession> GetOrCreateDefaultSessionAsync(
        string divisionId, CancellationToken ct = default)
    {
        const string defaultCode = "PHASE1-DEFAULT";
        var existing = await _db.ExamSessions.FirstOrDefaultAsync(
            x => x.DivisionID == divisionId && x.SessionCode == defaultCode, ct);
        if (existing != null) return existing;

        var now = DateTime.UtcNow;
        var sessionId = Guid.NewGuid();

        if (_db.Database.IsNpgsql())
        {
            await _db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "HEX_ExamSession"
                    ("SessionID", "DivisionID", "SessionCode", "SessionName", "ExamDate",
                     "DepartmentID", "State", "ExpectedCount", "IsActive",
                     "CreatedDate", "CreatedBy", "CreatedActorKind",
                     "ModifiedDate", "ModifiedBy", "ModifiedActorKind")
                VALUES
                    ({sessionId}, {divisionId}, {defaultCode}, {"Đợt mặc định Phase 1"},
                     {DateOnly.FromDateTime(now)}, {0}, {(short)ExamSessionState.Open}, {0}, {true},
                     {now}, {0L}, {(short)ActorKind.Employee},
                     {now}, {0L}, {(short)ActorKind.Employee})
                ON CONFLICT ("DivisionID", "SessionCode") DO NOTHING
                """, ct);

            return await _db.ExamSessions.SingleAsync(
                x => x.DivisionID == divisionId && x.SessionCode == defaultCode, ct);
        }

        var session = new ExamSession
        {
            SessionID = sessionId,
            DivisionID = divisionId,
            SessionCode = defaultCode,
            SessionName = "Đợt mặc định Phase 1",
            ExamDate = DateOnly.FromDateTime(now),
            State = ExamSessionState.Open,
            IsActive = true,
            CreatedDate = now,
            CreatedBy = 0L,
            CreatedActorKind = ActorKind.Employee,
            ModifiedDate = now,
            ModifiedBy = 0L,
            ModifiedActorKind = ActorKind.Employee
        };

        _db.ExamSessions.Add(session);
        await _db.SaveChangesAsync(ct);
        return session;
    }

    public async Task<ExamSessionProgressResult> GetProgressAsync(
        string divisionId, Guid sessionId, CancellationToken ct = default)
    {
        var session = await _db.ExamSessions.FirstOrDefaultAsync(
            x => x.SessionID == sessionId && x.DivisionID == divisionId, ct);
        if (session == null) return null;

        var q = _db.ExamRecords.Where(x => x.SessionID == session.SessionID && x.DivisionID == divisionId);

        var counts = await q
            .GroupBy(x => x.State)
            .Select(g => new { State = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        int CountOf(ExamRecordState state) => counts.FirstOrDefault(c => c.State == state)?.Count ?? 0;

        var notReg = CountOf(ExamRecordState.NotRegistered);
        var waiting = CountOf(ExamRecordState.Waiting);
        var inProgress = CountOf(ExamRecordState.InProgress);
        var completed = CountOf(ExamRecordState.Completed);
        var regCancel = CountOf(ExamRecordState.RegistrationCancelled);
        var examCancel = CountOf(ExamRecordState.ExamCancelled);
        var cancelled = regCancel + examCancel;
        var total = counts.Sum(c => c.Count);

        var profiles = new ExamSessionProgressCounts(
            Total: total,
            NotRegistered: notReg,
            Waiting: waiting,
            InProgress: inProgress,
            Completed: completed,
            RegistrationCancelled: regCancel,
            ExamCancelled: examCancel,
            Cancelled: cancelled);

        var conclusionReady = await q.CountAsync(
            x => x.State == ExamRecordState.InProgress
              && x.ProgressTotal > 0 && x.ProgressDone >= x.ProgressTotal, ct);

        var lastEventAt = await q.MaxAsync(x => (DateTime?)x.LastEventAt, ct);
        var lastSyncedAt = await q.MaxAsync(x => (DateTime?)x.ProgressSyncedAt, ct);

        DateTime? Latest(DateTime? a, DateTime? b)
        {
            if (!a.HasValue) return b;
            if (!b.HasValue) return a;
            return a.Value >= b.Value ? a : b;
        }

        return new ExamSessionProgressResult(
            SessionID: session.SessionID,
            SessionCode: session.SessionCode,
            State: ExamSessionStateNames.Of(session.State),
            Profiles: profiles,
            ConclusionReady: conclusionReady,
            UpdatedAt: Latest(lastEventAt, lastSyncedAt));
    }

    public async Task<ExamRecordResult> VerifyPortalCredentialsAsync(
        string divisionId, string patientCode, string identifier, CancellationToken ct = default)
    {
        return await VerifyPortalCredentialsAsync(divisionId, patientCode, identifier, identifier, ct);
    }

    public async Task<ExamRecordResult> VerifyPortalCredentialsAsync(
        string divisionId, string patientCode, string identityNumber, string insuranceNumber, CancellationToken ct = default)
    {
        var normPatientCode = (patientCode ?? "").Trim();
        var normIdentity = (identityNumber ?? "").Trim();
        var normInsurance = (insuranceNumber ?? "").Trim();

        var candidates = await _db.ExamRecords
            .Where(x => x.DivisionID == divisionId
                     && x.Patient.PatientCode == normPatientCode
                     && x.State != ExamRecordState.RegistrationCancelled
                     && x.State != ExamRecordState.ExamCancelled
                     && x.Session.State != ExamSessionState.Cancelled)
            .Select(x => new
            {
                RecordID = x.RecordID,
                IdentityNumber = x.Patient.IdentityNumber,
                InsuranceNumber = x.Insurance != null ? x.Insurance.InsuranceNumber : "",
                ExamDate = x.Session.ExamDate,
                CreatedDate = x.CreatedDate
            })
            .ToListAsync(ct);

        var matched = candidates
            .Where(c => PortalCredentialsMatch(c.IdentityNumber, c.InsuranceNumber, normIdentity, normInsurance))
            .OrderByDescending(c => c.ExamDate)
            .ThenByDescending(c => c.CreatedDate)
            .FirstOrDefault();

        if (matched == null) return null;
        return await GetResultAsync(divisionId, matched.RecordID, ct);
    }

    public static bool PortalCredentialsMatch(
        string storedIdentity, string storedInsurance, string identityNumber, string insuranceNumber)
    {
        var normId = (identityNumber ?? "").Trim();
        var normIns = (insuranceNumber ?? "").Trim();

        var matchedAny = false;

        if (normId.Length > 0)
        {
            var stored = (storedIdentity ?? "").Trim();
            if (stored.Length == 0 || !string.Equals(stored, normId, StringComparison.OrdinalIgnoreCase))
                return false;
            matchedAny = true;
        }

        if (normIns.Length > 0)
        {
            var stored = (storedInsurance ?? "").Trim();
            if (stored.Length == 0 || !string.Equals(stored, normIns, StringComparison.OrdinalIgnoreCase))
                return false;
            matchedAny = true;
        }

        return matchedAny;
    }

    public async Task<ExamPackage> GetPackageAsync(
        string divisionId, Guid packageId, CancellationToken ct = default)
    {
        return await _db.ExamPackages.FirstOrDefaultAsync(
            x => x.PackageID == packageId && x.DivisionID == divisionId, ct);
    }

    public async Task<(string Code, string Name)?> ResolveMasterDataAsync(
        string divisionId, string category, string code, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;
        var trimmed = code.Trim();

        if (category == MasterDataCategories.BloodAbo)
        {
            var match = new[] { "A", "B", "AB", "O" }
                .FirstOrDefault(x => string.Equals(x, trimmed, StringComparison.OrdinalIgnoreCase));
            return match != null ? (match, match) : null;
        }

        if (category == MasterDataCategories.BloodRh)
        {
            var match = new[] { "+", "-" }
                .FirstOrDefault(x => string.Equals(x, trimmed, StringComparison.OrdinalIgnoreCase));
            return match != null ? (match, match) : null;
        }

        if (category == MasterDataCategories.Relationship)
        {
            var staticRel = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["FATHER"] = "Cha",
                ["MOTHER"] = "Mẹ",
                ["SPOUSE"] = "Vợ-chồng",
                ["CHILD"] = "Con",
                ["GUARDIAN"] = "Người giám hộ",
                ["OTHER"] = "Khác"
            };
            return staticRel.TryGetValue(trimmed, out var name) ? (trimmed.ToUpperInvariant(), name) : null;
        }

        var opt = await _db.MasterDataOptions.FirstOrDefaultAsync(
            x => x.DivisionID == divisionId && x.Category == category && x.Code == trimmed && x.IsActive, ct);

        return opt != null ? (opt.Code, opt.Name) : null;
    }

    public async Task<MasterDataOption> ResolveMasterDataOptionAsync(
        string divisionId, string category, string code, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;
        var trimmed = code.Trim();
        return await _db.MasterDataOptions.AsTracking().FirstOrDefaultAsync(
            x => x.DivisionID == divisionId && x.Category == category && x.Code == trimmed && x.IsActive, ct);
    }

    public async Task<(string Code, string Name)?> ResolveWardAsync(
        string divisionId, string provinceCode, string wardCode, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(provinceCode) || string.IsNullOrWhiteSpace(wardCode))
            return null;

        var prov = provinceCode.Trim();
        var ward = wardCode.Trim();

        var provExists = await _db.MasterDataOptions.AnyAsync(
            x => x.DivisionID == divisionId && x.Category == MasterDataCategories.Province && x.Code == prov && x.IsActive, ct);
        if (!provExists) return null;

        var opt = await _db.MasterDataOptions.FirstOrDefaultAsync(
            x => x.DivisionID == divisionId && x.Category == MasterDataCategories.Ward && x.ParentCode == prov && x.Code == ward && x.IsActive, ct);

        return opt != null ? (opt.Code, opt.Name) : null;
    }

    public async Task<ExamRecord> ResolveForWebhookAsync(
        string divisionId,
        Guid? submissionId,
        string subjectId,
        string hostRefId,
        bool forUpdate = true,
        CancellationToken ct = default)
    {
        var division = divisionId ?? "";
        var q = _db.ExamRecords.Where(x => x.DivisionID == division);
        if (forUpdate) q = q.AsTracking();

        if (submissionId.HasValue && submissionId.Value != Guid.Empty)
        {
            var bySubmission = await q.FirstOrDefaultAsync(x => x.SubmissionID == submissionId.Value, ct);
            if (bySubmission != null) return bySubmission;
        }

        var subject = (subjectId ?? "").Trim();
        var hostRef = (hostRefId ?? "").Trim();

        if (subject.Length > 0)
        {
            var bySubject = await q.FirstOrDefaultAsync(x => x.RecordCode == subject, ct);
            if (bySubject != null) return bySubject;
        }

        if (hostRef.Length > 0)
        {
            var byHostRef = await q.FirstOrDefaultAsync(x => x.RecordCode == hostRef, ct);
            if (byHostRef != null) return byHostRef;

            if (subject.Length > 0)
            {
                var byPatientInSession = await q.FirstOrDefaultAsync(
                    x => x.Session.SessionCode == hostRef && x.Patient.PatientCode == subject, ct);
                if (byPatientInSession != null) return byPatientInSession;
            }
        }

        return null;
    }

    public async Task<ExamRecord> LockRecordAsync(Guid recordId, CancellationToken ct = default)
    {
        return _db.Database.IsNpgsql()
            ? await _db.ExamRecords
                .FromSqlRaw("""SELECT * FROM "HEX_ExamRecord" WHERE "RecordID" = {0} FOR UPDATE""", recordId)
                .Include(x => x.Patient)
                .AsTracking()
                .FirstOrDefaultAsync(ct)
            : await _db.ExamRecords
                .Include(x => x.Patient)
                .AsTracking()
                .FirstOrDefaultAsync(x => x.RecordID == recordId, ct);
    }

    public async Task<IReadOnlyList<ReconcileCandidate>> FindReconcileCandidatesAsync(
        string divisionId = null,
        Guid? sessionId = null,
        int batchSize = 100,
        CancellationToken ct = default)
    {
        var q = _db.ExamRecords
            .Where(x => x.SubmissionID != null
                     && (x.State == ExamRecordState.Waiting || x.State == ExamRecordState.InProgress));

        if (!string.IsNullOrWhiteSpace(divisionId)) q = q.Where(x => x.DivisionID == divisionId);
        if (sessionId.HasValue) q = q.Where(x => x.SessionID == sessionId.Value);

        return await q
            .OrderBy(x => x.ProgressSyncedAt ?? x.CreatedDate)
            .Take(batchSize <= 0 ? 100 : batchSize)
            .Select(x => new ReconcileCandidate(x.RecordID, x.SubmissionID.Value, x.DivisionID, x.RecordCode))
            .ToListAsync(ct);
    }

    public void Add(ExamRecord record)
    {
        _db.ExamRecords.Add(record);
    }

    public static ExamRecordResult MapToResult(ExamRecord x, string sessionCode, DateOnly examDate) => new(
        RecordID: x.RecordID,
        SessionID: x.SessionID,
        SessionCode: sessionCode ?? "",
        ExamDate: examDate,
        RecordCode: x.RecordCode ?? "",
        PatientID: x.Patient?.HisPatientID ?? 0L,
        AdmissionID: x.AdmissionID,
        PatientCode: x.Patient?.PatientCode ?? "",
        FullName: x.Patient?.FullName ?? "",
        Dob: x.Patient?.Dob,
        BirthYear: x.Patient?.BirthYear,
        GenderID: x.Patient?.GenderID ?? 0,
        IdentityNumber: x.Patient?.IdentityNumber ?? "",
        InsuranceNumber: x.Insurance?.InsuranceNumber ?? "",
        PhoneNumber: x.Patient?.PhoneNumber ?? "",
        Email: x.Patient?.Email ?? "",
        Address: x.Patient?.Address ?? "",
        StaffCode: x.Employment?.StaffCode ?? "",
        OrgDeptName: x.Employment?.OrgDeptName ?? "",
        JobTitle: x.Employment?.JobTitle ?? "",
        VariantCode: x.VariantCode ?? "",
        VariantName: ExamGroups.All.FirstOrDefault(g => g.VariantCode == x.VariantCode)?.GroupName ?? "",
        PackageID: x.PackageID,
        PackageName: x.PackageName ?? "",
        FormID: x.FormID,
        FormCode: x.FormCode ?? "",
        SubmissionID: x.SubmissionID,
        State: x.State,
        StateName: ExamRecordStateNames.Of(x.State),
        RegisteredAt: x.RegisteredAt,
        ExamStartedAt: x.ExamStartedAt,
        ExamFinishedAt: x.ExamFinishedAt,
        CancelledAt: x.CancelledAt,
        CancelReason: x.CancelReason ?? "",
        ProgressDone: x.ProgressDone,
        ProgressTotal: x.ProgressTotal,
        HealthClassCode: x.HealthClassCode ?? "",
        Note: x.Note,
        EthnicityCode: x.Patient?.EthnicityOption?.Code ?? "",
        EthnicityName: x.Patient?.EthnicityOption?.Name ?? "",
        OccupationCode: x.Employment?.OccupationOption?.Code ?? "",
        OccupationName: x.Employment?.OccupationOption?.Name ?? "",
        BloodAboCode: x.Patient?.BloodAboCode ?? "",
        BloodAboName: ResolveBloodAboName(x.Patient?.BloodAboCode),
        BloodRhCode: x.Patient?.BloodRhCode ?? "",
        BloodRhName: ResolveBloodRhName(x.Patient?.BloodRhCode),
        ProvinceCode: x.ProvinceCode ?? "",
        ProvinceName: x.ProvinceName ?? "",
        WardCode: x.WardCode ?? "",
        WardName: x.WardName ?? "",
        IdentityIssuedDate: x.Patient?.IdentityIssuedDate,
        IdentityIssuerCode: x.Patient?.IdentityIssuerOption?.Code ?? "",
        IdentityIssuerName: x.Patient?.IdentityIssuerOption?.Name ?? "",
        RelativeRelationshipCode: x.Relative?.RelationshipCode ?? "",
        RelativeRelationshipName: ResolveRelationshipName(x.Relative?.RelationshipCode),
        RelativeFullName: x.Relative?.FullName ?? "",
        RelativeIdentityNumber: x.Relative?.IdentityNumber ?? "",
        RelativePhoneNumber: x.Relative?.PhoneNumber ?? "",
        InsuranceObjectCode: x.Insurance?.InsuranceObjectOption?.Code ?? "",
        InsuranceObjectName: x.Insurance?.InsuranceObjectOption?.Name ?? "",
        InsuranceValidFrom: x.Insurance?.ValidFrom,
        InsuranceValidTo: x.Insurance?.ValidTo,
        ExamReason: x.ExamReason ?? "",
        PatientTypeCode: x.PatientTypeOption?.Code ?? "",
        PatientTypeName: x.PatientTypeOption?.Name ?? "",
        PatientSubjectCode: x.PatientSubjectOption?.Code ?? "",
        PatientSubjectName: x.PatientSubjectOption?.Name ?? "",
        PaymentSourceCode: x.PaymentSourceOption?.Code ?? "",
        PaymentSourceName: x.PaymentSourceOption?.Name ?? "",
        PaymentSourceOther: x.PaymentSourceOther ?? "",
        ExamLocationCode: x.ExamLocationOption?.Code ?? "",
        ExamLocationName: x.ExamLocationOption?.Name ?? "",
        CreatedDate: x.CreatedDate,
        ModifiedDate: x.ModifiedDate,
        PatientRefID: x.PatientRefID,
        InsuranceRefID: x.InsuranceRefID,
        EmploymentRefID: x.EmploymentRefID,
        RelativeRefID: x.RelativeRefID,
        PatientTypeOptionID: x.PatientTypeOptionID,
        PatientSubjectOptionID: x.PatientSubjectOptionID,
        PaymentSourceOptionID: x.PaymentSourceOptionID,
        ExamLocationOptionID: x.ExamLocationOptionID,
        RegistrationPlaceCode: x.Insurance?.RegistrationPlaceOption?.Code ?? "",
        RegistrationPlaceName: x.Insurance?.RegistrationPlaceOption?.Name ?? "",
        HisSignStatus: x.SignStatus == ExamRecordSignStatus.New ? "" : x.SignStatus,
        HisSignedAt: x.HisSignedAt,
        HisSignedFilePath: x.SignedFilePath ?? "");

    private static string ResolveBloodAboName(string code) =>
        code?.ToUpperInvariant() switch
        {
            "A" => "A",
            "B" => "B",
            "AB" => "AB",
            "O" => "O",
            _ => code ?? ""
        };

    private static string ResolveBloodRhName(string code) =>
        code switch
        {
            "+" => "+",
            "-" => "-",
            _ => code ?? ""
        };

    private static string ResolveRelationshipName(string code) => RelationshipNames.Of(code);
}
