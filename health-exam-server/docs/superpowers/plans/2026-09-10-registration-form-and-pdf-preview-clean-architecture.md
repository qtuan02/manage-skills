# Registration Form Step 2 & PDF Preview Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Port and integrate Step 2 Registration Form configuration, HIS EMR form value persistence (read-merge-write with automatic admission linking), and binary PDF preview into the Clean Architecture branch (`feat-refactor-clean-architecture`).

**Architecture:** Implement the feature as a dedicated `RegistrationForms` vertical slice following Onion Architecture. Domain layer defines pure BCL entities (`ExamGroupFormMapping`, `ExamGroupFormSectionMapping`) and tracks HIS EMR state on `ExamRecord`. Infrastructure provides EF Core migrations, PostgreSQL persistence repository, and HIS EMR HTTP client extensions (including raw PDF rendering). Application orchestrates business logic via 5 dedicated use case handlers with zero external NuGet packages. API controllers expose clean HTTP endpoints delegating strictly to application handlers.

**Tech Stack:** .NET 8 BCL, C# 12, ASP.NET Core 8, EF Core 8 (Npgsql PostgreSQL), Swashbuckle OpenAPI, xUnit.

## Global Constraints

- `HealthExam.Domain` and `HealthExam.Application` MUST reference only BCL/.NET 8 (0 external NuGet packages, no EF Core, no HttpClient, no Newtonsoft).
- Preserve exact database schema, table/column names, and migration IDs: `20260909081824_AddExamGroupFormMappings` and `20260909102548_AddHisRegistrationFormState`.
- Dedicated handler interface per use case; no mediator, event bus, or generic repository.
- Controllers call Application handlers only and return standard `HealthExamControllerBase` responses or raw `File(...)` for PDF.
- Preserve route contracts:
  - `GET /v1/exam-groups/available`
  - `GET /v1/exam-groups/{variantCode}/registration-form`
  - `GET /v1/exam-records/{recordId}/registration-form`
  - `PUT /v1/exam-records/{recordId}/registration-form/sections/{sectionKind}`
  - `GET /v1/exam-records/{recordId}/registration-form/preview` (raw `application/pdf`)
- Always execute `dotnet` commands with `DOTNET_ROLL_FORWARD=LatestMajor` prefix.
- Never modify or stage root `node_modules/`, `package.json`, or `pnpm-lock.yaml`.

---

### Task 1: Domain Entities & Database Migrations

**Files:**
- Create: `HealthExam.Domain/ExamForms/ExamGroupFormMapping.cs`
- Create: `HealthExam.Domain/ExamForms/ExamGroupFormSectionMapping.cs`
- Modify: `HealthExam.Domain/ExamRecords/ExamRecord.cs:120-135`
- Modify: `HealthExam.Infrastructure/Persistence/HealthExamDbContext.cs:40-75, 200-240, 520-560`
- Create: `HealthExam.Infrastructure/Persistence/Migrations/20260909081824_AddExamGroupFormMappings.cs`
- Create: `HealthExam.Infrastructure/Persistence/Migrations/20260909081824_AddExamGroupFormMappings.Designer.cs`
- Create: `HealthExam.Infrastructure/Persistence/Migrations/20260909102548_AddHisRegistrationFormState.cs`
- Create: `HealthExam.Infrastructure/Persistence/Migrations/20260909102548_AddHisRegistrationFormState.Designer.cs`
- Modify: `HealthExam.Infrastructure/Persistence/Migrations/HealthExamDbContextModelSnapshot.cs`
- Test: `HealthExam.Tests/Domain/ExamGroupFormMappingDomainTests.cs`
- Test: `HealthExam.Tests/Infrastructure/RegistrationFormPersistenceTests.cs`

**Interfaces:**
- Consumes: `HealthExam.Domain.Common.ActorKind`, `HealthExam.Domain.ExamRecords.ExamRecord`
- Produces: `ExamGroupFormMapping`, `ExamGroupFormSectionMapping`, `ExamRecord.HisEmrDataID`, `ExamRecord.HisFormTemplateID`, `ExamRecord.HisFormSyncStatus`, `ExamRecord.HisFormSyncError`

- [ ] **Step 1: Write the failing domain test**

Create `HealthExam.Tests/Domain/ExamGroupFormMappingDomainTests.cs`:
```csharp
using System;
using HealthExam.Domain.Common;
using HealthExam.Domain.ExamForms;
using HealthExam.Domain.ExamRecords;
using Xunit;

namespace HealthExam.Tests.Domain;

public class ExamGroupFormMappingDomainTests
{
    [Fact]
    public void ExamGroupFormMapping_initializes_with_expected_defaults()
    {
        var mapping = new ExamGroupFormMapping
        {
            DivisionID = "DEV",
            VariantCode = "DTK_03",
            TemplateCode = "KSK-TREN18TUOI"
        };

        Assert.Equal(Guid.Empty, mapping.MappingID);
        Assert.True(mapping.IsActive);
        Assert.Equal("DEV", mapping.DivisionID);
        Assert.Equal("DTK_03", mapping.VariantCode);
        Assert.Equal("KSK-TREN18TUOI", mapping.TemplateCode);
        Assert.NotNull(mapping.Sections);
        Assert.Empty(mapping.Sections);
    }

    [Fact]
    public void ExamRecord_contains_his_registration_form_state_fields()
    {
        var record = new ExamRecord();
        Assert.Null(record.HisEmrDataID);
        Assert.Null(record.HisFormTemplateID);
        Assert.Equal("Pending", record.HisFormSyncStatus);
        Assert.Equal("", record.HisFormSyncError);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `DOTNET_ROLL_FORWARD=LatestMajor dotnet test HealthExam.Tests/HealthExam.Tests.csproj --filter FullyQualifiedName~ExamGroupFormMappingDomainTests`
Expected: Compilation failure due to missing `ExamGroupFormMapping` and missing properties on `ExamRecord`.

- [ ] **Step 3: Implement domain entities and update ExamRecord**

Create `HealthExam.Domain/ExamForms/ExamGroupFormMapping.cs`:
```csharp
using System;
using System.Collections.Generic;
using HealthExam.Domain.Common;

