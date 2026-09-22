# Exam Form Interface and HIS Gateway Design

## Context

The health-exam API currently exposes eight form operations backed by
`his-server`: one standalone form-definition endpoint and seven operations
scoped to an exam record. Controllers and application services depend directly
on concrete HIS-named classes. Tests replace these dependencies by subclassing
the concrete classes and overriding virtual methods.

The current implementation works, but its dependency direction makes replacing
HIS with a future business implementation unnecessarily expensive. The API
boundary should depend on a stable, provider-neutral contract, while HIS remains
the first adapter behind that contract.

## Goals

- Make every existing HIS-form controller action depend on one
  provider-neutral interface.
- Implement that interface with a gateway that calls the same HIS APIs and
  preserves the current behavior.
- Isolate HIS HTTP transport details behind a smaller internal interface.
- Allow a future implementation to replace HIS by changing dependency
  injection registration only.
- Replace subclass-based test doubles with interface-based fakes.
- Preserve all existing HTTP routes, request and response JSON, error codes,
  status codes, authorization behavior, caching behavior, and HIS side effects.

## Non-goals

- Adding, removing, or versioning an endpoint.
- Changing the database schema or the `AdmissionID` association.
- Modifying `his-server`.
- Changing authentication or introducing a service credential.
- Adding a provider factory, runtime provider switch, or provider configuration.
- Falling back to another provider when HIS is unavailable.
- Converting the currently opaque process and workflow JSON into typed domain
  models.
- Changing the form-definition source, template code, signing workflow, or
  retry policy.

## Existing API Surface

All eight operations move behind the new application contract:

1. Read a form definition by template code.
2. Read the form definition and HIS admission associated with an exam record.
3. Read medical processes associated with an exam record.
4. Read one medical process and its sections.
5. Read the signing workflow.
6. Submit a section for signing.
7. Sign a section.
8. Cancel a section signature.

Their current routes and wire contracts remain unchanged.

## Architecture

The dependency direction becomes:

```text
HisFormController ───────────────┐
                                ├──> IExamFormService
ExamRecordHisFormController ─────┘             │
                                               ▼
                                    HisExamFormGateway
                                      ├── IUnitOfWork
                                      ├── IHealthExamContext
                                      ├── IMemoryCache
                                      └── IHisEmrApi
                                               │
                                               ▼
                                      HisEmrHttpClient
                                               │
                                               ▼
                                           his-server
```

### `IExamFormService`

`IExamFormService` is the application port consumed by both controllers. It
contains the eight asynchronous operations listed above and accepts a
`CancellationToken` for every operation. Its method and model names must not
refer to HIS.

The interface represents the stable behavior of the health-exam API, not the
remote HIS endpoint structure. It does not expose an `HttpClient`, URL, header,
HIS wire request, or provider-selection concern.

### `HisExamFormGateway`

`HisExamFormGateway` is the initial implementation of `IExamFormService`. It
absorbs the responsibilities currently split between
`HisExamFormService` and `HisFormDefinitionService` while keeping focused
private collaborators or components where that improves readability.

It owns provider-specific orchestration and the application safety checks that
must happen before calling HIS:

- resolving and caching the active `KSK-TREN18TUOI` definition;
- locating an exam record within the authenticated division;
- requiring a valid `AdmissionID`;
- verifying that a medical process belongs to the record and target form;
- verifying the requested section exists;
- ensuring the authenticated employee is the requested signer;
- mapping neutral application requests to HIS wire requests.

These checks stay above the HTTP transport because they require health-exam
state and define the behavior of this HIS-backed implementation.

### `IHisEmrApi`

`IHisEmrApi` is an internal transport port used only by the HIS-backed gateway.
It exposes the exact remote capabilities required by the gateway: template
reads, medical-process reads, signing-workflow reads, and section submit, sign,
and cancel operations.

It exists to separate orchestration from HTTP and to make gateway tests use
plain fakes instead of subclasses of a concrete client. It is not the extension
point selected by controllers and must not be used as a substitute for
`IExamFormService`.

### `HisEmrHttpClient`

`HisEmrHttpClient` implements `IHisEmrApi`. It is the only component allowed to
know HIS route names, query parameter names, HTTP methods, credential headers,
wire payloads, response-envelope parsing, connection retries, and timeout/error
translation.

The existing HIS route constants and transport behavior move to or remain in
this class without semantic changes.

## Contracts

Application-facing CLR types become provider-neutral while retaining the same
serialized JSON properties:

| Current type | Provider-neutral type |
| --- | --- |
| `HisFormDefinition` | `ExamFormDefinition` |
| `ExamRecordHisForm` | `ExamRecordForm` |
| `HisSectionSubmitRequest` | `FormSectionSubmitRequest` |
| `HisSectionSignRequest` | `FormSectionSignRequest` |
| `HisSectionCancelRequest` | `FormSectionCancelRequest` |

HIS wire models such as `HisSubmitWireRequest`, `HisSignWireRequest`, and
`HisCancelWireRequest` remain provider-specific and are visible only within the
HIS adapter boundary.

Medical-process details, signing workflows, and mutation results currently pass
through as `JToken`. They remain opaque `JToken` values in this change. This is
an explicit compatibility boundary: a future `IExamFormService` implementation
must produce the same JSON expected by current API clients. Replacing these
payloads with typed models requires a separately designed, versioned contract
change.

## Data Flows

### Definition read

1. `HisFormController` validates routing/binding concerns and calls
   `IExamFormService`.
2. `HisExamFormGateway` validates the supported template code.
3. The gateway checks the definition cache using the normalized HIS base URL,
   division ID, and template code.
