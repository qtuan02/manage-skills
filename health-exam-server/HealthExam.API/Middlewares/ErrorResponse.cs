using System;
using HealthExam.API.Contracts;
using HealthExam.Application.Common;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace HealthExam.API.Middlewares;

public class HealthExamContractResolver : DefaultContractResolver
{
    protected override JsonContract CreateContract(Type objectType)
    {
        if (typeof(ValidationErrors).IsAssignableFrom(objectType))
        {
            return CreateObjectContract(objectType);
        }
        return base.CreateContract(objectType);
    }
}

/// <summary>Ghi envelope ResultData ra response — dùng chung cho middleware và filter.</summary>
public static class ErrorResponse
{
    private static readonly JsonSerializerSettings Settings = new()
    {
        ContractResolver = new HealthExamContractResolver()   // giữ PascalCase như FE đang đọc
    };

    public static Task WriteAsync(HttpContext context, int errorCode, string message = null, object data = null)
    {
        var traceId = context.Items[HealthExamRequestContext.TraceIdItemKey] as string ?? "";
        var body = new ResultData<object>
        {
            ErrorCode = errorCode,
            Message = message ?? ErrorCodes.DefaultMessage(errorCode),
            Data = data,
            TraceID = traceId
        };

        context.Response.StatusCode = ErrorCodes.ToHttpStatus(errorCode);
        context.Response.ContentType = "application/json; charset=utf-8";
        return context.Response.WriteAsync(JsonConvert.SerializeObject(body, Settings));
    }
}
