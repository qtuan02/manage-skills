# Pragmatic Clean Architecture Remediation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Hoàn thiện các boundary còn thiếu của Health Exam Server theo Pragmatic Clean Architecture mà không thay đổi API contract, database schema hoặc observable behavior.

**Architecture:** Giữ nguyên bốn project hiện tại và sửa theo vertical slice. Domain khóa state/invariant bằng behavior thuần; Application dùng một concrete handler cho mỗi use case và chỉ giữ interface ở repository/integration boundary; Infrastructure sở hữu PostgreSQL/HTTP/vendor mapping; API chỉ biết Infrastructure tại `Program.cs`.

**Tech Stack:** .NET 8, ASP.NET Core 8, EF Core 8.0.11, Npgsql EF Core 8.0.11, PostgreSQL, Newtonsoft.Json 13.0.3, EPPlus 7.1.2, xUnit 2.9.2

**Spec:** `docs/superpowers/specs/2026-09-11-clean-architecture-remediation-design.md`

## Global Constraints

- Giữ nguyên route, HTTP method, headers, authentication, request/response JSON, HTTP status, numeric error code, payload, trace ID và Swagger contract.
- Giữ nguyên database schema, table/column names, enum numeric values, migration IDs và dữ liệu hiện hữu; không sinh migration mới cho remediation này.
- Giữ nguyên transaction boundary, PostgreSQL locking, sequence, savepoint, idempotency, retry/dead-letter và timestamp semantics.
- `HealthExam.Domain` không tham chiếu project/package khác; `HealthExam.Application` chỉ tham chiếu `HealthExam.Domain` và BCL/.NET.
- Không thêm mediator, generic repository, domain-event framework, transaction middleware hoặc logging abstraction cho core.
- Một use case có một concrete handler; không tạo `I*Handler` nếu không có nhiều implementation thực sự.
- Chỉ giữ interface cho repository, unit of work, audit/outbox, clock, cache và integration boundary cần dependency inversion.
- Không expose `IQueryable`, expression tree, ORM row, HTTP request, raw vendor JSON hoặc arbitrary header accessor qua Application boundary.
- API source ngoài `Program.cs` không được import Infrastructure.
- PostgreSQL integration tests dùng `HEALTHEXAM_TEST_DB`; thiếu biến phải fail thay vì skip.
- Không sửa hoặc commit các file root không liên quan: `node_modules/`, `package.json`, `pnpm-lock.yaml`.

---

## Target File Map

```text
HealthExam.Domain/Common/
  DomainFailure.cs                         # semantic domain failures/results
HealthExam.Domain/ExamSessions/
  ExamSession.cs                           # session state/invariants
HealthExam.Domain/ExamRecords/
  ExamRecord.cs                            # record state/webhook/reconcile behavior
HealthExam.Domain/Imports/
  ImportBatch.cs                           # import lifecycle
HealthExam.Domain/Paraclinical/
  ParaclinicalOrder.cs
  ParaclinicalOrderItem.cs                 # order/item transitions
HealthExam.Domain/Webhooks/
  WebhookInbox.cs                          # retry/requeue/stamp lifecycle
HealthExam.Domain/Integrations/
  IntegrationOutbox.cs                     # claim/retry/dead-letter lifecycle

HealthExam.Application/Common/
  IClock.cs                                # interface only
  DomainFailureMapper.cs                   # DomainResult -> ApplicationFailureCode
HealthExam.Application/<feature>/*.cs       # concrete handlers, no I*Handler
HealthExam.Application/Integrations/
  IFormServerClient.cs                     # single role-sized FormServer port
  IHisEmrClient.cs                         # operation-oriented HIS port
  IntegrationModels.cs                     # transport-neutral request/result models
HealthExam.Application/Imports/
  IExamWorkbookReader.cs                   # typed workbook outcome

HealthExam.Infrastructure/Common/
  SystemClock.cs                           # production IClock implementation
HealthExam.Infrastructure/Excel/
  ExamWorkbookReader.cs
  ExamImportSheet.cs                       # EPPlus parsing and typed failures
HealthExam.Infrastructure/Integrations/
  FormServer/*                             # route/header/JSON mapping
  HisEmr/*                                 # route/header/JSON mapping
  Ris/*                                    # vendor wire mapping
HealthExam.Infrastructure/Persistence/
  HealthExamDbContext.cs                   # private-setter/backing-field mapping
  Repositories/*                           # PostgreSQL semantics unchanged

HealthExam.API/Configuration/
  HisRequestOptions.cs                     # incoming credential-header name
  VendorCallbackOptions.cs                 # callback Basic credentials
HealthExam.API/Middlewares/
  ApplicationResultMapper.cs
  ExceptionMiddleware.cs
  VendorCallbackAuthMiddleware.cs
HealthExam.API/Controllers/*.cs             # concrete handler injection only
HealthExam.API/Extensions/
  ApplicationServiceExtensions.cs          # concrete handler registration

HealthExam.Tests/Architecture/
  DependencyRuleTests.cs                   # enforce all layer boundaries
HealthExam.Tests/Domain/*.cs                # transition matrices/invariants
HealthExam.Tests/Application/*.cs           # concrete handlers + fake ports
HealthExam.Tests/Infrastructure/*.cs        # PostgreSQL and adapter seams
HealthExam.Tests/API/*.cs                   # contract/composition/middleware
```

### Task 1: Replace one-implementation handler interfaces with concrete handlers

**Files:**
- Modify: `HealthExam.Application/Auth/GetCurrentUser.cs`, `Login.cs`, `Logout.cs`
- Modify: `HealthExam.Application/Catalogs/GetRegistrationOptions.cs`, `ListExamGroups.cs`, `ListExamPackages.cs`, `ListOrganizations.cs`, `ListProvinces.cs`, `ListServiceCategories.cs`, `ListServiceGroups.cs`, `ListServices.cs`, `ListWards.cs`
- Modify: `HealthExam.Application/ExamSessions/CloseExamSession.cs`, `CreateExamSession.cs`, `GetExamSession.cs`, `ListExamSessions.cs`, `ReopenExamSession.cs`, `UpdateExamSession.cs`
- Modify: `HealthExam.Application/ExamRecords/CancelExamRecord.cs`, `ConfirmExamRecord.cs`, `CreateExamRecord.cs`, `GetExamFormDraft.cs`, `GetExamRecord.cs`, `GetExamSessionProgress.cs`, `ListExamRecords.cs`, `UpdateExamRecord.cs`, `VerifyPortalCredentials.cs`
- Modify: `HealthExam.Application/Imports/CommitImport.cs`, `DiscardImport.cs`, `DownloadImportTemplate.cs`, `GetImport.cs`, `ListImportErrors.cs`, `UploadImport.cs`
- Modify: `HealthExam.Application/Paraclinical/CancelOrder.cs`, `ChangeOrderState.cs`, `CreateOrders.cs`, `CreateOrdersFromPackage.cs`, `DispatchOrder.cs`, `GetConclusionEligibility.cs`, `GetOrder.cs`, `GetRecordOrders.cs`, `ProcessOutboxBatch.cs`, `SignConclusion.cs`, `UpdateVendorStatus.cs`
- Modify: `HealthExam.Application/Webhooks/BackfillScanResults.cs`, `GetWebhookMetrics.cs`, `IngestWebhook.cs`, `ProcessWebhookBatch.cs`, `ReconcileProgress.cs`, `RequeueWebhooks.cs`
- Modify: `HealthExam.Application/His/CancelHisSectionSignature.cs`, `GetExamRecordHisForm.cs`, `GetHisFormDefinition.cs`, `GetHisProcess.cs`, `GetHisRecordSection.cs`, `GetSignWorkflow.cs`, `ListHisProcesses.cs`, `SignHisSection.cs`, `SubmitHisSection.cs`
- Modify: `HealthExam.Application/RegistrationForms/GetExamGroupRegistrationForms.cs`, `GetRegistrationForm.cs`, `ListAvailableExamGroups.cs`, `PreviewRegistrationFormPdf.cs`, `SaveRegistrationFormSection.cs`
- Modify: `HealthExam.API/Controllers/AuthController.cs`, `CatalogController.cs`, `ExamGroupController.cs`, `ExamImportController.cs`, `ExamRecordController.cs`, `ExamRecordHisFormController.cs`, `ExamRecordRegistrationFormController.cs`, `ExamSessionController.cs`, `HisFormController.cs`, `HooksController.cs`, `InternalController.cs`, `MasterDataController.cs`, `ParaclinicalOrderController.cs`, `ServiceCatalogController.cs`, `VendorIntegrationController.cs`
- Modify: `HealthExam.API/Extensions/ApplicationServiceExtensions.cs`
- Modify: `HealthExam.Infrastructure/BackgroundJobs/IntegrationOutboxWorker.cs`, `ProgressReconciliationWorker.cs`, `WebhookWorker.cs`
- Test: `HealthExam.Tests/Architecture/DependencyRuleTests.cs`
- Test: `HealthExam.Tests/API/CompositionRootTests.cs`, `HisAuthEndpointTests.cs`, `RegistrationFormEndpointTests.cs`
- Test: `HealthExam.Tests/Application/HisHandlerTests.cs`, `ImportHandlerTests.cs`, `WebhookHandlerTests.cs`
- Test: `HealthExam.Tests/ExamImportTests.cs`, `FormDraftTests.cs`, `HisExamFormServiceTests.cs`, `HisFormEndpointTests.cs`, `InMemoryTestDb.cs`, `OutboxWorkerLoopPostgresTests.cs`, `TestImportService.cs`, `TestParaclinicalAdapters.cs`

