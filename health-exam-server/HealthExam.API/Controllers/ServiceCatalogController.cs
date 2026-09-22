using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HealthExam.API.Contracts;
using HealthExam.Application.Catalogs;
using HealthExam.Application.Common;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace HealthExam.API.Controllers;

/// <summary>
/// Danh mục dịch vụ CLS 3 tầng cho pop-up chỉ định — 02-api-spec §3.4.
///
/// ⚠️ Nguồn dữ liệu của P3a là ảnh chụp dịch vụ trong GÓI KHÁM, chưa phải danh mục HIS.
/// </summary>
[Route("v1/service-catalog")]
public class ServiceCatalogController : HealthExamControllerBase
{
    private readonly IListServiceCategoriesHandler _categoriesHandler;
    private readonly IListServiceGroupsHandler _groupsHandler;
    private readonly IListServicesHandler _servicesHandler;

    public ServiceCatalogController(
        IListServiceCategoriesHandler categoriesHandler,
        IListServiceGroupsHandler groupsHandler,
        IListServicesHandler servicesHandler)
    {
        _categoriesHandler = categoriesHandler;
        _groupsHandler = groupsHandler;
        _servicesHandler = servicesHandler;
    }

    /// <summary>Tầng 1 — XN / CĐHA / TDCN. Không phân trang: danh mục đóng đúng 3 dòng.</summary>
    [HttpGet("categories")]
    [SwaggerOperation(
        Summary = "Lấy danh mục dịch vụ cận lâm sàng",
        Description = "Trả về các nhóm lớn dịch vụ cận lâm sàng (xét nghiệm, chẩn đoán hình ảnh, thăm dò chức năng) để bắt đầu tra cứu.")]
    public async Task<ActionResult<ResultData<IReadOnlyList<ServiceCategoryItem>>>> Categories(
        CancellationToken ct = default)
    {
        var result = await _categoriesHandler.HandleAsync(
            new ListServiceCategoriesQuery(HealthExamContext.DivisionId), ct);

        if (!result.IsSuccess)
        {
            return ToActionResult(ApplicationResult<IReadOnlyList<ServiceCategoryItem>>.Fail(
                result.Failure.Code, result.Failure.Message, result.Failure.Payload));
        }

        var items = result.Value.Select(x => new ServiceCategoryItem
        {
            CategoryCode = x.CategoryCode,
            CategoryName = x.CategoryName,
            ServiceCount = x.ServiceCount
        }).ToList();

        return ToActionResult(ApplicationResult<IReadOnlyList<ServiceCategoryItem>>.Success(items));
    }

    [HttpGet("groups")]
    [SwaggerOperation(
        Summary = "Lấy nhóm dịch vụ cận lâm sàng",
        Description = "Lọc danh sách nhóm dịch vụ theo mã danh mục và từ khóa; dùng để chọn nhóm trước khi tra cứu dịch vụ chi tiết.")]
    public async Task<ActionResult<ResultData<IReadOnlyList<ServiceGroupItem>>>> Groups(
        [FromQuery] string categoryCode = null, [FromQuery] string keyword = null,
        CancellationToken ct = default)
    {
        var result = await _groupsHandler.HandleAsync(
            new ListServiceGroupsQuery(HealthExamContext.DivisionId, categoryCode, keyword), ct);

        if (!result.IsSuccess)
        {
            return ToActionResult(ApplicationResult<IReadOnlyList<ServiceGroupItem>>.Fail(
                result.Failure.Code, result.Failure.Message, result.Failure.Payload));
        }

        var items = result.Value.Select(x => new ServiceGroupItem
        {
            GroupCode = x.GroupCode,
            GroupName = x.GroupName,
            CategoryCode = x.CategoryCode,
            ServiceCount = x.ServiceCount
        }).ToList();

        return ToActionResult(ApplicationResult<IReadOnlyList<ServiceGroupItem>>.Success(items));
    }

    [HttpGet("services")]
    [SwaggerOperation(
        Summary = "Tra cứu dịch vụ cận lâm sàng",
        Description = "Trả về các dịch vụ trong danh mục snapshot của gói khám, có thể lọc theo danh mục, nhóm, từ khóa và phân trang.")]
    public async Task<ActionResult<ResultData<PaginationData<ServiceCatalogItem>>>> Services(
        [FromQuery] string categoryCode = null, [FromQuery] string groupCode = null,
        [FromQuery] string keyword = null,
        [FromQuery] int page = 1, [FromQuery] int size = 20, CancellationToken ct = default)
    {
        var result = await _servicesHandler.HandleAsync(
            new ListServicesQuery(HealthExamContext.DivisionId, categoryCode, groupCode, keyword, page, size), ct);

        if (!result.IsSuccess)
        {
            return ToActionResult(ApplicationResult<PaginationData<ServiceCatalogItem>>.Fail(
                result.Failure.Code, result.Failure.Message, result.Failure.Payload));
        }

        var pageResult = result.Value;
        var items = pageResult.Items.Select(x => new ServiceCatalogItem
        {
            ServiceID = x.ServiceID,
            ServiceCode = x.ServiceCode,
            ServiceName = x.ServiceName,
            CategoryCode = x.CategoryCode,
            GroupCode = x.GroupCode,
            InPackages = x.InPackages.ToList()
        }).ToList();

        var paginated = new PaginationData<ServiceCatalogItem>(items, pageResult.Page, pageResult.Size, pageResult.Total);
        return ToActionResult(ApplicationResult<PaginationData<ServiceCatalogItem>>.Success(paginated));
    }
}
