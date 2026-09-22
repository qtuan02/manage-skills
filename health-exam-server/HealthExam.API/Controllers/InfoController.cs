using HealthExam.API.Contracts;
using HealthExam.Domain.Common;
using Microsoft.AspNetCore.Mvc;

namespace HealthExam.API.Controllers;

/// <summary>
/// Thông tin phiên bản service. Cũng là endpoint nghiệp vụ đầu tiên đi qua đủ chuỗi
/// middleware mà KHÔNG chạm DB, nên dùng để kiểm chứng gate "thiếu X-Division-Id trả 4001"
/// và để test chạy được khi không có database.
/// </summary>
[Route("v1/info")]
public class InfoController : HealthExamControllerBase
{
    [HttpGet]
    public ActionResult<ResultData<object>> Get()
    {
        var ctx = HealthExamContext;
        return Success<object>(new
        {
            Service = "health-exam-server",
            Version = Environment.GetEnvironmentVariable("CURRENT_VERSION") ?? "0.1.0",
            DivisionID = ctx.DivisionId,
            ModuleCode = string.IsNullOrWhiteSpace(ctx.ModuleCode) ? ModuleCodes.HealthExam : ctx.ModuleCode,
            ServerTime = DateTimeOffset.Now
        });
    }
}
