# Patient Profile Search and Reuse Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Tìm và tái sử dụng hồ sơ người bệnh active theo CCCD, đồng thời version hóa thông tin cá nhân để mỗi đợt khám giữ đúng dữ liệu lịch sử.

**Architecture:** Tiếp tục dùng `HEX_Patient` làm bảng chứa các phiên bản và `ExamRecord.PatientRefID` làm con trỏ lịch sử. `PatientRegistrationWriter` là một điểm duy nhất tính diff, tái sử dụng hoặc tạo phiên bản; repository chịu trách nhiệm truy vấn theo tenant và chuyển active an toàn trong transaction. API tìm kiếm mới chỉ trả hồ sơ active theo CCCD và các API tạo/cập nhật hiện có nhận thêm quyết định nullable `SetAsActiveProfile`.

**Tech Stack:** .NET 8, ASP.NET Core controllers, EF Core 8, PostgreSQL/Npgsql, xUnit.

**Spec:** `docs/superpowers/specs/2026-09-16-patient-profile-search-and-reuse-design.md`

## Global Constraints

- Chỉ sửa `health-exam-server`; không sửa `his-server`.
- Làm trực tiếp trên checkout hiện tại; không tạo hoặc dùng git worktree.
- Tìm kiếm chỉ theo CCCD và chỉ trong `DivisionID` hiện tại.
- Chỉ hồ sơ `IsActive = true` xuất hiện trong tìm kiếm thông thường.
- Tái sử dụng hồ sơ yêu cầu CCCD, họ tên và ngày sinh cùng khớp; tên bỏ khác biệt hoa/thường và khoảng trắng nhưng vẫn phân biệt dấu.
- Không sửa tại chỗ một `Patient` đã được `ExamRecord` tham chiếu.
- Không thêm API tra cứu lịch sử/inactive, fuzzy search hoặc dependency mới.
- GitNexus đánh giá thay đổi `IPatientRepository` là **CRITICAL** do interface/DI boundary; trước mỗi task phải chạy `impact` cho từng symbol sắp sửa và không được coi caller rỗng/`UNKNOWN` là an toàn nếu chưa `rg` xác nhận.
- Trước mỗi commit phải chạy `node .gitnexus/run.cjs detect-changes --scope all --repo .`; kết quả partial/truncated phải chạy lại.
- Không stage hoặc commit hai file không liên quan đang untracked: `docs/health-exam-record-json-fields.md` và `docs/superpowers/specs/2026-09-15-his-clinical-progress-status-design.md`.

## File Structure

- Modify `HealthExam.Domain/Patients/Patient.cs`: trạng thái và liên kết phiên bản.
- Modify `HealthExam.Infrastructure/Persistence/HealthExamDbContext.cs`: mapping tự tham chiếu và filtered unique indexes.
- Create `HealthExam.Infrastructure/Persistence/Migrations/20260916120000_AddPatientProfileVersioning.cs`: schema, backfill active và thay indexes.
- Create `HealthExam.Infrastructure/Persistence/Migrations/20260916120000_AddPatientProfileVersioning.Designer.cs`: metadata migration do EF tạo.
- Modify `HealthExam.Infrastructure/Persistence/Migrations/HealthExamDbContextModelSnapshot.cs`: model snapshot.
- Modify `HealthExam.Application/Patients/PatientModels.cs`: quyết định active, search DTO/query và diff payload.
- Modify `HealthExam.Application/Patients/IPatientRepository.cs`: search active và thao tác chuyển active.
- Modify `HealthExam.Infrastructure/Persistence/Repositories/PatientRepository.cs`: triển khai query/update PostgreSQL.
- Modify `HealthExam.Application/Patients/PatientRegistrationWriter.cs`: versioning thay cho update tại chỗ.
- Create `HealthExam.Application/Patients/SearchActivePatient.cs`: use case tìm kiếm tenant-scoped.
- Create `HealthExam.API/Controllers/PatientController.cs`: `GET /v1/patients/search`.
- Modify `HealthExam.API/Contracts/ExamRecordModels.cs`: nhận `PatientRefID` và `SetAsActiveProfile` từ client.
- Modify `HealthExam.Application/ExamRecords/ExamRecordModels.cs`: truyền quyết định qua command.
- Modify `HealthExam.API/Controllers/ExamRecordController.cs`: map hai field mới vào create/update command.
- Modify `HealthExam.Application/ExamRecords/CreateExamRecord.cs`: xử lý result versioning và conflict.
- Modify `HealthExam.Application/ExamRecords/UpdateExamRecord.cs`: fork Patient khi sửa lịch sử.
- Modify `HealthExam.Application/Common/ApplicationFailure.cs`, `HealthExam.API/Contracts/ErrorCodes.cs`, `HealthExam.API/Middlewares/ApplicationResultMapper.cs`: lỗi `PatientProfileChanged` HTTP 409.
- Modify `HealthExam.API/Extensions/ApplicationServiceExtensions.cs`: đăng ký search handler.
- Modify `HealthExam.Tests/Application/PatientRegistrationTestDoubles.cs`: fake theo repository contract mới.
- Create `HealthExam.Tests/Application/PatientRegistrationWriterTests.cs`: unit tests diff/versioning.
- Create `HealthExam.Tests/Application/SearchActivePatientTests.cs`: unit tests tìm kiếm và tenant isolation.
- Modify `HealthExam.Tests/Application/ExamRecordHandlerTests.cs`: create/update không làm sai lịch sử.
- Modify `HealthExam.Tests/Infrastructure/RegistrationPersistenceTests.cs`: mapping và filtered indexes.
- Create `HealthExam.Tests/Infrastructure/PatientProfilePersistenceTests.cs`: PostgreSQL search, unique và concurrency.
- Create `HealthExam.Tests/PatientEndpointTests.cs`: contract endpoint và error envelope.

