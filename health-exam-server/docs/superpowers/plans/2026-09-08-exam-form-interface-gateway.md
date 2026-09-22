# Exam Form Interface and HIS Gateway Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make all eight health-exam form operations depend on one provider-neutral interface whose default implementation is an HIS-backed gateway replaceable through dependency injection.

**Architecture:** Both form controllers consume `IExamFormService`. `HisExamFormGateway` implements the application port, owns record/form orchestration and definition caching, and calls `IHisEmrApi`; the typed `HisEmrHttpClient` implements that transport port and is the only component aware of HIS HTTP details.

**Tech Stack:** .NET 8, C# 12, ASP.NET Core controllers and dependency injection, `IHttpClientFactory`, EF Core 8, Newtonsoft.Json `JToken`, `IMemoryCache`, xUnit, `WebApplicationFactory`.

**Spec:** `docs/superpowers/specs/2026-09-08-exam-form-interface-gateway-design.md`

## Global Constraints

- Preserve all eight existing routes, HTTP methods, JSON property names, `ResultData<T>` envelopes, status codes, and `ErrorCode` values.
- Keep `KSK-TREN18TUOI` as the only supported case-insensitive active template code.
- Treat process/workflow/mutation `JToken` values as opaque compatibility payloads; do not normalize their contents in this refactor.
- Keep HIS as the default implementation and source of truth; do not modify `his-server` or the database schema.
- Select implementations only through DI registration; do not add a provider factory, runtime switch, reflection, or provider configuration.
- Do not fall back to another implementation when HIS fails.
- Forward the real inbound HIS credential only inside the HIS HTTP adapter; never expose it through `IExamFormService`, cache it, or log it.
- Cache only successful form-definition reads using the existing normalized base URL, division ID, template code, duration, and single-flight behavior.
- Preserve the current retry policy: one connection retry only for the same safe definition GETs; no automatic retry for medical-process reads or submit/sign/cancel mutations.
- Preserve tenant, `AdmissionID`, process ownership, section, and signer validations without broadening their behavior.
- Use test-first red/green cycles and keep each task in a separate commit.

## File Structure

- `HealthExam.Server/Models/ExamFormModels.cs`: provider-neutral application request and response types.
- `HealthExam.Server/Models/HisEmrModels.cs`: HIS-only wire request types; no controller-facing types.
- `HealthExam.Server/Service/IExamFormService.cs`: stable application port consumed by both controllers.
- `HealthExam.Server/Service/IHisEmrApi.cs`: internal HIS capability port consumed by the gateway.
- `HealthExam.Server/Service/HisEmrHttpClient.cs`: typed HTTP implementation of `IHisEmrApi`.
- `HealthExam.Server/Service/HisExamFormGateway.cs`: record-scoped orchestration, validation, and mutation mapping.
- `HealthExam.Server/Service/HisExamFormGateway.Definition.cs`: definition resolution, normalization, locking, and caching for the same partial gateway class.
- `HealthExam.Server/Service/DependencyInjection.cs`: interface-to-implementation bindings and typed-client setup.
- `HealthExam.API/Controllers/HisFormController.cs`: definition endpoint depending only on `IExamFormService`.
- `HealthExam.API/Controllers/ExamRecordHisFormController.cs`: seven record-scoped endpoints depending only on `IExamFormService`.
- `HealthExam.Tests/ExamFormContractTests.cs`: application-port signatures and JSON compatibility.
- `HealthExam.Tests/FakeHisEmrApi.cs`: reusable interface-based HIS fake for gateway tests.
- `HealthExam.Tests/HisEmrHttpClientTests.cs`: exact HIS HTTP transport regression tests.
- `HealthExam.Tests/HisExamFormGatewayDefinitionTests.cs`: definition resolution and cache tests.
- `HealthExam.Tests/HisExamFormGatewayRecordTests.cs`: record/process/section/signer tests.
- `HealthExam.Tests/HisFormEndpointTests.cs`: route, controller dependency, DI, and replacement acceptance tests.
- `README.md`: architecture note naming the application port and default gateway.

---

### Task 1: Add provider-neutral application contracts

**Files:**
- Create: `HealthExam.Server/Models/ExamFormModels.cs`
- Create: `HealthExam.Server/Service/IExamFormService.cs`
- Create: `HealthExam.Tests/ExamFormContractTests.cs`
- Keep unchanged temporarily: `HealthExam.Server/Models/HisEmrModels.cs`

**Interfaces:**
- Produces: `ExamFormDefinition`, `ExamRecordForm`, `FormSectionSubmitRequest`, `FormSectionSignRequest`, and `FormSectionCancelRequest` with the same property names and types as the current HIS-named API models.
- Produces: the exact `IExamFormService` signatures below for Tasks 3 and 4.
- Consumes: existing `Newtonsoft.Json.Linq.JToken` and `JArray` compatibility payloads.

