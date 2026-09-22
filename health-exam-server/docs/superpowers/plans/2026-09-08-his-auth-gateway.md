# HIS Authentication Gateway Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Expose login, current-user, and logout APIs from `health-exam-server` through a replaceable `IHisAuthGateway` whose phase-1 implementation forwards authentication to `his-server`.

**Architecture:** `AuthController` depends only on `IHisAuthGateway`. `HisHttpAuthGateway` is a typed HTTP client that owns HIS authentication routes, envelope parsing, credential forwarding, and dependency-error mapping; the HIS-issued JWT is returned to the frontend and is already accepted by the existing health-exam JWT middleware.

**Tech Stack:** .NET 8, ASP.NET Core controllers and middleware, `IHttpClientFactory`, Newtonsoft.Json/JToken, xUnit, `WebApplicationFactory`.

**Spec:** `docs/superpowers/specs/2026-09-08-his-auth-gateway-design.md`

## Global Constraints

- HIS remains the source of truth for credentials, employee identity, active sessions, token issuance, and logout/revocation.
- Never store or log username/password or HIS JWT values.
- `POST /v1/auth/login` is the only anonymous business endpoint and still requires `X-Division-Id`.
- `GET /v1/auth/me` and `POST /v1/auth/logout` accept only an authenticated employee JWT, not `SECRET_INTER`.
- Do not retry login, current-user, or logout automatically.
- Do not add refresh, change-password, reset-password, local account, or local JWT behavior.
- Do not modify `his-server`.
- All production code is written test-first.

---

### Task 1: Define the replaceable HIS authentication boundary

**Files:**
- Create: `HealthExam.Server/Models/HisAuthModels.cs`
- Create: `HealthExam.Server/Service/IHisAuthGateway.cs`
- Test: `HealthExam.Tests/HisAuthGatewayContractTests.cs`

**Interfaces:**
- Consumes: `CancellationToken`; no controller, MediatR, EF, or HIS assembly types.
- Produces: `IHisAuthGateway.LoginAsync`, `IHisAuthGateway.GetCurrentUserAsync`, and `IHisAuthGateway.LogoutAsync`, plus public request/result models used by the controller and HTTP implementation.

- [x] **Step 1: Write the failing boundary tests**

Create `HealthExam.Tests/HisAuthGatewayContractTests.cs` with reflection assertions that pin the interface and ensure it does not leak HIS implementation types:

```csharp
using HealthExam.Server.Models;
using HealthExam.Server.Service;
using Newtonsoft.Json.Linq;
using Xunit;

namespace HealthExam.Tests;

public class HisAuthGatewayContractTests
{
    [Fact]
    public void Gateway_exposes_three_replaceable_auth_operations()
    {
        var type = typeof(IHisAuthGateway);

        Assert.True(type.IsInterface);
        Assert.Equal(typeof(Task<HisLoginResult>), type.GetMethod("LoginAsync")!.ReturnType);
        Assert.Equal(typeof(Task<JToken>), type.GetMethod("GetCurrentUserAsync")!.ReturnType);
        Assert.Equal(typeof(Task), type.GetMethod("LogoutAsync")!.ReturnType);
    }

    [Fact]
    public void Login_models_pin_the_public_contract()
    {
        var request = new HisLoginRequest
        {
            Username = "doctor",
            Password = "secret",
            ConfirmLogin = true
        };
        var result = new HisLoginResult { AccessToken = "jwt" };

        Assert.Equal("doctor", request.Username);
        Assert.Equal("secret", request.Password);
        Assert.True(request.ConfirmLogin);
        Assert.Equal("jwt", result.AccessToken);
        Assert.Equal("Bearer", result.TokenType);
    }
}
```

- [x] **Step 2: Run the boundary tests and verify they fail**

Run:

```bash
dotnet test HealthExam.Tests/HealthExam.Tests.csproj --filter FullyQualifiedName~HisAuthGatewayContractTests
```

Expected: build fails because `HisAuthModels.cs` and `IHisAuthGateway` do not exist.

- [x] **Step 3: Add the transport-neutral models and interface**

Create `HealthExam.Server/Models/HisAuthModels.cs`:

```csharp
#nullable enable

namespace HealthExam.Server.Models;

public sealed class HisLoginRequest
{
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    public bool ConfirmLogin { get; set; }
}

public sealed class HisLoginResult
{
    public string AccessToken { get; set; } = "";
    public string TokenType { get; set; } = "Bearer";
}
```

Create `HealthExam.Server/Service/IHisAuthGateway.cs`:

```csharp
#nullable enable

using HealthExam.Server.Models;
using Newtonsoft.Json.Linq;

namespace HealthExam.Server.Service;

public interface IHisAuthGateway
{
    Task<HisLoginResult> LoginAsync(HisLoginRequest request, CancellationToken ct = default);
    Task<JToken> GetCurrentUserAsync(CancellationToken ct = default);
    Task LogoutAsync(CancellationToken ct = default);
}
```

- [x] **Step 4: Run the boundary tests and verify they pass**

Run:

```bash
dotnet test HealthExam.Tests/HealthExam.Tests.csproj --filter FullyQualifiedName~HisAuthGatewayContractTests
```

Expected: 2 tests pass.

- [x] **Step 5: Commit the boundary**

```bash
git add HealthExam.Server/Models/HisAuthModels.cs HealthExam.Server/Service/IHisAuthGateway.cs HealthExam.Tests/HisAuthGatewayContractTests.cs
git commit -m "feat(auth): define HIS authentication gateway"
```

---

### Task 2: Implement the HTTP-forwarding gateway

**Files:**
- Create: `HealthExam.Server/Service/HisHttpAuthGateway.cs`
- Test: `HealthExam.Tests/HisHttpAuthGatewayTests.cs`

**Interfaces:**
- Consumes: `IHisAuthGateway`, `HisLoginRequest`, `HisLoginResult`, `HisEmrOptions`, and `IHealthExamContext.GetRequestHeader`.
- Produces: `HisHttpAuthGateway : IHisAuthGateway`; downstream routes `api/Auth/Login`, `api/Auth/Info`, and `api/Auth/Logout`.

- [x] **Step 1: Write failing HTTP contract tests**

Create `HealthExam.Tests/HisHttpAuthGatewayTests.cs` with a recording `HttpMessageHandler` and fake `IHealthExamContext`. Pin these cases:

```csharp
[Fact]
public async Task Login_forwards_credentials_division_and_trace_but_not_authorization()
{
    var (gateway, handler) = CreateGateway(_ => OkEnvelope("his.jwt"));

    var result = await gateway.LoginAsync(new HisLoginRequest
    {
        Username = "doctor",
        Password = "secret",
        ConfirmLogin = true
    });

    Assert.Equal("his.jwt", result.AccessToken);
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
        request.RequestUri!.AbsolutePath.EndsWith("/Info")
            ? OkEnvelope(new JObject { ["EmployeeID"] = 4210 })
            : OkEnvelope(JValue.CreateNull()));

    var user = await gateway.GetCurrentUserAsync();
    await gateway.LogoutAsync();

    Assert.Equal(4210, user["EmployeeID"]!.Value<long>());
    Assert.All(handler.Requests, request =>
        Assert.Equal("Bearer employee.jwt", request.Headers.Authorization!.ToString()));
}
```

Also add focused tests named:

```text
Login_preserves_SESSION_EXIST_payload
Invalid_credentials_map_to_4010
Timeout_maps_to_5040
Network_failure_maps_to_5022
Malformed_success_response_maps_to_5022
Logout_accepts_success_with_null_data
Service_identity_is_rejected_for_current_user_and_logout
```

The fake context must return `ActorKind.Employee`, division `DHTESTING`, trace
`trace-1`, and `Bearer employee.jwt` by default. For the service-identity test,
return `ActorKind.Integration`.

- [x] **Step 2: Run the HTTP gateway tests and verify they fail**

Run:

```bash
dotnet test HealthExam.Tests/HealthExam.Tests.csproj --filter FullyQualifiedName~HisHttpAuthGatewayTests
```

Expected: build fails because `HisHttpAuthGateway` does not exist.

- [x] **Step 3: Implement the non-retrying HTTP adapter**

Create `HealthExam.Server/Service/HisHttpAuthGateway.cs` with these constants and entry points:

```csharp
public sealed class HisHttpAuthGateway : IHisAuthGateway
{
    public const string RouteLogin = "api/Auth/Login";
    public const string RouteInfo = "api/Auth/Info";
    public const string RouteLogout = "api/Auth/Logout";

    public async Task<HisLoginResult> LoginAsync(HisLoginRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Username))
            throw HealthExamException.BadRequest("Tên đăng nhập là bắt buộc");
        if (string.IsNullOrWhiteSpace(request.Password))
            throw HealthExamException.BadRequest("Mật khẩu là bắt buộc");

        var data = await SendAsync(
            HttpMethod.Post,
            RouteLogin,
            request,
            includeCredential: false,
            allowNullData: false,
            ct);
        var token = data!.Type == JTokenType.String ? data.Value<string>() : null;
        if (string.IsNullOrWhiteSpace(token))
            throw new HealthExamException(ErrorCodes.HisBadGateway, "HIS không trả về access token hợp lệ");
        return new HisLoginResult { AccessToken = token };
    }

    public Task<JToken> GetCurrentUserAsync(CancellationToken ct = default)
    {
        RequireEmployee();
        return SendRequiredAsync(HttpMethod.Get, RouteInfo, null, ct);
    }

    public async Task LogoutAsync(CancellationToken ct = default)
    {
        RequireEmployee();
        await SendAsync(HttpMethod.Post, RouteLogout, null, true, true, ct);
    }
}
```

Implement `SendAsync` with a fresh `HttpRequestMessage` for exactly one
`HttpClient.SendAsync` call. Build the URI from `HisEmrOptions.BaseUrl`; forward
`X-Division-Id` and `X-Trace-Id`; add the configured credential header only
when `includeCredential` is true. Deserialize this private wire envelope:

```csharp
private sealed class HisAuthEnvelope
{
    public int ErrorCode { get; set; }
    public string Message { get; set; } = "";
    public string MessageCode { get; set; } = "";
    public JToken? Data { get; set; }
}
```

Map outcomes exactly:

```text
HTTP 401 or invalid credentials -> ErrorCodes.Unauthorized (4010)
HTTP 403                      -> ErrorCodes.Forbidden (4030)
timeout                       -> ErrorCodes.HisTimeout (5040)
connection failure            -> ErrorCodes.HisBadGateway (5022)
malformed/empty response      -> ErrorCodes.HisBadGateway (5022)
MessageCode SESSION_EXIST     -> ErrorCodes.InvalidState (4090), Payload = HIS Data
other HIS business failure    -> ErrorCodes.BadRequest (4001), preserve HIS Message
```

Do not call or inherit `HisEmrClient`: login has no incoming bearer credential,
logout may succeed with null `Data`, and authentication failures have different
error semantics from form-signing failures. Log only operation, status, elapsed
milliseconds, division, and trace ID.

- [x] **Step 4: Run the HTTP gateway tests and verify they pass**

Run:

```bash
dotnet test HealthExam.Tests/HealthExam.Tests.csproj --filter FullyQualifiedName~HisHttpAuthGatewayTests
```

Expected: all gateway tests pass and each operation records exactly one HTTP request.

- [x] **Step 5: Commit the HTTP implementation**

```bash
git add HealthExam.Server/Service/HisHttpAuthGateway.cs HealthExam.Tests/HisHttpAuthGatewayTests.cs
git commit -m "feat(auth): forward authentication to HIS"
```

---

### Task 3: Expose the auth controller and exact anonymous boundary

**Files:**
- Create: `HealthExam.API/Controllers/AuthController.cs`
- Modify: `HealthExam.API/Middlewares/AuthGuardMiddleware.cs`
- Test: `HealthExam.Tests/HisAuthEndpointTests.cs`
- Test: `HealthExam.Tests/AuthGuardTests.cs`

**Interfaces:**
- Consumes: `IHisAuthGateway` and the models from Task 1.
- Produces: `POST /v1/auth/login`, `GET /v1/auth/me`, and `POST /v1/auth/logout` using the standard `ResultData<T>` envelope.

- [x] **Step 1: Write failing route and middleware tests**

Create `HealthExam.Tests/HisAuthEndpointTests.cs` to pin `AuthController`'s base
route and method attributes:

```csharp
[Fact]
public void Controller_defines_the_three_exact_auth_routes()
{
    var type = typeof(AuthController);
    Assert.Equal("v1/auth", type.GetCustomAttribute<RouteAttribute>()!.Template);
    Assert.Equal("login", type.GetMethod("Login")!.GetCustomAttribute<HttpPostAttribute>()!.Template);
    Assert.Equal("me", type.GetMethod("Me")!.GetCustomAttribute<HttpGetAttribute>()!.Template);
    Assert.Equal("logout", type.GetMethod("Logout")!.GetCustomAttribute<HttpPostAttribute>()!.Template);
}
```

Add two integration tests to `AuthGuardTests.cs`:

```csharp
[Fact]
public async Task Anonymous_login_reaches_controller_instead_of_4010()
{
    var response = await _host.CreateAnonymousClient().PostAsJsonAsync(
        "/v1/auth/login",
        new { Username = "doctor", Password = "wrong", ConfirmLogin = false });

    Assert.NotEqual(ErrorCodes.Unauthorized,
        JObject.Parse(await response.Content.ReadAsStringAsync())["ErrorCode"]!.Value<int>());
}

[Theory]
[InlineData("/v1/auth/me", "GET")]
[InlineData("/v1/auth/logout", "POST")]
public async Task Other_auth_routes_are_not_anonymous(string path, string method)
{
    using var request = new HttpRequestMessage(new HttpMethod(method), path);
    var response = await _host.CreateAnonymousClient().SendAsync(request);
    var body = JObject.Parse(await response.Content.ReadAsStringAsync());

    Assert.Equal(ErrorCodes.Unauthorized, body["ErrorCode"]!.Value<int>());
}
```

Add a separate test that sends login without `X-Division-Id` and asserts
`ErrorCodes.BadRequest` plus HTTP 400.

- [x] **Step 2: Run the endpoint tests and verify they fail**

Run:

```bash
dotnet test HealthExam.Tests/HealthExam.Tests.csproj --filter "FullyQualifiedName~HisAuthEndpointTests|FullyQualifiedName~AuthGuardTests"
```

Expected: controller reflection test fails and anonymous login returns 4010.

- [x] **Step 3: Add the controller and method-specific exemption**

Create `HealthExam.API/Controllers/AuthController.cs`:

```csharp
#nullable enable

using HealthExam.Core.Models;
using HealthExam.Server.Models;
using HealthExam.Server.Service;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json.Linq;

namespace HealthExam.API.Controllers;

[Route("v1/auth")]
public sealed class AuthController : HealthExamControllerBase
{
    private readonly IHisAuthGateway _gateway;

    public AuthController(IHisAuthGateway gateway) => _gateway = gateway;

    [HttpPost("login")]
    public async Task<ActionResult<ResultData<HisLoginResult>>> Login(
        [FromBody] HisLoginRequest request,
        CancellationToken ct = default)
        => Success(await _gateway.LoginAsync(request, ct));

    [HttpGet("me")]
    public async Task<ActionResult<ResultData<JToken>>> Me(CancellationToken ct = default)
        => Success(await _gateway.GetCurrentUserAsync(ct));

    [HttpPost("logout")]
    public async Task<ActionResult<ResultData<object>>> Logout(CancellationToken ct = default)
    {
        await _gateway.LogoutAsync(ct);
        return Success<object>(null!);
    }
}
```

In `AuthGuardMiddleware`, replace the broad prefix-only exemption calculation
with an exact helper:

```csharp
private static bool IsAnonymousEndpoint(HttpContext context)
    => HttpMethods.IsPost(context.Request.Method) &&
       context.Request.Path.Equals("/v1/auth/login", StringComparison.OrdinalIgnoreCase);
```

Include `IsAnonymousEndpoint(context)` in the existing `exempt` expression.
Do not add `/v1/auth` to `ExemptPrefixes`.

- [x] **Step 4: Run endpoint and auth-guard tests**

Run:

```bash
dotnet test HealthExam.Tests/HealthExam.Tests.csproj --filter "FullyQualifiedName~HisAuthEndpointTests|FullyQualifiedName~AuthGuardTests"
```

Expected: route tests pass; anonymous login reaches the controller; me/logout still return 4010 anonymously; missing division still returns 4001.

- [x] **Step 5: Commit the public API**

```bash
git add HealthExam.API/Controllers/AuthController.cs HealthExam.API/Middlewares/AuthGuardMiddleware.cs HealthExam.Tests/HisAuthEndpointTests.cs HealthExam.Tests/AuthGuardTests.cs
git commit -m "feat(auth): expose HIS-backed auth endpoints"
```

