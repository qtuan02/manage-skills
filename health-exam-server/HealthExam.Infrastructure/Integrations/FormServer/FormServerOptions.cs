using System;

namespace HealthExam.Infrastructure.Integrations.FormServer;

public class FormServerOptions
{
    public const string BaseUrlEnv = "FORM_SERVER_BASE_URL";
    public const string DocTypeEnv = "HEALTH_EXAM_DOCTYPE_ID";
    public const string ServiceTokenEnv = "SECRET_INTER";
    public const int DefaultDocTypeId = 990001;
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(3);

    public string BaseUrl { get; init; } = "";
    public int DocTypeId { get; init; } = DefaultDocTypeId;
    public string DocTypeIdRaw { get; init; } = "";
    public string ServiceToken { get; init; } = "";

    public static FormServerOptions FromEnvironment()
    {
        var raw = (Environment.GetEnvironmentVariable(DocTypeEnv) ?? "").Trim();

        return new FormServerOptions
        {
            BaseUrl = (Environment.GetEnvironmentVariable(BaseUrlEnv) ?? "").Trim().TrimEnd('/'),
            ServiceToken = (Environment.GetEnvironmentVariable(ServiceTokenEnv) ?? "").Trim(),
            DocTypeIdRaw = raw,
            DocTypeId = raw.Length == 0
                ? DefaultDocTypeId
                : (int.TryParse(raw, out var v) ? v : 0)
        };
    }
}
