# Registration 3NF Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Chuẩn hoá phân hệ đăng ký hồ sơ theo 3NF, để health-exam sở hữu Patient, tái sử dụng Patient qua nhiều registration, giữ version BHYT/nghề nghiệp/thân nhân theo từng registration và không phá public API hiện tại.

**Architecture:** Giữ `HEX_ExamRecord` làm registration header. Thêm `Patient` làm aggregate nguồn sự thật nội bộ cùng các bảng versioned facts (`PatientInsurance`, `PatientEmployment`, `PatientRelative`); registration giữ FK tới đúng bản ghi đã sử dụng. Rollout theo kiểu additive: backfill trước, normalized read/write sau, xoá legacy columns ở migration riêng sau khi đối chiếu ổn định.

**Tech Stack:** .NET 8, ASP.NET Core, EF Core 8, PostgreSQL, Newtonsoft.Json, xUnit.

**Spec:** `docs/superpowers/specs/2026-09-11-registration-3nf-design.md`

## Global Constraints

- `health-exam-server` là source of truth của Patient; `HisPatientID` chỉ là mã tham chiếu downstream.
- `PatientRefID` là UUID nội bộ; unique HIS key là `(DivisionID, HisPatientID)` khi `HisPatientID > 0`.
- Không tự merge Patient theo CCCD hoặc BHYT khi chưa có `HisPatientID`.
- `AdmissionID` thuộc registration, không thuộc Patient.
- BHYT, nghề nghiệp/đơn vị công tác và thân nhân đã được registration tham chiếu là immutable; thay đổi tạo row mới.
- Giữ nguyên route, field name và PascalCase JSON contract hiện tại.
- Không gọi HIS trong transaction DB health-exam và không dùng distributed transaction.
- Không xoá legacy columns trước Release C và trước khi normalized/legacy đối chiếu không còn lệch.
- Không thêm package mới; dùng repository, migration, HTTP client và test pattern hiện có.
- Mọi query ghi/đọc Patient và child facts phải lọc `DivisionID` của request context.
- Mỗi task phải kết thúc bằng test liên quan và một commit riêng, không stage các file untracked có sẵn trong worktree.

## File Map

### Tạo mới

- `HealthExam.Core/EntityFramework/Entity/Patient.cs`: Patient canonical và trạng thái đồng bộ HIS.
- `HealthExam.Core/EntityFramework/Entity/PatientInsurance.cs`: version BHYT của Patient.
- `HealthExam.Core/EntityFramework/Entity/PatientEmployment.cs`: version nghề nghiệp/đơn vị công tác.
- `HealthExam.Core/EntityFramework/Entity/PatientRelative.cs`: thân nhân của Patient.
- `HealthExam.Server/Models/PatientModels.cs`: internal command/result models cho Patient và child facts.
- `HealthExam.Server/Service/PatientService.cs`: upsert Patient local, tạo version child facts, map Patient response.
- `HealthExam.Server/Service/HisPatientService.cs`: đồng bộ Patient local sang HIS và lưu `HisPatientID`.
- `HealthExam.Server/Models/HisPatientModels.cs`: wire request/response contract cho HIS Patient.
- `HealthExam.Tests/PatientServiceTests.cs`: reuse Patient, tenant isolation và immutable child facts.
- `HealthExam.Tests/HisPatientServiceTests.cs`: HIS Patient sync, lỗi và retry.
- `HealthExam.Tests/Registration3NfPersistenceTests.cs`: FK, normalized mapping và legacy fallback.
- `HealthExam.Tests/Registration3NfBackfillTests.cs`: kiểm tra logic backfill bằng PostgreSQL fixture.
- `Deploy/migration/20260911_registration_3nf_backfill.sql`: backfill idempotent và conflict report.
- `Deploy/migration/20260911_registration_3nf_verify.sql`: count/orphan/conflict checks sau backfill.

### Sửa đổi

