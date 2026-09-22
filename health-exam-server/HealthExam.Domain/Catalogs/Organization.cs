using HealthExam.Domain.Common;

namespace HealthExam.Domain.Catalogs;

/// <summary>
/// HEX_Organization — đơn vị ký hợp đồng khám (01-db-model §3.1).
/// Danh mục RIÊNG của KSK, không mượn bảng đối tác/nhà cung cấp của HIS: mượn bảng bên kia
/// là tái lập đúng thứ phụ thuộc mà việc tách service đang muốn gỡ.
/// </summary>
public class Organization : AuditableEntity
{
    public Guid OrganizationID { get; set; }
    public string DivisionID { get; set; } = "";
    public string OrgCode { get; set; } = "";
    public string OrgName { get; set; } = "";
    public string ShortName { get; set; } = "";
    public string TaxCode { get; set; } = "";
    public string Address { get; set; } = "";
    public string ContactName { get; set; } = "";
    public string ContactPhone { get; set; } = "";
    public string ContactEmail { get; set; } = "";
    public string Note { get; set; }

    /// <summary>Soft-delete. Đợt khám cũ vẫn phải tra ngược được tên đơn vị.</summary>
    public bool IsActive { get; set; } = true;
}
