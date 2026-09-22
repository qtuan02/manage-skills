# Registration Master Data Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Cung cấp đủ master data và dữ liệu hồ sơ cho wizard đăng ký KSK, đồng thời tách trạng thái hủy thành “Hủy đăng ký” và “Hủy khám”.

**Architecture:** `health-exam-server` sở hữu bảng master data đa category theo tenant, kết hợp option tĩnh trong code và trả qua `MasterDataController`. `ExamRecordService` nhận code, resolve tên tại backend và lưu code/name snapshot; API cancel tự suy ra trạng thái hủy. `form-server` và `iam-server` không thay đổi.

**Tech Stack:** .NET 6, ASP.NET Core, Entity Framework Core 6, PostgreSQL, Newtonsoft.Json, xUnit.

**Spec:** `docs/superpowers/specs/2026-09-07-registration-master-data-design.md`

## Global Constraints

- Chỉ thay đổi `health-exam-server`; tiếp tục dùng DB riêng và schema `public`.
- Không tạo shared schema, cross-database foreign key hoặc distributed transaction.
- Giữ numeric state `4` cho dữ liệu cũ (`RegistrationCancelled`); thêm `ExamCancelled=5`.
- Cột mới nullable/default rỗng; request và import cũ phải tiếp tục chạy.
- FE gửi option code; backend resolve và lưu code/name snapshot, không tin name từ FE.
- `ExamReason` và nội dung nguồn khác chỉ bắt buộc khi confirm.
- Mọi query master data phải lọc `IHealthExamContext.DivisionId` và `IsActive`.
- Trước khi sửa mỗi symbol, chạy GitNexus `impact` theo `AGENTS.md`; HIGH/CRITICAL phải báo người dùng.
- Trước mỗi commit, chạy GitNexus `detect_changes`. Không stage thay đổi sẵn có ở `Program.cs`, `JwtSetup.cs`, `.claude/`, `AGENTS.md`, `CLAUDE.md`.

## File Structure

- `HealthExam.Core/EntityFramework/Entity/MasterDataOption.cs`: entity danh mục cấu hình.
- `HealthExam.Core/Models/MasterDataCategories.cs`: category constants và static options.
- `HealthExam.Server/Models/MasterDataModels.cs`: contract API master data.
- `HealthExam.Server/Service/MasterDataService.cs`: query tenant, search geography, resolve snapshot.
- `HealthExam.API/Controllers/MasterDataController.cs`: ba endpoint read-only.
- `HealthExam.Tests/MasterDataTests.cs`: test service master data.
- `HealthExam.Tests/MasterDataEndpointTests.cs`: test HTTP contract.
- `HealthExam.Tests/ExamRecordRegistrationFieldsTests.cs`: mapping, validation, snapshot.
- `HealthExam.Tests/ExamRecordCancellationTests.cs`: state machine, audit, idempotency.

---

### Task 1: Persistence model và migration

**Files:**
- Create: `HealthExam.Core/EntityFramework/Entity/MasterDataOption.cs`
- Create: `HealthExam.Core/Models/MasterDataCategories.cs`
- Modify: `HealthExam.Core/EntityFramework/Entity/ExamRecord.cs`
- Modify: `HealthExam.Core/Models/RecordFieldLengths.cs`
- Modify: `HealthExam.Core/Models/Enums.cs`
- Modify: `HealthExam.Core/EntityFramework/HealthExamDbContext.cs`
- Modify: `HealthExam.Core/EntityFramework/Repositories/UnitOfWork.cs`
- Create: EF-generated `AddRegistrationMasterData` migration source in `HealthExam.Core/Migrations/`
- Create: EF-generated `AddRegistrationMasterData` migration designer in `HealthExam.Core/Migrations/`
- Modify: `HealthExam.Core/Migrations/HealthExamDbContextModelSnapshot.cs`
- Test: `HealthExam.Tests/RegistrationPersistenceModelTests.cs`