- `HealthExam.Core/EntityFramework/Entity/ExamRecord.cs`: thêm FK normalized và giữ legacy fields trong transition.
- `HealthExam.Core/EntityFramework/HealthExamDbContext.cs`: mapping bảng, FK, unique/index và delete behavior.
- `HealthExam.Core/EntityFramework/Repositories/IUnitOfWork.cs`: expose repositories Patient và child facts.
- `HealthExam.Core/EntityFramework/Repositories/UnitOfWork.cs`: implement repositories mới.
- `HealthExam.Core/Models/RecordFieldLengths.cs`: shared lengths cho Patient tables.
- `HealthExam.Server/Models/ExamRecordModels.cs`: giữ public request/response, bổ sung internal normalized identifiers nếu cần nhưng không đổi JSON cũ.
- `HealthExam.Server/Models/MasterDataModels.cs`: thêm resolved option có `OptionID` cho internal use.
- `HealthExam.Server/Service/IMasterDataService` và `MasterDataService.cs`: resolve DB option kèm `OptionID`.
- `HealthExam.Server/Service/ExamRecordService.cs`: normalized create/update/list/get mapping và temporary legacy mirror.
- `HealthExam.Server/Service/ExamImportService.cs`: dùng normalized create path, không tự ghi legacy fields.
- `HealthExam.Server/Service/HisAdmissionService.cs`: đọc Patient canonical và registration child facts.
- `HealthExam.Server/Service/IHisEmrApi.cs`: thêm operation tạo/đồng bộ Patient HIS.
- `HealthExam.Server/Service/HisEmrHttpClient.cs`: route/configured request cho HIS Patient.
- `HealthExam.Server/Service/HisEmrOptions.cs`: thêm `HIS_EMR_PATIENT_CREATE_ROUTE` và validation khi Patient sync bật.
- `HealthExam.Server/Service/DependencyInjection.cs`: đăng ký `PatientService` và `HisPatientService`.
- `HealthExam.API/Controllers/ExamRecordController.cs`: giữ route, chỉ thay orchestration nội bộ nếu cần.
- `HealthExam.Tests/InMemoryTestDb.cs`: register services/repositories mới cho unit tests.
- `HealthExam.Tests/FakeHisEmrApi.cs`: fake Patient sync operation.
- `HealthExam.Tests/HisAdmissionServiceTests.cs`: assert admission payload lấy dữ liệu từ normalized Patient/registration.
- `HealthExam.Tests/ExamRecordRegistrationFieldsTests.cs`: assert normalized persistence và API mapping.
- `Deploy/schema/health-exam-schema.sql`: publish additive schema và sau cùng publish drop migration riêng.

---

### Task 1: Chốt contracts và test harness cho normalized model

**Files:**
- Create: `HealthExam.Tests/Registration3NfPersistenceTests.cs`
- Create: `HealthExam.Tests/PatientServiceTests.cs`
- Modify: `HealthExam.Tests/InMemoryTestDb.cs`
- Modify: `HealthExam.Tests/ExamRecordRegistrationFieldsTests.cs`

**Interfaces:**
- Consumes: existing `InMemoryTestDb`, `ExamRecordService`, `MasterDataService`.
- Produces: failing tests that define `PatientRefID`, child FK properties, reuse behavior and API-compatible mapping.

- [ ] **Step 1: Write the failing normalized persistence test.**

Add a test that creates two registrations with the same positive HIS Patient ID and asserts one local Patient is reused, while different registration-specific employment/insurance rows are selected:

```csharp
[Fact]
public async Task Registrations_reuse_patient_but_keep_registration_facts_separate()
{
    using var db = new InMemoryTestDb();
    var session = db.SeedSession();

    var first = await db.Records.CreateAsync(new ExamRecordSaveRequest
    {
        SessionID = session.SessionID,
        PatientID = 7001,
        PatientCode = "NB-7001",
        FullName = "Nguyễn Văn A",
        VariantCode = "DTK_01",
        StaffCode = "NV-01",
        OrgDeptName = "Kinh doanh",
        JobTitle = "Nhân viên",
        InsuranceNumber = "BH-01"
    });

    var second = await db.Records.CreateAsync(new ExamRecordSaveRequest
    {
        SessionID = session.SessionID,
        PatientID = 7001,
        PatientCode = "NB-7001",
        FullName = "Nguyễn Văn A",
        VariantCode = "DTK_01",
        StaffCode = "NV-02",
        OrgDeptName = "Kỹ thuật",
        JobTitle = "Kỹ sư",
        InsuranceNumber = "BH-02"
    });

    Assert.Equal(first.PatientID, second.PatientID);
    Assert.NotEqual(first.EmploymentRefID, second.EmploymentRefID);
    Assert.NotEqual(first.InsuranceRefID, second.InsuranceRefID);
}
```

- [ ] **Step 2: Write the failing API compatibility assertions.**

Extend `ExamRecordRegistrationFieldsTests` so `GetAsync` returns the old `PatientID`, `PatientCode`, `FullName`, `PatientTypeCode`, `PatientTypeName`, `ExamLocationCode` and `ExamLocationName` even though their source is normalized.

