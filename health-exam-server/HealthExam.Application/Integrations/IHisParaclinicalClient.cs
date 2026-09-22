using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace HealthExam.Application.Integrations;

// --- DTOs for HIS Paraclinical Integration ---

public sealed record HisAdmissionInfo(
    long AdmissionId,
    string AdmissionCode,
    long PatientId,
    string PatientCode,
    string FullName,
    int? DepartmentId,
    string DepartmentName,
    DateTime? AdmissionDate);

public sealed record HisMedicalServiceType(
    int ServiceTypeId,
    string ServiceTypeCode,
    string ServiceTypeName,
    string ParaclinicalKind);

public sealed record HisMedicalServiceItem(
    long MedSerId,
    string MedSerCode,
    string MedSerName,
    int ServiceTypeId,
    string ServiceTypeName,
    decimal Price,
    string Unit);

public sealed record HisClinicalRequestItem(
    long MedSerId,
    int Quantity = 1,
    string Note = "");

public sealed record HisCreateClinicalRequest(
    long PtId,
    string PtCode,
    long AdmissionId,
    string AdmissionCode,
    long TreatmentProcessId,
    long DoctorId,
    int DepartmentId,
    string Note,
    IReadOnlyList<HisClinicalRequestItem> Items);

public sealed record HisClinicalRequestDetailItem(
    long ParaClinReqDtlId,
    long MedSerId,
    string MedSerCode,
    string MedSerName,
    int Quantity,
    int Status,
    long? ParaClinProcessId);

public sealed record HisClinicalRequestSummary(
    long ParaClinReqId,
    string ParaClinReqCode,
    long AdmissionId,
    string AdmissionCode,
    long PtId,
    string PtCode,
    int Status,
    DateTime? RequestDate,
    IReadOnlyList<HisClinicalRequestDetailItem> Details);

public sealed record HisConnectTreatmentProcessRequest(
    long ParaClinReqId,
    long TreatmentProcessId,
    IReadOnlyList<long> ParaClinReqDtlIds = null);

public sealed record HisConnectTreatmentProcessResult(
    long ParaClinProcessId,
    string Message = "");

public sealed record HisCancelConnectTreatmentProcessRequest(
    long ParaClinProcessId,
    string Reason = "");

public sealed record HisParaclinicalResultDetail(
    string HisDetailId,
    string ServiceCode,
    string ServiceName,
    string Value,
    string Text,
    string Unit,
    string ReferenceRange,
    string AbnormalFlag);

public sealed record HisParaclinicalReport(
    string HisResultId,
    long? ParaClinReqId,
    long? ParaClinProcessId,
    long? AdmissionId,
    string Status,
    DateTime ResultDate,
    string Conclusion,
    string DoctorName,
    IReadOnlyList<HisParaclinicalResultDetail> Details);

public interface IHisParaclinicalClient
{
    Task<HisClientResult<HisAdmissionInfo>> GetAdmissionInfoAsync(
        long admissionId,
        string credential = null,
        CancellationToken ct = default);

    Task<HisClientResult<IReadOnlyList<HisMedicalServiceType>>> GetListMedSerTypeAsync(
        string credential = null,
        CancellationToken ct = default);

    Task<HisClientResult<IReadOnlyList<HisMedicalServiceItem>>> GetListMedicalServiceItemAsync(
        int? serviceTypeId = null,
        string credential = null,
        CancellationToken ct = default);

    Task<HisClientResult<HisClinicalRequestSummary>> CreateClinicalRequestAsync(
        HisCreateClinicalRequest request,
        string credential = null,
        CancellationToken ct = default);

    Task<HisClientResult<HisClinicalRequestSummary>> GetClinicalRequestAsync(
        long paraClinReqId,
        string credential = null,
        CancellationToken ct = default);

    Task<HisClientResult<IReadOnlyList<HisClinicalRequestSummary>>> GetClinicalRequestsByAdmissionAsync(
        long admissionId,
        string credential = null,
        CancellationToken ct = default);

    Task<HisClientResult<bool>> DeleteClinicalRequestAsync(
        long paraClinReqId,
        string reason = "",
        string credential = null,
        CancellationToken ct = default);

    Task<HisClientResult<HisConnectTreatmentProcessResult>> CareProcessConnectTPAsync(
        HisConnectTreatmentProcessRequest request,
        string credential = null,
        CancellationToken ct = default);

    Task<HisClientResult<bool>> CareProcessConnectCancelTPAsync(
        HisCancelConnectTreatmentProcessRequest request,
        string credential = null,
        CancellationToken ct = default);

    Task<HisClientResult<IReadOnlyList<HisParaclinicalReport>>> GetParaClinicalByAdmissionAsync(
        long admissionId,
        string credential = null,
        CancellationToken ct = default);

    Task<HisClientResult<IReadOnlyList<HisParaclinicalReport>>> GetParaClinicalResultByAdmissionAsync(
        long admissionId,
        string credential = null,
        CancellationToken ct = default);

    Task<HisClientResult<HisParaclinicalReport>> GetParaClinicalByIdAsync(
        string hisResultId,
        string credential = null,
        CancellationToken ct = default);
}
