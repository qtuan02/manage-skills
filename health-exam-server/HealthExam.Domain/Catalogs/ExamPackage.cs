using HealthExam.Domain.Common;

namespace HealthExam.Domain.Catalogs;

/// <summary>HEX_ExamPackage — gói khám mẫu (01-db-model §3.2).</summary>
public class ExamPackage : AuditableEntity
{
    public Guid PackageID { get; set; }
    public string DivisionID { get; set; } = "";
    public string PackageCode { get; set; } = "";
    public string PackageName { get; set; } = "";

    /// <summary>Gói gắn với 1 Nhóm khám (DTK_01..DTK_10); NULL = dùng chung mọi nhóm.</summary>
    public string VariantCode { get; set; }

    public string Description { get; set; }

    /// <summary>
    /// Soft-delete, KHÔNG xoá cứng: đợt khám cũ đã bung dịch vụ theo gói vẫn phải đọc lại
    /// được tên gói khi in phiếu và khi đối soát với đơn vị ký hợp đồng.
    /// </summary>
    public bool IsActive { get; set; } = true;

    public ICollection<ExamPackageService> Services { get; set; } = new List<ExamPackageService>();
}