namespace HealthExam.Domain.ExamForms;

public class ExamGroupFormMapping
{
    public Guid MappingID { get; set; }
    public string DivisionID { get; set; } = "";
    public string VariantCode { get; set; } = "";
    public string TemplateCode { get; set; } = "";
    public bool IsActive { get; set; } = true;

    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;
    public long CreatedBy { get; set; }
    public ActorKind CreatedActorKind { get; set; }
    public DateTime ModifiedDate { get; set; } = DateTime.UtcNow;
    public long ModifiedBy { get; set; }
    public ActorKind ModifiedActorKind { get; set; }

    public ICollection<ExamGroupFormSectionMapping> Sections { get; set; } = new List<ExamGroupFormSectionMapping>();
}
```

Create `HealthExam.Domain/ExamForms/ExamGroupFormSectionMapping.cs`:
```csharp
using System;

namespace HealthExam.Domain.ExamForms;

public class ExamGroupFormSectionMapping
{
    public Guid SectionMappingID { get; set; }
    public Guid MappingID { get; set; }
    public string SectionKind { get; set; } = "";
    public int ItemGroupID { get; set; }

    public ExamGroupFormMapping Mapping { get; set; }
}
```

In `HealthExam.Domain/ExamRecords/ExamRecord.cs`, add:
```csharp
    // --- Biểu mẫu đăng ký KSK trên HIS EMR
    public Guid? HisEmrDataID { get; set; }
    public Guid? HisFormTemplateID { get; set; }
    public string HisFormSyncStatus { get; set; } = "Pending";
    public string HisFormSyncError { get; set; } = "";
```

- [ ] **Step 4: Update DbContext and apply migrations**

In `HealthExam.Infrastructure/Persistence/HealthExamDbContext.cs`:
1. Add `DbSet<ExamGroupFormMapping> ExamGroupFormMappings => Set<ExamGroupFormMapping>();`
2. Add `DbSet<ExamGroupFormSectionMapping> ExamGroupFormSectionMappings => Set<ExamGroupFormSectionMapping>();`
3. In `ConfigureSessionAndRecord`, map:
```csharp
    e.Property(x => x.HisFormSyncStatus).HasMaxLength(50).HasDefaultValue("Pending");
    e.Property(x => x.HisFormSyncError).HasMaxLength(1000).HasDefaultValue("");
```
4. Add method `ConfigureExamGroupFormMapping(ModelBuilder b)` with table definitions, foreign keys, unique indices, and seed data for `DEV` and `DHTESTING` (`DTK_03` -> `KSK-TREN18TUOI`).
5. Call `ConfigureExamGroupFormMapping(b)` inside `OnModelCreating`.

Add migration files:
- `HealthExam.Infrastructure/Persistence/Migrations/20260909081824_AddExamGroupFormMappings.cs` & `.Designer.cs`
- `HealthExam.Infrastructure/Persistence/Migrations/20260909102548_AddHisRegistrationFormState.cs` & `.Designer.cs`
- Update `HealthExamDbContextModelSnapshot.cs` with the new tables, columns, and seed data.

- [ ] **Step 5: Write PostgreSQL persistence integration test**

Create `HealthExam.Tests/Infrastructure/RegistrationFormPersistenceTests.cs`:
```csharp
using System;
using System.Linq;
using System.Threading.Tasks;
using HealthExam.Domain.ExamForms;
using HealthExam.Domain.ExamRecords;
using HealthExam.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HealthExam.Tests.Infrastructure;

public class RegistrationFormPersistenceTests : IClassFixture<PostgresTestDb>
{
    private readonly PostgresTestDb _db;

    public RegistrationFormPersistenceTests(PostgresTestDb db)
    {
        _db = db;
    }

    [Fact]
    public async Task Seed_mappings_exist_for_dev_and_dhtesting()
    {
        await using var ctx = _db.CreateDbContext();
        var devMapping = await ctx.ExamGroupFormMappings
            .Include(m => m.Sections)
            .FirstOrDefaultAsync(m => m.DivisionID == "DEV" && m.VariantCode == "DTK_03");

        Assert.NotNull(devMapping);
        Assert.Equal("KSK-TREN18TUOI", devMapping.TemplateCode);
        Assert.Equal(2, devMapping.Sections.Count);
        Assert.Contains(devMapping.Sections, s => s.SectionKind == "HISTORY" && s.ItemGroupID == 64);
        Assert.Contains(devMapping.Sections, s => s.SectionKind == "EXTRA_INFO" && s.ItemGroupID == 63);
    }