---

### Task 1: Add the Patient versioning schema and deterministic backfill

**Files:**
- Modify: `HealthExam.Domain/Patients/Patient.cs`
- Modify: `HealthExam.Infrastructure/Persistence/HealthExamDbContext.cs`
- Create: `HealthExam.Infrastructure/Persistence/Migrations/20260916120000_AddPatientProfileVersioning.cs`
- Create: `HealthExam.Infrastructure/Persistence/Migrations/20260916120000_AddPatientProfileVersioning.Designer.cs`
- Modify: `HealthExam.Infrastructure/Persistence/Migrations/HealthExamDbContextModelSnapshot.cs`
- Modify: `HealthExam.Tests/Infrastructure/RegistrationPersistenceTests.cs`

**Interfaces:**
- Produces: `Patient.IsActive : bool`, `Patient.PreviousPatientRefID : Guid?`, `Patient.PreviousPatient : Patient`.
- Produces: indexes `UX_HEX_Patient_Division_ActiveIdentity` and updated `UX_HEX_Patient_Division_HisPatientID`.

- [ ] **Step 1: Run graph impact before editing**

```bash
node .gitnexus/run.cjs impact "Patient" --direction upstream --repo .
node .gitnexus/run.cjs impact "ConfigurePatientRegistration" --direction upstream --repo .
rg -n "class Patient|ConfigurePatientRegistration|UX_HEX_Patient_Division_HisPatientID" HealthExam.*
```

Expected: record the reported risk; verify `Patient` is read by exam-record persistence and the existing HIS index lives in `HealthExamDbContext`.

- [ ] **Step 2: Write failing model assertions**

Add to `RegistrationPersistenceTests`:

```csharp
[Fact]
public void Patient_model_has_version_link_and_filtered_active_indexes()
{
    using var db = CreateContext();
    var entity = db.Model.FindEntityType(typeof(Patient))!;

    Assert.NotNull(entity.FindProperty(nameof(Patient.IsActive)));
    Assert.NotNull(entity.FindProperty(nameof(Patient.PreviousPatientRefID)));
    Assert.Contains(entity.GetForeignKeys(), fk =>
        fk.Properties.Single().Name == nameof(Patient.PreviousPatientRefID));

    var identity = entity.GetIndexes().Single(x =>
        x.GetDatabaseName() == "UX_HEX_Patient_Division_ActiveIdentity");
    Assert.True(identity.IsUnique);
    Assert.Equal("\"IsActive\" AND \"IdentityNumber\" <> ''", identity.GetFilter());

    var his = entity.GetIndexes().Single(x =>
        x.GetDatabaseName() == "UX_HEX_Patient_Division_HisPatientID");
    Assert.Equal("\"IsActive\" AND \"HisPatientID\" IS NOT NULL AND \"HisPatientID\" > 0", his.GetFilter());
}
```

- [ ] **Step 3: Run the model test and verify it fails**

```bash
dotnet test HealthExam.Tests/HealthExam.Tests.csproj --filter FullyQualifiedName~RegistrationPersistenceTests.Patient_model_has_version_link_and_filtered_active_indexes
```

Expected: FAIL because `Patient` has no version fields/index.

- [ ] **Step 4: Add the minimal domain and EF mapping**

Add to `Patient`:

```csharp
public bool IsActive { get; set; } = true;
public Guid? PreviousPatientRefID { get; set; }
public Patient PreviousPatient { get; set; }
```

In `ConfigurePatientRegistration`, configure the self-reference and replace the two relevant indexes:

```csharp
e.Property(x => x.IsActive).HasDefaultValue(true);
e.HasOne(x => x.PreviousPatient).WithMany()
 .HasForeignKey(x => x.PreviousPatientRefID)
 .OnDelete(DeleteBehavior.Restrict);

e.HasIndex(x => new { x.DivisionID, x.IdentityNumber })
 .HasDatabaseName("UX_HEX_Patient_Division_ActiveIdentity")
 .IsUnique()
 .HasFilter("\"IsActive\" AND \"IdentityNumber\" <> ''");

e.HasIndex(x => new { x.DivisionID, x.HisPatientID })
 .HasDatabaseName("UX_HEX_Patient_Division_HisPatientID")
 .IsUnique()
 .HasFilter("\"IsActive\" AND \"HisPatientID\" IS NOT NULL AND \"HisPatientID\" > 0");
```

