using HealthExam.Domain.Common;
using HealthExam.Domain.ExamSessions;

namespace HealthExam.Domain.Imports;

/// <summary>
/// HEX_ImportBatch — một lô nạp Excel danh sách đăng ký (01-db-model §7, UC03.5).
///
/// Lô tồn tại vì việc nạp có HAI PHA. FRD bắt người dùng xem lỗi từng dòng rồi mới nạp; một
/// pha (upload là ghi luôn) dẫn tới cảnh nạp 300 dòng, 40 dòng hỏng, và không có cách nào lùi
/// lại ngoài xoá tay 260 hồ sơ đã vào. Hàng này chính là "kết quả kiểm tra" được giữ lại giữa
/// hai pha, và <see cref="BatchID"/> là thứ làm cho pha hai idempotent.
/// </summary>
public class ImportBatch : AuditableEntity
{
    public Guid BatchID { get; set; }
    public string DivisionID { get; set; } = "";
    public Guid SessionID { get; set; }

    public string FileName { get; set; } = "";

    /// <summary>
    /// Object key của file gốc trên MinIO. CHƯA dùng ở phase này — bản gốc chưa được lưu ra
    /// kho, vì RawData từng dòng đã đủ để dựng lại file lỗi. Giữ cột để khỏi phải migrate lần
    /// nữa khi bật lưu file thật (tra soát pháp lý cần đúng file đơn vị đã gửi).
    /// </summary>
    public string StoragePath { get; set; } = "";

    public string SheetName { get; set; } = "";

    public int TotalRow { get; set; }

    /// <summary>Số dòng KIỂM TRA ĐẠT ở pha một. KHÔNG phải số hồ sơ đã ghi — xem <see cref="CreatedRecordCount"/>.</summary>
    public int SuccessRow { get; set; }

    public int ErrorRow { get; set; }

    public ImportBatchState State { get; set; } = ImportBatchState.Pending;

    public DateTime StartedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Thời điểm chốt (commit) hoặc bỏ lô. NULL khi lô còn đang chờ xác nhận.</summary>
    public DateTime? FinishedAt { get; set; }

    public string ErrorSummary { get; set; } = "";

    /// <summary>
    /// Số hồ sơ THẬT SỰ đã ghi ở pha hai. Tách khỏi SuccessRow vì hai con số lệch nhau một
    /// cách bình thường: commit với SkipInvalidRows=true bỏ qua dòng hỏng, và một dòng đạt ở
    /// pha một vẫn có thể trùng ở pha hai (ai đó nhập tay đúng người đó xen vào giữa hai pha).
    /// </summary>
    public int CreatedRecordCount { get; set; }

    public ExamSession Session { get; set; }
    public ICollection<ImportBatchRow> Rows { get; set; }

    public static readonly TimeSpan BatchLifetime = TimeSpan.FromMinutes(60);
    public static readonly TimeSpan StaleCommitAfter = TimeSpan.FromMinutes(5);

    public DomainResult BeginCommit(DateTime utcNow, TimeSpan? staleThreshold = null)
    {
        if (State == ImportBatchState.Completed)
            return DomainResult.NoOp();

        if (State == ImportBatchState.Discarded)
            return DomainResult.Reject(DomainFailureCode.InvalidTransition, "Lô nạp đã bị bỏ, không nạp lại được");

        if (utcNow > StartedAt + BatchLifetime)
            return DomainResult.Reject(DomainFailureCode.InvalidState, $"Lô nạp đã quá hạn {BatchLifetime.TotalMinutes:0} phút, vui lòng tải lên lại");

        if (State == ImportBatchState.Committing)
        {
            var threshold = staleThreshold ?? StaleCommitAfter;
            if (ModifiedDate >= utcNow - threshold)
                return DomainResult.Reject(DomainFailureCode.InvalidState, "Lô nạp đang được ghi bởi một lượt khác, vui lòng đợi rồi mở lại để xem kết quả");

            State = ImportBatchState.Committing;
            ModifiedDate = utcNow;
            return DomainResult.Apply();
        }

        if (State == ImportBatchState.Pending)
        {
            State = ImportBatchState.Committing;
            ModifiedDate = utcNow;
            return DomainResult.Apply();
        }

        return DomainResult.Reject(DomainFailureCode.InvalidTransition, $"Không thể bắt đầu chốt lô khi ở trạng thái \"{State}\"");
    }