- [ ] **Step 3: Run the focused tests and verify they fail for missing normalized properties/services.**

Run:

```bash
DOTNET_ROLL_FORWARD=Major TZ=Asia/Ho_Chi_Minh dotnet test HealthExam.Tests/HealthExam.Tests.csproj --filter "FullyQualifiedName~Registration3NfPersistenceTests|FullyQualifiedName~ExamRecordRegistrationFieldsTests" --no-restore
```

Expected: FAIL because `PatientRefID` and child references do not exist yet.

- [ ] **Step 4: Add only the test harness seams needed by later tasks.**

Update `InMemoryTestDb` after the entity/repository task is available so every test gets the same concrete `PatientService` and `HisPatientService` instances. Do not add mocks for EF repositories.

- [ ] **Step 5: Commit the contract tests.**

```bash
git add HealthExam.Tests/Registration3NfPersistenceTests.cs HealthExam.Tests/PatientServiceTests.cs HealthExam.Tests/InMemoryTestDb.cs HealthExam.Tests/ExamRecordRegistrationFieldsTests.cs
git commit -m "test: define normalized registration contracts"
```

### Task 2: Add normalized Patient entities and EF schema

**Files:**
- Create: `HealthExam.Core/EntityFramework/Entity/Patient.cs`
- Create: `HealthExam.Core/EntityFramework/Entity/PatientInsurance.cs`
- Create: `HealthExam.Core/EntityFramework/Entity/PatientEmployment.cs`
- Create: `HealthExam.Core/EntityFramework/Entity/PatientRelative.cs`
- Modify: `HealthExam.Core/EntityFramework/Entity/ExamRecord.cs`
- Modify: `HealthExam.Core/EntityFramework/HealthExamDbContext.cs`
- Modify: `HealthExam.Core/EntityFramework/Repositories/IUnitOfWork.cs`
- Modify: `HealthExam.Core/EntityFramework/Repositories/UnitOfWork.cs`
- Modify: `HealthExam.Core/Models/RecordFieldLengths.cs`
- Test: `HealthExam.Tests/RegistrationPersistenceModelTests.cs`

**Interfaces:**
- Consumes: existing `AuditableEntity`, `MasterDataOption`, `ExamRecord`.
- Produces: `Patient`, `PatientInsurance`, `PatientEmployment`, `PatientRelative`; `ExamRecord.PatientRefID`, `InsuranceRefID`, `EmploymentRefID`, `RelativeRefID`; repositories exposed through `IUnitOfWork`.

- [ ] **Step 1: Add failing model assertions.**

Extend `RegistrationPersistenceModelTests` to assert table names, required properties, the Patient unique index, and the four `ExamRecord` FKs. Assert delete behavior is `Restrict`/`NoAction` for Patient child rows so deleting a Patient cannot silently delete registration history.

- [ ] **Step 2: Define `Patient` and child entities.**

Use `Guid` local keys and these exact properties:

```csharp
public sealed class Patient : AuditableEntity
{
    public Guid PatientRefID { get; set; }
    public string DivisionID { get; set; } = "";
    public long? HisPatientID { get; set; }
    public string PatientCode { get; set; } = "";
    public string FullName { get; set; } = "";
    public DateOnly? Dob { get; set; }
    public short? BirthYear { get; set; }
    public short GenderID { get; set; }
    public string IdentityNumber { get; set; } = "";
    public DateOnly? IdentityIssuedDate { get; set; }
    public Guid? IdentityIssuerOptionID { get; set; }
    public string PhoneNumber { get; set; } = "";
    public string Email { get; set; } = "";
    public string Address { get; set; } = "";
    public Guid? EthnicityOptionID { get; set; }
    public string BloodAboCode { get; set; } = "";
    public string BloodRhCode { get; set; } = "";
    public string HisSyncStatus { get; set; } = "Pending";
    public string HisSyncError { get; set; } = "";
}
```

`PatientInsurance`, `PatientEmployment` and `PatientRelative` each carry their own UUID, `PatientRefID`, business fields and audit fields. They do not store display names copied from master-data.

- [ ] **Step 3: Add normalized FK properties to `ExamRecord`.**

Add nullable `Guid? PatientRefID`, `InsuranceRefID`, `EmploymentRefID`, `RelativeRefID`, and nullable `Guid? PatientTypeOptionID`, `PaymentSourceOptionID`, `ExamLocationOptionID`. Keep the existing long/string fields unchanged for the transition release.

- [ ] **Step 4: Configure EF mapping and repositories.**

