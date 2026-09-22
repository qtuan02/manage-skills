namespace HealthExam.Application.Patients;

/// <summary>
/// Tên hiển thị của mã quan hệ thân nhân (PatientRelative.RelationshipCode). Một bảng dùng chung
/// cho ExamRecordResult và PatientProfileResult — không để hai chỗ lệch chữ.
/// </summary>
public static class RelationshipNames
{
    public static string Of(string code) =>
        code?.ToUpperInvariant() switch
        {
            "FATHER" => "Cha",
            "MOTHER" => "Mẹ",
            "SPOUSE" => "Vợ-chồng",
            "CHILD" => "Con",
            "GUARDIAN" => "Người giám hộ",
            "OTHER" => "Khác",
            _ => code ?? ""
        };
}
