# HIS Authentication Gateway Design

## Goal

Allow the health-exam frontend to sign in once with the employee's existing
HIS username and password, then use the HIS-issued JWT for both
`health-exam-server` and downstream HIS form/signing operations.

The implementation must sit behind an application interface so a later phase
can replace the HTTP-forwarding strategy without changing controllers or the
public API contract.

## Context

`health-exam-server` does not own employee accounts and must not issue a second
employee token. Its existing JWT validation intentionally accepts the token
format issued by HIS/IAM. HIS remains the source of truth for credentials,
active sessions, employee identity, and logout/revocation.

## Architecture boundary

Public controllers depend only on this abstraction:

```csharp
public interface IHisAuthGateway
{
    Task<HisLoginResult> LoginAsync(HisLoginRequest request, CancellationToken cancellationToken);
    Task<object> GetCurrentUserAsync(CancellationToken cancellationToken);
    Task LogoutAsync(CancellationToken cancellationToken);
}
```

Phase 1 registers one implementation:

```text
AuthController
  -> IHisAuthGateway
  -> HisHttpAuthGateway
  -> his-server /api/Auth/*
```

The interface belongs to `health-exam-server`; it must not reference HIS
controller, MediatR, Entity Framework, or database types. The HTTP
implementation is the only component that knows HIS routes and response
shapes. A future implementation may use another identity provider or broker
while preserving the public controller contract.

## Public API

All endpoints continue to require `X-Division-Id`.

### Login

```http
POST /v1/auth/login
Content-Type: application/json
X-Division-Id: DHTESTING

{
  "username": "employee-code",
  "password": "secret",
  "confirmLogin": false
}
```

This is the only anonymous business endpoint. It forwards the body to
`POST /api/Auth/Login` and returns the HIS access token in the normal
health-exam response envelope. When HIS reports `SESSION_EXIST`, the response
preserves the condition so the frontend can repeat the same request with
`confirmLogin: true`.

The password exists only in request memory for the duration of the call. It is
never persisted, cached, included in exception text, or written to logs.

### Current user

```http
GET /v1/auth/me
Authorization: Bearer <his-jwt>
X-Division-Id: DHTESTING
```

The gateway forwards the same bearer token to `GET /api/Auth/Info`. HIS remains
responsible for the returned employee/account information.

### Logout

```http
POST /v1/auth/logout
Authorization: Bearer <his-jwt>
X-Division-Id: DHTESTING
```

The gateway forwards the bearer token to `POST /api/Auth/Logout`. No local
token or credential record is deleted because none is stored locally.

## Token flow

The access token returned by login is the HIS JWT. The frontend uses it as the
ordinary `Authorization` bearer token on every later health-exam request.
Existing health-exam authentication validates that JWT, and the HIS EMR client
forwards it downstream for form reads and per-section signing. There is no
second token and no custom HIS credential header in this flow.

## Middleware behavior

`AuthGuardMiddleware` exempts exactly `POST /v1/auth/login`. It does not exempt
the whole `/v1/auth` prefix. Division validation still runs before the login
controller so tenant selection remains mandatory and deterministic.

`GET /v1/auth/me` and `POST /v1/auth/logout` require an authenticated employee
JWT. The internal `SECRET_INTER` identity must not be accepted for employee
login, current-user lookup, or any signing operation because it contains no
real `EmployeeID`.

## Downstream behavior and errors

- The gateway uses the existing HIS base URL and timeout configuration.
- Login does not forward the incoming `Authorization` header.
- Current-user and logout forward the authenticated HIS bearer token unchanged.
- HIS business outcomes, including bad credentials and `SESSION_EXIST`, are
  translated into the standard health-exam envelope without exposing raw HTTP
  or parsing errors.
- Network failures and timeouts use the existing HIS integration error family.
- Authentication calls are never retried automatically. In particular, a
  repeated login with `confirmLogin: true` could revoke another active token.
- No refresh endpoint is exposed because the current HIS authentication API
  does not issue refresh tokens.

## Security and observability

- Do not log request or response bodies for login.
- Redact `Password` and `Authorization` in middleware, HTTP-client, and
  exception logs.
- Do not return the password in validation errors.
- Use TLS for the HIS base URL outside local development.
- Log operation name, downstream status, elapsed time, division, and TraceID;
  never log access tokens or credential hashes.

## Testing

- Controller contract tests for login, current user, and logout.
- An anonymous request can reach only `POST /v1/auth/login`.
- Login still fails when `X-Division-Id` is absent.
- The HTTP implementation forwards the exact login fields and does not forward
  an unrelated bearer token.
- Current-user and logout forward the caller's bearer token.
- `SESSION_EXIST`, invalid credentials, timeout, malformed response, and HIS
  unavailability are mapped deterministically.
- DI resolves `IHisAuthGateway` to the HTTP implementation.
- A successful HIS token authenticates a subsequent health-exam request and is
  forwarded by the existing HIS EMR form/signing path.

## Out of scope

- Storing usernames, passwords, or HIS JWTs in the health-exam database.
- Issuing a health-exam-specific employee JWT.
- Refresh-token support.
- Password change/reset APIs.
- Reimplementing HIS account, session, or authorization rules.
- Modifying `his-server`.
