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

[Route("v1/master-data")]
[SwaggerTag("Danh mục phục vụ đăng ký KSK (Dân tộc, Nghề nghiệp, Tỉnh/Thành, Phường/Xã, Quan hệ, Lý do hủy)")]
public class MasterDataController : HealthExamControllerBase
{
    private readonly IGetRegistrationOptionsHandler _optionsHandler;
    private readonly IListProvincesHandler _provincesHandler;
    private readonly IListWardsHandler _wardsHandler;

    public MasterDataController(
        IGetRegistrationOptionsHandler optionsHandler,
        IListProvincesHandler provincesHandler,
        IListWardsHandler wardsHandler)
    {
        _optionsHandler = optionsHandler;
        _provincesHandler = provincesHandler;
        _wardsHandler = wardsHandler;
    }

    [HttpGet("registration-options")]
    [SwaggerOperation(
        Summary = "Lấy toàn bộ danh mục tĩnh phục vụ đăng ký KSK",
        Description = "Trả về options cho dân tộc, nghề nghiệp, quan hệ người giám hộ, lý do hủy.")]
    public async Task<ActionResult<ResultData<RegistrationOptionsItem>>> RegistrationOptions(CancellationToken ct)
    {
        var result = await _optionsHandler.HandleAsync(
            new GetRegistrationOptionsQuery(HealthExamContext.DivisionId), ct);

        if (!result.IsSuccess)
        {
            return ToActionResult(ApplicationResult<RegistrationOptionsItem>.Fail(
                result.Failure.Code, result.Failure.Message, result.Failure.Payload));
        }

        var opt = result.Value;
        static MasterDataOptionItem Map(MasterDataOptionResult r) => new(r.Code, r.Name, r.OrderNo);

        var item = new RegistrationOptionsItem
        {
            Genders = opt.Genders.Select(Map).ToList(),
            Ethnicities = opt.Ethnicities.Select(Map).ToList(),
            Occupations = opt.Occupations.Select(Map).ToList(),
            BloodAbos = opt.BloodAbos.Select(Map).ToList(),
            BloodRhs = opt.BloodRhs.Select(Map).ToList(),
            IdentityIssuers = opt.IdentityIssuers.Select(Map).ToList(),
            Relationships = opt.Relationships.Select(Map).ToList(),
            InsuranceObjects = opt.InsuranceObjects.Select(Map).ToList(),
            PatientTypes = opt.PatientTypes.Select(Map).ToList(),
            PatientSubjects = opt.PatientSubjects.Select(Map).ToList(),
            PaymentSources = opt.PaymentSources.Select(Map).ToList(),
            ExamLocations = opt.ExamLocations.Select(Map).ToList(),
            ExamRecordStates = opt.ExamRecordStates.Select(Map).ToList(),
            ExamGroups = opt.ExamGroups.Select(g => new ExamGroupItem
            {
                VariantCode = g.VariantCode,
                GroupName = g.GroupName,
                FormCode = g.FormCode,
                OrderNo = g.OrderNo
            }).ToList()
        };

        return ToActionResult(ApplicationResult<RegistrationOptionsItem>.Success(item));
    }

    [HttpGet("provinces")]
    [SwaggerOperation(
        Summary = "Danh sách Tỉnh/Thành phố",
        Description = "Lọc theo từ khóa tìm kiếm (keyword).")]
    public async Task<ActionResult<ResultData<IReadOnlyList<MasterDataOptionItem>>>> Provinces(
        [FromQuery] string keyword = null, CancellationToken ct = default)
    {
        var result = await _provincesHandler.HandleAsync(
            new ListProvincesQuery(HealthExamContext.DivisionId, keyword), ct);

        if (!result.IsSuccess)
        {
            return ToActionResult(ApplicationResult<IReadOnlyList<MasterDataOptionItem>>.Fail(
                result.Failure.Code, result.Failure.Message, result.Failure.Payload));
        }

        var items = result.Value.Select(r => new MasterDataOptionItem(r.Code, r.Name, r.OrderNo)).ToList();
        return ToActionResult(ApplicationResult<IReadOnlyList<MasterDataOptionItem>>.Success(items));
    }

    [HttpGet("wards")]
    [SwaggerOperation(
        Summary = "Danh sách Quận/Huyện/Phường/Xã",
        Description = "Lọc theo mã tỉnh (provinceCode) và từ khóa tìm kiếm (keyword).")]
    public async Task<ActionResult<ResultData<IReadOnlyList<MasterDataOptionItem>>>> Wards(
        [FromQuery] string provinceCode = null, [FromQuery] string keyword = null, CancellationToken ct = default)
    {
        var result = await _wardsHandler.HandleAsync(
            new ListWardsQuery(HealthExamContext.DivisionId, provinceCode, keyword), ct);

        if (!result.IsSuccess)
        {
            return ToActionResult(ApplicationResult<IReadOnlyList<MasterDataOptionItem>>.Fail(
                result.Failure.Code, result.Failure.Message, result.Failure.Payload));
        }

        var items = result.Value.Select(r => new MasterDataOptionItem(r.Code, r.Name, r.OrderNo)).ToList();
        return ToActionResult(ApplicationResult<IReadOnlyList<MasterDataOptionItem>>.Success(items));
    }
}