- [ ] **Step 1: Write failing contract and JSON compatibility tests**

Create `HealthExam.Tests/ExamFormContractTests.cs` with reflection coverage for all eight methods and serialization comparisons against the current models:

```csharp
#nullable enable

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HealthExam.Server.Models;
using HealthExam.Server.Service;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace HealthExam.Tests;

public class ExamFormContractTests
{
    [Fact]
    public void Application_port_exposes_all_eight_operations()
    {
        var methods = typeof(IExamFormService).GetMethods().ToDictionary(x => x.Name);
        Assert.Equal(8, methods.Count);
        Assert.Equal(typeof(Task<ExamFormDefinition>), methods[nameof(IExamFormService.GetDefinitionAsync)].ReturnType);
        Assert.Equal(typeof(Task<ExamRecordForm>), methods[nameof(IExamFormService.GetFormAsync)].ReturnType);
        Assert.Equal(typeof(Task<JToken>), methods[nameof(IExamFormService.GetProcessesAsync)].ReturnType);
        Assert.Equal(typeof(Task<JToken>), methods[nameof(IExamFormService.GetProcessAsync)].ReturnType);
        Assert.Equal(typeof(Task<JToken>), methods[nameof(IExamFormService.GetSignWorkflowAsync)].ReturnType);
        Assert.Equal(typeof(Task<JToken>), methods[nameof(IExamFormService.SubmitSectionAsync)].ReturnType);
        Assert.Equal(typeof(Task<JToken>), methods[nameof(IExamFormService.SignSectionAsync)].ReturnType);
        Assert.Equal(typeof(Task<JToken>), methods[nameof(IExamFormService.CancelSectionAsync)].ReturnType);
        Assert.All(methods.Values, method =>
            Assert.Equal(typeof(CancellationToken), method.GetParameters().Last().ParameterType));
    }

    [Fact]
    public void Neutral_models_preserve_existing_json_contract()
    {
        var definition = new ExamFormDefinition
        {
            TemplateId = Guid.Parse("10000000-0000-0000-0000-000000000001"),
            TemplateCode = "KSK-TREN18TUOI",
            TemplateName = "Khám sức khỏe",
            FileDocTypeId = 101,
            VersionCode = "V1",
            IsDraft = false,
            Active = true,
            Details = JArray.Parse("[{\"OrderNo\":1}]"),
            Layout = JArray.Parse("[{\"ItemGroupID\":\"KSK_NOI\"}]")
        };

        var legacy = new HisFormDefinition
        {
            TemplateId = definition.TemplateId,
            TemplateCode = definition.TemplateCode,
            TemplateName = definition.TemplateName,
            FileDocTypeId = definition.FileDocTypeId,
            VersionCode = definition.VersionCode,
            IsDraft = definition.IsDraft,
            Active = definition.Active,
            Details = definition.Details,
            Layout = definition.Layout
        };

        Assert.True(JToken.DeepEquals(
            JToken.Parse(JsonConvert.SerializeObject(legacy)),
            JToken.Parse(JsonConvert.SerializeObject(definition))));

        Assert.True(JToken.DeepEquals(
            JToken.Parse(JsonConvert.SerializeObject(new HisSectionSignRequest(2, 4210, 9))),
            JToken.Parse(JsonConvert.SerializeObject(new FormSectionSignRequest(2, 4210, 9)))));
    }
}
```

- [ ] **Step 2: Run the focused test and verify RED**

Run:

```bash
dotnet test HealthExam.Tests/HealthExam.Tests.csproj \
  --filter FullyQualifiedName~ExamFormContractTests
```

Expected: compilation fails because the neutral types and `IExamFormService` do not exist.

- [ ] **Step 3: Add the neutral model file**

Create `HealthExam.Server/Models/ExamFormModels.cs`:

```csharp
#nullable enable

using System;
using Newtonsoft.Json.Linq;

namespace HealthExam.Server.Models;

public sealed class ExamFormDefinition
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

public sealed record ExamRecordForm(Guid RecordId, long? AdmissionId, ExamFormDefinition Definition);
public sealed record FormSectionSubmitRequest(int SWStep, JArray SignatoryFlows);
public sealed record FormSectionSignRequest(int SWStep, long EmployeeId, long HandledRoleId);
public sealed record FormSectionCancelRequest(int SWStep, string Reason);
```

Do not add JSON-renaming attributes: keeping the same CLR property names is what preserves the current serialized names.

- [ ] **Step 4: Add the application port with exact signatures**

Create `HealthExam.Server/Service/IExamFormService.cs`:

```csharp
#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;
using HealthExam.Server.Models;
using Newtonsoft.Json.Linq;

namespace HealthExam.Server.Service;

public interface IExamFormService
{
    Task<ExamFormDefinition> GetDefinitionAsync(string templateCode, CancellationToken ct = default);
    Task<ExamRecordForm> GetFormAsync(Guid recordId, CancellationToken ct = default);
    Task<JToken> GetProcessesAsync(Guid recordId, CancellationToken ct = default);
    Task<JToken> GetProcessAsync(Guid recordId, Guid processId, CancellationToken ct = default);
    Task<JToken> GetSignWorkflowAsync(Guid recordId, CancellationToken ct = default);
    Task<JToken> SubmitSectionAsync(Guid recordId, Guid processId, string sectionKey, FormSectionSubmitRequest request, CancellationToken ct = default);
    Task<JToken> SignSectionAsync(Guid recordId, Guid processId, string sectionKey, FormSectionSignRequest request, CancellationToken ct = default);
    Task<JToken> CancelSectionAsync(Guid recordId, Guid processId, string sectionKey, FormSectionCancelRequest request, CancellationToken ct = default);
}
```

`GetSignWorkflowAsync` intentionally retains the current service signature. The route's `processId` remains accepted by the controller but does not gain a new ownership check during this compatibility-only refactor.

- [ ] **Step 5: Run the focused contract tests and verify GREEN**

Run:

```bash
dotnet test HealthExam.Tests/HealthExam.Tests.csproj \
  --filter FullyQualifiedName~ExamFormContractTests
```

Expected: PASS with two tests. Existing HIS-named types remain temporarily so the compatibility comparison and old implementation continue compiling.

- [ ] **Step 6: Commit the application-contract slice**

```bash
git add HealthExam.Server/Models/ExamFormModels.cs \
  HealthExam.Server/Service/IExamFormService.cs \
  HealthExam.Tests/ExamFormContractTests.cs
git commit -m "refactor: define exam form application port"
```

---

### Task 2: Isolate HIS transport behind `IHisEmrApi`

**Files:**
- Create: `HealthExam.Server/Service/IHisEmrApi.cs`
- Rename: `HealthExam.Server/Service/HisEmrClient.cs` → `HealthExam.Server/Service/HisEmrHttpClient.cs`
- Modify: `HealthExam.Server/Service/HisFormDefinitionService.cs`
- Modify: `HealthExam.Server/Service/HisExamFormService.cs`
- Modify: `HealthExam.Server/Service/DependencyInjection.cs`
- Rename: `HealthExam.Tests/HisEmrClientTests.cs` → `HealthExam.Tests/HisEmrHttpClientTests.cs`
- Create: `HealthExam.Tests/FakeHisEmrApi.cs`
- Modify: `HealthExam.Tests/HisFormDefinitionTests.cs`
- Modify: `HealthExam.Tests/HisExamFormServiceTests.cs`
- Modify: `HealthExam.Tests/HisFormEndpointTests.cs`

**Interfaces:**
- Produces: `IHisEmrApi` with the ten existing HIS capability methods and exact signatures below.
- Produces: `HisEmrHttpClient : IHisEmrApi`, retaining all current HTTP behavior.
- Consumes: `HisEmrOptions`, `IHealthExamContext`, and the three existing HIS wire request types.
- Preserves temporarily: existing `HisFormDefinitionService` and `HisExamFormService`, now depending on `IHisEmrApi` so the task remains buildable before Task 3.

- [ ] **Step 1: Add a failing transport-boundary test**

At the beginning of the renamed `HisEmrHttpClientTests` class, replace the old class assertion with:

```csharp
[Fact]
public void Http_adapter_implements_his_transport_port()
{
    Assert.Contains(typeof(IHisEmrApi), typeof(HisEmrHttpClient).GetInterfaces());
    Assert.False(typeof(HisEmrHttpClient)
        .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
        .Any(x => x.IsVirtual && !x.IsFinal));
}
```

Add `using System.Reflection;` to this test file.

Update the test class name to `HisEmrHttpClientTests`, but leave construction references failing until the implementation step.

- [ ] **Step 2: Run the focused transport test and verify RED**

Run:

```bash
dotnet test HealthExam.Tests/HealthExam.Tests.csproj \
  --filter FullyQualifiedName~HisEmrHttpClientTests.Http_adapter_implements_his_transport_port
```

Expected: compilation fails because `IHisEmrApi` and `HisEmrHttpClient` do not exist.

- [ ] **Step 3: Define the HIS transport port**

Create `HealthExam.Server/Service/IHisEmrApi.cs`:

```csharp
#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;
using HealthExam.Server.Models;
using Newtonsoft.Json.Linq;

namespace HealthExam.Server.Service;

public interface IHisEmrApi
{
    Task<JToken> GetTemplateListAsync(CancellationToken ct = default);
    Task<JToken> GetTemplateAsync(Guid templateId, CancellationToken ct = default);
    Task<JToken> GetTemplateTreeAsync(Guid templateId, long? admissionId = null, CancellationToken ct = default);
    Task<JToken> GetTreeByFileDocTypeAsync(int fileDocTypeId, CancellationToken ct = default);
    Task<JToken> GetMedicalProcessesAsync(long admissionId, CancellationToken ct = default);
    Task<JToken> GetMedicalProcessAsync(Guid processId, CancellationToken ct = default);
    Task<JToken> GetSignWorkflowAsync(int fileDocTypeId, CancellationToken ct = default);
    Task<JToken> SubmitSectionAsync(HisSubmitWireRequest request, CancellationToken ct = default);
    Task<JToken> SignSectionAsync(HisSignWireRequest request, CancellationToken ct = default);
    Task<JToken> CancelSectionAsync(HisCancelWireRequest request, CancellationToken ct = default);
}
```

- [ ] **Step 4: Rename the concrete client and implement the port**

Use `git mv` for the source and test files. Rename the class, constructor, logger generic, test class, helper return types, and every construction site from `HisEmrClient` to `HisEmrHttpClient`. Change the class declaration to:

```csharp
public sealed class HisEmrHttpClient : IHisEmrApi
```

Remove `virtual` from all ten public methods. Do not change route constants, request construction, envelope parsing, status mapping, logging, or retry conditions.

- [ ] **Step 5: Add a reusable interface fake and move service tests off subclassing**

Create `HealthExam.Tests/FakeHisEmrApi.cs`:

```csharp
#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using HealthExam.Core.Models;
using HealthExam.Server.Models;
using HealthExam.Server.Service;
using Newtonsoft.Json.Linq;

namespace HealthExam.Tests;

internal sealed class FakeHisEmrApi : IHisEmrApi
{
    public int TemplateListCalls { get; private set; }
    public JArray Templates { get; set; } = new();
    public JToken Metadata { get; set; } = new JObject();
    public JToken Layout { get; set; } = new JArray();
    public JToken Processes { get; set; } = new JArray();
    public JToken ProcessDetail { get; set; } = new JObject();
    public JToken Workflow { get; set; } = new JArray();
    public HisSubmitWireRequest? LastSubmit { get; private set; }
    public HisSignWireRequest? LastSign { get; private set; }
    public HisCancelWireRequest? LastCancel { get; private set; }
    public bool FailTemplateList { get; set; }

    public Task<JToken> GetTemplateListAsync(CancellationToken ct = default)
    {
        TemplateListCalls++;
        if (FailTemplateList)
            throw new HealthExamException(ErrorCodes.HisBadGateway, "Downstream failure");
        return Task.FromResult<JToken>(Templates);
    }

    public Task<JToken> GetTemplateAsync(Guid templateId, CancellationToken ct = default) => Task.FromResult(Metadata);
    public Task<JToken> GetTemplateTreeAsync(Guid templateId, long? admissionId = null, CancellationToken ct = default) => Task.FromResult(Layout);
    public Task<JToken> GetTreeByFileDocTypeAsync(int fileDocTypeId, CancellationToken ct = default) => Task.FromResult<JToken>(new JArray());
    public Task<JToken> GetMedicalProcessesAsync(long admissionId, CancellationToken ct = default) => Task.FromResult(Processes);
    public Task<JToken> GetMedicalProcessAsync(Guid processId, CancellationToken ct = default) => Task.FromResult(ProcessDetail);
    public Task<JToken> GetSignWorkflowAsync(int fileDocTypeId, CancellationToken ct = default) => Task.FromResult(Workflow);

    public Task<JToken> SubmitSectionAsync(HisSubmitWireRequest request, CancellationToken ct = default)
    {
        LastSubmit = request;
        return Task.FromResult<JToken>(new JObject { ["ok"] = true });
    }

    public Task<JToken> SignSectionAsync(HisSignWireRequest request, CancellationToken ct = default)
    {
        LastSign = request;
        return Task.FromResult<JToken>(new JObject { ["ok"] = true });
    }

    public Task<JToken> CancelSectionAsync(HisCancelWireRequest request, CancellationToken ct = default)
    {
        LastCancel = request;
        return Task.FromResult<JToken>(new JObject { ["ok"] = true });
    }
}
```

Replace both nested `FakeHisClient : HisEmrClient` classes in
`HisFormDefinitionTests.cs` and `HisExamFormServiceTests.cs` with
`FakeHisEmrApi`. Keep every assertion and fixture value; extend the shared fake
with a counter or injected `HealthExamException` only when an existing test
requires that exact observation.

- [ ] **Step 6: Move temporary services and DI to the transport interface**

Change constructor fields and parameters in both existing services from the
concrete client to `IHisEmrApi`. Register the typed client and interface with:

```csharp
services.AddHttpClient<IHisEmrApi, HisEmrHttpClient>((sp, http) =>
{
    http.Timeout = sp.GetRequiredService<HisEmrOptions>().Timeout;
})
.ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
{
    AllowAutoRedirect = false
});
```

Update the temporary DI assertion to resolve `IHisEmrApi` and assert its runtime
type is `HisEmrHttpClient`. Obtain the named client with
`typeof(IHisEmrApi).Name`, which is the name used by the generic typed-client
registration, and keep the timeout assertion.

- [ ] **Step 7: Run transport and existing form regressions**

Run:

```bash
dotnet test HealthExam.Tests/HealthExam.Tests.csproj \
  --filter "FullyQualifiedName~HisEmrHttpClientTests|FullyQualifiedName~HisFormDefinitionTests|FullyQualifiedName~HisExamFormServiceTests|FullyQualifiedName~HisFormEndpointTests"
```

Expected: PASS. Confirm with:

```bash
rg -n "class Fake.*: HisEmr|new HisEmrHttpClient" HealthExam.Tests
```

Expected: no fake inherits an HIS client; direct construction appears only in HTTP adapter tests.

- [ ] **Step 8: Commit the transport-boundary slice**

```bash
git add HealthExam.Server/Service/IHisEmrApi.cs \
  HealthExam.Server/Service/HisEmrHttpClient.cs \
  HealthExam.Server/Service/HisFormDefinitionService.cs \
  HealthExam.Server/Service/HisExamFormService.cs \
  HealthExam.Server/Service/DependencyInjection.cs \
  HealthExam.Tests/FakeHisEmrApi.cs \
  HealthExam.Tests/HisEmrHttpClientTests.cs \
  HealthExam.Tests/HisFormDefinitionTests.cs \
  HealthExam.Tests/HisExamFormServiceTests.cs \
  HealthExam.Tests/HisFormEndpointTests.cs
git commit -m "refactor: isolate HIS EMR transport"
```

---

### Task 3: Implement the HIS-backed application gateway

**Files:**
- Create: `HealthExam.Server/Service/HisExamFormGateway.cs`
- Create: `HealthExam.Server/Service/HisExamFormGateway.Definition.cs`
- Rename: `HealthExam.Tests/HisFormDefinitionTests.cs` → `HealthExam.Tests/HisExamFormGatewayDefinitionTests.cs`
- Rename: `HealthExam.Tests/HisExamFormServiceTests.cs` → `HealthExam.Tests/HisExamFormGatewayRecordTests.cs`
- Keep temporarily: `HealthExam.Server/Service/HisFormDefinitionService.cs`
- Keep temporarily: `HealthExam.Server/Service/HisExamFormService.cs`

**Interfaces:**
- Consumes: `IExamFormService` from Task 1 and `IHisEmrApi` from Task 2.
- Produces: `HisExamFormGateway : IExamFormService` with the exact eight method signatures from Task 1.
- Produces: unchanged definition cache, tenant/admission/process/section/signer behavior using neutral application types.

- [ ] **Step 1: Rename the service tests and make them target the missing gateway**

Use `git mv` on both test files and rename their classes. Replace fixtures so
they construct one `HisExamFormGateway` directly:

```csharp
private static (HisExamFormGateway gateway, FakeHisEmrApi his, InMemoryTestDb db) CreateGateway()
{
    var db = new InMemoryTestDb();
    var options = new HisEmrOptions
    {
        Enabled = true,
        BaseUrl = "https://his.test",
        CredentialHeaderName = "Authorization",
        DefinitionCacheDuration = TimeSpan.FromSeconds(300)
    };
    var his = new FakeHisEmrApi();
    SeedActiveDefinition(his);
    var cache = new MemoryCache(new MemoryCacheOptions());

    var gateway = new HisExamFormGateway(
        db.Uow,
        db.Ctx,
        his,
        options,
        cache,
        NullLogger<HisExamFormGateway>.Instance);
    return (gateway, his, db);
}
```

Change expected public types to `ExamFormDefinition`, `ExamRecordForm`, and the
three neutral section request records. Preserve all existing assertions for
template normalization, cache isolation, record ownership, filtering, section
validation, signer validation, and exact HIS wire requests.

Add one explicit interface assertion:

```csharp
[Fact]
public void Gateway_implements_application_port()
    => Assert.Contains(typeof(IExamFormService), typeof(HisExamFormGateway).GetInterfaces());
```

- [ ] **Step 2: Run the renamed gateway tests and verify RED**

Run:

```bash
dotnet test HealthExam.Tests/HealthExam.Tests.csproj \
  --filter "FullyQualifiedName~HisExamFormGatewayDefinitionTests|FullyQualifiedName~HisExamFormGatewayRecordTests"
```

Expected: compilation fails because `HisExamFormGateway` does not exist.

- [ ] **Step 3: Add the gateway shell and record-scoped operations**

