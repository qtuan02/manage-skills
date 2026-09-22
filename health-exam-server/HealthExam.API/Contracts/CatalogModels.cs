using System;

namespace HealthExam.API.Contracts;

/// <summary>Dòng dropdown đơn vị ký hợp đồng — GET /v1/organizations.</summary>
public class OrganizationItem
{
    public Guid OrganizationID { get; set; }
    public string OrgCode { get; set; } = "";
    public string OrgName { get; set; } = "";
    public string ShortName { get; set; } = "";
    public string TaxCode { get; set; } = "";
    public string Address { get; set; } = "";
    public string ContactName { get; set; } = "";
    public string ContactPhone { get; set; } = "";
    public string ContactEmail { get; set; } = "";
    public bool IsActive { get; set; }
}

/// <summary>Dòng dropdown gói khám — GET /v1/exam-packages.</summary>
public class ExamPackageItem
{
    public Guid PackageID { get; set; }
    public string PackageCode { get; set; } = "";
    public string PackageName { get; set; } = "";
    public string VariantCode { get; set; }
    public string Description { get; set; }
    /// <summary>Số dịch vụ đang hiệu lực trong gói — FE hiển thị "gói N dịch vụ".</summary>
    public int ServiceCount { get; set; }
    public bool IsActive { get; set; }
}

/// <summary>Dòng dropdown Nhóm khám — GET /v1/exam-groups (danh mục tĩnh, xem ExamGroups).</summary>
public class ExamGroupItem
{
    public string VariantCode { get; set; } = "";
    public string GroupName { get; set; } = "";
    /// <summary>FormCode tương ứng bên form-server để FE tra biểu mẫu ở bước 2.</summary>
    public string FormCode { get; set; } = "";
    public int OrderNo { get; set; }
}

/// <summary>Dòng dropdown khoa được cấp cho nhân viên hiện tại — GET /v1/departments.</summary>
public sealed class DepartmentCatalogItem
{
    public long DepartmentID { get; set; }
    public string DepartmentCode { get; set; } = "";
    public string DepartmentName { get; set; } = "";
    public long ParentDepartmentID { get; set; }
    public bool IsTraditional { get; set; }
}
