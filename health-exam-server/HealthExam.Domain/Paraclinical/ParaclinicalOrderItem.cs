using HealthExam.Domain.Common;

namespace HealthExam.Domain.Paraclinical;

/// <summary>
/// HEX_ParaclinicalOrderItem — MỘT DÒNG DỊCH VỤ trong phiếu chỉ định (01-db-model §5).
///
/// Đây là đơn vị có vòng đời (<see cref="ParaclinicalItemState"/>) và là đơn vị điều kiện (B)
/// đếm. Mọi chuyển trạng thái đi qua đúng một chỗ — <c>ParaclinicalStateMachine</c> — vì ba
/// đường ghi khác nhau (người bấm tay, sự kiện SCAN_RESULT, huỷ chỉ định) mà mỗi đường tự
/// diễn giải luật thì cái sai sẽ là cái ít người nhìn nhất.
/// </summary>
public abstract class ParaclinicalOrderItemBase : AuditableEntity
{
    public static bool BlocksConditionB(ParaclinicalItemState state)
        => state is not (ParaclinicalItemState.Done or ParaclinicalItemState.Cancelled);
}

public class ParaclinicalOrderItem : ParaclinicalOrderItemBase
{
    public Guid OrderItemID { get; set; }
    public Guid OrderID { get; set; }

    /// <summary>
    /// Denormalize từ phiếu: điều kiện (B) là một câu đếm theo HỒ SƠ, chạy mỗi lần mở tab Kết
    /// luận. Bắt nó join sang phiếu chỉ để lấy RecordID là thêm một join vào đúng câu truy vấn
    /// nóng nhất của màn hình.
    /// </summary>
    public Guid RecordID { get; set; }

    /// <summary>
    /// Tenant, denormalize xuống tận dòng dịch vụ.
    ///
    /// Không thừa: khoá chống trùng theo <see cref="MessageID"/> phải là UNIQUE
    /// (DivisionID, MessageID) — cùng khuôn với HEX_WebhookInbox — mà một chỉ mục thì không
    /// với sang bảng cha lấy cột được. Nhiều DB tenant dùng chung mã, thiếu chốt chặn này thì
    /// kết quả trả về của đơn vị khác trông vẫn hoàn toàn hợp lý.
    /// </summary>
    public string DivisionID { get; set; } = "";

    /// <summary>Con trỏ sang danh mục dịch vụ của HIS — không FK, không join xuyên DB.</summary>
    public long ServiceID { get; set; }

    /// <summary>Ảnh chụp có chủ đích, cùng lý do với HEX_ExamPackageService: HIS đổi tên dịch
    /// vụ thì phiếu đã in vẫn giữ đúng tên tại thời điểm chỉ định.</summary>
    public string ServiceCode { get; set; } = "";
    public string ServiceName { get; set; } = "";
    public string ServiceGroupCode { get; set; } = "";

    public short Quantity { get; set; } = 1;
    public int Priority { get; set; } = 0;
    public DateTime? ScheduledFrom { get; set; }
    public DateTime? ScheduledTo { get; set; }
    public long? HisParaClinReqDtlId { get; set; }
    public long? HisParaClinProcessId { get; set; }
    public bool ResultRequired { get; set; } = true;

    public ParaclinicalItemState State { get; set; } = ParaclinicalItemState.Ordered;

    public DateTime? PerformedAt { get; set; }

    /// <summary>
    /// 🔴 NGỮ NGHĨA ĐÃ CHỐT: "lúc health-exam-server BIẾT kết quả đã sẵn sàng", **không** phải
    /// lúc labo ký kết quả. Server tự đóng dấu khi dòng dịch vụ vào trạng thái Đã trả KQ.
    ///
    /// Phải ghi rõ vì cả hai nguồn đưa lên trạng thái đó đều không mang mốc lâm sàng: sự kiện
    /// đính kèm chỉ nói "vừa có file", còn đường vendor thì DTO đúng ba trường
    /// (OrderID/VoucherType/NewStatus) — không có ResultAt. Đọc nhầm thành mốc lâm sàng là
    /// sai dữ liệu y tế. Xem docs/handoff/20260826-236-chot-p3-cls.md §5.
    /// </summary>
    public DateTime? ResultAt { get; set; }

    /// <summary>SCAN | MANUAL | LIS | PACS — xem ParaclinicalResultSources.</summary>
    public string ResultSourceKind { get; set; } = "";