**Interfaces:**
- Consumes: các `HandleAsync(commandOrQuery, CancellationToken)` hiện hữu
- Produces: cùng public handler class và method signature, nhưng không còn `I*Handler`

- [ ] **Step 1: Add a failing architecture test for handler interfaces**

```csharp
[Fact]
public void Application_does_not_publish_one_implementation_handler_interfaces()
{
    var offenders = typeof(HealthExam.Application.Common.IUnitOfWork).Assembly
        .GetTypes()
        .Where(t => t.IsPublic && t.IsInterface && t.Name.EndsWith("Handler", StringComparison.Ordinal))
        .Select(t => t.FullName)
        .OrderBy(x => x)
        .ToArray();

    Assert.Empty(offenders);
}
```

- [ ] **Step 2: Run the architecture test and verify it fails**

Run: `DOTNET_ROLL_FORWARD=Major dotnet test HealthExam.Tests/HealthExam.Tests.csproj --filter "FullyQualifiedName~Application_does_not_publish_one_implementation_handler_interfaces"`

Expected: FAIL listing the current `I*Handler` interfaces.

- [ ] **Step 3: Remove handler interfaces and change consumers to concrete classes**

For every listed Application file, remove the interface declaration and the `: I...Handler` clause while preserving the handler class and `HandleAsync` signature. Replace constructor fields/parameters such as:

```csharp
private readonly GetHisFormDefinitionHandler _definitionHandler;

public ListHisProcessesHandler(
    IExamRecordRepository records,
    GetHisFormDefinitionHandler definitionHandler,
    IHisEmrClient client)
```

Controllers and workers must likewise receive `ConfirmExamRecordHandler`, `ProcessWebhookBatchHandler`, and other concrete handler types.

- [ ] **Step 4: Register concrete handlers**

Replace two-type registrations with one-type registrations:

```csharp
services.AddScoped<ConfirmExamRecordHandler>();
services.AddScoped<CommitImportHandler>();
services.AddScoped<ProcessWebhookBatchHandler>();
```

Apply the same form to every handler currently registered in `ApplicationServiceExtensions`.

- [ ] **Step 5: Update composition-root tests and run the non-PostgreSQL suite**

Run: `DOTNET_ROLL_FORWARD=Major dotnet test HealthExam.Tests/HealthExam.Tests.csproj --filter "FullyQualifiedName~HealthExam.Tests.Architecture|FullyQualifiedName~HealthExam.Tests.Application|FullyQualifiedName~HealthExam.Tests.API"`

Expected: PASS and every concrete handler resolves from the service provider.

- [ ] **Step 6: Commit**

```bash
git add HealthExam.Application HealthExam.API/Controllers HealthExam.API/Extensions/ApplicationServiceExtensions.cs HealthExam.Infrastructure/BackgroundJobs HealthExam.Tests/Architecture HealthExam.Tests/API/CompositionRootTests.cs
git commit -m "refactor: use concrete application handlers"
```

### Task 2: Remove request transport and system-clock implementations from Application

**Files:**
- Modify: `HealthExam.Application/Common/IHealthExamContext.cs`
- Modify: `HealthExam.Application/Common/IClock.cs`
- Create: `HealthExam.Infrastructure/Common/SystemClock.cs`
- Modify: `HealthExam.Infrastructure/DependencyInjection.cs`
- Modify: `HealthExam.Infrastructure/Integrations/FormServer/FormServerClient.cs`
- Modify: `HealthExam.Infrastructure/Integrations/HisEmr/HisEmrClient.cs`
- Create: `HealthExam.API/Configuration/HisRequestOptions.cs`
- Create: `HealthExam.API/Configuration/VendorCallbackOptions.cs`
- Modify: `HealthExam.API/Program.cs`
- Modify: `HealthExam.API/Extensions/ApplicationServiceExtensions.cs`
- Modify: `HealthExam.API/Middlewares/VendorCallbackAuthMiddleware.cs`
- Modify: `HealthExam.API/Controllers/ExamGroupController.cs`, `ExamRecordHisFormController.cs`, `ExamRecordRegistrationFormController.cs`, `HisFormController.cs`
- Modify: `HealthExam.Application/ExamRecords/CancelExamRecord.cs`, `ConfirmExamRecord.cs`, `CreateExamRecord.cs`, `UpdateExamRecord.cs`
- Modify: `HealthExam.Application/ExamSessions/CloseExamSession.cs`, `CreateExamSession.cs`, `ReopenExamSession.cs`, `UpdateExamSession.cs`
- Modify: `HealthExam.Application/Imports/UploadImport.cs`
- Modify: `HealthExam.Application/Paraclinical/CancelOrder.cs`, `ChangeOrderState.cs`, `CreateOrders.cs`, `CreateOrdersFromPackage.cs`
- Modify: `HealthExam.Application/RegistrationForms/SaveRegistrationFormSection.cs`
- Modify: `HealthExam.Application/Webhooks/IngestWebhook.cs`
- Test: `HealthExam.Tests/Architecture/DependencyRuleTests.cs`
- Test: `HealthExam.Tests/API/CompositionRootTests.cs`, `ApiContractSurfaceTests.cs`
- Test: `HealthExam.Tests/Application/ExamRecordHandlerTests.cs`, `ImportHandlerTests.cs`, `RegistrationFormHandlerTests.cs`, `WebhookHandlerTests.cs`
- Test: `HealthExam.Tests/FakeHealthExamContext.cs`, `FormBindingRegressionTests.cs`, `FormDraftTests.cs`, `HisEmrConfigurationTests.cs`, `InMemoryTestDb.cs`, `TestImportService.cs`

**Interfaces:**
- Consumes: `IClock.UtcNow`, credential and trace already present on commands/queries
- Produces: `HealthExam.Infrastructure.Common.SystemClock`, API-owned request options, context without arbitrary header access

- [ ] **Step 1: Add failing boundary tests**

```csharp
[Fact]
public void Application_context_does_not_expose_request_headers()
{
    Assert.DoesNotContain(
        typeof(IHealthExamContext).GetMethods(),
        m => m.Name == "GetRequestHeader");
}

[Fact]
public void Application_contains_no_clock_implementation()
{
    var concreteClocks = typeof(IClock).Assembly.GetTypes()
        .Where(t => typeof(IClock).IsAssignableFrom(t) && t.IsClass && !t.IsAbstract);
    Assert.Empty(concreteClocks);
}
```

Add a source test that scans `HealthExam.API/**/*.cs` except `Program.cs` and asserts the text `HealthExam.Infrastructure` is absent.

- [ ] **Step 2: Run the new tests and verify all three fail**

