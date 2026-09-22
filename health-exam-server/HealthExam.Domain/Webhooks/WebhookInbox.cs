using HealthExam.Domain.Common;

namespace HealthExam.Domain.Webhooks;

/// <summary>
/// HEX_WebhookInbox — mọi sự kiện form-server đẩy sang đều đáp xuống đây TRƯỚC, xử lý sau
/// (01-db-model §8, 02-api-spec §5.3).
///
/// Vì sao không xử lý thẳng trong request webhook: bên phát coi phản hồi chậm/lỗi là "chưa
/// nhận được" và sẽ gửi lại. Nếu ta vừa cập nhật hồ sơ vừa bắt họ chờ thì một lần chậm của
/// mình thành một lần retry của họ, và người bấm nút Ký là người chịu. Ghi một dòng rồi trả
/// 200 ngay là hợp đồng rẻ nhất giữ được cả hai đầu.
///
/// UNIQUE(EventID) là chốt chặn trùng THẬT — không phải kiểm ở tầng service: webhook gửi lại
/// là chuyện bình thường (mạng chớp, bên phát restart giữa lúc chờ ack), và hai gói cùng
/// EventID tới song song thì mọi phép kiểm "đã có chưa" ở tầng code đều lọt.
/// </summary>
public class WebhookInbox
{
    public long InboxID { get; set; }

    /// <summary>
    /// ★ Khoá idempotency do form-server cấp. UNIQUE theo (DivisionID, EventID).
    ///
    /// Vì sao không UNIQUE toàn bảng dù bên phát sinh mã duy nhất toàn cục: khoá toàn cục
    /// biến "một EventID bị tiêu thụ nhầm tenant" thành "gói thật vĩnh viễn là trùng" — mất
    /// hẳn một sự kiện khám, không lỗi nào ném ra (BLOCKER 2, review MR !5).
    /// </summary>
    public string EventID { get; set; } = "";

    /// <summary>Tên sự kiện — xem FormEventNames. Lưu nguyên chuỗi nhận được, kể cả tên lạ.</summary>
    public string EventType { get; set; } = "";

    /// <summary>
    /// Tenant của phiếu, lấy từ header X-Division-Id LÚC NHẬN.
    ///
    /// ⚠️ Đây là BỔ SUNG so với 01-db-model §8 (bảng ở đó ghi "division suy từ hồ sơ"). Suy
    /// ngược không làm được: RecordCode/SessionCode chỉ duy nhất TRONG một tenant, nên muốn
    /// tra hồ sơ thì phải biết tenant trước — mà worker chạy ngoài mọi request nên không có
    /// header nào để đọc. Không giữ cột này thì worker phải quét toàn bộ tenant và có thể
    /// khớp nhầm hồ sơ cùng mã ở đơn vị khác.
    /// </summary>
    public string DivisionID { get; set; } = "";

    public Guid? SubmissionID { get; set; }

    /// <summary>Tra được lúc nhận thì điền, không thì để NULL và worker tra lại lúc xử lý.</summary>
    public Guid? RecordID { get; set; }

    /// <summary>jsonb — nguyên văn gói đã nhận. Giữ nguyên để dựng lại được sự việc khi đối soát.</summary>
    public string Payload { get; set; } = "{}";

    /// <summary>Thời điểm sự kiện XẢY RA bên form-server (không phải lúc ta nhận) — dùng để
    /// so thứ tự. Thiếu thì lấy tạm ReceivedAt, xem WebhookIngestService.</summary>
    public DateTime OccurredAt { get; set; }

    public DateTime ReceivedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ProcessedAt { get; set; }

    public WebhookProcessState ProcessState { get; set; } = WebhookProcessState.New;
    public short RetryCount { get; set; }

    /// <summary>
    /// ★ Sớm nhất được thử lại lúc nào. NULL = thử ngay (hàng mới).
    ///
    /// Vì sao cần cột này chứ không chỉ đếm số lần: worker ngủ khi lô RỖNG, mà một hàng vừa
    /// Failed vẫn được tính là "đã xử lý" nên lô không rỗng và lượt sau nhặt lại NGAY. Đo
    /// được: 5 lượt thử của một hàng cháy hết trong ~2 giây, tức toàn bộ ngân sách thử lại
    /// tiêu hết trước khi sự cố tạm thời (form-server restart, mạng chớp) kịp qua — thử lại
    /// mà không giãn cách thì không phải thử lại (M2, review MR !5).
    /// </summary>
    public DateTime? NextAttemptAt { get; set; }