    [Fact]
    public async Task ExamRecord_persists_and_reads_his_form_columns()
    {
        await using var ctx = _db.CreateDbContext();
        var emrId = Guid.NewGuid();
        var templateId = Guid.NewGuid();

        var record = new ExamRecord
        {
            DivisionID = "DEV",
            RecordCode = $"TEST-{Guid.NewGuid():N}"[..20],
            FullName = "Nguyen Van Test",
            VariantCode = "DTK_03",
            HisEmrDataID = emrId,
            HisFormTemplateID = templateId,
            HisFormSyncStatus = "Synced",
            HisFormSyncError = ""
        };

        ctx.ExamRecords.Add(record);
        await ctx.SaveChangesAsync();

        await using var readCtx = _db.CreateDbContext();
        var loaded = await readCtx.ExamRecords.FirstOrDefaultAsync(r => r.RecordID == record.RecordID);
        Assert.NotNull(loaded);
        Assert.Equal(emrId, loaded.HisEmrDataID);
        Assert.Equal(templateId, loaded.HisFormTemplateID);
        Assert.Equal("Synced", loaded.HisFormSyncStatus);
    }
}
```

- [ ] **Step 6: Run tests and verify GREEN**

Run: `DOTNET_ROLL_FORWARD=LatestMajor dotnet test HealthExam.Tests/HealthExam.Tests.csproj --filter "FullyQualifiedName~ExamGroupFormMappingDomainTests|FullyQualifiedName~RegistrationFormPersistenceTests"`
Expected: All tests PASS.

- [ ] **Step 7: Commit**

```bash
git add HealthExam.Domain/ExamForms HealthExam.Domain/ExamRecords/ExamRecord.cs HealthExam.Infrastructure/Persistence HealthExam.Tests/Domain/ExamGroupFormMappingDomainTests.cs HealthExam.Tests/Infrastructure/RegistrationFormPersistenceTests.cs
git commit -m "feat(domain): add exam group form mappings and his registration form state"
```

---

### Task 2: Infrastructure Ports & Adapters (HIS EMR Client Extension & Repository)

**Files:**
- Create: `HealthExam.Application/RegistrationForms/IRegistrationFormRepository.cs`
- Modify: `HealthExam.Application/Integrations/IHisEmrClient.cs:5-60`
- Modify: `HealthExam.Infrastructure/Integrations/HisEmr/HisEmrOptions.cs:10-58`
- Modify: `HealthExam.Infrastructure/Integrations/HisEmr/HisEmrClient.cs:20-60, 260-297`
- Create: `HealthExam.Infrastructure/Persistence/Repositories/RegistrationFormRepository.cs`
- Modify: `HealthExam.Infrastructure/DependencyInjection.cs:70-95`
- Test: `HealthExam.Tests/Infrastructure/HisEmrClientPdfTests.cs`
- Test: `HealthExam.Tests/Infrastructure/RegistrationFormRepositoryTests.cs`

**Interfaces:**
- Consumes: `HealthExam.Domain.ExamForms.ExamGroupFormMapping`, `HealthExam.Domain.ExamRecords.ExamRecord`, `HealthExamDbContext`
- Produces: `IRegistrationFormRepository`, `IHisEmrClient.RenderFormPdfAsync`, extended `HisOperation` (CreateAdmission, ReadFormData, SaveFormData)

- [ ] **Step 1: Write the failing test for PDF rendering and repository**

Create `HealthExam.Tests/Infrastructure/HisEmrClientPdfTests.cs`:
```csharp
using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HealthExam.Application.Integrations;
using HealthExam.Infrastructure.Integrations.HisEmr;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HealthExam.Tests.Infrastructure;

public class HisEmrClientPdfTests
{
    private sealed class DelegatingHandlerStub : DelegatingHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;
        public DelegatingHandlerStub(Func<HttpRequestMessage, HttpResponseMessage> handler) => _handler = handler;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(_handler(request));
    }

    [Fact]
    public async Task RenderFormPdfAsync_returns_bytes_when_his_responds_pdf()
    {
        var emrDataId = Guid.NewGuid();
        var expectedBytes = Encoding.ASCII.GetBytes("%PDF-1.7 mock content");

        var stub = new DelegatingHandlerStub(req =>
        {
            Assert.Equal(HttpMethod.Get, req.Method);
            Assert.Contains($"api/M03F10010/VEMR?EMRDataID={emrDataId}&IsJson=false", req.RequestUri!.ToString());
            Assert.Equal("Bearer token123", req.Headers.Authorization?.ToString());

            var resp = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(expectedBytes)
            };
            resp.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/pdf");
            return resp;
        });

        var options = new HisEmrOptions
        {
            Enabled = true,
            BaseUrl = "http://his-mock.local",
            CredentialHeaderName = "Authorization"
        };
        var client = new HisEmrClient(new HttpClient(stub), options, NullLogger<HisEmrClient>.Instance);

        var res = await client.RenderFormPdfAsync(
            emrDataId,
            new HisRequest("", "GET", "Bearer token123", null, "trace-1", "DEV"),
            CancellationToken.None);

        Assert.True(res.IsSuccess);
        Assert.Equal(expectedBytes, res.Value);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `DOTNET_ROLL_FORWARD=LatestMajor dotnet test HealthExam.Tests/HealthExam.Tests.csproj --filter FullyQualifiedName~HisEmrClientPdfTests`
Expected: Compilation failure because `RenderFormPdfAsync` is not yet declared on `IHisEmrClient` or implemented on `HisEmrClient`.

- [ ] **Step 3: Define repository interface and extend IHisEmrClient**

Create `HealthExam.Application/RegistrationForms/IRegistrationFormRepository.cs`:
```csharp
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using HealthExam.Domain.ExamForms;
using HealthExam.Domain.ExamRecords;

namespace HealthExam.Application.RegistrationForms;

public interface IRegistrationFormRepository
{
    Task<ExamGroupFormMapping> GetActiveMappingAsync(string divisionId, string variantCode, CancellationToken ct = default);
    Task<IReadOnlyList<string>> ListActiveVariantCodesAsync(string divisionId, CancellationToken ct = default);
    Task<ExamRecord> GetRecordAsync(string divisionId, Guid recordId, bool forUpdate = false, CancellationToken ct = default);
    Task<ExamRecord> GetRecordWithSessionAsync(string divisionId, Guid recordId, bool forUpdate = false, CancellationToken ct = default);
}
```

In `HealthExam.Application/Integrations/IHisEmrClient.cs`:
Add operations to `HisOperation`:
```csharp
public enum HisOperation
{
    ListDefinitions,
    GetDefinition,
    GetDefinitionLayout,
    ListProcesses,
    GetProcess,
    GetSignWorkflow,
    SubmitSection,
    SignSection,
    CancelSectionSignature,
    CreateAdmission,
    ReadFormData,
    SaveFormData,
    RenderFormPdf
}
```
Add method to `IHisEmrClient`:
```csharp
Task<HisClientResult<byte[]>> RenderFormPdfAsync(
    Guid emrDataId, HisRequest request, CancellationToken ct = default);
```