4. On a miss, the gateway uses `IHisEmrApi` to resolve the single active
   template and fetch its metadata and layout.
5. Only a successful, valid definition is cached and returned.

The existing per-key single-flight locking and configured cache duration remain
unchanged. Cache behavior belongs to the HIS implementation because its key and
freshness assumptions are provider-specific.

### Record-scoped read

1. The controller calls `IExamFormService` with the record identifier.
2. The gateway loads the record within the current division.
3. Operations that need HIS encounter state require a positive `AdmissionID`.
4. The gateway loads HIS state through `IHisEmrApi` and filters or validates it
   against the resolved target definition.
5. The gateway returns the same serialized result as the current API.

### Section mutation

1. The gateway validates the neutral request.
2. It loads the record, requires its admission, verifies process ownership, and
   verifies the section key.
3. Signing additionally requires the requested employee to match the
   authenticated actor.
4. The gateway maps the neutral request to the HIS wire payload.
5. `IHisEmrApi` performs exactly one mutation request.
6. Success is returned only when HIS reports success; the gateway never
   synthesizes a successful result.

## Dependency Injection

The composition root registers:

```text
IExamFormService -> HisExamFormGateway
IHisEmrApi       -> HisEmrHttpClient
```

`HisEmrHttpClient` remains a typed HTTP client configured with the existing HIS
timeout. The high-level binding uses the normal application lifetime required by
its database context and request context.

There is deliberately no runtime provider selector. A future implementation is
activated by replacing the `IExamFormService` registration. No controller,
route, or API model should need to change.

Application startup or DI validation must detect a missing
`IExamFormService` binding rather than deferring the failure until a form
endpoint is called.

## Authentication and Security

- The inbound user's configured HIS credential is read only by the HIS HTTP
  adapter when constructing an outbound request.
- The credential is never a parameter of `IExamFormService`, stored in an
  application model, cached, or logged.
- The adapter forwards the credential only to the configured HIS base URL.
- Trace and division headers retain their current behavior.
- Missing credentials retain the current unauthorized result.
- The gateway continues to validate the requested signer against the
  authenticated actor before calling HIS.

## Error Handling and Resilience

The refactor preserves current externally observable failures.

The gateway owns health-exam and orchestration errors, including unsupported
templates, missing records, missing admissions, non-owned processes, unknown
sections, invalid mutation input, and signer mismatches.

The HTTP adapter owns transport errors, including invalid or disabled HIS
configuration, missing forwarded credentials, connection failures, timeouts,
downstream HTTP errors, and malformed HIS envelopes.

Existing mappings to `HealthExamException`, `ErrorCode`, HTTP status, and user
message remain unchanged. The adapter retains the existing single retry for the
same safe definition GET operations. Medical-process reads and all submit,
sign, and cancel requests are not automatically retried. No failure triggers a
fallback provider.

## Testing Strategy

### Contract and controller tests

- Verify both form controllers depend only on `IExamFormService`.
- Preserve exact routes, HTTP methods, Swagger-visible request/response types,
  JSON property names, result envelopes, status codes, and error codes.
- Exercise controllers with a fake `IExamFormService`.

### Gateway tests

- Use a fake `IHisEmrApi`; do not inherit from the HTTP client.
- Cover template resolution, duplicate or missing active definitions, cache hits,
  cache isolation, and the rule that failures are not cached.
- Cover division-scoped record lookup, required admission, process ownership,
  form filtering, section validation, and signer validation.
- Verify neutral requests map to the exact HIS wire values.
- Verify a downstream failure never becomes a local success.

### HTTP adapter tests

- Preserve tests for routes, query strings, JSON payloads, forwarded credential,
  trace/division headers, envelope parsing, configuration validation, HTTP error
  mapping, timeout behavior, and retry policy.
- Explicitly verify submit, sign, and cancel are sent at most once.

### Dependency injection acceptance tests

- Resolve `IExamFormService` as `HisExamFormGateway` under the default
  registration.
- Resolve `IHisEmrApi` as the configured typed HTTP adapter.
- Replace `IExamFormService` with a fake implementation and verify both
  controllers operate without registering or calling the HIS implementation.

The final test is the acceptance criterion for provider replaceability.

## Migration Sequence

1. Add provider-neutral application contracts and characterize the existing API
   serialization with regression tests.
2. Introduce `IHisEmrApi` and make the current HTTP client implement it without
   changing transport behavior.
3. Implement `HisExamFormGateway` against `IHisEmrApi`, preserving the current
   orchestration and cache behavior.
4. Move both controllers to `IExamFormService` and neutral CLR types while
   preserving their JSON contract.
5. Replace concrete DI registrations with the two interface bindings.
6. Remove superseded concrete services and subclass-based test doubles after all
   regression and acceptance tests pass.

The migration does not require a database migration, downstream HIS deployment,
or frontend rollout.

## Acceptance Criteria

- Every existing form endpoint resolves and invokes `IExamFormService` rather
  than a concrete HIS service.
- The default implementation calls the same `his-server` endpoints with the
  same credentials, validation, caching, retry rules, and mutation semantics.
- Existing clients observe no route, JSON, status, or error-code changes.
- No provider-specific type is exposed by the controller/application contract,
  except the explicitly retained opaque `JToken` payloads.
- Tests no longer subclass `HisEmrClient`, `HisFormDefinitionService`, or
  `HisExamFormService` to replace behavior.
- A fake `IExamFormService` can replace the HIS-backed gateway by changing DI
  registration only.
