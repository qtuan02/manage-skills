using HealthExam.Application.Common;
using HealthExam.Domain.Common;

namespace HealthExam.API.Contracts;

/// <summary>Kết quả pha MỘT: đã kiểm tra từng dòng, CHƯA ghi hồ sơ nào — 02-api-spec §3.3.</summary>
public class ImportBatchItem
{
    public Guid ImportID { get; set; }
    public Guid SessionID { get; set; }
    public string SessionCode { get; set; } = "";
    public string FileName { get; set; } = "";
    public string SheetName { get; set; } = "";

    public int TotalRows { get; set; }
    public int ValidRows { get; set; }
    public int InvalidRows { get; set; }

    public ImportBatchState State { get; set; }
    public string StateName { get; set; } = "";

    public DateTime StartedAt { get; set; }
    public DateTime? FinishedAt { get; set; }

    /// <summary>
    /// Hạn dùng của lô. Quá hạn thì commit bị từ chối — 02-api-spec §3.3 chốt tạm 60 phút
    /// (Q-HEX-04). Trả ra để FE hiện đồng hồ đếm ngược thay vì để người dùng bấm xác nhận
    /// rồi mới biết lô đã hết hạn.
    /// </summary>
    public DateTime ExpiresAt { get; set; }

    public string ErrorSummary { get; set; } = "";

    /// <summary>Số hồ sơ đã ghi. 0 chừng nào chưa commit.</summary>
    public int CreatedRecordCount { get; set; }

    /// <summary>
    /// Số dòng ĐẠT nhưng thiếu cả CCCD lẫn số BHYT ⇒ những người này KHÔNG đăng nhập được
    /// cổng người bệnh, chỉ đến khám tại quầy. Không phải lỗi — xem ImportRowWarnings.
    ///
    /// Đếm trên TOÀN lô chứ không theo trang: con số này là thứ người nạp file phải nhìn
    /// thấy ngay ở màn xác nhận, mà màn đó chỉ hiện trang đầu.
    /// </summary>
    public int NoPortalCredentialRows { get; set; }

    /// <summary>Trang dòng hiện tại. NULL ở phản hồi của commit (commit trả tổng kết, không trả bảng).</summary>
    public PaginationData<ImportRowItem> Rows { get; set; }

    /// <summary>true khi commit được gọi lại trên lô ĐÃ nạp — xem ExamImportService.CommitAsync.</summary>
    public bool AlreadyCommitted { get; set; }
}

/// <summary>Một dòng Excel kèm kết quả kiểm tra.</summary>
public class ImportRowItem
{
    /// <summary>Số dòng trong file, tính cả dòng tiêu đề — mở Excel ra là thấy đúng dòng này.</summary>
    public int Row { get; set; }

    public string PatientCode { get; set; } = "";
    public string FullName { get; set; } = "";

    /// <summary>Valid | Invalid | Duplicate — 02-api-spec §3.3.</summary>
    public string Status { get; set; } = "";

    /// <summary>REQUIRED | BAD_FORMAT | DUPLICATE | UNKNOWN_VARIANT | UNKNOWN. Rỗng khi dòng đạt.</summary>
    public string ErrorCode { get; set; } = "";

    public List<ValidationError> Errors { get; set; } = new();

    /// <summary>
    /// NO_PORTAL_CREDENTIAL — xem ImportRowWarnings. Rỗng ở dòng không có gì phải lưu ý.
    ///
    /// Tách khỏi <see cref="Errors"/> có chủ ý: dòng mang cảnh báo VẪN được nạp, và FE phải
    /// hiện nó khác màu với dòng hỏng. Trộn vào Errors là biến một chuyện "nên biết" thành
    /// một chuyện "phải sửa", rồi người dùng bấm SkipInvalidRows để đi tiếp.
    /// </summary>
    public List<string> Warnings { get; set; } = new();

    /// <summary>Nguyên dòng gốc, khoá = tên cột trong file. Để FE dựng lại file lỗi cho người dùng sửa.</summary>
    public Dictionary<string, string> RawData { get; set; } = new();

    /// <summary>Dòng này đã sinh ra hồ sơ nào. NULL chừng nào chưa commit.</summary>
    public Guid? RecordID { get; set; }
}

/// <summary>Giá trị trường Status của một dòng.</summary>
public static class ImportRowStatus
{
    public const string Valid = "Valid";
    public const string Invalid = "Invalid";
    public const string Duplicate = "Duplicate";
}

/// <summary>Body của POST /v1/imports/{importId}/commit.</summary>
public class ImportCommitRequest
{
    /// <summary>
    /// true = nạp các dòng đạt, bỏ qua dòng hỏng. false = có một dòng hỏng thì không nạp gì cả.
    ///
    /// Mặc định false có chủ ý: người dùng phải TỰ CHỌN bỏ qua dòng hỏng. Mặc định true thì
    /// bấm nhầm nút xác nhận là 40 người biến mất khỏi danh sách mà không ai nhận ra.
    /// </summary>
    public bool SkipInvalidRows { get; set; }
}