- [ ] **Step 4: Update HisEmrOptions and implement HisEmrClient.RenderFormPdfAsync**

In `HealthExam.Infrastructure/Integrations/HisEmr/HisEmrOptions.cs`:
Add constants and properties:
```csharp
    public const string KskDepartmentIdEnv = "HIS_EMR_KSK_DEPARTMENT_ID";
    public const string KskDepartmentCodeEnv = "HIS_EMR_KSK_DEPARTMENT_CODE";

    public int? KskDepartmentId { get; init; }
    public string KskDepartmentCode { get; init; }
```
Parse them in `FromEnvironment()`.

In `HealthExam.Infrastructure/Integrations/HisEmr/HisEmrClient.cs`:
Add route constants:
```csharp
    public const string RouteREmr = "api/M03F10010/REMR";
    public const string RouteCueEmr = "api/M03F10010/CUEMR";
    public const string RouteVEmr = "api/M03F10010/VEMR";
    public const string RouteCUAdmission = "api/M02F00000/GetAdmissionInfo";
```
Implement `RenderFormPdfAsync(Guid emrDataId, HisRequest request, CancellationToken ct = default)`:
- Check `_options.Enabled` and `BaseUrl`.
- Prepare `HttpRequestMessage` GET `api/M03F10010/VEMR?EMRDataID={emrDataId}&IsJson=false`.
- Add headers: `CredentialHeaderName`, `X-Trace-Id`, `X-Division-Id`.
- Send HTTP request; handle timeout / HttpRequestException -> map to `HisClientOutcome.Timeout` / `BadGateway`.
- Validate HTTP status code: 401 -> Unauthorized, 403 -> Forbidden, 404 -> NotFound, non-success -> BadGateway.
- Validate Content-Type: if JSON or body starts with `{`, parse envelope and check ErrorCode. If non-zero, map to `SignPrecondition`.
- Read byte array; return `HisClientResult<byte[]>.Success(bytes)`.

- [ ] **Step 5: Implement RegistrationFormRepository and register DI**

Create `HealthExam.Infrastructure/Persistence/Repositories/RegistrationFormRepository.cs`:
```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HealthExam.Application.RegistrationForms;
using HealthExam.Domain.ExamForms;
using HealthExam.Domain.ExamRecords;
using Microsoft.EntityFrameworkCore;

namespace HealthExam.Infrastructure.Persistence.Repositories;

public class RegistrationFormRepository : IRegistrationFormRepository
{
    private readonly HealthExamDbContext _db;

    public RegistrationFormRepository(HealthExamDbContext db)
    {
        _db = db;
    }

    public Task<ExamGroupFormMapping> GetActiveMappingAsync(
        string divisionId, string variantCode, CancellationToken ct = default)
    {
        return _db.ExamGroupFormMappings
            .Include(m => m.Sections)
            .FirstOrDefaultAsync(m => m.DivisionID == divisionId && m.VariantCode == variantCode && m.IsActive, ct);
    }

    public async Task<IReadOnlyList<string>> ListActiveVariantCodesAsync(
        string divisionId, CancellationToken ct = default)
    {
        var list = await _db.ExamGroupFormMappings
            .Where(m => m.DivisionID == divisionId && m.IsActive)
            .Select(m => m.VariantCode)
            .Distinct()
            .ToListAsync(ct);
        return list;
    }

    public Task<ExamRecord> GetRecordAsync(
        string divisionId, Guid recordId, bool forUpdate = false, CancellationToken ct = default)
    {
        var q = forUpdate ? _db.ExamRecords.AsTracking() : _db.ExamRecords.AsNoTracking();
        return q.FirstOrDefaultAsync(r => r.RecordID == recordId && r.DivisionID == divisionId, ct);
    }

    public Task<ExamRecord> GetRecordWithSessionAsync(
        string divisionId, Guid recordId, bool forUpdate = false, CancellationToken ct = default)
    {
        var q = forUpdate ? _db.ExamRecords.AsTracking() : _db.ExamRecords.AsNoTracking();
        return q.Include(r => r.Session)
            .FirstOrDefaultAsync(r => r.RecordID == recordId && r.DivisionID == divisionId, ct);
    }
}
```

In `HealthExam.Infrastructure/DependencyInjection.cs`:
Register `services.AddScoped<IRegistrationFormRepository, RegistrationFormRepository>();`.

Create `HealthExam.Tests/Infrastructure/RegistrationFormRepositoryTests.cs` validating `RegistrationFormRepository` queries against `PostgresTestDb`.

- [ ] **Step 6: Run tests and verify GREEN**

Run: `DOTNET_ROLL_FORWARD=LatestMajor dotnet test HealthExam.Tests/HealthExam.Tests.csproj --filter "FullyQualifiedName~HisEmrClientPdfTests|FullyQualifiedName~RegistrationFormRepositoryTests"`
Expected: All tests PASS.

- [ ] **Step 7: Commit**

```bash
git add HealthExam.Application/RegistrationForms/IRegistrationFormRepository.cs HealthExam.Application/Integrations/IHisEmrClient.cs HealthExam.Infrastructure/Integrations/HisEmr HealthExam.Infrastructure/Persistence/Repositories/RegistrationFormRepository.cs HealthExam.Infrastructure/DependencyInjection.cs HealthExam.Tests/Infrastructure/HisEmrClientPdfTests.cs HealthExam.Tests/Infrastructure/RegistrationFormRepositoryTests.cs
git commit -m "feat(infra): add registration form repository and his emr pdf client adapter"
```

---

### Task 3: Application Models & Use Case Handlers

