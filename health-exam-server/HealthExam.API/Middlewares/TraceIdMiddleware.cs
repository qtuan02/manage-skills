namespace HealthExam.API.Middlewares;

/// <summary>
/// Sinh (hoặc nhận lại) TraceID cho mọi request và trả về trong header + envelope,
/// để một khiếu nại từ bệnh viện đối soát được thẳng sang log Elasticsearch.
/// </summary>
public class TraceIdMiddleware
{
    private readonly RequestDelegate _next;

    public TraceIdMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        var incoming = context.Request.Headers[HealthExamRequestContext.TraceHeader].ToString();
        var traceId = string.IsNullOrWhiteSpace(incoming) ? NewTraceId() : incoming.Trim();

        context.Items[HealthExamRequestContext.TraceIdItemKey] = traceId;
        context.Response.Headers[HealthExamRequestContext.TraceHeader] = traceId;

        await _next(context);
    }

    /// <summary>26 ký tự Crockford base32, sắp xếp được theo thời gian (dạng ULID).</summary>
    private static string NewTraceId()
    {
        const string alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var chars = new char[26];

        for (var i = 9; i >= 0; i--)
        {
            chars[i] = alphabet[(int)(timestamp & 31)];
            timestamp >>= 5;
        }

        var random = new byte[16];
        Random.Shared.NextBytes(random);
        for (var i = 0; i < 16; i++) chars[10 + i] = alphabet[random[i] & 31];

        return new string(chars);
    }
}