**Interfaces:**
- Produces: `MasterDataOption`, `MasterDataCategories`, two cancel states, `ExamRecordStates.IsCancelled`, `IUnitOfWork.MasterDataOptions`.
- Consumes: `AuditableEntity`, `RecordFieldLengths`, EF conventions.

- [ ] **Step 1: Analyze impact**

Run GitNexus upstream impact with tests for `ExamRecord`, `ExamRecordState`, `HealthExamDbContext`, `IUnitOfWork`, and `UnitOfWork`. Record direct consumers before editing.

- [ ] **Step 2: Write failing EF model tests**

~~~csharp
[Fact]
public void Master_data_has_tenant_unique_key_and_ward_index()
{
    using var db = Db();
    var type = db.Model.FindEntityType(typeof(MasterDataOption));
    Assert.Equal("HEX_MasterDataOption", type!.GetTableName());
    Assert.Contains(type.GetIndexes(), x => x.IsUnique &&
        x.Properties.Select(p => p.Name).SequenceEqual(new[] { "DivisionID", "Category", "Code" }));
    Assert.Contains(type.GetIndexes(), x =>
        x.Properties.Select(p => p.Name).SequenceEqual(new[] { "DivisionID", "Category", "ParentCode", "IsActive" }));
}

[Fact]
public void Active_person_indexes_exclude_both_cancel_states()
{
    using var db = Db();
    var filters = db.Model.FindEntityType(typeof(ExamRecord))!.GetIndexes()
        .Select(x => x.GetFilter()).Where(x => x != null);
    Assert.Contains(filters, x => x!.Contains("NOT IN (4, 5)"));
}
~~~

Add a theory checking properties `EthnicityCode`, `ProvinceName`, `IdentityIssuedDate`, `ExamReason`, and `ExamLocationName`. `Db()` uses `UseInMemoryDatabase(Guid.NewGuid().ToString())`.

- [ ] **Step 3: Verify RED**

~~~bash
dotnet test HealthExam.Tests/HealthExam.Tests.csproj --filter FullyQualifiedName~RegistrationPersistenceModelTests
~~~

Expected: compile failure because `MasterDataOption` is missing.

- [ ] **Step 4: Add domain types and record columns**

~~~csharp
public class MasterDataOption : AuditableEntity
{
    public Guid OptionID { get; set; }
    public string DivisionID { get; set; } = "";
    public string Category { get; set; } = "";
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string ParentCode { get; set; } = "";
    public int OrderNo { get; set; }
    public bool IsActive { get; set; } = true;
}

public static class MasterDataCategories
{
    public const string Ethnicity = "ETHNICITY", Occupation = "OCCUPATION";
    public const string Province = "PROVINCE", Ward = "WARD";
    public const string IdentityIssuer = "IDENTITY_ISSUER", InsuranceObject = "INSURANCE_OBJECT";
    public const string PatientType = "PATIENT_TYPE", PaymentSource = "PAYMENT_SOURCE";
    public const string ExamLocation = "EXAM_LOCATION", OtherPaymentSource = "OTHER";
}

public enum ExamRecordState : short
{
    NotRegistered = 0, Waiting = 1, InProgress = 2, Completed = 3,
    RegistrationCancelled = 4, ExamCancelled = 5
}

public static class ExamRecordStates
{
    public static bool IsCancelled(ExamRecordState state)
        => state is ExamRecordState.RegistrationCancelled or ExamRecordState.ExamCancelled;
}
~~~

Add these `ExamRecord` fields (each string defaults to `""`): `EthnicityCode/Name`, `OccupationCode/Name`, `BloodAboCode/Name`, `BloodRhCode/Name`, `ProvinceCode/Name`, `WardCode/Name`, `IdentityIssuedDate`, `IdentityIssuerCode/Name`, `RelativeRelationshipCode/Name`, `RelativeFullName`, `RelativeIdentityNumber`, `RelativePhoneNumber`, `InsuranceObjectCode/Name`, `InsuranceValidFrom/To`, `ExamReason`, `PatientTypeCode/Name`, `PaymentSourceCode/Name`, `PaymentSourceOther`, `ExamLocationCode/Name`.