    /// <summary>
    /// NULLABLE, sửa so với 01-db-model §5 (bản đó khai NOT NULL DEFAULT '').
    /// Rỗng và "chưa có nguồn nào cấp" là hai chuyện khác nhau khi đối soát với vendor.
    /// </summary>
    public string ResultRefID { get; set; }

    /// <summary>Con trỏ FRM_SubmissionAttachment — chỉ điền trên đường SCAN_RESULT.</summary>
    public Guid? AttachmentID { get; set; }

    /// <summary>
    /// ★ NULLABLE, sửa so với 01-db-model §5 (bản đó khai <c>NOT NULL DEFAULT false</c>).
    ///
    /// <c>false</c> nghĩa là "đã đọc kết quả và thấy bình thường" — một khẳng định lâm sàng.
    /// Nguồn RIS không nói được gì về việc đó, nên mặc định false là bịa ra một kết luận
    /// không ai đưa. NULL = chưa ai đánh giá.
    /// </summary>
    public bool? IsAbnormal { get; set; }

    /// <summary>
    /// ★ Khoá chống trùng do vendor cấp, NULLABLE + UNIQUE MỘT PHẦN (DivisionID, MessageID).
    ///
    /// Khai NGAY TỪ MIGRATION ĐẦU dù P3a chưa có đường ghi: vendor duy nhất đang nối
    /// (VietRad) KHÔNG gửi MessageID, nên chống trùng thật hôm nay là guard theo nguồn
    /// (ParaclinicalStateMachine) — nhưng một LIS thật sau này có thể gửi, và thêm cột +
    /// chỉ mục lúc bảng đã có dữ liệu thật thì đắt hơn nhiều. Cùng khuôn mẫu với
    /// HEX_WebhookInbox: chốt chặn ở TẦNG DB, và unique phải KÈM DivisionID.
    /// </summary>
    public string MessageID { get; set; }

    /// <summary>
    /// ★ Số hiệu THUẦN SỐ của dòng dịch vụ trên dây vendor — NULL cho tới lúc phiếu được gửi.
    ///
    /// Vì sao không dùng thẳng <see cref="OrderItemID"/>: đường về của RIS cắt hai ký tự đầu
    /// rồi <c>long.TryParse</c> phần còn lại (pacs-connect-server/M07F99020Commands.cs) — một
    /// GUID gửi đi thì gói trả về không parse được, và cầu im lặng không áp trạng thái nào.
    /// Nếp của HIS cũng vậy: <c>Orders[].Id = "K0" + ParaClinProcessID</c>.
    ///
    /// Cấp LÚC GỬI chứ không lúc tạo dòng: có số hiệu vendor nghĩa là "dòng này đã ra tới một
    /// hệ bên ngoài". Cấp sẵn cho mọi dòng thì cột này không còn trả lời được câu đó, mà đó
    /// đúng là câu phải hỏi khi đối soát.
    ///
    /// ⚠️ CHƯA GIẢI QUYẾT — va chạm số hiệu giữa hai hệ trong CÙNG một tenant vendor: RIS chỉ
    /// cấu hình được MỘT URL gọi về cho mỗi tenant, và nó vứt bỏ tiền tố 2 ký tự. Nếu KSK và
    /// HIS cùng bắn vào tenant <c>84006</c> thì gói gọi về đi hết sang HIS, ở đó
    /// <c>Substring(2)</c> ra một số CÓ THỂ trùng một ParaClinProcessID thật. Cần tenant RIS
    /// riêng cho KSK (hoặc một bộ định tuyến trước hai service) — xem
    /// docs/handoff/20260826-235-p3b-dispatch-ris.md §5.
    /// </summary>
    public long? VendorLineNo { get; set; }

    public DateTime? CancelledAt { get; set; }
    public string CancelReason { get; set; } = "";

    /// <summary>Gói khám đã bung ra dòng này — giữ ở cả dòng dịch vụ chứ không chỉ ở phiếu,
    /// vì một dịch vụ trong gói có thể bị huỷ lẻ và phần đối soát chi phí hỏi theo dịch vụ.</summary>
    public Guid? SourcePackageID { get; set; }

    public ParaclinicalOrder Order { get; set; }

    public new bool BlocksConditionB => State is not (ParaclinicalItemState.Done or ParaclinicalItemState.Cancelled);

