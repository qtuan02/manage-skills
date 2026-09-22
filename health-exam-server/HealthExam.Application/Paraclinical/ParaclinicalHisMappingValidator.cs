using System;
using System.Collections.Generic;
using System.Linq;
using HealthExam.Application.Common;

namespace HealthExam.Application.Paraclinical;

public sealed record ParaclinicalHisIdentity(
    long HisPtId,
    string HisPtCode,
    long HisAdmissionId,
    string HisAdmissionCode,
    long HisTreatmentProcessId,
    long PCReqDoctorId,
    int ReqDeptId);

public sealed record ParaclinicalServiceMappingItem(
    string LocalServiceCode,
    long HisMedSerId,
    string HisMedSerName,
    string ServiceKind);

public interface IHisServiceMappingResolver
{
    Task<ApplicationResult<long>> ResolveMedSerIdAsync(string divisionId, string localServiceCode, long existingServiceId, CancellationToken ct = default);
}

public static class ParaclinicalSubmitPreconditionValidator
{
    public static ApplicationResult<bool> Validate(
        ParaclinicalHisIdentity identity,
        IReadOnlyList<ParaclinicalServiceMappingItem> items)
    {
        var errors = new List<ApplicationValidationError>();

        if (identity == null)
        {
            errors.Add(new ApplicationValidationError("HisIdentity", "Thiếu thông tin định danh HIS"));
            return ApplicationResult<bool>.Fail(
                ApplicationFailureCode.SignPrecondition,
                "Không đủ điều kiện gửi chỉ định sang HIS do thiếu định danh",
                new ApplicationValidationErrors(errors));
        }

        if (identity.HisPtId <= 0 || string.IsNullOrWhiteSpace(identity.HisPtCode))
        {
            errors.Add(new ApplicationValidationError("HisPtId", "Thiếu mã hoặc ID người bệnh trên HIS"));
        }

        if (identity.HisAdmissionId <= 0)
        {
            errors.Add(new ApplicationValidationError("HisAdmissionId", "Thiếu lượt tiếp nhận AdmissionID trên HIS"));
        }

        if (identity.HisTreatmentProcessId <= 0)
        {
            errors.Add(new ApplicationValidationError("HisTreatmentProcessId", "Thiếu hồ sơ điều trị TreatmentProcessID (TPID) trên HIS"));
        }

        if (identity.PCReqDoctorId <= 0)
        {
            errors.Add(new ApplicationValidationError("PCReqDoctorId", "Thiếu bác sĩ chỉ định hợp lệ"));
        }

        if (identity.ReqDeptId <= 0)
        {
            errors.Add(new ApplicationValidationError("ReqDeptId", "Thiếu khoa phòng chỉ định hợp lệ"));
        }

        if (items == null || items.Count == 0)
        {
            errors.Add(new ApplicationValidationError("Items", "Phiếu chỉ định không có dịch vụ nào"));
        }
        else
        {
            for (var i = 0; i < items.Count; i++)
            {
                var item = items[i];
                if (item.HisMedSerId <= 0)
                {
                    errors.Add(new ApplicationValidationError(
                        $"Items[{i}].HisMedSerId",
                        $"Dịch vụ '{item.LocalServiceCode}' chưa được ánh xạ mã kỹ thuật HIS (MedSerID)",
                        i));
                }
            }
        }

        if (errors.Count > 0)
        {
            return ApplicationResult<bool>.Fail(
                ApplicationFailureCode.SignPrecondition,
                "Không đủ điều kiện gửi chỉ định sang HIS do thiếu thông tin định danh hoặc ánh xạ dịch vụ",
                new ApplicationValidationErrors(errors));
        }

        return ApplicationResult<bool>.Success(true);
    }
}