    public string LastError { get; set; } = "";

    /// <summary>Trace của thao tác gốc bên form-server, truyền tiếp để nối hai chuỗi log.</summary>
    public string TraceID { get; set; } = "";

    /// <summary>
    /// ★ Khoá chống trùng của đường VENDOR (LIS/PACS/RIS) — NULL trên mọi gói của form-server.
    ///
    /// Hai đường vào cùng một hộp thư, hai khoá khác nhau: form-server cấp <see cref="EventID"/>
    /// (bắt buộc), vendor thì tuỳ — VietRad, vendor duy nhất hệ thống đang nối, gửi đúng ba
    /// trường OrderID/VoucherType/NewStatus và KHÔNG có khoá nào. Vì thế cột này NULLABLE và
    /// chỉ mục là UNIQUE MỘT PHẦN (DivisionID, MessageID) WHERE MessageID IS NOT NULL:
    /// vendor nào gửi thì được chặn ở tầng DB, không gửi thì rơi về guard theo nguồn
    /// (ParaclinicalStateMachine) chứ không phải chặn nhầm hàng loạt vì cùng NULL.
    ///
    /// Khai từ migration ĐẦU dù P3a chưa có đường ghi: thêm cột + chỉ mục vào một bảng đã
    /// chạy thật đắt hơn nhiều lần. Chốt tại docs/handoff/20260826-236-chot-p3-cls.md §5.
    ///
    /// ⚠️ 02-api-spec §5.3 gọi bảng này là <c>HEX_InboundEvent</c>, 01-db-model gọi
    /// <c>HEX_WebhookInbox</c> — MỘT bảng, hai tên trong hai tài liệu hợp đồng.
    /// </summary>
    public string MessageID { get; set; }

    /// <summary>
    /// Số lần thử tối đa của một hàng lỗi. Hết lượt thì hàng NẰM LẠI ở Failed và không được
    /// nhặt nữa — đó chính là dead-letter, không cần bảng thứ hai.
    /// </summary>
    public const short MaxRetry = 5;

    /// <summary>
    /// Giãn cách trước lần thử kế tiếp: 30s · 1' · 2' · 4' (rồi hết lượt).
    /// </summary>
    public static TimeSpan BackoffFor(short retryCount)
        => TimeSpan.FromSeconds(30 * Math.Pow(2, Math.Max(0, retryCount - 1)));

    public static TimeSpan BackoffFor(int retryCount)
        => BackoffFor((short)retryCount);

    /// <summary>
    /// Kiểm tra điều kiện rút xử lý hàng inbox.
    /// </summary>
    public bool Claim(DateTime nowUtc)
    {
        if (ProcessState == WebhookProcessState.Processed || ProcessState == WebhookProcessState.Skipped)
            return false;

        if (ProcessState == WebhookProcessState.Failed && RetryCount >= MaxRetry)
            return false;

        if (NextAttemptAt.HasValue && NextAttemptAt.Value > nowUtc)
            return false;

        return true;
    }

    /// <summary>
    /// Đóng dấu kết quả xử lý một hàng inbox.
    /// </summary>
    public void Stamp(WebhookProcessState state, string lastError, DateTime nowUtc)
    {
        LastError = Trim(lastError, WebhookFieldLengths.LastError);
        ProcessState = state;

        switch (state)
        {
            case WebhookProcessState.Processed:
            case WebhookProcessState.Skipped:
                ProcessedAt = nowUtc;
                NextAttemptAt = null;
                break;

            case WebhookProcessState.Failed:
                RetryCount = (short)(RetryCount + 1);
                NextAttemptAt = nowUtc + BackoffFor(RetryCount);
                break;

            case WebhookProcessState.New:
                RetryCount = 0;
                NextAttemptAt = null;
                break;
        }
    }

    private static string Trim(string value, int max)
        => string.IsNullOrEmpty(value) ? "" : (value.Length <= max ? value : value[..max]);
}