**Files:**
- Create: `HealthExam.Application/RegistrationForms/RegistrationFormModels.cs`
- Create: `HealthExam.Application/RegistrationForms/GetExamGroupRegistrationForms.cs`
- Create: `HealthExam.Application/RegistrationForms/ListAvailableExamGroups.cs`
- Create: `HealthExam.Application/RegistrationForms/GetRegistrationForm.cs`
- Create: `HealthExam.Application/RegistrationForms/SaveRegistrationFormSection.cs`
- Create: `HealthExam.Application/RegistrationForms/PreviewRegistrationFormPdf.cs`
- Test: `HealthExam.Tests/Application/RegistrationFormHandlerTests.cs`

**Interfaces:**
- Consumes: `IRegistrationFormRepository`, `IHisEmrClient`, `IHisFormDefinitionCache`, `IUnitOfWork`
- Produces:
  - `IGetExamGroupRegistrationFormsHandler`, `GetExamGroupRegistrationFormsQuery`, `RegistrationFormDefinition`
  - `IListAvailableExamGroupsHandler`, `ListAvailableExamGroupsQuery`, `IReadOnlyList<AvailableExamGroupItem>`
  - `IGetRegistrationFormHandler`, `GetRegistrationFormQuery`, `RegistrationRecordForm`
  - `ISaveRegistrationFormSectionHandler`, `SaveRegistrationFormSectionCommand`, `RegistrationRecordForm`
  - `IPreviewRegistrationFormPdfHandler`, `PreviewRegistrationFormPdfQuery`, `byte[]`

- [ ] **Step 1: Write the failing tests for Application Handlers**

Create `HealthExam.Tests/Application/RegistrationFormHandlerTests.cs` covering:
- `GetExamGroupRegistrationForms`: Returns normalized HISTORY & EXTRA_INFO sections; returns NotFound for unknown variant or unmapped group.
- `ListAvailableExamGroups`: Returns active mapped groups ordered by statutory `OrderNo`.
- `GetRegistrationForm`: Returns record form; merges REMR values if admission exists.
- `SaveRegistrationFormSection`:
  - Rejects invalid sectionKind (e.g. "FOO") with BadRequest.
  - Rejects readonly or unknown field IDs with BadRequest.
  - Automatically invokes admission linking if `AdmissionID` is missing.
  - Performs Read-Merge-Write via REMR and CUEMR.
  - Updates `HisEmrDataID` and marks `HisFormSyncStatus = "Synced"`.
- `PreviewRegistrationFormPdf`:
  - Throws NotFound if record not found.
  - Throws InvalidState if `HisEmrDataID` is null.
  - Returns raw PDF bytes on success.

- [ ] **Step 2: Run test to verify it fails**

Run: `DOTNET_ROLL_FORWARD=LatestMajor dotnet test HealthExam.Tests/HealthExam.Tests.csproj --filter FullyQualifiedName~RegistrationFormHandlerTests`
Expected: Compilation failure due to missing handlers and DTOs.

- [ ] **Step 3: Define Application DTOs and Queries/Commands**

Create `HealthExam.Application/RegistrationForms/RegistrationFormModels.cs`:
```csharp
using System;
using System.Collections.Generic;
using HealthExam.Domain.Common;

namespace HealthExam.Application.RegistrationForms;

public sealed class RegistrationFormDefinition
{
    public string VariantCode { get; init; } = "";
    public string TemplateCode { get; init; } = "";
    public string TemplateName { get; init; } = "";
    public IReadOnlyList<RegistrationFormSection> Sections { get; init; } = Array.Empty<RegistrationFormSection>();
}

public sealed class RegistrationFormSection
{
    public string Kind { get; init; } = "";
    public int ItemGroupID { get; init; }
    public string Title { get; init; } = "";
    public IReadOnlyList<RegistrationFormNode> Nodes { get; init; } = Array.Empty<RegistrationFormNode>();
}

public sealed class RegistrationFormNode
{
    public string NodeID { get; init; } = "";
    public string ParentNodeID { get; init; }
    public Guid? ItemID { get; init; }
    public int ItemGroupID { get; init; }
    public int Order { get; init; }
    public int Level { get; init; }
    public string Label { get; init; } = "";
    public string ControlType { get; init; } = "";
    public string DataType { get; init; } = "";
    public bool Required { get; init; }
    public bool ReadOnly { get; init; }
    public string Value { get; init; } = "";
    public string Text { get; init; }
    public IReadOnlyList<RegistrationFormChoice> Choices { get; init; }
}

public sealed class RegistrationFormChoice
{
    public string Code { get; init; } = "";
    public string Label { get; init; } = "";
}

public sealed class AvailableExamGroupItem
{
    public string VariantCode { get; init; } = "";
    public string Name { get; init; } = "";
    public int OrderNo { get; init; }
}

public sealed class RegistrationRecordForm
{
    public Guid RecordID { get; init; }
    public long? AdmissionID { get; init; }
    public Guid? HisEmrDataID { get; init; }
    public Guid? HisFormTemplateID { get; init; }
    public string HisFormSyncStatus { get; init; } = "Pending";
    public string HisFormSyncError { get; init; } = "";
    public string VariantCode { get; init; } = "";
    public string TemplateCode { get; init; } = "";
    public string TemplateName { get; init; } = "";
    public IReadOnlyList<RegistrationFormSection> Sections { get; init; } = Array.Empty<RegistrationFormSection>();
}

public sealed class RegistrationSectionSaveRequest
{
    public RegistrationSectionSaveRequest() { }
    public RegistrationSectionSaveRequest(IReadOnlyList<RegistrationFormFieldValue> fields, bool isDraft = true)
    {
        Fields = fields ?? Array.Empty<RegistrationFormFieldValue>();
        IsDraft = isDraft;
    }
    public IReadOnlyList<RegistrationFormFieldValue> Fields { get; init; } = Array.Empty<RegistrationFormFieldValue>();
    public bool IsDraft { get; init; } = true;
}

public sealed class RegistrationFormFieldValue
{
    public RegistrationFormFieldValue() { }
    public RegistrationFormFieldValue(Guid itemId, string value, string text = null)
    {
        ItemId = itemId;
        Value = value ?? "";
        Text = text;
    }
    public Guid ItemId { get; init; }
    public string Value { get; init; } = "";
    public string Text { get; init; }
}

public sealed record GetExamGroupRegistrationFormsQuery(
    string DivisionId, string VariantCode, string Credential = null, string TraceId = null);

public sealed record ListAvailableExamGroupsQuery(
    string DivisionId);

public sealed record GetRegistrationFormQuery(
    string DivisionId, Guid RecordId, string Credential = null, string TraceId = null);

public sealed record SaveRegistrationFormSectionCommand(
    string DivisionId, Guid RecordId, string SectionKind, RegistrationSectionSaveRequest Request,
    string ActorId, ActorKind ActorKind, string Credential = null, string TraceId = null);

public sealed record PreviewRegistrationFormPdfQuery(
    string DivisionId, Guid RecordId, string Credential = null, string TraceId = null);
```