Map the tables as `HEX_Patient`, `HEX_PatientInsurance`, `HEX_PatientEmployment`, `HEX_PatientRelative`; add `(DivisionID, HisPatientID)` unique filtered index; add FK indexes; add `Restrict` delete behavior; add `ExamRecord` FK indexes. Add repository properties to `IUnitOfWork` and `UnitOfWork`.

- [ ] **Step 5: Run model tests and create the additive migration.**

Run:

```bash
DOTNET_ROLL_FORWARD=Major dotnet test HealthExam.Tests/HealthExam.Tests.csproj --filter FullyQualifiedName~RegistrationPersistenceModelTests
dotnet ef migrations add NormalizeRegistration3Nf --project HealthExam.Core/HealthExam.Core.csproj --startup-project HealthExam.API/HealthExam.API.csproj --output-dir Migrations
```

Expected: model tests pass and the migration only creates new tables/indexes/columns. Review the generated migration and reject any `DropColumn` or destructive operation.

- [ ] **Step 6: Publish the additive schema script.**

Update `Deploy/schema/health-exam-schema.sql` with the generated migration operations and verify that rerunning the script is safe through the existing migration-history guards.

- [ ] **Step 7: Commit the schema task.**

```bash
git add HealthExam.Core HealthExam.Tests/RegistrationPersistenceModelTests.cs Deploy/schema/health-exam-schema.sql
git commit -m "feat: add normalized patient registration schema"
```

### Task 3: Implement Patient aggregate and immutable child facts

**Files:**
- Create: `HealthExam.Server/Models/PatientModels.cs`
- Create: `HealthExam.Server/Service/PatientService.cs`
- Modify: `HealthExam.Server/Service/DependencyInjection.cs`
- Modify: `HealthExam.Tests/InMemoryTestDb.cs`
- Modify: `HealthExam.Tests/PatientServiceTests.cs`

**Interfaces:**
- Consumes: `IUnitOfWork.Patients`, `PatientInsurances`, `PatientEmployments`, `PatientRelatives`; `IHealthExamContext`.
- Produces: `PatientService.UpsertLocalAsync(PatientWriteData, CancellationToken)` returning `PatientWriteResult`; `GetOrCreateInsuranceAsync`, `GetOrCreateEmploymentAsync`, `GetOrCreateRelativeAsync` returning UUID references.

- [ ] **Step 1: Write failing Patient service tests.**

Cover these exact cases:

```csharp
[Fact] public async Task Same_his_patient_id_reuses_patient_within_division() { /* assert one PatientRefID */ }
[Fact] public async Task Same_his_patient_id_in_other_division_does_not_cross_tenant() { /* assert separate rows */ }
[Fact] public async Task Patient_without_his_id_creates_local_patient_without_heuristic_merge() { /* duplicate CCCD stays separate */ }
[Fact] public async Task Changed_insurance_creates_new_row_and_does_not_mutate_referenced_row() { /* old row unchanged */ }
[Fact] public async Task Changed_employment_creates_new_row_and_does_not_mutate_referenced_row() { /* old row unchanged */ }
[Fact] public async Task Changed_relative_creates_new_row_and_does_not_mutate_referenced_row() { /* old row unchanged */ }
```

- [ ] **Step 2: Define internal command/result models.**

`PatientWriteData` carries the current Patient fields plus optional HIS ID. `PatientWriteResult` carries `PatientRefID`, `HisPatientID`, `PatientCode`, and the selected child references. Models are internal service contracts; public `ExamRecordWriteRequest` remains unchanged.

- [ ] **Step 3: Implement Patient lookup/upsert.**

Query by `(DivisionID, HisPatientID)` only when the incoming HIS ID is positive. If no HIS ID exists, always create a new local Patient for the registration. Update canonical Patient fields in the same local transaction and set `HisSyncStatus = "Pending"` when an outbound HIS sync is needed.

- [ ] **Step 4: Implement child-fact creation.**

Normalize empty strings to null for child rows. Reuse an existing child row only when the complete business tuple matches the same Patient and its values are unchanged; otherwise insert a new row. Never update a row that is referenced by any `ExamRecord`.

- [ ] **Step 5: Run Patient service tests.**

```bash
DOTNET_ROLL_FORWARD=Major TZ=Asia/Ho_Chi_Minh dotnet test HealthExam.Tests/HealthExam.Tests.csproj --filter FullyQualifiedName~PatientServiceTests --no-restore
```

Expected: all Patient reuse, tenant, no-merge and immutability tests pass.

- [ ] **Step 6: Register the concrete service and commit.**

