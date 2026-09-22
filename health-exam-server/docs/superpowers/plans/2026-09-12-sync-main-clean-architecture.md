# Đồng bộ code mới từ `main` vào Clean Architecture Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Đưa behavior và capability code mới từ `main` vào branch Clean Architecture mà không kéo lại `HealthExam.Core`/`HealthExam.Server` vào production path.

**Architecture:** Branch hiện tại là nguồn chuẩn cho Domain/Application/Infrastructure/API boundary và composition root. Code từ `main` được port theo vertical slice: normalized registration trước, sau đó HIS admission, form/PDF và các endpoint còn thiếu; migration/backfill/verify SQL được chép theo semantics nhưng không thực thi lại.

**Tech Stack:** .NET 8, ASP.NET Core 8, EF Core 8.0.11, Npgsql EF Core 8.0.11, PostgreSQL, Newtonsoft.Json 13.0.3, EPPlus 7.1.2, xUnit 2.9.2

**Spec:** `docs/superpowers/specs/2026-09-12-sync-main-into-clean-architecture-design.md`

## Global Constraints

- Không merge toàn bộ `main` hoặc replay nguyên xi feature commit viết cho `HealthExam.Core`/`HealthExam.Server`.
- Giữ branch hiện tại làm nguồn chuẩn cho Domain/Application/Infrastructure/API boundary, composition root và test harness.
- Giữ route, HTTP method, request/response contract, error mapping và behavior hiện hữu trừ capability mới được port từ `main`.
- `HealthExam.Domain` không tham chiếu project/package khác; `HealthExam.Application` không tham chiếu EF Core, ASP.NET Core, HTTP vendor type hoặc legacy project.
- Không expose `IQueryable`, EF entity row, HTTP request hoặc raw vendor JSON qua Application boundary mới.
- Giữ transaction, locking, sequence, idempotency, retry và timestamp semantics hiện hữu.
- Giữ migration ID, operation, table/column name và SQL semantics từ `main`; chỉ đổi namespace/path cần thiết để compile trong Infrastructure.
- Không regenerate migration bằng snapshot khác.
- Không chạy lại migration, backfill hoặc verify SQL trong plan này.
- Tài liệu, README, Dockerfile, seed/deploy phụ trợ nằm ngoài code phase, trừ khi code không build được nếu thiếu chúng.
- Không sửa hoặc commit `node_modules/`, `package.json`, `pnpm-lock.yaml`.
- Mỗi vertical slice kết thúc bằng build/test và một commit riêng.

---

### Task 1: Tạo integration branch và khóa baseline

**Files:**
- No tracked file changes.
- Preserve: `node_modules/`, `package.json`, `pnpm-lock.yaml` as existing untracked files.

**Interfaces:**
- Consumes: `feat-refactor-clean-architecture`, `main`, merge-base `3e51a34`.
- Produces: integration branch, baseline build/test result và danh sách source commit cần audit.

- [ ] **Step 1: Xác nhận working tree chỉ có các file untracked đã biết**

Run:

```bash
git status --short --branch
```

Expected: branch là `feat-refactor-clean-architecture`; các file untracked root chỉ là `node_modules/`, `package.json`, `pnpm-lock.yaml`. Không stage hoặc xóa chúng.

- [ ] **Step 2: Tạo branch tích hợp từ branch hiện tại**

Run:

```bash
git switch -c sync/main-clean-architecture
```

Nếu branch đã tồn tại, chuyển sang branch đó bằng `git switch sync/main-clean-architecture` và xác nhận nó bắt đầu từ HEAD hiện tại; không reset hoặc bỏ commit spec đã được duyệt.

- [ ] **Step 3: Ghi lại inventory commit và file thay đổi**

Run:

```bash
BASE=$(git merge-base HEAD main)
git show -s --format='%H %ad %s' --date=iso-strict "$BASE"
git log --reverse --format='%h %s' "$BASE"..main
git diff --name-status "$BASE"..main > /tmp/health-exam-main-sync-name-status.txt
git diff --stat "$BASE"..main
```

Đánh dấu từng commit vào ba nhóm: đã có tương đương, chưa có, hoặc khác behavior. Không đưa file inventory tạm vào git.

- [ ] **Step 4: Chạy baseline build và test không phụ thuộc migration/backfill**

Run:

```bash
dotnet build HealthExamServer.sln
dotnet test HealthExam.Tests/HealthExam.Tests.csproj \
  --filter 'FullyQualifiedName~HealthExam.Tests.Architecture|FullyQualifiedName~HealthExam.Tests.API|FullyQualifiedName~HealthExam.Tests.Application|FullyQualifiedName~HealthExam.Tests.Domain'
```

Expected: baseline hiện tại được ghi lại trước khi port; nếu lỗi đã tồn tại, ghi rõ lỗi đó để không quy nhầm cho slice mới. Task này không tạo commit.

---

### Task 2: Port patient model, normalized EF mapping và migration artifacts

