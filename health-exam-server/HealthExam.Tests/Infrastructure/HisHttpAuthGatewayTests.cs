#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HealthExam.Application.Auth;
using HealthExam.Application.Common;
using HealthExam.Domain.Common;
using HealthExam.Infrastructure.Integrations.HisEmr;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;
using Xunit;

namespace HealthExam.Tests.Infrastructure;

public class HisHttpAuthGatewayTests
{
    [Fact]
    public async Task Login_forwards_credentials_division_and_trace_but_not_authorization()
    {
        var (gateway, handler) = CreateGateway(_ => OkEnvelope("his.jwt"));

        var result = await gateway.LoginAsync(new LoginCommand(
            DivisionId: "DHTESTING",
            TraceId: "trace-1",
            Username: "doctor",
            Password: "secret",
            ConfirmLogin: true));

        Assert.True(result.IsSuccess);
        Assert.Equal("his.jwt", result.Value.AccessToken);
        Assert.Equal("api/Auth/Login", handler.LastRequest!.RequestUri!.PathAndQuery.TrimStart('/'));
        Assert.False(handler.LastRequest.Headers.Contains("Authorization"));
        Assert.Equal("DHTESTING", handler.Header("X-Division-Id"));
        Assert.Equal("trace-1", handler.Header("X-Trace-Id"));
        var body = JObject.Parse(handler.LastBody!);
        Assert.Equal("doctor", body["Username"]!.Value<string>());
        Assert.Equal("secret", body["Password"]!.Value<string>());
        Assert.True(body["ConfirmLogin"]!.Value<bool>());
    }

    [Fact]
    public async Task Current_user_and_logout_forward_the_same_bearer_token()
    {
        var (gateway, handler) = CreateGateway(request =>
            request.RequestUri!.AbsolutePath.EndsWith("/Info", StringComparison.OrdinalIgnoreCase)
                ? OkEnvelope(new JObject { ["EmployeeID"] = 4210 })
                : OkEnvelope(JValue.CreateNull()));

        var userRes = await gateway.GetCurrentUserAsync(new GetCurrentUserQuery(
            DivisionId: "DHTESTING",
            TraceId: "trace-1",
            ActorKind: ActorKind.Employee,
            ActorId: 4210,
            ActorName: "bs.hoa",
            AuthorizationHeader: "Bearer employee.jwt"));

        var logoutRes = await gateway.LogoutAsync(new LogoutCommand(
            DivisionId: "DHTESTING",
            TraceId: "trace-1",
            ActorKind: ActorKind.Employee,
            AuthorizationHeader: "Bearer employee.jwt"));

        Assert.True(userRes.IsSuccess);
        Assert.Equal(4210, userRes.Value.EmployeeID);
        Assert.True(logoutRes.IsSuccess);
        Assert.All(handler.Requests, request =>
            Assert.Equal("Bearer employee.jwt", request.Headers.Authorization!.ToString()));
    }

    [Fact]
    public async Task Current_user_maps_every_field_HIS_Auth_Info_returns()
    {
        var (gateway, _) = CreateGateway(_ => OkEnvelope(new JObject
        {
            ["AccountID"] = 77,
            ["EmployeeID"] = 4210,
            ["EmployeeCode"] = "NV001",
            ["DivisionID"] = "DHTESTING",
            ["UserName"] = "Bác sĩ EMR",
            ["PhoneNumber"] = "0901234567",
            ["DepartmentID"] = 12,
            ["DepartmentName"] = "Khoa Khám bệnh",
            ["IsChangePassword"] = true,
            ["Permissions"] = new JObject { ["KSK.SIGN"] = 1, ["KSK.VIEW"] = 2 }
        }));

        var res = await gateway.GetCurrentUserAsync(new GetCurrentUserQuery(
            DivisionId: "DHTESTING",
            TraceId: "trace-1",
            ActorKind: ActorKind.Employee,
            ActorId: 4210,
            ActorName: "bs.hoa",
            AuthorizationHeader: "Bearer tok"));

        Assert.True(res.IsSuccess);
        Assert.Equal(77, res.Value.AccountID);
        Assert.Equal(4210, res.Value.EmployeeID);
        Assert.Equal("NV001", res.Value.EmployeeCode);
        Assert.Equal("DHTESTING", res.Value.DivisionID);
        Assert.Equal("Bác sĩ EMR", res.Value.UserName);
        Assert.Equal("0901234567", res.Value.PhoneNumber);
        Assert.Equal(12, res.Value.DepartmentID);
        Assert.Equal("Khoa Khám bệnh", res.Value.DepartmentName);
        Assert.True(res.Value.IsChangePassword);
        Assert.NotNull(res.Value.Permissions);
        Assert.Equal(1, res.Value.Permissions!["KSK.SIGN"]);
        Assert.Equal(2, res.Value.Permissions["KSK.VIEW"]);
    }