```bash
git add HealthExam.Server/Models/PatientModels.cs HealthExam.Server/Service/PatientService.cs HealthExam.Server/Service/DependencyInjection.cs HealthExam.Tests/InMemoryTestDb.cs HealthExam.Tests/PatientServiceTests.cs
git commit -m "feat: add patient aggregate and versioned facts"
```

### Task 4: Resolve master-data IDs without changing the API contract

**Files:**
- Modify: `HealthExam.Server/Models/MasterDataModels.cs`
- Modify: `HealthExam.Server/Service/MasterDataService.cs`
- Modify: `HealthExam.Server/Service/ExamRecordService.cs`
- Modify: `HealthExam.Tests/MasterDataTests.cs`
- Modify: `HealthExam.Tests/ExamRecordRegistrationFieldsTests.cs`

**Interfaces:**
- Consumes: existing `IMasterDataService.ResolveAsync` and tenant filtering.
- Produces: `ResolvedMasterDataOption(Guid OptionID, string Code, string Name, int OrderNo)` through `ResolveOptionAsync(category, code, field, ct)` for DB-backed categories.

- [ ] **Step 1: Add a failing test for resolved `OptionID`.**

Seed `PATIENT_TYPE` and `EXAM_LOCATION`, call `ResolveOptionAsync`, and assert the returned `OptionID`, code and name. Assert inactive and cross-tenant codes still return field-specific bad request.

- [ ] **Step 2: Implement `ResolveOptionAsync`.**

Keep static code catalogs (`Gender`, `BloodAbo`, `BloodRh`, `Relationship`) on their current code path. For DB-backed categories, query `DivisionID`, category, code and `IsActive`; return the entity ID plus canonical code/name. Keep `ResolveAsync` as a compatibility wrapper returning `MasterDataOptionItem`.

- [ ] **Step 3: Add normalized option IDs to registration application mapping.**

When request fields are non-null, resolve `PatientTypeCode`, `PaymentSourceCode`, `ExamLocationCode`, `InsuranceObjectCode`, `EthnicityCode`, `OccupationCode` and `IdentityIssuerCode`; store the resolved IDs in the corresponding normalized entity. Continue producing the old code/name response shape.

- [ ] **Step 4: Test the original two-field behavior with real seed codes.**

Use `OUTPATIENT` and `CLINIC_3_F2` in the test, assert both normalized option IDs and legacy response fields. Keep the existing test with arbitrary `DV`/`PK1` only if those options are explicitly seeded in the test fixture.

- [ ] **Step 5: Run and commit.**

```bash
DOTNET_ROLL_FORWARD=Major TZ=Asia/Ho_Chi_Minh dotnet test HealthExam.Tests/HealthExam.Tests.csproj --filter "FullyQualifiedName~MasterDataTests|FullyQualifiedName~ExamRecordRegistrationFieldsTests" --no-restore
git add HealthExam.Server/Models/MasterDataModels.cs HealthExam.Server/Service/MasterDataService.cs HealthExam.Server/Service/ExamRecordService.cs HealthExam.Tests/MasterDataTests.cs HealthExam.Tests/ExamRecordRegistrationFieldsTests.cs
git commit -m "feat: persist normalized master-data references"
```

### Task 5: Switch create/update/list/get to normalized registration reads and writes

**Files:**
- Modify: `HealthExam.Server/Service/ExamRecordService.cs`
- Modify: `HealthExam.Server/Models/ExamRecordModels.cs`
- Modify: `HealthExam.Server/Service/ExamImportService.cs`
- Modify: `HealthExam.Tests/ExamRecordRegistrationFieldsTests.cs`
- Modify: `HealthExam.Tests/ExamRecordListFilterTests.cs`
- Modify: `HealthExam.Tests/ExamImportTests.cs`
- Test: `HealthExam.Tests/Registration3NfPersistenceTests.cs`

**Interfaces:**
- Consumes: `PatientService`, `ResolveOptionAsync`, normalized `ExamRecord` FKs.
- Produces: unchanged `CreateInDefaultSessionAsync`, `CreateAsync`, `UpdateAsync`, `GetAsync`, `ListAsync`; normalized source of truth with temporary legacy mirror.

- [ ] **Step 1: Add failing create/update/list tests.**

Assert that:

- create creates/reuses Patient and writes all child FK references;
- update with changed BHYT/employment/relative creates new child rows and leaves old rows unchanged;
- `GetAsync` and `ListAsync` return old API fields from joins;
- import uses exactly the same normalized path;
- a record with no normalized FK still reads through the legacy fallback during transition.

