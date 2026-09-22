using System;

namespace HealthExam.Domain.ExamForms;

/// <summary>
/// Cấu hình một bước ký của một bộ biểu mẫu KSK. Thay cho api/M02F01500/RSWByDocTypeID của HIS:
/// sau khi bỏ HIS khỏi đường ký thì bảng này chính là quy trình ký.
/// </summary>
public class SignStepMap
{
    public Guid ID { get; set; }
    public string DivisionID { get; set; } = "";
    public string VariantCode { get; set; } = "";
    public int SWStep { get; set; }

    /// <summary>Mục khám lâm sàng. Null ở bước kết luận vì bước đó không thuộc mục khám nào.</summary>
    public int? ItemGroupID { get; set; }

    public string StepName { get; set; } = "";
    public string SignTitle { get; set; } = "";
    public long SWRoleID { get; set; }

    /// <summary>1 ký số, 2 ký điện tử, 3 đóng dấu. Khớp SignType của sign-server.</summary>
    public int SignType { get; set; } = 1;

    /// <summary>1 vị trí chính xác, 2 theo chuỗi tìm kiếm, 3 người dùng chọn.</summary>
    public int SLType { get; set; } = 2;

    public string SearchPattern { get; set; } = "";
    public int SLPage { get; set; }
    public float SLX { get; set; }
    public float SLY { get; set; }
    public bool IsConclusionStep { get; set; }
    public bool IsActive { get; set; } = true;
}
