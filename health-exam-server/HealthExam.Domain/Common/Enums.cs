namespace HealthExam.Domain.Common;

/// <summary>
/// Chủ thể thao tác. Dùng CHUNG bảng số với form-server (Form.Core/Models/Enums.cs) —
/// hai service ghi audit cùng hình dạng thì đọc chéo nhật ký không phải map.
/// </summary>
public enum ActorKind : short
{
    Employee = 1,
    Patient = 2,
    System = 3,
    Integration = 4
}

/// <summary>
/// Trạng thái HỒ SƠ của một người trong đợt — 01-db-model §2.2, chốt cứng theo FRD UC03/UC05.
///
/// ⚠️ KHÁC <c>SectionState</c> của form-server: "Đang khám" ở đây là hồ sơ của MỘT NGƯỜI,
/// còn bên kia là MỘT NHÓM CHỈ TIÊU. Trùng chữ, khác tầng.
/// </summary>
public enum ExamRecordState : short
{
    NotRegistered = 0,  // Chưa đăng ký
    Waiting = 1,        // Chờ khám
    InProgress = 2,     // Đang khám
    Completed = 3,      // Đã khám
    RegistrationCancelled = 4, // Hủy đăng ký
    ExamCancelled = 5   // Hủy khám
}

public static class ExamRecordStates
{
    public static bool IsCancelled(ExamRecordState state)
        => state is ExamRecordState.RegistrationCancelled or ExamRecordState.ExamCancelled;
}

/// <summary>
/// Trạng thái ĐỢT khám — 01-db-model §2.2 ghi rõ đây là "chốt tạm" (Q-DB-01): FRD chỉ nói
/// "trạng thái đợt" mà chưa liệt kê. Giữ nguyên bảng số của tài liệu để khi BA chốt thì chỉ
/// phải thêm giá trị, không phải đánh số lại.
/// </summary>
public enum ExamSessionState : short
{
    Draft = 0,       // Nháp
    Open = 1,        // Đang mở
    InProgress = 2,  // Đang khám
    Closed = 3,      // Đã đóng
    Cancelled = 4    // Hủy
}

/// <summary>
/// Trạng thái một lô nạp Excel — 01-db-model §2.2 (bảng ImportBatchState), giữ nguyên bảng số.
///
/// Hai pha của UC03.5 nằm ở đây: lô sinh ra ở <c>Pending</c> (đã kiểm tra từng dòng nhưng CHƯA
/// ghi hồ sơ nào), chỉ sang <c>Completed</c> khi người dùng bấm xác nhận.
/// </summary>
public enum ImportBatchState : short
{
    Pending = 0,    // Đang xử lý — đã upload & kiểm tra, chờ người dùng xác nhận
    Completed = 1,  // Hoàn tất — đã ghi hồ sơ
    Failed = 2,     // Lỗi
    Discarded = 3,  // Đã hủy — người dùng bỏ lô, hoặc lô quá hạn bị dọn

    /// <summary>
    /// ★ Đang ghi hồ sơ — GIÁ TRỊ MỚI, thêm sau review MR !4 (F1).
    ///
    /// Vì sao phải có: việc ghi KHÔNG nằm trọn trong một transaction (mỗi hồ sơ một
    /// transaction riêng, xem ExamImportService.CommitAsync), nên "đã commit chưa" không thể
    /// đọc bằng <c>State == Completed</c> — cờ đó chỉ được đặt SAU cả vòng lặp. Cửa sổ giữa
    /// hai mốc là 4,8 giây cho 260 dòng, và trong cửa sổ đó hai lượt commit song song đều
    /// thấy "chưa commit" rồi cùng ghi: đo thật ra 59 hồ sơ từ một file 30 dòng, không lỗi
    /// nào báo ra.
    ///
    /// Trạng thái này được đặt bằng MỘT câu UPDATE có điều kiện (WHERE State = Pending), tức
    /// đúng một lượt giành được. Tiến trình chết giữa chừng thì lô nằm lại ở đây và được
    /// nhặt lại sau <c>ExamImportService.StaleCommitAfter</c> — đó là lý do bảng số nhận
    /// thêm một giá trị thay vì mượn tạm Failed.
    /// </summary>
    Committing = 4
}