Add shared lengths: category/code `50`, names/reason/location `500`, person names `255`, phone/identity `20`. Add every writable string to `RecordFieldLengths.MaxOf`.

- [ ] **Step 5: Configure EF and repository**

~~~csharp
e.ToTable("HEX_MasterDataOption");
e.HasKey(x => x.OptionID);
e.Property(x => x.OptionID).HasDefaultValueSql("gen_random_uuid()");
e.Property(x => x.DivisionID).HasMaxLength(20).HasDefaultValue("");
e.Property(x => x.Category).HasMaxLength(50).IsRequired();
e.Property(x => x.Code).HasMaxLength(50).IsRequired();
e.Property(x => x.Name).HasMaxLength(500).IsRequired();
e.Property(x => x.ParentCode).HasMaxLength(50).HasDefaultValue("");
e.Property(x => x.IsActive).HasDefaultValue(true);
e.HasIndex(x => new { x.DivisionID, x.Category, x.Code }).IsUnique();
e.HasIndex(x => new { x.DivisionID, x.Category, x.IsActive, x.OrderNo });
e.HasIndex(x => new { x.DivisionID, x.Category, x.ParentCode, x.IsActive });
~~~

Add `DbSet`/repository, configure every new string with shared lengths, and change both record partial indexes to `"State" NOT IN (4, 5)`.

- [ ] **Step 6: Generate migration and verify GREEN**

~~~bash
dotnet ef migrations add AddRegistrationMasterData --project HealthExam.Core --startup-project HealthExam.API
dotnet test HealthExam.Tests/HealthExam.Tests.csproj --filter FullyQualifiedName~RegistrationPersistenceModelTests
dotnet build HealthExamServer.sln --no-restore
~~~

Inspect `Up`: only add table/columns and recreate filtered indexes; no row rewrite or schema creation.

- [ ] **Step 7: Detect scope and commit**

Run GitNexus detect changes. Stage only Task 1 files and commit:

~~~bash
git commit -m "feat: add registration master data persistence"
~~~

### Task 2: Master-data service và API

**Files:**
- Modify: `HealthExam.Core/Models/MasterDataCategories.cs`
- Create: `HealthExam.Server/Models/MasterDataModels.cs`
- Create: `HealthExam.Server/Service/MasterDataService.cs`
- Modify: `HealthExam.Server/Service/DependencyInjection.cs`
- Create: `HealthExam.API/Controllers/MasterDataController.cs`
- Modify: `HealthExam.Tests/InMemoryTestDb.cs`
- Test: `HealthExam.Tests/MasterDataTests.cs`
- Test: `HealthExam.Tests/MasterDataEndpointTests.cs`

**Interfaces:**
- Produces: `MasterDataOptionItem`, `RegistrationOptionsItem`, three GET routes, `ResolveAsync`, `ResolveWardAsync`.
- Consumes: `IUnitOfWork.MasterDataOptions`, tenant context, `ExamGroups.All`.

- [ ] **Step 1: Analyze impact and write failing tests**

Analyze `DependencyInjection.AddHealthExamServices` and `InMemoryTestDb`. Tests prove static ABO/status; active/current-tenant rows only; case-insensitive search; ward-parent constraint; inactive/wrong-category rejection; standard envelope; missing province gives 400; anonymous gives 401.

~~~csharp
db.SeedMaster("ETHNICITY", "KINH", "Kinh");
db.SeedMaster("ETHNICITY", "HIDDEN", "Ẩn", isActive: false);
db.SeedMaster("ETHNICITY", "OTHER", "Tenant khác", divisionId: "OTHER");
var result = await db.MasterData.GetRegistrationOptionsAsync();
Assert.Contains(result.BloodAbos, x => x.Code == "AB");
Assert.Contains(result.ExamRecordStates, x => x.Code == "4" && x.Name == "Hủy đăng ký");
Assert.Contains(result.ExamRecordStates, x => x.Code == "5" && x.Name == "Hủy khám");
Assert.Single(result.Ethnicities);
~~~