Run: `DOTNET_ROLL_FORWARD=Major dotnet test HealthExam.Tests/HealthExam.Tests.csproj --filter "FullyQualifiedName~Application_context_does_not_expose_request_headers|FullyQualifiedName~Application_contains_no_clock_implementation|FullyQualifiedName~Api_sources_outside_program_do_not_reference_infrastructure"`

Expected: FAIL on `GetRequestHeader`, `SystemClock`, and `VendorCallbackAuthMiddleware`.

- [ ] **Step 3: Move the clock implementation outward and make time mandatory**

Keep only this in Application:

```csharp
public interface IClock
{
    DateTime UtcNow { get; }
}
```

Create in Infrastructure:

```csharp
namespace HealthExam.Infrastructure.Common;

public sealed class SystemClock : IClock
{
    public DateTime UtcNow => DateTime.UtcNow;
}
```

Register `services.AddSingleton<IClock, SystemClock>();` in Infrastructure. Remove `clock ?? new SystemClock()` fallbacks and inject `IClock` into every Application handler that currently reads `DateTime.UtcNow`.

- [ ] **Step 4: Remove arbitrary request-header access**

Delete `GetRequestHeader` from `IHealthExamContext`. Remove the fallback reads in FormServer/HIS clients; their public operation calls must receive credential/metadata explicitly. Update fakes so they no longer implement the removed method.

- [ ] **Step 5: Add API-owned request options**

```csharp
public sealed record HisRequestOptions(string CredentialHeaderName)
{
    public static HisRequestOptions FromEnvironment()
        => new(Environment.GetEnvironmentVariable("HIS_EMR_CREDENTIAL_HEADER")?.Trim() is { Length: > 0 } value
            ? value
            : "Authorization");
}

public sealed record VendorCallbackOptions(string User, string Password)
{
    public bool IsConfigured => !string.IsNullOrWhiteSpace(User) && !string.IsNullOrWhiteSpace(Password);
    public static VendorCallbackOptions FromEnvironment() => new(
        Environment.GetEnvironmentVariable("RIS_CALLBACK_USER")?.Trim() ?? "",
        Environment.GetEnvironmentVariable("RIS_CALLBACK_PASSWORD") ?? "");
}
```

Register both in `Program.cs`. Controllers use `HisRequestOptions`; vendor middleware uses `VendorCallbackOptions`. Infrastructure `HisEmrOptions` keeps only outbound HIS configuration and department settings until Task 9 removes the Application-facing options interface.

- [ ] **Step 6: Run deterministic clock and API middleware tests**

Run: `DOTNET_ROLL_FORWARD=Major dotnet test HealthExam.Tests/HealthExam.Tests.csproj --filter "FullyQualifiedName~HealthExam.Tests.Application|FullyQualifiedName~HealthExam.Tests.API|FullyQualifiedName~HisEmrConfigurationTests"`

Expected: PASS with fake clock timestamps unchanged and vendor callback status/error codes unchanged.

- [ ] **Step 7: Commit**

```bash
git add HealthExam.Application HealthExam.Infrastructure/Common HealthExam.Infrastructure/DependencyInjection.cs HealthExam.Infrastructure/Integrations HealthExam.API/Configuration HealthExam.API/Program.cs HealthExam.API/Middlewares/VendorCallbackAuthMiddleware.cs HealthExam.API/Controllers HealthExam.Tests
git commit -m "refactor: isolate request context and system clock"
```

### Task 3: Make ExamSession protect its state and availability rules

**Files:**
- Modify: `HealthExam.Domain/ExamSessions/ExamSession.cs`
- Modify: `HealthExam.Application/ExamSessions/CloseExamSession.cs`, `ReopenExamSession.cs`, `UpdateExamSession.cs`
- Modify: `HealthExam.Application/ExamRecords/CreateExamRecord.cs`, `UpdateExamRecord.cs`, `ConfirmExamRecord.cs`, `CancelExamRecord.cs`, `GetExamFormDraft.cs`
- Modify: `HealthExam.Application/Imports/UploadImport.cs`, `CommitImport.cs`
- Modify: `HealthExam.Application/Paraclinical/CreateOrders.cs`, `CreateOrdersFromPackage.cs`, `ChangeOrderState.cs`, `CancelOrder.cs`
- Create: `HealthExam.Application/Common/DomainFailureMapper.cs`
- Modify: `HealthExam.Infrastructure/Persistence/HealthExamDbContext.cs`
- Test: `HealthExam.Tests/Domain/ExamSessionDomainTests.cs`
- Test: `HealthExam.Tests/Application/ExamSessionHandlerTests.cs`, `ExamRecordHandlerTests.cs`, `ImportHandlerTests.cs`, `ParaclinicalHandlerTests.cs`
- Test: `HealthExam.Tests/Infrastructure/PersistenceModelTests.cs`

**Interfaces:**
- Produces: `ExamSession.State { get; private set; }`, `DomainResult EnsureAcceptsChanges()`, existing `Close()` and `Reopen(reason)`, `DomainFailureMapper.Map(DomainFailureCode)`

- [ ] **Step 1: Add the complete session transition matrix tests**

Add cases asserting: Draft/Open/InProgress can close; Closed→Close rejects; Cancelled→Close rejects; Closed→Reopen with reason applies; all other Reopen sources reject; blank reopen reason rejects; Closed/Cancelled reject `EnsureAcceptsChanges`; Draft/Open/InProgress accept it.

```csharp
[Theory]
[InlineData(ExamSessionState.Closed)]
[InlineData(ExamSessionState.Cancelled)]
public void EnsureAcceptsChanges_rejects_terminal_sessions(ExamSessionState state)
{
    var session = new ExamSession { SessionCode = "S-001" };
    typeof(ExamSession).GetProperty(nameof(ExamSession.State))!.SetValue(session, state);
    var result = session.EnsureAcceptsChanges();
    Assert.False(result.IsSuccess);
    Assert.Equal(DomainFailureCode.InvalidState, result.Failure.Code);
}
```

- [ ] **Step 2: Run Domain tests and verify the new availability tests fail**

Run: `DOTNET_ROLL_FORWARD=Major dotnet test HealthExam.Tests/HealthExam.Tests.csproj --filter FullyQualifiedName~ExamSessionDomainTests`

Expected: FAIL because `EnsureAcceptsChanges` and protected setter do not exist.

- [ ] **Step 3: Protect session state and centralize the rule**

```csharp
public ExamSessionState State { get; private set; } = ExamSessionState.Draft;

public DomainResult EnsureAcceptsChanges()
    => State is ExamSessionState.Closed or ExamSessionState.Cancelled
        ? DomainResult.Reject(DomainFailureCode.InvalidState,
            $"Đợt khám {SessionCode} đã đóng, cần mở lại đợt trước khi thao tác")
        : DomainResult.NoOp();
```

Replace duplicated Application checks with this method and map its failure through the shared mapper. Preserve current messages/error codes at the Application/API boundary.

Create the initial shared mapper used by all later slices:

```csharp
public static class DomainFailureMapper
{
    public static ApplicationFailureCode Map(DomainFailureCode code) => code switch
    {
        DomainFailureCode.RequiredValue => ApplicationFailureCode.BadRequest,
        DomainFailureCode.Conflict => ApplicationFailureCode.InvalidState,
        DomainFailureCode.InvalidState => ApplicationFailureCode.InvalidState,
        DomainFailureCode.InvalidTransition => ApplicationFailureCode.InvalidState,
        _ => ApplicationFailureCode.InvalidState
    };
}
```

- [ ] **Step 4: Verify EF materialization and slice behavior**

Run: `DOTNET_ROLL_FORWARD=Major dotnet test HealthExam.Tests/HealthExam.Tests.csproj --filter "FullyQualifiedName~ExamSessionDomainTests|FullyQualifiedName~ExamSessionHandlerTests|FullyQualifiedName~PersistenceModelTests"`

Expected: PASS without a migration/model snapshot diff.

- [ ] **Step 5: Commit**

```bash
git add HealthExam.Domain/ExamSessions HealthExam.Application/ExamSessions HealthExam.Application/ExamRecords HealthExam.Application/Imports HealthExam.Application/Paraclinical HealthExam.Infrastructure/Persistence/HealthExamDbContext.cs HealthExam.Tests
git commit -m "refactor: protect exam session rules in domain"
```