    public ParaclinicalTransition TransitionTo(
        ParaclinicalItemState target,
        ParaclinicalStateSource source,
        DateTime now,
        Guid? attachmentId = null,
        string resultSourceKind = null,
        bool? isAbnormal = null,
        string resultRefId = null,
        string messageId = null)
    {
        if (State == ParaclinicalItemState.Cancelled)
            return new ParaclinicalTransition(
                ParaclinicalTransitionOutcome.Rejected,
                "Dịch vụ đã huỷ — trạng thái cuối, không quay lại được. Chỉ định lại một dòng mới nếu cần khám.");

        if (target == ParaclinicalItemState.Cancelled)
            return new ParaclinicalTransition(
                ParaclinicalTransitionOutcome.Rejected,
                "Huỷ dịch vụ đi đường riêng (POST /v1/orders/{id}/cancel) vì cần lý do và có chốt chặn 4094");

        if (source == ParaclinicalStateSource.Vendor)
        {
            if (target is not (ParaclinicalItemState.Waiting or ParaclinicalItemState.InProgress))
                return new ParaclinicalTransition(
                    ParaclinicalTransitionOutcome.Rejected,
                    $"Vendor chỉ báo được 'Chờ thực hiện' hoặc 'Đang thực hiện', không phải '{StateName(target)}'");

            if (target == State)
                return new ParaclinicalTransition(
                    ParaclinicalTransitionOutcome.NoOp,
                    $"Dịch vụ đã ở '{StateName(target)}' — gói vendor lặp lại, bỏ qua");

            if (target < State)
                return new ParaclinicalTransition(
                    ParaclinicalTransitionOutcome.Rejected,
                    $"Dịch vụ đang ở '{StateName(State)}', gói vendor đòi lùi về "
                    + $"'{StateName(target)}' — đường về của vendor chỉ tiến, không lùi");
        }

        if (State == target)
            return new ParaclinicalTransition(
                ParaclinicalTransitionOutcome.NoOp, $"Dịch vụ đã ở '{StateName(target)}'");

        var from = State;
        State = target;

        if (target == ParaclinicalItemState.InProgress && !PerformedAt.HasValue)
            PerformedAt = now;

        if (target == ParaclinicalItemState.Done)
        {
            ResultAt ??= now;
            if (!string.IsNullOrWhiteSpace(resultSourceKind)) ResultSourceKind = resultSourceKind;
            if (attachmentId.HasValue) AttachmentID = attachmentId;
            if (isAbnormal.HasValue) IsAbnormal = isAbnormal;
            if (!string.IsNullOrWhiteSpace(resultRefId)) ResultRefID = resultRefId;
        }
        else if (from == ParaclinicalItemState.Done)
        {
            ResultAt = null;
            ResultSourceKind = "";
            AttachmentID = null;
            IsAbnormal = null;
            ResultRefID = null;
        }

        if (!string.IsNullOrWhiteSpace(messageId)) MessageID = messageId;

        ModifiedDate = now;
        return new ParaclinicalTransition(
            ParaclinicalTransitionOutcome.Applied,
            $"{StateName(from)} → {StateName(target)}");
    }

    public bool TryCancel(string reason, DateTime now)
    {
        if (State == ParaclinicalItemState.Done) return false;
        if (State == ParaclinicalItemState.Cancelled) return true;

        State = ParaclinicalItemState.Cancelled;
        CancelledAt = now;
        CancelReason = reason ?? "";
        ModifiedDate = now;
        return true;
    }

    private static string StateName(ParaclinicalItemState state) => state switch
    {
        ParaclinicalItemState.Ordered => "Chờ chỉ định",
        ParaclinicalItemState.Waiting => "Chờ thực hiện",
        ParaclinicalItemState.InProgress => "Đang thực hiện",
        ParaclinicalItemState.Done => "Đã trả KQ",
        ParaclinicalItemState.Cancelled => "Hủy",
        _ => ""
    };
}

public enum ParaclinicalStateSource
{
    Manual = 0,
    Internal = 0,
    Vendor = 1
}

public enum ParaclinicalTransitionOutcome
{
    Applied,
    NoOp,
    Rejected
}

public readonly record struct ParaclinicalTransition(
    ParaclinicalTransitionOutcome Outcome, string Reason)
{
    public bool Applied => Outcome == ParaclinicalTransitionOutcome.Applied;
    public bool IsSuccess => Outcome != ParaclinicalTransitionOutcome.Rejected;
}