    [Fact]
    public async Task Current_user_falls_back_to_query_without_inventing_fields()
    {
        // HIS trả Permissions = null cho mọi tài khoản (AuthService.GetInfo không gán) —
        // giữ null, không dựng dictionary rỗng để FE phân biệt "không có" với "rỗng".
        var (gateway, _) = CreateGateway(_ => OkEnvelope(new JObject
        {
            ["Permissions"] = JValue.CreateNull()
        }));

        var res = await gateway.GetCurrentUserAsync(new GetCurrentUserQuery(
            DivisionId: "DHTESTING",
            TraceId: "trace-1",
            ActorKind: ActorKind.Employee,
            ActorId: 4210,
            ActorName: "bs.hoa",
            AuthorizationHeader: "Bearer tok"));

        Assert.True(res.IsSuccess);
        Assert.Null(res.Value.AccountID);
        Assert.Equal(4210, res.Value.EmployeeID);
        Assert.Equal("bs.hoa", res.Value.UserName);
        Assert.Null(res.Value.EmployeeCode);
        Assert.Null(res.Value.DivisionID);
        Assert.Null(res.Value.PhoneNumber);
        Assert.Null(res.Value.DepartmentID);
        Assert.Null(res.Value.DepartmentName);
        Assert.False(res.Value.IsChangePassword);
        Assert.Null(res.Value.Permissions);
    }

    [Fact]
    public async Task Login_preserves_SESSION_EXIST_payload()
    {
        var sessionData = new JObject { ["ActiveSessionID"] = 9988 };
        var (gateway, _) = CreateGateway(_ =>
            OkEnvelope(sessionData, errorCode: 4001, message: "Tài khoản đang đăng nhập", messageCode: "SESSION_EXIST"));

        var res = await gateway.LoginAsync(new LoginCommand("DHTESTING", "trace", "doctor", "secret"));

        Assert.False(res.IsSuccess);
        Assert.Equal(ApplicationFailureCode.InvalidState, res.Failure.Code);
        Assert.Equal("Tài khoản đang đăng nhập", res.Failure.Message);
        Assert.NotNull(res.Failure.Payload);
    }

    [Fact]
    public async Task Login_maps_401_to_Unauthorized()
    {
        var (gateway, _) = CreateGateway(_ =>
            new HttpResponseMessage(HttpStatusCode.Unauthorized));

        var res = await gateway.LoginAsync(new LoginCommand("DHTESTING", "trace", "doctor", "wrong"));

        Assert.False(res.IsSuccess);
        Assert.Equal(ApplicationFailureCode.Unauthorized, res.Failure.Code);
    }

    [Fact]
    public async Task Login_maps_timeout_to_HisTimeout()
    {
        var (gateway, _) = CreateGateway(_ =>
            throw new OperationCanceledException());

        var res = await gateway.LoginAsync(new LoginCommand("DHTESTING", "trace", "doctor", "secret"));

        Assert.False(res.IsSuccess);
        Assert.Equal(ApplicationFailureCode.HisTimeout, res.Failure.Code);
    }

    [Fact]
    public async Task Disabled_options_returns_HisBadGateway()
    {
        var (gateway, _) = CreateGateway(_ => OkEnvelope("ok"), enabled: false);

        var res = await gateway.LoginAsync(new LoginCommand("DHTESTING", "trace", "doctor", "secret"));

        Assert.False(res.IsSuccess);
        Assert.Equal(ApplicationFailureCode.HisBadGateway, res.Failure.Code);
        Assert.Contains("chưa được bật", res.Failure.Message);
    }

    private static (HisHttpAuthGateway Gateway, RecordingHttpMessageHandler Handler) CreateGateway(
        Func<HttpRequestMessage, HttpResponseMessage> responder,
        bool enabled = true)
    {
        var handler = new RecordingHttpMessageHandler(responder);
        var httpClient = new HttpClient(handler);

        var options = new HisEmrOptions
        {
            Enabled = enabled,
            BaseUrl = "https://his.test.internal",
            CredentialHeaderName = "Authorization",
            Timeout = TimeSpan.FromSeconds(10)
        };

        var logger = NullLogger<HisHttpAuthGateway>.Instance;
        var gateway = new HisHttpAuthGateway(httpClient, options, logger);
        return (gateway, handler);
    }

    private static HttpResponseMessage OkEnvelope(
        JToken? data,
        int errorCode = 0,
        string message = "OK",
        string messageCode = "")
    {
        var obj = new JObject
        {
            ["ErrorCode"] = errorCode,
            ["Message"] = message,
            ["MessageCode"] = messageCode,
            ["Data"] = data
        };
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(obj.ToString(), Encoding.UTF8, "application/json")
        };
    }

    private sealed class RecordingHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;
        public List<HttpRequestMessage> Requests { get; } = new();
        public HttpRequestMessage? LastRequest => Requests.LastOrDefault();
        public string? LastBody { get; private set; }

        public RecordingHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        {
            _responder = responder;
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request);
            if (request.Content != null)
            {
                LastBody = await request.Content.ReadAsStringAsync(cancellationToken);
            }
            return _responder(request);
        }

        public string? Header(string name)
        {
            if (LastRequest == null) return null;
            if (LastRequest.Headers.TryGetValues(name, out var values))
                return values.FirstOrDefault();
            return null;
        }
    }
}