---

### Task 4: Register the implementation and verify the complete token path

**Files:**
- Modify: `HealthExam.Server/Service/DependencyInjection.cs`
- Modify: `HealthExam.Tests/HisAuthEndpointTests.cs`
- Modify: `docs/superpowers/plans/2026-09-08-his-auth-gateway.md`

**Interfaces:**
- Consumes: `HisHttpAuthGateway : IHisAuthGateway` from Task 2.
- Produces: scoped resolution of `IHisAuthGateway` through `IHttpClientFactory`, using `HisEmrOptions.Timeout` and redirects disabled.

- [x] **Step 1: Write the failing DI test**

Add to `HisAuthEndpointTests.cs`:

```csharp
[Fact]
public async Task DI_resolves_the_HTTP_gateway_through_the_interface()
{
    await using var scope = _host.Services.CreateAsyncScope();
    var gateway = scope.ServiceProvider.GetRequiredService<IHisAuthGateway>();
    Assert.IsType<HisHttpAuthGateway>(gateway);

    var options = scope.ServiceProvider.GetRequiredService<HisEmrOptions>();
    var factory = scope.ServiceProvider.GetRequiredService<IHttpClientFactory>();
    Assert.Equal(options.Timeout, factory.CreateClient(typeof(HisHttpAuthGateway).Name).Timeout);
}
```

- [x] **Step 2: Run the DI test and verify it fails**

Run:

```bash
dotnet test HealthExam.Tests/HealthExam.Tests.csproj --filter FullyQualifiedName~DI_resolves_the_HTTP_gateway
```

Expected: DI cannot resolve `IHisAuthGateway`.

- [x] **Step 3: Register the typed client behind the interface**

Add this beside the existing HIS EMR client registration in
`DependencyInjection.AddHealthExamServices`:

```csharp
services.AddHttpClient<IHisAuthGateway, HisHttpAuthGateway>((sp, http) =>
{
    http.Timeout = sp.GetRequiredService<HisEmrOptions>().Timeout;
}).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
{
    AllowAutoRedirect = false
});
```

Do not register the controller against `HisHttpAuthGateway` directly.

- [x] **Step 4: Run the focused and complete test suites**

Run:

```bash
dotnet test HealthExam.Tests/HealthExam.Tests.csproj --filter "FullyQualifiedName~HisAuth|FullyQualifiedName~AuthGuardTests|FullyQualifiedName~HisEmr"
dotnet test HealthExam.Tests/HealthExam.Tests.csproj
dotnet build HealthExam.sln --no-restore
git diff --check
```

Expected: all tests pass, build succeeds without warnings introduced by this feature, and `git diff --check` prints no errors.

- [x] **Step 5: Mark this plan complete and commit**

Change each completed checkbox in this plan from `- [ ]` to `- [x]`, then run:

```bash
git add HealthExam.Server/Service/DependencyInjection.cs HealthExam.Tests/HisAuthEndpointTests.cs docs/superpowers/plans/2026-09-08-his-auth-gateway.md
git commit -m "test(auth): verify HIS authentication gateway"
```

---

## Manual smoke test

With `HIS_EMR_ENABLED=true`, `HIS_EMR_BASE_URL` pointing to the correct HIS
deployment, and matching `SECRET_KEY`/`SECRET_ISSUER`, run:

```bash
curl -X POST 'http://localhost:9920/v1/auth/login' \
  -H 'Content-Type: application/json' \
  -H 'X-Division-Id: DHTESTING' \
  -d '{"username":"<employee>","password":"<password>","confirmLogin":false}'
```

Copy only the returned access token into a local shell variable without writing
it to source files or logs, then verify:

```bash
curl 'http://localhost:9920/v1/auth/me' \
  -H 'Authorization: Bearer <his-jwt>' \
  -H 'X-Division-Id: DHTESTING'

curl -X POST 'http://localhost:9920/v1/auth/logout' \
  -H 'Authorization: Bearer <his-jwt>' \
  -H 'X-Division-Id: DHTESTING'
```

Expected: login returns the HIS JWT in `Data.AccessToken`, `me` returns the HIS
employee identity, and logout returns `ErrorCode: 0`. Do not perform a real
section signature as part of an authentication smoke test.