- [ ] **Step 4: Implement the 5 Application Handlers**

1. Create `HealthExam.Application/RegistrationForms/GetExamGroupRegistrationForms.cs`:
   Implements `IGetExamGroupRegistrationFormsHandler`. Validates `ExamGroups.IsValid(variantCode)`, retrieves mapping from `IRegistrationFormRepository`, loads form definition layout via `IHisFormDefinitionCache`, normalizes nodes for HISTORY (64) and EXTRA_INFO (63), and returns `ApplicationResult<RegistrationFormDefinition>`.
2. Create `HealthExam.Application/RegistrationForms/ListAvailableExamGroups.cs`:
   Implements `IListAvailableExamGroupsHandler`. Calls `_repo.ListActiveVariantCodesAsync`, resolves name and order using `ExamGroups.Find`, returns ordered list of `AvailableExamGroupItem`.
3. Create `HealthExam.Application/RegistrationForms/GetRegistrationForm.cs`:
   Implements `IGetRegistrationFormHandler`. Loads record and mapping. If `record.AdmissionID > 0`, invokes HIS `REMR` via `IHisEmrClient` (operation `ReadFormData`) to load current values. If `REMR` returns an EMRDataID and record's `HisEmrDataID` was empty, updates record. Returns `RegistrationRecordForm`.
4. Create `HealthExam.Application/RegistrationForms/SaveRegistrationFormSection.cs`:
   Implements `ISaveRegistrationFormSectionHandler`.
   - Validates `sectionKind` ("HISTORY" or "EXTRA_INFO").
   - Loads record and mapping.
   - Validates submitted field IDs belong to the section and are not read-only.
   - Ensures admission: if `record.AdmissionID <= 0`, calls `IHisEmrClient` operation `CreateAdmission` using session department info and updates `record.AdmissionID`.
   - Read-merge-write: reads existing details from HIS `REMR`, preserves fields from the other section, updates submitted fields for current section, calls `CUEMR`.
   - Updates `record.HisEmrDataID = savedEmrDataId; record.HisFormTemplateID = hisDef.TemplateId; record.HisFormSyncStatus = "Synced"; record.HisFormSyncError = "";`.
   - In case of failure: marks `record.HisFormSyncStatus = "Failed"; record.HisFormSyncError = error;` and saves.
   - Returns updated `RegistrationRecordForm`.
5. Create `HealthExam.Application/RegistrationForms/PreviewRegistrationFormPdf.cs`:
   Implements `IPreviewRegistrationFormPdfHandler`.
   - Loads record. If null -> NotFound ("Không tìm thấy hồ sơ khám").
   - If `!record.HisEmrDataID.HasValue || record.HisEmrDataID.Value == Guid.Empty` -> Fail with InvalidState ("Biểu mẫu chưa được lưu lên HIS").
   - Calls `_client.RenderFormPdfAsync(record.HisEmrDataID.Value, new HisRequest(..., Credential, TraceId, DivisionId), ct)`.
   - Returns `ApplicationResult<byte[]>.Success(pdfBytes)`.

- [ ] **Step 5: Run tests and verify GREEN**

Run: `DOTNET_ROLL_FORWARD=LatestMajor dotnet test HealthExam.Tests/HealthExam.Tests.csproj --filter FullyQualifiedName~RegistrationFormHandlerTests`
Expected: All tests PASS.

- [ ] **Step 6: Commit**

```bash
git add HealthExam.Application/RegistrationForms HealthExam.Tests/Application/RegistrationFormHandlerTests.cs
git commit -m "feat(application): add registration form handlers and models"
```

---

### Task 4: API Controllers & DI Wireup

**Files:**
- Create: `HealthExam.API/Controllers/ExamGroupController.cs`
- Create: `HealthExam.API/Controllers/ExamRecordRegistrationFormController.cs`
- Modify: `HealthExam.API/Extensions/ApplicationServiceExtensions.cs:30-80`
- Modify: `HealthExam.Tests/API/CompositionRootTests.cs:50-90`
- Create: `HealthExam.Tests/API/RegistrationFormEndpointTests.cs`

**Interfaces:**
- Consumes: `IGetExamGroupRegistrationFormsHandler`, `IListAvailableExamGroupsHandler`, `IGetRegistrationFormHandler`, `ISaveRegistrationFormSectionHandler`, `IPreviewRegistrationFormPdfHandler`
- Produces:
  - `GET /v1/exam-groups/available`
  - `GET /v1/exam-groups/{variantCode}/registration-form`
  - `GET /v1/exam-records/{recordId}/registration-form`
  - `PUT /v1/exam-records/{recordId}/registration-form/sections/{sectionKind}`
  - `GET /v1/exam-records/{recordId}/registration-form/preview`

- [ ] **Step 1: Write the failing API endpoint tests**