- [ ] **Step 2: Add normalized write orchestration.**

Within the existing local transaction, call `PatientService`, resolve registration options, create/update `ExamRecord` header and child references, then save. Keep legacy fields populated through one mapper method named `MirrorLegacyRegistrationFields(ExamRecord, PatientAggregate, RegistrationFacts)` until Release C. No controller contract changes.

- [ ] **Step 3: Add normalized read projection.**

Update `GetAsync`, `ListAsync` and `ToItem` to join/include Patient and child rows. For backfilled/migrating rows use normalized values first and legacy values only when the normalized reference is null. Keep tenant filters on every join.

- [ ] **Step 4: Update import path.**

Do not add a second import implementation. Ensure `ExamImportService` continues calling `ExamRecordService.CreateAsync`; update only any direct field access that assumes Patient fields live on `ExamRecord`.

- [ ] **Step 5: Run focused regression tests.**

```bash
DOTNET_ROLL_FORWARD=Major TZ=Asia/Ho_Chi_Minh dotnet test HealthExam.Tests/HealthExam.Tests.csproj --filter "FullyQualifiedName~Registration3NfPersistenceTests|FullyQualifiedName~ExamRecordRegistrationFieldsTests|FullyQualifiedName~ExamRecordListFilterTests|FullyQualifiedName~ExamImportTests" --no-restore
```

- [ ] **Step 6: Commit normalized application flow.**

```bash
git add HealthExam.Server/Service/ExamRecordService.cs HealthExam.Server/Models/ExamRecordModels.cs HealthExam.Server/Service/ExamImportService.cs HealthExam.Tests/ExamRecordRegistrationFieldsTests.cs HealthExam.Tests/ExamRecordListFilterTests.cs HealthExam.Tests/ExamImportTests.cs HealthExam.Tests/Registration3NfPersistenceTests.cs
git commit -m "feat: route registration flows through normalized model"
```

### Task 6: Move HIS Patient sync ahead of Admission creation

**Files:**
- Create: `HealthExam.Server/Models/HisPatientModels.cs`
- Create: `HealthExam.Server/Service/HisPatientService.cs`
- Modify: `HealthExam.Server/Service/IHisEmrApi.cs`
- Modify: `HealthExam.Server/Service/HisEmrHttpClient.cs`
- Modify: `HealthExam.Server/Service/HisEmrOptions.cs`
- Modify: `HealthExam.Server/Service/DependencyInjection.cs`
- Modify: `HealthExam.Server/Service/ExamRecordService.cs`
- Modify: `HealthExam.Server/Service/HisAdmissionService.cs`
- Modify: `HealthExam.Tests/FakeHisEmrApi.cs`
- Create: `HealthExam.Tests/HisPatientServiceTests.cs`
- Modify: `HealthExam.Tests/HisAdmissionServiceTests.cs`
- Modify: `HealthExam.Tests/HisAdmissionLinkTests.cs`
- Modify: `HealthExam.Tests/HisEmrHttpClientTests.cs`

**Interfaces:**
- Consumes: normalized Patient, `IHisEmrApi`, existing admission operation.
- Produces: `IHisEmrApi.CreatePatientAsync(HisPatientWireRequest, CancellationToken)`, `HisPatientService.EnsurePatientAsync(Guid patientRefId, CancellationToken)`, and an updated admission flow that refuses to create Admission without a valid `HisPatientID`/PatientCode.

- [ ] **Step 1: Add the configured HIS route contract.**

Add `HisEmrOptions.PatientCreateRoute` loaded from `HIS_EMR_PATIENT_CREATE_ROUTE`. If HIS integration is enabled and this value is empty, `HisPatientService` throws `VendorNotConfigured` before making an HTTP call. This avoids guessing an HIS route that is not present in the current repository.

- [ ] **Step 2: Write failing transport tests.**

Add `HisPatientWireRequest` with the exact HIS payload fields agreed by the HIS contract owner, and assert `HisEmrHttpClient` posts to the configured route, forwards the credential/trace/tenant headers and parses a positive returned `HisPatientID`. Assert malformed/zero/string responses produce `HisBadGateway`.

- [ ] **Step 3: Implement `HisPatientService`.**

Load the tenant-owned Patient tracking row. Return immediately when `HisPatientID > 0`; otherwise validate required PatientCode/name fields, call `CreatePatientAsync`, parse the positive ID, set `HisPatientID`, `HisSyncStatus = "Linked"`, clear `HisSyncError`, and save. On external failure, preserve the local Patient and set `HisSyncStatus = "Failed"` with a bounded error message before rethrowing the business exception.

