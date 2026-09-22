#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;
using HealthExam.API.Contracts;
using HealthExam.API.Controllers;
using HealthExam.Application.Common;
using HealthExam.Application.His;
using HealthExam.Application.Integrations;
using HealthExam.Infrastructure.Caching;
using HealthExam.Infrastructure.Integrations.HisEmr;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace HealthExam.Tests;

public class HisFormDefinitionTests
{
    private static readonly HisEmrOptions DefaultOptions = new()
    {
        Enabled = true,
        BaseUrl = "https://his.test",
        Timeout = TimeSpan.FromSeconds(5),
        DefinitionCacheDuration = TimeSpan.FromSeconds(300),
        CredentialHeaderName = "Authorization"
    };

    [Fact]
    public async Task Resolves_active_KSK_TREN18TUOI_ordinal_ignore_case()
    {
        var templateId = Guid.NewGuid();
        var fakeClient = new FakeHisClient();
        fakeClient.Templates = JArray.Parse($@"
        [
            {{ ""Id"": ""{templateId}"", ""TemplateCode"": ""ksk-tren18tuoi"", ""Active"": true }},
            {{ ""Id"": ""{Guid.NewGuid()}"", ""TemplateCode"": ""OTHER-CODE"", ""Active"": true }}
        ]");
        fakeClient.Metadata = JObject.Parse($@"
        {{
            ""Id"": ""{templateId}"",
            ""TemplateCode"": ""KSK-TREN18TUOI"",
            ""TemplateName"": ""Phiếu khám KSK trên 18 tuổi"",
            ""FileDocTypeID"": 102,
            ""VersionCode"": ""V2.0"",
            ""IsDraft"": false,
            ""Active"": true,
            ""Details"": [ {{ ""DetailId"": 1, ""CustomField"": ""Value1"" }} ]
        }}");
        fakeClient.Layout = JArray.Parse(@"
        [
            { ""OrderNo"": 1, ""OrderString"": ""1"", ""LevelNo"": 0, ""ParentItemID"": 0, ""ItemRootID"": 10, ""ItemGroupID"": 1001, ""ExtraCol"": ""XYZ"" }
        ]");

        using var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var cache = new HisFormDefinitionCache(memoryCache, DefaultOptions, NullLogger<HisFormDefinitionCache>.Instance);
        var handler = new GetHisFormDefinitionHandler(fakeClient, cache);

        var appResult = await handler.HandleAsync(
            new GetHisFormDefinitionQuery("KSK-TREN18TUOI", "DEV", "Bearer test", "TRACE-01"), CancellationToken.None);

        Assert.True(appResult.IsSuccess);
        var result = HisFormController.MapToDto(appResult.Value);

        Assert.Equal(templateId, result.TemplateId);
        Assert.Equal("KSK-TREN18TUOI", result.TemplateCode);
        Assert.Equal("Phiếu khám KSK trên 18 tuổi", result.TemplateName);
        Assert.Equal(102, result.FileDocTypeId);
        Assert.Equal("V2.0", result.VersionCode);
        Assert.False(result.IsDraft);
        Assert.True(result.Active);
        Assert.Single(result.Details);
        Assert.Equal("Value1", result.Details[0]["CustomField"]!.ToString());
        Assert.Single(result.Layout);
        Assert.Equal("XYZ", result.Layout[0]["ExtraCol"]!.ToString());
        Assert.Equal(1001, (int)result.Layout[0]["ItemGroupID"]!);
    }

    [Fact]
    public async Task Non_KSK_TREN18TUOI_code_returns_4040()
    {
        var fakeClient = new FakeHisClient();
        using var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var cache = new HisFormDefinitionCache(memoryCache, DefaultOptions, NullLogger<HisFormDefinitionCache>.Instance);
        var handler = new GetHisFormDefinitionHandler(fakeClient, cache);

        var result = await handler.HandleAsync(
            new GetHisFormDefinitionQuery("OTHER-TEMPLATE", "DEV", "Bearer test", "TRACE-01"), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ApplicationFailureCode.NotFound, result.Failure.Code);
    }

    [Fact]
    public async Task No_active_match_returns_4040()
    {
        var fakeClient = new FakeHisClient();
        fakeClient.Templates = JArray.Parse(@"
        [
            { ""Id"": ""00000000-0000-0000-0000-000000000001"", ""TemplateCode"": ""KSK-TREN18TUOI"", ""Active"": false }
        ]");

        using var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var cache = new HisFormDefinitionCache(memoryCache, DefaultOptions, NullLogger<HisFormDefinitionCache>.Instance);
        var handler = new GetHisFormDefinitionHandler(fakeClient, cache);

        var result = await handler.HandleAsync(
            new GetHisFormDefinitionQuery("KSK-TREN18TUOI", "DEV", "Bearer test", "TRACE-01"), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ApplicationFailureCode.NotFound, result.Failure.Code);
    }

    [Fact]
    public async Task Duplicate_active_matches_returns_4090()
    {
        var fakeClient = new FakeHisClient();
        fakeClient.Templates = JArray.Parse(@"
        [
            { ""Id"": ""00000000-0000-0000-0000-000000000001"", ""TemplateCode"": ""KSK-TREN18TUOI"", ""Active"": true },
            { ""Id"": ""00000000-0000-0000-0000-000000000002"", ""TemplateCode"": ""ksk-tren18tuoi"", ""Active"": true }
        ]");

        using var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var cache = new HisFormDefinitionCache(memoryCache, DefaultOptions, NullLogger<HisFormDefinitionCache>.Instance);
        var handler = new GetHisFormDefinitionHandler(fakeClient, cache);

        var result = await handler.HandleAsync(
            new GetHisFormDefinitionQuery("KSK-TREN18TUOI", "DEV", "Bearer test", "TRACE-01"), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ApplicationFailureCode.InvalidState, result.Failure.Code);
    }

    [Fact]
    public async Task Cache_returns_same_instance_and_avoids_re_querying_his()
    {
        var templateId = Guid.NewGuid();
        var fakeClient = new FakeHisClient();
        fakeClient.Templates = JArray.Parse($@"[{{ ""Id"": ""{templateId}"", ""TemplateCode"": ""KSK-TREN18TUOI"", ""Active"": true }}]");
        fakeClient.Metadata = JObject.Parse($@"{{ ""Id"": ""{templateId}"", ""TemplateCode"": ""KSK-TREN18TUOI"", ""Active"": true }}");
        fakeClient.Layout = new JArray();

        using var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var cache = new HisFormDefinitionCache(memoryCache, DefaultOptions, NullLogger<HisFormDefinitionCache>.Instance);
        var handler = new GetHisFormDefinitionHandler(fakeClient, cache);

        var first = await handler.HandleAsync(
            new GetHisFormDefinitionQuery("KSK-TREN18TUOI", "DEV", "Bearer test", "TRACE-01"), CancellationToken.None);
        var second = await handler.HandleAsync(
            new GetHisFormDefinitionQuery("ksk-tren18tuoi", "DEV", "Bearer test", "TRACE-01"), CancellationToken.None);

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        Assert.Same(first.Value, second.Value);
        Assert.Equal(1, fakeClient.TemplateListCalls);
    }

    [Fact]
    public async Task Cache_key_isolates_by_division()
    {
        var templateId = Guid.NewGuid();
        var fakeClient = new FakeHisClient();
        fakeClient.Templates = JArray.Parse($@"[{{ ""Id"": ""{templateId}"", ""TemplateCode"": ""KSK-TREN18TUOI"", ""Active"": true }}]");
        fakeClient.Metadata = JObject.Parse($@"{{ ""Id"": ""{templateId}"", ""TemplateCode"": ""KSK-TREN18TUOI"", ""Active"": true }}");
        fakeClient.Layout = new JArray();

        using var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var cache = new HisFormDefinitionCache(memoryCache, DefaultOptions, NullLogger<HisFormDefinitionCache>.Instance);
        var handler = new GetHisFormDefinitionHandler(fakeClient, cache);

        var first = await handler.HandleAsync(
            new GetHisFormDefinitionQuery("KSK-TREN18TUOI", "DEV", "Bearer test", "TRACE-01"), CancellationToken.None);
        Assert.True(first.IsSuccess);
        Assert.Equal(1, fakeClient.TemplateListCalls);

        // Switch division
        var second = await handler.HandleAsync(
            new GetHisFormDefinitionQuery("KSK-TREN18TUOI", "PROD", "Bearer test", "TRACE-01"), CancellationToken.None);
        Assert.True(second.IsSuccess);

        Assert.Equal(2, fakeClient.TemplateListCalls);
        Assert.NotSame(first.Value, second.Value);
    }

    [Fact]
    public async Task Failures_are_not_cached()
    {
        var fakeClient = new FakeHisClient();
        fakeClient.ShouldFailList = true;

        using var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var cache = new HisFormDefinitionCache(memoryCache, DefaultOptions, NullLogger<HisFormDefinitionCache>.Instance);
        var handler = new GetHisFormDefinitionHandler(fakeClient, cache);

        var first = await handler.HandleAsync(
            new GetHisFormDefinitionQuery("KSK-TREN18TUOI", "DEV", "Bearer test", "TRACE-01"), CancellationToken.None);
        Assert.False(first.IsSuccess);
        Assert.Equal(1, fakeClient.TemplateListCalls);

        // Unfail
        fakeClient.ShouldFailList = false;
        var templateId = Guid.NewGuid();
        fakeClient.Templates = JArray.Parse($@"[{{ ""Id"": ""{templateId}"", ""TemplateCode"": ""KSK-TREN18TUOI"", ""Active"": true }}]");
        fakeClient.Metadata = JObject.Parse($@"{{ ""Id"": ""{templateId}"", ""TemplateCode"": ""KSK-TREN18TUOI"", ""Active"": true }}");
        fakeClient.Layout = new JArray();

        var second = await handler.HandleAsync(
            new GetHisFormDefinitionQuery("KSK-TREN18TUOI", "DEV", "Bearer test", "TRACE-01"), CancellationToken.None);
        Assert.True(second.IsSuccess);
        Assert.Equal(2, fakeClient.TemplateListCalls);
        Assert.NotNull(second.Value);
    }

    private sealed class FakeHisClient : IHisEmrClient
    {
        public int TemplateListCalls { get; private set; }
        public int TemplateCalls { get; private set; }
        public int TemplateTreeCalls { get; private set; }

        public bool ShouldFailList { get; set; }
        public JArray Templates { get; set; } = new();
        public JObject Metadata { get; set; } = new();
        public JArray Layout { get; set; } = new();

        public Task<HisClientResult<HisJsonDocument>> SendAsync(
            HisOperation operation, HisRequest request, CancellationToken ct = default)
        {
            switch (operation)
            {
                case HisOperation.ListDefinitions:
                    TemplateListCalls++;
                    if (ShouldFailList)
                        return Task.FromResult(HisClientResult<HisJsonDocument>.Fail(HisClientOutcome.BadGateway, "Downstream failure"));
                    return Task.FromResult(HisClientResult<HisJsonDocument>.Success(new HisJsonDocument(Templates.ToString(Formatting.None))));

                case HisOperation.GetDefinition:
                    TemplateCalls++;
                    return Task.FromResult(HisClientResult<HisJsonDocument>.Success(new HisJsonDocument(Metadata.ToString(Formatting.None))));

                case HisOperation.GetDefinitionLayout:
                    TemplateTreeCalls++;
                    return Task.FromResult(HisClientResult<HisJsonDocument>.Success(new HisJsonDocument(Layout.ToString(Formatting.None))));

                default:
                    return Task.FromResult(HisClientResult<HisJsonDocument>.Fail(HisClientOutcome.BadGateway, "Not supported"));
            }
        }
    }
}
