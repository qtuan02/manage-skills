using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HealthExam.Application.Integrations;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace HealthExam.Infrastructure.Integrations.Ris;

/// <summary>Kết quả thô một lượt gọi RIS — đủ để hàng đợi ghi lại được sự việc.</summary>
public record RisClientRawResult(bool Success, int StatusCode, string ResponseBody, string Error)
{
    public bool Transport => StatusCode == 0;
}

public class RisClient : IRisClient
{
    public const string DependencyName = "ris";
    private const int MaxSnippet = 900;

    private static readonly JsonSerializerSettings WireSettings = new()
    {
        ContractResolver = new CamelCasePropertyNamesContractResolver(),
        DateFormatString = VendorClock.WireFormat,
        Converters = { new VendorStampJsonConverter() },
        NullValueHandling = NullValueHandling.Ignore
    };

    private readonly HttpClient _http;
    private readonly RisOptions _options;
    private readonly ILogger<RisClient> _logger;

    public RisClient(HttpClient http, RisOptions options, ILogger<RisClient> logger)
    {
        _http = http;
        _options = options;
        _logger = logger;
    }

    public static string Serialize(RisOrderRequest request)
        => JsonConvert.SerializeObject(request, WireSettings);

    public async Task<RisSendResult> SendOrderAsync(
        string payload, long outboxId, CancellationToken ct = default)
    {
        var raw = await SendAsync(payload, outboxId, ct);
        if (raw.Success)
        {
            return RisSendResult.Sent(raw.ResponseBody);
        }

        if (raw.StatusCode >= 400 && raw.StatusCode < 500)
        {
            return RisSendResult.Rejected(raw.Error, raw.ResponseBody);
        }

        return RisSendResult.DependencyFailure(raw.Error, raw.ResponseBody);
    }

    public async Task<RisClientRawResult> SendAsync(string payloadJson, long outboxId = 0, CancellationToken ct = default)
    {
        if (!_options.IsDispatchConfigured)
            throw new InvalidOperationException(
                $"Chưa cấu hình cầu RIS: cần {RisOptions.DispatchEnvNames}");

        var url = _options.BaseUrl + RisOptions.OrderPath;

        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(payloadJson, Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", BasicCredential());
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        try
        {
            using var response = await _http.SendAsync(request, ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            var snippet = Snippet(body);

            _logger.LogInformation(
                "RIS ← gửi phiếu: POST {Url} outbox={OutboxID} reqBytes={RequestLength} status={Status} res={Response}",
                url, outboxId, payloadJson?.Length ?? 0, (int)response.StatusCode, snippet);

            return response.IsSuccessStatusCode
                ? new RisClientRawResult(true, (int)response.StatusCode, snippet, "")
                : new RisClientRawResult(false, (int)response.StatusCode, snippet,
                    $"RIS trả {(int)response.StatusCode}: {snippet}");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex,
                "RIS ← không gọi được: POST {Url} outbox={OutboxID} reqBytes={RequestLength}",
                url, outboxId, payloadJson?.Length ?? 0);
            return new RisClientRawResult(false, 0, "", $"Không gọi được RIS: {ex.Message}");
        }
    }

    private string BasicCredential()
        => Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_options.Username}:{_options.Password}"));

    private static string Snippet(string body)
        => string.IsNullOrEmpty(body) ? "" : (body.Length <= MaxSnippet ? body : body[..MaxSnippet]);
}
