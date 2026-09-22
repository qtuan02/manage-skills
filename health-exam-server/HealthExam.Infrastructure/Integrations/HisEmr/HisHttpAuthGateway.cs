#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HealthExam.Application.Auth;
using HealthExam.Application.Common;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace HealthExam.Infrastructure.Integrations.HisEmr;

public sealed class HisHttpAuthGateway : IHisAuthGateway
{
    public const string RouteLogin = "api/Auth/Login";
    public const string RouteInfo = "api/Auth/Info";
    public const string RouteLogout = "api/Auth/Logout";

    private readonly HttpClient _http;
    private readonly HisEmrOptions _options;
    private readonly ILogger<HisHttpAuthGateway> _logger;

    public HisHttpAuthGateway(
        HttpClient http,
        HisEmrOptions options,
        ILogger<HisHttpAuthGateway> logger)
    {
        _http = http;
        _options = options;
        _logger = logger;
    }

    public async Task<ApplicationResult<LoginResult>> LoginAsync(LoginCommand command, CancellationToken ct = default)
    {
        var (outcome, data, failure) = await SendAsync(
            HttpMethod.Post,
            RouteLogin,
            command.DivisionId,
            command.TraceId,
            authHeader: null,
            body: new
            {
                Username = command.Username,
                Password = command.Password,
                ConfirmLogin = command.ConfirmLogin
            },
            allowNullData: false,
            ct);

        if (!outcome || failure != null)
            return ApplicationResult<LoginResult>.Fail(failure!.Code, failure.Message, failure.Payload);

        var token = data?.Type == JTokenType.String ? data.Value<string>() : null;
        if (string.IsNullOrWhiteSpace(token))
            return ApplicationResult<LoginResult>.Fail(ApplicationFailureCode.HisBadGateway, "HIS không trả về access token hợp lệ");

        return ApplicationResult<LoginResult>.Success(new LoginResult(token));
    }

    public async Task<ApplicationResult<CurrentUserResult>> GetCurrentUserAsync(GetCurrentUserQuery query, CancellationToken ct = default)
    {
        var (outcome, data, failure) = await SendAsync(
            HttpMethod.Get,
            RouteInfo,
            query.DivisionId,
            query.TraceId,
            authHeader: query.AuthorizationHeader,
            body: null,
            allowNullData: false,
            ct);

        if (!outcome || failure != null)
            return ApplicationResult<CurrentUserResult>.Fail(failure!.Code, failure.Message, failure.Payload);

        if (data == null || data.Type == JTokenType.Null)
            return ApplicationResult<CurrentUserResult>.Fail(ApplicationFailureCode.HisBadGateway, "HIS không trả về dữ liệu người dùng hợp lệ");

        var result = new CurrentUserResult(
            AccountID: data.Value<long?>("AccountID"),
            EmployeeID: data.Value<long?>("EmployeeID") ?? query.ActorId,
            EmployeeCode: NullIfBlank(data.Value<string>("EmployeeCode")),
            DivisionID: NullIfBlank(data.Value<string>("DivisionID")),
            UserName: NullIfBlank(data.Value<string>("UserName")) ?? query.ActorName,
            PhoneNumber: NullIfBlank(data.Value<string>("PhoneNumber")),
            DepartmentID: data.Value<int?>("DepartmentID"),
            DepartmentName: NullIfBlank(data.Value<string>("DepartmentName")),
            IsChangePassword: data.Value<bool?>("IsChangePassword") ?? false,
            Permissions: ReadPermissions(data["Permissions"], _logger));

        return ApplicationResult<CurrentUserResult>.Success(result);
    }

    public async Task<ApplicationResult<object>> LogoutAsync(LogoutCommand command, CancellationToken ct = default)
    {
        var (outcome, _, failure) = await SendAsync(
            HttpMethod.Post,
            RouteLogout,
            command.DivisionId,
            command.TraceId,
            authHeader: command.AuthorizationHeader,
            body: null,
            allowNullData: true,
            ct);

        if (!outcome || failure != null)
            return ApplicationResult<object>.Fail(failure!.Code, failure.Message, failure.Payload);

        return ApplicationResult<object>.Success(null!);
    }

