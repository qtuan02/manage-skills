# Clean Architecture Refactor Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace `HealthExam.Core` and `HealthExam.Server` with Domain, Application, and Infrastructure projects while preserving every API contract, database schema, and existing behavior.

**Architecture:** Build the new project foundation first, move persistence without schema changes, then migrate feature-by-feature through Domain rules, one Application handler per use case, Infrastructure implementations, and thin API controllers. Keep the legacy projects only while a slice still depends on them; remove them after all endpoints and workers use the new handlers.

**Tech Stack:** .NET 8, ASP.NET Core 8, EF Core 8.0.11, Npgsql EF Core 8.0.11, PostgreSQL, Newtonsoft.Json 13.0.3, EPPlus 7.1.2, xUnit 2.9.2

**Spec:** `docs/superpowers/specs/2026-09-10-clean-architecture-refactor-design.md`

## Global Constraints

- Preserve routes, HTTP methods, headers, authentication, JSON, HTTP status, error codes, payloads, trace IDs, and Swagger output.
- Preserve the database schema, table/column names, enum numeric values, existing migration IDs, and stored data.
- Preserve transaction boundaries, PostgreSQL locking, sequence semantics, savepoints, idempotency, retries, and timestamps.
- `HealthExam.Domain` and `HealthExam.Application` may reference only BCL/.NET; no EF Core, Npgsql, Newtonsoft, EPPlus, ASP.NET, HTTP, cache, hosting, logging, options, or DI packages.
- Do not expose `IQueryable` from Application interfaces and do not add a generic repository to Application.
- Controllers call Application handlers only. `Program.cs` is the sole API composition-root exception that references Infrastructure.
- Use one dedicated handler interface per use case; do not add mediator, event-bus, domain-event, transaction-pipeline, or logging frameworks.
- Keep the folder structure shallow and feature-oriented.
- Run Infrastructure integration tests against PostgreSQL through `HEALTHEXAM_TEST_DB`; a missing variable must fail the integration suite rather than skip it.
- Limit new tests to Domain, Application, Infrastructure integration, and API contract tests.
- Do not modify or commit the unrelated root `node_modules/`, `package.json`, or `pnpm-lock.yaml` files.

---

## Target File Map

```text
HealthExam.Domain/
  HealthExam.Domain.csproj                 # package-free domain assembly
  Common/DomainFailure.cs                  # domain-only failure vocabulary
  ExamSessions/ExamSession.cs              # session aggregate and transitions
  ExamRecords/ExamRecord.cs                # record aggregate and transitions
  Imports/ImportBatch.cs                    # import lifecycle
  Paraclinical/ParaclinicalOrder.cs         # order aggregate and item state machine
  Webhooks/WebhookInbox.cs                  # inbox lifecycle
  Integrations/IntegrationOutbox.cs         # outbox lifecycle
  Catalogs/*.cs                             # catalog entities and enums

HealthExam.Application/
  HealthExam.Application.csproj
  Common/ApplicationResult.cs               # typed handler result
  Common/ApplicationFailure.cs              # semantic failure codes
  Common/IUnitOfWork.cs                      # technology-neutral transaction boundary
  Common/IAuditRepository.cs                 # audit write abstraction
  Catalogs/*.cs                              # catalog handlers and repository interfaces
  ExamSessions/*.cs                         # session handlers and repository interface
  ExamRecords/*.cs                          # record handlers and repository interface
  Imports/*.cs                              # import handlers and workbook/store interfaces
  Paraclinical/*.cs                         # order/conclusion handlers and interfaces
  Webhooks/*.cs                             # ingest/process/requeue/reconcile handlers
  Integrations/*.cs                         # form-server, HIS, RIS contracts

HealthExam.Infrastructure/
  HealthExam.Infrastructure.csproj
  DependencyInjection.cs
  Persistence/HealthExamDbContext.cs
  Persistence/HealthExamDbContextFactory.cs
  Persistence/Configurations/*.cs
  Persistence/Migrations/*.cs
  Persistence/Repositories/*.cs
  Integrations/FormServer/*.cs
  Integrations/HisEmr/*.cs
  Integrations/Ris/*.cs
  Excel/*.cs
  Caching/*.cs
  BackgroundJobs/*.cs

HealthExam.API/
  Program.cs
  Contracts/*.cs
  Controllers/*.cs
  Middlewares/ApplicationResultMapper.cs

HealthExam.Tests/
  Architecture/DependencyRuleTests.cs
  Domain/*.cs
  Application/*.cs
  Infrastructure/*.cs
  API/*.cs
```

Existing tests remain in place while their production code moves. Move or replace a test only when the corresponding slice makes the old test helper impossible to retain.

---

### Task 1: Create the clean project skeleton and dependency gate

**Files:**
- Create: `HealthExam.Domain/HealthExam.Domain.csproj`
- Create: `HealthExam.Application/HealthExam.Application.csproj`
- Create: `HealthExam.Infrastructure/HealthExam.Infrastructure.csproj`
- Create: `HealthExam.Tests/Architecture/DependencyRuleTests.cs`
- Modify: `HealthExamServer.sln`
- Modify: `HealthExam.API/HealthExam.API.csproj`
- Modify: `HealthExam.Tests/HealthExam.Tests.csproj`

**Interfaces:**
- Consumes: existing .NET 8 solution and package versions
- Produces: the final project-reference direction and a test that prevents dependency regressions

- [ ] **Step 1: Write the failing dependency-rule test**

```csharp
namespace HealthExam.Tests.Architecture;

public class DependencyRuleTests
{
    private static readonly string Root = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "../../../../"));

    [Theory]
    [InlineData("HealthExam.Domain/HealthExam.Domain.csproj")]
    [InlineData("HealthExam.Application/HealthExam.Application.csproj")]
    public void Inner_projects_have_no_package_references(string relativePath)
    {
        var xml = File.ReadAllText(Path.Combine(Root, relativePath));
        Assert.DoesNotContain("<PackageReference", xml, StringComparison.Ordinal);
    }

    [Fact]
    public void Application_references_only_Domain()
    {
        var xml = File.ReadAllText(Path.Combine(
            Root, "HealthExam.Application/HealthExam.Application.csproj"));
        Assert.Contains("HealthExam.Domain.csproj", xml, StringComparison.Ordinal);
        Assert.DoesNotContain("HealthExam.Infrastructure.csproj", xml, StringComparison.Ordinal);
        Assert.DoesNotContain("HealthExam.API.csproj", xml, StringComparison.Ordinal);
    }
}
```

- [ ] **Step 2: Run the test and verify it fails because the new projects do not exist**

Run: `dotnet test HealthExam.Tests/HealthExam.Tests.csproj --filter FullyQualifiedName~DependencyRuleTests`

Expected: FAIL with `DirectoryNotFoundException` or `FileNotFoundException` for `HealthExam.Domain.csproj`.

- [ ] **Step 3: Create the projects with the exact dependency direction**

```xml
<!-- HealthExam.Domain/HealthExam.Domain.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>disable</Nullable>
  </PropertyGroup>
</Project>
```

```xml
<!-- HealthExam.Application/HealthExam.Application.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>disable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\HealthExam.Domain\HealthExam.Domain.csproj" />
  </ItemGroup>
</Project>
```

Create Infrastructure with the exact package baseline already used by the solution:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>disable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.EntityFrameworkCore" Version="8.0.11" />
    <PackageReference Include="Microsoft.EntityFrameworkCore.Design" Version="8.0.11">
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
      <PrivateAssets>all</PrivateAssets>
    </PackageReference>
    <PackageReference Include="Npgsql.EntityFrameworkCore.PostgreSQL" Version="8.0.11" />
    <PackageReference Include="Microsoft.Extensions.DependencyInjection.Abstractions" Version="8.0.2" />
    <PackageReference Include="Microsoft.Extensions.Hosting.Abstractions" Version="8.0.1" />
    <PackageReference Include="Microsoft.Extensions.Http" Version="8.0.1" />
    <PackageReference Include="Microsoft.Extensions.Caching.Memory" Version="8.0.1" />
    <PackageReference Include="Newtonsoft.Json" Version="13.0.3" />
    <PackageReference Include="EPPlus" Version="7.1.2" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\HealthExam.Domain\HealthExam.Domain.csproj" />
    <ProjectReference Include="..\HealthExam.Application\HealthExam.Application.csproj" />
  </ItemGroup>
