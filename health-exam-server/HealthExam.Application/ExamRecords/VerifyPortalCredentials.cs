using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using HealthExam.Application.Common;

namespace HealthExam.Application.ExamRecords;

public interface IVerifyPortalCredentialsHandler
{
    Task<ApplicationResult<PortalCredentialsResult>> HandleAsync(
        VerifyPortalCredentialsCommand command, CancellationToken ct = default);
}

public sealed class VerifyPortalCredentialsHandler : IVerifyPortalCredentialsHandler
{
    private readonly IExamRecordRepository _recordRepository;

    public VerifyPortalCredentialsHandler(IExamRecordRepository recordRepository)
    {
        _recordRepository = recordRepository;
    }

    public async Task<ApplicationResult<PortalCredentialsResult>> HandleAsync(
        VerifyPortalCredentialsCommand command, CancellationToken ct = default)
    {
        var patientCode = (command.PatientCode ?? "").Trim();
        var identityNumber = (command.IdentityNumber ?? "").Trim();
        var insuranceNumber = (command.InsuranceNumber ?? "").Trim();

        var errors = new List<ApplicationValidationError>();
        if (patientCode.Length == 0)
            errors.Add(new ApplicationValidationError(nameof(command.PatientCode), "Bỏ trống (bắt buộc)"));
        if (identityNumber.Length == 0 && insuranceNumber.Length == 0)
        {
            errors.Add(new ApplicationValidationError(
                nameof(command.IdentityNumber),
                "Bỏ trống (bắt buộc ít nhất một trong IdentityNumber / InsuranceNumber)"));
        }

        if (errors.Count > 0)
        {
            return ApplicationResult<PortalCredentialsResult>.Fail(
                ApplicationFailureCode.BadRequest, "Dữ liệu không hợp lệ", new ApplicationValidationErrors(errors));
        }

        var matched = await _recordRepository.VerifyPortalCredentialsAsync(
            command.DivisionId, patientCode, identityNumber, insuranceNumber, ct);

        if (matched == null)
        {
            return ApplicationResult<PortalCredentialsResult>.Success(PortalCredentialsResult.Invalid());
        }

        return ApplicationResult<PortalCredentialsResult>.Success(new PortalCredentialsResult(
            true,
            matched.RecordCode,
            matched.SessionCode,
            matched.FullName));
    }
}
