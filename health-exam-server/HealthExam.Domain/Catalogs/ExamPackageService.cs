using HealthExam.Domain.Common;

namespace HealthExam.Domain.Catalogs;

/// <summary>
/// HEX_ExamPackageService — dịch vụ trong gói khám (01-db-model §3.2).
///
/// Lưu kèm ServiceCode/ServiceName chứ không chỉ ServiceID vì danh mục dịch vụ do HIS sở
/// hữu, nằm ở DB khác và KHÔNG join xuyên DB được. Đây là ẢNH CHỤP có chủ đích: chấp nhận
/// lệch khi HIS đổi tên dịch vụ, đổi lại phiếu đã in giữ đúng tên tại thời điểm chỉ định.
/// </summary>
public class ExamPackageService : AuditableEntity
{
    public Guid PackageServiceID { get; set; }
    public Guid PackageID { get; set; }

    /// <summary>Con trỏ sang danh mục dịch vụ của HIS — không FK, không join.</summary>
    public long ServiceID { get; set; }
    public string ServiceCode { get; set; } = "";
    public string ServiceName { get; set; } = "";

    /// <summary>XN | CDHA | TDCN — tầng 1 của pop-up chỉ định.</summary>
    public string ParaclinicalKind { get; set; } = "";

    /// <summary>Nhóm dịch vụ — tầng 2 của pop-up chỉ định.</summary>
    public string ServiceGroupCode { get; set; } = "";

    public short Quantity { get; set; } = 1;
    public int OrderNo { get; set; }
    public bool IsActive { get; set; } = true;

    public ExamPackage Package { get; set; }
}
