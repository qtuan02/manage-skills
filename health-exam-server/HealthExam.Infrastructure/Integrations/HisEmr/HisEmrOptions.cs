using System;
using HealthExam.Application.Integrations;

namespace HealthExam.Infrastructure.Integrations.HisEmr;

public sealed class HisEmrOptions : IHisCredentialOptions
{
    public const string EnabledEnv = "HIS_EMR_ENABLED";
    public const string BaseUrlEnv = "HIS_EMR_BASE_URL";
    public const string TimeoutEnv = "HIS_EMR_TIMEOUT_SECONDS";
    public const string CredentialHeaderEnv = "HIS_EMR_CREDENTIAL_HEADER";
    public const string CacheSecondsEnv = "HIS_EMR_DEFINITION_CACHE_SECONDS";
    public const string KskDepartmentIdEnv = "HIS_EMR_KSK_DEPARTMENT_ID";
    public const string KskDepartmentCodeEnv = "HIS_EMR_KSK_DEPARTMENT_CODE";
    public const string PatientCreateRouteEnv = "HIS_EMR_PATIENT_CREATE_ROUTE";

    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);
    public static readonly TimeSpan DefaultDefinitionCacheDuration = TimeSpan.FromSeconds(300);

    public bool Enabled { get; init; }
    public string BaseUrl { get; init; } = "";
    public TimeSpan Timeout { get; init; } = DefaultTimeout;
    public string CredentialHeaderName { get; init; } = "Authorization";
    public TimeSpan DefinitionCacheDuration { get; init; } = DefaultDefinitionCacheDuration;
    public int? KskDepartmentId { get; init; }
    public string KskDepartmentCode { get; init; }
    public string PatientCreateRoute { get; init; } = "";

    public static HisEmrOptions FromEnvironment()
    {
        var enabledRaw = Environment.GetEnvironmentVariable(EnabledEnv);
        var enabled = string.Equals(enabledRaw, "true", StringComparison.OrdinalIgnoreCase) ||
                      string.Equals(enabledRaw, "1", StringComparison.OrdinalIgnoreCase);

        var baseUrl = Environment.GetEnvironmentVariable(BaseUrlEnv)?.Trim() ?? "";

        var timeout = DefaultTimeout;
        var timeoutRaw = Environment.GetEnvironmentVariable(TimeoutEnv);
        if (int.TryParse(timeoutRaw, out var timeoutSec) && timeoutSec > 0)
        {
            timeout = TimeSpan.FromSeconds(timeoutSec);
        }

        var credHeader = Environment.GetEnvironmentVariable(CredentialHeaderEnv)?.Trim();
        if (string.IsNullOrEmpty(credHeader))
        {
            credHeader = "Authorization";
        }

        var cacheDuration = DefaultDefinitionCacheDuration;
        var cacheRaw = Environment.GetEnvironmentVariable(CacheSecondsEnv);
        if (int.TryParse(cacheRaw, out var cacheSec) && cacheSec >= 0)
        {
            cacheDuration = TimeSpan.FromSeconds(cacheSec);
        }

        int? kskDepartmentId = null;
        var departmentRaw = Environment.GetEnvironmentVariable(KskDepartmentIdEnv);
        if (int.TryParse(departmentRaw, out var departmentId) && departmentId > 0)
        {
            kskDepartmentId = departmentId;
        }

        var kskDepartmentCode = Environment.GetEnvironmentVariable(KskDepartmentCodeEnv)?.Trim();
        if (string.IsNullOrEmpty(kskDepartmentCode))
        {
            kskDepartmentCode = null;
        }

        var patientCreateRoute = Environment.GetEnvironmentVariable(PatientCreateRouteEnv)?.Trim() ?? "";

        return new HisEmrOptions
        {
            Enabled = enabled,
            BaseUrl = baseUrl,
            Timeout = timeout,
            CredentialHeaderName = credHeader,
            DefinitionCacheDuration = cacheDuration,
            KskDepartmentId = kskDepartmentId,
            KskDepartmentCode = kskDepartmentCode,
            PatientCreateRoute = patientCreateRoute
        };
    }
}