Create `HisExamFormGateway.cs` as a `public sealed partial class` implementing
`IExamFormService`. Use this constructor and shared fields:

```csharp
public sealed partial class HisExamFormGateway : IExamFormService
{
    public const string TargetTemplateCode = "KSK-TREN18TUOI";

    private readonly IUnitOfWork _uow;
    private readonly IHealthExamContext _context;
    private readonly IHisEmrApi _his;
    private readonly HisEmrOptions _options;
    private readonly IMemoryCache _cache;
    private readonly ILogger<HisExamFormGateway> _logger;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _definitionLocks = new(StringComparer.Ordinal);

    public HisExamFormGateway(
        IUnitOfWork uow,
        IHealthExamContext context,
        IHisEmrApi his,
        HisEmrOptions options,
        IMemoryCache cache,
        ILogger<HisExamFormGateway> logger)
    {
        _uow = uow;
        _context = context;
        _his = his;
        _options = options;
        _cache = cache;
        _logger = logger;
    }
}
```

Move the record-scoped logic from `HisExamFormService` into this class without
changing validation order or error messages. Make these mechanical type changes:

```csharp
HisFormDefinition       -> ExamFormDefinition
ExamRecordHisForm       -> ExamRecordForm
HisSectionSubmitRequest -> FormSectionSubmitRequest
HisSectionSignRequest   -> FormSectionSignRequest
HisSectionCancelRequest -> FormSectionCancelRequest
```

Replace calls to the old definition service with:

```csharp
await GetDefinitionAsync(TargetTemplateCode, ct)
```

Retain the existing private `RequireRecordAsync`, `RequireAdmission`,
`RequireOwnedProcessAsync`, `RequireSection`, and `RequireSigner` behavior.

- [ ] **Step 4: Move definition resolution and caching into the partial gateway**

Create `HisExamFormGateway.Definition.cs`. Move `GetAsync`,
`ResolveAndFetchAsync`, cache-key construction, double-checked cache access, and
the per-key semaphore logic from `HisFormDefinitionService`. Rename the public
entry point and return type:

```csharp
public async Task<ExamFormDefinition> GetDefinitionAsync(
    string templateCode,
    CancellationToken ct = default)
```

Construct `ExamFormDefinition` instead of `HisFormDefinition`. Preserve exact
template matching, error codes/messages, normalization, parallel metadata/tree
fetching, deep cloning, successful-result-only caching, and `finally` release of
the semaphore.

- [ ] **Step 5: Run gateway tests and verify GREEN**

Run:

```bash
dotnet test HealthExam.Tests/HealthExam.Tests.csproj \
  --filter "FullyQualifiedName~HisExamFormGatewayDefinitionTests|FullyQualifiedName~HisExamFormGatewayRecordTests|FullyQualifiedName~ExamFormContractTests"
```

Expected: PASS. Then run:

```bash
rg -n "HisFormDefinitionService|HisExamFormService" \
  HealthExam.Server/Service/HisExamFormGateway*.cs \
  HealthExam.Tests/HisExamFormGateway*Tests.cs
```

Expected: no match; the gateway and its tests do not delegate to superseded
concrete services.

- [ ] **Step 6: Commit the gateway slice**

```bash
git add HealthExam.Server/Service/HisExamFormGateway.cs \
  HealthExam.Server/Service/HisExamFormGateway.Definition.cs \
  HealthExam.Tests/HisExamFormGatewayDefinitionTests.cs \
  HealthExam.Tests/HisExamFormGatewayRecordTests.cs
git commit -m "refactor: implement HIS exam form gateway"
```

---

### Task 4: Switch controllers and DI, prove replaceability, and remove legacy types

**Files:**
- Modify: `HealthExam.API/Controllers/HisFormController.cs`
- Modify: `HealthExam.API/Controllers/ExamRecordHisFormController.cs`
- Modify: `HealthExam.Server/Service/DependencyInjection.cs`
- Modify: `HealthExam.Server/Models/HisEmrModels.cs`
- Modify: `HealthExam.Tests/ExamFormContractTests.cs`
- Modify: `HealthExam.Tests/HisFormEndpointTests.cs`
- Delete: `HealthExam.Server/Service/HisFormDefinitionService.cs`
- Delete: `HealthExam.Server/Service/HisExamFormService.cs`
- Modify: `README.md`

**Interfaces:**
- Consumes: `IExamFormService`, `HisExamFormGateway`, `IHisEmrApi`, and `HisEmrHttpClient` from Tasks 1–3.
- Produces: both controllers depending only on `IExamFormService`.
- Produces: default DI binding `IExamFormService -> HisExamFormGateway` and transport binding `IHisEmrApi -> HisEmrHttpClient`.
- Produces: a passing HTTP acceptance test that replaces only `IExamFormService`.

- [ ] **Step 1: Write failing controller-dependency and replacement tests**

