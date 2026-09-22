using System;

namespace HealthExam.Domain.ExamRecords;

public static class ExamRecordSignStepStatus
{
    /// <summary>Đang khám / đã lưu form nhưng chưa ký.</summary>
    public const string InProgress = "InProgress";

    /// <summary>Bác sĩ đã bấm ký, chưa đóng lên PDF.</summary>
    public const string Snapshot = "Snapshot";

    /// <summary>Đã đóng lên PDF thành công.</summary>
    public const string Signed = "Signed";

    public const string Failed = "Failed";
}

/// <summary>
/// Một lượt "bác sĩ X bấm Ký số ở mục Y lúc Z". Dòng chỉ sinh ra khi có người bấm ký —
/// không đổ sẵn theo quy trình. Sự tồn tại của dòng này CHÍNH LÀ khóa read-only của mục khám.
/// </summary>
public class ExamRecordSignStep
{
    public Guid ID { get; set; }
    public string DivisionID { get; set; } = "";
    public Guid RecordID { get; set; }
    public string VariantCode { get; set; } = "";

    /// <summary>Mục khám sinh ra snapshot này. Null ở bước kết luận.</summary>
    public int? ItemGroupID { get; set; }

    public int SWStep { get; set; }
    public string StepName { get; set; } = "";
    public long SWRoleID { get; set; }
    public string Status { get; set; } = ExamRecordSignStepStatus.Snapshot;
    public long? SignedByEmployeeID { get; set; }

    /// <summary>sign-server nhận empCode, và chứng thư số tra theo mã chứ không theo ID.</summary>
    public string SignedByEmployeeCode { get; set; } = "";

    /// <summary>Tên nhân viên lúc ký; sign-server in tên này cạnh chữ ký.</summary>
    public string SignedByEmployeeName { get; set; } = "";

    /// <summary>Người bấm ký (có thể khác người ký khi ký thay — PROJ-2374). Không dùng để tra chứng thư.</summary>
    public long? PerformedByEmployeeID { get; set; }

    public string PerformedByEmployeeName { get; set; } = "";

    public DateTime? SignedAt { get; set; }
    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;
    public DateTime ModifiedDate { get; set; } = DateTime.UtcNow;

    public ExamRecord Record { get; set; }
}
