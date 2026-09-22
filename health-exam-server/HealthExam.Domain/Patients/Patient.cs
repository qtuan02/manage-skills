using HealthExam.Domain.Catalogs;
using HealthExam.Domain.Common;

namespace HealthExam.Domain.Patients;

/// <summary>
/// HEX_Patient — nguồn sự thật nội bộ của người bệnh (3NF).
/// HisPatientID là mã tham chiếu downstream từ HIS; unique theo (DivisionID, HisPatientID) khi HisPatientID > 0.
/// </summary>
public sealed class Patient : AuditableEntity
{
    public Guid PatientRefID { get; set; }
    public string DivisionID { get; set; } = "";
    public long? HisPatientID { get; set; }
    public string PatientCode { get; set; } = "";
    public string FullName { get; set; } = "";
    public DateOnly? Dob { get; set; }
    public short? BirthYear { get; set; }
    public short GenderID { get; set; }
    public string IdentityNumber { get; set; } = "";
    public DateOnly? IdentityIssuedDate { get; set; }
    public Guid? IdentityIssuerOptionID { get; set; }
    public string PhoneNumber { get; set; } = "";
    public string Email { get; set; } = "";
    public string Address { get; set; } = "";
    public string ProvinceCode { get; set; } = "";
    public string WardCode { get; set; } = "";
    public Guid? EthnicityOptionID { get; set; }
    public string BloodAboCode { get; set; } = "";
    public string BloodRhCode { get; set; } = "";
    public string HisSyncStatus { get; set; } = "Pending";
    public string HisSyncError { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public Guid ProfileLineageID { get; set; }
    public int VersionNumber { get; set; } = 1;
    public Guid? PreviousPatientRefID { get; set; }
    public Patient PreviousPatient { get; set; }

    public MasterDataOption IdentityIssuerOption { get; set; }
    public MasterDataOption EthnicityOption { get; set; }
    public ICollection<PatientInsurance> Insurances { get; set; } = new List<PatientInsurance>();
    public ICollection<PatientEmployment> Employments { get; set; } = new List<PatientEmployment>();
    public ICollection<PatientRelative> Relatives { get; set; } = new List<PatientRelative>();
}