Run focused tests. Expected: compile failure for absent service/models.

- [ ] **Step 2: Implement contracts and static values**

~~~csharp
public record MasterDataOptionItem(string Code, string Name, int OrderNo);

public class RegistrationOptionsItem
{
    public IReadOnlyList<MasterDataOptionItem> Genders { get; init; } = Array.Empty<MasterDataOptionItem>();
    public IReadOnlyList<MasterDataOptionItem> Ethnicities { get; init; } = Array.Empty<MasterDataOptionItem>();
    public IReadOnlyList<MasterDataOptionItem> Occupations { get; init; } = Array.Empty<MasterDataOptionItem>();
    public IReadOnlyList<MasterDataOptionItem> BloodAbos { get; init; } = Array.Empty<MasterDataOptionItem>();
    public IReadOnlyList<MasterDataOptionItem> BloodRhs { get; init; } = Array.Empty<MasterDataOptionItem>();
    public IReadOnlyList<MasterDataOptionItem> IdentityIssuers { get; init; } = Array.Empty<MasterDataOptionItem>();
    public IReadOnlyList<MasterDataOptionItem> Relationships { get; init; } = Array.Empty<MasterDataOptionItem>();
    public IReadOnlyList<MasterDataOptionItem> InsuranceObjects { get; init; } = Array.Empty<MasterDataOptionItem>();
    public IReadOnlyList<MasterDataOptionItem> PatientTypes { get; init; } = Array.Empty<MasterDataOptionItem>();
    public IReadOnlyList<MasterDataOptionItem> PaymentSources { get; init; } = Array.Empty<MasterDataOptionItem>();
    public IReadOnlyList<MasterDataOptionItem> ExamLocations { get; init; } = Array.Empty<MasterDataOptionItem>();
    public IReadOnlyList<MasterDataOptionItem> ExamRecordStates { get; init; } = Array.Empty<MasterDataOptionItem>();
    public IReadOnlyList<ExamGroupItem> ExamGroups { get; init; } = Array.Empty<ExamGroupItem>();
}
~~~

Exact static values: gender `1/Nam, 2/Nữ, 3/Khác`; ABO `A,B,AB,O`; Rh `+,-`; relationship `FATHER/Cha, MOTHER/Mẹ, SPOUSE/Vợ-chồng, CHILD/Con, GUARDIAN/Người giám hộ, OTHER/Khác`; states `0..5` with approved names.

- [ ] **Step 3: Implement tenant-scoped service**

~~~csharp
Task<RegistrationOptionsItem> GetRegistrationOptionsAsync(CancellationToken ct = default);
Task<IReadOnlyList<MasterDataOptionItem>> ListProvincesAsync(string keyword, CancellationToken ct = default);
Task<IReadOnlyList<MasterDataOptionItem>> ListWardsAsync(string provinceCode, string keyword, CancellationToken ct = default);
Task<MasterDataOptionItem?> ResolveAsync(string category, string code, string field, CancellationToken ct = default);
Task<MasterDataOptionItem?> ResolveWardAsync(string provinceCode, string wardCode, string field, CancellationToken ct = default);
~~~

Every query begins with tenant/category/active predicates. Blank optional code returns null; invalid nonblank code throws `BadRequest` with `ValidationErrors.Of(field, "Mã danh mục không hợp lệ")`. Missing/invalid province is validation error. Search matches trimmed lower-case code/name and orders by `OrderNo`, then `Name`. Register scoped service and add `MasterData` plus `SeedMaster(...)` to test DB.

- [ ] **Step 4: Implement thin controller**

~~~csharp
[Route("v1/master-data")]
public class MasterDataController : HealthExamControllerBase
{
    [HttpGet("registration-options")]
    public async Task<ActionResult<ResultData<RegistrationOptionsItem>>> RegistrationOptions(CancellationToken ct)
        => Success(await _service.GetRegistrationOptionsAsync(ct));

