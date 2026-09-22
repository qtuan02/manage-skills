#nullable enable

using System;
using System.Collections.Generic;
using HealthExam.API.Contracts;
using HealthExam.API.Middlewares;
using HealthExam.Application.Common;
using HealthExam.Domain.Common;
using HealthExam.Domain.ExamSessions;
using HealthExam.Domain.ExamRecords;
using HealthExam.Domain.Imports;
using HealthExam.Domain.Paraclinical;
using HealthExam.Domain.Webhooks;
using HealthExam.Domain.Integrations;
using HealthExam.Domain.Catalogs;
using HealthExam.Infrastructure.Integrations.HisEmr;
using Newtonsoft.Json.Linq;
using Xunit;

namespace HealthExam.Tests;

/// <summary>Gom mọi bộ test mutate biến môi trường process-wide qua <see cref="TemporaryEnvironment"/>
/// vào một collection: xunit chạy song song theo mặc định, nên hai test cùng đổi biến môi trường ở hai
/// thread khác nhau có thể giẫm lên nhau (test A đọc giá trị test B vừa set tạm rồi trả lại).</summary>
[CollectionDefinition("env", DisableParallelization = true)]
public class EnvCollection { }

[Collection("env")]
public class HisEmrConfigurationTests
{
    [Fact]
    public void Defaults_are_safe_for_phase_one()
    {
        using var env = new TemporaryEnvironment(
            (HisEmrOptions.EnabledEnv, null),
            (HisEmrOptions.BaseUrlEnv, null),
            (HisEmrOptions.TimeoutEnv, null),
            (HisEmrOptions.CredentialHeaderEnv, null),
            (HisEmrOptions.CacheSecondsEnv, null));

        var options = HisEmrOptions.FromEnvironment();
        Assert.False(options.Enabled);
        Assert.Equal("", options.BaseUrl);
        Assert.Equal("Authorization", options.CredentialHeaderName);
        Assert.Equal(TimeSpan.FromSeconds(300), options.DefinitionCacheDuration);
        Assert.Equal(TimeSpan.FromSeconds(10), options.Timeout);
    }

    [Fact]
    public void Environment_variables_override_defaults()
    {
        using var env = new TemporaryEnvironment(
            (HisEmrOptions.EnabledEnv, "true"),
            (HisEmrOptions.BaseUrlEnv, "https://his.hospital.local/"),
            (HisEmrOptions.TimeoutEnv, "25"),
            (HisEmrOptions.CredentialHeaderEnv, "X-His-Token"),
            (HisEmrOptions.CacheSecondsEnv, "600"));

        var options = HisEmrOptions.FromEnvironment();
        Assert.True(options.Enabled);
        Assert.Equal("https://his.hospital.local/", options.BaseUrl);
        Assert.Equal(TimeSpan.FromSeconds(25), options.Timeout);
        Assert.Equal("X-His-Token", options.CredentialHeaderName);
        Assert.Equal(TimeSpan.FromSeconds(600), options.DefinitionCacheDuration);
    }

    [Theory]
    [InlineData(ErrorCodes.HisBadGateway, 502)]
    [InlineData(ErrorCodes.HisTimeout, 504)]
    public void His_transport_codes_map_to_expected_http_status(int code, int status)
        => Assert.Equal(status, ErrorCodes.ToHttpStatus(code));

    [Fact]
    public void His_transport_codes_have_default_messages()
    {
        Assert.False(string.IsNullOrWhiteSpace(ErrorCodes.DefaultMessage(ErrorCodes.HisBadGateway)));
        Assert.False(string.IsNullOrWhiteSpace(ErrorCodes.DefaultMessage(ErrorCodes.HisTimeout)));
    }

    [Fact]
    public void Fake_context_reads_headers_case_insensitively()
    {
        var context = new FakeHealthExamContext();
        context.Headers["Authorization"] = "Bearer token-123";

        Assert.Equal("Bearer token-123", context.GetRequestHeader("authorization"));
        Assert.Equal("Bearer token-123", context.GetRequestHeader("AUTHORIZATION"));
        Assert.Equal("", context.GetRequestHeader("X-Missing"));
    }

    [Fact]
    public void Public_and_wire_contracts_hold_expected_fields()
    {
        var templateId = Guid.NewGuid();
        var def = new HisFormDefinition
        {
            TemplateId = templateId,
            TemplateCode = "KSK-TREN18TUOI",
            TemplateName = "Khám sức khỏe trên 18 tuổi",
            FileDocTypeId = 1,
            VersionCode = "V1",
            IsDraft = false,
            Active = true,
            Details = new JArray(),
            Layout = new JArray()
        };

        var recordId = Guid.NewGuid();
        var form = new ExamRecordHisForm(recordId, 12345, def);
        Assert.Equal(recordId, form.RecordId);
        Assert.Equal(12345, form.AdmissionId);
        Assert.Equal(templateId, form.Definition.TemplateId);
    }
}

public sealed class TemporaryEnvironment : IDisposable
{
    private readonly Dictionary<string, string?> _originalValues = new();

    public TemporaryEnvironment(params (string key, string? value)[] pairs)
    {
        foreach (var (key, value) in pairs)
        {
            if (!_originalValues.ContainsKey(key))
            {
                _originalValues[key] = Environment.GetEnvironmentVariable(key);
            }
            Environment.SetEnvironmentVariable(key, value);
        }
    }

    public void Dispose()
    {
        foreach (var (key, value) in _originalValues)
        {
            Environment.SetEnvironmentVariable(key, value);
        }
    }
}
