using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using HealthExam.Application.His;
using HealthExam.Application.Integrations;
using HealthExam.Infrastructure.Integrations.HisEmr;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace HealthExam.Infrastructure.Caching;

public class HisFormDefinitionCache : IHisFormDefinitionCache
{
    private readonly IMemoryCache _cache;
    private readonly HisEmrOptions _options;
    private readonly ILogger<HisFormDefinitionCache> _logger;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new(StringComparer.Ordinal);

    public HisFormDefinitionCache(
        IMemoryCache cache,
        HisEmrOptions options,
        ILogger<HisFormDefinitionCache> logger)
    {
        _cache = cache;
        _options = options;
        _logger = logger;
    }

    public async Task<HisFormDefinitionResult> GetOrCreateAsync(
        string divisionId,
        string templateCode,
        Func<CancellationToken, Task<HisFormDefinitionResult>> factory,
        CancellationToken ct = default)
    {
        var normalizedBaseUrl = _options.BaseUrl?.TrimEnd('/').ToLowerInvariant() ?? "";
        var div = divisionId?.Trim() ?? "";
        var code = templateCode?.Trim().ToUpperInvariant() ?? "";
        var cacheKey = $"{normalizedBaseUrl}|{div}|{code}";

        if (_cache.TryGetValue(cacheKey, out HisFormDefinitionResult cached) && cached != null)
        {
            return cached;
        }

        var keyLock = _locks.GetOrAdd(cacheKey, _ => new SemaphoreSlim(1, 1));
        await keyLock.WaitAsync(ct);
        try
        {
            if (_cache.TryGetValue(cacheKey, out HisFormDefinitionResult doubleChecked) && doubleChecked != null)
            {
                return doubleChecked;
            }

            var definition = await factory(ct);
            _cache.Set(cacheKey, definition, _options.DefinitionCacheDuration);
            _logger.LogInformation("Đã nạp và lưu cache định nghĩa biểu mẫu {TemplateCode} (division: {DivisionId})",
                code, div);

            return definition;
        }
        finally
        {
            keyLock.Release();
        }
    }

    public async Task<System.Collections.Generic.IReadOnlyList<Icd10Choice>> GetOrCreateIcd10Async(
        string divisionId,
        string filter,
        int amount,
        Func<CancellationToken, Task<System.Collections.Generic.IReadOnlyList<Icd10Choice>>> factory,
        CancellationToken ct = default)
    {
        var normalizedBaseUrl = _options.BaseUrl?.TrimEnd('/').ToLowerInvariant() ?? "";
        var div = divisionId?.Trim().ToLowerInvariant() ?? "";
        var f = filter?.Trim().ToLowerInvariant() ?? "";
        var cacheKey = $"{normalizedBaseUrl}|icd10|{div}|{f}|{amount}";

        if (_cache.TryGetValue(cacheKey, out System.Collections.Generic.IReadOnlyList<Icd10Choice> cached) && cached != null)
        {
            return cached;
        }

        var keyLock = _locks.GetOrAdd(cacheKey, _ => new SemaphoreSlim(1, 1));
        await keyLock.WaitAsync(ct);
        try
        {
            if (_cache.TryGetValue(cacheKey, out System.Collections.Generic.IReadOnlyList<Icd10Choice> doubleChecked) && doubleChecked != null)
            {
                return doubleChecked;
            }

            var items = await factory(ct);
            _cache.Set(cacheKey, items, TimeSpan.FromMinutes(10));
            return items;
        }
        finally
        {
            keyLock.Release();
        }
    }
}