    [HttpGet("provinces")]
    public async Task<ActionResult<ResultData<IReadOnlyList<MasterDataOptionItem>>>> Provinces(
        [FromQuery] string keyword = null, CancellationToken ct = default)
        => Success(await _service.ListProvincesAsync(keyword, ct));

    [HttpGet("wards")]
    public async Task<ActionResult<ResultData<IReadOnlyList<MasterDataOptionItem>>>> Wards(
        [FromQuery] string provinceCode = null, [FromQuery] string keyword = null, CancellationToken ct = default)
        => Success(await _service.ListWardsAsync(provinceCode, keyword, ct));
}
~~~

- [ ] **Step 5: Verify and commit**

~~~bash
dotnet test HealthExam.Tests/HealthExam.Tests.csproj --filter "FullyQualifiedName~MasterDataTests|FullyQualifiedName~MasterDataEndpointTests"
~~~

Expected: PASS. Run detect changes, stage only Task 2 files, commit `feat: expose registration master data APIs`.

### Task 3: Store code/name snapshots on exam records

**Files:**
- Modify: `HealthExam.Server/Models/ExamRecordModels.cs`
- Modify: `HealthExam.Server/Service/ExamRecordService.cs`
- Modify: `HealthExam.Tests/InMemoryTestDb.cs`
- Test: `HealthExam.Tests/ExamRecordRegistrationFieldsTests.cs`

**Interfaces:**
- Consumes: `MasterDataService.ResolveAsync/ResolveWardAsync` and Task 1 columns.
- Produces: expanded request/response and atomic snapshot mapping.

- [ ] **Step 1: Analyze impact and write failing tests**

Analyze `ExamRecordSaveRequest`, `ExamRecordItem`, `ExamRecordService.ApplyAsync`, `ToItem` and constructor. Tests: create/update saves canonical names; old request succeeds; ward from another province fails; inactive code fails; invalid ABO/Rh/relationship fails; BHYT end-before-start fails; master rename does not alter stored snapshot; clearing code clears name.

~~~csharp
var item = await db.Records.CreateAsync(new ExamRecordSaveRequest {
    SessionID = session.SessionID, FullName = "Nguyễn Văn A", VariantCode = "DTK_01",
    EthnicityCode = "KINH", ProvinceCode = "92", WardCode = "26734"
});
Assert.Equal("Kinh", item.EthnicityName);
Assert.Equal("Cần Thơ", item.ProvinceName);
Assert.Equal("Phường An Cư", item.WardName);
~~~

Run focused tests. Expected: compile failure for missing properties.

- [ ] **Step 2: Extend request and response**

Writable request properties are: all new `*Code` values, `IdentityIssuedDate`, relative text fields, `InsuranceValidFrom/To`, `ExamReason` and `PaymentSourceOther`. Do not expose writable `*Name`. Response contains every code/name/date/text pair listed in the spec.

- [ ] **Step 3: Inject resolver and map atomically**

Use constructor:

~~~csharp
public ExamRecordService(
    IUnitOfWork uow, IHealthExamContext ctx, ExamSessionService sessions,
    MasterDataService masterData, AuditService audit,
    ILogger<ExamRecordService> logger = null)
~~~

For each DB option:

~~~csharp
var ethnicity = await _masterData.ResolveAsync(
    MasterDataCategories.Ethnicity, req.EthnicityCode, nameof(req.EthnicityCode), ct);
entity.EthnicityCode = ethnicity?.Code ?? "";
entity.EthnicityName = ethnicity?.Name ?? "";
~~~

Resolve ward through `ResolveWardAsync`. Resolve static ABO/Rh/relationship against exact Task 2 lists. Trim text. Enforce `InsuranceValidTo >= InsuranceValidFrom`. Preserve `PaymentSourceOther` only for `PaymentSourceCode == OTHER`; otherwise clear it. Map all new fields in `ToItem`. Update test composition root.

- [ ] **Step 4: Verify and commit**

~~~bash
dotnet test HealthExam.Tests/HealthExam.Tests.csproj --filter "FullyQualifiedName~ExamRecordRegistrationFieldsTests|FullyQualifiedName~RecordCodeTests|FullyQualifiedName~ExamImportTests"
~~~

