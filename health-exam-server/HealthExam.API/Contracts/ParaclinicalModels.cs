namespace HealthExam.API.Contracts;

// ───────────────────────── Danh mục dịch vụ 3 tầng (02-api-spec §3.4) ─────────────────────

/// <summary>Tầng 1 — XN / CĐHA / TDCN.</summary>
public class ServiceCategoryItem
{
    public string CategoryCode { get; set; } = "";
    public string CategoryName { get; set; } = "";

    /// <summary>Số dịch vụ đang dùng được trong tầng này — FE hiện ngay ở nút tầng 1 để người
    /// dùng không bấm vào một nhánh rỗng rồi mới biết.</summary>
    public int ServiceCount { get; set; }
}

/// <summary>Tầng 2 — nhóm dịch vụ trong một loại.</summary>
public class ServiceGroupItem
{
    public string GroupCode { get; set; } = "";
    public string GroupName { get; set; } = "";
    public string CategoryCode { get; set; } = "";
    public int ServiceCount { get; set; }
}

/// <summary>Tầng 3 — dịch vụ. <see cref="ServiceID"/> là con trỏ sang danh mục của HIS.</summary>
public class ServiceCatalogItem
{
    public long ServiceID { get; set; }
    public string ServiceCode { get; set; } = "";
    public string ServiceName { get; set; } = "";
    public string CategoryCode { get; set; } = "";
    public string GroupCode { get; set; } = "";

    /// <summary>Gói khám nào đang chứa dịch vụ này — FE đánh dấu "đã có trong gói" để người
    /// chỉ định không bung trùng.</summary>
    public List<Guid> InPackages { get; set; } = new();
}

// ───────────────────────── Chỉ định (02-api-spec §3.4) ────────────────────────────────────

public class ParaclinicalOrderItemView
{
    public Guid OrderItemID { get; set; }
    public Guid OrderID { get; set; }
    public string OrderNo { get; set; } = "";
    public long ServiceID { get; set; }
    public string ServiceCode { get; set; } = "";
    public string ServiceName { get; set; } = "";
    public string ServiceGroupCode { get; set; } = "";
    public short Quantity { get; set; }

    public short State { get; set; }

    /// <summary>Tên tiếng Việt của trạng thái — trả kèm để FE không phải giữ bảng số thứ hai
    /// (cùng lý do với StateNames của hồ sơ).</summary>
    public string StateName { get; set; } = "";

    public DateTime? PerformedAt { get; set; }

    /// <summary>🔴 "Lúc health-exam-server biết KQ đã sẵn sàng", KHÔNG phải lúc labo ký —
    /// xem ParaclinicalOrderItem.ResultAt.</summary>
    public DateTime? ResultAt { get; set; }

    public string ResultSourceKind { get; set; } = "";
    public string ResultRefID { get; set; }
    public Guid? AttachmentID { get; set; }

    /// <summary>NULL = chưa ai đánh giá. false = đã đọc và thấy bình thường. Hai chuyện khác nhau.</summary>
    public bool? IsAbnormal { get; set; }

    public Guid? SourcePackageID { get; set; }
    public DateTime? CancelledAt { get; set; }
    public string CancelReason { get; set; } = "";
}

public class ParaclinicalOrderView
{
    public Guid OrderID { get; set; }
    public Guid RecordID { get; set; }
    public Guid SessionID { get; set; }
    public string OrderNo { get; set; } = "";
    public string ParaclinicalKind { get; set; } = "";
    public Guid? SourcePackageID { get; set; }
    public long OrderedByID { get; set; }
    public string OrderedByName { get; set; } = "";
    public DateTime OrderedAt { get; set; }
    public string TargetSystem { get; set; } = "";
    public short SentStatus { get; set; }
    public DateTime? SentAt { get; set; }
    public string Note { get; set; } = "";
    public bool IsActive { get; set; }
    public List<ParaclinicalOrderItemView> Items { get; set; } = new();
}

/// <summary>Thân gói POST /v1/exam-records/{id}/orders — chỉ định TAY.</summary>
public class ParaclinicalOrderCreateRequest
{
    /// <summary>Danh mục dịch vụ chọn ở tầng 3 của pop-up.</summary>
    public List<long> ServiceIDs { get; set; } = new();

