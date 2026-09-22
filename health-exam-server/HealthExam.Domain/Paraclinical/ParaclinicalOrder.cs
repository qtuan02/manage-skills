using HealthExam.Domain.Common;
using HealthExam.Domain.ExamRecords;

namespace HealthExam.Domain.Paraclinical;

/// <summary>
/// HEX_ParaclinicalOrder — PHIẾU chỉ định cận lâm sàng, một lần bấm Lưu ở pop-up (01-db-model §5).
///
/// Tách khỏi <see cref="ParaclinicalOrderItem"/> chứ không gộp một bảng: LIS/PACS/RIS nhận
/// theo PHIẾU (một số phiếu, một lần gửi, một lần retry), còn vòng đời 5 trạng thái và điều
/// kiện (B) thì đếm theo DÒNG DỊCH VỤ vì kết quả về lẻ tẻ từng cái. Gộp lại thì hoặc mất số
/// phiếu để đối soát với vendor, hoặc phải nhét trạng thái tổng hợp vào phiếu rồi tính ngược
/// mỗi lần một kết quả về.
/// </summary>
public class ParaclinicalOrder : AuditableEntity
{
    public Guid OrderID { get; set; }
    public string DivisionID { get; set; } = "";
    public Guid RecordID { get; set; }

    /// <summary>
    /// Denormalize từ hồ sơ: màn theo dõi đợt lọc chỉ định theo ĐỢT, và một join thêm cho
    /// mỗi lần lật trang trên đợt vài trăm người là cái giá không cần trả.
    /// </summary>
    public Guid SessionID { get; set; }

    /// <summary>
    /// Số phiếu chỉ định — UNIQUE theo (DivisionID, OrderNo).
    ///
    /// ★ Quy ước 2 KÝ TỰ ĐẦU rồi phần còn lại THUẦN SỐ, cố ý theo nếp HIS: đường về của RIS
    /// (pacs-connect-server M07F99020Commands) cắt <c>OrderID.Substring(2)</c> rồi mới parse
    /// số. Đặt mã tự do ngay từ P3a thì P3b hoặc phải đổi mã hàng loạt, hoặc phải dựng một
    /// bảng ánh xạ chỉ để nói chuyện với vendor.
    /// </summary>
    public string OrderNo { get; set; } = "";

    /// <summary>XN | CDHA | TDCN — tầng 1 của pop-up. Một phiếu chỉ chứa một loại: vendor
    /// nhận phiếu theo hệ đích, trộn XN với CĐHA trong một phiếu là không gửi đi đâu được.</summary>
    public string ParaclinicalKind { get; set; } = "";

    /// <summary>
    /// Gói khám đã bung ra phiếu này — NULL nếu chỉ định tay.
    ///
    /// Giữ lại vì đối soát chi phí với đơn vị ký hợp đồng cần biết chỉ định nào là "trong
    /// gói" và nào là phát sinh. Không FK sang HEX_ExamPackage: gói bị soft-delete vẫn phải
    /// truy được, và phiếu đã in giữ đúng thông tin tại thời điểm chỉ định.
    /// </summary>
    public Guid? SourcePackageID { get; set; }

    public long OrderedByID { get; set; }
    public string OrderedByName { get; set; } = "";
    public DateTime OrderedAt { get; set; } = DateTime.UtcNow;
    public int RoomID { get; set; }

    /// <summary>
    /// LIS | PACS | RIS | NONE. P3a luôn ghi <c>NONE</c>: chưa có cầu nào để gửi (nhánh
    /// vendor là P3b, xem docs/handoff/20260826-236-chot-p3-cls.md §4). Ghi đúng sự thật ở
    /// đây để lúc P3b vào, câu "phiếu nào chưa từng được gửi" trả lời được bằng dữ liệu.
    /// </summary>
    public string TargetSystem { get; set; } = ParaclinicalTargets.None;

    public OrderSentStatus SentStatus { get; set; } = OrderSentStatus.NotSent;
    public DateTime? SentAt { get; set; }

    /// <summary>ID phiếu bên LIS/PACS — rỗng cho tới khi P3b gửi đi thật.</summary>
    public string ExternalOrderID { get; set; } = "";

    public string Note { get; set; } = "";
    public bool IsActive { get; set; } = true;

    // --- HIS Linkage & Sync (docs/superpowers/specs/2026-09-18-health-exam-paraclinical-design.md §Local data)
    public long? HisPtId { get; set; }
    public string HisPtCode { get; set; } = "";
    public long? HisAdmissionId { get; set; }
    public string HisAdmissionCode { get; set; } = "";
    public long? HisTreatmentProcessId { get; set; }
    public long? HisParaClinReqId { get; set; }
    public ParaclinicalOrderStatus Status { get; set; } = ParaclinicalOrderStatus.Draft;
    public DateTime? SubmittedAt { get; set; }
    public DateTime? CancelledAt { get; set; }
    public DateTime? LastSyncAt { get; set; }
    public string LastSyncError { get; set; } = "";
    public int Version { get; set; } = 1;

