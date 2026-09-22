using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using HealthExam.API.Contracts;
using HealthExam.Application.Common;
using HealthExam.Application.ExamRecords;
using HealthExam.Application.Patients;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace HealthExam.API.Controllers;

[Route("v1/patients")]
public sealed class PatientController : HealthExamControllerBase
{
    private readonly ISearchPatientsHandler _searchHandler;
    private readonly IGetPatientProfileHandler _getHandler;
    private readonly IMatchPatientProfileHandler _matchHandler;
    private readonly IGetPatientExamHistoryHandler _examHistoryHandler;

    public PatientController(
        ISearchPatientsHandler searchHandler,
        IGetPatientProfileHandler getHandler,
        IMatchPatientProfileHandler matchHandler,
        IGetPatientExamHistoryHandler examHistoryHandler)
    {
        _searchHandler = searchHandler;
        _getHandler = getHandler;
        _matchHandler = matchHandler;
        _examHistoryHandler = examHistoryHandler;
    }

    /// <summary>
    /// Tìm hồ sơ active trong đơn vị theo CCCD, mã BN, tên, SĐT hoặc tự nhận diện.
    /// Không truyền keyword thì trả 20 hồ sơ mới cập nhật nhất. Luôn tối đa 20 dòng tóm tắt,
    /// mới cập nhật lên đầu; lấy chi tiết bằng GET /v1/patients/{patientRefId}.
    /// </summary>
    [HttpGet("search")]
    [SwaggerOperation(
        Summary = "Tìm người bệnh",
        Description = "type = identity | code | name | phone | auto (mặc định auto). Không truyền keyword thì trả 20 hồ sơ mới cập nhật nhất. Chỉ trả hồ sơ active trong X-Division-Id, tối đa 20 dòng tóm tắt, sắp theo ModifiedDate giảm dần.")]
    public async Task<ActionResult<ResultData<IReadOnlyList<PatientSearchItem>>>> Search(
        [FromQuery] string keyword, [FromQuery] string type = null, CancellationToken ct = default)
    {
        var result = await _searchHandler.HandleAsync(
            new SearchPatientsQuery(HealthExamContext.DivisionId, type, keyword), ct);
        return ToActionResult(result);
    }

    /// <summary>Lấy hồ sơ cá nhân đầy đủ để điền vào form đăng ký khám.</summary>
    [HttpGet("{patientRefId:guid}")]
    [SwaggerOperation(
        Summary = "Lấy hồ sơ người bệnh",
        Description = "Trả hồ sơ active theo PatientRefID trong X-Division-Id; 404 nếu không active hoặc khác đơn vị.")]
    public async Task<ActionResult<ResultData<PatientProfileResult>>> Get(
        Guid patientRefId, CancellationToken ct = default)
    {
        var result = await _getHandler.HandleAsync(
            new GetPatientProfileQuery(HealthExamContext.DivisionId, patientRefId), ct);
        return ToActionResult(result);
    }

    /// <summary>
    /// Danh sách các đợt khám trước của người bệnh — những hồ sơ KSK đã ký kết luận.
    /// </summary>
    [HttpGet("{patientRefId:guid}/exam-history")]
    [SwaggerOperation(
        Summary = "Lịch sử khám của người bệnh",
        Description = "Trả các hồ sơ KSK đã ký kết luận (HisSignStatus = Signed) của MỌI phiên bản hồ sơ cùng dòng (ProfileLineageID) trong X-Division-Id, mới nhất lên đầu theo ngày khám của đợt. size mặc định 5, tối đa 200; page mặc định 1. 404 nếu PatientRefID không thuộc đơn vị.")]
    public async Task<ActionResult<ResultData<PageResult<ExamRecordResult>>>> ExamHistory(
        Guid patientRefId,
        [FromQuery] int page = 1,
        [FromQuery] int size = 0,
        CancellationToken ct = default)
    {
        var result = await _examHistoryHandler.HandleAsync(
            new GetPatientExamHistoryQuery(HealthExamContext.DivisionId, patientRefId, page, size), ct);
        return ToActionResult(result);
    }

    [HttpPost("match")]
    public async Task<ActionResult<ResultData<MatchPatientProfileResult>>> Match(
        [FromBody] MatchPatientProfileRequest request, CancellationToken ct = default)
    {
        if (request == null)
        {
            return Failure<MatchPatientProfileResult>(ErrorCodes.BadRequest, "Dữ liệu không hợp lệ");
        }

        var profile = new PatientProfileValues(
            request.FullName,
            request.Dob,
            request.BirthYear,
            request.GenderID,
            request.IdentityNumber,
            request.IdentityIssuedDate,
            request.IdentityIssuerOptionID,
            request.PhoneNumber,
            request.Email,
            request.Address,
            request.EthnicityOptionID,
            request.BloodAboCode,
            request.BloodRhCode,
            request.ProvinceCode ?? "",
            request.WardCode ?? "");
        var result = await _matchHandler.HandleAsync(
            new MatchPatientProfileQuery(HealthExamContext.DivisionId, profile), ct);
        return ToActionResult(result);
    }
}

public class MatchPatientProfileRequest
{
    public string FullName { get; set; } = "";
    public DateOnly? Dob { get; set; }
    public short? BirthYear { get; set; }
    public short GenderID { get; set; }
    public string IdentityNumber { get; set; } = "";
    public DateOnly? IdentityIssuedDate { get; set; }
    public Guid? IdentityIssuerOptionID { get; set; }
    public string PhoneNumber { get; set; } = "";
    public string Email { get; set; } = "";
    public string Address { get; set; } = "";
    public Guid? EthnicityOptionID { get; set; }
    public string BloodAboCode { get; set; } = "";
    public string BloodRhCode { get; set; } = "";
    public string ProvinceCode { get; set; } = "";
    public string WardCode { get; set; } = "";
}
