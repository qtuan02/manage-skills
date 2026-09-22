using System;
using System.Threading;
using System.Threading.Tasks;
using HealthExam.API.Contracts;
using HealthExam.Application.Common;
using HealthExam.Application.Paraclinical;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace HealthExam.API.Controllers;

/// <summary>
/// Tra cứu chi tiết kết quả cận lâm sàng theo resultId.
/// </summary>
[Route("v1/paraclinical-results")]
public class ParaclinicalResultController : HealthExamControllerBase
{
    private readonly IGetResultHandler _getResultHandler;

    public ParaclinicalResultController(IGetResultHandler getResultHandler)
    {
        _getResultHandler = getResultHandler;
    }

    [HttpGet("{resultId:guid}")]
    [SwaggerOperation(
        Summary = "Lấy chi tiết kết quả cận lâm sàng",
        Description = "Trả về thông tin kết quả và các chỉ số xét nghiệm / mô tả CĐHA của một kết quả cận lâm sàng.")]
    public async Task<ActionResult<ResultData<ParaclinicalResultDto>>> Get(
        Guid resultId, CancellationToken ct = default)
    {
        var res = await _getResultHandler.HandleAsync(
            new GetResultQuery(HealthExamContext.DivisionId, resultId), ct);
        return ToActionResult(res);
    }
}