    public string Note { get; set; } = "";
    public int RoomID { get; set; }
}

/// <summary>Thân gói POST …/orders/from-package — bung gói khám.</summary>
public class ParaclinicalOrderFromPackageRequest
{
    /// <summary>NULL = lấy gói đang gắn trên hồ sơ (hoặc trên đợt nếu hồ sơ không có).</summary>
    public Guid? PackageID { get; set; }

    public string Note { get; set; } = "";
    public int RoomID { get; set; }
}

public class ParaclinicalCancelRequest
{
    /// <summary>NULL/rỗng = huỷ cả phiếu. Có giá trị = chỉ huỷ những dòng dịch vụ này.</summary>
    public List<Guid> OrderItemIDs { get; set; }

    public string Reason { get; set; } = "";
}

/// <summary>
/// Thân gói PUT /v1/orders/{orderId}/state — thao tác TAY tại phòng thực hiện.
///
/// ⚠️ LỆCH TÀI LIỆU CÓ CHỦ Ý: 02-api-spec §3.4 đặt endpoint ở cấp PHIẾU, nhưng vòng đời là
/// của DÒNG DỊCH VỤ (§2.4) — kết quả về lẻ tẻ từng cái. Giữ nguyên đường dẫn của tài liệu,
/// và thân gói cho phép nêu <see cref="OrderItemIDs"/>; bỏ trống thì áp cho mọi dòng còn sống
/// của phiếu. Đặt endpoint ở cấp phiếu mà không có lối nêu từng dòng thì phòng xét nghiệm trả
/// 1 trong 5 kết quả sẽ không có cách nào ghi nhận.
/// </summary>
public class ParaclinicalStateRequest
{
    public List<Guid> OrderItemIDs { get; set; }

    /// <summary>ParaclinicalItemState — 0..3. Huỷ (4) đi đường riêng (POST …/cancel) vì nó
    /// cần lý do và có chốt chặn 4094.</summary>
    public short State { get; set; }

    /// <summary>Đánh giá bất thường, chỉ có nghĩa khi chuyển sang Đã trả KQ. NULL = không
    /// đụng tới giá trị đang có.</summary>
    public bool? IsAbnormal { get; set; }

    public string ResultRefID { get; set; }
}

// ───────────────────────── Điều kiện ký kết luận (02-api-spec §3.7) ───────────────────────

public class ConclusionConditionItem
{
    public string Code { get; set; } = "";
    public string Label { get; set; } = "";
    public bool Satisfied { get; set; }
    public string Detail { get; set; } = "";
    public string Source { get; set; } = "";
}

public class ConclusionEligibilityItem
{
    public Guid ProfileID { get; set; }
    public string RecordCode { get; set; } = "";
    public Guid? SubmissionID { get; set; }
    public List<ConclusionConditionItem> Conditions { get; set; } = new();
    public bool CanSignConclusion { get; set; }
    public long? SignedByEmployeeID { get; set; }
    public DateTime? SignedAt { get; set; }
    public List<ConclusionSignStep> Steps { get; set; } = new();
    public List<int> MissingSteps { get; set; } = new();
}

/// <summary>
/// Kết quả lệnh ký kết luận qua sign-server — trạng thái hồ sơ, file đã ký và từng bước ký.
/// </summary>
public class ConclusionSignResult
{
    public Guid RecordID { get; set; }
    public string Status { get; set; } = "";
    public string SignedFilePath { get; set; }
    public long? SignedByEmployeeID { get; set; }
    public DateTime? SignedAt { get; set; }
    public List<ConclusionConditionItem> Conditions { get; set; } = new();
    public List<ConclusionSignStep> Steps { get; set; } = new();
    public List<int> MissingSteps { get; set; } = new();
}

public class ConclusionSignStep
{
    public int SwStep { get; set; }
    public string StepName { get; set; } = "";
    public int? ItemGroupID { get; set; }
    public long SwRoleId { get; set; }
    public string Status { get; set; } = "";
    public long? SignedByEmployeeID { get; set; }
    public DateTime? SignedAt { get; set; }
    public string SignedByEmployeeName { get; set; } = "";
    public long? PerformedByEmployeeID { get; set; }
    public string PerformedByEmployeeName { get; set; } = "";
}
