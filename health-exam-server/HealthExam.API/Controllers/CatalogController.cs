using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HealthExam.API.Contracts;
using HealthExam.Application.Catalogs;
using HealthExam.Application.Common;
using HealthExam.Application.His;
using HealthExam.Application.Integrations;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace HealthExam.API.Controllers;

/// <summary>
/// Danh mục cho dropdown màn Đăng ký KSK — 02-api-spec §3.5 + Nhóm khám (03-ksk-mapping §2.2).
///
/// PHASE ĐĂNG KÝ: CHỈ ĐỌC. POST/PUT/DELETE của đơn vị và gói khám chưa làm; dữ liệu vào bằng
/// Deploy/seed/health-exam-seed.sql.
/// </summary>
public class CatalogController : HealthExamControllerBase
{
    private readonly IListOrganizationsHandler _organizationsHandler;
    private readonly IListExamPackagesHandler _packagesHandler;
    private readonly IListExamGroupsHandler _examGroupsHandler;
    private readonly IGetIcd10ChoicesHandler _icd10Handler;
    private readonly IListEmployeeDepartmentsHandler _departmentsHandler;
    private readonly IHisCredentialOptions _credentialOptions;

    public CatalogController(
        IListOrganizationsHandler organizationsHandler,
        IListExamPackagesHandler packagesHandler,
        IListExamGroupsHandler examGroupsHandler,
        IGetIcd10ChoicesHandler icd10Handler,
        IListEmployeeDepartmentsHandler departmentsHandler,
        IHisCredentialOptions credentialOptions)
    {
        _organizationsHandler = organizationsHandler;
        _packagesHandler = packagesHandler;
        _examGroupsHandler = examGroupsHandler;
        _icd10Handler = icd10Handler;
        _departmentsHandler = departmentsHandler;
        _credentialOptions = credentialOptions;
    }

    private string Credential => Request.Headers[_credentialOptions.CredentialHeaderName].ToString();

    [HttpGet("organizations")]
    [SwaggerOperation(
        Summary = "Tra cứu đơn vị đăng ký khám",
        Description = "Trả về danh sách đơn vị tổ chức ký hợp đồng khám sức khỏe, có thể lọc theo từ khóa, trạng thái hoạt động và phân trang.")]
    public async Task<ActionResult<ResultData<PaginationData<OrganizationItem>>>> Organizations(
        [FromQuery] string keyword = null, [FromQuery] bool? isActive = null,
        [FromQuery] int page = 1, [FromQuery] int size = 20, CancellationToken ct = default)
    {
        var result = await _organizationsHandler.HandleAsync(
            new ListOrganizationsQuery(HealthExamContext.DivisionId, keyword, isActive, page, size), ct);

        if (!result.IsSuccess)
        {
            return ToActionResult(ApplicationResult<PaginationData<OrganizationItem>>.Fail(
                result.Failure.Code, result.Failure.Message, result.Failure.Payload));
        }

        var pageResult = result.Value;
        var items = pageResult.Items.Select(x => new OrganizationItem
        {
            OrganizationID = x.OrganizationID,
            OrgCode = x.OrgCode,
            OrgName = x.OrgName,
            ShortName = x.ShortName,
            TaxCode = x.TaxCode,
            Address = x.Address,
            ContactName = x.ContactName,
            ContactPhone = x.ContactPhone,
            ContactEmail = x.ContactEmail,
            IsActive = x.IsActive
        }).ToList();

        var paginated = new PaginationData<OrganizationItem>(items, pageResult.Page, pageResult.Size, pageResult.Total);
        return ToActionResult(ApplicationResult<PaginationData<OrganizationItem>>.Success(paginated));
    }

    [HttpGet("exam-packages")]
    [SwaggerOperation(
        Summary = "Tra cứu gói khám sức khỏe",
        Description = "Trả về các gói khám được phép đăng ký; hỗ trợ lọc theo tên, mã biến thể, trạng thái hoạt động và phân trang.")]
    public async Task<ActionResult<ResultData<PaginationData<ExamPackageItem>>>> ExamPackages(
        [FromQuery] string keyword = null, [FromQuery] string variantCode = null,
        [FromQuery] bool? isActive = null,
        [FromQuery] int page = 1, [FromQuery] int size = 20, CancellationToken ct = default)
    {
        var result = await _packagesHandler.HandleAsync(
            new ListExamPackagesQuery(HealthExamContext.DivisionId, keyword, variantCode, isActive, page, size), ct);

        if (!result.IsSuccess)
        {
            return ToActionResult(ApplicationResult<PaginationData<ExamPackageItem>>.Fail(
                result.Failure.Code, result.Failure.Message, result.Failure.Payload));
        }

        var pageResult = result.Value;
        var items = pageResult.Items.Select(x => new ExamPackageItem
        {
            PackageID = x.PackageID,
            PackageCode = x.PackageCode,
            PackageName = x.PackageName,
            VariantCode = x.VariantCode,
            Description = x.Description,
            ServiceCount = x.ServiceCount,
            IsActive = x.IsActive
        }).ToList();

        var paginated = new PaginationData<ExamPackageItem>(items, pageResult.Page, pageResult.Size, pageResult.Total);
        return ToActionResult(ApplicationResult<PaginationData<ExamPackageItem>>.Success(paginated));
    }

