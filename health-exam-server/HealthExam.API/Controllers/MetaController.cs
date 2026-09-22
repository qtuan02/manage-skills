using HealthExam.API.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace HealthExam.API.Controllers;

/// <summary>
/// Endpoint hạ tầng để kiểm tra nhanh middleware đã gắn đúng chưa: gọi được nghĩa là
/// X-Division-Id đã qua chốt chặn và TraceID đã sinh. Không chạm DB.
/// </summary>
public class MetaController : HealthExamControllerBase
{
    [HttpGet("meta/ping")]
    public ActionResult<ResultData<object>> Ping()
    {
        var ctx = HealthExamContext;
        return Success<object>(new
        {
            Service = "health-exam-server",
            ctx.DivisionId,
            ctx.ModuleCode,
            ctx.Scope,
            ServerTime = DateTimeOffset.Now
        });
    }

    /// <summary>
    /// Danh tính mà server ĐỌC RA từ token. Có endpoint này để khi cột audit ghi sai người,
    /// việc đầu tiên là hỏi thẳng server "anh thấy tôi là ai" thay vì đi đọc log đoán claim.
    /// Không trả claim thô — chỉ trả đúng thứ health-exam-server thật sự dùng.
    /// </summary>
    [HttpGet("meta/whoami")]
    public ActionResult<ResultData<object>> WhoAmI()
    {
        var ctx = HealthExamContext;
        return Success<object>(new
        {
            ctx.ActorId,
            ActorKind = (short)ctx.ActorKind,
            ctx.ActorName,
            ctx.Scope,
            ctx.DivisionId
        });
    }
}