Create `HealthExam.Tests/API/RegistrationFormEndpointTests.cs`:
```csharp
using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HealthExam.Application.Common;
using HealthExam.Application.RegistrationForms;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace HealthExam.Tests.API;

public class RegistrationFormEndpointTests : IClassFixture<AuthTestHost>
{
    private readonly AuthTestHost _host;

    public RegistrationFormEndpointTests(AuthTestHost host)
    {
        _host = host;
    }

    [Fact]
    public async Task Get_preview_returns_pdf_stream()
    {
        var recordId = Guid.NewGuid();
        var pdfBytes = Encoding.ASCII.GetBytes("%PDF-1.7 mock preview data");

        var fakeHandler = new FakePreviewHandler(ApplicationResult<byte[]>.Success(pdfBytes));

        await using var factory = _host.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
                services.Replace(ServiceDescriptor.Singleton<IPreviewRegistrationFormPdfHandler>(fakeHandler))));

        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestHeaders.Division, "DEV");
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", AuthTestHost.EmployeeToken());

        using var response = await client.GetAsync($"/v1/exam-records/{recordId}/registration-form/preview");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/pdf", response.Content.Headers.ContentType?.MediaType);
        var bytes = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal(pdfBytes, bytes);
    }

    private sealed class FakePreviewHandler : IPreviewRegistrationFormPdfHandler
    {
        private readonly ApplicationResult<byte[]> _result;
        public FakePreviewHandler(ApplicationResult<byte[]> result) => _result = result;
        public Task<ApplicationResult<byte[]>> HandleAsync(PreviewRegistrationFormPdfQuery query, CancellationToken ct = default)
            => Task.FromResult(_result);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `DOTNET_ROLL_FORWARD=LatestMajor dotnet test HealthExam.Tests/HealthExam.Tests.csproj --filter FullyQualifiedName~RegistrationFormEndpointTests`
Expected: 404 Not Found because controllers are not yet implemented.

- [ ] **Step 3: Implement Controllers**

Create `HealthExam.API/Controllers/ExamGroupController.cs`:
```csharp
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using HealthExam.Application.RegistrationForms;
using HealthExam.Core.Models;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace HealthExam.API.Controllers;

[Route("v1/exam-groups")]
public class ExamGroupController : HealthExamControllerBase
{
    private readonly IListAvailableExamGroupsHandler _listAvailableHandler;
    private readonly IGetExamGroupRegistrationFormsHandler _getFormHandler;

    public ExamGroupController(
        IListAvailableExamGroupsHandler listAvailableHandler,
        IGetExamGroupRegistrationFormsHandler getFormHandler)
    {
        _listAvailableHandler = listAvailableHandler;
        _getFormHandler = getFormHandler;
    }

    [HttpGet("available")]
    [SwaggerOperation(
        Summary = "Danh sách nhóm khám có biểu mẫu đăng ký hoạt động",
        Description = "Trả về các nhóm khám được cấu hình biểu mẫu cho tenant, sắp xếp theo OrderNo.")]
    public async Task<ActionResult<ResultData<IReadOnlyList<AvailableExamGroupItem>>>> ListAvailable(
        CancellationToken ct = default)
    {
        var res = await _listAvailableHandler.HandleAsync(new ListAvailableExamGroupsQuery(DivisionId), ct);
        return ToActionResult(res);
    }

    [HttpGet("{variantCode}/registration-form")]
    [HttpGet("{variantCode}/registration-forms")]
    [SwaggerOperation(
        Summary = "Lấy biểu mẫu đăng ký KSK chuẩn hoá theo nhóm khám",
        Description = "Lấy và chuẩn hoá hai phân đoạn Tiền sử bệnh (HISTORY) và Thông tin bổ sung (EXTRA_INFO) từ HIS.")]
    public async Task<ActionResult<ResultData<RegistrationFormDefinition>>> GetRegistrationForm(
        [FromRoute] string variantCode,
        CancellationToken ct = default)
    {
        var res = await _getFormHandler.HandleAsync(
            new GetExamGroupRegistrationFormsQuery(DivisionId, variantCode, RequestCredential, TraceId), ct);
        return ToActionResult(res);
    }
}
```

Create `HealthExam.API/Controllers/ExamRecordRegistrationFormController.cs`:
```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using HealthExam.Application.RegistrationForms;
using HealthExam.Core.Models;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace HealthExam.API.Controllers;

[Route("v1/exam-records/{recordId:guid}/registration-form")]
public class ExamRecordRegistrationFormController : HealthExamControllerBase
{
    private readonly IGetRegistrationFormHandler _getFormHandler;
    private readonly ISaveRegistrationFormSectionHandler _saveSectionHandler;
    private readonly IPreviewRegistrationFormPdfHandler _previewPdfHandler;

    public ExamRecordRegistrationFormController(
        IGetRegistrationFormHandler getFormHandler,
        ISaveRegistrationFormSectionHandler saveSectionHandler,
        IPreviewRegistrationFormPdfHandler previewPdfHandler)
    {
        _getFormHandler = getFormHandler;
        _saveSectionHandler = saveSectionHandler;
        _previewPdfHandler = previewPdfHandler;
    }

    [HttpGet]
    [SwaggerOperation(
        Summary = "Lấy biểu mẫu đăng ký KSK và các giá trị đã lưu của hồ sơ",
        Description = "Trả về biểu mẫu chuẩn hoá (gồm phân đoạn HISTORY và EXTRA_INFO) cùng các giá trị trường đã lưu trên HIS EMR.")]
    public async Task<ActionResult<ResultData<RegistrationRecordForm>>> GetForm(
        [FromRoute] Guid recordId,
        CancellationToken ct = default)
    {
        var res = await _getFormHandler.HandleAsync(
            new GetRegistrationFormQuery(DivisionId, recordId, RequestCredential, TraceId), ct);
        return ToActionResult(res);
    }

