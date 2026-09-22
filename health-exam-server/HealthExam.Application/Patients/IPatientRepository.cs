using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using HealthExam.Domain.Common;
using HealthExam.Domain.Patients;

namespace HealthExam.Application.Patients;

public interface IPatientRepository
{
    Task<Patient> FindForWriteAsync(string divisionId, Guid? patientRefID, long? hisPatientID, CancellationToken ct = default);
    Task<Patient> FindActiveByIdentityNumberAsync(string divisionId, string identityNumber, CancellationToken ct = default);
    Task<Patient> FindActiveByRefIdAsync(string divisionId, Guid patientRefID, CancellationToken ct = default);

    /// <summary>
    /// Phiên bản active của PatientRefID trong đơn vị, nạp sẵn IdentityIssuerOption/EthnicityOption,
    /// kèm dòng BHYT / nghề nghiệp / thân nhân theo HỒ SƠ KHÁM GẦN NHẤT của phiên bản đó
    /// (CreatedDate DESC; ref null nghĩa là lần đó không khai). Chưa có hồ sơ khám nào thì lấy
    /// dòng active mới nhất theo CreatedDate. null = không active hoặc khác đơn vị.
    /// </summary>
    Task<PatientProfileSnapshot> FindActiveProfileAsync(string divisionId, Guid patientRefID, CancellationToken ct = default);

    /// <summary>
    /// Trả ProfileLineageID của một PatientRefID bất kỳ trong đơn vị — kể cả phiên bản đã
    /// inactive. Đây là cách duy nhất đi từ "một phiên bản hồ sơ" sang "người đó": hồ sơ khám
    /// cũ neo vào phiên bản tại thời điểm khám, nên lọc IsActive ở đây sẽ cắt mất lịch sử.
    /// null = không có hàng nào khớp trong đơn vị này.
    /// </summary>
    Task<Guid?> FindProfileLineageIdAsync(string divisionId, Guid patientRefID, CancellationToken ct = default);
    Task<IReadOnlyList<Patient>> SearchActiveAsync(string divisionId, PatientSearchCriteria criteria, int limit, CancellationToken ct = default);
    Task<int?> AllocateNextVersionAsync(string divisionId, Guid profileLineageID, Guid sourcePatientRefID, CancellationToken ct = default);
    Task<bool> DeactivateAsync(string divisionId, Guid patientRefID, DateTime modifiedDate, long actorId, ActorKind actorKind, CancellationToken ct = default);
    Task<PatientInsurance> FindActiveInsuranceAsync(string divisionId, Guid patientRefID, PatientInsuranceWrite value, CancellationToken ct = default);
    Task<PatientEmployment> FindActiveEmploymentAsync(string divisionId, Guid patientRefID, PatientEmploymentWrite value, CancellationToken ct = default);
    Task<PatientRelative> FindActiveRelativeAsync(string divisionId, Guid patientRefID, PatientRelativeWrite value, CancellationToken ct = default);
    void Add(Patient entity);
    void Add(PatientInsurance entity);
    void Add(PatientEmployment entity);
    void Add(PatientRelative entity);
}