### Task 4: Move ExamRecord webhook and reconciliation transitions into Domain

**Files:**
- Modify: `HealthExam.Domain/ExamRecords/ExamRecord.cs`
- Modify: `HealthExam.Application/ExamRecords/ConfirmExamRecord.cs`, `CancelExamRecord.cs`
- Modify: `HealthExam.Application/Webhooks/ProcessWebhookBatch.cs`, `ReconcileProgress.cs`
- Modify: `HealthExam.Infrastructure/Persistence/HealthExamDbContext.cs`
- Test: `HealthExam.Tests/Domain/ExamRecordDomainTests.cs`
- Test: `HealthExam.Tests/Application/ExamRecordHandlerTests.cs`, `WebhookHandlerTests.cs`
- Test: `HealthExam.Tests/WebhookProcessorTests.cs`, `WebhookIngestTests.cs`
- Test: `HealthExam.Tests/Infrastructure/PersistenceModelTests.cs`

**Interfaces:**
- Produces: `ExamRecord.State { get; private set; }`, `ApplyWebhookTransition(...)`, `ApplyReconciledProgress(...)`

- [ ] **Step 1: Add failing Domain tests for the full record transition behavior**

Pin these cases: target already reached is NoOp; wrong source rejects; Waiting→InProgress applies; InProgress→Completed applies; late real event replaces estimated timestamp; late/older event does not move state backward; reconciliation may infer Waiting→InProgress and InProgress→Completed but cannot override terminal cancellation; Confirm and Cancel stay idempotent/rejected exactly as current contract expects.

```csharp
var result = record.ApplyWebhookTransition(
    ExamRecordState.Waiting,
    ExamRecordState.InProgress,
    occurredAt,
    now);

Assert.True(result.Applied);
Assert.Equal(ExamRecordState.InProgress, record.State);
Assert.Equal(occurredAt, record.ExamStartedAt);
Assert.False(record.ExamStartedAtEstimated);
```

- [ ] **Step 2: Run tests and verify failure on missing Domain operations**

Run: `DOTNET_ROLL_FORWARD=Major dotnet test HealthExam.Tests/HealthExam.Tests.csproj --filter FullyQualifiedName~ExamRecordDomainTests`

Expected: FAIL because webhook/reconciliation transition methods do not exist.

- [ ] **Step 3: Implement protected state and domain operations**

Use a transport-neutral request:

```csharp
public readonly record struct ExamRecordTransition(
    ExamRecordState Expected,
    ExamRecordState Target,
    DateTime OccurredAt,
    DateTime AppliedAt,
    bool Estimated = false);
```

`ApplyWebhookTransition` owns expected/target/idempotency/timestamp rules. `ApplyReconciledProgress` owns inference and estimated timestamp flags. Neither method writes audit or knows webhook JSON.

- [ ] **Step 4: Reduce handlers to orchestration**

Delete `record.State = ...` and the transition table from `ProcessWebhookBatch`/`ReconcileProgress`. Handlers call the Domain operation, then create audit entries only when the returned outcome is Applied or when the current compatibility behavior requires an idempotent data fill.

- [ ] **Step 5: Run Domain, Application and webhook regression tests**

Run: `DOTNET_ROLL_FORWARD=Major dotnet test HealthExam.Tests/HealthExam.Tests.csproj --filter "FullyQualifiedName~ExamRecordDomainTests|FullyQualifiedName~ExamRecordHandlerTests|FullyQualifiedName~WebhookHandlerTests|FullyQualifiedName~WebhookProcessorTests"`

Expected: PASS with existing skip/error messages and timestamps unchanged.

- [ ] **Step 6: Commit**

```bash
git add HealthExam.Domain/ExamRecords HealthExam.Application/ExamRecords HealthExam.Application/Webhooks HealthExam.Infrastructure/Persistence/HealthExamDbContext.cs HealthExam.Tests
git commit -m "refactor: move exam record transitions into domain"
```

### Task 5: Make ImportBatch lifecycle authoritative and return typed workbook failures

**Files:**
- Modify: `HealthExam.Domain/Imports/ImportBatch.cs`
- Modify: `HealthExam.Application/Imports/IExamWorkbookReader.cs`, `UploadImport.cs`, `CommitImport.cs`, `DiscardImport.cs`
- Modify: `HealthExam.Infrastructure/Excel/ExamWorkbookReader.cs`, `ExamImportSheet.cs`
- Modify: `HealthExam.Infrastructure/Persistence/Repositories/ImportRepository.cs`
- Modify: `HealthExam.Infrastructure/Persistence/HealthExamDbContext.cs`
- Test: `HealthExam.Tests/Domain/ImportBatchDomainTests.cs`
- Test: `HealthExam.Tests/Application/ImportHandlerTests.cs`
- Test: `HealthExam.Tests/Infrastructure/ImportPersistenceTests.cs`
- Test: `HealthExam.Tests/ExamImportTests.cs`

**Interfaces:**
- Produces: protected `ImportBatch.State`; authoritative `BeginCommit`, `Complete`, `Discard`; `WorkbookReadResult`

- [ ] **Step 1: Add failing tests for protected state and typed workbook failure**

```csharp
public sealed record WorkbookReadFailure(string Message, IReadOnlyList<ImportValidationError> Errors);
public sealed record WorkbookReadResult(ParsedImportSheet Value, WorkbookReadFailure Failure)
{
    public bool IsSuccess => Failure is null;
}
```

Test Pending→Committing, stale Committing reclaim, active Committing reject, Completed BeginCommit NoOp, Discarded reject, Complete idempotency, Discard rules, expiry, and malformed/missing-sheet/missing-header workbook outcomes.

- [ ] **Step 2: Run import tests and verify they fail on the new contract**

Run: `DOTNET_ROLL_FORWARD=Major dotnet test HealthExam.Tests/HealthExam.Tests.csproj --filter "FullyQualifiedName~ImportBatchDomainTests|FullyQualifiedName~ImportHandlerTests"`

Expected: FAIL because state is public and workbook reader throws `HealthExamException`.

- [ ] **Step 3: Protect lifecycle and remove handler assignments**

Set `State` to private setter. In `CommitImportHandler`, replace `batch.State = Committing` with `batch.BeginCommit(_clock.UtcNow)`. Keep PostgreSQL `TryClaimForCommitAsync` as the concurrency port; after a successful DB claim, call the domain behavior to synchronize the tracked aggregate.

- [ ] **Step 4: Convert EPPlus errors to typed outcomes**

Change `IExamWorkbookReader.ReadAsync` to return `Task<WorkbookReadResult>`. `ExamWorkbookReader` catches EPPlus/file-shape errors and returns `WorkbookReadFailure`; `UploadImportHandler` maps it to `ApplicationFailureCode.FileInvalid` with the same message/payload. Do not expose EPPlus exceptions or numeric error codes.

- [ ] **Step 5: Run import and PostgreSQL persistence tests**

Run unit tests: `DOTNET_ROLL_FORWARD=Major dotnet test HealthExam.Tests/HealthExam.Tests.csproj --filter "FullyQualifiedName~ImportBatchDomainTests|FullyQualifiedName~ImportHandlerTests|FullyQualifiedName~ExamImportTests"`

Run PostgreSQL: `HEALTHEXAM_TEST_DB="$HEALTHEXAM_TEST_DB" DOTNET_ROLL_FORWARD=Major dotnet test HealthExam.Tests/HealthExam.Tests.csproj --filter FullyQualifiedName~ImportPersistenceTests`

Expected: PASS, including savepoint, unique conflict, stale claim and rollback behavior.

- [ ] **Step 6: Commit**

```bash
git add HealthExam.Domain/Imports HealthExam.Application/Imports HealthExam.Infrastructure/Excel HealthExam.Infrastructure/Persistence/Repositories/ImportRepository.cs HealthExam.Infrastructure/Persistence/HealthExamDbContext.cs HealthExam.Tests
git commit -m "refactor: enforce import lifecycle in domain"
```

### Task 6: Protect paraclinical order and item transitions