    public DomainResult CompleteCommit(DateTime utcNow)
    {
        if (State == ImportBatchState.Completed)
            return DomainResult.NoOp();

        if (State != ImportBatchState.Committing)
            return DomainResult.Reject(DomainFailureCode.InvalidTransition, $"Không thể hoàn thành chốt lô khi ở trạng thái \"{State}\"");

        State = ImportBatchState.Completed;
        FinishedAt = utcNow;
        ModifiedDate = utcNow;
        return DomainResult.Apply();
    }

    public DomainResult Discard(DateTime utcNow, TimeSpan? staleThreshold = null)
    {
        if (State == ImportBatchState.Completed)
            return DomainResult.Reject(DomainFailureCode.InvalidState, "Lô đã nạp xong, không bỏ được. Muốn gỡ thì huỷ từng hồ sơ đã tạo.");

        if (State == ImportBatchState.Discarded)
            return DomainResult.NoOp();

        if (State == ImportBatchState.Committing)
        {
            var threshold = staleThreshold ?? StaleCommitAfter;
            if (ModifiedDate > utcNow - threshold)
                return DomainResult.Reject(DomainFailureCode.InvalidState, $"Lô đang được ghi, không bỏ được. Đợi lượt ghi kết thúc (tối đa {threshold.TotalMinutes:0} phút) rồi mở lại để xem kết quả.");
        }

        State = ImportBatchState.Discarded;
        FinishedAt = utcNow;
        ModifiedDate = utcNow;
        return DomainResult.Apply();
    }

    public DomainResult ReleaseClaim(DateTime utcNow)
    {
        if (State != ImportBatchState.Committing)
            return DomainResult.NoOp();

        State = ImportBatchState.Pending;
        ModifiedDate = utcNow;
        return DomainResult.Apply();
    }
}

/// <summary>
/// HEX_ImportBatchRow — một dòng của file Excel kèm kết quả kiểm tra (01-db-model §7).
///
/// ★ <see cref="RawData"/> giữ NGUYÊN dòng gốc. Người dùng nạp file 500 dòng, hỏng 12; cách
/// duy nhất tử tế là trả lại đúng 12 dòng đó kèm lý do để họ sửa rồi nạp lại — muốn vậy phải
/// còn nội dung gốc, kể cả phần không parse được.
/// </summary>
public class ImportBatchRow
{
    public long ImportRowID { get; set; }
    public Guid BatchID { get; set; }

    /// <summary>Số dòng TRONG FILE, tính cả dòng tiêu đề — để người dùng mở Excel ra là thấy đúng dòng đó.</summary>
    public int RowNo { get; set; }

    /// <summary>jsonb, khoá = tên cột như trong file. Nguyên văn, không chuẩn hoá.</summary>
    public string RawData { get; set; } = "{}";

    public bool IsValid { get; set; }

    /// <summary>REQUIRED | BAD_FORMAT | DUPLICATE | UNKNOWN_VARIANT | UNKNOWN — xem ImportRowErrors.</summary>
    public string ErrorCode { get; set; } = "";

    public string ErrorMessage { get; set; } = "";

    /// <summary>
    /// Dòng này đã sinh ra hồ sơ nào (01-db-model §5: truy ngược dòng ↔ hồ sơ). Cũng là thứ
    /// làm cho commit idempotent kiểm được: đã có RecordID thì lần gọi thứ hai không ghi lại.
    /// </summary>
    public Guid? RecordID { get; set; }

    public ImportBatch Batch { get; set; }
}

/// <summary>Giá trị cột ErrorCode của từng dòng — hằng số để FE nhóm lỗi mà không so chuỗi tiếng Việt.</summary>
public static class ImportRowErrors
{
    public const string Required = "REQUIRED";
    public const string BadFormat = "BAD_FORMAT";
    public const string Duplicate = "DUPLICATE";
    public const string UnknownVariant = "UNKNOWN_VARIANT";