- [ ] **Step 5: Generate and inspect the migration**

```bash
dotnet ef migrations add AddPatientProfileVersioning \
  --project HealthExam.Infrastructure/HealthExam.Infrastructure.csproj \
  --startup-project HealthExam.API/HealthExam.API.csproj \
  --output-dir Persistence/Migrations
```

If EF emits a different timestamp, keep its generated timestamp and update the plan checklist reference when tracking execution. Edit `Up` so backfill runs before filtered unique indexes:

```sql
WITH ranked AS (
    SELECT p."PatientRefID",
           ROW_NUMBER() OVER (
             PARTITION BY p."DivisionID", p."IdentityNumber"
             ORDER BY MAX(er."ModifiedDate") DESC NULLS LAST,
                      p."ModifiedDate" DESC,
                      p."PatientRefID") AS rn
    FROM "HEX_Patient" p
    LEFT JOIN "HEX_ExamRecord" er ON er."PatientRefID" = p."PatientRefID"
    WHERE p."IdentityNumber" <> ''
    GROUP BY p."PatientRefID", p."DivisionID", p."IdentityNumber", p."ModifiedDate"
)
UPDATE "HEX_Patient" p
SET "IsActive" = (ranked.rn = 1)
FROM ranked
WHERE p."PatientRefID" = ranked."PatientRefID";
```

The migration must not delete or merge rows. `Down` drops the self-FK/new columns and restores the original HIS filtered index.

- [ ] **Step 6: Run mapping tests and migration script generation**

```bash
dotnet test HealthExam.Tests/HealthExam.Tests.csproj --filter FullyQualifiedName~RegistrationPersistenceTests
dotnet ef migrations script --idempotent \
  --project HealthExam.Infrastructure/HealthExam.Infrastructure.csproj \
  --startup-project HealthExam.API/HealthExam.API.csproj \
  --output /tmp/health-exam-patient-profile.sql
rg -n "UX_HEX_Patient_Division_ActiveIdentity|ROW_NUMBER|PreviousPatientRefID" /tmp/health-exam-patient-profile.sql
```

Expected: tests PASS; script contains the backfill before creation of the active identity index.

- [ ] **Step 7: Analyze graph changes and commit**

```bash
node .gitnexus/run.cjs detect-changes --scope all --repo .
git add HealthExam.Domain/Patients/Patient.cs \
  HealthExam.Infrastructure/Persistence/HealthExamDbContext.cs \
  HealthExam.Infrastructure/Persistence/Migrations \
  HealthExam.Tests/Infrastructure/RegistrationPersistenceTests.cs
git commit -m "feat: add patient profile version schema"
```

---

### Task 2: Replace in-place Patient mutation with explicit version decisions

**Files:**
- Modify: `HealthExam.Application/Patients/PatientModels.cs`
- Modify: `HealthExam.Application/Patients/IPatientRepository.cs`
- Modify: `HealthExam.Infrastructure/Persistence/Repositories/PatientRepository.cs`
- Modify: `HealthExam.Application/Patients/PatientRegistrationWriter.cs`
- Modify: `HealthExam.Tests/Application/PatientRegistrationTestDoubles.cs`
- Create: `HealthExam.Tests/Application/PatientRegistrationWriterTests.cs`

**Interfaces:**
- Consumes: `Patient.IsActive`, `Patient.PreviousPatientRefID` from Task 1.
- Produces: `PatientWriteRequest.SetAsActiveProfile : bool?`.
- Produces: `PatientProfileChangedPayload(IReadOnlyList<string> ChangedFields)`.
- Produces: `Task<ApplicationResult<PatientWriteResult>> PatientRegistrationWriter.UpsertAsync(...)`.
- Produces repository methods `FindActiveByIdentityNumberAsync`, `DeactivateAsync`.

- [ ] **Step 1: Run graph impact and confirm the CRITICAL interface boundary**

```bash
node .gitnexus/run.cjs impact "IPatientRepository" --direction upstream --repo .
node .gitnexus/run.cjs impact "PatientRegistrationWriter.UpsertAsync" --direction upstream --repo .
rg -n "IPatientRepository|UpsertAsync\(" HealthExam.Application HealthExam.Infrastructure HealthExam.API HealthExam.Tests
```

Expected: `IPatientRepository` remains CRITICAL; all concrete implementations/fakes are listed before editing.

- [ ] **Step 2: Write failing versioning tests**

Create `PatientRegistrationWriterTests` covering these concrete assertions:

```csharp
[Fact]
public async Task Changed_profile_without_decision_returns_changed_fields()
{
    var active = ActivePatient(fullName: "Nguyễn  Văn An");
    var sut = CreateWriter(active);
    var result = await sut.UpsertAsync(Request(active.PatientRefID,
        fullName: "nguyễn văn an", phone: "0912000000", setActive: null));

    Assert.False(result.IsSuccess);
    Assert.Equal(ApplicationFailureCode.PatientProfileChanged, result.Failure.Code);
    var payload = Assert.IsType<PatientProfileChangedPayload>(result.Failure.Payload);
    Assert.Equal(new[] { nameof(Patient.PhoneNumber) }, payload.ChangedFields);
}

[Fact]
public async Task Changed_profile_declined_creates_inactive_version()
{
    var active = ActivePatient();
    var (sut, repo) = CreateWriterWithRepository(active);
    var result = await sut.UpsertAsync(Request(active.PatientRefID,
        phone: "0912000000", setActive: false));

    Assert.True(result.IsSuccess);
    Assert.True(active.IsActive);
    Assert.False(result.Value.Patient.IsActive);
    Assert.Equal(active.PatientRefID, result.Value.Patient.PreviousPatientRefID);
    Assert.Equal(2, repo.Patients.Count);
}

[Fact]
public async Task Changed_profile_accepted_replaces_active_version()
{
    var active = ActivePatient();
    var (sut, repo) = CreateWriterWithRepository(active);
    var result = await sut.UpsertAsync(Request(active.PatientRefID,
        phone: "0912000000", setActive: true));

    Assert.True(result.IsSuccess);
    Assert.False(active.IsActive);
    Assert.True(result.Value.Patient.IsActive);
    Assert.NotEqual(active.PatientRefID, result.Value.PatientRefID);
}
```

Also add tests for unchanged data reusing the same ID, new registration creating active, wrong-tenant/inactive source returning conflict, and name normalization preserving Vietnamese diacritics.

- [ ] **Step 3: Run tests and verify they fail**

```bash
dotnet test HealthExam.Tests/HealthExam.Tests.csproj --filter FullyQualifiedName~PatientRegistrationWriterTests
```

Expected: FAIL because result/failure/version semantics do not exist.

- [ ] **Step 4: Add request/result types and one comparison routine**

Extend `PatientWriteRequest` with `bool? SetAsActiveProfile`. Add:

```csharp
public sealed record PatientProfileChangedPayload(
    IReadOnlyList<string> ChangedFields);
```

Keep normalization/diff private to `PatientRegistrationWriter`; do not add a new service/interface. Normalize names with whitespace splitting and `StringComparison.OrdinalIgnoreCase`; compare all personal fields listed in the spec, and sort changed field names ordinally so the payload is deterministic.

- [ ] **Step 5: Extend the repository contract and both implementations**

Add:

```csharp
Task<Patient> FindActiveByIdentityNumberAsync(
    string divisionId, string identityNumber, CancellationToken ct = default);

Task<bool> DeactivateAsync(
    string divisionId, Guid patientRefID, DateTime modifiedDate,
    long actorId, ActorKind actorKind, CancellationToken ct = default);
```

Production `FindForWriteAsync` must require `IsActive` for source reuse. Implement `DeactivateAsync` with a tenant-scoped conditional update (`PatientRefID`, `DivisionID`, `IsActive`) and return whether exactly one row changed. Mirror the same semantics in `FakePatientRepository`.

- [ ] **Step 6: Implement minimal writer state transitions**

Change `UpsertAsync` to return `ApplicationResult<PatientWriteResult>`:

```csharp
if (source != null && changedFields.Count == 0)
    return ApplicationResult<PatientWriteResult>.Success(
        await AttachChildFactsAsync(source, request, now, ct));

if (source != null && request.SetAsActiveProfile is null)
    return ApplicationResult<PatientWriteResult>.Fail(
        ApplicationFailureCode.PatientProfileChanged,
        "Thông tin người bệnh đã thay đổi",
        new PatientProfileChangedPayload(changedFields));

var version = CopyPatientValues(request, now);
version.PreviousPatientRefID = source?.PatientRefID;
version.IsActive = source == null || request.SetAsActiveProfile == true;
```

When promoting, call `DeactivateAsync` and return `InvalidState` if it updates zero rows. Never mutate personal fields on `source`. Keep the existing insurance/employment/relative exact-match logic, but attach newly created child facts to the selected/new version.

- [ ] **Step 7: Run focused tests**

```bash
dotnet test HealthExam.Tests/HealthExam.Tests.csproj --filter "FullyQualifiedName~PatientRegistrationWriterTests|FullyQualifiedName~ExamRecordHandlerTests"
```

Expected: writer tests PASS; existing handler tests may fail only at compile sites that Task 4 will deliberately update. Before committing this task, update those call sites mechanically to unwrap `ApplicationResult` without changing their final business behavior, so the solution compiles.

- [ ] **Step 8: Analyze and commit**