In `HisFormEndpointTests.cs`, replace the concrete DI test and add:

```csharp
[Fact]
public void Form_controllers_depend_only_on_application_port()
{
    Assert.Equal(
        new[] { typeof(IExamFormService) },
        typeof(HisFormController).GetConstructors().Single().GetParameters().Select(x => x.ParameterType));
    Assert.Equal(
        new[] { typeof(IExamFormService) },
        typeof(ExamRecordHisFormController).GetConstructors().Single().GetParameters().Select(x => x.ParameterType));
}

[Fact]
public async Task Dependency_injection_resolves_default_form_and_transport_adapters()
{
    await using var scope = _host.Services.CreateAsyncScope();
    Assert.IsType<HisExamFormGateway>(scope.ServiceProvider.GetRequiredService<IExamFormService>());
    Assert.IsType<HisEmrHttpClient>(scope.ServiceProvider.GetRequiredService<IHisEmrApi>());
}
```

Add a nested fake and an HTTP replacement test:

```csharp
private sealed class ReplacementExamFormService : IExamFormService
{
    public int DefinitionCalls { get; private set; }

    public Task<ExamFormDefinition> GetDefinitionAsync(string templateCode, CancellationToken ct = default)
    {
        DefinitionCalls++;
        return Task.FromResult(new ExamFormDefinition
        {
            TemplateCode = templateCode,
            TemplateName = "Replacement provider",
            Active = true
        });
    }

    public Task<ExamRecordForm> GetFormAsync(Guid recordId, CancellationToken ct = default) =>
        Task.FromResult(new ExamRecordForm(recordId, null, new ExamFormDefinition()));
    public Task<JToken> GetProcessesAsync(Guid recordId, CancellationToken ct = default) => Task.FromResult<JToken>(new JArray());
    public Task<JToken> GetProcessAsync(Guid recordId, Guid processId, CancellationToken ct = default) => Task.FromResult<JToken>(new JObject());
    public Task<JToken> GetSignWorkflowAsync(Guid recordId, CancellationToken ct = default) => Task.FromResult<JToken>(new JArray());
    public Task<JToken> SubmitSectionAsync(Guid recordId, Guid processId, string sectionKey, FormSectionSubmitRequest request, CancellationToken ct = default) => Task.FromResult<JToken>(new JObject());
    public Task<JToken> SignSectionAsync(Guid recordId, Guid processId, string sectionKey, FormSectionSignRequest request, CancellationToken ct = default) => Task.FromResult<JToken>(new JObject());
    public Task<JToken> CancelSectionAsync(Guid recordId, Guid processId, string sectionKey, FormSectionCancelRequest request, CancellationToken ct = default) => Task.FromResult<JToken>(new JObject());
}

[Fact]
public async Task Replacing_only_application_port_changes_the_endpoint_provider()
{
    var replacement = new ReplacementExamFormService();
    await using var factory = _host.WithWebHostBuilder(builder =>
        builder.ConfigureTestServices(services =>
            services.Replace(ServiceDescriptor.Singleton<IExamFormService>(replacement))));

    using var client = factory.CreateClient();
    client.DefaultRequestHeaders.Add(TestHeaders.Division, "DEV");
    client.DefaultRequestHeaders.Authorization =
        new AuthenticationHeaderValue("Bearer", AuthTestHost.EmployeeToken());

    using var response = await client.GetAsync("/v1/his-forms/KSK-TREN18TUOI");
    response.EnsureSuccessStatusCode();
    var json = JObject.Parse(await response.Content.ReadAsStringAsync());
    Assert.Equal("Replacement provider", json["Data"]?["TemplateName"]?.ToString());
    Assert.Equal(1, replacement.DefinitionCalls);
}
```

Add the required test usings for `Microsoft.AspNetCore.TestHost`,
`Microsoft.Extensions.DependencyInjection.Extensions`,
`System.Net.Http.Headers`, `System.Threading`, and `Newtonsoft.Json.Linq`.

- [ ] **Step 2: Run the focused endpoint tests and verify RED**

Run:

```bash
dotnet test HealthExam.Tests/HealthExam.Tests.csproj \
  --filter "FullyQualifiedName~HisFormEndpointTests.Form_controllers_depend_only_on_application_port|FullyQualifiedName~HisFormEndpointTests.Dependency_injection_resolves_default_form_and_transport_adapters|FullyQualifiedName~HisFormEndpointTests.Replacing_only_application_port_changes_the_endpoint_provider"
```

Expected: controller dependency and default DI assertions fail because concrete
services are still wired.

- [ ] **Step 3: Switch both controllers to `IExamFormService` and neutral types**

Change the field and constructor parameter in each controller to
`IExamFormService`. Update controller-visible types and request defaults:

```csharp
HisFormDefinition       -> ExamFormDefinition
ExamRecordHisForm       -> ExamRecordForm
HisSectionSubmitRequest -> FormSectionSubmitRequest
HisSectionSignRequest   -> FormSectionSignRequest
HisSectionCancelRequest -> FormSectionCancelRequest
```

Call `GetDefinitionAsync` from `HisFormController`. Reference
`HisExamFormGateway.TargetTemplateCode` only for the current route validation
message and supported-code check; do not inject the gateway itself. Keep all
route attributes and Swagger text unchanged in this refactor.

- [ ] **Step 4: Replace high-level DI registrations**

Remove the registrations for `HisFormDefinitionService` and
`HisExamFormService`. Add:

```csharp
services.AddScoped<HisExamFormGateway>();
services.AddScoped<IExamFormService>(sp =>
    sp.GetRequiredService<HisExamFormGateway>());
```

Registering the concrete gateway as well keeps the DI acceptance assertion
possible without creating two scoped instances. Keep the generic typed-client
registration from Task 2 unchanged.

- [ ] **Step 5: Remove legacy services and public HIS-named API models**

Delete `HisFormDefinitionService.cs` and `HisExamFormService.cs`. From
`HisEmrModels.cs`, remove only these five controller-facing types:

```text
HisFormDefinition
ExamRecordHisForm
HisSectionSubmitRequest
HisSectionSignRequest
HisSectionCancelRequest
```

Retain `HisSubmitWireRequest`, `HisSignWireRequest`, and
`HisCancelWireRequest`, including their `JsonProperty` mappings.

Update `ExamFormContractTests.Neutral_models_preserve_existing_json_contract`
after legacy-type removal to assert exact property names directly:

```csharp
var json = JObject.FromObject(definition);
Assert.Equal(
    new[] { "TemplateId", "TemplateCode", "TemplateName", "FileDocTypeId", "VersionCode", "IsDraft", "Active", "Details", "Layout" },
    json.Properties().Select(x => x.Name));

var signJson = JObject.FromObject(new FormSectionSignRequest(2, 4210, 9));
Assert.Equal(2, signJson["SWStep"]!.Value<int>());
Assert.Equal(4210, signJson["EmployeeId"]!.Value<long>());
Assert.Equal(9, signJson["HandledRoleId"]!.Value<long>());
```

- [ ] **Step 6: Run endpoint, contract, gateway, and transport tests**

Run:

```bash
dotnet test HealthExam.Tests/HealthExam.Tests.csproj \
  --filter "FullyQualifiedName~ExamFormContractTests|FullyQualifiedName~HisFormEndpointTests|FullyQualifiedName~HisExamFormGateway|FullyQualifiedName~HisEmrHttpClientTests"
```

Expected: PASS, including the replacement-provider HTTP test.

- [ ] **Step 7: Update architecture documentation and scan for leaked dependencies**

In the README HIS integration section, add this concise statement:

```markdown
- **Cổng nghiệp vụ biểu mẫu**: Các controller chỉ phụ thuộc `IExamFormService`.
  Mặc định DI ánh xạ interface này tới `HisExamFormGateway`; gateway gọi HIS qua
  `IHisEmrApi`/`HisEmrHttpClient`. Implementation khác được kích hoạt bằng cách
  đổi đăng ký `IExamFormService`, không đổi controller hoặc HTTP contract.
```

Run:

```bash
rg -n "HisFormDefinitionService|HisExamFormService|HisFormDefinition|ExamRecordHisForm|HisSection(Submit|Sign|Cancel)Request" \
  HealthExam.API HealthExam.Server HealthExam.Tests
```

Expected: no matches. `HisSubmitWireRequest`, `HisSignWireRequest`, and
`HisCancelWireRequest` remain valid HIS transport types.

- [ ] **Step 8: Run the complete verification suite**

Run:

```bash
dotnet test HealthExamServer.sln
dotnet build HealthExamServer.sln --no-restore
git diff --check
```

Expected: all tests pass, build succeeds with zero errors, and `git diff --check`
prints nothing. If warnings already exist at the starting commit, confirm this
refactor introduces no new warning category or count.

- [ ] **Step 9: Commit the controller/DI cutover and cleanup**

```bash
git add HealthExam.API/Controllers/HisFormController.cs \
  HealthExam.API/Controllers/ExamRecordHisFormController.cs \
  HealthExam.Server/Models/HisEmrModels.cs \
  HealthExam.Server/Service/DependencyInjection.cs \
  HealthExam.Server/Service/HisFormDefinitionService.cs \
  HealthExam.Server/Service/HisExamFormService.cs \
  HealthExam.Tests/ExamFormContractTests.cs \
  HealthExam.Tests/HisFormEndpointTests.cs \
  README.md
git commit -m "refactor: route form APIs through application interface"
```

Use `git status --short` immediately after committing. Expected: clean worktree.
