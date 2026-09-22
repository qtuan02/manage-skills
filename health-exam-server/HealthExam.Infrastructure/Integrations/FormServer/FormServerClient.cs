using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HealthExam.Application.Common;
using HealthExam.Application.Integrations;
using HealthExam.Application.Webhooks;
using HealthExam.Domain.Common;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace HealthExam.Infrastructure.Integrations.FormServer;

public class FormServerClient :
    HealthExam.Application.Integrations.IFormServerClient,
    HealthExam.Application.Paraclinical.IFormServerClient
{
    public const string DependencyName = "form-server";

    private const string TraceHeader = "X-Trace-Id";
    private const string DivisionHeader = "X-Division-Id";
    private const string ModuleHeader = "X-Module-Code";
    private const int MaxAttempts = 3;
    private static readonly TimeSpan BaseBackoff = TimeSpan.FromMilliseconds(200);

    private static readonly JsonSerializerSettings JsonSettings = new()
    {
        ContractResolver = new DefaultContractResolver()
    };

    private readonly HttpClient _http;
    private readonly FormServerOptions _options;
    private readonly IHealthExamContext _ctx;
    private readonly ILogger<FormServerClient> _logger;

    public FormServerClient(
        HttpClient http,
        FormServerOptions options,
        IHealthExamContext ctx,
        ILogger<FormServerClient> logger)
    {
        _http = http;
        _options = options;
        _ctx = ctx;
        _logger = logger;
    }

    public async Task<FormResolveResult> ResolveFormAsync(
        string variantCode, DateOnly effectiveOn, CancellationToken ct = default)
    {
        var config = RequireConfig();

        var url = $"{config.BaseUrl}/v1/forms/resolve"
                + $"?docTypeId={config.DocTypeId}"
                + $"&variantCode={Uri.EscapeDataString(variantCode ?? "")}"
                + $"&effectiveOn={Uri.EscapeDataString(effectiveOn.ToString(FormContextKeys.DateFormat, CultureInfo.InvariantCulture))}";

        var dto = await SendAsync<FormResolveDto>(
            () => new HttpRequestMessage(HttpMethod.Get, url),
            idempotent: true, operation: "forms/resolve", ct);

        if (dto.MatchCount > 1)
        {
            _logger.LogWarning(
                "form-server resolve trả về {MatchCount} bản cùng hiệu lực cho VariantCode={VariantCode} "
                + "tại {EffectiveOn} (DocTypeID={DocTypeId}); đang dùng FormID={FormID} ({FormCode}). "
                + "Cấu hình hiệu lực bên form-server đang chồng lấn.",
                dto.MatchCount, variantCode, effectiveOn, config.DocTypeId, dto.FormID, dto.FormCode);
        }

        return new FormResolveResult(
            dto.FormID,
            dto.FormCode,
            dto.FormName,
            dto.VersionCode,
            dto.MatchCount);
    }

    public async Task<FormDraftResult> CreateDraftAsync(
        Guid formId,
        string hostRefType,
        string hostRefId,
        Dictionary<string, Dictionary<string, string>> contextValues,
        CancellationToken ct = default)
    {
        var config = RequireConfig();

        var url = $"{config.BaseUrl}/v1/submissions/draft"
                + $"?formId={formId}"
                + $"&hostRefType={Uri.EscapeDataString(hostRefType ?? "")}"
                + $"&hostRefId={Uri.EscapeDataString(hostRefId ?? "")}";

        var body = JsonConvert.SerializeObject(
            new SubmissionDraftRequest { ContextValues = contextValues ?? new() }, JsonSettings);

        var dto = await SendAsync<SubmissionDraftDto>(
            () => new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            },
            idempotent: false, operation: "submissions/draft", ct);

        return new FormDraftResult(
            dto.FormID,
            dto.HostRefType,
            dto.HostRefID,
            dto.Layout,
            dto.PrefillValues,
            dto.MissingContext);
    }

    public Task<FormSubmissionProgressDto> GetProgressAsync(
        Guid submissionId, ServiceCallOrigin origin = null, CancellationToken ct = default)
    {
        var config = RequireConfig();
        var url = $"{config.BaseUrl}/v1/submissions/{submissionId}/progress";

        return SendAsync<FormSubmissionProgressDto>(
            () => new HttpRequestMessage(HttpMethod.Get, url),
            idempotent: true, operation: "submissions/progress", ct, origin);
    }

    async Task<HealthExam.Application.Paraclinical.SubmissionProgressResult> HealthExam.Application.Paraclinical.IFormServerClient.GetProgressAsync(
        Guid submissionId, CancellationToken ct)
    {
        var dto = await GetProgressAsync(submissionId, null, ct);
        return new HealthExam.Application.Paraclinical.SubmissionProgressResult(
            dto?.Sections?.Completed ?? 0,
            dto?.Sections?.Total ?? 0,
            dto?.CanSignConclusion ?? false);
    }

    public async Task<HealthExam.Application.Paraclinical.SignSubmissionResult> SignAsync(
        Guid submissionId, HealthExam.Application.Paraclinical.SignSubmissionRequest request, CancellationToken ct = default)
    {
        var config = RequireConfig();
        var url = $"{config.BaseUrl}/v1/submissions/{submissionId}/sections/{request.SectionId}/sign";

        var body = JsonConvert.SerializeObject(
            new SectionSignRequest
            {
                ActorID = request.ActorId,
                ActorKind = request.ActorKind,
                ActorName = request.ActorName ?? "",
                SignatoryFlows = request.SignatoryFlows
            },
            JsonSettings);

        var dto = await SendAsync<SectionSignDto>(
            () => new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            },
            idempotent: false, operation: "submissions/sections/sign", ct);

        return new HealthExam.Application.Paraclinical.SignSubmissionResult(
            true,
            dto?.SignStatus?.ToString() ?? "không rõ");
    }

    private FormServerOptions RequireConfig()
    {
        if (string.IsNullOrWhiteSpace(_options.BaseUrl))
            throw Unavailable(
                $"Chưa cấu hình biến môi trường {FormServerOptions.BaseUrlEnv} cho health-exam-server",
                $"{FormServerOptions.BaseUrlEnv} rỗng");

        if (_options.DocTypeId <= 0)
            throw Unavailable(
                $"Biến môi trường {FormServerOptions.DocTypeEnv} không phải số nguyên hợp lệ: \"{_options.DocTypeIdRaw}\"",
                $"{FormServerOptions.DocTypeEnv} sai định dạng");

        return _options;
    }

    private async Task<T> SendAsync<T>(
        Func<HttpRequestMessage> requestFactory, bool idempotent, string operation, CancellationToken ct,
        ServiceCallOrigin origin = null)
    {
        var maxAttempts = idempotent ? MaxAttempts : 1;

        for (var attempt = 1; ; attempt++)
        {
            var isLast = attempt >= maxAttempts;

            try
            {
                using var request = requestFactory();
                ApplyHeaders(request, origin);

                using var response = await _http.SendAsync(request, ct);
                var raw = await response.Content.ReadAsStringAsync(ct);

                if ((int)response.StatusCode >= 500)
                {
                    if (!isLast) { await BackoffAsync(attempt, operation, response.StatusCode.ToString(), ct); continue; }
                    throw Unavailable(
                        $"form-server trả lỗi {(int)response.StatusCode} khi gọi {operation}",
                        $"HTTP {(int)response.StatusCode}");
                }

                return ReadEnvelope<T>(raw, response.StatusCode, operation);
            }
            catch (HealthExamException)
            {
                throw;
            }
            catch (HttpRequestException ex)
            {
                if (!isLast) { await BackoffAsync(attempt, operation, ex.GetType().Name, ct); continue; }
                throw Unavailable($"Không gọi được form-server ({operation})", ex.Message, ex);
            }
            catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
            {
                if (!isLast) { await BackoffAsync(attempt, operation, "Timeout", ct); continue; }
                throw Unavailable(
                    $"form-server không phản hồi trong {_http.Timeout.TotalSeconds:0.#}s ({operation})",
                    "Timeout", ex);
            }
        }
    }

    private async Task BackoffAsync(int attempt, string operation, string reason, CancellationToken ct)
    {
        var delay = BaseBackoff * Math.Pow(2, attempt - 1);
        _logger.LogWarning(
            "Gọi form-server {Operation} lỗi ({Reason}), thử lại lần {Attempt} sau {Delay}ms",
            operation, reason, attempt + 1, delay.TotalMilliseconds);
        await Task.Delay(delay, ct);
    }

    private void ApplyHeaders(HttpRequestMessage request, ServiceCallOrigin origin = null)
    {
        var traceId = origin?.TraceId ?? _ctx.TraceId;
        var divisionId = origin?.DivisionId ?? _ctx.DivisionId;

        if (!string.IsNullOrWhiteSpace(traceId))
            request.Headers.TryAddWithoutValidation(TraceHeader, traceId);

        if (!string.IsNullOrWhiteSpace(divisionId))
            request.Headers.TryAddWithoutValidation(DivisionHeader, divisionId);

        request.Headers.TryAddWithoutValidation(ModuleHeader, ModuleCodes.HealthExam);

        if (!string.IsNullOrWhiteSpace(_options.ServiceToken))
            request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {_options.ServiceToken}");
    }

    private T ReadEnvelope<T>(string raw, HttpStatusCode status, string operation)
    {
        FormServerEnvelope<T> envelope;
        try
        {
            envelope = JsonConvert.DeserializeObject<FormServerEnvelope<T>>(raw, JsonSettings);
        }
        catch (JsonException ex)
        {
            throw Unavailable(
                $"form-server trả về nội dung không đọc được ({operation})", "Body không phải JSON", ex);
        }

        if (envelope == null)
            throw Unavailable($"form-server trả về phản hồi rỗng ({operation})", "Body rỗng");

        if (envelope.ErrorCode != 0)
            throw new HealthExamException(
                envelope.ErrorCode,
                $"form-server: {(string.IsNullOrWhiteSpace(envelope.Message) ? "Lỗi phụ thuộc" : envelope.Message)}",
                new { Dependency = DependencyName, Operation = operation, TraceID = envelope.TraceID });

        if (envelope.Data == null)
            throw Unavailable(
                $"form-server báo thành công nhưng không kèm dữ liệu ({operation})",
                $"Data rỗng, HTTP {(int)status}");

        return envelope.Data;
    }

    private static HealthExamException Unavailable(string message, string reason, Exception inner = null)
    {
        if (inner != null)
            message = $"{message}: {inner.Message}";

        return new HealthExamException(
            5020,
            message,
            new { Dependency = DependencyName, Reason = reason });
    }
}
