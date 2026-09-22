# HIS-backed KSK Form API Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Expose the active HIS form `KSK-TREN18TUOI` and its per-section medical-process signing operations through `health-exam-server`, without reading or writing `form-server`.

**Architecture:** Add a typed HTTP adapter for HIS M03/M02, a five-minute cached definition service, and a record-scoped orchestration service that verifies tenant, admission, process, section, and signer ownership before forwarding calls. `HEX_ExamRecord.AdmissionID` is the only locally persisted HIS link; form structure, entered values, workflows, and signatures remain owned by HIS.

**Tech Stack:** .NET 8, ASP.NET Core controllers, EF Core 8/Npgsql, `IHttpClientFactory`, Newtonsoft.Json `JToken`, `IMemoryCache`, xUnit.

**Spec:** `docs/superpowers/specs/2026-09-08-his-ksk-form-api-design.md`

## Global Constraints

- HIS is the source of truth for template metadata/layout, medical processes, workflows, form values, and signatures.
- Phase 1 may mutate only `SubmitEMR`, `SignEMR`, and `CancleEMR`; never expose or call either M02/M03 `CUEMR` or any template mutation.
- Resolve only the exact case-insensitive active template code `KSK-TREN18TUOI`; never resolve by Vietnamese display name.
- Treat `sectionKey` as opaque `string` from `M02_MedicalProcessDetail.ItemGroupID`; never parse it as numeric M03 `ItemGroupID`.
- Require `HEX_ExamRecord.AdmissionID > 0` for process/workflow/signing routes; never substitute `PatientID`, `SessionID`, or `RecordID`.
- Forward the real inbound HIS credential only to the configured HIS origin; never log, cache, persist, or return it.
- Cache only successful form-definition reads for 300 seconds. Never cache medical-process, workflow, or signing state.
- A definition GET may retry once only for a connection failure before response bytes; submit/sign/cancel must have exactly one HTTP attempt.
- Preserve the existing `ResultData<T>` public envelope and existing employee authentication middleware.
- Do not persist duplicate section signature state in health-exam.

## File map

- `HealthExam.Core/EntityFramework/Entity/ExamRecord.cs`: nullable cross-service `AdmissionID`.
- `HealthExam.Core/Models/IHealthExamContext.cs`: safe access to the configured inbound credential header.
- `HealthExam.Core/Models/ErrorCodes.cs`: explicit HIS bad-gateway/timeout mappings.
- `HealthExam.Core/Migrations/*AddHisAdmissionId*`: nullable database migration and snapshot.
- `HealthExam.Server/Models/ExamRecordModels.cs`: expose/accept `AdmissionID` in existing record contracts.
- `HealthExam.Server/Models/HisEmrModels.cs`: public normalized contracts plus private HIS wire payload contracts.
- `HealthExam.Server/Service/HisEmrOptions.cs`: environment-backed enablement, origin, timeout, credential-header, and cache settings.
- `HealthExam.Server/Service/HisEmrClient.cs`: only class allowed to call HIS M03/M02 routes.
- `HealthExam.Server/Service/HisFormDefinitionService.cs`: exact template resolution, normalization, and definition cache.
- `HealthExam.Server/Service/HisExamFormService.cs`: record/admission/process/section/signer authorization boundary.
- `HealthExam.Server/Service/DependencyInjection.cs`: memory cache, options, client, and service wiring.
- `HealthExam.API/Middlewares/HealthExamRequestContext.cs`: read a requested inbound header without exposing `HttpContext` to Server.
- `HealthExam.API/Controllers/HisFormController.cs`: template-code definition endpoint.
- `HealthExam.API/Controllers/ExamRecordHisFormController.cs`: record-scoped reads and section mutations.
- `HealthExam.Tests/HisAdmissionLinkTests.cs`: persistence/API mapping regression coverage.
- `HealthExam.Tests/HisEmrClientTests.cs`: exact downstream HTTP contract and failure mapping.
- `HealthExam.Tests/HisFormDefinitionTests.cs`: resolution/normalization/cache behavior.
- `HealthExam.Tests/HisExamFormServiceTests.cs`: tenant/admission/process/section/signer gates.
- `HealthExam.Tests/HisFormEndpointTests.cs`: route/action contracts and DI wiring.
- `.env.example` and `README.md`: deployment configuration and phase-1 limitations.

