using System;

namespace HealthExam.Infrastructure.Integrations.SignServer;

public class SignServerOptions
{
    public string SsmBaseUrl { get; init; } = "";
    public string SignServerBaseUrl { get; init; } = "";
    public string InternalSecret { get; init; } = "";
    public int TimeoutSeconds { get; init; } = 60;

    public static SignServerOptions FromEnvironment() => new()
    {
        SsmBaseUrl = Environment.GetEnvironmentVariable("SSM_BASE_URL")?.Trim() ?? "",
        SignServerBaseUrl = Environment.GetEnvironmentVariable("SIGN_SERVER_BASE_URL")?.Trim() ?? "",
        InternalSecret = Environment.GetEnvironmentVariable("SECRET_INTER")?.Trim() ?? "",
        TimeoutSeconds = int.TryParse(
            Environment.GetEnvironmentVariable("SIGN_SERVER_TIMEOUT_SECONDS"), out var t) && t > 0 ? t : 60
    };
}