- [ ] **Step 4: Update create flow ordering.**

After the local Patient/registration transaction succeeds, call `EnsurePatientAsync`, then call the existing `EnsureAdmissionAsync`. The local registration remains available when either HIS call fails. Retry is idempotent because both services return early when their external ID is already stored.

- [ ] **Step 5: Update admission payload source.**

Modify `HisAdmissionService` to load `ExamRecord` with `Patient` and selected versioned facts. Build `HisAdmissionWireRequest` from Patient canonical fields and registration-specific references, not from legacy Patient columns on `ExamRecord`.

- [ ] **Step 6: Run HIS-focused tests and commit.**

```bash
DOTNET_ROLL_FORWARD=Major TZ=Asia/Ho_Chi_Minh dotnet test HealthExam.Tests/HealthExam.Tests.csproj --filter "FullyQualifiedName~HisPatientServiceTests|FullyQualifiedName~HisAdmissionServiceTests|FullyQualifiedName~HisAdmissionLinkTests|FullyQualifiedName~HisEmrHttpClientTests" --no-restore
git add HealthExam.Server HealthExam.Tests/FakeHisEmrApi.cs HealthExam.Tests/HisPatientServiceTests.cs HealthExam.Tests/HisAdmissionServiceTests.cs HealthExam.Tests/HisAdmissionLinkTests.cs HealthExam.Tests/HisEmrHttpClientTests.cs
git commit -m "feat: sync normalized patients before HIS admission"
```

### Task 7: Build idempotent backfill and verification scripts

**Files:**
- Create: `Deploy/migration/20260911_registration_3nf_backfill.sql`
- Create: `Deploy/migration/20260911_registration_3nf_verify.sql`
- Create: `HealthExam.Tests/Registration3NfBackfillTests.cs`
- Modify: `Deploy/schema/health-exam-schema.sql`

**Interfaces:**
- Consumes: additive normalized schema, existing `HEX_ExamRecord` legacy columns and `HEX_MasterDataOption`.
- Produces: deterministic Patient/child mappings, conflict report and zero-orphan verification queries.

- [ ] **Step 1: Write PostgreSQL backfill acceptance tests.**

Use `PostgresTestDb` to seed: two records with one HIS PatientID, two records with PatientID zero, changed insurance/employment/relative values, one invalid master code and two tenants. Assert shared/local Patient rules, references, preservation of values and conflict rows.

- [ ] **Step 2: Implement Patient backfill.**

Insert one Patient per `(DivisionID, PatientID)` for positive PatientID values using `ON CONFLICT` on the normalized unique key. Insert one local Patient per legacy record when PatientID is zero. Populate `ExamRecord.PatientRefID` only where null.

- [ ] **Step 3: Implement child-fact backfill.**

Insert child rows keyed by Patient plus the complete legacy business tuple; update registration FK columns only where null. Do not overwrite an already backfilled reference. Keep invalid master-code rows in a conflict result table or a deterministic report query.

- [ ] **Step 4: Make the script rerunnable.**

Wrap each phase in a transaction, use `ON CONFLICT`/`WHERE ... IS NULL`, and make the script safe to rerun after a failed batch. Do not delete legacy values during backfill.

- [ ] **Step 5: Add verification queries.**

`20260911_registration_3nf_verify.sql` must report: registrations without PatientRefID, child FK rows from another tenant, duplicate `(DivisionID, HisPatientID)`, invalid option category/FK pairs, normalized/legacy projection mismatches and counts before/after migration.

- [ ] **Step 6: Run backfill tests twice and commit.**

```bash
DOTNET_ROLL_FORWARD=Major TZ=Asia/Ho_Chi_Minh dotnet test HealthExam.Tests/HealthExam.Tests.csproj --filter FullyQualifiedName~Registration3NfBackfillTests --no-restore
git add Deploy/migration/20260911_registration_3nf_backfill.sql Deploy/migration/20260911_registration_3nf_verify.sql HealthExam.Tests/Registration3NfBackfillTests.cs Deploy/schema/health-exam-schema.sql
git commit -m "feat: add idempotent registration 3nf backfill"
```

### Task 8: Add compatibility comparison and release gates

**Files:**
- Create: `HealthExam.Server/Service/Registration3NfConsistencyService.cs`
- Create: `HealthExam.Tests/Registration3NfConsistencyTests.cs`
- Modify: `HealthExam.Server/Service/DependencyInjection.cs`
- Modify: `README.md`