---

### Task 1: Persist and expose the HIS admission link

**Files:**
- Modify: `HealthExam.Core/EntityFramework/Entity/ExamRecord.cs`
- Modify: `HealthExam.Server/Models/ExamRecordModels.cs`
- Modify: `HealthExam.Server/Service/ExamRecordService.cs`
- Create: `HealthExam.Tests/HisAdmissionLinkTests.cs`
- Create: `HealthExam.Core/Migrations/*_AddHisAdmissionId.cs` (timestamped by `dotnet ef`)
- Create: `HealthExam.Core/Migrations/*_AddHisAdmissionId.Designer.cs` (timestamped by `dotnet ef`)
- Modify: `HealthExam.Core/Migrations/HealthExamDbContextModelSnapshot.cs`

**Interfaces:**
- Produces: `ExamRecord.AdmissionID`, `ExamRecordItem.AdmissionID`, and `ExamRecordWriteRequest.AdmissionID`, all `long?`.
- Produces: create/update/read behavior that preserves `null` and positive HIS admission identifiers.

- [x] **Step 1: Write failing mapping and persistence-model tests**

Add tests that assert the property exists with the correct nullable type and that existing record create/update mapping carries it:

```csharp
[Fact]
public void Exam_record_contracts_expose_nullable_his_admission_id()
{
    Assert.Equal(typeof(long?), typeof(ExamRecord).GetProperty("AdmissionID")!.PropertyType);
    Assert.Equal(typeof(long?), typeof(ExamRecordItem).GetProperty("AdmissionID")!.PropertyType);
    Assert.Equal(typeof(long?), typeof(ExamRecordWriteRequest).GetProperty("AdmissionID")!.PropertyType);
}

[Fact]
public async Task Update_accepts_and_returns_his_admission_id()
{
    using var db = new InMemoryTestDb();
    var record = db.SeedRecord();
    var item = await db.Records.UpdateAsync(record.RecordID,
        new ExamRecordWriteRequest { AdmissionID = 9000123 });
    Assert.Equal(9000123, item.AdmissionID);
}
```

- [x] **Step 2: Run the focused tests and confirm RED**

Run: `dotnet test HealthExam.Tests/HealthExam.Tests.csproj --filter FullyQualifiedName~HisAdmissionLinkTests`

Expected: compilation/test failure because `AdmissionID` is absent.

- [x] **Step 3: Add the minimal entity, request, response, and mapping fields**

Add beside `PatientID`:

```csharp
/// <summary>Con trỏ HIS M02_Admission; không có foreign key vì khác service/database.</summary>
public long? AdmissionID { get; set; }
```

In `ExamRecordService.ApplyAsync` update only when supplied, and in `ToItem` return it:

```csharp
if (req.AdmissionID.HasValue) entity.AdmissionID = req.AdmissionID.Value;
// ...
AdmissionID = x.AdmissionID,
```

Reject non-positive supplied values with `HealthExamException.BadRequest` and `ValidationErrors.Of(nameof(req.AdmissionID), "Phải lớn hơn 0")`.

- [x] **Step 4: Generate and inspect the EF migration**

Run:

```bash
dotnet ef migrations add AddHisAdmissionId \
  --project HealthExam.Core/HealthExam.Core.csproj \
  --startup-project HealthExam.API/HealthExam.API.csproj
```

The `Up` operation must add nullable `bigint "AdmissionID"` to `HEX_ExamRecord`; `Down` must drop only that column. There must be no foreign key or index unless an observed query plan later justifies one.

- [x] **Step 5: Run focused and migration regression tests**