**Files:**
- Modify: `HealthExam.Domain/Paraclinical/ParaclinicalOrder.cs`, `ParaclinicalOrderItem.cs`
- Modify: `HealthExam.Application/Paraclinical/CreateOrders.cs`, `CreateOrdersFromPackage.cs`, `ChangeOrderState.cs`, `CancelOrder.cs`, `UpdateVendorStatus.cs`, `SignConclusion.cs`
- Modify: `HealthExam.Infrastructure/Persistence/HealthExamDbContext.cs`
- Test: `HealthExam.Tests/Domain/ParaclinicalOrderDomainTests.cs`
- Test: `HealthExam.Tests/Application/ParaclinicalHandlerTests.cs`
- Test: `HealthExam.Tests/ConclusionEligibilityTests.cs`, `ParaclinicalScanResultTests.cs`
- Test: `HealthExam.Tests/Infrastructure/PersistenceModelTests.cs`

**Interfaces:**
- Produces: protected item/order state, existing `TransitionTo` and `TryCancel` changed to semantic `DomainResult`/typed transition without public bypass

- [ ] **Step 1: Complete the item transition matrix tests**

Cover Ordered/Waiting/InProgress/Done/Cancelled for Manual/Internal/Vendor sources; repeated target NoOp; vendor cannot move backward or report Done; Done cannot cancel; Cancelled is terminal; transition away from Done clears result fields only where current behavior permits; conclusion eligibility ignores Cancelled and requires all live items Done.

- [ ] **Step 2: Run tests and confirm public setter guard fails**

Add reflection assertion `Assert.False(typeof(ParaclinicalOrderItem).GetProperty("State")!.SetMethod!.IsPublic)` and run `ParaclinicalOrderDomainTests`; expect FAIL.

- [ ] **Step 3: Protect state and make cancellation return a semantic result**

```csharp
public ParaclinicalItemState State { get; private set; } = ParaclinicalItemState.Ordered;

public DomainResult Cancel(string reason, DateTime now)
{
    if (State == ParaclinicalItemState.Cancelled) return DomainResult.NoOp();
    if (State == ParaclinicalItemState.Done)
        return DomainResult.Reject(DomainFailureCode.InvalidTransition, "Dịch vụ đã có kết quả");
    if (string.IsNullOrWhiteSpace(reason))
        return DomainResult.Reject(DomainFailureCode.RequiredValue, "Hủy dịch vụ phải có lý do");
    State = ParaclinicalItemState.Cancelled;
    CancelledAt = now;
    CancelReason = reason.Trim();
    ModifiedDate = now;
    return DomainResult.Apply();
}
```

- [ ] **Step 4: Replace handler state/precondition duplication with domain calls**

Handlers keep authorization, repository lookup, vendor payload creation, audit/outbox and transaction orchestration. They do not decide the transition or mutate protected state.

- [ ] **Step 5: Run paraclinical tests**

Run: `DOTNET_ROLL_FORWARD=Major dotnet test HealthExam.Tests/HealthExam.Tests.csproj --filter "FullyQualifiedName~ParaclinicalOrderDomainTests|FullyQualifiedName~ParaclinicalHandlerTests|FullyQualifiedName~ConclusionEligibilityTests|FullyQualifiedName~ParaclinicalScanResultTests"`

Expected: PASS with error codes 4090/4094/4221 and outbox dedup behavior unchanged.

- [ ] **Step 6: Commit**

```bash
git add HealthExam.Domain/Paraclinical HealthExam.Application/Paraclinical HealthExam.Infrastructure/Persistence/HealthExamDbContext.cs HealthExam.Tests
git commit -m "refactor: protect paraclinical state transitions"
```

### Task 7: Make webhook inbox and integration outbox lifecycle non-bypassable

**Files:**
- Modify: `HealthExam.Domain/Webhooks/WebhookInbox.cs`
- Modify: `HealthExam.Domain/Integrations/IntegrationOutbox.cs`
- Modify: `HealthExam.Application/Webhooks/BackfillScanResults.cs`, `ProcessWebhookBatch.cs`, `RequeueWebhooks.cs`
- Modify: `HealthExam.Application/Paraclinical/ProcessOutboxBatch.cs`
- Modify: `HealthExam.Infrastructure/Persistence/Repositories/WebhookInboxRepository.cs`, `IntegrationOutboxRepository.cs`
- Modify: `HealthExam.Infrastructure/Persistence/HealthExamDbContext.cs`
- Test: `HealthExam.Tests/Application/WebhookHandlerTests.cs`, `OutboxHandlerTests.cs`
- Test: `HealthExam.Tests/Infrastructure/WebhookQueueIntegrationTests.cs`, `OutboxQueueIntegrationTests.cs`
- Test: `HealthExam.Tests/WebhookProcessorTests.cs`, `OutboxDeadLetterTests.cs`, `OutboxInvariantPinTests.cs`, `OutboxQueuePostgresTests.cs`

**Interfaces:**
- Produces: `WebhookInbox.Requeue(now)`, protected `ProcessState`; protected `IntegrationOutbox.State`; authoritative claim/stamp/dead-letter behavior

- [ ] **Step 1: Add lifecycle matrix tests**

Pin New/Failed/Processed/Skipped/dead-letter claims, exponential backoff, MaxRetry boundary, requeue reset, duplicate Stamp behavior, outbox Pending/Failed/Sent/DeadLetter claims, Sent terminal behavior and dead-letter retirement dedup key.

- [ ] **Step 2: Verify tests fail on direct requeue mutation**

Run: `DOTNET_ROLL_FORWARD=Major dotnet test HealthExam.Tests/HealthExam.Tests.csproj --filter "FullyQualifiedName~WebhookHandlerTests|FullyQualifiedName~OutboxHandlerTests"`

Expected: new tests FAIL until `Requeue` and protected setters exist.

- [ ] **Step 3: Add domain lifecycle operations and remove direct assignments**

```csharp
public DomainResult Requeue(DateTime nowUtc)
{
    if (ProcessState is WebhookProcessState.Processed or WebhookProcessState.Skipped)
        return DomainResult.Reject(DomainFailureCode.InvalidTransition, "Sự kiện đã kết thúc, không thể xếp lại");
    ProcessState = WebhookProcessState.New;
    RetryCount = 0;
    NextAttemptAt = null;
    LastError = "";
    ProcessedAt = null;
    return DomainResult.Apply();
}
```

Repositories may filter by state but must not change lifecycle state with ad-hoc assignments outside atomic SQL claim operations.

- [ ] **Step 4: Run unit and PostgreSQL queue tests**

Run unit tests: `DOTNET_ROLL_FORWARD=Major dotnet test HealthExam.Tests/HealthExam.Tests.csproj --filter "FullyQualifiedName~WebhookHandlerTests|FullyQualifiedName~OutboxHandlerTests|FullyQualifiedName~OutboxDeadLetterTests|FullyQualifiedName~OutboxInvariantPinTests"`

Run PostgreSQL: `HEALTHEXAM_TEST_DB="$HEALTHEXAM_TEST_DB" DOTNET_ROLL_FORWARD=Major dotnet test HealthExam.Tests/HealthExam.Tests.csproj --filter "FullyQualifiedName~WebhookQueueIntegrationTests|FullyQualifiedName~OutboxQueueIntegrationTests|FullyQualifiedName~OutboxQueuePostgresTests"`

Expected: PASS, including `SKIP LOCKED`, retry scheduling and dead-letter persistence.

- [ ] **Step 5: Commit**

```bash
git add HealthExam.Domain/Webhooks HealthExam.Domain/Integrations HealthExam.Application/Webhooks HealthExam.Application/Paraclinical/ProcessOutboxBatch.cs HealthExam.Infrastructure/Persistence HealthExam.Tests
git commit -m "refactor: enforce inbox and outbox lifecycles"
```

### Task 8: Consolidate FormServer ports and return typed integration outcomes