Expected: PASS. Run detect changes and commit `feat: persist registration option snapshots`.

### Task 4: Confirm-time registration validation

**Files:**
- Modify: `HealthExam.Server/Service/ExamRecordService.cs`
- Modify: `HealthExam.Tests/ExamRecordRegistrationFieldsTests.cs`

**Interfaces:**
- Consumes: stored `ExamReason`, `PaymentSourceCode`, `PaymentSourceOther`.
- Produces: aggregate validation before `NotRegistered -> Waiting`.

- [ ] **Step 1: Analyze impact and write failing tests**

Analyze `ConfirmAsync`. Prove draft saves without reason but confirm fails; `OTHER` without text fails; both values present succeeds. Assert state and registered timestamps remain unchanged on failure.

- [ ] **Step 2: Verify RED and implement**

~~~csharp
if (string.IsNullOrWhiteSpace(entity.ExamReason))
    errors.Errors.Add(new ValidationError { Field = nameof(entity.ExamReason), Reason = "Bỏ trống (bắt buộc)" });
if (entity.PaymentSourceCode == MasterDataCategories.OtherPaymentSource &&
    string.IsNullOrWhiteSpace(entity.PaymentSourceOther))
    errors.Errors.Add(new ValidationError { Field = nameof(entity.PaymentSourceOther), Reason = "Bỏ trống (bắt buộc)" });
if (errors.Errors.Count > 0)
    throw HealthExamException.BadRequest("Thông tin đăng ký chưa đầy đủ", errors);
~~~

Validation must run before state/timestamp mutation.

- [ ] **Step 3: Verify and commit**

~~~bash
dotnet test HealthExam.Tests/HealthExam.Tests.csproj --filter "FullyQualifiedName~ExamRecordRegistrationFieldsTests|FullyQualifiedName~ExamSessionCloseReopenTests"
~~~

Expected: PASS. Run detect changes and commit `feat: validate registration details on confirm`.

### Task 5: Cancel registration/exam API

**Files:**
- Modify: `HealthExam.Server/Models/ExamRecordModels.cs`
- Modify: `HealthExam.Server/Service/ExamRecordService.cs`
- Modify: `HealthExam.API/Controllers/ExamRecordController.cs`
- Test: `HealthExam.Tests/ExamRecordCancellationTests.cs`

**Interfaces:**
- Produces: `ExamRecordCancelRequest`, `CancelAsync`, `POST /v1/exam-records/{recordId}/cancel`.
- Consumes: `AuditService.StateChange`, `ExamRecordStates.IsCancelled`, `GuardSessionWritable`.

- [ ] **Step 1: Analyze impact and write failing tests**

Analyze service/controller/route. Test `0→4`, `1→5`, `2→5`, completed rejection, blank reason, 404, closed session, audit payload and idempotency.

~~~csharp
[Theory]
[InlineData(ExamRecordState.NotRegistered, ExamRecordState.RegistrationCancelled)]
[InlineData(ExamRecordState.Waiting, ExamRecordState.ExamCancelled)]
[InlineData(ExamRecordState.InProgress, ExamRecordState.ExamCancelled)]
public async Task Cancel_derives_target(ExamRecordState from, ExamRecordState expected)
{
    using var db = new InMemoryTestDb();
    var session = db.SeedSession();
    var record = db.SeedRecord(session.SessionID, from);
    var item = await db.Records.CancelAsync(record.RecordID,
        new ExamRecordCancelRequest { Reason = "Người bệnh yêu cầu" });
    Assert.Equal(expected, item.State);
    Assert.Single(db.Db.AuditLogs.Where(x => x.EntityID == record.RecordID));
}
~~~

Repeated cancel must keep original reason and exactly one audit row.

- [ ] **Step 2: Verify RED and implement service**

~~~csharp
public class ExamRecordCancelRequest { public string Reason { get; set; } }
~~~

