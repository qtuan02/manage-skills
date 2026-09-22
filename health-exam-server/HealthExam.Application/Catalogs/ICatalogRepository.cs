using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using HealthExam.Application.Common;

namespace HealthExam.Application.Catalogs;

public interface ICatalogRepository
{
    Task<PageResult<OrganizationResult>> ListOrganizationsAsync(
        string divisionId, string keyword, bool? isActive, int page, int size,
        CancellationToken ct = default);

    Task<PageResult<ExamPackageResult>> ListExamPackagesAsync(
        string divisionId, string keyword, string variantCode, bool? isActive,
        int page, int size, CancellationToken ct = default);

    Task<RegistrationOptionsResult> GetRegistrationOptionsAsync(
        string divisionId, CancellationToken ct = default);

    Task<IReadOnlyList<MasterDataOptionResult>> ListProvincesAsync(
        string divisionId, string keyword, CancellationToken ct = default);

    Task<IReadOnlyList<MasterDataOptionResult>> ListWardsAsync(
        string divisionId, string provinceCode, string keyword,
        CancellationToken ct = default);

    Task<IReadOnlyList<ServiceCategoryResult>> ListServiceCategoriesAsync(
        string divisionId, CancellationToken ct = default);

    Task<IReadOnlyList<ServiceGroupResult>> ListServiceGroupsAsync(
        string divisionId, string categoryCode, string keyword = null,
        CancellationToken ct = default);

    Task<PageResult<ServiceCatalogResult>> ListServicesAsync(
        string divisionId, ServiceCatalogFilter filter,
        CancellationToken ct = default);
}