    [HttpPut("sections/{sectionKind}")]
    [SwaggerOperation(
        Summary = "Lưu giá trị của một phân đoạn biểu mẫu đăng ký KSK",
        Description = "Lưu các giá trị của phân đoạn ngữ nghĩa (HISTORY hoặc EXTRA_INFO) vào HIS EMR, tự động tạo tiếp nhận HIS nếu thiếu.")]
    public async Task<ActionResult<ResultData<RegistrationRecordForm>>> SaveSection(
        [FromRoute] Guid recordId,
        [FromRoute] string sectionKind,
        [FromBody] RegistrationSectionSaveRequest request,
        CancellationToken ct = default)
    {
        var res = await _saveSectionHandler.HandleAsync(
            new SaveRegistrationFormSectionCommand(
                DivisionId, recordId, sectionKind, request ?? new(), ActorId, ActorKind, RequestCredential, TraceId), ct);
        return ToActionResult(res);
    }

    [HttpGet("preview")]
    [SwaggerOperation(
        Summary = "Xem trước PDF biểu mẫu đăng ký KSK",
        Description = "Trả về file PDF của snapshot HIS EMR đã lưu tương ứng với hồ sơ khám mà không submit hay ký biểu mẫu.")]
    [ProducesResponseType(typeof(FileContentResult), 200, "application/pdf")]
    public async Task<IActionResult> Preview(
        [FromRoute] Guid recordId,
        CancellationToken ct = default)
    {
        var res = await _previewPdfHandler.HandleAsync(
            new PreviewRegistrationFormPdfQuery(DivisionId, recordId, RequestCredential, TraceId), ct);

        if (!res.IsSuccess)
        {
            return ToActionResult(res);
        }

        return File(res.Value, "application/pdf");
    }
}
```

- [ ] **Step 4: Register handlers in DI and update CompositionRootTests**

In `HealthExam.API/Extensions/ApplicationServiceExtensions.cs`:
Register:
- `services.AddScoped<IGetExamGroupRegistrationFormsHandler, GetExamGroupRegistrationFormsHandler>();`
- `services.AddScoped<IListAvailableExamGroupsHandler, ListAvailableExamGroupsHandler>();`
- `services.AddScoped<IGetRegistrationFormHandler, GetRegistrationFormHandler>();`
- `services.AddScoped<ISaveRegistrationFormSectionHandler, SaveRegistrationFormSectionHandler>();`
- `services.AddScoped<IPreviewRegistrationFormPdfHandler, PreviewRegistrationFormPdfHandler>();`

In `HealthExam.Tests/API/CompositionRootTests.cs`:
Add `[InlineData(typeof(IGetExamGroupRegistrationFormsHandler))]`, etc., asserting all 5 handlers are registered and resolvable.

- [ ] **Step 5: Run tests and verify GREEN**

Run: `DOTNET_ROLL_FORWARD=LatestMajor dotnet test HealthExam.Tests/HealthExam.Tests.csproj --filter "FullyQualifiedName~RegistrationFormEndpointTests|FullyQualifiedName~CompositionRootTests"`
Expected: All tests PASS.

- [ ] **Step 6: Commit**

```bash
git add HealthExam.API/Controllers/ExamGroupController.cs HealthExam.API/Controllers/ExamRecordRegistrationFormController.cs HealthExam.API/Extensions/ApplicationServiceExtensions.cs HealthExam.Tests/API/RegistrationFormEndpointTests.cs HealthExam.Tests/API/CompositionRootTests.cs
git commit -m "feat(api): expose registration form and pdf preview endpoints"
```

---

### Task 5: End-to-End Verification, Swagger Surface & Full Regression

**Files:**
- Modify: `HealthExam.Tests/API/ApiContractSurfaceTests.cs:60-120`
- Modify: `HealthExam.Tests/ArchitectureDependencyTests.cs:1-60`
- Test: Full test suite (`dotnet test`)

**Interfaces:**
- Consumes: All routes and architectural rules
- Produces: Verified branch ready for merge/PR

- [ ] **Step 1: Update ApiContractSurfaceTests for new Swagger paths**

In `HealthExam.Tests/API/ApiContractSurfaceTests.cs`, assert that Swagger document includes:
- `/v1/exam-groups/available` (GET)
- `/v1/exam-groups/{variantCode}/registration-form` (GET)
- `/v1/exam-records/{recordId}/registration-form` (GET)
- `/v1/exam-records/{recordId}/registration-form/sections/{sectionKind}` (PUT)
- `/v1/exam-records/{recordId}/registration-form/preview` (GET, response 200 has content type `application/pdf`)

- [ ] **Step 2: Run ApiContractSurfaceTests to verify**

Run: `DOTNET_ROLL_FORWARD=LatestMajor dotnet test HealthExam.Tests/HealthExam.Tests.csproj --filter FullyQualifiedName~ApiContractSurfaceTests`
Expected: PASS with all Swagger endpoints verified.

- [ ] **Step 3: Run ArchitectureDependencyTests to verify Clean Architecture rules**

Run: `DOTNET_ROLL_FORWARD=LatestMajor dotnet test HealthExam.Tests/HealthExam.Tests.csproj --filter FullyQualifiedName~ArchitectureDependencyTests`
Expected: PASS (0 external NuGet packages in Domain and Application, no EF Core or HttpClient leaked).

- [ ] **Step 4: Run full regression test suite with local PostgreSQL**

Export DB environment variable:
`export HEALTHEXAM_TEST_DB="Host=127.0.0.1;Port=54329;Database=healthexam_test;User ID=postgres;Password=postgres"`

Run: `DOTNET_ROLL_FORWARD=LatestMajor TZ=Asia/Ho_Chi_Minh dotnet test HealthExam.Tests/HealthExam.Tests.csproj`
Expected: 100% tests PASS (>= 850 tests, 0 failed, 0 warnings, 0 errors).

- [ ] **Step 5: Commit**

```bash
git add HealthExam.Tests/API/ApiContractSurfaceTests.cs
git commit -m "test: verify swagger contract and regression suite for registration forms and pdf preview"
```
