using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HealthExam.Application.Catalogs;
using HealthExam.Application.Common;
using Xunit;

namespace HealthExam.Tests.Application;

public class CatalogHandlerTests
{
    [Fact]
    public async Task ListOrganizations_normalizes_paging_and_forwards_division()
    {
        var repo = new FakeCatalogRepository();
        var handler = new ListOrganizationsHandler(repo);

        var result = await handler.HandleAsync(
            new ListOrganizationsQuery("D01", "abc", true, 0, 999));

        Assert.True(result.IsSuccess);
        Assert.Equal((1, 200, "D01"), repo.LastOrganizationRequest);
    }

    [Fact]
    public async Task ListExamPackages_normalizes_paging_and_forwards_division()
    {
        var repo = new FakeCatalogRepository();
        var handler = new ListExamPackagesHandler(repo);

        var result = await handler.HandleAsync(
            new ListExamPackagesQuery("D01", "gói", "DTK_01", true, -1, 0));

        Assert.True(result.IsSuccess);
        Assert.Equal((1, 20, "D01"), repo.LastExamPackageRequest);
    }

    [Fact]
    public async Task ListExamGroups_returns_domain_groups()
    {
        var handler = new ListExamGroupsHandler();

        var result = await handler.HandleAsync(new ListExamGroupsQuery());

        Assert.True(result.IsSuccess);
        Assert.Equal(10, result.Value.Count);
        Assert.Equal("DTK_01", result.Value[0].VariantCode);
        Assert.Equal("DTK_10", result.Value[9].VariantCode);
    }

    [Fact]
    public async Task GetRegistrationOptions_forwards_division()
    {
        var repo = new FakeCatalogRepository();
        var handler = new GetRegistrationOptionsHandler(repo);

        var result = await handler.HandleAsync(new GetRegistrationOptionsQuery("D02"));

        Assert.True(result.IsSuccess);
        Assert.Equal("D02", repo.LastRegistrationOptionsDivision);
    }

    [Fact]
    public async Task ListProvinces_forwards_division_and_keyword()
    {
        var repo = new FakeCatalogRepository();
        var handler = new ListProvincesHandler(repo);

        var result = await handler.HandleAsync(new ListProvincesQuery("D01", "Hà"));

        Assert.True(result.IsSuccess);
        Assert.Equal(("D01", "Hà"), repo.LastProvincesRequest);
    }

    [Fact]
    public async Task ListWards_validates_province_code_and_forwards()
    {
        var repo = new FakeCatalogRepository();
        var handler = new ListWardsHandler(repo);

        // Missing province code
        var emptyRes = await handler.HandleAsync(new ListWardsQuery("D01", "  ", ""));
        Assert.False(emptyRes.IsSuccess);
        Assert.Equal(ApplicationFailureCode.BadRequest, emptyRes.Failure.Code);
        var emptyErrors = Assert.IsType<ValidationErrors>(emptyRes.Failure.Payload);
        Assert.Equal("Mã tỉnh/thành phố không được để trống", emptyErrors["ProvinceCode"]);

        // Unknown province
        repo.WardReturnNull = true;
        var unknownRes = await handler.HandleAsync(new ListWardsQuery("D01", "99", ""));
        Assert.False(unknownRes.IsSuccess);
        Assert.Equal(ApplicationFailureCode.BadRequest, unknownRes.Failure.Code);
        var unknownErrors = Assert.IsType<ValidationErrors>(unknownRes.Failure.Payload);
        Assert.Equal("Tỉnh/thành phố không hợp lệ", unknownErrors["ProvinceCode"]);

        // Valid province
        repo.WardReturnNull = false;
        var validRes = await handler.HandleAsync(new ListWardsQuery("D01", "01", "Phúc"));
        Assert.True(validRes.IsSuccess);
        Assert.Equal(("D01", "01", "Phúc"), repo.LastWardsRequest);
    }

    [Fact]
    public async Task ListServiceCategories_forwards_division()
    {
        var repo = new FakeCatalogRepository();
        var handler = new ListServiceCategoriesHandler(repo);

        var result = await handler.HandleAsync(new ListServiceCategoriesQuery("D01"));

        Assert.True(result.IsSuccess);
        Assert.Equal("D01", repo.LastCategoriesDivision);
    }

    [Fact]
    public async Task ListServiceGroups_forwards_division_and_category()
    {
        var repo = new FakeCatalogRepository();
        var handler = new ListServiceGroupsHandler(repo);

        var result = await handler.HandleAsync(new ListServiceGroupsQuery("D01", "CDHA", "XQ"));

        Assert.True(result.IsSuccess);
        Assert.Equal(("D01", "CDHA", "XQ"), repo.LastGroupsRequest);
    }

