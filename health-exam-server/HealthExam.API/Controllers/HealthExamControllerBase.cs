using HealthExam.API.Middlewares;
using HealthExam.Application.Common;
using HealthExam.API.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace HealthExam.API.Controllers;

/// <summary>
/// Base cho mọi controller của health-exam-server: đóng gói envelope ResultData kèm TraceID,
/// để không controller nào phải tự nhớ gắn TraceID vào phản hồi.
/// </summary>
[ApiController]
[Route("v1")]
public abstract class HealthExamControllerBase : ControllerBase
{
    protected IHealthExamContext HealthExamContext =>
        HttpContext.RequestServices.GetRequiredService<IHealthExamContext>();

    protected ActionResult<ResultData<T>> Success<T>(T data)
        => Ok(ResultData<T>.Ok(data, HealthExamContext.TraceId));

    protected ActionResult<ResultData<T>> Failure<T>(int errorCode, string message = null, T data = default)
        => StatusCode(
            ErrorCodes.ToHttpStatus(errorCode),
            ResultData<T>.Fail(errorCode, message, data, HealthExamContext.TraceId));

    protected ActionResult<ResultData<T>> ToActionResult<T>(ApplicationResult<T> result)
        => ApplicationResultMapper.ToActionResult(result, HealthExamContext.TraceId);

    protected ActionResult<ResultData<TTarget>> ToActionResult<TSource, TTarget>(
        ApplicationResult<TSource> result, System.Func<TSource, TTarget> mapper)
    {
        if (result.IsSuccess)
        {
            return Success(mapper(result.Value));
        }

        return ApplicationResultMapper.ToActionResult<TTarget>(
            ApplicationResult<TTarget>.Fail(result.Failure.Code, result.Failure.Message, result.Failure.Payload),
            HealthExamContext.TraceId);
    }
}

