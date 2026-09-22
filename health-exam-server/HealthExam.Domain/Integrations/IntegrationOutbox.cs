using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using HealthExam.Domain.Common;

namespace HealthExam.Domain.Integrations;

/// <summary>
/// HEX_IntegrationOutbox — hàng đợi GỬI RA vendor (RIS/PACS), đối xứng với
/// <see cref="WebhookInbox"/> ở chiều vào (P3b, docs/handoff/20260826-236-chot-p3-cls.md §4).
///
/// Vì sao phải có hàng đợi chứ không gọi thẳng RIS trong request của người dùng:
/// RIS là hệ của bên thứ ba, nằm ngoài mạng của mình. Gọi đồng bộ thì một lần RIS chậm là
/// một lần bác sĩ ngồi chờ vòng quay ở màn chỉ định, và một lần RIS chết là chỉ định KHÔNG
/// ghi được — trong khi việc phải xảy ra là ngược lại: chỉ định luôn ghi được, còn việc gửi
/// thì thử lại tới khi thành công.
///
/// ★ Payload ĐÓNG BĂNG tại lúc xếp hàng, không dựng lại lúc gửi. Dựng lại là gửi đi một gói
/// mô tả trạng thái MỚI dưới danh nghĩa một sự kiện CŨ — tên dịch vụ đổi, dòng bị huỷ giữa
/// chừng, và cái vendor nhận được không còn là cái người dùng đã bấm.
///
/// Chống trùng ở TẦNG DB bằng UNIQUE (DivisionID, DedupKey) chứ không kiểm ở tầng service:
/// người dùng bấm Gửi hai lần liên tiếp là chuyện thường, và hai request song song thì mọi
/// phép "đã xếp hàng chưa" ở tầng code đều lọt — cùng bài học với UNIQUE(EventID) của hộp thư.
/// </summary>
public class IntegrationOutbox
{
    public long OutboxID { get; set; }

    /// <summary>Tenant — mọi truy vấn của worker phải kèm cột này. Nhiều DB tenant dùng chung
    /// mã, và một gói gửi nhầm đơn vị là một phiếu chụp cho người khác.</summary>
    public string DivisionID { get; set; } = "";

    /// <summary>Phiếu chỉ định sinh ra gói này — HEX_ParaclinicalOrder.OrderID.</summary>
    public Guid OrderID { get; set; }

    /// <summary>LIS | PACS | RIS — xem <see cref="ParaclinicalTargets"/>.</summary>
    public string Vendor { get; set; } = "";

    /// <summary>NEW | CANCELLED — đúng hai giá trị hợp đồng dây VietRad chấp nhận
    /// (<c>PACSRequest.Status</c>).</summary>
    public string Operation { get; set; } = "";

    /// <summary>
    /// ★ Khoá chống xếp hàng trùng, duy nhất trong một tenant. Hình dạng:
    /// <c>{Vendor}:{Operation}:{OrderNo}</c>.
    ///
    /// Cố ý KHÔNG kèm thời gian: mục đích của khoá là để "bấm Gửi ba lần" thành MỘT gói.
    /// Gửi lại sau khi đã gửi thành công là việc khác — đi bằng lệnh huỷ rồi chỉ định lại,
    /// vì bên vendor một phiếu đã nhận rồi thì lần gửi thứ hai không phải một bản sao vô hại.
    /// </summary>
    public string DedupKey { get; set; } = "";

    /// <summary>Nguyên văn JSON sẽ gửi đi — đã camelCase, đã theo đúng khuôn dây. Giữ lại để
    /// đối soát với vendor được mà không phải dựng lại từ dữ liệu hôm nay.</summary>
    public string Payload { get; set; } = "{}";

    public OutboxState State { get; set; } = OutboxState.Pending;

    public short RetryCount { get; set; }

    /// <summary>Sớm nhất được thử lại lúc nào. NULL = gửi ngay. Cùng lý do với
    /// <see cref="WebhookInbox.NextAttemptAt"/>: thử lại mà không giãn cách thì không phải
    /// thử lại — cả ngân sách thử cháy hết trước khi sự cố tạm thời kịp qua.</summary>
    public DateTime? NextAttemptAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? SentAt { get; set; }