**Files:**
- Modify: `HealthExam.Application/Integrations/IFormServerClient.cs`
- Create: `HealthExam.Application/Integrations/IntegrationModels.cs`
- Delete: `HealthExam.Application/Paraclinical/IFormServerClient.cs`
- Modify: `HealthExam.Application/Webhooks/WebhookModels.cs`
- Modify: `HealthExam.Application/ExamRecords/GetExamFormDraft.cs`
- Modify: `HealthExam.Application/Paraclinical/GetConclusionEligibility.cs`, `SignConclusion.cs`
- Modify: `HealthExam.Application/Webhooks/ReconcileProgress.cs`
- Modify: `HealthExam.Infrastructure/Integrations/FormServer/FormServerClient.cs`, `FormServerWireModels.cs`
- Modify: `HealthExam.Infrastructure/DependencyInjection.cs`
- Test: `HealthExam.Tests/Application/ParaclinicalHandlerTests.cs`, `ExamRecordHandlerTests.cs`, `WebhookHandlerTests.cs`
- Test: `HealthExam.Tests/FormDraftTests.cs`, `ConclusionEligibilityTests.cs`
- Test: `HealthExam.Tests/API/CompositionRootTests.cs`

**Interfaces:**
- Produces: one `HealthExam.Application.Integrations.IFormServerClient`; `IntegrationResult<T>` with semantic outcomes

- [ ] **Step 1: Add failing tests for one FormServer boundary and expected failures**

Assert exactly one public interface named `IFormServerClient` exists. Add fake-client tests for Unauthorized, NotFound, DependencyUnavailable and Success without throwing framework/vendor exceptions.

- [ ] **Step 2: Run focused tests and verify duplicate-interface failure**

Run: `DOTNET_ROLL_FORWARD=Major dotnet test HealthExam.Tests/HealthExam.Tests.csproj --filter "FullyQualifiedName~ParaclinicalHandlerTests|FullyQualifiedName~CompositionRootTests"`

Expected: FAIL because two interfaces are registered.

- [ ] **Step 3: Define one role-sized interface**

```csharp
public enum IntegrationOutcome { Success, Unauthorized, Forbidden, NotFound, InvalidRequest, Unavailable, Timeout }

public sealed record IntegrationResult<T>(IntegrationOutcome Outcome, T Value, string Message, object Payload = null)
{
    public bool IsSuccess => Outcome == IntegrationOutcome.Success;
}

public sealed record ServiceCallOrigin(string DivisionId = "", string TraceId = "");

public sealed record FormSubmissionProgress(
    int CompletedSections,
    int TotalSections,
    short? SubmissionState,
    bool CanSignConclusion,
    string HealthClassCode);

public interface IFormServerClient
{
    Task<IntegrationResult<FormResolveResult>> ResolveFormAsync(string variantCode, DateOnly effectiveOn, ServiceCallOrigin origin, CancellationToken ct = default);
    Task<IntegrationResult<FormDraftResult>> CreateDraftAsync(Guid formId, string hostRefType, string hostRefId, Dictionary<string, Dictionary<string, string>> contextValues, ServiceCallOrigin origin, CancellationToken ct = default);
    Task<IntegrationResult<FormSubmissionProgress>> GetProgressAsync(Guid submissionId, ServiceCallOrigin origin, CancellationToken ct = default);
    Task<IntegrationResult<SignSubmissionResult>> SignAsync(Guid submissionId, SignSubmissionRequest request, ServiceCallOrigin origin, CancellationToken ct = default);
}
```

Infrastructure maps HTTP/vendor envelopes into these outcomes; handlers map outcomes to `ApplicationFailureCode`.

- [ ] **Step 4: Remove the duplicate interface and exception-based expected flow**

Update all four consumers and one DI registration. `FormServerClient` must not throw `HealthExamException` for expected HTTP/vendor errors.

- [ ] **Step 5: Run FormServer consumers and API composition tests**

Run: `DOTNET_ROLL_FORWARD=Major dotnet test HealthExam.Tests/HealthExam.Tests.csproj --filter "FullyQualifiedName~FormDraftTests|FullyQualifiedName~ConclusionEligibilityTests|FullyQualifiedName~ParaclinicalHandlerTests|FullyQualifiedName~WebhookHandlerTests|FullyQualifiedName~CompositionRootTests"`

Expected: PASS with existing Application failures and API numeric codes unchanged.

- [ ] **Step 6: Commit**

```bash
git add HealthExam.Application/Integrations HealthExam.Application/Paraclinical HealthExam.Application/ExamRecords/GetExamFormDraft.cs HealthExam.Application/Webhooks/ReconcileProgress.cs HealthExam.Infrastructure/Integrations/FormServer HealthExam.Infrastructure/DependencyInjection.cs HealthExam.Tests
git commit -m "refactor: consolidate form server integration port"
```

### Task 9: Replace generic HIS HTTP requests with operation-oriented typed ports

**Files:**
- Modify: `HealthExam.Application/Integrations/IHisEmrClient.cs`, `IHisFormDefinitionCache.cs`
- Delete: `HealthExam.Application/Integrations/IHisCredentialOptions.cs`
- Modify: `HealthExam.Application/His/HisModels.cs`, `HisHelper.cs`, `HisProcessValidator.cs`, `GetHisFormDefinition.cs`, `GetHisRecordSection.cs`, `GetExamRecordHisForm.cs`, `ListHisProcesses.cs`, `GetHisProcess.cs`, `GetSignWorkflow.cs`, `SubmitHisSection.cs`, `SignHisSection.cs`, `CancelHisSectionSignature.cs`
- Modify: `HealthExam.Application/RegistrationForms/HisDefinitionResolver.cs`, `RegistrationFormModels.cs`, `RegistrationFormNormalizer.cs`, `GetExamGroupRegistrationForms.cs`, `GetRegistrationForm.cs`, `SaveRegistrationFormSection.cs`, `PreviewRegistrationFormPdf.cs`
- Modify: `HealthExam.Infrastructure/Integrations/HisEmr/HisEmrClient.cs`, `HisEmrOptions.cs`, `HisEmrWireModels.cs`
- Modify: `HealthExam.Infrastructure/DependencyInjection.cs`
- Modify: `HealthExam.API/Controllers/HisFormController.cs`, `ExamRecordHisFormController.cs`, `ExamRecordRegistrationFormController.cs`, `ExamGroupController.cs`
- Test: `HealthExam.Tests/Application/HisHandlerTests.cs`, `RegistrationFormHandlerTests.cs`
- Test: `HealthExam.Tests/Infrastructure/HisEmrClientPdfTests.cs`
- Test: `HealthExam.Tests/HisEmrClientTests.cs`, `HisFormDefinitionTests.cs`, `HisExamFormServiceTests.cs`, `HisFormEndpointTests.cs`, `RisWireContractTests.cs`
- Test: `HealthExam.Tests/API/HisAuthEndpointTests.cs`, `RegistrationFormEndpointTests.cs`

**Interfaces:**
- Produces: operation methods on `IHisEmrClient`; typed definition/process/form-data models; no `HisRequest`, `HisJsonDocument`, `SendAsync` or raw vendor JSON in Application

- [ ] **Step 1: Add failing architecture and fake-port tests**

```csharp
[Fact]
public void His_port_does_not_expose_transport_shaped_members()
{
    var text = File.ReadAllText(Path.Combine(Root, "HealthExam.Application/Integrations/IHisEmrClient.cs"));
    Assert.DoesNotContain("RelativePath", text);
    Assert.DoesNotContain("RawJson", text);
    Assert.DoesNotContain("SendAsync", text);
    Assert.DoesNotContain("\"GET\"", text);
    Assert.DoesNotContain("\"POST\"", text);
}
```

Add handler tests using a fake operation method and assert the handler never supplies a route or HTTP verb.

- [ ] **Step 2: Run tests and verify the current generic client fails**

Run: `DOTNET_ROLL_FORWARD=Major dotnet test HealthExam.Tests/HealthExam.Tests.csproj --filter "FullyQualifiedName~His_port_does_not_expose_transport_shaped_members|FullyQualifiedName~HisHandlerTests"`

Expected: FAIL on `HisRequest`, `RawJson`, and `SendAsync`.

- [ ] **Step 3: Define the typed operation port**

