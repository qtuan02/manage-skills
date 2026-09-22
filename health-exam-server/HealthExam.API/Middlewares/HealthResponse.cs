using Microsoft.Extensions.Diagnostics.HealthChecks;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace HealthExam.API.Middlewares;

/// <summary>Trả kết quả health check dạng JSON để gateway và người trực đọc được ngay.</summary>
public static class HealthResponse
{
    private static readonly JsonSerializerSettings Settings = new()
    {
        ContractResolver = new DefaultContractResolver()
    };

    public static Task WriteAsync(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json; charset=utf-8";

        var body = new
        {
            Status = report.Status.ToString(),
            TotalDurationMs = (long)report.TotalDuration.TotalMilliseconds,
            Entries = report.Entries.ToDictionary(
                e => e.Key,
                e => new
                {
                    Status = e.Value.Status.ToString(),
                    Description = e.Value.Description,
                    DurationMs = (long)e.Value.Duration.TotalMilliseconds,
                    Error = e.Value.Exception?.Message
                })
        };

        return context.Response.WriteAsync(JsonConvert.SerializeObject(body, Settings));
    }
}
