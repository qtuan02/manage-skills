using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HealthExam.Application.Common;
using HealthExam.Application.Integrations;
using HealthExam.Application.Patients;

namespace HealthExam.Application.His;

public interface IEnsureHisPatient
{
    Task<ApplicationResult<long>> HandleAsync(
        EnsureHisPatientCommand command, CancellationToken ct = default);
}

public sealed class EnsureHisPatient : IEnsureHisPatient
{
    private readonly IPatientRepository _repo;
    private readonly IHisEmrClient _his;
    private readonly IClock _clock;
    private readonly IUnitOfWork _uow;

    public EnsureHisPatient(
        IPatientRepository repo,
        IHisEmrClient his,
        IClock clock,
        IUnitOfWork uow = null)
    {
        _repo = repo;
        _his = his;
        _clock = clock;
        _uow = uow;
    }

    public async Task<ApplicationResult<long>> HandleAsync(
        EnsureHisPatientCommand command, CancellationToken ct = default)
    {
        var patient = await _repo.FindForWriteAsync(command.DivisionId, command.PatientRefID, null, ct);
        if (patient == null)
        {
            return ApplicationResult<long>.Fail(
                ApplicationFailureCode.NotFound, "Không tìm thấy thông tin người bệnh");
        }

        if (patient.HisPatientID.HasValue && patient.HisPatientID.Value > 0)
        {
            return ApplicationResult<long>.Success(patient.HisPatientID.Value);
        }

        if (string.IsNullOrWhiteSpace(patient.PatientCode))
        {
            return ApplicationResult<long>.Fail(
                ApplicationFailureCode.BadRequest, "Người bệnh chưa có mã để đồng bộ HIS");
        }

        if (string.IsNullOrWhiteSpace(patient.FullName))
        {
            return ApplicationResult<long>.Fail(
                ApplicationFailureCode.BadRequest, "Người bệnh chưa có họ tên để đồng bộ HIS");
        }

        if (patient.GenderID < byte.MinValue || patient.GenderID > byte.MaxValue)
        {
            return ApplicationResult<long>.Fail(
                ApplicationFailureCode.BadRequest, "Giới tính người bệnh không hợp lệ");
        }

        var (lastName, firstName) = SplitName(patient.FullName);
        var request = new HisPatientCreateRequest(
            PatientCode: patient.PatientCode,
            FirstName: firstName,
            LastName: lastName,
            FullName: patient.FullName,
            Gender: (byte)patient.GenderID,
            BirthDate: patient.Dob?.ToDateTime(TimeOnly.MinValue),
            BirthYear: patient.BirthYear ?? (patient.Dob.HasValue ? patient.Dob.Value.Year : 0),
            IdentityNumber: patient.IdentityNumber ?? "",
            PhoneNumber: patient.PhoneNumber ?? "",
            Email: patient.Email ?? "",
            Address: patient.Address ?? ""
        );

        try
        {
            var res = await _his.CreatePatientAsync(request, command.Context, ct);
            if (!res.IsSuccess)
            {
                patient.HisSyncStatus = "Failed";
                var rawMessage = res.Message ?? "Đồng bộ HIS thất bại";
                patient.HisSyncError = rawMessage.Length > 1000 ? rawMessage[..1000] : rawMessage;
                patient.ModifiedDate = _clock.UtcNow;
                patient.ModifiedBy = command.ActorId;
                patient.ModifiedActorKind = command.ActorKind;
                if (_uow != null)
                {
                    try { await _uow.SaveChangesAsync(ct); } catch { }
                }

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

            patient.HisPatientID = res.Value;
            patient.HisSyncStatus = "Linked";
            patient.HisSyncError = "";
            patient.ModifiedDate = _clock.UtcNow;
            patient.ModifiedBy = command.ActorId;
            patient.ModifiedActorKind = command.ActorKind;
            if (_uow != null)
            {
                await _uow.SaveChangesAsync(ct);
            }

            return ApplicationResult<long>.Success(res.Value);
        }
        catch (Exception ex)
        {
            patient.HisSyncStatus = "Failed";
            var rawMessage = ex.Message ?? "Đồng bộ HIS thất bại";
            patient.HisSyncError = rawMessage.Length > 1000 ? rawMessage[..1000] : rawMessage;
            patient.ModifiedDate = _clock.UtcNow;
            patient.ModifiedBy = command.ActorId;
            patient.ModifiedActorKind = command.ActorKind;
            if (_uow != null)
            {
                try { await _uow.SaveChangesAsync(ct); } catch { }
            }

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