```csharp
public sealed record RequestMetadata(string DivisionId, string TraceId);

public sealed record HisFormDefinitionData(
    Guid TemplateId,
    string TemplateCode,
    string TemplateName,
    int FileDocTypeId,
    string VersionCode,
    bool IsDraft,
    bool Active,
    IReadOnlyList<HisFormSectionDefinition> Details,
    IReadOnlyList<HisLayoutNode> LayoutNodes);

public sealed record HisFormSectionDefinition(int ItemGroupId, string Name);

public sealed record HisLayoutNode(
    string NodeId,
    string ParentNodeId,
    Guid? ItemId,
    int ItemGroupId,
    int Order,
    int Level,
    string Label,
    string ControlType,
    string DataType,
    bool Required,
    bool ReadOnly,
    IReadOnlyList<RegistrationFormChoice> Choices);

public sealed record HisMedicalProcess(
    Guid Id,
    Guid? TemplateId,
    int? FileDocTypeId,
    IReadOnlyList<HisMedicalProcessSection> Sections,
    IReadOnlyDictionary<string, object> ContractData);

public sealed record HisMedicalProcessSection(string SectionKey, IReadOnlyDictionary<string, object> ContractData);
public sealed record HisSignWorkflow(IReadOnlyDictionary<string, object> ContractData);
public sealed record HisOperationResponse(IReadOnlyDictionary<string, object> ContractData);
public sealed record HisSubmitSection(Guid ProcessId, string SectionKey, int Step, IReadOnlyList<object> SignatoryFlows);
public sealed record HisSignSection(Guid ProcessId, string SectionKey, int Step, long EmployeeId, long HandledRoleId);
public sealed record HisCancelSectionSignature(Guid ProcessId, string SectionKey, int Step, string Reason);
public sealed record HisAdmissionDraft(long PatientId, string PatientCode, string FullName, DateTime AdmissionDate);
public sealed record HisAdmission(long AdmissionId);
public sealed record HisFormDataKey(Guid EmrDataId, Guid TemplateId, long AdmissionId, bool Inherit);
public sealed record HisFormFieldValue(Guid ItemId, string Value, string Text, string DataType, string ControlStyle);
public sealed record HisFormData(Guid EmrDataId, IReadOnlyList<HisFormFieldValue> Details);
public sealed record HisFormDataDraft(Guid EmrDataId, Guid TemplateId, long AdmissionId, long PatientId, string PatientCode, int DepartmentId, DateTime VoucherDate, bool IsDraft, IReadOnlyList<HisFormFieldValue> Details);

public interface IHisEmrClient
{
    Task<HisClientResult<HisFormDefinitionData>> GetFormDefinitionAsync(string templateCode, string credential, RequestMetadata metadata, CancellationToken ct = default);
    Task<HisClientResult<IReadOnlyList<HisMedicalProcess>>> ListProcessesAsync(long admissionId, string credential, RequestMetadata metadata, CancellationToken ct = default);
    Task<HisClientResult<HisMedicalProcess>> GetProcessAsync(Guid processId, string credential, RequestMetadata metadata, CancellationToken ct = default);
    Task<HisClientResult<HisSignWorkflow>> GetSignWorkflowAsync(int fileDocTypeId, string credential, RequestMetadata metadata, CancellationToken ct = default);
    Task<HisClientResult<HisOperationResponse>> SubmitSectionAsync(HisSubmitSection request, string credential, RequestMetadata metadata, CancellationToken ct = default);
    Task<HisClientResult<HisOperationResponse>> SignSectionAsync(HisSignSection request, string credential, RequestMetadata metadata, CancellationToken ct = default);
    Task<HisClientResult<HisOperationResponse>> CancelSectionSignatureAsync(HisCancelSectionSignature request, string credential, RequestMetadata metadata, CancellationToken ct = default);
    Task<HisClientResult<HisAdmission>> CreateAdmissionAsync(HisAdmissionDraft request, string credential, RequestMetadata metadata, CancellationToken ct = default);
    Task<HisClientResult<HisFormData>> ReadFormDataAsync(HisFormDataKey key, string credential, RequestMetadata metadata, CancellationToken ct = default);
    Task<HisClientResult<Guid>> SaveFormDataAsync(HisFormDataDraft request, string credential, RequestMetadata metadata, CancellationToken ct = default);
    Task<HisClientResult<byte[]>> RenderFormPdfAsync(Guid emrDataId, string credential, RequestMetadata metadata, CancellationToken ct = default);
}
```

Place these request/result records in `HisModels.cs`. `ContractData` contains already-parsed primitive/list/dictionary values required solely to reproduce the existing dynamic API payload; it must never contain `JsonElement`, `JToken`, an HTTP type or raw JSON text.

Change `SubmitHisSectionCommand.SignatoryFlowsJson` to `IReadOnlyList<object> SignatoryFlows`. At the API boundary, recursively convert the incoming `JArray` to plain dictionaries, lists, strings, numbers, booleans and nulls before constructing the command; no Newtonsoft type crosses into Application.

- [ ] **Step 4: Move all route, verb and JSON work into Infrastructure**

`HisEmrClient` implements each operation by composing the existing route constants internally, serializing wire request models and parsing vendor aliases (`Id`/`TemplateID`/`TemplateId`, etc.) into typed Application models. Preserve timeouts, header name, redirect behavior, status mapping and response compatibility.

- [ ] **Step 5: Simplify handlers and remove Application JSON parsing**

Handlers filter typed lists and validate typed process sections. Registration form normalization consumes typed layout/detail nodes. `SaveRegistrationFormSectionHandler` sends `HisAdmissionDraft`/`HisFormDataDraft`; it no longer creates anonymous wire payloads or parses returned JSON IDs. Remove `IHisCredentialOptions`; department configuration is applied inside Infrastructure admission creation.

- [ ] **Step 6: Map typed results to the unchanged API contract**

Controllers map typed Application models into existing API contract DTO/JToken shapes. Keep property names and unknown optional fields required by characterization tests; use API/Newtonsoft mapping only at this boundary.

- [ ] **Step 7: Run HIS, registration-form and API contract tests**

Run: `DOTNET_ROLL_FORWARD=Major dotnet test HealthExam.Tests/HealthExam.Tests.csproj --filter "FullyQualifiedName~HisHandlerTests|FullyQualifiedName~RegistrationFormHandlerTests|FullyQualifiedName~HisEmrClientTests|FullyQualifiedName~HisFormDefinitionTests|FullyQualifiedName~HisExamFormServiceTests|FullyQualifiedName~HisFormEndpointTests|FullyQualifiedName~HisAuthEndpointTests|FullyQualifiedName~RegistrationFormEndpointTests"`

Expected: PASS with no `System.Text.Json` usage in Application HIS/RegistrationForms and unchanged endpoint JSON.

- [ ] **Step 8: Commit**

```bash
git add HealthExam.Application/Integrations HealthExam.Application/His HealthExam.Application/RegistrationForms HealthExam.Infrastructure/Integrations/HisEmr HealthExam.Infrastructure/DependencyInjection.cs HealthExam.API/Controllers HealthExam.Tests
git commit -m "refactor: make his integration port use case oriented"
```

### Task 10: Remove Domain API exceptions and finish semantic failure mapping

**Files:**
- Delete: `HealthExam.Domain/Common/HealthExamException.cs`
- Modify: `HealthExam.Domain/Common/DomainFailure.cs`
- Modify: `HealthExam.Application/Common/DomainFailureMapper.cs`
- Modify: `HealthExam.Application/ExamRecords/CancelExamRecord.cs`, `ConfirmExamRecord.cs`
- Modify: `HealthExam.Application/ExamSessions/CloseExamSession.cs`, `ReopenExamSession.cs`
- Modify: `HealthExam.Application/Imports/DiscardImport.cs`
- Modify: `HealthExam.API/Middlewares/ApplicationResultMapper.cs`, `ExceptionMiddleware.cs`
- Modify: `HealthExam.API/Controllers/InternalController.cs`
- Modify: `HealthExam.Infrastructure/Excel/ExamImportSheet.cs`
- Modify: `HealthExam.Infrastructure/Integrations/FormServer/FormServerClient.cs`, `HisEmr/HisEmrClient.cs`
- Test: `HealthExam.Tests/API/ApiContractSurfaceTests.cs`
- Test: `HealthExam.Tests/Application/ApplicationResultTests.cs`
- Test: `HealthExam.Tests/ErrorCodeTests.cs`, `ExamImportTests.cs`, `ConclusionEligibilityTests.cs`
- Test: `HealthExam.Tests/Architecture/DependencyRuleTests.cs`