**Files:**
- Create: `HealthExam.Domain/Patients/Patient.cs`
- Create: `HealthExam.Domain/Patients/PatientInsurance.cs`
- Create: `HealthExam.Domain/Patients/PatientEmployment.cs`
- Create: `HealthExam.Domain/Patients/PatientRelative.cs`
- Modify: `HealthExam.Domain/ExamRecords/ExamRecord.cs`
- Modify: `HealthExam.Infrastructure/Persistence/HealthExamDbContext.cs`
- Modify: `HealthExam.Infrastructure/Persistence/Migrations/HealthExamDbContextModelSnapshot.cs`
- Create: `HealthExam.Infrastructure/Persistence/Migrations/20260911151210_NormalizeRegistration3Nf.cs`
- Create: `HealthExam.Infrastructure/Persistence/Migrations/20260911151210_NormalizeRegistration3Nf.Designer.cs`
- Create: `HealthExam.Infrastructure/Persistence/Migrations/20260911163307_AddRegistrationPlaceOption.cs`
- Create: `HealthExam.Infrastructure/Persistence/Migrations/20260911163307_AddRegistrationPlaceOption.Designer.cs`
- Create: `HealthExam.Infrastructure/Persistence/Migrations/20260911180000_DropRegistrationLegacyColumns.cs`
- Create: `HealthExam.Infrastructure/Persistence/Migrations/20260911180000_DropRegistrationLegacyColumns.Designer.cs`
- Create: `Deploy/migration/20260911_registration_3nf_backfill.sql`
- Create: `Deploy/migration/20260911_registration_3nf_verify.sql`
- Test: `HealthExam.Tests/Infrastructure/PersistenceModelTests.cs`

**Interfaces:**
- Consumes: patient entities and final normalized schema from `main` migrations `20260911151210`, `20260911163307`, `20260911180000`.
- Produces: `HealthExamDbContext.Patients`, `.PatientInsurances`, `.PatientEmployments`, `.PatientRelatives`; `ExamRecord.PatientRefID`, `InsuranceRefID`, `EmploymentRefID`, `RelativeRefID`, `PatientTypeOptionID`, `PaymentSourceOptionID`, `ExamLocationOptionID` and their navigation properties.

- [ ] **Step 1: Add a failing model metadata test**

Extend `PersistenceModelTests.cs` with an assertion over `HealthExamDbContext.Model`:

```csharp
[Fact]
public void Model_contains_normalized_registration_entities_and_record_links()
{
    var model = _db.Model;

    Assert.NotNull(model.FindEntityType(typeof(Patient)));
    Assert.NotNull(model.FindEntityType(typeof(PatientInsurance)));
    Assert.NotNull(model.FindEntityType(typeof(PatientEmployment)));
    Assert.NotNull(model.FindEntityType(typeof(PatientRelative)));

    var record = model.FindEntityType(typeof(ExamRecord));
    Assert.NotNull(record?.FindProperty(nameof(ExamRecord.PatientRefID)));
    Assert.NotNull(record?.FindProperty(nameof(ExamRecord.InsuranceRefID)));
    Assert.NotNull(record?.FindProperty(nameof(ExamRecord.EmploymentRefID)));
    Assert.NotNull(record?.FindProperty(nameof(ExamRecord.RelativeRefID)));
}
```

- [ ] **Step 2: Run the test and verify the expected failure**

Run:

```bash
dotnet test HealthExam.Tests/HealthExam.Tests.csproj \
  --filter 'FullyQualifiedName~Model_contains_normalized_registration_entities_and_record_links'
```

Expected: FAIL because the four Domain types and normalized record properties are not yet mapped.

- [ ] **Step 3: Create the four package-free Domain entities**

Port the property sets from `main` into the `HealthExam.Domain.Patients` namespace. Each entity inherits `AuditableEntity`; no EF attribute or `Microsoft.EntityFrameworkCore` using is allowed.

Use these keys and relationships:

```csharp
Patient.PatientRefID
PatientInsurance.InsuranceRefID
PatientEmployment.EmploymentRefID
PatientRelative.RelativeRefID
```

`Patient` owns `Insurances`, `Employments` and `Relatives`; option navigation properties remain typed as `MasterDataOption`.

- [ ] **Step 4: Add normalized references to `ExamRecord`**

Add the seven nullable `Guid` references and six navigation properties exactly as represented by the final `main` model. Remove only denormalized fields that the final drop migration removes after all application callers are migrated; keep `ProvinceCode`, `ProvinceName`, `WardCode`, `WardName`, `ExamReason` and `PaymentSourceOther`, which remain in the final model.

- [ ] **Step 5: Add EF sets and mappings**

Add the four `DbSet` properties and a `ConfigurePatientRegistration(ModelBuilder)` call in `HealthExamDbContext.OnModelCreating`. Port the table names, default values, lengths, restrict-delete foreign keys, filtered indexes and the `UX_HEX_Record_Session_PatientRef` index from `main` without changing them.

The normalized `ExamRecord` projection must use:

```csharp
e.HasOne(x => x.Patient).WithMany().HasForeignKey(x => x.PatientRefID).OnDelete(DeleteBehavior.Restrict);
e.HasOne(x => x.Insurance).WithMany().HasForeignKey(x => x.InsuranceRefID).OnDelete(DeleteBehavior.Restrict);
e.HasOne(x => x.Employment).WithMany().HasForeignKey(x => x.EmploymentRefID).OnDelete(DeleteBehavior.Restrict);
e.HasOne(x => x.Relative).WithMany().HasForeignKey(x => x.RelativeRefID).OnDelete(DeleteBehavior.Restrict);
```

Add equivalent restricted relationships for `PatientTypeOption`, `PaymentSourceOption` and `ExamLocationOption`.

- [ ] **Step 6: Copy migration operations and SQL artifacts without executing them**

Copy the three migration pairs and two Deploy SQL files from `main`. Change only namespace/path references needed for `HealthExam.Infrastructure.Persistence.Migrations`; preserve migration attributes, class names, operation order, SQL strings, table names and index names.