    private static string? NullIfBlank(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>HIS gửi <c>Permissions</c> là object {code: int} hoặc null. Không phải object → null.</summary>
    private static IReadOnlyDictionary<string, int>? ReadPermissions(JToken? token, ILogger logger)
    {
        if (token == null || token.Type != JTokenType.Object) return null;
        try
        {
            return token.ToObject<Dictionary<string, int>>();
        }
        catch (JsonException ex)
        {
            logger.LogError("HIS auth Permissions không đúng dạng {{code: int}}: {Message}", ex.Message);
            return null;
        }
    }

    private async Task<(bool Success, JToken? Data, ApplicationFailure? Failure)> SendAsync(
        HttpMethod method,
        string route,
        string divisionId,
        string traceId,
        string? authHeader,
        object? body,
        bool allowNullData,
        CancellationToken ct)
    {
        if (!_options.Enabled)
            return (false, null, new ApplicationFailure(ApplicationFailureCode.HisBadGateway, "Cầu nối HIS EMR chưa được bật"));

        var baseUrl = _options.BaseUrl?.TrimEnd('/') ?? "";
        if (string.IsNullOrWhiteSpace(baseUrl))
            return (false, null, new ApplicationFailure(ApplicationFailureCode.HisBadGateway, "Chưa cấu hình HIS_EMR_BASE_URL"));

        var uri = new Uri($"{baseUrl}/{route.TrimStart('/')}");
        using var request = new HttpRequestMessage(method, uri);

        if (!string.IsNullOrWhiteSpace(divisionId))
            request.Headers.TryAddWithoutValidation("X-Division-Id", divisionId);
        if (!string.IsNullOrWhiteSpace(traceId))
            request.Headers.TryAddWithoutValidation("X-Trace-Id", traceId);

        if (!string.IsNullOrWhiteSpace(authHeader))
        {
            var headerName = string.IsNullOrWhiteSpace(_options.CredentialHeaderName) ? "Authorization" : _options.CredentialHeaderName;
            request.Headers.TryAddWithoutValidation(headerName, authHeader);
        }

        if (body != null)
        {
            var json = JsonConvert.SerializeObject(body);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        var sw = Stopwatch.StartNew();
        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, ct);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            _logger.LogError("HIS auth timeout ({Route}) after {ElapsedMs}ms", route, sw.ElapsedMilliseconds);
            return (false, null, new ApplicationFailure(ApplicationFailureCode.HisTimeout, "Gọi HIS quá thời gian chờ"));
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError("HIS auth network failure ({Route}) after {ElapsedMs}ms: {Message}", route, sw.ElapsedMilliseconds, ex.Message);
            return (false, null, new ApplicationFailure(ApplicationFailureCode.HisBadGateway, "Lỗi kết nối tới HIS"));
        }

        sw.Stop();
        _logger.LogInformation("HIS auth {Route} returned {StatusCode} in {ElapsedMs}ms (Division: {Division}, TraceId: {TraceId})",
            route, response.StatusCode, sw.ElapsedMilliseconds, divisionId, traceId);

        if (response.StatusCode == HttpStatusCode.Unauthorized)
            return (false, null, new ApplicationFailure(ApplicationFailureCode.Unauthorized, "Thông tin đăng nhập không chính xác hoặc phiên đã hết hạn"));

        if (response.StatusCode == HttpStatusCode.Forbidden)
            return (false, null, new ApplicationFailure(ApplicationFailureCode.Forbidden, "Không có quyền thực hiện thao tác"));

        string content;
        try
        {
            content = await response.Content.ReadAsStringAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError("HIS auth read response body failure: {Message}", ex.Message);
            return (false, null, new ApplicationFailure(ApplicationFailureCode.HisBadGateway, "Không đọc được phản hồi từ HIS"));
        }

        HisAuthEnvelope? envelope;
        try
        {
            envelope = JsonConvert.DeserializeObject<HisAuthEnvelope>(content);
        }
        catch (Exception ex)
        {
            _logger.LogError("HIS auth deserialize envelope failure: {Message}", ex.Message);
            return (false, null, new ApplicationFailure(ApplicationFailureCode.HisBadGateway, "Phản hồi từ HIS không đúng định dạng JSON"));
        }

        if (envelope == null)
            return (false, null, new ApplicationFailure(ApplicationFailureCode.HisBadGateway, "Phản hồi từ HIS rỗng"));

        if (string.Equals(envelope.MessageCode, "SESSION_EXIST", StringComparison.OrdinalIgnoreCase))
        {
            return (false, null, new ApplicationFailure(
                ApplicationFailureCode.InvalidState,
                string.IsNullOrWhiteSpace(envelope.Message) ? "Tài khoản đang đăng nhập ở thiết bị khác" : envelope.Message,
                envelope.Data));
        }

        if (envelope.ErrorCode == 401 || envelope.ErrorCode == 4010)
        {
            return (false, null, new ApplicationFailure(
                ApplicationFailureCode.Unauthorized,
                string.IsNullOrWhiteSpace(envelope.Message) ? "Thông tin đăng nhập không chính xác" : envelope.Message));
        }

        if (envelope.ErrorCode == 403 || envelope.ErrorCode == 4030)
        {
            return (false, null, new ApplicationFailure(
                ApplicationFailureCode.Forbidden,
                string.IsNullOrWhiteSpace(envelope.Message) ? "Không có quyền truy cập" : envelope.Message));
        }

        if (envelope.ErrorCode != 0)
        {
            return (false, null, new ApplicationFailure(
                ApplicationFailureCode.BadRequest,
                string.IsNullOrWhiteSpace(envelope.Message) ? "HIS từ chối yêu cầu" : envelope.Message,
                envelope.Data));
        }

        if (!allowNullData && (envelope.Data == null || envelope.Data.Type == JTokenType.Null))
        {
            return (false, null, new ApplicationFailure(ApplicationFailureCode.HisBadGateway, "HIS trả về dữ liệu rỗng"));
        }

        return (true, envelope.Data, null);
    }

    private sealed class HisAuthEnvelope
    {
        public int ErrorCode { get; set; }
        public string Message { get; set; } = "";
        public string MessageCode { get; set; } = "";
        public JToken? Data { get; set; }
    }
}