    public ExamRecord Record { get; set; }
    public ICollection<ParaclinicalOrderItem> Items { get; set; } = new List<ParaclinicalOrderItem>();

    public DomainResult Submit(DateTime utcNow)
    {
        if (Status != ParaclinicalOrderStatus.Draft)
            return DomainResult.Reject(DomainFailureCode.InvalidTransition, $"Chỉ phiếu ở trạng thái Draft mới có thể submit (hiện tại: {Status})");

        Status = ParaclinicalOrderStatus.Submitted;
        SubmittedAt = utcNow;
        Version++;
        return DomainResult.Apply();
    }

    public DomainResult MarkOrdered(long hisParaClinReqId, DateTime utcNow)
    {
        if (Status != ParaclinicalOrderStatus.Submitted)
            return DomainResult.Reject(DomainFailureCode.InvalidTransition, $"Phiếu phải ở trạng thái Submitted trước khi sang Ordered (hiện tại: {Status})");

        Status = ParaclinicalOrderStatus.Ordered;
        HisParaClinReqId = hisParaClinReqId;
        LastSyncAt = utcNow;
        Version++;
        return DomainResult.Apply();
    }

    public DomainResult MarkInProgress(DateTime utcNow)
    {
        if (Status != ParaclinicalOrderStatus.Ordered && Status != ParaclinicalOrderStatus.InProgress)
            return DomainResult.Reject(DomainFailureCode.InvalidTransition, $"Phiếu phải ở trạng thái Ordered hoặc InProgress (hiện tại: {Status})");

        Status = ParaclinicalOrderStatus.InProgress;
        LastSyncAt = utcNow;
        Version++;
        return DomainResult.Apply();
    }

    public DomainResult MarkCompleted(DateTime utcNow)
    {
        if (Status != ParaclinicalOrderStatus.InProgress && Status != ParaclinicalOrderStatus.Ordered)
            return DomainResult.Reject(DomainFailureCode.InvalidTransition, $"Phiếu phải ở trạng thái Ordered/InProgress trước khi Completed (hiện tại: {Status})");

        Status = ParaclinicalOrderStatus.Completed;
        LastSyncAt = utcNow;
        Version++;
        return DomainResult.Apply();
    }

    public DomainResult Cancel(string reason, DateTime utcNow)
    {
        if (Status == ParaclinicalOrderStatus.Completed)
            return DomainResult.Reject(DomainFailureCode.InvalidTransition, "Phiếu đã hoàn thành (Completed) không thể hủy");

        if (Status == ParaclinicalOrderStatus.Cancelled)
            return DomainResult.NoOp();

        Status = ParaclinicalOrderStatus.Cancelled;
        CancelledAt = utcNow;
        if (!string.IsNullOrWhiteSpace(reason))
            Note = string.IsNullOrWhiteSpace(Note) ? $"[Hủy]: {reason}" : $"{Note} | [Hủy]: {reason}";
        Version++;
        return DomainResult.Apply();
    }
}

/// <summary>Tầng 1 của pop-up chỉ định — ba loại cận lâm sàng, danh mục ĐÓNG (FRD UC05.2).</summary>
public static class ParaclinicalKinds
{
    public const string Lab = "XN";       // Xét nghiệm
    public const string Imaging = "CDHA"; // Chẩn đoán hình ảnh
    public const string Function = "TDCN";// Thăm dò chức năng

    public static readonly IReadOnlyDictionary<string, string> Names =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [Lab] = "Xét nghiệm",
            [Imaging] = "Chẩn đoán hình ảnh",
            [Function] = "Thăm dò chức năng"
        };

    public static bool IsValid(string kind) => Names.ContainsKey((kind ?? "").Trim());

    public static string NameOf(string kind)
        => Names.TryGetValue((kind ?? "").Trim(), out var name) ? name : (kind ?? "");
}

/// <summary>Giá trị của <see cref="ParaclinicalOrder.TargetSystem"/>.</summary>
public static class ParaclinicalTargets
{
    /// <summary>Không gửi đi đâu — nhập tay / scan kết quả. Mặc định của P3a.</summary>
    public const string None = "NONE";

    public const string Lis = "LIS";
    public const string Pacs = "PACS";
    public const string Ris = "RIS";
    public const string His = "HIS";
}

/// <summary>Giá trị của <see cref="ParaclinicalOrderItem.ResultSourceKind"/> — nguồn đã đưa
/// dòng dịch vụ lên "Đã trả KQ". Ghi lại vì bốn cột kết quả có nguồn khác nhau và độ tin
/// khác nhau (docs/handoff/20260826-236-chot-p3-cls.md §5).</summary>
public static class ParaclinicalResultSources
{
    /// <summary>Đính kèm SCAN_RESULT bên form-server — đường DUY NHẤT tự động đóng được (B).</summary>
    public const string Scan = "SCAN";

    /// <summary>Người dùng bấm đổi trạng thái tay tại phòng thực hiện.</summary>
    public const string Manual = "MANUAL";

    public const string Lis = "LIS";
    public const string Pacs = "PACS";
}