Run: `dotnet test HealthExam.Tests/HealthExam.Tests.csproj --filter "FullyQualifiedName~HisAdmissionLinkTests|FullyQualifiedName~RegistrationPersistenceModelTests"`

Expected: PASS.

- [x] **Step 6: Commit the admission-link slice**

```bash
git add HealthExam.Core/EntityFramework/Entity/ExamRecord.cs HealthExam.Core/Migrations HealthExam.Server/Models/ExamRecordModels.cs HealthExam.Server/Service/ExamRecordService.cs HealthExam.Tests/HisAdmissionLinkTests.cs
git commit -m "feat: link exam records to HIS admissions"
```

---

### Task 2: Define configuration, errors, context access, and contracts

**Files:**
- Modify: `HealthExam.Core/Models/IHealthExamContext.cs`
- Modify: `HealthExam.Core/Models/ErrorCodes.cs`
- Modify: `HealthExam.API/Middlewares/HealthExamRequestContext.cs`
- Modify: `HealthExam.Tests/FakeHealthExamContext.cs`
- Create: `HealthExam.Server/Service/HisEmrOptions.cs`
- Create: `HealthExam.Server/Models/HisEmrModels.cs`
- Create: `HealthExam.Tests/HisEmrConfigurationTests.cs`

**Interfaces:**
- Produces: `string IHealthExamContext.GetRequestHeader(string name)`.
- Produces: `HisEmrOptions.FromEnvironment()` with `Enabled`, `BaseUrl`, `Timeout`, `CredentialHeaderName`, and `DefinitionCacheDuration`.
- Produces: public response/request types used by Tasks 4–6.

- [x] **Step 1: Write failing option, header, and error-code tests**

```csharp
[Fact]
public void Defaults_are_safe_for_phase_one()
{
    using var env = new TemporaryEnvironment(
        (HisEmrOptions.EnabledEnv, null),
        (HisEmrOptions.BaseUrlEnv, null));
    var options = HisEmrOptions.FromEnvironment();
    Assert.False(options.Enabled);
    Assert.Equal("Authorization", options.CredentialHeaderName);
    Assert.Equal(TimeSpan.FromSeconds(300), options.DefinitionCacheDuration);
}

[Theory]
[InlineData(ErrorCodes.HisBadGateway, 502)]
[InlineData(ErrorCodes.HisTimeout, 504)]
public void His_transport_codes_map_to_expected_http_status(int code, int status)
    => Assert.Equal(status, ErrorCodes.ToHttpStatus(code));
```

- [x] **Step 2: Run focused tests and confirm RED**

Run: `dotnet test HealthExam.Tests/HealthExam.Tests.csproj --filter FullyQualifiedName~HisEmrConfigurationTests`

Expected: compilation failure for the new interfaces/types.

- [x] **Step 3: Implement environment parsing and request-header access**

Use these exact variables/defaults:

```csharp
public const string EnabledEnv = "HIS_EMR_ENABLED";
public const string BaseUrlEnv = "HIS_EMR_BASE_URL";
public const string TimeoutEnv = "HIS_EMR_TIMEOUT_SECONDS";
public const string CredentialHeaderEnv = "HIS_EMR_CREDENTIAL_HEADER";
public const string CacheSecondsEnv = "HIS_EMR_DEFINITION_CACHE_SECONDS";
public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);
public static readonly TimeSpan DefaultDefinitionCacheDuration = TimeSpan.FromSeconds(300);
```

`HealthExamRequestContext.GetRequestHeader` must return `Ctx?.Request.Headers[name].ToString() ?? ""`; the fake context stores a case-insensitive `Dictionary<string,string>`.

Add `HisBadGateway = 5022` and `HisTimeout = 5040`, map them to HTTP 502 and 504, and add clear default messages. Keep existing `DependencyUnavailable` behavior unchanged.

- [x] **Step 4: Add normalized public and HIS wire contracts**

Use `JToken/JArray/JObject` for dynamic HIS shapes so unknown render/process fields survive phase 1:

```csharp
public sealed class HisFormDefinition
{
    public Guid TemplateId { get; init; }
    public string TemplateCode { get; init; } = "";
    public string TemplateName { get; init; } = "";
    public int FileDocTypeId { get; init; }
    public string VersionCode { get; init; } = "";
    public bool IsDraft { get; init; }
    public bool Active { get; init; }
    public JArray Details { get; init; } = new();
    public JArray Layout { get; init; } = new();
}

public sealed record ExamRecordHisForm(Guid RecordId, long? AdmissionId, HisFormDefinition Definition);
public sealed record HisSectionSubmitRequest(int SWStep, JArray SignatoryFlows);
public sealed record HisSectionSignRequest(int SWStep, long EmployeeId, long HandledRoleId);
public sealed record HisSectionCancelRequest(int SWStep, string Reason);

internal sealed class HisSubmitWireRequest
{
    [JsonProperty("Id")] public Guid Id { get; init; }
    [JsonProperty("SWStep")] public int SWStep { get; init; }
    [JsonProperty("ItemGroupID")] public string ItemGroupID { get; init; } = "";
    [JsonProperty("SignatoryFlows")] public JArray SignatoryFlows { get; init; } = new();
}
```

Define equivalent concrete `HisSignWireRequest` (`Id`, `SWStep`, `ItemGroupID`, `EmpID`, `HandledRoleID`) and `HisCancelWireRequest` (`Id`, `SWStep`, `ItemGroupID`, `Reason`).

- [x] **Step 5: Run the focused configuration tests**

Run: `dotnet test HealthExam.Tests/HealthExam.Tests.csproj --filter FullyQualifiedName~HisEmrConfigurationTests`

Expected: PASS.

- [x] **Step 6: Commit contracts and configuration**

```bash
git add HealthExam.Core/Models/IHealthExamContext.cs HealthExam.Core/Models/ErrorCodes.cs HealthExam.API/Middlewares/HealthExamRequestContext.cs HealthExam.Tests/FakeHealthExamContext.cs HealthExam.Server/Models/HisEmrModels.cs HealthExam.Server/Service/HisEmrOptions.cs HealthExam.Tests/HisEmrConfigurationTests.cs
git commit -m "feat: define HIS EMR integration contracts"
```

---

### Task 3: Build the non-retrying HIS HTTP adapter

**Files:**
- Create: `HealthExam.Server/Service/HisEmrClient.cs`
- Create: `HealthExam.Tests/HisEmrClientTests.cs`

**Interfaces:**
- Consumes: `HisEmrOptions`, `IHealthExamContext.GetRequestHeader`, and wire request types from Task 2.
- Produces: `GetTemplateListAsync`, `GetTemplateAsync`, `GetTemplateTreeAsync`, `GetTreeByFileDocTypeAsync`, `GetMedicalProcessesAsync`, `GetMedicalProcessAsync`, `GetSignWorkflowAsync`, `SubmitSectionAsync`, `SignSectionAsync`, and `CancelSectionAsync`, each returning successful HIS `Data` as `JToken`.

- [x] **Step 1: Write failing exact-wire contract tests**

Using a recording `HttpMessageHandler`, assert these routes and query names exactly:

```csharp
await client.GetTemplateAsync(templateId, ct);
Assert.Equal($"https://his.test/api/M03F00030/GetTemplateByID?templateID={templateId}", call.Url);

await client.GetMedicalProcessesAsync(901, ct);
Assert.EndsWith("/api/M02F01500/RMedicalProcess?admissionID=901", call.Url);

await client.SignSectionAsync(new HisSignWireRequest
{
    Id = processId, SWStep = 2, ItemGroupID = "KSK_NOI", EmpID = 456, HandledRoleID = 12
}, ct);
Assert.Equal("456", JObject.Parse(call.Body)["EmpID"]!.ToString());
Assert.Equal("KSK_NOI", JObject.Parse(call.Body)["ItemGroupID"]!.ToString());
```

Also assert the configured credential header value is forwarded unchanged, `X-Trace-Id` and `X-Division-Id` are forwarded, and no request includes credentials in its URI/body.