</Project>
```

Add the three projects to `HealthExamServer.sln`. Add Application and Infrastructure references to API and add all three new project references to Tests; retain legacy references until Task 14.

- [ ] **Step 4: Run the dependency test and solution build**

Run: `dotnet test HealthExam.Tests/HealthExam.Tests.csproj --filter FullyQualifiedName~DependencyRuleTests && dotnet build HealthExamServer.sln`

Expected: PASS and `Build succeeded`.

- [ ] **Step 5: Commit the skeleton**

```bash
git add HealthExam.Domain HealthExam.Application HealthExam.Infrastructure HealthExam.API/HealthExam.API.csproj HealthExam.Tests/HealthExam.Tests.csproj HealthExam.Tests/Architecture HealthExamServer.sln
git commit -m "refactor: add clean architecture project skeleton"
```

---

### Task 2: Move entities, DbContext, configurations, and migrations without behavior changes

**Files:**
- Move: `HealthExam.Core/EntityFramework/Entity/*.cs` to the matching feature folder under `HealthExam.Domain/`
- Move: `HealthExam.Core/Models/Enums.cs` to `HealthExam.Domain/Common/Enums.cs`
- Move: `HealthExam.Core/Models/RecordFieldLengths.cs` to `HealthExam.Domain/Common/FieldLengths.cs`
- Move: `HealthExam.Core/Models/ModuleCodes.cs` to `HealthExam.Domain/Common/ModuleCodes.cs`
- Move: `HealthExam.Core/Models/ExamGroups.cs` to `HealthExam.Domain/ExamSessions/ExamGroups.cs`
- Move: `HealthExam.Core/Models/MasterDataCategories.cs` to `HealthExam.Domain/Catalogs/MasterDataCategories.cs`
- Move: `HealthExam.Core/EntityFramework/HealthExamDbContext.cs` to `HealthExam.Infrastructure/Persistence/HealthExamDbContext.cs`
- Move: `HealthExam.Core/EntityFramework/HealthExamDbContextFactory.cs` to `HealthExam.Infrastructure/Persistence/HealthExamDbContextFactory.cs`
- Move: `HealthExam.Core/Migrations/*.cs` to `HealthExam.Infrastructure/Persistence/Migrations/`
- Move temporarily: `HealthExam.Core/EntityFramework/Repositories/*.cs` to `HealthExam.Infrastructure/Persistence/Legacy/`
- Create: `HealthExam.Tests/Infrastructure/PersistenceModelTests.cs`
- Modify: `HealthExam.Server/HealthExam.Server.csproj`
- Modify: all compile-time namespace imports under `HealthExam.Server/`, `HealthExam.API/`, and `HealthExam.Tests/`

**Interfaces:**
- Consumes: the existing EF model and migration IDs
- Produces: `HealthExam.Infrastructure.Persistence.HealthExamDbContext` mapped to Domain entities; legacy services compile through a temporary Infrastructure repository implementation

- [ ] **Step 1: Add a model/migration characterization test before moving files**

```csharp
namespace HealthExam.Tests.Infrastructure;

public class PersistenceModelTests
{
    [Fact]
    public void Model_keeps_critical_table_names()
    {
        using var db = InMemoryTestDb.CreateContext();
        var tables = db.Model.GetEntityTypes()
            .Select(x => x.GetTableName())
            .Where(x => x is not null)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Contains("HEX_ExamSession", tables);
        Assert.Contains("HEX_ExamRecord", tables);
        Assert.Contains("HEX_ImportBatch", tables);
        Assert.Contains("HEX_WebhookInbox", tables);
        Assert.Contains("HEX_IntegrationOutbox", tables);
    }
}
```

- [ ] **Step 2: Run the characterization test before the move**

Run: `dotnet test HealthExam.Tests/HealthExam.Tests.csproj --filter FullyQualifiedName~PersistenceModelTests`

Expected: PASS against the legacy context.

- [ ] **Step 3: Move persistence files and update namespaces mechanically**

Use `git mv`, not copy/delete. Domain entity namespaces must follow `HealthExam.Domain.<Feature>`; move the enums, field-length rules, module constants, exam groups, and master-data categories listed above with them because the entities and Domain rules consume those types. DbContext, factory, migrations, configurations, and the temporary generic repository use `HealthExam.Infrastructure.Persistence` namespaces. Do not rename properties, keys, indexes, sequences, constraints, tables, columns, migration classes, or migration ID attributes.

The design-time factory must continue to construct the same provider:

```csharp
public sealed class HealthExamDbContextFactory
    : IDesignTimeDbContextFactory<HealthExamDbContext>
{
    public HealthExamDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("HEALTHEXAM_DB") ?? "";
        var options = new DbContextOptionsBuilder<HealthExamDbContext>()
            .UseNpgsql(connectionString)
            .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking)
            .Options;
        return new HealthExamDbContext(options);
    }
}
```

Add Domain and Infrastructure project references to `HealthExam.Server.csproj`, then update existing services to import Domain entities and the temporary Infrastructure legacy repository. This dependency is transitional and is deleted with the project in Task 14.

- [ ] **Step 4: Verify model, migration discovery, and build**

Run: `dotnet test HealthExam.Tests/HealthExam.Tests.csproj --filter FullyQualifiedName~PersistenceModelTests && dotnet ef migrations list --project HealthExam.Infrastructure --startup-project HealthExam.Infrastructure && dotnet build HealthExamServer.sln`

Expected: the migration list contains `20260824083342_InitialHealthExamSchema` through `20260908102936_AddHisAdmissionId`, with no new migration, and the build succeeds.

- [ ] **Step 5: Commit the persistence relocation**

```bash
git add HealthExam.Domain HealthExam.Infrastructure HealthExam.Server HealthExam.API HealthExam.Tests
git commit -m "refactor: move domain entities and persistence infrastructure"
```

---

### Task 3: Add typed results, transaction abstractions, and API failure mapping

**Files:**
- Create: `HealthExam.Domain/Common/DomainFailure.cs`
- Create: `HealthExam.Application/Common/ApplicationFailure.cs`
- Create: `HealthExam.Application/Common/ApplicationResult.cs`
- Create: `HealthExam.Application/Common/IUnitOfWork.cs`
- Create: `HealthExam.Application/Common/IAuditRepository.cs`
- Create: `HealthExam.Application/Common/IClock.cs`
- Create: `HealthExam.Infrastructure/Persistence/UnitOfWork.cs`
- Create: `HealthExam.Infrastructure/Persistence/AuditRepository.cs`
- Create: `HealthExam.API/Middlewares/ApplicationResultMapper.cs`
- Create: `HealthExam.Tests/Application/ApplicationResultTests.cs`
- Modify: `HealthExam.API/Controllers/HealthExamControllerBase.cs`

**Interfaces:**
- Consumes: existing `ErrorCodes`, `ResultData<T>`, and HTTP mapping
- Produces: `ApplicationResult<T>`, `ApplicationFailureCode`, `IUnitOfWork`, `IApplicationTransaction`, and one centralized API mapper

- [ ] **Step 1: Write failing result and error-mapping tests**

```csharp
public class ApplicationResultTests
{
    [Fact]
    public void Failure_carries_semantic_code_without_numeric_api_code()
    {
        var result = ApplicationResult<string>.Fail(
            ApplicationFailureCode.InvalidState, "Đợt khám đã đóng");

        Assert.False(result.IsSuccess);
        Assert.Equal(ApplicationFailureCode.InvalidState, result.Failure.Code);
        Assert.Equal("Đợt khám đã đóng", result.Failure.Message);
    }
}
```

Extend `ErrorCodeTests` with a theory asserting that every `ApplicationFailureCode` used by handlers maps to the existing `ErrorCodes` constant and HTTP status.

- [ ] **Step 2: Run the tests and verify missing-type failures**

Run: `dotnet test HealthExam.Tests/HealthExam.Tests.csproj --filter "FullyQualifiedName~ApplicationResultTests|FullyQualifiedName~ErrorCodeTests"`

Expected: FAIL because `ApplicationResult<T>` and `ApplicationFailureCode` do not exist.

- [ ] **Step 3: Implement the shared types and mapper**

```csharp
public enum DomainFailureCode
{
    InvalidTransition,
    RequiredValue,
    Conflict
}

public enum DomainOperationOutcome
{
    Applied,
    NoOp,
    Rejected
}

public sealed record DomainFailure(DomainFailureCode Code, string Message);

public readonly record struct DomainResult(
    DomainOperationOutcome Outcome,
    DomainFailure Failure = null)
{
    public bool IsSuccess => Outcome is not DomainOperationOutcome.Rejected;
    public bool Applied => Outcome is DomainOperationOutcome.Applied;

    public static DomainResult Apply() => new(DomainOperationOutcome.Applied);
    public static DomainResult NoOp() => new(DomainOperationOutcome.NoOp);
    public static DomainResult Reject(DomainFailureCode code, string message)
        => new(DomainOperationOutcome.Rejected, new DomainFailure(code, message));
}
```

```csharp
public enum ApplicationFailureCode
{
    BadRequest,
    Unauthorized,
    Forbidden,
    NotOwner,
    NotFound,
    InvalidState,
    FileInvalid,
    SessionClosed,
    NotInSession,
    DuplicateInSession,
    ParaclinicalResultExists,
    VendorPayloadIncomplete,
    VendorNotConfigured,
    DependencyUnavailable,
    HisTimeout,
    HisBadGateway,
    SignPrecondition
}

public sealed record ApplicationFailure(
    ApplicationFailureCode Code,
    string Message,
    object Payload = null);

public sealed class ApplicationResult<T>
{
    public bool IsSuccess { get; }
    public T Value { get; }
    public ApplicationFailure Failure { get; }

    private ApplicationResult(bool success, T value, ApplicationFailure failure)
        => (IsSuccess, Value, Failure) = (success, value, failure);

    public static ApplicationResult<T> Success(T value) => new(true, value, null);
    public static ApplicationResult<T> Fail(
        ApplicationFailureCode code, string message, object payload = null)
        => new(false, default, new ApplicationFailure(code, message, payload));
}

public sealed record PageResult<T>(
    IReadOnlyList<T> Items, int Page, int Size, int Total);
```

```csharp
public interface IApplicationTransaction : IAsyncDisposable
{
    Task CommitAsync(CancellationToken ct = default);
    Task RollbackAsync(CancellationToken ct = default);
}

public enum PersistenceSaveOutcome { Saved, UniqueConflict }

public readonly record struct PersistenceSaveResult(
    PersistenceSaveOutcome Outcome, string ConstraintName = null);

public interface IUnitOfWork
{
    Task<IApplicationTransaction> BeginAsync(CancellationToken ct = default);
    Task<PersistenceSaveResult> SaveChangesAsync(CancellationToken ct = default);
    void DiscardPendingChanges();
}

public sealed record AuditEntry(
    string DivisionId,
    string EntityType,
    Guid EntityId,
    string Action,
    string ActorId,
    short? FromState,
    short? ToState,
    object Payload);

public interface IAuditRepository
{
    void Add(AuditEntry entry);
}

public interface IClock
{
    DateTime UtcNow { get; }
}
```

Infrastructure catches PostgreSQL SQLSTATE `23505` and returns `UniqueConflict`; it must not expose `PostgresException` to Application. `ApplicationResultMapper` maps semantic codes exactly as follows and is called by `HealthExamControllerBase`:

```csharp
private static int ToErrorCode(ApplicationFailureCode code) => code switch
{
    ApplicationFailureCode.BadRequest => ErrorCodes.BadRequest,
    ApplicationFailureCode.Unauthorized => ErrorCodes.Unauthorized,
    ApplicationFailureCode.Forbidden => ErrorCodes.Forbidden,
    ApplicationFailureCode.NotOwner => ErrorCodes.NotOwner,
    ApplicationFailureCode.NotFound => ErrorCodes.NotFound,
    ApplicationFailureCode.InvalidState => ErrorCodes.InvalidState,
    ApplicationFailureCode.FileInvalid => ErrorCodes.FileInvalid,
    ApplicationFailureCode.SessionClosed => ErrorCodes.SessionClosed,
    ApplicationFailureCode.NotInSession => ErrorCodes.NotInSession,
    ApplicationFailureCode.DuplicateInSession => ErrorCodes.DuplicateInSession,
    ApplicationFailureCode.ParaclinicalResultExists => ErrorCodes.ParaclinicalResultExists,
    ApplicationFailureCode.VendorPayloadIncomplete => ErrorCodes.VendorPayloadIncomplete,
    ApplicationFailureCode.VendorNotConfigured => ErrorCodes.VendorNotConfigured,
    ApplicationFailureCode.DependencyUnavailable => ErrorCodes.DependencyUnavailable,
    ApplicationFailureCode.HisTimeout => ErrorCodes.HisTimeout,
    ApplicationFailureCode.HisBadGateway => ErrorCodes.HisBadGateway,
    ApplicationFailureCode.SignPrecondition => ErrorCodes.SignPrecondition,
    _ => ErrorCodes.InternalError
};
```

- [ ] **Step 4: Run result, mapping, and architecture tests**

Run: `dotnet test HealthExam.Tests/HealthExam.Tests.csproj --filter "FullyQualifiedName~ApplicationResultTests|FullyQualifiedName~ErrorCodeTests|FullyQualifiedName~DependencyRuleTests"`

Expected: PASS.

- [ ] **Step 5: Commit shared application primitives**

```bash
git add HealthExam.Domain/Common HealthExam.Application/Common HealthExam.Infrastructure/Persistence HealthExam.API/Middlewares/ApplicationResultMapper.cs HealthExam.API/Controllers/HealthExamControllerBase.cs HealthExam.Tests
git commit -m "refactor: add typed application results and transaction boundary"
```

---

### Task 4: Migrate Catalog and Master Data as the walking skeleton

**Files:**
- Create: `HealthExam.Application/Catalogs/CatalogModels.cs`
- Create: `HealthExam.Application/Catalogs/ICatalogRepository.cs`
- Create: `HealthExam.Application/Catalogs/ListOrganizations.cs`
- Create: `HealthExam.Application/Catalogs/ListExamPackages.cs`
- Create: `HealthExam.Application/Catalogs/ListExamGroups.cs`
- Create: `HealthExam.Application/Catalogs/GetRegistrationOptions.cs`
- Create: `HealthExam.Application/Catalogs/ListProvinces.cs`
- Create: `HealthExam.Application/Catalogs/ListWards.cs`
- Create: `HealthExam.Application/Catalogs/ListServiceCategories.cs`
- Create: `HealthExam.Application/Catalogs/ListServiceGroups.cs`
- Create: `HealthExam.Application/Catalogs/ListServices.cs`
- Create: `HealthExam.Infrastructure/Persistence/Repositories/CatalogRepository.cs`
- Move API contracts: `HealthExam.Server/Models/CatalogModels.cs` to `HealthExam.API/Contracts/CatalogModels.cs`
- Move API contracts: `HealthExam.Server/Models/MasterDataModels.cs` to `HealthExam.API/Contracts/MasterDataModels.cs`
- Create: `HealthExam.Tests/Application/CatalogHandlerTests.cs`
- Modify: `HealthExam.API/Controllers/CatalogController.cs`
- Modify: `HealthExam.API/Controllers/MasterDataController.cs`
- Modify: `HealthExam.API/Controllers/ServiceCatalogController.cs`
- Modify: `HealthExam.API/Program.cs`
- Delete after cutover: `HealthExam.Server/Service/CatalogService.cs`
- Delete after cutover: `HealthExam.Server/Service/MasterDataService.cs`
- Delete after cutover: `HealthExam.Server/Service/ParaclinicalCatalogService.cs`

**Interfaces:**
- Consumes: `ApplicationResult<T>` and Domain catalog entities
- Produces: nine dedicated handler interfaces and `ICatalogRepository`; proves Controller -> Application -> Infrastructure end-to-end

- [ ] **Step 1: Write failing handler orchestration tests with a fake repository**

```csharp
public class CatalogHandlerTests
{
    [Fact]
    public async Task ListOrganizations_normalizes_paging_and_forwards_division()
    {
        var repo = new FakeCatalogRepository();
        var handler = new ListOrganizationsHandler(repo);

        var result = await handler.HandleAsync(
            new ListOrganizationsQuery("D01", "abc", true, 0, 999));

        Assert.True(result.IsSuccess);
        Assert.Equal((1, 200, "D01"), repo.LastOrganizationRequest);
    }
}
```

- [ ] **Step 2: Run the new test and verify it fails**

Run: `dotnet test HealthExam.Tests/HealthExam.Tests.csproj --filter FullyQualifiedName~CatalogHandlerTests`

Expected: FAIL because the catalog Application types do not exist.

- [ ] **Step 3: Define handlers and repository, then move existing query behavior**

```csharp
public interface ICatalogRepository
{
    Task<PageResult<OrganizationResult>> ListOrganizationsAsync(
        string divisionId, string keyword, bool? isActive, int page, int size,
        CancellationToken ct = default);
    Task<PageResult<ExamPackageResult>> ListExamPackagesAsync(
        string divisionId, string keyword, string variantCode, bool? isActive,
        int page, int size, CancellationToken ct = default);
    Task<RegistrationOptionsResult> GetRegistrationOptionsAsync(
        string divisionId, CancellationToken ct = default);
    Task<IReadOnlyList<MasterDataOptionResult>> ListProvincesAsync(
        string divisionId, string keyword, CancellationToken ct = default);
    Task<IReadOnlyList<MasterDataOptionResult>> ListWardsAsync(
        string divisionId, string provinceCode, string keyword,
        CancellationToken ct = default);
    Task<IReadOnlyList<ServiceCategoryResult>> ListServiceCategoriesAsync(
        string divisionId, CancellationToken ct = default);
    Task<IReadOnlyList<ServiceGroupResult>> ListServiceGroupsAsync(
        string divisionId, string categoryCode, CancellationToken ct = default);
    Task<PageResult<ServiceCatalogResult>> ListServicesAsync(
        string divisionId, ServiceCatalogFilter filter,
        CancellationToken ct = default);
}
```

Each use-case file defines a dedicated `I<Name>Handler` with `HandleAsync`. Copy predicates, ordering, static option lists, paging defaults (`1`, `20`) and max size (`200`) exactly from the three legacy services. Controllers map Application results back to existing API DTOs.

- [ ] **Step 4: Run catalog Application and API tests**

Run: `dotnet test HealthExam.Tests/HealthExam.Tests.csproj --filter "FullyQualifiedName~CatalogHandlerTests|FullyQualifiedName~ExamGroupTests|FullyQualifiedName~MasterDataTests|FullyQualifiedName~MasterDataEndpointTests"`

Expected: PASS.

- [ ] **Step 5: Commit the walking skeleton**

```bash
git add HealthExam.Application/Catalogs HealthExam.Infrastructure/Persistence/Repositories/CatalogRepository.cs HealthExam.API HealthExam.Tests HealthExam.Server/Service
git commit -m "refactor: migrate catalog and master data handlers"
```

---

### Task 5: Move Exam Session rules into Domain and migrate session use cases

**Files:**
- Modify: `HealthExam.Domain/ExamSessions/ExamSession.cs`
- Create: `HealthExam.Application/ExamSessions/ExamSessionModels.cs`
- Create: `HealthExam.Application/ExamSessions/IExamSessionRepository.cs`
- Create: `HealthExam.Application/ExamSessions/ListExamSessions.cs`
- Create: `HealthExam.Application/ExamSessions/GetExamSession.cs`
- Create: `HealthExam.Application/ExamSessions/CreateExamSession.cs`
- Create: `HealthExam.Application/ExamSessions/UpdateExamSession.cs`
- Create: `HealthExam.Application/ExamSessions/CloseExamSession.cs`
- Create: `HealthExam.Application/ExamSessions/ReopenExamSession.cs`
- Create: `HealthExam.Infrastructure/Persistence/Repositories/ExamSessionRepository.cs`
- Move API contracts: `HealthExam.Server/Models/ExamSessionModels.cs` to `HealthExam.API/Contracts/ExamSessionModels.cs`
- Create: `HealthExam.Tests/Domain/ExamSessionDomainTests.cs`
- Create: `HealthExam.Tests/Application/ExamSessionHandlerTests.cs`
- Modify: `HealthExam.API/Controllers/ExamSessionController.cs`
- Delete after cutover: `HealthExam.Server/Service/ExamSessionService.cs`

**Interfaces:**
- Consumes: `IUnitOfWork`, `IAuditRepository`, `ApplicationResult<T>`
- Produces: `ExamSession.Close`, `ExamSession.Reopen`, six handler interfaces, and `IExamSessionRepository`

- [ ] **Step 1: Write failing Domain transition tests**

```csharp
public class ExamSessionDomainTests
{
    [Fact]
    public void Close_open_session_changes_state_to_closed()
    {
        var session = ExamSessionTestData.Open();
        var result = session.Close();
        Assert.True(result.IsSuccess);
        Assert.Equal(ExamSessionState.Closed, session.State);
    }

    [Fact]
    public void Close_cancelled_session_returns_invalid_transition()
    {
        var session = ExamSessionTestData.Cancelled();
        var result = session.Close();
        Assert.Equal(DomainFailureCode.InvalidTransition, result.Failure.Code);
    }
}
```

Add tests for closing an already closed session, reopening a non-closed session, and reopening a closed session. Match current messages asserted by `ExamSessionCloseReopenTests`.

- [ ] **Step 2: Run Domain tests and verify they fail**

Run: `dotnet test HealthExam.Tests/HealthExam.Tests.csproj --filter FullyQualifiedName~ExamSessionDomainTests`

Expected: FAIL because `Close()` and `Reopen()` do not exist on the Domain entity.

- [ ] **Step 3: Implement rules, repository, and dedicated handlers**

```csharp
public interface IExamSessionRepository
{
    Task<PageResult<ExamSessionResult>> ListAsync(
        string divisionId, ExamSessionFilter filter, CancellationToken ct = default);
    Task<ExamSession> GetAsync(
        string divisionId, Guid sessionId, bool forUpdate,
        CancellationToken ct = default);
    void Add(ExamSession session);
}

public interface ICloseExamSessionHandler
{
    Task<ApplicationResult<ExamSessionResult>> HandleAsync(
        CloseExamSessionCommand command, CancellationToken ct = default);
}
```

Move close/reopen decisions into Domain. Keep existence/ownership checks, repository calls, audit creation, save, transaction, and DTO mapping in handlers. Copy CRUD validation, filters, ordering, and messages exactly from `ExamSessionService`.

- [ ] **Step 4: Run Domain, Application, and existing API tests**

Run: `dotnet test HealthExam.Tests/HealthExam.Tests.csproj --filter "FullyQualifiedName~ExamSessionDomainTests|FullyQualifiedName~ExamSessionHandlerTests|FullyQualifiedName~ExamSessionCloseReopenTests"`

Expected: PASS.

- [ ] **Step 5: Commit the session slice**

```bash
git add HealthExam.Domain/ExamSessions HealthExam.Application/ExamSessions HealthExam.Infrastructure/Persistence/Repositories/ExamSessionRepository.cs HealthExam.API/Controllers/ExamSessionController.cs HealthExam.Tests HealthExam.Server/Service/ExamSessionService.cs
git commit -m "refactor: migrate exam session use cases"
```

---

### Task 6: Move Exam Record rules and migrate record use cases

**Files:**
- Modify: `HealthExam.Domain/ExamRecords/ExamRecord.cs`
- Create: `HealthExam.Application/ExamRecords/ExamRecordModels.cs`
- Create: `HealthExam.Application/ExamRecords/IExamRecordRepository.cs`
- Create: `HealthExam.Application/ExamRecords/IRecordCodeAllocator.cs`
- Create: `HealthExam.Application/ExamRecords/ListExamRecords.cs`
- Create: `HealthExam.Application/ExamRecords/GetExamRecord.cs`
- Create: `HealthExam.Application/ExamRecords/CreateExamRecord.cs`
- Create: `HealthExam.Application/ExamRecords/UpdateExamRecord.cs`
- Create: `HealthExam.Application/ExamRecords/ConfirmExamRecord.cs`
- Create: `HealthExam.Application/ExamRecords/CancelExamRecord.cs`
- Create: `HealthExam.Application/ExamRecords/VerifyPortalCredentials.cs`
- Create: `HealthExam.Application/ExamRecords/GetExamSessionProgress.cs`
- Create: `HealthExam.Infrastructure/Persistence/Repositories/ExamRecordRepository.cs`
- Create: `HealthExam.Infrastructure/Persistence/RecordCodeAllocator.cs`
- Move API contracts: `HealthExam.Server/Models/ExamRecordModels.cs` to `HealthExam.API/Contracts/ExamRecordModels.cs`
- Move API contracts: `HealthExam.Server/Models/ExamRecordFilterModels.cs` to `HealthExam.API/Contracts/ExamRecordFilterModels.cs`
- Split portal API contracts from `HealthExam.Server/Models/InternalModels.cs` into `HealthExam.API/Contracts/InternalModels.cs`
- Create: `HealthExam.Tests/Domain/ExamRecordDomainTests.cs`
- Create: `HealthExam.Tests/Application/ExamRecordHandlerTests.cs`
- Create: `HealthExam.Tests/Infrastructure/ExamRecordConcurrencyTests.cs`
- Modify: `HealthExam.API/Controllers/ExamRecordController.cs`
- Modify: `HealthExam.API/Controllers/ExamSessionController.cs`
- Modify: `HealthExam.API/Controllers/InternalController.cs`
- Delete after cutover: `HealthExam.Server/Service/ExamRecordService.cs`
- Delete after cutover: `HealthExam.Server/Service/ExamSessionProgressService.cs`
- Delete after cutover: `HealthExam.Server/Service/PortalCredentialService.cs`
- Delete after cutover: `HealthExam.Server/Service/OrderNoAllocator.cs` record-code implementation

**Interfaces:**
- Consumes: session repository, audit repository, unit of work, clock
- Produces: ExamRecord transition methods, eight handlers, specialized record queries, and provider-specific record-code allocation hidden in Infrastructure

- [ ] **Step 1: Write failing record transition and handler tests**

```csharp
[Theory]
[InlineData(ExamRecordState.NotRegistered, ExamRecordState.RegistrationCancelled)]
[InlineData(ExamRecordState.Waiting, ExamRecordState.ExamCancelled)]
[InlineData(ExamRecordState.InProgress, ExamRecordState.ExamCancelled)]
public void Cancel_valid_state_applies_expected_target(
    ExamRecordState source, ExamRecordState target)
{
    var record = ExamRecordTestData.WithState(source);
    var result = record.Cancel("lý do");
    Assert.True(result.IsSuccess);
    Assert.Equal(target, record.State);
}
```

Also test confirm `NotRegistered -> Waiting`, rejected confirm/cancel states, empty cancellation reason, duplicate-in-session mapping, and handler rollback when allocation/save fails.

- [ ] **Step 2: Run the new tests and verify they fail**

Run: `dotnet test HealthExam.Tests/HealthExam.Tests.csproj --filter "FullyQualifiedName~ExamRecordDomainTests|FullyQualifiedName~ExamRecordHandlerTests"`

Expected: FAIL because record transition methods and handlers do not exist.

- [ ] **Step 3: Implement Domain rules and Application repository contracts**

```csharp
public interface IExamRecordRepository
{
    Task<PageResult<ExamRecordResult>> ListAsync(
        string divisionId, ExamRecordFilter filter, CancellationToken ct = default);
    Task<ExamRecord> GetAsync(
        string divisionId, Guid recordId, bool forUpdate,
        CancellationToken ct = default);
    Task<bool> ExistsInSessionAsync(
        string divisionId, Guid sessionId, string patientCode,
        Guid? excludingRecordId, CancellationToken ct = default);
    Task<ExamSession> GetOrCreateDefaultSessionAsync(
        string divisionId, CancellationToken ct = default);
    void Add(ExamRecord record);
}

public interface IRecordCodeAllocator
{
    Task<string> NextAsync(CancellationToken ct = default);
}
```

Handlers preserve every filter, inclusive date boundary, newest-first ordering, default-session behavior, race handling, `ChangeTracker.Clear()` equivalent through `DiscardPendingChanges`, record-code format, audit content, and failure message from the legacy service.

- [ ] **Step 4: Implement PostgreSQL adapter and run real concurrency tests**

Require the database variable before the test command:

```bash
test -n "$HEALTHEXAM_TEST_DB"
dotnet test HealthExam.Tests/HealthExam.Tests.csproj --filter "FullyQualifiedName~ExamRecordConcurrencyTests|FullyQualifiedName~ExamRecordAllocationPostgresTests|FullyQualifiedName~ExamDefaultSessionPostgresTests|FullyQualifiedName~ExamRecordListFilterPostgresTests"
```

Expected: PASS using separate PostgreSQL connections for competing operations. Sequence values remain consumed after rollback, and duplicate records return `DuplicateInSession` rather than a provider exception.

- [ ] **Step 5: Run record Domain/Application/API tests and commit**

Run: `dotnet test HealthExam.Tests/HealthExam.Tests.csproj --filter "FullyQualifiedName~ExamRecord|FullyQualifiedName~PortalCredential|FullyQualifiedName~SessionProgress"`

Expected: PASS.

```bash
git add HealthExam.Domain/ExamRecords HealthExam.Application/ExamRecords HealthExam.Infrastructure/Persistence HealthExam.API/Controllers HealthExam.Tests HealthExam.Server/Service
git commit -m "refactor: migrate exam record use cases"
```

---

### Task 7: Move Import lifecycle and Application handlers

**Files:**
- Modify: `HealthExam.Domain/Imports/ImportBatch.cs`
- Create: `HealthExam.Application/Imports/ImportModels.cs`
- Create: `HealthExam.Application/Imports/IImportRepository.cs`
- Create: `HealthExam.Application/Imports/IExamWorkbookReader.cs`
- Create: `HealthExam.Application/Imports/DownloadImportTemplate.cs`
- Create: `HealthExam.Application/Imports/UploadImport.cs`
- Create: `HealthExam.Application/Imports/GetImport.cs`
- Create: `HealthExam.Application/Imports/ListImportErrors.cs`
- Create: `HealthExam.Application/Imports/CommitImport.cs`
- Create: `HealthExam.Application/Imports/DiscardImport.cs`
- Create: `HealthExam.Tests/Domain/ImportBatchDomainTests.cs`
- Create: `HealthExam.Tests/Application/ImportHandlerTests.cs`

**Interfaces:**
- Consumes: record/session repositories, unit of work, clock
- Produces: ImportBatch transition methods, six handler interfaces, workbook reader, and specialized import persistence operations

- [ ] **Step 1: Write failing lifecycle and idempotency tests**

```csharp
public class ImportBatchDomainTests
{
    [Fact]
    public void Completed_batch_commit_is_idempotent()
    {
        var batch = ImportBatchTestData.Completed();
        var result = batch.BeginCommit(DateTime.UtcNow);
        Assert.Equal(DomainOperationOutcome.NoOp, result.Outcome);
        Assert.Equal(ImportBatchState.Completed, batch.State);
    }

    [Fact]
    public void Discarded_batch_cannot_commit()
    {
        var batch = ImportBatchTestData.Discarded();
        var result = batch.BeginCommit(DateTime.UtcNow);
        Assert.Equal(DomainFailureCode.InvalidTransition, result.Failure.Code);
    }
}
```

Cover active/stale `Committing`, `Completed`, `Discarded`, and discard transitions using the existing stale threshold.

- [ ] **Step 2: Run the new import tests and verify they fail**

Run: `dotnet test HealthExam.Tests/HealthExam.Tests.csproj --filter "FullyQualifiedName~ImportBatchDomainTests|FullyQualifiedName~ImportHandlerTests"`

Expected: FAIL because the lifecycle methods and handlers do not exist.

- [ ] **Step 3: Implement Domain lifecycle and handler interfaces**

```csharp
public interface IImportRepository
{
    Task<ImportBatch> GetAsync(
        string divisionId, Guid batchId, bool forUpdate,
        CancellationToken ct = default);
    Task<bool> TryClaimForCommitAsync(
        string divisionId, Guid batchId, DateTime staleBeforeUtc,
        CancellationToken ct = default);
    Task<IReadOnlyList<ImportRowData>> ListRowsAsync(
        Guid batchId, CancellationToken ct = default);
    Task<PageResult<ImportErrorResult>> ListErrorsAsync(
        string divisionId, Guid batchId, int page, int size,
        CancellationToken ct = default);
    Task BulkCreateRecordsAsync(
        IReadOnlyList<ImportRecordWrite> records,
        CancellationToken ct = default);
    void Add(ImportBatch batch);
}
```

`CommitImportHandler` keeps the two-phase workflow and calls the record allocator/repository through Application interfaces. Domain decides legal lifecycle transitions; Application decides the orchestration and row-level failure mapping.

- [ ] **Step 4: Run Domain and Application import tests**

Run: `dotnet test HealthExam.Tests/HealthExam.Tests.csproj --filter "FullyQualifiedName~ImportBatchDomainTests|FullyQualifiedName~ImportHandlerTests"`

Expected: PASS.

- [ ] **Step 5: Commit the Domain/Application half of Import**

```bash
git add HealthExam.Domain/Imports HealthExam.Application/Imports HealthExam.Tests/Domain HealthExam.Tests/Application
git commit -m "refactor: add import domain rules and handlers"
```

---

### Task 8: Move EPPlus and PostgreSQL Import implementations, then cut over the API

**Files:**
- Move: `HealthExam.Server/Service/ExamImportColumns.cs` to `HealthExam.Infrastructure/Excel/ExamImportColumns.cs`
- Move: `HealthExam.Server/Service/ExamImportSheet.cs` to `HealthExam.Infrastructure/Excel/ExamImportSheet.cs`
- Move: `HealthExam.Server/Service/ExamImportWorkbook.cs` to `HealthExam.Infrastructure/Excel/ExamWorkbookReader.cs`
- Create: `HealthExam.Infrastructure/Persistence/Repositories/ImportRepository.cs`
- Move API contracts: `HealthExam.Server/Models/ImportModels.cs` to `HealthExam.API/Contracts/ImportModels.cs`
- Create: `HealthExam.Tests/Infrastructure/ImportPersistenceTests.cs`
- Modify: `HealthExam.API/Controllers/ExamImportController.cs`
- Modify: `HealthExam.API/Program.cs`
- Delete after cutover: `HealthExam.Server/Service/ExamImportService.cs`

**Interfaces:**
- Consumes: Task 7 import interfaces
- Produces: EPPlus workbook adapter, PostgreSQL claim/bulk/savepoint implementation, and import endpoints backed only by handlers

- [ ] **Step 1: Write failing real-PostgreSQL tests for claim, savepoint, and rollback**

```csharp
[Fact]
public async Task Competing_commit_claims_allow_one_owner()
{
    var batchId = await SeedPendingBatchAsync();
    var claims = await Task.WhenAll(
        ClaimFromNewScopeAsync(batchId),
        ClaimFromNewScopeAsync(batchId));
    Assert.Equal(1, claims.Count(x => x));
}
```

Add a test proving row failure rolls back to the savepoint without losing prior valid rows and a test proving outer transaction rollback leaves the batch uncommitted.

- [ ] **Step 2: Run Infrastructure import tests and verify they fail**

Run: `test -n "$HEALTHEXAM_TEST_DB" && dotnet test HealthExam.Tests/HealthExam.Tests.csproj --filter FullyQualifiedName~ImportPersistenceTests`

Expected: FAIL because `ImportRepository` is not implemented.

- [ ] **Step 3: Implement adapters and preserve the wire/file behavior**

Use EPPlus only in `Infrastructure/Excel`. Preserve the current column aliases, required headers, cell conversion, warning/error codes, template contents, row numbering, and workbook disposal behavior. Implement commit claiming with the same conditional PostgreSQL update, savepoints with the EF/Npgsql transaction, and bulk writes with the current SQL semantics.

```csharp
public sealed class ExamWorkbookReader : IExamWorkbookReader
{
    public Task<WorkbookReadResult> ReadAsync(
        Stream content, string fileName, CancellationToken ct = default);
    public Task<byte[]> CreateTemplateAsync(CancellationToken ct = default);
}
```

- [ ] **Step 4: Cut over controller and run all Import tests**

Run: `test -n "$HEALTHEXAM_TEST_DB" && dotnet test HealthExam.Tests/HealthExam.Tests.csproj --filter "FullyQualifiedName~ExamImportTests|FullyQualifiedName~ImportBatchDomainTests|FullyQualifiedName~ImportHandlerTests|FullyQualifiedName~ImportPersistenceTests"`

Expected: PASS with no EPPlus reference in Application.

- [ ] **Step 5: Commit the Import slice**

```bash
git add HealthExam.Infrastructure/Excel HealthExam.Infrastructure/Persistence/Repositories/ImportRepository.cs HealthExam.API HealthExam.Tests HealthExam.Server/Service
git commit -m "refactor: migrate import infrastructure and endpoints"
```

---

### Task 9: Migrate Paraclinical Domain rules and use cases

**Files:**
- Modify: `HealthExam.Domain/Paraclinical/ParaclinicalOrder.cs`
- Modify: `HealthExam.Domain/Paraclinical/ParaclinicalOrderItem.cs`
- Create: `HealthExam.Application/Paraclinical/ParaclinicalModels.cs`
- Create: `HealthExam.Application/Paraclinical/IParaclinicalRepository.cs`
- Create: `HealthExam.Application/Paraclinical/GetRecordOrders.cs`
- Create: `HealthExam.Application/Paraclinical/CreateOrders.cs`
- Create: `HealthExam.Application/Paraclinical/CreateOrdersFromPackage.cs`
- Create: `HealthExam.Application/Paraclinical/GetOrder.cs`
- Create: `HealthExam.Application/Paraclinical/CancelOrder.cs`
- Create: `HealthExam.Application/Paraclinical/ChangeOrderState.cs`
- Create: `HealthExam.Application/Paraclinical/GetConclusionEligibility.cs`
- Create: `HealthExam.Application/Paraclinical/SignConclusion.cs`
- Create: `HealthExam.Infrastructure/Persistence/Repositories/ParaclinicalRepository.cs`
- Move API contracts: `HealthExam.Server/Models/ParaclinicalModels.cs` to `HealthExam.API/Contracts/ParaclinicalModels.cs`
- Create: `HealthExam.Tests/Domain/ParaclinicalOrderDomainTests.cs`
- Create: `HealthExam.Tests/Application/ParaclinicalHandlerTests.cs`
- Modify: `HealthExam.API/Controllers/ExamRecordController.cs`
- Modify: `HealthExam.API/Controllers/ParaclinicalOrderController.cs`
- Delete after cutover: `HealthExam.Server/Service/ParaclinicalOrderService.cs`
- Delete after cutover: `HealthExam.Server/Service/ParaclinicalStateMachine.cs`
- Delete after cutover: `HealthExam.Server/Service/ConclusionService.cs`

**Interfaces:**
- Consumes: record repository, audit repository, unit of work
- Produces: the item state machine inside Domain, eight dedicated handlers, and specialized order persistence

- [ ] **Step 1: Port the existing state-machine tests to Domain**

```csharp
[Theory]
[InlineData(ParaclinicalItemState.Ordered, ParaclinicalItemState.Received)]
[InlineData(ParaclinicalItemState.Received, ParaclinicalItemState.Processing)]
[InlineData(ParaclinicalItemState.Processing, ParaclinicalItemState.Done)]
public void Manual_transition_applies_allowed_path(
    ParaclinicalItemState source, ParaclinicalItemState target)
{
    var item = ParaclinicalTestData.Item(source);
    var result = item.TransitionTo(target, ParaclinicalStateSource.Manual);
    Assert.True(result.Applied);
    Assert.Equal(target, item.State);
}
```

Port all current rejected/no-op/idempotent/cancel/condition-B assertions from `ParaclinicalOrderTests`, `ParaclinicalScanResultTests`, and `ConclusionEligibilityTests` without changing expected outcomes.

- [ ] **Step 2: Run Domain tests and verify missing methods fail**

Run: `dotnet test HealthExam.Tests/HealthExam.Tests.csproj --filter FullyQualifiedName~ParaclinicalOrderDomainTests`

Expected: FAIL because transitions still live in the legacy service class.

- [ ] **Step 3: Implement aggregate methods, handlers, and repository**

```csharp
public interface IParaclinicalRepository
{
    Task<ParaclinicalOrder> GetAsync(
        string divisionId, Guid orderId, bool includeItems, bool forUpdate,
        CancellationToken ct = default);
    Task<IReadOnlyList<ParaclinicalOrder>> ListByRecordAsync(
        string divisionId, Guid recordId, CancellationToken ct = default);
    Task<IReadOnlyList<ExamPackageService>> ListActivePackageServicesAsync(
        string divisionId, Guid packageId, CancellationToken ct = default);
    void Add(ParaclinicalOrder order);
}
```

Domain performs every item/order transition and conclusion invariant. Handlers perform ownership checks, package lookup, orchestration, audit, outbox request, transaction, and result mapping.

- [ ] **Step 4: Run Domain, Application, and endpoint tests**

Run: `dotnet test HealthExam.Tests/HealthExam.Tests.csproj --filter "FullyQualifiedName~Paraclinical|FullyQualifiedName~ConclusionEligibility"`

Expected: PASS.

- [ ] **Step 5: Commit the Paraclinical core slice**

```bash
git add HealthExam.Domain/Paraclinical HealthExam.Application/Paraclinical HealthExam.Infrastructure/Persistence/Repositories/ParaclinicalRepository.cs HealthExam.API/Controllers HealthExam.Tests HealthExam.Server/Service
git commit -m "refactor: migrate paraclinical domain and use cases"
```

---

### Task 10: Migrate RIS, vendor callback, Integration Outbox, and worker

**Files:**
- Modify: `HealthExam.Domain/Integrations/IntegrationOutbox.cs`
- Create: `HealthExam.Application/Integrations/IRisClient.cs`
- Create: `HealthExam.Application/Paraclinical/IIntegrationOutboxRepository.cs`
- Create: `HealthExam.Application/Paraclinical/DispatchOrder.cs`
- Create: `HealthExam.Application/Paraclinical/UpdateVendorStatus.cs`
- Create: `HealthExam.Application/Paraclinical/ProcessOutboxBatch.cs`
- Move: `HealthExam.Server/Service/RisClient.cs` to `HealthExam.Infrastructure/Integrations/Ris/RisClient.cs`
- Move: `HealthExam.Server/Service/RisOrderPayloadBuilder.cs` to `HealthExam.Infrastructure/Integrations/Ris/RisOrderPayloadBuilder.cs`
- Move: `HealthExam.Server/Service/VendorClock.cs` to `HealthExam.Infrastructure/Integrations/Ris/VendorClock.cs`
- Split: `HealthExam.Server/Models/VendorIntegrationModels.cs` into `HealthExam.API/Contracts/VendorIntegrationModels.cs` for inbound/outbound API DTOs and `HealthExam.Infrastructure/Integrations/Ris/RisWireModels.cs` for Newtonsoft wire DTOs
- Create: `HealthExam.Application/Integrations/RisContracts.cs` for the pure request/result types used by `IRisClient`
- Create: `HealthExam.Infrastructure/Persistence/Repositories/IntegrationOutboxRepository.cs`
- Move: `HealthExam.Server/Service/IntegrationOutboxWorker.cs` to `HealthExam.Infrastructure/BackgroundJobs/IntegrationOutboxWorker.cs`
- Create: `HealthExam.Tests/Application/OutboxHandlerTests.cs`
- Create: `HealthExam.Tests/Infrastructure/OutboxQueueIntegrationTests.cs`
- Modify: `HealthExam.API/Controllers/ParaclinicalOrderController.cs`
- Modify: `HealthExam.API/Controllers/VendorIntegrationController.cs`
- Delete after cutover: `HealthExam.Server/Service/IntegrationOutboxService.cs`
- Delete after cutover: `HealthExam.Server/Service/VendorStatusService.cs`

**Interfaces:**
- Consumes: Paraclinical aggregate/repository and unit of work
- Produces: RIS client abstraction, dispatch/vendor-status handlers, batch processor, `SKIP LOCKED` outbox repository, and scheduling-only hosted worker

- [ ] **Step 1: Write failing outbox lifecycle and orchestration tests**

```csharp
[Fact]
public async Task Failed_send_schedules_retry_without_losing_row()
{
    var repo = new FakeOutboxRepository(OutboxTestData.Pending());
    var client = new FakeRisClient(RisSendResult.DependencyFailure("timeout"));
    var handler = new ProcessOutboxBatchHandler(repo, client, new FakeClock());

    var result = await handler.HandleAsync(new ProcessOutboxBatchCommand(1));

    Assert.True(result.IsSuccess);
    Assert.Equal(OutboxState.Failed, repo.Row.State);
    Assert.NotNull(repo.Row.NextAttemptAt);
}
```

Cover sent, retry, dead-letter, stale claim, duplicate dispatch, and cancellation invariants currently asserted by existing outbox tests.

- [ ] **Step 2: Run Application tests and verify they fail**

Run: `dotnet test HealthExam.Tests/HealthExam.Tests.csproj --filter FullyQualifiedName~OutboxHandlerTests`

Expected: FAIL because the new outbox handler and client interface do not exist.

- [ ] **Step 3: Implement Application flow and Infrastructure adapters**

```csharp
public interface IIntegrationOutboxRepository
{
    Task<IntegrationOutbox> ClaimNextAsync(
        DateTime nowUtc, CancellationToken ct = default);
    Task<IntegrationOutbox> LockAsync(
        long outboxId, CancellationToken ct = default);
    Task<bool> WasSentAsync(
        Guid orderId, string messageType, CancellationToken ct = default);
    void Add(IntegrationOutbox row);
}

public enum RisSendOutcome
{
    Sent,
    DependencyFailure,
    Rejected
}

public sealed record RisSendResult(
    RisSendOutcome Outcome, string Message, object Payload = null);

public interface IRisClient
{
    Task<RisSendResult> SendOrderAsync(
        RisOrderRequest request, CancellationToken ct = default);
}
```

The hosted worker injects `IProcessOutboxBatchHandler`, creates a scope each loop, and contains no state transitions. Keep existing batch size, retry delays, maximum attempts, dead-letter behavior, wire payload, headers, timestamp formatting, and credential handling.

- [ ] **Step 4: Run real PostgreSQL queue tests and API contract tests**

Run: `test -n "$HEALTHEXAM_TEST_DB" && dotnet test HealthExam.Tests/HealthExam.Tests.csproj --filter "FullyQualifiedName~Outbox|FullyQualifiedName~DispatchEndpointTests|FullyQualifiedName~RisWireContractTests|FullyQualifiedName~Vendor"`

Expected: PASS; concurrent claims return different rows or one null result, never the same claimed row.

- [ ] **Step 5: Commit vendor/outbox migration**

```bash
git add HealthExam.Domain/Integrations HealthExam.Application/Integrations HealthExam.Application/Paraclinical HealthExam.Infrastructure/Integrations/Ris HealthExam.Infrastructure/Persistence/Repositories/IntegrationOutboxRepository.cs HealthExam.Infrastructure/BackgroundJobs HealthExam.API HealthExam.Tests HealthExam.Server/Service
git commit -m "refactor: migrate vendor integration and outbox worker"
```

---

### Task 11: Migrate Form Server draft, webhook, and reconciliation workflows

**Files:**
- Modify: `HealthExam.Domain/Webhooks/WebhookInbox.cs`
- Create: `HealthExam.Application/Integrations/IFormServerClient.cs`
- Create: `HealthExam.Application/ExamRecords/GetExamFormDraft.cs`
- Create: `HealthExam.Application/Webhooks/IWebhookInboxRepository.cs`
- Create: `HealthExam.Application/Webhooks/IngestWebhook.cs`
- Create: `HealthExam.Application/Webhooks/ProcessWebhookBatch.cs`
- Create: `HealthExam.Application/Webhooks/RequeueWebhooks.cs`
- Create: `HealthExam.Application/Webhooks/BackfillScanResults.cs`
- Create: `HealthExam.Application/Webhooks/GetWebhookMetrics.cs`
- Create: `HealthExam.Application/Webhooks/ReconcileProgress.cs`
- Move: `HealthExam.Server/Service/FormServerClient.cs` to `HealthExam.Infrastructure/Integrations/FormServer/FormServerClient.cs`
- Move wire DTOs: `HealthExam.Server/Models/FormServerModels.cs` to `HealthExam.Infrastructure/Integrations/FormServer/FormServerWireModels.cs`
- Split: `HealthExam.Server/Models/WebhookModels.cs` into `HealthExam.API/Contracts/WebhookModels.cs` for HTTP DTOs and `HealthExam.Application/Webhooks/WebhookModels.cs` for pure commands/results
- Create: `HealthExam.Infrastructure/Persistence/Repositories/WebhookInboxRepository.cs`
- Move: `HealthExam.Server/Service/WebhookWorker.cs` to `HealthExam.Infrastructure/BackgroundJobs/WebhookWorker.cs`
- Move: `HealthExam.Server/Service/ProgressReconciliationWorker.cs` to `HealthExam.Infrastructure/BackgroundJobs/ProgressReconciliationWorker.cs`
- Create: `HealthExam.Tests/Application/WebhookHandlerTests.cs`
- Create: `HealthExam.Tests/Infrastructure/WebhookQueueIntegrationTests.cs`
- Modify: `HealthExam.API/Controllers/ExamRecordController.cs`
- Modify: `HealthExam.API/Controllers/HooksController.cs`
- Modify: `HealthExam.API/Controllers/InternalController.cs`
- Delete after cutover: `HealthExam.Server/Service/ExamFormDraftService.cs`
- Delete after cutover: `HealthExam.Server/Service/WebhookIngestService.cs`
- Delete after cutover: `HealthExam.Server/Service/WebhookProcessor.cs`
- Delete after cutover: `HealthExam.Server/Service/WebhookMetrics.cs`
- Delete after cutover: `HealthExam.Server/Service/ProgressReconciliationService.cs`

**Interfaces:**
- Consumes: record/paraclinical repositories, unit of work, clock
- Produces: Form Server client contract, form-draft handler, webhook/reconciliation handlers, `SKIP LOCKED` inbox repository, and scheduling-only workers

- [ ] **Step 1: Write failing Domain/Application webhook tests**

```csharp
[Fact]
public async Task Duplicate_event_returns_success_ack_without_second_insert()
{
    var repo = new FakeWebhookInboxRepository { EventAlreadyExists = true };
    var handler = new IngestWebhookHandler(repo, new FakeUnitOfWork());

    var result = await handler.HandleAsync(WebhookTestData.ValidCommand());

    Assert.True(result.IsSuccess);
    Assert.True(result.Value.Duplicated);
    Assert.Empty(repo.AddedRows);
}
```

Cover unknown-event skip, duplicate, stale event, invalid transition, retry/dead-letter, requeue, scan-result backfill, and reconciliation outcomes using the assertions from current webhook/reconciliation tests.

- [ ] **Step 2: Run new tests and verify they fail**

Run: `dotnet test HealthExam.Tests/HealthExam.Tests.csproj --filter FullyQualifiedName~WebhookHandlerTests`

Expected: FAIL because the handlers and inbox repository do not exist.

- [ ] **Step 3: Implement workflow contracts and adapters**

```csharp
public interface IWebhookInboxRepository
{
    Task<bool> ExistsAsync(string eventId, CancellationToken ct = default);
    Task<WebhookInbox> ClaimNextAsync(DateTime nowUtc, CancellationToken ct = default);
    Task<WebhookInbox> LockAsync(long inboxId, CancellationToken ct = default);
    Task<IReadOnlyList<WebhookInbox>> FindForRequeueAsync(
        WebhookRequeueFilter filter, CancellationToken ct = default);
    void Add(WebhookInbox row);
}
```

Keep webhook HMAC/raw-body verification in API. Domain owns record/order/inbox transitions. Application owns event routing, idempotency, audit, metrics decisions, reconciliation, and transaction orchestration. Infrastructure owns HTTP/JSON, row locking, queue claiming, cache implementation, scheduling, and logging.

- [ ] **Step 4: Run PostgreSQL and API contract tests**

Run: `test -n "$HEALTHEXAM_TEST_DB" && dotnet test HealthExam.Tests/HealthExam.Tests.csproj --filter "FullyQualifiedName~Webhook|FullyQualifiedName~ProgressReconciliation|FullyQualifiedName~FormDraft"`

Expected: PASS with unchanged webhook acknowledgements, metrics counts, form draft context values, and `HostRefID = SessionCode` behavior.

- [ ] **Step 5: Commit Form Server workflows**

```bash
git add HealthExam.Domain/Webhooks HealthExam.Application/Integrations HealthExam.Application/ExamRecords HealthExam.Application/Webhooks HealthExam.Infrastructure/Integrations/FormServer HealthExam.Infrastructure/Persistence/Repositories/WebhookInboxRepository.cs HealthExam.Infrastructure/BackgroundJobs HealthExam.API HealthExam.Tests HealthExam.Server/Service
git commit -m "refactor: migrate form server and webhook workflows"
```

---

### Task 12: Migrate HIS EMR use cases, client, and cache

**Files:**
- Create: `HealthExam.Application/Integrations/IHisEmrClient.cs`
- Create: `HealthExam.Application/Integrations/IHisFormDefinitionCache.cs`
- Create: `HealthExam.Application/His/GetHisFormDefinition.cs`
- Create: `HealthExam.Application/His/GetExamRecordHisForm.cs`
- Create: `HealthExam.Application/His/ListHisProcesses.cs`
- Create: `HealthExam.Application/His/GetHisProcess.cs`
- Create: `HealthExam.Application/His/GetSignWorkflow.cs`
- Create: `HealthExam.Application/His/SubmitHisSection.cs`
- Create: `HealthExam.Application/His/SignHisSection.cs`
- Create: `HealthExam.Application/His/CancelHisSectionSignature.cs`
- Move: `HealthExam.Server/Service/HisEmrClient.cs` to `HealthExam.Infrastructure/Integrations/HisEmr/HisEmrClient.cs`
- Move: `HealthExam.Server/Service/HisEmrOptions.cs` to `HealthExam.Infrastructure/Integrations/HisEmr/HisEmrOptions.cs`
- Split: `HealthExam.Server/Models/HisEmrModels.cs` into `HealthExam.API/Contracts/HisEmrModels.cs`, `HealthExam.Application/His/HisModels.cs`, and `HealthExam.Infrastructure/Integrations/HisEmr/HisEmrWireModels.cs`; Newtonsoft types remain only in API/Infrastructure
- Create: `HealthExam.Infrastructure/Caching/HisFormDefinitionCache.cs`
- Create: `HealthExam.Tests/Application/HisHandlerTests.cs`
- Modify: `HealthExam.API/Controllers/HisFormController.cs`
- Modify: `HealthExam.API/Controllers/ExamRecordHisFormController.cs`
- Delete after cutover: `HealthExam.Server/Service/HisFormDefinitionService.cs`
- Delete after cutover: `HealthExam.Server/Service/HisExamFormService.cs`

**Interfaces:**
- Consumes: record repository and typed Application failures
- Produces: eight HIS handlers, pure client/cache interfaces, and Infrastructure-only HTTP/JSON/cache implementation

- [ ] **Step 1: Write failing handler tests around ownership and upstream failures**

```csharp
[Fact]
public async Task Sign_rejects_employee_that_differs_from_actor()
{
    var handler = HisHandlerTestData.CreateSignHandler();
    var command = HisHandlerTestData.SignCommand(
        actorId: "E01", employeeId: "E02");

    var result = await handler.HandleAsync(command);

    Assert.Equal(ApplicationFailureCode.Forbidden, result.Failure.Code);
}
```

Cover disabled integration, invalid base URL, missing credential, record/admission/process/section not found, multiple active definitions, timeout, upstream 401/403/404, sign precondition, and successful JSON payload passthrough.

- [ ] **Step 2: Run new handler tests and verify they fail**

Run: `dotnet test HealthExam.Tests/HealthExam.Tests.csproj --filter FullyQualifiedName~HisHandlerTests`

Expected: FAIL because the HIS Application handlers do not exist.

- [ ] **Step 3: Implement pure contracts and Infrastructure mapping**

```csharp
public interface IHisEmrClient
{
    Task<HisClientResult<HisJsonDocument>> SendAsync(
        HisOperation operation, HisRequest request,
        CancellationToken ct = default);
}

public enum HisOperation
{
    ListDefinitions,
    GetDefinition,
    ListProcesses,
    GetProcess,
    GetSignWorkflow,
    SubmitSection,
    SignSection,
    CancelSectionSignature
}

public sealed record HisRequest(
    string RelativePath,
    string Method,
    string Credential,
    string RawJsonBody = null);

public sealed record HisJsonDocument(string RawJson);

public enum HisClientOutcome
{
    Success,
    Unauthorized,
    Forbidden,
    NotFound,
    Timeout,
    BadGateway,
    SignPrecondition
}

public sealed record HisClientResult<T>(
    HisClientOutcome Outcome,
    T Value,
    string Message,
    object Payload = null);

public sealed record HisFormDefinitionResult(
    long DefinitionId,
    string TemplateCode,
    HisJsonDocument Document);

public interface IHisFormDefinitionCache
{
    Task<HisFormDefinitionResult> GetOrCreateAsync(
        string templateCode,
        Func<CancellationToken, Task<HisFormDefinitionResult>> factory,
        CancellationToken ct = default);
}
```

Use `HisJsonDocument.RawJson` in Application contracts; do not expose `JToken`. Infrastructure uses Newtonsoft to preserve vendor wire behavior and converts to/from the raw JSON string at the boundary. API parses that string into the existing response contract. Credentials remain request-scoped and are never logged or cached.

- [ ] **Step 4: Run Application, client, configuration, and endpoint tests**

Run: `dotnet test HealthExam.Tests/HealthExam.Tests.csproj --filter "FullyQualifiedName~HisHandlerTests|FullyQualifiedName~HisEmr|FullyQualifiedName~HisForm|FullyQualifiedName~HisAdmission"`

Expected: PASS with no Newtonsoft, HTTP, options, or cache references in Domain/Application.

- [ ] **Step 5: Commit the HIS slice**

```bash
git add HealthExam.Application/His HealthExam.Application/Integrations HealthExam.Infrastructure/Integrations/HisEmr HealthExam.Infrastructure/Caching HealthExam.API/Controllers HealthExam.Tests HealthExam.Server/Service
git commit -m "refactor: migrate HIS EMR workflows"
```

---

### Task 13: Centralize registrations and make PostgreSQL integration tests mandatory

**Files:**
- Create: `HealthExam.Infrastructure/DependencyInjection.cs`
- Modify: `HealthExam.API/Program.cs`
- Modify: `HealthExam.Tests/PostgresFixture.cs`
- Modify: `HealthExam.Tests/PostgresTestDb.cs`
- Modify: `HealthExam.Tests/HealthExam.Tests.csproj`
- Delete: `HealthExam.Server/Service/DependencyInjection.cs`
- Delete: `HealthExam.Core/EntityFramework/Repositories/DependencyInjection.cs` if still present
- Create: `HealthExam.Tests/API/CompositionRootTests.cs`

**Interfaces:**
- Consumes: every handler and Infrastructure implementation from Tasks 4-12
- Produces: the final composition root, one Infrastructure registration extension, and a hard PostgreSQL test gate

- [ ] **Step 1: Write a failing composition-root test and remove PostgreSQL skips**

```csharp
public class CompositionRootTests
{
    [Fact]
    public void Api_resolves_every_controller()
    {
        using var factory = new AuthTestHost();
        using var scope = factory.Services.CreateScope();
        var controllers = typeof(Program).Assembly.GetTypes()
            .Where(x => !x.IsAbstract && typeof(ControllerBase).IsAssignableFrom(x));

        foreach (var controller in controllers)
            Assert.NotNull(ActivatorUtilities.GetServiceOrCreateInstance(
                scope.ServiceProvider, controller));
    }
}
```

Change the PostgreSQL fixture to throw `InvalidOperationException("HEALTHEXAM_TEST_DB is required for Infrastructure integration tests")` when an Infrastructure integration test is constructed without the variable.

- [ ] **Step 2: Run tests and verify missing registrations fail**

Run: `dotnet test HealthExam.Tests/HealthExam.Tests.csproj --filter FullyQualifiedName~CompositionRootTests`

Expected: FAIL naming the first unresolved handler or repository.

- [ ] **Step 3: Register all handlers and adapters explicitly**

```csharp
public static class DependencyInjection
{
    public static IServiceCollection AddHealthExamInfrastructure(
        this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = Environment.GetEnvironmentVariable("HEALTHEXAM_DB") ?? "";
        services.AddDbContext<HealthExamDbContext>(options =>
            options.UseNpgsql(connectionString)
                .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking));

        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<ICatalogRepository, CatalogRepository>();
        services.AddScoped<IExamSessionRepository, ExamSessionRepository>();
        services.AddScoped<IExamRecordRepository, ExamRecordRepository>();
        services.AddScoped<IImportRepository, ImportRepository>();
        services.AddScoped<IParaclinicalRepository, ParaclinicalRepository>();
        services.AddScoped<IIntegrationOutboxRepository, IntegrationOutboxRepository>();
        services.AddScoped<IWebhookInboxRepository, WebhookInboxRepository>();
        return services;
    }
}
```

Register dedicated handler interfaces explicitly in API composition code. Register typed HTTP clients, caches, and hosted workers from Infrastructure. Preserve the current rule that workers are registered only when `HEALTHEXAM_DB` is non-empty.

- [ ] **Step 4: Run composition and full PostgreSQL-backed suite**

Run: `test -n "$HEALTHEXAM_TEST_DB" && TZ=Asia/Ho_Chi_Minh dotnet test HealthExamServer.sln`

Expected: PASS with zero skipped PostgreSQL Infrastructure tests.

- [ ] **Step 5: Commit final composition**

```bash
git add HealthExam.Infrastructure/DependencyInjection.cs HealthExam.API/Program.cs HealthExam.Tests HealthExam.Server/Service/DependencyInjection.cs HealthExam.Core/EntityFramework/Repositories
git commit -m "refactor: centralize clean architecture composition"
```

---

### Task 14: Remove legacy projects and certify the final architecture

**Files:**
- Move any remaining API DTOs from `HealthExam.Server/Models/*.cs` to `HealthExam.API/Contracts/`; Tasks 4-12 should leave this directory empty
- Move API-only models: `HealthExam.Core/Models/ErrorCodes.cs`, `ResultData.cs`, `PaginationData.cs`, and `ClaimNames.cs` to `HealthExam.API/Contracts/` or `HealthExam.API/Middlewares/`
- Delete replaced legacy abstractions: `HealthExam.Core/Models/HealthExamException.cs` and `HealthExam.Core/Models/IHealthExamContext.cs`
- Delete: `HealthExam.Core/`
- Delete: `HealthExam.Server/`
- Delete: `HealthExam.Utils/`
- Modify: `HealthExamServer.sln`
- Modify: `HealthExam.API/HealthExam.API.csproj`
- Modify: `HealthExam.Tests/HealthExam.Tests.csproj`
- Modify: `README.md`
- Modify: `HealthExam.Tests/Architecture/DependencyRuleTests.cs`
- Create: `HealthExam.Tests/API/ApiContractSurfaceTests.cs`

**Interfaces:**
- Consumes: all migrated slices and final DI registrations
- Produces: a solution containing only Domain, Application, Infrastructure, API, and Tests, with architecture and API-contract proof

- [ ] **Step 1: Write the final failing architecture and API-surface assertions**

```csharp
[Fact]
public void Legacy_projects_are_absent_from_solution()
{
    var solution = File.ReadAllText(Path.Combine(Root, "HealthExamServer.sln"));
    Assert.DoesNotContain("HealthExam.Core", solution, StringComparison.Ordinal);
    Assert.DoesNotContain("HealthExam.Server", solution, StringComparison.Ordinal);
    Assert.DoesNotContain("HealthExam.Utils", solution, StringComparison.Ordinal);
}
```

`ApiContractSurfaceTests` loads Swagger and asserts the exact existing path/method set from all controllers, including internal, hooks, vendor, import, paraclinical, form-server, and HIS endpoints. Keep the existing Swagger assertions for summaries/business documentation.

- [ ] **Step 2: Run final architecture tests and verify they fail while legacy projects remain**

Run: `dotnet test HealthExam.Tests/HealthExam.Tests.csproj --filter "FullyQualifiedName~DependencyRuleTests|FullyQualifiedName~ApiContractSurfaceTests"`

Expected: FAIL because the solution still lists legacy projects or a controller still exposes a legacy dependency.

- [ ] **Step 3: Move remaining contracts, remove legacy references, and delete legacy projects**

Use `git mv` for retained source files. API request/response DTOs keep every property name, type, default, and Newtonsoft attribute. Application models must remain free of Newtonsoft. Remove legacy projects from the solution and remove their project references from API/Tests. Delete the temporary generic repository under `Infrastructure/Persistence/Legacy` and confirm no Application interface returns `IQueryable`.

Update README commands:

```bash
dotnet ef database update --project HealthExam.Infrastructure --startup-project HealthExam.Infrastructure
ASPNETCORE_URLS=http://127.0.0.1:9920 ENABLE_SWAGGER=true dotnet run --project HealthExam.API
test -n "$HEALTHEXAM_TEST_DB" && TZ=Asia/Ho_Chi_Minh dotnet test HealthExamServer.sln
```

- [ ] **Step 4: Run all four required test groups and structural checks**

```bash
test -n "$HEALTHEXAM_TEST_DB"
TZ=Asia/Ho_Chi_Minh dotnet test HealthExamServer.sln
dotnet build HealthExamServer.sln --no-restore
dotnet ef migrations list --project HealthExam.Infrastructure --startup-project HealthExam.Infrastructure
rg -n "Microsoft.EntityFrameworkCore|Npgsql|Newtonsoft|OfficeOpenXml|Microsoft.AspNetCore|Microsoft.Extensions" HealthExam.Domain HealthExam.Application
rg -n "IQueryable" HealthExam.Application
git diff --check
```

Expected:

- Tests and build pass.
- Migration IDs remain unchanged from `20260824083342_InitialHealthExamSchema` through `20260908102936_AddHisAdmissionId`.
- Both `rg` commands return no matches.
- `git diff --check` reports no whitespace errors.

- [ ] **Step 5: Commit the completed refactor**

```bash
git add HealthExam.Domain HealthExam.Application HealthExam.Infrastructure HealthExam.API HealthExam.Tests HealthExamServer.sln README.md HealthExam.Core HealthExam.Server HealthExam.Utils
git commit -m "refactor: complete clean architecture migration"
```

---

## Execution Checkpoints

- After Task 3: review dependency boundaries and shared result semantics before feature migration.
- After Task 4: review the first complete Controller -> Handler -> Repository path.
- After Task 6: review PostgreSQL sequence, unique-conflict, and transaction parity.
- After Task 8: review import savepoint and bulk-write parity.
- After Task 10: review outbox locking, retry, and vendor wire parity.
- After Task 11: review webhook idempotency, late-event handling, and reconciliation parity.
- After Task 12: review HIS credential isolation and JSON contract parity.
- After Task 14: run the final architecture and API contract gate before considering the branch complete.
