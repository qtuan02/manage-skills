using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HealthExam.Application.Common;
using HealthExam.Application.ExamRecords;
using HealthExam.Application.Integrations;

namespace HealthExam.Application.His;

public interface IEnsureHisAdmission
{
    Task<ApplicationResult<long>> HandleAsync(
        EnsureHisAdmissionCommand command, CancellationToken ct = default);
}

public sealed class EnsureHisAdmission : IEnsureHisAdmission
{
    private readonly IExamRecordRepository _repo;
    private readonly IHisEmrClient _his;
    private readonly IHisCredentialOptions _options;
    private readonly IClock _clock;
    private readonly IUnitOfWork _uow;

    public EnsureHisAdmission(
        IExamRecordRepository repo,
        IHisEmrClient his,
        IHisCredentialOptions options,
        IClock clock,
        IUnitOfWork uow = null)
    {
        _repo = repo;
        _his = his;
        _options = options;
        _clock = clock;
        _uow = uow;
    }

    public async Task<ApplicationResult<long>> HandleAsync(
        EnsureHisAdmissionCommand command, CancellationToken ct = default)
    {
        var record = await _repo.GetWithRegistrationAsync(command.DivisionId, command.RecordId, forUpdate: true, ct)
            ?? await _repo.GetAsync(command.DivisionId, command.RecordId, forUpdate: true, ct);
        if (record == null)
        {
            return ApplicationResult<long>.Fail(
                ApplicationFailureCode.NotFound, "Không tìm thấy hồ sơ khám");
        }

        if (record.AdmissionID.GetValueOrDefault() > 0)
        {
            return ApplicationResult<long>.Success(record.AdmissionID.Value);
        }

        var departmentId = record.Session?.DepartmentID > 0
            ? record.Session.DepartmentID
            : _options.KskDepartmentId.GetValueOrDefault();
        if (departmentId <= 0)
        {
            return ApplicationResult<long>.Fail(
                ApplicationFailureCode.VendorNotConfigured,
                "Chưa cấu hình khoa KSK HIS cho lượt tiếp nhận");
        }

        var departmentCode = _options.KskDepartmentCode?.Trim();
        if (string.IsNullOrEmpty(departmentCode))
        {
            return ApplicationResult<long>.Fail(
                ApplicationFailureCode.VendorNotConfigured,
                "Chưa cấu hình mã khoa KSK HIS cho lượt tiếp nhận");
        }

        var patientCode = record.Patient?.PatientCode;
        if (string.IsNullOrWhiteSpace(patientCode))
        {
            return ApplicationResult<long>.Fail(
                ApplicationFailureCode.BadRequest,
                "Hồ sơ chưa có mã người bệnh để tạo lượt tiếp nhận HIS");
        }

        var genderId = record.Patient?.GenderID ?? 0;
        if (genderId < byte.MinValue || genderId > byte.MaxValue)
        {
            return ApplicationResult<long>.Fail(
                ApplicationFailureCode.BadRequest,
                "Giới tính trong hồ sơ không hợp lệ");
        }

        var patient = record.Patient;
        var fullName = patient?.FullName ?? "";
        var (lastName, firstName) = SplitName(fullName);
        var dob = patient?.Dob;
        var birthYear = patient?.BirthYear ?? (dob.HasValue ? dob.Value.Year : 0);

        var patReq = new HisPatientCreateRequest(
            PatientCode: patientCode,
            FirstName: firstName,
            LastName: lastName,
            FullName: fullName,
            Gender: (byte)genderId,
            BirthDate: dob?.ToDateTime(TimeOnly.MinValue),
            BirthYear: birthYear,
            IdentityNumber: patient?.IdentityNumber ?? "",
            PhoneNumber: patient?.PhoneNumber ?? "",
            Email: patient?.Email ?? "",
            Address: patient?.Address ?? ""
        );

        var admReq = new HisAdmissionCreateRequest(
            IsOutPatient: (byte)2,
            AdmissionCode: $"HEX-{record.RecordID:N}",
            AdmissionDate: record.Session != null ? record.Session.ExamDate.ToDateTime(TimeOnly.MinValue) : DateTime.UtcNow,
            DepartmentID: departmentId,
            DepartmentCode: departmentCode,
            Patient: patReq
        );

        try
        {
            var res = await _his.CreateAdmissionAsync(admReq, command.Context, ct);
            if (!res.IsSuccess)
            {
                var failureCode = res.Outcome switch
                {
                    HisClientOutcome.Unauthorized => ApplicationFailureCode.Unauthorized,
                    HisClientOutcome.Forbidden => ApplicationFailureCode.Forbidden,
                    HisClientOutcome.NotFound => ApplicationFailureCode.NotFound,
                    HisClientOutcome.Timeout => ApplicationFailureCode.HisTimeout,
                    HisClientOutcome.VendorNotConfigured => ApplicationFailureCode.VendorNotConfigured,
                    HisClientOutcome.SignPrecondition => ApplicationFailureCode.SignPrecondition,
                    _ => ApplicationFailureCode.HisBadGateway
                };
                return ApplicationResult<long>.Fail(failureCode, res.Message);
            }

            record.AdmissionID = res.Value;
            record.ModifiedDate = _clock.UtcNow;
            record.ModifiedBy = command.ActorId;
            record.ModifiedActorKind = command.ActorKind;
            if (_uow != null)
            {
                await _uow.SaveChangesAsync(ct);
            }

            return ApplicationResult<long>.Success(res.Value);
        }
        catch (Exception ex)
        {
            return ApplicationResult<long>.Fail(ApplicationFailureCode.HisBadGateway, ex.Message);
        }
    }

    private static (string LastName, string FirstName) SplitName(string fullName)
    {
        var parts = (fullName ?? "")
            .Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
            return ("", "");
        if (parts.Length == 1)
            return ("", parts[0]);
        return (string.Join(" ", parts.Take(parts.Length - 1)), parts[^1]);
    }
}
