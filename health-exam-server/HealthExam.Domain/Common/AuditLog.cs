namespace HealthExam.Domain.Common;

/// <summary>
/// HEX_AuditLog — nhật ký thao tác (01-db-model §4, UC01).
///
/// Khác khối audit gắn sẵn trên từng bảng (AuditableEntity): khối kia chỉ giữ được TRẠNG THÁI
/// CUỐI ("ai sửa lần gần nhất"), còn bảng này giữ TỪNG LẦN xảy ra. Đóng/mở lại một đợt khám là
/// việc phải giải trình được — mở lại đợt đã đóng cần biết ai mở, lúc nào, LÝ DO gì; ghi đè
/// ModifiedBy thì lần mở trước biến mất.
/// </summary>
public class AuditLog
{
    public long AuditID { get; set; }
    public string DivisionID { get; set; } = "";

    /// <summary>SESSION | RECORD | ORDER | ORDER_ITEM | PORTAL | IMPORT — xem AuditEntityTypes.</summary>
    public string EntityType { get; set; } = "";
    public Guid EntityID { get; set; }

    /// <summary>CREATE|UPDATE|DELETE|STATE_CHANGE|IMPORT|WEBHOOK|RECONCILE — xem AuditActions.</summary>
    public string Action { get; set; } = "";

    public long ActorID { get; set; }
    public ActorKind ActorKind { get; set; } = ActorKind.Employee;
    public string ActorName { get; set; } = "";

    /// <summary>Chuyển tiếp trạng thái. NULL với hành động không đổi trạng thái.</summary>
    public short? FromState { get; set; }
    public short? ToState { get; set; }

    /// <summary>jsonb — chỗ đựng lý do mở lại đợt và các dữ kiện kèm theo.</summary>
    public string Payload { get; set; }

    /// <summary>Nối dòng nhật ký này với log của request đã sinh ra nó.</summary>
    public string TraceID { get; set; } = "";

    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
}

/// <summary>Giá trị cột EntityType — hằng số để không rải chuỗi trần khắp nơi.</summary>
public static class AuditEntityTypes
{
    public const string Session = "SESSION";
    public const string Record = "RECORD";
    public const string Import = "IMPORT";

    /// <summary>Phiếu chỉ định CLS — HEX_ParaclinicalOrder.</summary>
    public const string Order = "ORDER";

    /// <summary>
    /// Dòng dịch vụ trong phiếu — HEX_ParaclinicalOrderItem.
    ///
    /// Tách khỏi ORDER chứ không gom về phiếu: vòng đời và điều kiện (B) đều đếm theo DÒNG,
    /// nên câu hỏi thật lúc tra là "ai đã đưa dịch vụ này sang Đã trả KQ" — ghi ở cấp phiếu
    /// thì phải đọc Payload mới biết dòng nào, và mất luôn cặp FromState/ToState.
    /// </summary>
    public const string OrderItem = "ORDER_ITEM";
}

/// <summary>Giá trị cột Action.</summary>
public static class AuditActions
{
    public const string Create = "CREATE";
    public const string Update = "UPDATE";
    public const string Delete = "DELETE";
    public const string StateChange = "STATE_CHANGE";

    /// <summary>Nạp Excel danh sách đăng ký (UC03.5) — pha upload; pha commit ghi STATE_CHANGE.</summary>
    public const string Import = "IMPORT";

    /// <summary>
    /// Chuyển trạng thái do sự kiện form-server đẩy về (02-api-spec §5.2).
    ///
    /// Tách khỏi STATE_CHANGE thay vì dùng chung: ba trong năm chuyển tiếp của hồ sơ KSK
    /// KHÔNG do ai bấm nút cả. Trộn chung thì câu hỏi "ai đẩy hồ sơ này sang Đã khám" trả về
    /// một dòng có ActorID=0 mà không nói được vì sao — còn tách ra thì đọc là biết ngay
    /// phải đi tìm bên form-server.
    /// </summary>
    public const string Webhook = "WEBHOOK";

    /// <summary>Job đối soát kéo tự chữa lệch (02-api-spec §5.3, 03-task H2-06).</summary>
    public const string Reconcile = "RECONCILE";
}