Update `HealthExamDbContextModelSnapshot.cs` to include the normalized model and dropped legacy columns. Do not run `dotnet ef migrations add`, `dotnet ef database update`, backfill SQL or verify SQL.

- [ ] **Step 7: Build and run the model test**

Run:

```bash
dotnet build HealthExamServer.sln
dotnet test HealthExam.Tests/HealthExam.Tests.csproj \
  --filter 'FullyQualifiedName~PersistenceModelTests|FullyQualifiedName~HealthExam.Tests.Architecture'
```

Expected: PASS. This validates compilation and EF metadata only; it does not execute migration/backfill/verify SQL.

- [ ] **Step 8: Commit the persistence slice**

```bash
git add HealthExam.Domain/Patients HealthExam.Domain/ExamRecords/ExamRecord.cs \
  HealthExam.Infrastructure/Persistence/HealthExamDbContext.cs \
  HealthExam.Infrastructure/Persistence/Migrations \
  Deploy/migration HealthExam.Tests/Infrastructure/PersistenceModelTests.cs
git commit -m "refactor: port normalized registration persistence"
```

---

### Task 3: Port normalized registration writes and read projections

**Files:**
- Create: `HealthExam.Application/Patients/PatientModels.cs`
- Create: `HealthExam.Application/Patients/IPatientRepository.cs`
- Create: `HealthExam.Application/Patients/PatientRegistrationWriter.cs`
- Create: `HealthExam.Infrastructure/Persistence/Repositories/PatientRepository.cs`
- Modify: `HealthExam.Application/ExamRecords/ExamRecordModels.cs`
- Modify: `HealthExam.Application/ExamRecords/IExamRecordRepository.cs`
- Modify: `HealthExam.Application/ExamRecords/CreateExamRecord.cs`
- Modify: `HealthExam.Application/ExamRecords/UpdateExamRecord.cs`
- Modify: `HealthExam.Infrastructure/Persistence/Repositories/ExamRecordRepository.cs`
- Modify: `HealthExam.API/Contracts/ExamRecordModels.cs`
- Modify: `HealthExam.API/Controllers/ExamRecordController.cs`
- Modify: `HealthExam.Infrastructure/DependencyInjection.cs`
- Test: `HealthExam.Tests/Application/ExamRecordHandlerTests.cs`
- Create: `HealthExam.Tests/Application/PatientRegistrationTestDoubles.cs`
- Create: `HealthExam.Tests/Infrastructure/RegistrationPersistenceTests.cs`
- Modify: `HealthExam.Tests/LegacyAdapters/ExamRecordService.cs`
- Modify: `HealthExam.Tests/InMemoryTestDb.cs`

**Interfaces:**
- Consumes: normalized Domain entities and `IUnitOfWork` from Task 2.
- Produces: `PatientRegistrationWriter.UpsertAsync(PatientWriteRequest, CancellationToken)`, `IPatientRepository` persistence methods, and result projections populated from `Patient`/fact entities rather than dropped `HEX_ExamRecord` columns.

The concrete writer exposes:

```csharp
Task<PatientWriteResult> UpsertAsync(
    PatientWriteRequest request, CancellationToken ct = default);
```

Its constructor takes `IPatientRepository`, `IUnitOfWork` and `IClock`; the create/update handlers receive this concrete writer as a constructor dependency.

Add this typed read to `IExamRecordRepository` so HIS/form code can load the normalized graph without exposing EF:

```csharp
Task<ExamRecord> GetWithRegistrationAsync(
    string divisionId, Guid recordId, bool forUpdate = false,
    CancellationToken ct = default);
```

Define these application records in `PatientModels.cs`:

```csharp
public sealed record PatientInsuranceWrite(
    string InsuranceNumber,
    Guid? InsuranceObjectOptionID,
    Guid? RegistrationPlaceOptionID,
    DateOnly? ValidFrom,
    DateOnly? ValidTo);

public sealed record PatientEmploymentWrite(
    Guid? OccupationOptionID,
    string StaffCode,
    string OrgDeptName,
    string JobTitle);

public sealed record PatientRelativeWrite(
    string RelationshipCode,
    Guid? RelationshipOptionID,
    string FullName,
    string IdentityNumber,
    string PhoneNumber);

public sealed record PatientWriteRequest(
    string DivisionId,
    long ActorId,
    ActorKind ActorKind,
    Guid? PatientRefID,
    long? HisPatientID,
    string PatientCode,
    string FullName,
    DateOnly? Dob,
    short? BirthYear,
    short GenderID,
    string IdentityNumber,
    DateOnly? IdentityIssuedDate,
    Guid? IdentityIssuerOptionID,
    string PhoneNumber,
    string Email,
    string Address,
    Guid? EthnicityOptionID,
    string BloodAboCode,
    string BloodRhCode,
    PatientInsuranceWrite Insurance,
    PatientEmploymentWrite Employment,
    PatientRelativeWrite Relative);
```

`IPatientRepository` exposes only typed reads/adds needed by the writer:

```csharp
Task<Patient> FindForWriteAsync(string divisionId, Guid? patientRefID, long? hisPatientID, CancellationToken ct = default);
Task<PatientInsurance> FindActiveInsuranceAsync(string divisionId, Guid patientRefID, PatientInsuranceWrite value, CancellationToken ct = default);
Task<PatientEmployment> FindActiveEmploymentAsync(string divisionId, Guid patientRefID, PatientEmploymentWrite value, CancellationToken ct = default);
Task<PatientRelative> FindActiveRelativeAsync(string divisionId, Guid patientRefID, PatientRelativeWrite value, CancellationToken ct = default);
void Add(Patient entity);
void Add(PatientInsurance entity);
void Add(PatientEmployment entity);
void Add(PatientRelative entity);
```

