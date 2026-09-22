using HealthExam.Domain.Common;

namespace HealthExam.Domain.Patients;

/// <summary>
/// HEX_PatientRelative — thông tin thân nhân của người bệnh theo từng phiên bản.
/// Bản ghi được registration tham chiếu là immutable; thay đổi tạo bản ghi mới.
/// </summary>
public sealed class PatientRelative : AuditableEntity
{
    public Guid RelativeRefID { get; set; }
    public string DivisionID { get; set; } = "";
    public Guid PatientRefID { get; set; }
    public string RelationshipCode { get; set; } = "";
    public Guid? RelationshipOptionID { get; set; }
    public string FullName { get; set; } = "";
    public string IdentityNumber { get; set; } = "";
    public string PhoneNumber { get; set; } = "";
    public bool IsActive { get; set; } = true;

    public Patient Patient { get; set; }
}