- [x] **Step 2: Write failing resilience/error tests**

Cover:

- missing/disabled configuration fails before the handler is called;
- missing inbound credential raises `4010` before the handler is called;
- HIS 401/403/404 map to health-exam 4010/4030/4040;
- HIS HTTP 400 or a nonzero business envelope maps to `4221` and preserves its message;
- HTML/malformed JSON or success with missing `Data` maps to `5022`;
- `TaskCanceledException` not caused by caller cancellation maps to `5040`;
- `SubmitSectionAsync`, `SignSectionAsync`, and `CancelSectionAsync` each make exactly one attempt;
- definition GET retries once only for `HttpRequestException` before any response exists and does not cache/retry an HTTP response.

- [x] **Step 3: Run client tests and confirm RED**

Run: `dotnet test HealthExam.Tests/HealthExam.Tests.csproj --filter FullyQualifiedName~HisEmrClientTests`

Expected: compilation failure because `HisEmrClient` is absent.

- [x] **Step 4: Implement the typed adapter with a single envelope reader**

All routes must be constants in `HisEmrClient`. Use PascalCase serialization and one request sender:

```csharp
private Task<JToken> SendAsync(
    Func<HttpRequestMessage> requestFactory,
    string operation,
    bool retryConnectionOnce,
    CancellationToken ct);
```

Before sending, require `options.Enabled`, an absolute HTTP(S) base URI, and a nonblank configured credential header value. Build URLs from one validated base `Uri` and relative paths so credentials can only be sent to that origin. Do not enable automatic redirect following for the registered handler; a redirect could forward credentials to a different host.

Deserialize only this envelope at the boundary:

```csharp
private sealed class HisEnvelope
{
    public int ErrorCode { get; set; }
    public string Message { get; set; } = "";
    public JToken Data { get; set; }
    public string TraceID { get; set; } = "";
}
```

Log operation, downstream status, elapsed milliseconds, trace ID, and orchestration identifiers supplied by the caller; never log request headers or bodies.

- [x] **Step 5: Run client tests and confirm GREEN**

Run: `dotnet test HealthExam.Tests/HealthExam.Tests.csproj --filter FullyQualifiedName~HisEmrClientTests`

Expected: PASS, including the exact one-attempt counts for all mutations.

- [x] **Step 6: Commit the HIS client slice**

```bash
git add HealthExam.Server/Service/HisEmrClient.cs HealthExam.Tests/HisEmrClientTests.cs
git commit -m "feat: add HIS EMR HTTP client"
```

---

### Task 4: Resolve and cache the exact KSK definition

**Files:**
- Modify: `HealthExam.Server/HealthExam.Server.csproj`
- Create: `HealthExam.Server/Service/HisFormDefinitionService.cs`
- Create: `HealthExam.Tests/HisFormDefinitionTests.cs`

**Interfaces:**
- Consumes: M03 methods from `HisEmrClient`.
- Produces: `Task<HisFormDefinition> GetAsync(string templateCode, CancellationToken ct)`.
- Produces: cache key `{normalized-base-url}|{division-id}|{upper-template-code}` with configured absolute expiration.

- [x] **Step 1: Write failing resolution/normalization tests**

Test that the service:

- selects exactly one object where `TemplateCode` equals `KSK-TREN18TUOI` ordinal-ignore-case and `Active == true`;
- returns `4040` for no active match and `4090` for duplicate active matches;
- calls `GetTemplateByID` and `GetTreeTemplateByID` after resolving the ID;
- returns `TemplateId`, code/name, `FileDocTypeId`, version, draft/active flags;
- passes `Details` and layout rows as deep-cloned `JArray` values, preserving `OrderNo`, `OrderString`, `LevelNo`, `ParentItemID`, `ItemRootID`, numeric `ItemGroupID`, control/default/formula fields, and unknown fields.

- [x] **Step 2: Write failing cache tests**

Use a controllable `TimeProvider` or short injected cache duration to prove:

```csharp
var first = await service.GetAsync("KSK-TREN18TUOI", ct);
var second = await service.GetAsync("ksk-tren18tuoi", ct);
Assert.Same(first, second);
Assert.Equal(1, fakeClient.TemplateListCalls);
```

Also prove failures are not cached and a different division creates a different key.

- [x] **Step 3: Run focused tests and confirm RED**

Run: `dotnet test HealthExam.Tests/HealthExam.Tests.csproj --filter FullyQualifiedName~HisFormDefinitionTests`

Expected: compilation failure because the service is absent.

- [x] **Step 4: Add memory-cache dependency and implement minimal service**

Add `Microsoft.Extensions.Caching.Memory` version `8.0.1`. Resolve the list first, then fetch metadata and layout concurrently:

```csharp
var metadataTask = _his.GetTemplateAsync(templateId, ct);
var layoutTask = _his.GetTemplateTreeAsync(templateId, admissionId: null, ct);
await Task.WhenAll(metadataTask, layoutTask);
```

Use a per-key `SemaphoreSlim` or cached task to prevent a cold-cache stampede. Cache only after both calls and normalization succeed; remove failed/cancelled tasks immediately.

- [x] **Step 5: Run definition tests and confirm GREEN**

Run: `dotnet test HealthExam.Tests/HealthExam.Tests.csproj --filter FullyQualifiedName~HisFormDefinitionTests`

Expected: PASS.

- [x] **Step 6: Commit definition resolution and cache**

```bash
git add HealthExam.Server/HealthExam.Server.csproj HealthExam.Server/Service/HisFormDefinitionService.cs HealthExam.Tests/HisFormDefinitionTests.cs
git commit -m "feat: resolve cached HIS KSK form definition"
```

---

### Task 5: Enforce record, process, section, and signer ownership

**Files:**
- Create: `HealthExam.Server/Service/HisExamFormService.cs`
- Create: `HealthExam.Tests/HisExamFormServiceTests.cs`

**Interfaces:**
- Consumes: `IUnitOfWork`, `IHealthExamContext`, `HisFormDefinitionService`, and M02 methods from `HisEmrClient`.
- Produces: record-scoped `GetFormAsync`, `GetProcessesAsync`, `GetProcessAsync`, `GetSignWorkflowAsync`, `SubmitSectionAsync`, `SignSectionAsync`, and `CancelSectionAsync`.

- [x] **Step 1: Write failing record/admission/process gate tests**

Cover:

```csharp
// Every record lookup includes both keys.
x => x.RecordID == recordId && x.DivisionID == context.DivisionId
```

- unknown/cross-division record -> `4040`;
- definition access works with null admission and returns it in the envelope;
- process/workflow/signing with null or non-positive admission -> `4001`, field `AdmissionID`;
- process list is filtered to the definition `TemplateId` or `FileDocTypeId`;
- requested process ID must be present in the filtered `RMedicalProcess(admissionID)` response before `RMedicalProcessByID`, workflow, or mutation calls occur.

- [x] **Step 2: Write failing section/signer/mutation tests**

Given process detail rows containing `ItemGroupID: "KSK_NOI"`, assert:

- exact `KSK_NOI` is accepted and an unknown key is rejected before mutation;
- the opaque value is passed unchanged, including nonnumeric keys;
- `EmployeeId != context.ActorId` or non-employee context returns `4030` before HIS;
- blank cancel reason returns `4001` after trimming;
- the service re-reads ownership/current process detail immediately before each mutation;
- downstream failure is propagated and never converted to success.

- [x] **Step 3: Run focused tests and confirm RED**

Run: `dotnet test HealthExam.Tests/HealthExam.Tests.csproj --filter FullyQualifiedName~HisExamFormServiceTests`

Expected: compilation failure because `HisExamFormService` is absent.

- [x] **Step 4: Implement shared authorization helpers**

Use focused private helpers with these signatures:

```csharp
private Task<ExamRecord> RequireRecordAsync(Guid recordId, CancellationToken ct);
private static long RequireAdmission(ExamRecord record);
private Task<JObject> RequireOwnedProcessAsync(ExamRecord record, Guid processId, CancellationToken ct);
private static void RequireSection(JToken processDetail, string sectionKey);
private void RequireSigner(long employeeId);
```

Compare GUIDs and numeric IDs using invariant parsing, but compare `sectionKey` as exact ordinal string after rejecting only null/blank route values. `RequireSection` inspects the current process-detail rows returned by `RMedicalProcessByID`; it must not use numeric layout `ItemGroupID`.

- [x] **Step 5: Implement reads and mutation forwarding**

For each mutation, construct only the downstream fields approved by the spec:

```csharp
return await _his.SignSectionAsync(new HisSignWireRequest
{
    Id = processId,
    SWStep = request.SWStep,
    ItemGroupID = sectionKey,
    EmpID = request.EmployeeId,
    HandledRoleID = request.HandledRoleId
}, ct);
```

Do not add local transactions or signature writes: HIS success/failure is authoritative.

- [x] **Step 6: Run service tests and confirm GREEN**

Run: `dotnet test HealthExam.Tests/HealthExam.Tests.csproj --filter FullyQualifiedName~HisExamFormServiceTests`

Expected: PASS.

- [x] **Step 7: Commit orchestration and authorization**

```bash
git add HealthExam.Server/Service/HisExamFormService.cs HealthExam.Tests/HisExamFormServiceTests.cs
git commit -m "feat: authorize HIS form processes by exam record"
```

---

### Task 6: Expose the approved public endpoints and wire DI

**Files:**
- Modify: `HealthExam.Server/Service/DependencyInjection.cs`
- Create: `HealthExam.API/Controllers/HisFormController.cs`
- Create: `HealthExam.API/Controllers/ExamRecordHisFormController.cs`
- Create: `HealthExam.Tests/HisFormEndpointTests.cs`

**Interfaces:**
- Consumes: services from Tasks 4–5.
- Produces: the eight approved `/v1/his-forms` and `/v1/exam-records/{recordId}/his-form` routes.

- [x] **Step 1: Write failing route and action-contract tests**

Reflect controller attributes or use `WebApplicationFactory` to lock these routes:

```text
GET  /v1/his-forms/{templateCode}
GET  /v1/exam-records/{recordId}/his-form
GET  /v1/exam-records/{recordId}/his-form/processes
GET  /v1/exam-records/{recordId}/his-form/processes/{processId}
GET  /v1/exam-records/{recordId}/his-form/processes/{processId}/sign-workflow
POST /v1/exam-records/{recordId}/his-form/processes/{processId}/sections/{sectionKey}/submit
POST /v1/exam-records/{recordId}/his-form/processes/{processId}/sections/{sectionKey}/sign
POST /v1/exam-records/{recordId}/his-form/processes/{processId}/sections/{sectionKey}/sign/cancel
```

Assert `processId` is constrained as GUID, `sectionKey` binds as string, all actions return `ResultData<T>`, and no public action/route contains `CUEMR`, template create/update/delete, form-value save, or batch-sign behavior.

- [x] **Step 2: Write failing DI tests**

Resolve `HisEmrOptions`, `HisEmrClient`, `HisFormDefinitionService`, `HisExamFormService`, `IMemoryCache`, and the named client from the real host. Assert timeout equals the configured/default HIS timeout and the primary handler has redirects disabled.

- [x] **Step 3: Run endpoint tests and confirm RED**

Run: `dotnet test HealthExam.Tests/HealthExam.Tests.csproj --filter FullyQualifiedName~HisFormEndpointTests`

Expected: compilation/route failure because controllers and registrations are absent.

- [x] **Step 4: Register services and a safe HTTP handler**

Add:

```csharp
services.AddMemoryCache();
services.AddSingleton(_ => HisEmrOptions.FromEnvironment());
services.AddScoped<HisFormDefinitionService>();
services.AddScoped<HisExamFormService>();
services.AddHttpClient<HisEmrClient>((sp, http) =>
{
    http.Timeout = sp.GetRequiredService<HisEmrOptions>().Timeout;
}).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
{
    AllowAutoRedirect = false
});
```