`CancelAsync` order: load tracking tenant record; if already `4/5` return current item without revalidation/audit; require trimmed reason; guard writable session; map `0→4` and `1/2→5`, reject `3`; stamp cancel/modified fields; add `AuditEntityTypes.Record` state-change with reason; call one `SaveChangesAsync`; return item.

- [ ] **Step 3: Add endpoint**

~~~csharp
[HttpPost("{recordId:guid}/cancel")]
public async Task<ActionResult<ResultData<ExamRecordItem>>> Cancel(
    Guid recordId, [FromBody] ExamRecordCancelRequest request, CancellationToken ct = default)
    => Success(await _service.CancelAsync(recordId, request ?? new ExamRecordCancelRequest(), ct));
~~~

Update the controller comment that currently says `/cancel` is absent.

- [ ] **Step 4: Verify and commit**

~~~bash
dotnet test HealthExam.Tests/HealthExam.Tests.csproj --filter "FullyQualifiedName~ExamRecordCancellationTests|FullyQualifiedName~ExamSessionCloseReopenTests"
~~~

Expected: PASS. Run detect changes and commit `feat: distinguish registration and exam cancellation`.

### Task 6: Update every cancelled-state consumer

**Files:**
- Modify: `HealthExam.Server/Service/StateNames.cs`
- Modify: `HealthExam.Server/Service/PortalCredentialService.cs`
- Modify: `HealthExam.Server/Service/ExamImportService.cs`
- Modify: `HealthExam.Server/Service/ExamSessionService.cs`
- Modify: `HealthExam.Server/Service/ExamRecordService.cs`
- Modify: `HealthExam.Server/Service/WebhookProcessor.cs`
- Modify: `HealthExam.Server/Service/ParaclinicalOrderService.cs`
- Modify: `HealthExam.Server/Service/ExamSessionProgressService.cs`
- Modify: `HealthExam.Server/Service/ProgressReconciliationService.cs`
- Modify: `HealthExam.Server/Models/WebhookModels.cs`
- Modify: `HealthExam.Tests/PortalCredentialTests.cs`
- Modify: `HealthExam.Tests/SessionProgressTests.cs`
- Modify: `HealthExam.Tests/ProgressReconciliationTests.cs`
- Modify: `HealthExam.Tests/WebhookProcessorTests.cs`
- Modify: `HealthExam.Tests/ParaclinicalOrderTests.cs`

**Interfaces:**
- Produces: backward-compatible `Profiles.Cancelled` total plus two detail counts.
- Consumes: `ExamRecordStates.IsCancelled` and both terminal states.

- [ ] **Step 1: Analyze all guards and write failing theories**

Enumerate `ExamRecordState.Cancelled` references with `rg`. Run impact on each containing method. Convert existing terminal-state tests to theories over both `RegistrationCancelled` and `ExamCancelled` for credential, order, webhook and reconcile behavior. Add progress assertions:

~~~csharp
Assert.Equal(2, result.Profiles.Cancelled);
Assert.Equal(1, result.Profiles.RegistrationCancelled);
Assert.Equal(1, result.Profiles.ExamCancelled);
~~~

- [ ] **Step 2: Verify RED and replace semantic guards**

For tracked objects use `ExamRecordStates.IsCancelled(record.State)`. In EF expressions use:

~~~csharp
x.State != ExamRecordState.RegistrationCancelled &&
x.State != ExamRecordState.ExamCancelled
~~~

Do not alter `ExamSessionState.Cancelled` or `ParaclinicalItemState.Cancelled`.

~~~csharp
ExamRecordState.RegistrationCancelled => "Hủy đăng ký",
ExamRecordState.ExamCancelled => "Hủy khám",
~~~

Progress model/service:

~~~csharp
public int Cancelled { get; set; }
public int RegistrationCancelled { get; set; }
public int ExamCancelled { get; set; }

RegistrationCancelled = CountOf(counts, ExamRecordState.RegistrationCancelled),
ExamCancelled = CountOf(counts, ExamRecordState.ExamCancelled),
Cancelled = CountOf(counts, ExamRecordState.RegistrationCancelled)
          + CountOf(counts, ExamRecordState.ExamCancelled),