```bash
node .gitnexus/run.cjs detect-changes --scope all --repo .
git add HealthExam.Application/Patients \
  HealthExam.Infrastructure/Persistence/Repositories/PatientRepository.cs \
  HealthExam.Tests/Application/PatientRegistrationTestDoubles.cs \
  HealthExam.Tests/Application/PatientRegistrationWriterTests.cs \
  HealthExam.Application/ExamRecords/CreateExamRecord.cs \
  HealthExam.Application/ExamRecords/UpdateExamRecord.cs
git commit -m "feat: version changed patient profiles"
```

---

### Task 3: Add tenant-scoped active Patient search

**Files:**
- Create: `HealthExam.Application/Patients/SearchActivePatient.cs`
- Create: `HealthExam.Tests/Application/SearchActivePatientTests.cs`
- Create: `HealthExam.API/Controllers/PatientController.cs`
- Create: `HealthExam.Tests/PatientEndpointTests.cs`
- Modify: `HealthExam.API/Extensions/ApplicationServiceExtensions.cs`

**Interfaces:**
- Consumes: `IPatientRepository.FindActiveByIdentityNumberAsync` from Task 2.
- Produces: `SearchActivePatientQuery(string DivisionId, string IdentityNumber)`.
- Produces: `PatientProfileResult` containing `PatientRefID`, personal fields and `IsActive`.
- Produces: `GET /v1/patients/search?identityNumber=...`.

- [ ] **Step 1: Run impact for the registration and controller patterns**

```bash
node .gitnexus/run.cjs impact "ApplicationServiceExtensions" --direction upstream --repo .
node .gitnexus/run.cjs context "ExamRecordController" --repo .
rg -n "AddScoped<.*Handler|class .*Controller : HealthExamControllerBase" HealthExam.API
```

- [ ] **Step 2: Write failing handler tests**

```csharp
[Fact]
public async Task Search_returns_only_active_patient_in_current_division()
{
    var wanted = Patient("D01", "079123456789", isActive: true);
    var repo = new FakePatientRepository(
        wanted,
        Patient("D01", "079123456789", isActive: false),
        Patient("D02", "079123456789", isActive: true));
    var sut = new SearchActivePatientHandler(repo);

    var result = await sut.HandleAsync(
        new SearchActivePatientQuery("D01", " 079123456789 "));

    Assert.True(result.IsSuccess);
    Assert.Single(result.Value);
    Assert.Equal(wanted.PatientRefID, result.Value[0].PatientRefID);
}

[Theory]
[InlineData(null)]
[InlineData("")]
[InlineData("   ")]
public async Task Search_rejects_blank_identity_number(string identityNumber)
{
    var result = await CreateHandler().HandleAsync(
        new SearchActivePatientQuery("D01", identityNumber));
    Assert.Equal(ApplicationFailureCode.BadRequest, result.Failure.Code);
}
```

- [ ] **Step 3: Run handler tests and verify failure**

```bash
dotnet test HealthExam.Tests/HealthExam.Tests.csproj --filter FullyQualifiedName~SearchActivePatientTests
```

Expected: FAIL because query/handler are absent.

- [ ] **Step 4: Implement the search handler without a new abstraction**

Define `ISearchActivePatientHandler` beside the handler because controllers in this repository depend on handler interfaces. Trim the CCCD, return `BadRequest` when empty, call the repository once, and map null to an empty `IReadOnlyList<PatientProfileResult>`.

- [ ] **Step 5: Write the failing endpoint contract test**

```csharp
[Fact]
public async Task Search_patient_returns_standard_envelope()
{
    using var app = CreateAppWithPatient("D01", "079123456789");
    var client = app.CreateAuthenticatedClient("D01");

    var response = await client.GetAsync(
        "/v1/patients/search?identityNumber=079123456789");

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    var json = await response.Content.ReadAsStringAsync();
    Assert.Contains("079123456789", json);
    Assert.DoesNotContain("HisSyncError", json);
}
```

- [ ] **Step 6: Add the controller and DI registration**

```csharp
[Route("v1/patients")]
public sealed class PatientController : HealthExamControllerBase
{
    [HttpGet("search")]
    public async Task<ActionResult<ResultData<IReadOnlyList<PatientProfileResult>>>> Search(
        [FromQuery] string identityNumber, CancellationToken ct = default)
    {
        var result = await _handler.HandleAsync(
            new SearchActivePatientQuery(HealthExamContext.DivisionId, identityNumber), ct);
        return ToActionResult(result);
    }
}
```

Register `ISearchActivePatientHandler`/`SearchActivePatientHandler` using the same scoped pattern as existing handlers.

- [ ] **Step 7: Run focused tests and commit**

```bash
dotnet test HealthExam.Tests/HealthExam.Tests.csproj --filter "FullyQualifiedName~SearchActivePatientTests|FullyQualifiedName~PatientEndpointTests"
node .gitnexus/run.cjs detect-changes --scope all --repo .
git add HealthExam.Application/Patients/SearchActivePatient.cs \
  HealthExam.API/Controllers/PatientController.cs \
  HealthExam.API/Extensions/ApplicationServiceExtensions.cs \
  HealthExam.Tests/Application/SearchActivePatientTests.cs \
  HealthExam.Tests/PatientEndpointTests.cs
git commit -m "feat: search active patient profiles by identity"
```

