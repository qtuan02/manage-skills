using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HealthExam.Application.Catalogs;
using HealthExam.Application.Common;
using HealthExam.Domain.Catalogs;
using HealthExam.Domain.ExamSessions;
using Microsoft.EntityFrameworkCore;

namespace HealthExam.Infrastructure.Persistence.Repositories;

public sealed class CatalogRepository : ICatalogRepository
{
    private readonly HealthExamDbContext _db;

    private static readonly IReadOnlyDictionary<string, string> ParaclinicalKinds =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["XN"] = "Xét nghiệm",
            ["CDHA"] = "Chẩn đoán hình ảnh",
            ["TDCN"] = "Thăm dò chức năng"
        };

    public static readonly IReadOnlyList<MasterDataOptionResult> StaticGenders = new[]
    {
        new MasterDataOptionResult("1", "Nam", 1),
        new MasterDataOptionResult("2", "Nữ", 2),
        new MasterDataOptionResult("3", "Khác", 3)
    };

    public static readonly IReadOnlyList<MasterDataOptionResult> StaticBloodAbos = new[]
    {
        new MasterDataOptionResult("A", "A", 1),
        new MasterDataOptionResult("B", "B", 2),
        new MasterDataOptionResult("AB", "AB", 3),
        new MasterDataOptionResult("O", "O", 4)
    };

    public static readonly IReadOnlyList<MasterDataOptionResult> StaticBloodRhs = new[]
    {
        new MasterDataOptionResult("+", "+", 1),
        new MasterDataOptionResult("-", "-", 2)
    };

    public static readonly IReadOnlyList<MasterDataOptionResult> StaticRelationships = new[]
    {
        new MasterDataOptionResult("FATHER", "Cha", 1),
        new MasterDataOptionResult("MOTHER", "Mẹ", 2),
        new MasterDataOptionResult("SPOUSE", "Vợ-chồng", 3),
        new MasterDataOptionResult("CHILD", "Con", 4),
        new MasterDataOptionResult("GUARDIAN", "Người giám hộ", 5),
        new MasterDataOptionResult("OTHER", "Khác", 6)
    };

    public static readonly IReadOnlyList<MasterDataOptionResult> StaticExamRecordStates = new[]
    {
        new MasterDataOptionResult("0", "Chưa đăng ký", 0),
        new MasterDataOptionResult("1", "Chờ khám", 1),
        new MasterDataOptionResult("2", "Đang khám", 2),
        new MasterDataOptionResult("3", "Đã khám", 3),
        new MasterDataOptionResult("4", "Hủy đăng ký", 4),
        new MasterDataOptionResult("5", "Hủy khám", 5)
    };

    public CatalogRepository(HealthExamDbContext db)
    {
        _db = db;
    }

    private IQueryable<ExamPackageService> Available(string divisionId)
        => _db.ExamPackageServices
            .Where(x => x.IsActive
                     && x.Package.IsActive
                     && x.Package.DivisionID == divisionId);

    public async Task<PageResult<OrganizationResult>> ListOrganizationsAsync(
        string divisionId, string keyword, bool? isActive, int page, int size,
        CancellationToken ct = default)
    {
        var q = _db.Organizations.Where(x => x.DivisionID == divisionId);

        if (isActive.HasValue) q = q.Where(x => x.IsActive == isActive.Value);
        else q = q.Where(x => x.IsActive);

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var kw = keyword.Trim().ToLower();
            q = q.Where(x => x.OrgName.ToLower().Contains(kw)
                          || x.OrgCode.ToLower().Contains(kw)
                          || x.TaxCode.ToLower().Contains(kw));
        }

        var total = await q.CountAsync(ct);
        var items = await q.OrderBy(x => x.OrgName)
            .Skip((page - 1) * size).Take(size)
            .Select(x => new OrganizationResult(
                x.OrganizationID,
                x.OrgCode,
                x.OrgName,
                x.ShortName,
                x.TaxCode,
                x.Address,
                x.ContactName,
                x.ContactPhone,
                x.ContactEmail,
                x.IsActive))
            .ToListAsync(ct);

        return new PageResult<OrganizationResult>(items, page, size, total);
    }

    public async Task<PageResult<ExamPackageResult>> ListExamPackagesAsync(
        string divisionId, string keyword, string variantCode, bool? isActive,
        int page, int size, CancellationToken ct = default)
    {
        var q = _db.ExamPackages.Where(x => x.DivisionID == divisionId);

        if (isActive.HasValue) q = q.Where(x => x.IsActive == isActive.Value);
        else q = q.Where(x => x.IsActive);

        if (!string.IsNullOrWhiteSpace(variantCode))
            q = q.Where(x => x.VariantCode == null || x.VariantCode == variantCode);

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var kw = keyword.Trim().ToLower();
            q = q.Where(x => x.PackageName.ToLower().Contains(kw) || x.PackageCode.ToLower().Contains(kw));
        }

        var total = await q.CountAsync(ct);
        var items = await q.OrderBy(x => x.PackageName)
            .Skip((page - 1) * size).Take(size)
            .Select(x => new ExamPackageResult(
                x.PackageID,
                x.PackageCode,
                x.PackageName,
                x.VariantCode,
                x.Description,
                x.Services.Count(s => s.IsActive),
                x.IsActive))
            .ToListAsync(ct);

        return new PageResult<ExamPackageResult>(items, page, size, total);
    }

    public async Task<RegistrationOptionsResult> GetRegistrationOptionsAsync(
        string divisionId, CancellationToken ct = default)
    {
        var tenantOptions = await _db.MasterDataOptions
            .Where(x => x.DivisionID == divisionId && x.IsActive)
            .OrderBy(x => x.OrderNo)
            .ThenBy(x => x.Name)
            .Select(x => new { x.Category, x.Code, x.Name, x.OrderNo })
            .ToListAsync(ct);

        List<MasterDataOptionResult> GetCategory(string cat) =>
            tenantOptions.Where(x => x.Category == cat)
                .Select(x => new MasterDataOptionResult(x.Code, x.Name, x.OrderNo))
                .ToList();

        var examGroups = ExamGroups.All
            .Select(x => new ExamGroupResult(x.VariantCode, x.GroupName, x.FormCode, x.OrderNo))
            .ToList();

        return new RegistrationOptionsResult
        {
            Genders = StaticGenders,
            Ethnicities = GetCategory(MasterDataCategories.Ethnicity),
            Occupations = GetCategory(MasterDataCategories.Occupation),
            BloodAbos = StaticBloodAbos,
            BloodRhs = StaticBloodRhs,
            IdentityIssuers = GetCategory(MasterDataCategories.IdentityIssuer),
            Relationships = StaticRelationships,
            InsuranceObjects = GetCategory(MasterDataCategories.InsuranceObject),
            PatientTypes = GetCategory(MasterDataCategories.PatientType),
            PatientSubjects = GetCategory(MasterDataCategories.PatientSubject),
            PaymentSources = GetCategory(MasterDataCategories.PaymentSource),
            ExamLocations = GetCategory(MasterDataCategories.ExamLocation),
            ExamRecordStates = StaticExamRecordStates,
            ExamGroups = examGroups
        };
    }

    public async Task<IReadOnlyList<MasterDataOptionResult>> ListProvincesAsync(
        string divisionId, string keyword, CancellationToken ct = default)
    {
        var query = _db.MasterDataOptions
            .Where(x => x.DivisionID == divisionId && x.Category == MasterDataCategories.Province && x.IsActive);

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var kw = keyword.Trim().ToLower();
            query = query.Where(x => x.Code.ToLower().Contains(kw) || x.Name.ToLower().Contains(kw));
        }

        return await query
            .OrderBy(x => x.OrderNo)
            .ThenBy(x => x.Name)
            .Select(x => new MasterDataOptionResult(x.Code, x.Name, x.OrderNo))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<MasterDataOptionResult>> ListWardsAsync(
        string divisionId, string provinceCode, string keyword,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(provinceCode))
            return null;

        var provCode = provinceCode.Trim();
        var provinceExists = await _db.MasterDataOptions.AnyAsync(
            x => x.DivisionID == divisionId && x.Category == MasterDataCategories.Province && x.Code == provCode && x.IsActive, ct);

        if (!provinceExists)
            return null;

        var query = _db.MasterDataOptions
            .Where(x => x.DivisionID == divisionId && x.Category == MasterDataCategories.Ward && x.ParentCode == provCode && x.IsActive);

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var kw = keyword.Trim().ToLower();
            query = query.Where(x => x.Code.ToLower().Contains(kw) || x.Name.ToLower().Contains(kw));
        }

        return await query
            .OrderBy(x => x.OrderNo)
            .ThenBy(x => x.Name)
            .Select(x => new MasterDataOptionResult(x.Code, x.Name, x.OrderNo))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<ServiceCategoryResult>> ListServiceCategoriesAsync(
        string divisionId, CancellationToken ct = default)
    {
        var counts = await Available(divisionId)
            .GroupBy(x => x.ParaclinicalKind)
            .Select(g => new { Kind = g.Key, Count = g.Select(x => x.ServiceID).Distinct().Count() })
            .ToListAsync(ct);

        return ParaclinicalKinds
            .Select(kv => new ServiceCategoryResult(
                kv.Key,
                kv.Value,
                counts.FirstOrDefault(c => string.Equals(c.Kind, kv.Key, StringComparison.OrdinalIgnoreCase))?.Count ?? 0
            ))
            .ToList();
    }

    public async Task<IReadOnlyList<ServiceGroupResult>> ListServiceGroupsAsync(
        string divisionId, string categoryCode, string keyword = null,
        CancellationToken ct = default)
    {
        var q = Available(divisionId);

        if (!string.IsNullOrWhiteSpace(categoryCode))
        {
            var kind = categoryCode.Trim();
            q = q.Where(x => x.ParaclinicalKind == kind);
        }

        var groups = await q
            .GroupBy(x => new { x.ParaclinicalKind, x.ServiceGroupCode })
            .Select(g => new
            {
                GroupCode = g.Key.ServiceGroupCode,
                GroupName = g.Key.ServiceGroupCode,
                CategoryCode = g.Key.ParaclinicalKind,
                ServiceCount = g.Select(x => x.ServiceID).Distinct().Count()
            })
            .ToListAsync(ct);

        var list = groups.Select(g => new ServiceGroupResult(
            g.GroupCode, g.GroupName, g.CategoryCode, g.ServiceCount));

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var kw = keyword.Trim();
            list = list.Where(x => x.GroupCode.Contains(kw, StringComparison.OrdinalIgnoreCase));
        }

        return list.OrderBy(x => x.CategoryCode).ThenBy(x => x.GroupCode).ToList();
    }

    public async Task<PageResult<ServiceCatalogResult>> ListServicesAsync(
        string divisionId, ServiceCatalogFilter filter,
        CancellationToken ct = default)
    {
        var q = Available(divisionId);

        if (!string.IsNullOrWhiteSpace(filter.CategoryCode))
        {
            var kind = filter.CategoryCode.Trim();
            q = q.Where(x => x.ParaclinicalKind == kind);
        }

        if (!string.IsNullOrWhiteSpace(filter.GroupCode))
        {
            var group = filter.GroupCode.Trim();
            q = q.Where(x => x.ServiceGroupCode == group);
        }

        if (!string.IsNullOrWhiteSpace(filter.Keyword))
        {
            var kw = filter.Keyword.Trim().ToLower();
            q = q.Where(x => x.ServiceName.ToLower().Contains(kw) || x.ServiceCode.ToLower().Contains(kw));
        }

        var grouped = q.GroupBy(x => x.ServiceID);
        var total = await grouped.CountAsync(ct);

        var rows = await grouped
            .OrderBy(g => g.Key)
            .Skip((filter.Page - 1) * filter.Size).Take(filter.Size)
            .Select(g => new
            {
                ServiceID = g.Key,
                Row = g.OrderByDescending(x => x.ModifiedDate).First(),
                Packages = g.Select(x => x.PackageID).Distinct().ToList()
            })
            .ToListAsync(ct);

        var items = rows.Select(r => new ServiceCatalogResult(
            r.ServiceID,
            r.Row.ServiceCode,
            r.Row.ServiceName,
            r.Row.ParaclinicalKind,
            r.Row.ServiceGroupCode,
            r.Packages
        )).ToList();

        return new PageResult<ServiceCatalogResult>(items, filter.Page, filter.Size, total);
    }
}