- [x] **Step 5: Implement thin controllers**

Controllers only bind parameters, call the service, and wrap success through `HealthExamControllerBase.Success`. For the three POST actions, replace a null body with `new HisSectionSubmitRequest(0, new JArray())`, `new HisSectionSignRequest(0, 0, 0)`, or `new HisSectionCancelRequest(0, "")` respectively so service validation returns the normal envelope. Add Vietnamese Swagger summary/description stating HIS ownership and phase-1 read-only form values.

`HisFormController.Get` must reject any template code other than `KSK-TREN18TUOI` with `4040`; this avoids turning phase 1 into an arbitrary HIS template proxy.

- [x] **Step 6: Run endpoint and authentication regressions**

Run: `dotnet test HealthExam.Tests/HealthExam.Tests.csproj --filter "FullyQualifiedName~HisFormEndpointTests|FullyQualifiedName~AuthGuardTests|FullyQualifiedName~SwaggerBusinessDocumentationTests"`

Expected: PASS.

- [x] **Step 7: Commit API endpoints and wiring**

```bash
git add HealthExam.Server/Service/DependencyInjection.cs HealthExam.API/Controllers/HisFormController.cs HealthExam.API/Controllers/ExamRecordHisFormController.cs HealthExam.Tests/HisFormEndpointTests.cs
git commit -m "feat: expose HIS-backed KSK form endpoints"
```

---

### Task 7: Document rollout and verify the whole branch

**Files:**
- Modify: `.env.example`
- Modify: `README.md`
- Test: all solution projects and migration model.

**Interfaces:**
- Produces: deployment instructions for enabling the HIS adapter after migration.
- Produces: final evidence that existing form-server routes remain untouched and phase 1 exposes no value/template mutation.

- [x] **Step 1: Add configuration and rollout documentation**

Document:

```dotenv
HIS_EMR_ENABLED=false
HIS_EMR_BASE_URL=https://his.example.internal
HIS_EMR_TIMEOUT_SECONDS=10
HIS_EMR_CREDENTIAL_HEADER=Authorization
HIS_EMR_DEFINITION_CACHE_SECONDS=300
```

State that the deployment order is migration -> deploy disabled -> configure non-production -> verify definition/process -> test one submit/sign/cancel with a real test employee -> enable clients. Explicitly state that form values remain read-only and each section is signed independently.

- [x] **Step 2: Verify forbidden calls and routes are absent**

Run:

```bash
rg -n "CUEMR|M03F10010|template.*(create|update|delete)|batch.*sign" \
  HealthExam.API/Controllers/HisFormController.cs \
  HealthExam.API/Controllers/ExamRecordHisFormController.cs \
  HealthExam.Server/Service/HisEmrClient.cs
```

Expected: no matches.

- [x] **Step 3: Build the complete solution**

Run: `dotnet build HealthExam.sln --no-restore`

Expected: exit 0 with no new warnings attributable to this change.

- [x] **Step 4: Run the complete automated test suite**

Run: `dotnet test HealthExam.sln --no-build`

Expected: all tests pass; no live HIS/form-server/PostgreSQL dependency is required for unit/HTTP contract tests.

- [x] **Step 5: Inspect final diff and migration**

Run:

```bash
git status --short
git diff --check HEAD~6..HEAD
git diff HEAD~6..HEAD -- HealthExam.Core/Migrations HealthExam.API/Controllers HealthExam.Server/Service
```

Confirm only the nullable admission link is persisted, every mutation revalidates process and section ownership, and authorization values never appear in log statements.

- [x] **Step 6: Commit documentation**

```bash
git add .env.example README.md
git commit -m "docs: document HIS KSK form integration rollout"
```

- [x] **Step 7: Record final verification evidence**

Run `git status --short --branch` and save the exact build/test counts in the handoff. Do not claim completion if any required test is skipped or failing.