---

### Task 4: Thread the active-profile decision through create and update APIs

**Files:**
- Modify: `HealthExam.API/Contracts/ExamRecordModels.cs`
- Modify: `HealthExam.Application/ExamRecords/ExamRecordModels.cs`
- Modify: `HealthExam.API/Controllers/ExamRecordController.cs`
- Modify: `HealthExam.Application/ExamRecords/CreateExamRecord.cs`
- Modify: `HealthExam.Application/ExamRecords/UpdateExamRecord.cs`
- Modify: `HealthExam.Application/Common/ApplicationFailure.cs`
- Modify: `HealthExam.API/Contracts/ErrorCodes.cs`
- Modify: `HealthExam.API/Middlewares/ApplicationResultMapper.cs`
- Modify: `HealthExam.Tests/Application/ExamRecordHandlerTests.cs`

**Interfaces:**
- Consumes: version-aware `PatientRegistrationWriter.UpsertAsync` from Task 2.
- Produces: nullable `PatientRefID` and `SetAsActiveProfile` on `ExamRecordWriteRequest`, create command and update command.
- Produces: `ApplicationFailureCode.PatientProfileChanged`, API error code `4096`, HTTP 409.

- [ ] **Step 1: Run impact before editing shared contracts and handlers**

```bash
node .gitnexus/run.cjs impact "ExamRecordWriteRequest" --direction upstream --repo .
node .gitnexus/run.cjs impact "CreateExamRecordHandler" --direction upstream --repo .
node .gitnexus/run.cjs impact "UpdateExamRecordHandler" --direction upstream --repo .
rg -n "new CreateExamRecordCommand|new UpdateExamRecordCommand|ExamRecordWriteRequest" HealthExam.API HealthExam.Tests
```

- [ ] **Step 2: Write failing create/update behavior tests**

Add tests that assert:

```csharp
[Fact]
public async Task Create_changed_patient_without_decision_returns_profile_changed()
{
    var active = SeedActivePatient(phone: "0901000000");
    var result = await CreateHandler().HandleAsync(CreateCommand(
        patientRefID: active.PatientRefID,
        phone: "0902000000",
        setAsActiveProfile: null));

    Assert.False(result.IsSuccess);
    Assert.Equal(ApplicationFailureCode.PatientProfileChanged, result.Failure.Code);
    Assert.Empty(_recordRepo.Records);
}

[Fact]
public async Task Update_changed_patient_forks_without_changing_another_record_history()
{
    var shared = SeedTwoRecordsWithSameActivePatient();
    var result = await UpdateHandler().HandleAsync(UpdateCommand(
        shared.FirstRecordId,
        phone: "0902000000",
        setAsActiveProfile: false));

    Assert.True(result.IsSuccess);
    Assert.NotEqual(shared.PatientRefID, result.Value.PatientRefID);
    Assert.Equal(shared.PatientRefID,
        _recordRepo.Records[shared.SecondRecordId].PatientRefID);
    Assert.Equal("0901000000",
        _recordRepo.Records[shared.SecondRecordId].Patient.PhoneNumber);
}
```

Also update the existing “reuses by HIS patient id” test: unchanged data may reuse; changed personal data must version instead of mutating the single row.

- [ ] **Step 3: Run tests and verify failure**

```bash
dotnet test HealthExam.Tests/HealthExam.Tests.csproj --filter FullyQualifiedName~ExamRecordHandlerTests
```

Expected: FAIL on missing decision property and current in-place update behavior.

- [ ] **Step 4: Add API/application fields and explicit conflict code**

Add to `ExamRecordWriteRequest`:

```csharp
public Guid? PatientRefID { get; set; }
public bool? SetAsActiveProfile { get; set; }
```

Append matching optional parameters to `CreateExamRecordCommand` and `UpdateExamRecordCommand`. Add:

```csharp
// ApplicationFailureCode
PatientProfileChanged

// ErrorCodes
public const int PatientProfileChanged = 4096;
```

Map it to HTTP 409 and message `Thông tin người bệnh đã thay đổi` in `ApplicationResultMapper`/`ErrorCodes`.

- [ ] **Step 5: Map the request in both controller actions**

Pass existing `PatientRefID` and new `SetAsActiveProfile` from the request into create/update commands. Do not add a second source-patient identifier.

- [ ] **Step 6: Propagate writer failure before creating or mutating the record**

In both handlers:

```csharp
var patientResult = await _writer.UpsertAsync(patientRequest, ct);
if (!patientResult.IsSuccess)
    return ApplicationResult<ExamRecordResult>.Fail(
        patientResult.Failure.Code,
        patientResult.Failure.Message,
        patientResult.Failure.Payload);

var writeResult = patientResult.Value;
```

For update, pass `command.PatientRefID ?? entity.PatientRefID` as source and never mutate `entity.Patient`; assign the returned version to the current record only.