    /// <summary>
    /// 10 Nhóm khám DTK_01..DTK_10 cho dropdown bước 2 wizard.
    /// Không phân trang: danh mục đóng đúng 10 dòng, phân trang chỉ làm FE thêm việc.
    /// </summary>
    [HttpGet("exam-groups")]
    [SwaggerOperation(
        Summary = "Lấy danh sách nhóm khám",
        Description = "Trả về toàn bộ nhóm khám dùng cho bước chọn nội dung khám trong quy trình đăng ký hồ sơ.")]
    public async Task<ActionResult<ResultData<IReadOnlyList<ExamGroupItem>>>> ExamGroups(
        CancellationToken ct = default)
    {
        var result = await _examGroupsHandler.HandleAsync(new ListExamGroupsQuery(), ct);

        if (!result.IsSuccess)
        {
            return ToActionResult(ApplicationResult<IReadOnlyList<ExamGroupItem>>.Fail(
                result.Failure.Code, result.Failure.Message, result.Failure.Payload));
        }

        var items = result.Value.Select(x => new ExamGroupItem
        {
            VariantCode = x.VariantCode,
            GroupName = x.GroupName,
            FormCode = x.FormCode,
            OrderNo = x.OrderNo
        }).ToList();

        return ToActionResult(ApplicationResult<IReadOnlyList<ExamGroupItem>>.Success(items));
    }

    [HttpGet("departments")]
    [SwaggerOperation(
        Summary = "Lấy danh sách khoa của nhân viên hiện tại",
        Description = "Trả về các khoa HIS cấp quyền cho tài khoản đang đăng nhập.")]
    public async Task<ActionResult<ResultData<IReadOnlyList<DepartmentCatalogItem>>>> Departments(
        CancellationToken ct = default)
    {
        if (HealthExamContext.ActorKind != Domain.Common.ActorKind.Employee || HealthExamContext.ActorId <= 0)
        {
            return Failure<IReadOnlyList<DepartmentCatalogItem>>(
                ErrorCodes.Forbidden,
                "Chỉ nhân viên đã xác thực mới được lấy danh sách khoa");
        }

        var result = await _departmentsHandler.HandleAsync(
            new ListEmployeeDepartmentsQuery(HealthExamContext.DivisionId, HealthExamContext.ActorId, Credential, HealthExamContext.TraceId), ct);

        if (!result.IsSuccess)
        {
            return ToActionResult(ApplicationResult<IReadOnlyList<DepartmentCatalogItem>>.Fail(
                result.Failure.Code, result.Failure.Message, result.Failure.Payload));
        }

        var items = result.Value.Select(x => new DepartmentCatalogItem
        {
            DepartmentID = x.DepartmentId,
            DepartmentCode = x.DepartmentCode,
            DepartmentName = x.DepartmentName,
            ParentDepartmentID = x.ParentDepartmentId,
            IsTraditional = x.IsTraditional
        }).ToList();

        return ToActionResult(ApplicationResult<IReadOnlyList<DepartmentCatalogItem>>.Success(items));
    }

    [HttpGet("catalogs/icd10")]
    [SwaggerOperation(
        Summary = "Tra cứu danh mục chẩn đoán ICD-10 từ HIS",
        Description = "Tìm kiếm danh mục ICD-10 phục vụ chẩn đoán sơ bộ và xác định, hỗ trợ lọc theo từ khóa và giới hạn số lượng.")]
    public async Task<ActionResult<ResultData<IReadOnlyList<Icd10ChoiceItem>>>> Icd10(
        [FromQuery] string filter = null,
        [FromQuery] int amount = 20,
        CancellationToken ct = default)
    {
        var result = await _icd10Handler.HandleAsync(
            new GetIcd10ChoicesQuery(HealthExamContext.DivisionId, filter ?? "", amount, Credential, HealthExamContext.TraceId), ct);

        if (!result.IsSuccess)
        {
            return ToActionResult(ApplicationResult<IReadOnlyList<Icd10ChoiceItem>>.Fail(
                result.Failure.Code, result.Failure.Message, result.Failure.Payload));
        }

        var items = result.Value.Select(x => new Icd10ChoiceItem(x.Code, x.Label, x.CodeName, x.DisplayName)).ToList();
        return ToActionResult(ApplicationResult<IReadOnlyList<Icd10ChoiceItem>>.Success(items));
    }
}