    public string LastError { get; set; } = "";

    /// <summary>Vài trăm ký tự đầu của thân phản hồi lần gần nhất. Vendor trả 200 kèm thân
    /// báo lỗi là chuyện có thật ở lớp tích hợp này, nên "đã gửi" mà không giữ lại họ nói gì
    /// là mất đúng bằng chứng cần lúc đối soát.</summary>
    public string ResponseSnippet { get; set; } = "";

    /// <summary>Trace của thao tác gốc, nối chuỗi log của người bấm với lượt gửi nền.</summary>
    public string TraceID { get; set; } = "";

    /// <summary>Số lần thử tối đa của một gói. Hết lượt ⇒ DeadLetter, KHÔNG tự bỏ đi.</summary>
    public const int MaxRetry = 5;

    /// <summary>
    /// Giãn cách trước lần thử kế tiếp — 30s · 1' · 2' · 4'.
    /// </summary>
    public static TimeSpan BackoffFor(int retryCount)
        => TimeSpan.FromSeconds(30 * Math.Pow(2, Math.Max(0, retryCount - 1)));

    /// <summary>
    /// Khoá chống xếp hàng trùng. NEW thì đúng một gói cho mỗi phiếu; CANCELLED thì kèm dấu
    /// của TẬP DÒNG bị huỷ, vì huỷ lẻ hai dòng ở hai lượt là hai gói khác nhau phải cùng đi.
    /// </summary>
    public static string DedupKeyFor(string vendor, string operation, string orderNo, IEnumerable<long> lineNos = null)
    {
        var key = $"{vendor}:{operation}:{orderNo}";
        if (lineNos == null) return key;

        var suffix = string.Join(",", lineNos.OrderBy(x => x));
        var composed = $"{key}:{suffix}";

        if (composed.Length <= OutboxFieldLengths.DedupKey) return composed;

        var hash = Convert.ToHexString(
            SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(suffix)))[..16];
        return $"{key}:{hash}";
    }

    /// <summary>
    /// Khép một hàng DeadLetter lại bằng cách nhường khoá chống trùng cho gói mới.
    /// </summary>
    public static void Retire(IntegrationOutbox dead)
    {
        var suffix = $"#dead{dead.OutboxID}";
        var room = OutboxFieldLengths.DedupKey - suffix.Length;
        var head = dead.DedupKey.Length <= room ? dead.DedupKey : dead.DedupKey[..room];
        dead.DedupKey = head + suffix;
    }

    /// <summary>
    /// Ghi sổ một lượt thử TRƯỚC khi gói rời máy. Trả false nếu đã vượt quá MaxRetry và chuyển sang DeadLetter.
    /// </summary>
    public bool Claim(DateTime nowUtc)
    {
        var attempt = (short)(RetryCount + 1);

        if (attempt > MaxRetry)
        {
            State = OutboxState.DeadLetter;
            NextAttemptAt = null;
            return false;
        }

        RetryCount = attempt;
        State = OutboxState.Failed;
        NextAttemptAt = nowUtc + BackoffFor(attempt);
        return true;
    }

    /// <summary>
    /// Đóng dấu kết quả một lượt gửi.
    /// </summary>
    public void Stamp(bool success, string detail, string snippet, DateTime nowUtc)
    {
        ResponseSnippet = Trim(snippet, OutboxFieldLengths.ResponseSnippet);
        LastError = success ? "" : Trim(detail, WebhookFieldLengths.LastError);

        if (success)
        {
            State = OutboxState.Sent;
            SentAt = nowUtc;
            NextAttemptAt = null;
            return;
        }

        State = OutboxState.Failed;
        if (RetryCount >= MaxRetry)
        {
            NextAttemptAt = nowUtc;
        }
    }

    private static string Trim(string value, int max)
        => string.IsNullOrEmpty(value) ? "" : (value.Length <= max ? value : value[..max]);
}