- [ ] **Step 7: Make create/update atomic around active switching**

Ensure `DeactivateAsync`, new `Patient`, child facts and `ExamRecord` save inside the same existing application transaction. For the manual record-code create path and update path, add `await using var tx = await _uow.BeginAsync(ct)` before calling the writer, commit only after audit save, and rollback/discard on all failure branches. Preserve the auto record-code savepoint retry behavior; do not widen retries to patient-profile unique conflicts.

When `SaveChangesAsync` reports `UX_HEX_Patient_Division_ActiveIdentity` or `UX_HEX_Patient_Division_HisPatientID`, return `InvalidState`/HTTP 409 with `Hồ sơ người bệnh đã được cập nhật, vui lòng tìm lại`; retain `DuplicateInSession` for record-code/session indexes.

- [ ] **Step 8: Run handler and API mapping tests**

```bash
dotnet test HealthExam.Tests/HealthExam.Tests.csproj --filter "FullyQualifiedName~ExamRecordHandlerTests|FullyQualifiedName~ApplicationResultMapper"
```

Expected: PASS; changed profile without decision creates no record; accepted/declined paths preserve old records.

- [ ] **Step 9: Analyze and commit**

```bash
node .gitnexus/run.cjs detect-changes --scope all --repo .
git add HealthExam.API/Contracts/ExamRecordModels.cs \
  HealthExam.Application/ExamRecords \
  HealthExam.API/Controllers/ExamRecordController.cs \
  HealthExam.Application/Common/ApplicationFailure.cs \
  HealthExam.API/Contracts/ErrorCodes.cs \
  HealthExam.API/Middlewares/ApplicationResultMapper.cs \
  HealthExam.Tests/Application/ExamRecordHandlerTests.cs
git commit -m "feat: apply patient profile decisions to exam records"
```

---

### Task 5: Prove PostgreSQL uniqueness, migration behavior and concurrency

**Files:**
- Create: `HealthExam.Tests/Infrastructure/PatientProfilePersistenceTests.cs`

**Interfaces:**
- Consumes: schema/indexes from Task 1 and repository/writer behavior from Tasks 2–4.
- Produces: integration evidence for tenant isolation, one-active invariant and transaction rollback.

- [ ] **Step 1: Inspect test infrastructure and run impact before any fixture edit**

```bash
node .gitnexus/run.cjs impact "PostgresFixture" --direction upstream --repo .
rg -n "class PostgresFixture|IClassFixture<PostgresFixture>|EnsureDeleted|MigrateAsync" HealthExam.Tests
```

Use `[Collection(PostgresCollection.Name)]` and inject the existing `PostgresFixture`;
its `NewContext()` method already supplies the isolated migrated PostgreSQL database.

- [ ] **Step 2: Write failing PostgreSQL integration tests**

Add tests for:

```csharp
[Fact]
public async Task Filtered_index_allows_history_but_only_one_active_identity()
{
    db.Patients.Add(Patient("D01", "079123456789", isActive: true));
    db.Patients.Add(Patient("D01", "079123456789", isActive: false));
    await db.SaveChangesAsync();

    db.Patients.Add(Patient("D01", "079123456789", isActive: true));
    var error = await Assert.ThrowsAsync<DbUpdateException>(
        () => db.SaveChangesAsync());
    Assert.Equal("UX_HEX_Patient_Division_ActiveIdentity",
        ((PostgresException)error.GetBaseException()).ConstraintName);
}

[Fact]
public async Task Search_never_crosses_division_or_returns_inactive()
{
    var cccd = $"S{Guid.NewGuid():N}";
    await using var db = _fixture.NewContext();
    var active = NewPatient("D01", cccd, true);
    db.Patients.AddRange(
        active,
        NewPatient("D01", cccd, false),
        NewPatient("D02", cccd, true));
    await db.SaveChangesAsync();

    var result = await new PatientRepository(db)
        .FindActiveByIdentityNumberAsync("D01", cccd);

    Assert.Equal(active.PatientRefID, result.PatientRefID);
}

[Fact]
public async Task Competing_promotions_leave_exactly_one_active_version()
{
    var cccd = $"C{Guid.NewGuid():N}";
    await using var first = _fixture.NewContext();
    await using var second = _fixture.NewContext();
    first.Patients.Add(NewPatient("D01", cccd, true));
    second.Patients.Add(NewPatient("D01", cccd, true));

    var attempts = await Task.WhenAll(
        CaptureSave(first), CaptureSave(second));

    Assert.Equal(1, attempts.Count(x => x is null));
    Assert.Equal(1, attempts.Count(x => x is DbUpdateException));
    await using var verify = _fixture.NewContext();
    Assert.Equal(1, await verify.Patients.CountAsync(x =>
        x.DivisionID == "D01" && x.IdentityNumber == cccd && x.IsActive));
}

private static async Task<Exception> CaptureSave(HealthExamDbContext db)
{
    try { await db.SaveChangesAsync(); return null; }
    catch (DbUpdateException ex) { return ex; }
}

private static Patient NewPatient(string divisionId, string identity, bool active) => new()
{
    PatientRefID = Guid.NewGuid(),
    DivisionID = divisionId,
    FullName = "Nguyễn Văn An",
    Dob = new DateOnly(1990, 1, 1),
    IdentityNumber = identity,
    IsActive = active
};
```