**Interfaces:**
- Produces: semantic `DomainFailureCode`; centralized Domain→Application and Application→API mappings; no numeric errors in Domain

- [ ] **Step 1: Add contract pins for every current error mapping**

Add theory cases for BadRequest=4001/400, FileInvalid=4002/400, Unauthorized=4010/401, Forbidden=4030/403, NotOwner=4031/403, NotFound=4040/404, InvalidState=4090/409, SessionClosed=4091/409, NotInSession=4092/409, DuplicateInSession=4093/409, ParaclinicalResultExists=4094/409, VendorPayloadIncomplete=4095/422, SignPrecondition=4221/422, InternalError=5000/500, DependencyUnavailable=5020/503, VendorNotConfigured=5021/503, HisBadGateway=5022/502 and HisTimeout=5040/504.

- [ ] **Step 2: Add a failing Domain purity test**

Scan Domain source and assert it contains none of the numeric contract values `4001`, `4040`, `4090`, `5021` and none of the type/name markers `HealthExamException`, `HttpStatusCode`, `StatusCodes`. Do not ban the word `Payload`: `WebhookInbox.Payload` and `IntegrationOutbox.Payload` are persisted domain data, not API error payloads. Run the test and expect failure at `HealthExamException.cs`.

- [ ] **Step 3: Expand semantic failure vocabulary and centralize mapping**

Add named Domain codes needed by current behavior, including `SessionClosed`, `NotInSession`, `DuplicateInSession`, `ParaclinicalResultExists` and `InvalidTransition`. Implement:

```csharp
public static ApplicationResult<T> FromDomain<T>(DomainResult result, T value = default)
    => result.IsSuccess
        ? ApplicationResult<T>.Success(value)
        : ApplicationResult<T>.Fail(Map(result.Failure.Code), result.Failure.Message);
```

The mapper contains no numeric API codes.

- [ ] **Step 4: Remove `HealthExamException` and exception-based expected flows**

Task 5, Task 8 and Task 9 have already converted workbook/FormServer/HIS expected errors to typed outcomes. Replace `InternalController` authorization throwing with an `ApplicationResult`/direct API error response owned by API. `ExceptionMiddleware` catches only unexpected exceptions and returns 5000. Delete `HealthExamException.cs`.

- [ ] **Step 5: Run error and architecture tests**

Run: `DOTNET_ROLL_FORWARD=Major dotnet test HealthExam.Tests/HealthExam.Tests.csproj --filter "FullyQualifiedName~ErrorCodeTests|FullyQualifiedName~ApplicationResultTests|FullyQualifiedName~ApiContractSurfaceTests|FullyQualifiedName~HealthExam.Tests.Architecture"`

Expected: PASS; all expected errors retain numeric/status contracts while Domain has none.

- [ ] **Step 6: Commit**

```bash
git add HealthExam.Domain/Common HealthExam.Application HealthExam.API/Middlewares HealthExam.API/Controllers/InternalController.cs HealthExam.Infrastructure/Excel HealthExam.Infrastructure/Integrations HealthExam.Tests
git commit -m "refactor: keep api error contracts outside domain"
```

### Task 11: Enforce all boundaries and run the four mandatory verification groups

**Files:**
- Modify: `HealthExam.Tests/Architecture/DependencyRuleTests.cs`
- Modify: `HealthExam.Tests/API/CompositionRootTests.cs`, `ApiContractSurfaceTests.cs`
- Modify: test files only where the final full run exposes a compatibility regression

**Interfaces:**
- Consumes: all remediated layers from Tasks 1–10
- Produces: executable architecture completion gate and verified local build/test evidence

- [ ] **Step 1: Complete the architecture gate**

Ensure tests assert:

```csharp
Assert.Empty(PublicInterfacesEndingInHandler());
Assert.Empty(ApiInfrastructureImportsOutsideProgram());
Assert.Empty(ApplicationPublicStateAssignments());
Assert.Empty(TransportShapedApplicationPorts());
Assert.Empty(PublicSettersForProtectedState());
```

Protected state list is exact: `ExamSession.State`, `ExamRecord.State`, `ImportBatch.State`, `ParaclinicalOrderItem.State`, `WebhookInbox.ProcessState`, `IntegrationOutbox.State`.

- [ ] **Step 2: Run dependency and build gates**

Run: `dotnet build HealthExamServer.sln --no-restore`

Expected: exit 0, 0 warnings, 0 errors.

Run: `DOTNET_ROLL_FORWARD=Major dotnet test HealthExam.Tests/HealthExam.Tests.csproj --no-build --filter FullyQualifiedName~HealthExam.Tests.Architecture`

Expected: PASS.

- [ ] **Step 3: Run Domain tests**

Run: `DOTNET_ROLL_FORWARD=Major dotnet test HealthExam.Tests/HealthExam.Tests.csproj --no-build --filter FullyQualifiedName~HealthExam.Tests.Domain`

Expected: PASS for every transition, invariant, idempotency and semantic failure.

- [ ] **Step 4: Run Application tests**

Run: `DOTNET_ROLL_FORWARD=Major dotnet test HealthExam.Tests/HealthExam.Tests.csproj --no-build --filter FullyQualifiedName~HealthExam.Tests.Application`

Expected: PASS using concrete handlers and fake repository/integration ports.

- [ ] **Step 5: Run PostgreSQL Infrastructure tests**

Precondition: `test -n "$HEALTHEXAM_TEST_DB"` exits 0.

Run: `HEALTHEXAM_TEST_DB="$HEALTHEXAM_TEST_DB" HEX_PG_TESTS=1 DOTNET_ROLL_FORWARD=Major dotnet test HealthExam.Tests/HealthExam.Tests.csproj --no-build --filter "FullyQualifiedName~HealthExam.Tests.Infrastructure|FullyQualifiedName~Postgres"`

Expected: PASS with real PostgreSQL coverage for `FOR UPDATE`, `SKIP LOCKED`, sequence, savepoint, unique constraint, retry/dead-letter, EF private-setter materialization and rollback.

- [ ] **Step 6: Run API contract tests**

Run: `DOTNET_ROLL_FORWARD=Major dotnet test HealthExam.Tests/HealthExam.Tests.csproj --no-build --filter FullyQualifiedName~HealthExam.Tests.API`

Expected: PASS for route, authentication, middleware order, request/response JSON, HTTP status, error code, payload, trace ID and Swagger.

- [ ] **Step 7: Verify no schema or migration drift**

Run: `git diff --exit-code 29c62d6 -- HealthExam.Infrastructure/Persistence/Migrations`

Expected: exit 0 with no migration or model snapshot changes.

- [ ] **Step 8: Run the complete test project**

Run: `HEALTHEXAM_TEST_DB="$HEALTHEXAM_TEST_DB" HEX_PG_TESTS=1 DOTNET_ROLL_FORWARD=Major dotnet test HealthExam.Tests/HealthExam.Tests.csproj --no-build`

Expected: all tests pass, 0 failed, 0 skipped for mandatory PostgreSQL cases.

- [ ] **Step 9: Commit final gates**

```bash
git add HealthExam.Tests
git commit -m "test: enforce pragmatic clean architecture boundaries"
```

## Completion Checklist

- [ ] Solution chỉ chứa Domain, Application, Infrastructure, API và Tests.
- [ ] Không còn public `I*Handler`; controller/worker inject concrete handlers.
- [ ] Domain không chứa numeric API codes hoặc vendor/HTTP concepts.
- [ ] Sáu protected state properties không có public setter.
- [ ] Application không gán trực tiếp protected state.
- [ ] Application integration ports không chứa route, verb, header lookup hoặc raw vendor JSON.
- [ ] API ngoài `Program.cs` không tham chiếu Infrastructure.
- [ ] `IClock` nằm ở Application, `SystemClock` nằm ở Infrastructure, và Application không đọc system time trực tiếp.
- [ ] API contract và database schema/migrations không đổi.
- [ ] Build cùng cả bốn nhóm test đều pass local, bao gồm PostgreSQL thật.
