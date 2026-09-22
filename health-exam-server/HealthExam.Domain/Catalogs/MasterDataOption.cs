using HealthExam.Domain.Common;

namespace HealthExam.Domain.Catalogs;

/// <summary>
/// HEX_MasterDataOption: danh mục cấu hình đa category theo tenant cho KSK.
/// </summary>
public class MasterDataOption : AuditableEntity
{
    public Guid OptionID { get; set; }
    public string DivisionID { get; set; } = "";
    public string Category { get; set; } = "";
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string ParentCode { get; set; } = "";
    public int OrderNo { get; set; }
    public bool IsActive { get; set; } = true;
}