Add `Backfill_activates_patient_used_by_latest_exam` using a dedicated temporary database:

1. Migrate it to `20260914110000_AddPatientSubjectToExamRecord`.
2. Insert two `HEX_Patient` rows with the same `(DivisionID, IdentityNumber)` and
   two `HEX_ExamRecord` rows whose `ModifiedDate` values are `2026-09-15` and
   `2026-09-16`.
3. Migrate to `AddPatientProfileVersioning`.
4. Query both patients and assert only the patient referenced by the
   `2026-09-16` exam has `IsActive = true`.
5. Drop the dedicated database in `finally`; do not downgrade or mutate the
   collection fixture's shared database.

- [ ] **Step 3: Run tests and verify failure before final implementation adjustment**

```bash
dotnet test HealthExam.Tests/HealthExam.Tests.csproj --filter FullyQualifiedName~PatientProfilePersistenceTests
```

Expected: at least the concurrency/backfill assertion FAIL until transaction/index handling is complete.

- [ ] **Step 4: Make only persistence fixes exposed by the tests**

Keep fixes inside `PatientRepository`, `UnitOfWork` conflict mapping, or the migration. Do not add locks, retry services or a new persistence abstraction. PostgreSQL filtered unique indexes remain the concurrency authority.

- [ ] **Step 5: Run all persistence tests**

```bash
dotnet test HealthExam.Tests/HealthExam.Tests.csproj --filter "FullyQualifiedName~PatientProfilePersistenceTests|FullyQualifiedName~RegistrationPersistenceTests|FullyQualifiedName~ExamRecordConcurrencyTests"
```

Expected: PASS with PostgreSQL available; no inactive profile appears in search and exactly one active version survives contention.

- [ ] **Step 6: Analyze and commit**

```bash
node .gitnexus/run.cjs detect-changes --scope all --repo .
git add HealthExam.Tests/Infrastructure/PatientProfilePersistenceTests.cs \
  HealthExam.Infrastructure/Persistence/Repositories/PatientRepository.cs \
  HealthExam.Infrastructure/Persistence/UnitOfWork.cs \
  HealthExam.Infrastructure/Persistence/Migrations
git commit -m "test: verify patient profile persistence invariants"
```

Only stage files that actually changed.

---

### Task 6: Full regression and API contract verification

**Files:**
- Modify: `README.md` only if it contains an endpoint inventory that must list `/v1/patients/search`.
- Modify: `HealthExam.API/HealthExam.API.http` only if it is the repository's maintained manual request collection.

**Interfaces:**
- Consumes: all preceding tasks.
- Produces: verified build, tests, migration script and clean graph-change report.

- [ ] **Step 1: Run impact before optional documentation edits**

```bash
rg -n "v1/exam-records|endpoint|API" README.md HealthExam.API/HealthExam.API.http 2>/dev/null
```

If neither file maintains endpoint examples, skip editing them; do not create documentation solely to repeat Swagger metadata.

- [ ] **Step 2: Run formatting and build checks**

```bash
dotnet format HealthExam.sln --verify-no-changes
dotnet build HealthExam.sln --no-restore
```

Expected: both exit 0.

- [ ] **Step 3: Run the full test suite**

```bash
dotnet test HealthExam.sln --no-build
```

Expected: exit 0; PostgreSQL-backed tests pass or report the repository's documented explicit skip when PostgreSQL is unavailable.

- [ ] **Step 4: Generate and inspect the final idempotent migration script**

```bash
dotnet ef migrations script --idempotent \
  --project HealthExam.Infrastructure/HealthExam.Infrastructure.csproj \
  --startup-project HealthExam.API/HealthExam.API.csproj \
  --output /tmp/health-exam-patient-profile-final.sql
rg -n "PreviousPatientRefID|IsActive|UX_HEX_Patient_Division_ActiveIdentity|ROW_NUMBER" \
  /tmp/health-exam-patient-profile-final.sql
```

Expected: all four markers exist; backfill precedes the unique active index.

- [ ] **Step 5: Run final graph and worktree review**

```bash
node .gitnexus/run.cjs detect-changes --scope compare --base-ref HEAD~5 --repo .
git diff --check
git status --short
```

Expected: no unexplained HIGH/CRITICAL regression path, no whitespace errors, and the two pre-existing untracked documents remain untouched.

- [ ] **Step 6: Commit optional endpoint documentation only when changed**

```bash
git add README.md HealthExam.API/HealthExam.API.http
git diff --cached --quiet || git commit -m "docs: document patient profile search endpoint"
```

Do not run this command with nonexistent files; stage only the maintained file found in Step 1.