    [Fact]
    public async Task ListServices_normalizes_paging_and_forwards_filter()
    {
        var repo = new FakeCatalogRepository();
        var handler = new ListServicesHandler(repo);

        var result = await handler.HandleAsync(
            new ListServicesQuery("D01", "XN", "SH", "máu", 0, 500));

        Assert.True(result.IsSuccess);
        Assert.NotNull(repo.LastServicesFilter);
        Assert.Equal("D01", repo.LastServicesDivision);
        Assert.Equal(1, repo.LastServicesFilter.Page);
        Assert.Equal(200, repo.LastServicesFilter.Size);
        Assert.Equal("XN", repo.LastServicesFilter.CategoryCode);
        Assert.Equal("SH", repo.LastServicesFilter.GroupCode);
        Assert.Equal("máu", repo.LastServicesFilter.Keyword);
    }
}

public class FakeCatalogRepository : ICatalogRepository
{
    public (int Page, int Size, string DivisionId) LastOrganizationRequest { get; private set; }
    public (int Page, int Size, string DivisionId) LastExamPackageRequest { get; private set; }
    public string LastRegistrationOptionsDivision { get; private set; }
    public (string DivisionId, string Keyword) LastProvincesRequest { get; private set; }
    public (string DivisionId, string ProvinceCode, string Keyword) LastWardsRequest { get; private set; }
    public bool WardReturnNull { get; set; }
    public string LastCategoriesDivision { get; private set; }
    public (string DivisionId, string CategoryCode, string Keyword) LastGroupsRequest { get; private set; }
    public string LastServicesDivision { get; private set; }
    public ServiceCatalogFilter LastServicesFilter { get; private set; }

    public Task<PageResult<OrganizationResult>> ListOrganizationsAsync(
        string divisionId, string keyword, bool? isActive, int page, int size,
        CancellationToken ct = default)
    {
        LastOrganizationRequest = (page, size, divisionId);
        return Task.FromResult(new PageResult<OrganizationResult>(
            Array.Empty<OrganizationResult>(), page, size, 0));
    }

    public Task<PageResult<ExamPackageResult>> ListExamPackagesAsync(
        string divisionId, string keyword, string variantCode, bool? isActive,
        int page, int size, CancellationToken ct = default)
    {
        LastExamPackageRequest = (page, size, divisionId);
        return Task.FromResult(new PageResult<ExamPackageResult>(
            Array.Empty<ExamPackageResult>(), page, size, 0));
    }

    public Task<RegistrationOptionsResult> GetRegistrationOptionsAsync(
        string divisionId, CancellationToken ct = default)
    {
        LastRegistrationOptionsDivision = divisionId;
        return Task.FromResult(new RegistrationOptionsResult());
    }

    public Task<IReadOnlyList<MasterDataOptionResult>> ListProvincesAsync(
        string divisionId, string keyword, CancellationToken ct = default)
    {
        LastProvincesRequest = (divisionId, keyword);
        return Task.FromResult<IReadOnlyList<MasterDataOptionResult>>(
            Array.Empty<MasterDataOptionResult>());
    }

    public Task<IReadOnlyList<MasterDataOptionResult>> ListWardsAsync(
        string divisionId, string provinceCode, string keyword,
        CancellationToken ct = default)
    {
        LastWardsRequest = (divisionId, provinceCode, keyword);
        if (WardReturnNull) return Task.FromResult<IReadOnlyList<MasterDataOptionResult>>(null);
        return Task.FromResult<IReadOnlyList<MasterDataOptionResult>>(
            Array.Empty<MasterDataOptionResult>());
    }

    public Task<IReadOnlyList<ServiceCategoryResult>> ListServiceCategoriesAsync(
        string divisionId, CancellationToken ct = default)
    {
        LastCategoriesDivision = divisionId;
        return Task.FromResult<IReadOnlyList<ServiceCategoryResult>>(
            Array.Empty<ServiceCategoryResult>());
    }

    public Task<IReadOnlyList<ServiceGroupResult>> ListServiceGroupsAsync(
        string divisionId, string categoryCode, string keyword = null,
        CancellationToken ct = default)
    {
        LastGroupsRequest = (divisionId, categoryCode, keyword);
        return Task.FromResult<IReadOnlyList<ServiceGroupResult>>(
            Array.Empty<ServiceGroupResult>());
    }

    public Task<PageResult<ServiceCatalogResult>> ListServicesAsync(
        string divisionId, ServiceCatalogFilter filter,
        CancellationToken ct = default)
    {
        LastServicesDivision = divisionId;
        LastServicesFilter = filter;
        return Task.FromResult(new PageResult<ServiceCatalogResult>(
            Array.Empty<ServiceCatalogResult>(), filter.Page, filter.Size, 0));
    }
}