/// <summary>
/// Trạng thái xử lý một dòng HEX_WebhookInbox — 01-db-model §8, giữ nguyên bảng số.
///
/// Không có giá trị "đang xử lý": worker giữ khoá hàng bằng transaction
/// (SELECT … FOR UPDATE SKIP LOCKED) trong suốt lúc xử lý. Đặt cờ "đang xử lý" rồi mới xử lý
/// thì tiến trình chết giữa chừng để lại một hàng kẹt cờ mà không ai gỡ; còn khoá hàng thì
/// tiến trình chết là transaction rollback, hàng tự về New và lượt sau nhặt lại.
/// </summary>
public enum WebhookProcessState : short
{
    New = 0,        // Mới nhận, chờ worker
    Processed = 1,  // Đã áp hệ quả
    Failed = 2,     // Lỗi — còn lượt thì thử lại, hết lượt thì nằm lại làm dead-letter
    Skipped = 3     // Cố ý bỏ qua: sự kiện lạ, sự kiện đến muộn, hoặc chuyển tiếp không hợp lệ
}

/// <summary>
/// Vòng đời MỘT DÒNG DỊCH VỤ cận lâm sàng — 01-db-model §2.4, FRD UC05.2.
///
/// Vòng đời là của DÒNG DỊCH VỤ chứ không phải của phiếu: kết quả về lẻ tẻ từng dịch vụ, và
/// điều kiện (B) đếm theo dịch vụ. Phiếu chỉ mang <see cref="OrderSentStatus"/> — đã gửi sang
/// LIS/PACS/RIS hay chưa.
///
/// ⚠️ <c>Hủy(4)</c> là "chốt tạm" của 01-db-model §13 Q-DB-02, giữ nguyên bảng số của tài liệu.
///
/// 🔴 VENDOR KHÔNG BAO GIỜ ĐẨY ĐƯỢC LÊN <c>Done(3)</c> — đo được, không phải phỏng đoán:
/// pacs-connect-server (M07F99020Commands.cs:55-58) chặn cứng mọi <c>NewStatus</c> ngoài
/// {0,1}, tức RIS chỉ nói được tới "đang thực hiện". Vì thế đường ĐƯA LÊN 3 chỉ có hai:
/// sự kiện đính kèm <c>SCAN_RESULT</c> của form-server, và người bấm tay (PUT …/state).
/// Xem docs/handoff/20260826-236-chot-p3-cls.md §5.1.
/// </summary>
public enum ParaclinicalItemState : short
{
    Ordered = 0,      // Chờ chỉ định — trạng thái mặc định của mọi dòng vừa tạo
    Waiting = 1,      // Chờ thực hiện
    InProgress = 2,   // Đang thực hiện
    Done = 3,         // Đã trả KQ
    Cancelled = 4     // Hủy — trạng thái cuối
}

/// <summary>
/// Trạng thái GỬI của một phiếu chỉ định sang LIS/PACS/RIS — 01-db-model §2.2.
///
/// P3a chưa gửi đi đâu cả (nhánh tích hợp vendor thuộc P3b, chưa chốt hướng — xem
/// docs/handoff/20260826-236-chot-p3-cls.md §4). Mọi phiếu vì thế nằm ở <c>NotSent</c>;
/// khai đủ bảng số ngay để P3b không phải migrate lại cột.
/// </summary>
public enum OrderSentStatus : short
{
    NotSent = 0,
    Sent = 1,
    Failed = 2
}

/// <summary>
/// Trạng thái một gói trong hàng đợi gửi ra vendor — HEX_IntegrationOutbox (P3b).
///
/// Cùng bảng số với <see cref="WebhookProcessState"/> ở chiều vào để người trực đọc hai hàng
/// đợi không phải nhớ hai bảng: 0 chờ, 1 xong, 2 lỗi còn thử lại, 3 nằm lại.
/// </summary>
public enum OutboxState : short
{
    Pending = 0,     // Chờ gửi
    Sent = 1,        // Vendor đã nhận (HTTP 2xx)
    Failed = 2,      // Lỗi — còn lượt thì thử lại
    DeadLetter = 3   // Hết lượt thử. KHÔNG tự bỏ đi: phải có người nhìn, xem IntegrationOutboxService
}

/// <summary>
/// Trạng thái vòng đời của phiếu chỉ định cận lâm sàng (ParaclinicalOrder).
/// Draft → Submitted → Ordered → InProgress → Completed.
/// Hủy được khi chưa Completed: Draft/Submitted/Ordered/InProgress → Cancelled.
/// </summary>
public enum ParaclinicalOrderStatus : short
{
    Draft = 0,
    Submitted = 1,
    Ordered = 2,
    InProgress = 3,
    Completed = 4,
    Cancelled = 5
}

