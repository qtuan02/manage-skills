using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace HealthExam.Infrastructure.Integrations.HisEmr;

public sealed class HisPatientWireRequest
{
    [JsonProperty("PatientCode")] public string PatientCode { get; init; } = "";
    [JsonProperty("FirstName")] public string FirstName { get; init; } = "";
    [JsonProperty("LastName")] public string LastName { get; init; } = "";
    [JsonProperty("FullName")] public string FullName { get; init; } = "";
    [JsonProperty("I_Gender")] public byte I_Gender { get; init; }
    [JsonProperty("BirthDate")] public DateTime? BirthDate { get; init; }
    [JsonProperty("BirthYear")] public int BirthYear { get; init; }
    [JsonProperty("IDCard")] public string IDCard { get; init; } = "";
    [JsonProperty("MobileNo")] public string MobileNo { get; init; } = "";
    [JsonProperty("PersonalEmail")] public string PersonalEmail { get; init; } = "";
    [JsonProperty("CurrentAddress")] public string CurrentAddress { get; init; } = "";
}

public sealed class HisMedicalProcessWireRequest
{
    [JsonProperty("MedicalTypeCode")] public string MedicalTypeCode { get; init; } = "";
    [JsonProperty("MedicalTypeCodeOld")] public string MedicalTypeCodeOld { get; init; } = null;
    [JsonProperty("AdmissionID")] public long AdmissionID { get; init; }
}

public sealed class HisAdmissionWireRequest
{
    [JsonProperty("IsOutPatient")] public byte IsOutPatient { get; init; } = 2;
    [JsonProperty("AdmissionCode")] public string AdmissionCode { get; init; } = "";
    [JsonProperty("AdmissionDate")] public DateTime AdmissionDate { get; init; }
    [JsonProperty("DepartmentID")] public int DepartmentID { get; init; }
    [JsonProperty("DepartmentCode")] public string DepartmentCode { get; init; } = "";
    [JsonProperty("PatientCode")] public string PatientCode { get; init; } = "";
    [JsonProperty("FirstName")] public string FirstName { get; init; } = "";
    [JsonProperty("LastName")] public string LastName { get; init; } = "";
    [JsonProperty("FullName")] public string FullName { get; init; } = "";
    [JsonProperty("I_Gender")] public byte I_Gender { get; init; }
    [JsonProperty("BirthDate")] public DateTime? BirthDate { get; init; }
    [JsonProperty("BirthYear")] public int BirthYear { get; init; }
    [JsonProperty("IDCard")] public string IDCard { get; init; } = "";
    [JsonProperty("MobileNo")] public string MobileNo { get; init; } = "";
    [JsonProperty("PersonalEmail")] public string PersonalEmail { get; init; } = "";
    [JsonProperty("CurrentAddress")] public string CurrentAddress { get; init; } = "";
}

public sealed class HisEnvelope
{
    public int ErrorCode { get; set; }
    public string Message { get; set; } = "";
    public JToken Data { get; set; }
    public string TraceID { get; set; } = "";
}