`PatientRegistrationWriter.UpsertAsync` performs the `main` behavior: reuse by `PatientRefID`, then positive `HisPatientID`; never merge a new local patient by identity number alone; trim values; create immutable active child facts only when their value is non-empty; mark a positive HIS ID as `Linked`; and use the current `IUnitOfWork` for the caller's transaction.

- [ ] **Step 1: Add failing application tests for normalized write behavior**

Add tests to `ExamRecordHandlerTests.cs` covering:

```csharp
[Fact]
public async Task Create_writes_patient_and_child_fact_references()
{
    var patients = new FakePatientRepository();
    var writer = new PatientRegistrationWriter(patients, _uow, new FixedClock());
    var handler = new CreateExamRecordHandler(
        _recordRepo, _sessionRepo, _allocator, _uow, _audit, writer);

    var result = await handler.HandleAsync(new CreateExamRecordCommand(
        DivisionId: "D01", ActorId: "7", ActorKind: ActorKind.Employee,
        PatientCode: "P01", FullName: "Nguyễn Văn A", GenderID: 1,
        InsuranceNumber: "BH-01", StaffCode: "NV-01", VariantCode: "DTK_01"));

    Assert.True(result.IsSuccess);
    Assert.NotEqual(Guid.Empty, result.Value.PatientRefID);
    Assert.NotEqual(Guid.Empty, result.Value.InsuranceRefID);
    Assert.NotEqual(Guid.Empty, result.Value.EmploymentRefID);
    Assert.Equal("Nguyễn Văn A", patients.Patients.Single().FullName);
}

[Fact]
public async Task Update_reuses_patient_by_positive_his_patient_id()
{
    var patient = new Patient
    {
        PatientRefID = Guid.NewGuid(), DivisionID = "D01", HisPatientID = 9001,
        FullName = "Nguyễn Văn A"
    };
    var patients = new FakePatientRepository(patient);
    var writer = new PatientRegistrationWriter(patients, _uow, new FixedClock());
    var recordId = Guid.NewGuid();
    _recordRepo.Records[recordId] = new ExamRecord
    {
        RecordID = recordId, DivisionID = "D01", SessionID = Guid.NewGuid(),
        RecordCode = "R01", VariantCode = "DTK_01", State = ExamRecordState.NotRegistered
    };
    _sessionRepo.Sessions[_recordRepo.Records[recordId].SessionID] = new ExamSession
    {
        SessionID = _recordRepo.Records[recordId].SessionID,
        DivisionID = "D01", State = ExamSessionState.Open
    };

    var handler = new UpdateExamRecordHandler(
        _recordRepo, _sessionRepo, _uow, _audit, writer);
    var result = await handler.HandleAsync(new UpdateExamRecordCommand(
        DivisionId: "D01", RecordId: recordId, ActorId: "7", ActorKind: ActorKind.Employee,
        PatientID: 9001, FullName: "Nguyễn Văn A", VariantCode: "DTK_01"));

    Assert.True(result.IsSuccess);
    Assert.Single(patients.Patients);
    Assert.Equal(patient.PatientRefID, _recordRepo.Records[recordId].PatientRefID);
}
```

Replace the comments with the existing `InMemoryTestDb` setup and concrete assertions; do not add a new test framework or fixture library.

- [ ] **Step 2: Run the tests and verify the expected failure**

Run:

```bash
dotnet test HealthExam.Tests/HealthExam.Tests.csproj \
  --filter 'FullyQualifiedName~Create_writes_patient_and_child_fact_references|FullyQualifiedName~Update_reuses_patient_by_positive_his_patient_id'
```

Expected: FAIL because the handlers still write denormalized fields and no patient repository is registered.

- [ ] **Step 3: Implement `PatientRepository` and register it**

Implement the typed queries with `AsTracking()` for the write path and exact composite comparisons from `main` for active insurance/employment/relative facts. Register:

```csharp
services.AddScoped<IPatientRepository, PatientRepository>();
```

Do not expose `IQueryable` or `HealthExamDbContext` from the Application interface.

- [ ] **Step 4: Implement `PatientRegistrationWriter`**

Move the upsert and child-fact rules from `main:HealthExam.Server/Service/PatientService.cs` into the concrete writer. Pass actor/division values through `PatientWriteRequest`; do not read request headers or use a legacy `IHealthExamContext` inside this writer.

- [ ] **Step 5: Replace denormalized writes in create/update handlers**

In `CreateExamRecordHandler` and `UpdateExamRecordHandler`:

1. Keep existing input validation and master-data resolution.
2. Resolve option IDs for identity issuer, ethnicity, occupation, insurance object, registration place, patient type, payment source and exam location.
3. Build `PatientWriteRequest` from the command and existing normalized navigation values.
4. Call `PatientRegistrationWriter.UpsertAsync` inside the existing unit-of-work transaction.
5. Set the record reference IDs and option IDs from the result.
6. Stop assigning fields removed by `DropRegistrationLegacyColumns`.

Do not change code allocation, savepoint or duplicate constraint handling.

- [ ] **Step 6: Update result projection and API contract**

Change `ExamRecordRepository` to load normalized navigations and map:

```csharp
PatientID       = record.Patient?.HisPatientID ?? 0,
PatientCode     = record.Patient?.PatientCode ?? "",
InsuranceNumber = record.Insurance?.InsuranceNumber ?? "",
StaffCode       = record.Employment?.StaffCode ?? "",
RelativeFullName = record.Relative?.FullName ?? "",
PatientTypeCode = record.PatientTypeOption?.Code ?? "",
PaymentSourceCode = record.PaymentSourceOption?.Code ?? "",
ExamLocationCode = record.ExamLocationOption?.Code ?? ""
```

Add `RegistrationPlaceCode`, `RegistrationPlaceName`, `PatientRefID`, `InsuranceRefID`, `EmploymentRefID` and `RelativeRefID` to the Application result/API item where they exist in the final `main` contract. Keep create/update request field names stable so existing clients continue sending the same payload.

- [ ] **Step 7: Update test adapters and run the write/projection suite**

Replace direct assignments to removed `ExamRecord` fields in `LegacyAdapters/ExamRecordService.cs` and `InMemoryTestDb.cs` with patient/fact entities and normalized references. Run:

```bash
dotnet build HealthExamServer.sln
dotnet test HealthExam.Tests/HealthExam.Tests.csproj \
  --filter 'FullyQualifiedName~HealthExam.Tests.Application.ExamRecordHandlerTests|FullyQualifiedName~HealthExam.Tests.Infrastructure.RegistrationPersistenceTests|FullyQualifiedName~HealthExam.Tests.Architecture'
```

Expected: PASS with create/update/list/get paths reading from normalized entities.

- [ ] **Step 8: Commit the registration slice**

```bash
git add HealthExam.Application/Patients HealthExam.Application/ExamRecords \
  HealthExam.Infrastructure/Persistence/Repositories/PatientRepository.cs \
  HealthExam.Infrastructure/Persistence/Repositories/ExamRecordRepository.cs \
  HealthExam.Infrastructure/DependencyInjection.cs \
  HealthExam.API/Contracts/ExamRecordModels.cs HealthExam.API/Controllers/ExamRecordController.cs \
  HealthExam.Tests/Application/ExamRecordHandlerTests.cs \
  HealthExam.Tests/Infrastructure/RegistrationPersistenceTests.cs \
  HealthExam.Tests/LegacyAdapters/ExamRecordService.cs HealthExam.Tests/InMemoryTestDb.cs
git commit -m "feat: port normalized registration flows"
```

---

### Task 4: Port HIS patient synchronization and admission fallback

**Files:**
- Create: `HealthExam.Application/His/EnsureHisPatient.cs`
- Create: `HealthExam.Application/His/EnsureHisAdmission.cs`
- Modify: `HealthExam.Application/His/HisModels.cs`
- Modify: `HealthExam.Application/Integrations/IHisEmrClient.cs`
- Modify: `HealthExam.Infrastructure/Integrations/HisEmr/HisEmrClient.cs`
- Modify: `HealthExam.Application/RegistrationForms/SaveRegistrationFormSection.cs`
- Modify: `HealthExam.Application/RegistrationForms/GetRegistrationForm.cs`
- Modify: `HealthExam.Infrastructure/Persistence/Repositories/RegistrationFormRepository.cs`
- Modify: `HealthExam.Infrastructure/DependencyInjection.cs`
- Test: `HealthExam.Tests/Application/HisHandlerTests.cs`
- Modify: `HealthExam.Tests/Application/RegistrationFormHandlerTests.cs`
- Modify: `HealthExam.Tests/HisFormDefinitionTests.cs`
- Modify: `HealthExam.Tests/HisExamFormServiceTests.cs`
- Test: `HealthExam.Tests/HisAdmissionLinkTests.cs`
- Test: `HealthExam.Tests/Infrastructure/HisEmrClientTests.cs`

**Interfaces:**
- Consumes: normalized patient/record references from Task 3 and existing `IHisEmrClient` transport.
- Produces: concrete `EnsureHisPatient` and `EnsureHisAdmission` operations; typed integration methods that return `HisClientResult<long>` instead of leaking vendor JSON.

The concrete operations expose:

```csharp
Task<ApplicationResult<long>> HandleAsync(
    EnsureHisPatientCommand command, CancellationToken ct = default);

Task<ApplicationResult<long>> HandleAsync(
    EnsureHisAdmissionCommand command, CancellationToken ct = default);
```

Add these transport-neutral records to `HealthExam.Application/His/HisModels.cs`:

```csharp
public sealed record HisPatientCreateRequest(
    string PatientCode, string FirstName, string LastName, string FullName,
    byte Gender, DateTime? BirthDate, int BirthYear, string IdentityNumber,
    string PhoneNumber, string Email, string Address);

public sealed record HisAdmissionCreateRequest(
    byte IsOutPatient, string AdmissionCode, DateTime AdmissionDate,
    int DepartmentID, string DepartmentCode, HisPatientCreateRequest Patient);

public sealed record HisCallContext(string Credential, string TraceId, string DivisionId);

public sealed record EnsureHisPatientCommand(
    string DivisionId, Guid PatientRefID, HisCallContext Context,
    long ActorId, ActorKind ActorKind);

public sealed record EnsureHisAdmissionCommand(
    string DivisionId, Guid RecordId, HisCallContext Context,
    long ActorId, ActorKind ActorKind);
```

Extend `IHisEmrClient` with:

```csharp
Task<HisClientResult<long>> CreatePatientAsync(
    HisPatientCreateRequest request, HisCallContext context, CancellationToken ct = default);

Task<HisClientResult<long>> CreateAdmissionAsync(
    HisAdmissionCreateRequest request, HisCallContext context, CancellationToken ct = default);
```

The Infrastructure client owns route constants, Newtonsoft mapping, response ID parsing and HTTP error mapping. Existing generic form methods remain unchanged until Task 5.

- [ ] **Step 1: Add failing tests for patient/admission behavior**

Add tests that assert:

```csharp
[Fact]
public async Task Missing_patient_his_id_creates_and_links_patient()
{
    var patient = new Patient
    {
        PatientRefID = Guid.NewGuid(), DivisionID = "D01", PatientCode = "P01",
        FullName = "Nguyễn Văn A", GenderID = 1
    };
    var repo = new FakePatientRepository(patient);
    var his = new FakeHisEmrClient { PatientId = 9001 };
    var operation = new EnsureHisPatient(repo, his, new FixedClock());

    var result = await operation.HandleAsync(new EnsureHisPatientCommand(
        "D01", patient.PatientRefID,
        new HisCallContext("credential", "trace-1", "D01"), 7, ActorKind.Employee));

    Assert.True(result.IsSuccess);
    Assert.Equal(9001, patient.HisPatientID);
    Assert.Equal("Linked", patient.HisSyncStatus);
}

[Fact]
public async Task Missing_admission_creates_and_links_admission_from_normalized_patient()
{
    var record = new ExamRecord
    {
        RecordID = Guid.NewGuid(), DivisionID = "D01", PatientRefID = Guid.NewGuid(),
        Patient = new Patient
        {
            PatientRefID = Guid.NewGuid(), DivisionID = "D01", PatientCode = "P01",
            FullName = "Nguyễn Văn A", GenderID = 1
        },
        Session = new ExamSession { ExamDate = new DateOnly(2026, 9, 12), DepartmentID = 15 }
    };
    var repo = new FakeExamRecordRepository();
    repo.Records[record.RecordID] = record;
    var his = new FakeHisEmrClient { AdmissionId = 7001 };
    var operation = new EnsureHisAdmission(
        repo, his, new FakeHisCredentialOptions { KskDepartmentId = 15, KskDepartmentCode = "KSK" }, new FixedClock());

    var result = await operation.HandleAsync(new EnsureHisAdmissionCommand(
        "D01", record.RecordID,
        new HisCallContext("credential", "trace-1", "D01"), 7, ActorKind.Employee));

    Assert.True(result.IsSuccess);
    Assert.Equal(7001, record.AdmissionID);
    Assert.Equal("P01", his.LastAdmissionRequest.Patient.PatientCode);
}
```

- [ ] **Step 2: Run the new tests and verify failure**

Run:

```bash
dotnet test HealthExam.Tests/HealthExam.Tests.csproj \
  --filter 'FullyQualifiedName~Missing_patient_his_id_creates_and_links_patient|FullyQualifiedName~Missing_admission_creates_and_links_admission_from_normalized_patient'
```

Expected: FAIL because typed HIS operations and normalized navigation loading do not exist.

- [ ] **Step 3: Implement typed HIS client operations**

Map `HisPatientCreateRequest` to the `main` wire fields (`PatientCode`, `FirstName`, `LastName`, `FullName`, `I_Gender`, `BirthDate`, `BirthYear`, `IDCard`, `MobileNo`, `PersonalEmail`, `CurrentAddress`). Map admission fields to `GetAdmissionInfo` and parse only a positive integer ID. Return `VendorNotConfigured`, `Unauthorized`, `Timeout` or `BadGateway` through the existing `HisClientOutcome` values.

- [ ] **Step 4: Implement `EnsureHisPatient` and `EnsureHisAdmission`**

Preserve these rules from `main`:

- reuse a positive `HisPatientID` without an HTTP call;
- require patient code, full name and a byte-range gender before creating a patient;
- use session department first, then configured KSK department;
- require both department ID and department code before creating admission;
- require patient code and positive gender before creating admission;
- write `HisSyncStatus = "Linked"` on success and bounded `HisSyncError` on patient failure;
- never overwrite an existing positive `AdmissionID`.

- [ ] **Step 5: Replace inline admission creation in form save**

Call `EnsureHisAdmission` from `SaveRegistrationFormSectionHandler` before read-merge-write. Keep the existing form section validation, HIS EMR data ID tracking, transaction/save behavior and response mapping. `GetRegistrationFormHandler` must load normalized patient/admission data through the repository path instead of old record columns.

- [ ] **Step 6: Run HIS and admission tests**

Run:

```bash
dotnet build HealthExamServer.sln
dotnet test HealthExam.Tests/HealthExam.Tests.csproj \
  --filter 'FullyQualifiedName~HealthExam.Tests.Application.HisHandlerTests|FullyQualifiedName~HealthExam.Tests.HisAdmissionLinkTests|FullyQualifiedName~HealthExam.Tests.Infrastructure.HisEmrClientTests'
```

- [ ] **Step 7: Commit the HIS patient/admission slice**

```bash
git add HealthExam.Application/His HealthExam.Application/Integrations/IHisEmrClient.cs \
  HealthExam.Application/RegistrationForms HealthExam.Infrastructure/Integrations/HisEmr \
  HealthExam.Infrastructure/Persistence/Repositories/RegistrationFormRepository.cs \
  HealthExam.Infrastructure/DependencyInjection.cs HealthExam.Tests/Application/HisHandlerTests.cs \
  HealthExam.Tests/HisAdmissionLinkTests.cs HealthExam.Tests/Infrastructure/HisEmrClientTests.cs
git commit -m "feat: port normalized HIS patient and admission flows"
```

