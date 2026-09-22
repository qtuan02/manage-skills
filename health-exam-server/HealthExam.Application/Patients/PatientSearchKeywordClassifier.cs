using System.Collections.Generic;
using System.Linq;

namespace HealthExam.Application.Patients;

/// <summary>Trường được dùng để tìm hồ sơ người bệnh.</summary>
public enum PatientSearchField
{
    Identity,
    Phone,
    Code,
    Name
}

/// <summary>
/// Chế độ "tự nhận diện": đoán người dùng đang gõ CCCD, SĐT, mã BN hay tên.
/// Mã BN không có format cố định (nhập tay hoặc "HEX-&lt;recordCode&gt;") nên khi mơ hồ
/// thì gộp nhiều trường thay vì đoán sai.
/// </summary>
public static class PatientSearchKeywordClassifier
{
    public static IReadOnlyList<PatientSearchField> Classify(string keyword)
    {
        var kw = (keyword ?? "").Trim();
        var allDigits = kw.Length > 0 && kw.All(char.IsDigit);

        if (!allDigits)
        {
            return new[] { PatientSearchField.Name, PatientSearchField.Code };
        }

        if (kw.Length == 9 || kw.Length == 12)
        {
            return new[] { PatientSearchField.Identity };
        }

        if (kw.Length == 10 && kw[0] == '0')
        {
            return new[] { PatientSearchField.Phone };
        }

        return new[] { PatientSearchField.Identity, PatientSearchField.Phone, PatientSearchField.Code };
    }
}