**Interfaces:**
- Consumes: normalized projection and legacy columns.
- Produces: a read-only consistency report with counts and record IDs for mismatches; no automatic data mutation.

- [ ] **Step 1: Write the consistency test.**

Seed one matching and one mismatching record, run `CheckAsync(DivisionID, CancellationToken)`, and assert the result contains mismatch field names and record IDs without changing either source row.

- [ ] **Step 2: Implement the read-only comparison.**

Compare Patient fields, selected child facts, master-data code/name projections and registration-specific fields. Return counts grouped by field; cap returned IDs at 100 per field to prevent an oversized operational response.

- [ ] **Step 3: Document release gates.**

Add the exact commands for migration, backfill, verification and focused tests to `README.md`. State that Release C cannot run while orphan, duplicate-HIS-key, invalid-option or projection-mismatch counts are non-zero.

- [ ] **Step 4: Run and commit.**

```bash
DOTNET_ROLL_FORWARD=Major TZ=Asia/Ho_Chi_Minh dotnet test HealthExam.Tests/HealthExam.Tests.csproj --filter FullyQualifiedName~Registration3NfConsistencyTests --no-restore
git add HealthExam.Server/Service/Registration3NfConsistencyService.cs HealthExam.Tests/Registration3NfConsistencyTests.cs HealthExam.Server/Service/DependencyInjection.cs README.md
git commit -m "docs: add registration 3nf rollout gates"
```

### Task 9: Full regression and Release C cleanup migration

**Files:**
- Create: `HealthExam.Core/Migrations/20260911120000_DropRegistrationLegacyColumns.cs` after the Release C gate passes
- Modify: `HealthExam.Core/EntityFramework/Entity/ExamRecord.cs`
- Modify: `HealthExam.Core/EntityFramework/HealthExamDbContext.cs`
- Modify: `HealthExam.Server/Service/ExamRecordService.cs`
- Modify: `Deploy/schema/health-exam-schema.sql`
- Modify: affected tests under `HealthExam.Tests/`

**Interfaces:**
- Consumes: normalized source of truth, successful consistency report and all consumer migrations.
- Produces: no legacy Patient/master-data columns on `HEX_ExamRecord`; unchanged public API mapping.

- [x] **Step 1: Run the complete pre-cleanup suite.**

```bash
DOTNET_ROLL_FORWARD=Major TZ=Asia/Ho_Chi_Minh dotnet test HealthExam.Tests/HealthExam.Tests.csproj --no-restore
```

Expected: all tests pass and the consistency report has zero blocking mismatches.

- [x] **Step 2: Add failing tests that prove legacy columns are no longer required.**

Run the normalized persistence, list/filter, import, HIS admission, form gateway and API endpoint tests against a schema/model without legacy Patient fields. Keep public `ExamRecordItem` assertions unchanged.

- [x] **Step 3: Generate and review the cleanup migration.**

Drop only columns moved to Patient/child tables. Keep `AdmissionID`, form-server links, state, timestamps, progress and other registration/integration fields. Reject any migration that drops a registration fact or changes a public response field.

- [x] **Step 4: Remove legacy mirror/fallback code.**

Delete `MirrorLegacyRegistrationFields`, legacy fallback branches and obsolete entity properties only after the cleanup migration is reviewed. Keep API mapping through normalized joins.

- [x] **Step 5: Publish the cleanup schema and run the full suite again.**

```bash
DOTNET_ROLL_FORWARD=Major TZ=Asia/Ho_Chi_Minh dotnet test HealthExam.Tests/HealthExam.Tests.csproj --no-restore
```

- [x] **Step 6: Commit Release C separately.**

```bash
git add HealthExam.Core HealthExam.Server HealthExam.API HealthExam.Tests Deploy/schema/health-exam-schema.sql
git commit -m "refactor: remove legacy registration denormalization"
```

## Verification Checklist Before Completion

- [x] `GET /v1/master-data/registration-options` returns tenant-active `PatientTypes` and `ExamLocations`.
- [x] POST/PUT `/v1/exam-records` still accepts the current request and returns PascalCase response fields.
- [x] Two registrations with the same HIS PatientID reuse one Patient.
- [x] BHYT/employment/relative changes create new rows and do not mutate old registration facts.
- [x] Patient sync failure leaves local Patient and registration available for retry.
- [x] Admission creation uses normalized Patient data and remains idempotent.
- [x] Backfill is rerunnable and produces no orphan or cross-tenant references.
- [x] Consistency report is clean before any legacy column is removed.
- [x] Full test suite passes on the post-cleanup schema.
