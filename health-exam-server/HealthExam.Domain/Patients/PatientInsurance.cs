using HealthExam.Domain.Catalogs;
using HealthExam.Domain.Common;

namespace HealthExam.Domain.Patients;

/// <summary>
/// HEX_PatientInsurance — thông tin BHYT theo từng phiên bản của người bệnh.
/// Bản ghi được registration tham chiếu là immutable; thay đổi tạo bản ghi mới.
/// </summary>
public sealed class PatientInsurance : AuditableEntity
{
    public Guid InsuranceRefID { get; set; }
    public string DivisionID { get; set; } = "";
    public Guid PatientRefID { get; set; }
    public string InsuranceNumber { get; set; } = "";
    public Guid? InsuranceObjectOptionID { get; set; }
    public Guid? RegistrationPlaceOptionID { get; set; }
    public DateOnly? ValidFrom { get; set; }
    public DateOnly? ValidTo { get; set; }
    public bool IsActive { get; set; } = true;

    public Patient Patient { get; set; }
    public MasterDataOption InsuranceObjectOption { get; set; }
    public MasterDataOption RegistrationPlaceOption { get; set; }
}