---

### Task 5: Port missing HIS/form/PDF capabilities and configuration code

**Files:**
- Create: `HealthExam.Application/His/GetIcd10Choices.cs`
- Create: `HealthExam.Application/His/ListEmployeeDepartments.cs`
- Create: `HealthExam.Application/His/SaveHisFormSection.cs`
- Modify: `HealthExam.Application/His/HisModels.cs`
- Modify: `HealthExam.Application/Integrations/IHisEmrClient.cs`
- Modify: `HealthExam.Infrastructure/Integrations/HisEmr/HisEmrClient.cs`
- Modify: `HealthExam.Infrastructure/Caching/HisFormDefinitionCache.cs`
- Modify: `HealthExam.API/Controllers/CatalogController.cs`
- Modify: `HealthExam.API/Controllers/HisFormController.cs`
- Modify: `HealthExam.API/Controllers/ExamRecordHisFormController.cs`
- Modify: `HealthExam.API/Contracts/CatalogModels.cs`
- Modify: `HealthExam.API/Contracts/HisEmrModels.cs`
- Modify: `HealthExam.API/Program.cs`
- Modify: `HealthExam.API/Extensions/ApplicationServiceExtensions.cs`
- Modify: `HealthExam.Infrastructure/DependencyInjection.cs`
- Modify: `HealthExam.Application/RegistrationForms/PreviewRegistrationFormPdf.cs`
- Modify: `HealthExam.Infrastructure/Integrations/HisEmr/HisEmrClient.cs`
- Test: `HealthExam.Tests/API/HisFormEndpointTests.cs`
- Test: `HealthExam.Tests/API/ApiContractSurfaceTests.cs`
- Test: `HealthExam.Tests/API/CompositionRootTests.cs`
- Test: `HealthExam.Tests/HisEmrClientTests.cs`
- Test: `HealthExam.Tests/Infrastructure/HisEmrClientPdfTests.cs`

**Interfaces:**
- Consumes: `main` capability commits `2f97249`, `c88c359`, `101dcc3`, `9c092b0`, `887263a`, `f7ada1d` and current handlers from Tasks 3–4.
- Produces: typed Application operations for ICD-10, current employee departments and clinical HIS section save, plus the corresponding API routes.

Use these concrete Application operations:

```csharp
public sealed record GetIcd10ChoicesQuery(
    string DivisionId, string Filter, int Amount, string Credential, string TraceId);

public sealed record ListEmployeeDepartmentsQuery(
    string DivisionId, long EmployeeId, string Credential, string TraceId);

public sealed record SaveHisFormSectionCommand(
    string DivisionId, Guid RecordId, int ItemGroupId,
    HisFormSectionSaveRequest Request, string Credential, string TraceId,
    string ActorId, ActorKind ActorKind);
```

The handlers call typed Application integration methods. They do not parse `JToken`, build `HttpRequestMessage` or serialize vendor DTOs.

- [ ] **Step 1: Extend the API contract tests with the main routes**

Add explicit Swagger assertions for:

```text
GET  /v1/catalogs/icd10
GET  /v1/departments
PUT  /v1/exam-records/{recordId}/his-form/sections/{itemGroupId}
```

Also assert that the response DTOs expose the fields present in `main` for ICD-10 choices, department items and section save results.

- [ ] **Step 2: Run the contract tests and verify failure**

Run:

```bash
dotnet test HealthExam.Tests/HealthExam.Tests.csproj \
  --filter 'FullyQualifiedName~HealthExam.Tests.API.ApiContractSurfaceTests|FullyQualifiedName~HealthExam.Tests.API.HisFormEndpointTests'
```

Expected: FAIL because the new routes/handlers are not yet registered.

- [ ] **Step 3: Implement ICD-10 operation and cache behavior**

Port `main` filtering and amount bounds into `GetIcd10ChoicesHandler`. The Infrastructure client calls the HIS diagnosis route and maps its response to `Icd10Choice`. Cache only the normalized `(filter, amount)` result through the existing form definition cache mechanism or an existing memory cache registration; do not add a cache package or a second cache abstraction.

- [ ] **Step 4: Implement current employee departments**

Add `ListEmployeeDepartmentsHandler`, validate `EmployeeId > 0` and employee actor kind, call the HIS department operation, de-duplicate by department ID, then order by name and ID exactly as `main`. Add `GET /v1/departments` to the existing catalog controller and map the PascalCase envelope through the existing base controller.

- [ ] **Step 5: Implement clinical HIS section save**

Add `SaveHisFormSectionHandler` using the existing `GetHisRecordSection`/HIS definition contracts. Preserve the `ItemGroupID` route, read-merge-write payload shape, HIS EMR data ID persistence, actor/record ownership checks and error mapping from `main:HealthExam.API/Controllers/ExamRecordHisFormController.cs` and its service implementation.

- [ ] **Step 6: Port PDF label/text fixes and `.env.local` loading**

Compare the current `HisEmrClient.RenderFormPdfAsync` and `PreviewRegistrationFormPdfHandler` with `main` commit `f7ada1d`; port only missing choice-label/text mapping and regression assertions. Add the `LoadEnvFile(".env.local")`, parent `.env.local` and `.env` loading before `WebApplication.CreateBuilder` in `Program.cs`, preserving current environment-variable precedence and no new dependency.