~~~

- [ ] **Step 3: Prove no stale record-state reference remains**

~~~bash
rg -n "ExamRecordState\\.Cancelled" HealthExam.Core HealthExam.Server HealthExam.API HealthExam.Tests
~~~

Expected: no match.

- [ ] **Step 4: Verify and commit**

~~~bash
dotnet test HealthExam.Tests/HealthExam.Tests.csproj --filter "FullyQualifiedName~PortalCredentialTests|FullyQualifiedName~SessionProgressTests|FullyQualifiedName~ProgressReconciliationTests|FullyQualifiedName~WebhookProcessorTests|FullyQualifiedName~ParaclinicalOrderTests"
~~~

Expected: PASS. Run detect changes and commit `fix: treat both record cancellation states as terminal`.

### Task 7: Deployment SQL, mock seed and full verification

**Files:**
- Modify: `Deploy/schema/health-exam-schema.sql`
- Modify: `Deploy/seed/health-exam-seed.sql`
- Modify: `README.md`

**Interfaces:**
- Produces: fresh-install DDL, renderable mock options and API documentation.
- Consumes: final migration and routes.

- [ ] **Step 1: Verify deployment artifacts are RED**

~~~bash
rg -n 'CREATE TABLE "HEX_MasterDataOption"' Deploy/schema/health-exam-schema.sql
rg -n '"ExamReason"' Deploy/schema/health-exam-schema.sql
rg -n 'OTHER.*Nguồn khác' Deploy/seed/health-exam-seed.sql
~~~

Expected: all fail before editing.

- [ ] **Step 2: Mirror migration in fresh-install SQL**

Add the master table, indexes, all new record columns and `State NOT IN (4,5)` filters. Do not add schema/database creation.

- [ ] **Step 3: Add idempotent mock seed**

Use existing tenant convention and `ON CONFLICT (DivisionID, Category, Code) DO UPDATE`. Seed:

~~~text
ETHNICITY: KINH/Kinh
OCCUPATION: DRIVER/Lái xe
PROVINCE: 92/Cần Thơ
WARD: 26734/Phường An Cư, ParentCode=92
IDENTITY_ISSUER: CAN_THO_POLICE/Công an Thành phố Cần Thơ
INSURANCE_OBJECT: FEE/Thu phí; HI/Bảo hiểm y tế
PATIENT_TYPE: OUTPATIENT/Ngoại trú
PAYMENT_SOURCE: STATE_CONTRACT/Ngân sách NN - Khám theo hợp đồng;
                STATE_NON_LOCAL/Ngân sách NN - Khám phi địa giới;
                SELF/Người dân tự chi trả; OTHER/Nguồn khác
EXAM_LOCATION: CLINIC_3_F2/Phòng khám 3 - Tầng 2
~~~

Do not include credentials. Document that geography is mock data, not a complete national catalog.

- [ ] **Step 4: Document routes and state values**

README lists three master routes, cancel route, and `4/5` meanings.

- [ ] **Step 5: Full verification**

~~~bash
dotnet ef migrations script --idempotent --project HealthExam.Core --startup-project HealthExam.API --output /tmp/health-exam-registration.sql
dotnet build HealthExamServer.sln --no-restore
dotnet test HealthExamServer.sln --no-build
git diff --check
~~~

Expected: migration generates, build passes, all tests pass, diff is clean. Report any environment-gated PostgreSQL tests as skipped rather than passed.

- [ ] **Step 6: Acceptance audit**

Confirm: static/tenant/active options; province-ward constraint; snapshots and old requests; confirm validation; `0→4`, `1/2→5` and idempotency; both cancel states terminal across webhook/reconcile/credentials/import/orders/update; compatible progress total; no row rewrite/shared schema.

- [ ] **Step 7: Detect scope and commit**

Run GitNexus detect changes, verify no pre-existing files are staged, and commit Task 7 as `docs: publish registration master data deployment assets`. Finish with:

~~~bash
git status --short --branch
git log --oneline --decorate -8
~~~
