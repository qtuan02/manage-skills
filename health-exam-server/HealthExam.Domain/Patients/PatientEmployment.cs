using HealthExam.Domain.Catalogs;
using HealthExam.Domain.Common;

namespace HealthExam.Domain.Patients;

/// <summary>
/// HEX_PatientEmployment — thông tin nghề nghiệp / đơn vị công tác theo từng phiên bản.
/// Bản ghi được registration tham chiếu là immutable; thay đổi tạo bản ghi mới.
/// </summary>
public sealed class PatientEmployment : AuditableEntity
{
    public Guid EmploymentRefID { get; set; }
    public string DivisionID { get; set; } = "";
    public Guid PatientRefID { get; set; }
    public Guid? OccupationOptionID { get; set; }
    public string StaffCode { get; set; } = "";
    public string OrgDeptName { get; set; } = "";
    public string JobTitle { get; set; } = "";
    public bool IsActive { get; set; } = true;

    public Patient Patient { get; set; }
    public MasterDataOption OccupationOption { get; set; }
}