    /// <summary>
    /// Ô dài hơn trần của cột (RecordFieldLengths). Tách khỏi BAD_FORMAT vì cách sửa khác
    /// hẳn: BAD_FORMAT là "gõ sai kiểu", còn TOO_LONG là "đúng kiểu nhưng phải cắt bớt", và
    /// người sửa file cần biết cắt còn bao nhiêu ký tự.
    /// </summary>
    public const string TooLong = "TOO_LONG";

    /// <summary>
    /// Tầng ghi từ chối dòng này vì lý do không đoán trước được ở pha 1 (ràng buộc DB nào đó
    /// mới thêm). Có mã riêng để nó KHÔNG bị đọc nhầm là lỗi dữ liệu của đơn vị gửi file:
    /// dòng mang mã này là dấu hiệu pha 1 còn thiếu một luật, tức việc của người viết code.
    /// </summary>
    public const string WriteFailed = "WRITE_FAILED";

    /// <summary>
    /// Người này đã có hồ sơ trong đợt — phát hiện ở PHA 2, không phải pha 1. Tách khỏi
    /// <see cref="Duplicate"/> vì hai mã trả lời hai câu hỏi khác nhau: DUPLICATE là "file
    /// có hai dòng cùng một người", còn mã này là "có người nhập tay xen vào giữa hai pha".
    /// Phân biệt được là điều kiện để <see cref="IsCommitPhase"/> biết dòng nào KHÔNG được
    /// chặn một lượt commit chạy lại — xem ExamImportService.CommitAsync.
    /// </summary>
    public const string DuplicateAtCommit = "DUPLICATE_AT_COMMIT";

    public const string Unknown = "UNKNOWN";

    /// <summary>
    /// Dòng này hỏng ở PHA 2 (lúc ghi) chứ không phải pha 1 (lúc kiểm file).
    ///
    /// Vì sao phải phân biệt: cờ SkipInvalidRows là câu trả lời cho câu hỏi "file của anh có
    /// dòng hỏng, vẫn nạp phần còn lại chứ?" — một câu hỏi về NỘI DUNG FILE. Dòng hỏng lúc
    /// ghi thì người dùng đã trả lời câu đó rồi, ở lượt commit đầu tiên. Đếm chúng vào phép
    /// kiểm ấy nghĩa là một lượt commit đứt giữa chừng biến lô thành lô không bao giờ khép
    /// lại được bằng tham số mặc định (§3b(ii), review lần 2 MR !4).
    /// </summary>
    public static bool IsCommitPhase(string errorCode)
        => errorCode == WriteFailed || errorCode == DuplicateAtCommit;
}

/// <summary>
/// Cảnh báo trên một dòng — dòng VẪN ĐẠT và vẫn được nạp. Khác hẳn ErrorCode.
///
/// Vì sao cần một khái niệm riêng thay vì thêm mã lỗi: hồ sơ chỉ đến khám tại quầy, không
/// dùng cổng người bệnh, là nghiệp vụ HỢP LỆ — chặn nó là bắt cả file 500 dòng hỏng vì một
/// cột mà đơn vị ký hợp đồng không phải lúc nào cũng có. Nhưng im lặng cũng sai: đơn vị tưởng
/// cả 500 người tra cứu được, tới lúc người bệnh gọi tổng đài mới vỡ ra, và lúc đó không ai
/// truy ngược được dòng Excel nào thiếu gì.
/// </summary>
public static class ImportRowWarnings
{
    /// <summary>
    /// Thiếu CẢ CCCD lẫn số BHYT ⇒ người này không đăng nhập được cổng người bệnh.
    ///
    /// Vì sao là "cả hai" chứ không phải "một trong hai": cổng NB đối chiếu mã người bệnh rồi
    /// so tiếp MỘT dữ kiện định danh, và phép so đó từ chối mọi giá trị lưu rỗng
    /// (iam-server PortalCredentialService.EqualsStored). Có một trong hai là đủ để đăng nhập.
    /// </summary>
    public const string NoPortalCredential = "NO_PORTAL_CREDENTIAL";
}