- [ ] **Step 7: Register concrete handlers and run endpoint tests**

Register the three handlers and any typed client methods in `ApplicationServiceExtensions`/`Infrastructure.DependencyInjection`. Run:

```bash
dotnet build HealthExamServer.sln
dotnet test HealthExam.Tests/HealthExam.Tests.csproj \
  --filter 'FullyQualifiedName~HealthExam.Tests.API|FullyQualifiedName~HealthExam.Tests.Application.HisHandlerTests|FullyQualifiedName~HealthExam.Tests.HisEmrClientTests|FullyQualifiedName~HealthExam.Tests.Infrastructure.HisEmrClientPdfTests'
```

- [ ] **Step 8: Commit the capability slice**

```bash
git add HealthExam.Application/His HealthExam.Application/Integrations/IHisEmrClient.cs \
  HealthExam.Infrastructure/Integrations/HisEmr HealthExam.Infrastructure/Caching/HisFormDefinitionCache.cs \
  HealthExam.Infrastructure/DependencyInjection.cs HealthExam.API/Controllers \
  HealthExam.API/Contracts HealthExam.API/Program.cs HealthExam.API/Extensions/ApplicationServiceExtensions.cs \
  HealthExam.Application/RegistrationForms/PreviewRegistrationFormPdf.cs HealthExam.Tests/API \
  HealthExam.Tests/HisEmrClientTests.cs HealthExam.Tests/Infrastructure/HisEmrClientPdfTests.cs
git commit -m "feat: port remaining HIS form and catalog capabilities"
```

---

### Task 6: Converge tests, dependency rules and main parity

**Files:**
- Modify: `HealthExam.Tests/Architecture/DependencyRuleTests.cs`
- Modify: `HealthExam.Tests/API/CompositionRootTests.cs`
- Modify: `HealthExam.Tests/API/ApiContractSurfaceTests.cs`
- Modify: `HealthExam.Tests/Application/ExamRecordHandlerTests.cs`
- Modify: `HealthExam.Tests/Application/RegistrationFormHandlerTests.cs`
- Modify: `HealthExam.Tests/Application/HisHandlerTests.cs`
- Modify: `HealthExam.Tests/Infrastructure/PersistenceModelTests.cs`
- Modify: `HealthExam.Tests/Infrastructure/RegistrationFormPersistenceTests.cs`
- Modify: `HealthExam.Tests/Infrastructure/HisEmrClientTests.cs`
- Modify: `HealthExam.Tests/Infrastructure/HisEmrClientPdfTests.cs`
- Modify: `HealthExam.Tests/LegacyAdapters/ExamRecordService.cs`

**Interfaces:**
- Consumes: all code slices from Tasks 2–5.
- Produces: passing code-phase regression suite and a clean parity inventory with no unclassified production feature from `main`.

- [ ] **Step 1: Add the legacy dependency guard**

Extend `DependencyRuleTests` with a source scan that fails when production files under `HealthExam.Domain`, `HealthExam.Application` or `HealthExam.Infrastructure` contain `HealthExam.Server` references, and when API files other than `Program.cs` contain `HealthExam.Infrastructure` references.

Expected assertion shape:

```csharp
Assert.DoesNotContain("HealthExam.Core", source, StringComparison.Ordinal);
Assert.DoesNotContain("HealthExam.Server", source, StringComparison.Ordinal);
```

- [ ] **Step 2: Run the guard and fix only actual production leaks**

Run:

```bash
dotnet test HealthExam.Tests/HealthExam.Tests.csproj \
  --filter 'FullyQualifiedName~HealthExam.Tests.Architecture'
```

Update namespaces/usings in production code and migration metadata as needed. Do not silence the guard by excluding feature files.

- [ ] **Step 3: Run the complete code-phase test suite**

Run:

```bash
dotnet build HealthExamServer.sln
dotnet test HealthExamServer.sln \
  --filter 'FullyQualifiedName~HealthExam.Tests.Architecture|FullyQualifiedName~HealthExam.Tests.API|FullyQualifiedName~HealthExam.Tests.Application|FullyQualifiedName~HealthExam.Tests.Domain|FullyQualifiedName~HealthExam.Tests.Infrastructure'
```

If PostgreSQL is configured and the existing suite requires it, run the persistence tests with the existing `HEALTHEXAM_TEST_DB` value. Do not invoke EF database update or either Deploy SQL file.

- [ ] **Step 4: Audit source parity against `main`**

Run:

```bash
git diff --name-status 3e51a34..main
git diff --name-only main..HEAD | rg 'HealthExam\.Core|HealthExam\.Server' || true
rg -n 'HealthExam\.Core|HealthExam\.Server' HealthExam.Domain HealthExam.Application HealthExam.Infrastructure HealthExam.API
```

For every main-only commit, record one of: already represented, ported in a named task, or intentionally deferred as docs/operational-only. There must be no unclassified production capability.

- [ ] **Step 5: Verify the final working tree scope**

Run:

```bash
git status --short --branch
git diff --check
```

Expected: only intended code, test and migration/backfill artifact changes are tracked; `node_modules/`, `package.json`, and `pnpm-lock.yaml` remain untracked and untouched.

- [ ] **Step 6: Commit final convergence checks**

```bash
git add HealthExam.Tests
git commit -m "test: verify main sync parity and architecture boundaries"
```

## Handoff

After Task 6, the integration branch is ready for review. The next decision is whether to merge the integration branch into `feat-refactor-clean-architecture`; no database migration/backfill execution is part of this plan.
