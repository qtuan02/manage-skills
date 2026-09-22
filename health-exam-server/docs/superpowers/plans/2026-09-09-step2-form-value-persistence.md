# Step 2 Form Value Persistence Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let a receptionist save and later edit the `HISTORY` (64) and `EXTRA_INFO` (63) values of `KSK-TREN18TUOI` without losing either section, while health-exam-server owns HIS admission and M03 integration.

**Architecture:** Keep one HIS M03 form instance per `HEX_ExamRecord`. A record-scoped value service resolves the tenant mapping and live definition, ensures the record has an HIS admission, reads existing M03 values, merges the requested section by `ItemID`, then sends one complete `Details` snapshot through the HIS transport. The browser only sends semantic section kind plus field values and refetches the normalized form after save.

**Tech Stack:** .NET 8, ASP.NET Core, EF Core/PostgreSQL, Newtonsoft JSON, xUnit; React, TypeScript, TanStack Query, React Hook Form, Vitest.

**Spec:** `docs/superpowers/specs/2026-09-09-step2-form-value-persistence-design.md`

## Global Constraints

- Support only `DTK_03` → `KSK-TREN18TUOI`, `HISTORY` → `ItemGroupID 64`, and `EXTRA_INFO` → `ItemGroupID 63` in this phase.
- Use `POST /api/M03F10010/CUEMR`; do not use `M02F01500/CUEMR`, create medical processes, or alter signing state.
- An M03 update replaces every detail row, so every save must send the fully merged snapshot.
- Client input never authorizes `AdmissionID`, `EMRDataID`, `TemplateID`, `ItemGroupID`, `DataType`, or `ControlStyle`.
- All record and mapping access is tenant-scoped; HIS writes make exactly one HTTP attempt.
- Preserve unrelated dirty files in the shared worktree.

---

## File structure

| File | Responsibility |
| --- | --- |
| `HealthExam.Core/EntityFramework/Entity/ExamRecord.cs` | Persistent HIS form pointer and sync result. |
| `HealthExam.Core/EntityFramework/HealthExamDbContext.cs` + migration | Mapping/columns for the local pointer and sync metadata. |
| `HealthExam.Server/Models/RegistrationFormModels.cs` | Expose stable `ItemID` and current value in normalized nodes; request/response value models. |
| `HealthExam.Server/Models/HisEmrModels.cs` | Private M03 read/write wire contracts. |
| `HealthExam.Server/Service/IHisEmrApi.cs`, `HisEmrHttpClient.cs` | Exact M03 REMR/CUEMR transport, no automatic retry. |
| `HealthExam.Server/Service/IRegistrationFormValueService.cs`, `RegistrationFormValueService.cs` | Tenant validation, admission ensure, read–merge–write orchestration. |
| `HealthExam.API/Controllers/ExamRecordRegistrationFormController.cs` | Record-scoped read/save HTTP contract. |
| `Frontend/.../health-exam-registration-form.ts` | Typed read/save hooks and cache invalidation. |
| `Frontend/.../dynamic-registration-form/*` | Editable controlled renderer and per-section save UI. |

### Task 1: Persist an M03 form pointer and expose writable field identity

**Files:**
- Modify: `HealthExam.Core/EntityFramework/Entity/ExamRecord.cs`
- Modify: `HealthExam.Core/EntityFramework/HealthExamDbContext.cs`
- Create: `HealthExam.Core/Migrations/<timestamp>_AddHisRegistrationFormState.cs`
- Modify: `HealthExam.Core/Migrations/HealthExamDbContextModelSnapshot.cs`
- Modify: `HealthExam.Server/Models/RegistrationFormModels.cs`
- Modify: `HealthExam.Server/Service/ExamGroupRegistrationFormService.cs`
- Test: `HealthExam.Tests/ExamGroupRegistrationFormServiceTests.cs`
- Test: `HealthExam.Tests/ExamRecordFormPersistenceTests.cs`

**Interfaces:**
- Produces `ExamRecord.HisEmrDataID : Guid?`, `HisFormTemplateID : Guid?`, `HisFormSyncStatus : string`, and `HisFormSyncError : string`.
- Produces normalized nodes with `ItemID : Guid?`, `Value : string`, and `Text : string?`; container nodes have no `ItemID`.

- [x] **Step 1: Write failing entity/model tests**

```csharp
[Fact]
public void Record_persists_his_form_pointer_and_sync_state()
{
    Assert.Equal(typeof(Guid?), typeof(ExamRecord).GetProperty("HisEmrDataID")!.PropertyType);
    Assert.Equal(typeof(Guid?), typeof(ExamRecord).GetProperty("HisFormTemplateID")!.PropertyType);
    Assert.Equal("Pending", new ExamRecord().HisFormSyncStatus);
}

[Fact]
public async Task Normalized_writable_node_keeps_his_item_id()
{
    var form = await service.GetRegistrationFormAsync("DTK_03");
    Assert.Equal(itemId, form.Sections.Single(x => x.Kind == "HISTORY").Nodes.Single().ItemID);
}
```

- [x] **Step 2: Run the focused tests and confirm they fail**

Run: `dotnet test HealthExam.Tests --filter "FullyQualifiedName~ExamRecordFormPersistenceTests|FullyQualifiedName~ExamGroupRegistrationFormServiceTests"`

Expected: compilation/test failure because the fields and normalized node properties do not exist.

- [x] **Step 3: Add the minimal local schema and normalized metadata**

Add nullable GUID columns and bounded non-null strings (`Pending`, `Synced`, `Failed`) to `HEX_ExamRecord`; use an EF migration and snapshot generated by the repository's standard migration command. In `NormalizeNodes`, parse only a valid GUID from raw `ItemID`, retain the raw type/control fields, and leave `ItemID = null` for labels/groups. Do not derive an ID from `NodeID`.

```csharp
public Guid? HisEmrDataID { get; set; }
public Guid? HisFormTemplateID { get; set; }
public string HisFormSyncStatus { get; set; } = "Pending";
public string HisFormSyncError { get; set; } = "";
```

- [x] **Step 4: Run the focused tests and EF build**

Run: `dotnet test HealthExam.Tests --filter "FullyQualifiedName~ExamRecordFormPersistenceTests|FullyQualifiedName~ExamGroupRegistrationFormServiceTests"`

Expected: PASS.

Run: `dotnet build HealthExam.sln --no-restore`

Expected: PASS.

- [x] **Step 5: Commit the isolated schema/metadata change**

```bash
git add HealthExam.Core HealthExam.Server/Models/RegistrationFormModels.cs HealthExam.Server/Service/ExamGroupRegistrationFormService.cs HealthExam.Tests/ExamRecordFormPersistenceTests.cs HealthExam.Tests/ExamGroupRegistrationFormServiceTests.cs
git commit -m "feat: persist HIS registration form state"
```

### Task 2: Add safe M03 read/write transport contracts

**Files:**
- Modify: `HealthExam.Server/Models/HisEmrModels.cs`
- Modify: `HealthExam.Server/Service/IHisEmrApi.cs`
- Modify: `HealthExam.Server/Service/HisEmrHttpClient.cs`
- Modify: `HealthExam.Tests/FakeHisEmrApi.cs`
- Test: `HealthExam.Tests/HisEmrHttpClientTests.cs`

**Interfaces:**
- Produces `Task<JToken> ReadFormDataAsync(HisEmrDataReadWireRequest request, CancellationToken ct)`.
- Produces `Task<Guid> SaveFormDataAsync(HisEmrDataSaveWireRequest request, CancellationToken ct)`.
- `HisEmrDataSaveWireRequest` contains `EMRDataID`, `TemplateID`, `AdmissionID`, patient/department context, `VoucherDate`, `IsDraft`, and `Details`; each detail has `ItemID`, `Value`, `Text`, `DataType`, `ControlStyle`.

- [x] **Step 1: Write failing transport tests**

```csharp
[Fact]
public async Task Save_form_posts_exactly_once_to_m03_cuemr()
{
    await client.SaveFormDataAsync(new HisEmrDataSaveWireRequest { EMRDataID = Guid.Empty, TemplateID = templateId, AdmissionID = 77, Details = [detail] });
    Assert.Single(handler.Calls);
    Assert.Equal("/api/M03F10010/CUEMR", handler.Calls[0].Path);
    Assert.Equal(templateId.ToString(), JObject.Parse(handler.Calls[0].Body)["TemplateID"]!.ToString());
}

[Fact]
public async Task Read_form_uses_remr_and_returns_his_details() { /* assert EMRDataID, TemplateID, AdmissionID query */ }
```

- [x] **Step 2: Run the transport tests and confirm they fail**

Run: `dotnet test HealthExam.Tests --filter "FullyQualifiedName~HisEmrHttpClientTests"`

Expected: compile failure for missing M03 contracts/methods.

- [x] **Step 3: Implement exact transport mapping**

Add route constants `api/M03F10010/REMR` and `api/M03F10010/CUEMR`. `REMR` includes `EMRDataID`, `TemplateID`, `AdmissionID`, `IsInherit=false`, and `Source` omitted. `CUEMR` uses existing `PostAsync`, so its mutation has `retryConnectionOnce: false`. Parse the successful CUEMR `Data` as a non-empty GUID; reject malformed success data with `HisBadGateway`. Extend the fake with stored read data, last save payload, call counts, and an injected save exception.

- [x] **Step 4: Run focused transport tests**

Run: `dotnet test HealthExam.Tests --filter "FullyQualifiedName~HisEmrHttpClientTests"`

Expected: PASS, including forwarded credential/division/trace headers and one-attempt behavior.

- [x] **Step 5: Commit transport capability**

```bash
git add HealthExam.Server/Models/HisEmrModels.cs HealthExam.Server/Service/IHisEmrApi.cs HealthExam.Server/Service/HisEmrHttpClient.cs HealthExam.Tests/FakeHisEmrApi.cs HealthExam.Tests/HisEmrHttpClientTests.cs
git commit -m "feat: add HIS M03 form value transport"
```

### Task 3: Implement registration-form value orchestration and merge safety

**Files:**
- Create: `HealthExam.Server/Service/IRegistrationFormValueService.cs`
- Create: `HealthExam.Server/Service/RegistrationFormValueService.cs`
- Modify: `HealthExam.Server/Models/RegistrationFormModels.cs`
- Modify: `HealthExam.Server/Service/ServiceCollectionExtensions.cs`
- Test: `HealthExam.Tests/RegistrationFormValueServiceTests.cs`

**Interfaces:**
- Consumes `IHisAdmissionService.EnsureAdmissionAsync(Guid, CancellationToken)`, `IHisEmrApi`, `IUnitOfWork`, and `IHealthExamContext`.
- Produces `Task<RegistrationRecordForm> GetAsync(Guid recordId, CancellationToken ct)` and `Task<RegistrationRecordForm> SaveSectionAsync(Guid recordId, string sectionKind, RegistrationSectionSaveRequest request, CancellationToken ct)`.

- [x] **Step 1: Write failing orchestration tests**

```csharp
[Fact]
public async Task Save_history_creates_admission_then_writes_both_section_snapshots()
{
    var result = await service.SaveSectionAsync(recordId, "HISTORY", new([new(item64, "có", "Có")], true));
    Assert.Equal(1, his.AdmissionCalls);
    Assert.Equal(1, his.FormSaveCalls);
    Assert.Contains(his.LastFormSave!.Details, x => x.ItemID == item64);
    Assert.Contains(his.LastFormSave!.Details, x => x.ItemID == item63 && x.Value == "retained");
    Assert.Equal("Synced", db.RecordOf(recordId).HisFormSyncStatus);
}

[Fact]
public async Task Save_rejects_foreign_or_read_only_field_before_his_mutation() { /* error 400, zero FormSaveCalls */ }

[Fact]
public async Task Failed_m03_save_keeps_admission_and_marks_record_failed() { /* HisFormSyncStatus=Failed */ }
```

- [x] **Step 2: Run the new service tests and confirm they fail**

Run: `dotnet test HealthExam.Tests --filter "FullyQualifiedName~RegistrationFormValueServiceTests"`

Expected: compile failure because the service and request contracts are absent.

- [x] **Step 3: Implement read–validate–merge–write**

Resolve the record by `RecordID + DivisionID`; resolve exactly one active mapping by its `VariantCode`, including sections; reject any section other than configured `HISTORY`/`EXTRA_INFO`. Fetch the live mapped template and use raw nodes to create the allow-list of writable GUID `ItemID`s, source `DataType` and `ControlStyle`. Call `EnsureAdmissionAsync`; reload the record and require its positive ID. If `HisEmrDataID` exists, call REMR and collect persisted details; otherwise begin empty. Replace only submitted IDs in the requested section, preserve all other valid details, then issue one CUEMR payload. On success persist the returned GUID, current template GUID, `Synced`, and empty error. On HIS failure preserve admission/old EMR pointer, set `Failed` plus a bounded non-PII message, save locally, then rethrow the original error.

- [x] **Step 4: Register and test service behavior**

Run: `dotnet test HealthExam.Tests --filter "FullyQualifiedName~RegistrationFormValueServiceTests"`

Expected: PASS for create, existing-form merge, tenant isolation, invalid fields, and admission/M03 failure paths.

- [x] **Step 5: Commit value service**

```bash
git add HealthExam.Server/Service/IRegistrationFormValueService.cs HealthExam.Server/Service/RegistrationFormValueService.cs HealthExam.Server/Models/RegistrationFormModels.cs HealthExam.Server/Service/ServiceCollectionExtensions.cs HealthExam.Tests/RegistrationFormValueServiceTests.cs
git commit -m "feat: save Step 2 form values through HIS"
```

### Task 4: Publish authenticated record-scoped read/save endpoints

**Files:**
- Create: `HealthExam.API/Controllers/ExamRecordRegistrationFormController.cs`
- Modify: `HealthExam.Tests/HisFormEndpointTests.cs`
- Create: `HealthExam.Tests/RegistrationFormValueEndpointTests.cs`

**Interfaces:**
- `GET /v1/exam-records/{recordId}/registration-form` returns `RegistrationRecordForm` with both sections and values.
- `PUT /v1/exam-records/{recordId}/registration-form/sections/{sectionKind}` accepts `RegistrationSectionSaveRequest` and returns the refreshed `RegistrationRecordForm` in `ResultData`.

- [x] **Step 1: Write failing controller/contract tests**

```csharp
[Fact]
public async Task Put_history_delegates_record_and_semantic_section_to_value_service()
{
    var response = await host.PutAsJsonAsync($"/v1/exam-records/{recordId}/registration-form/sections/HISTORY", new { fields = new[] { new { itemId, value = "Có" } }, isDraft = true });
    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
}

[Fact]
public void Form_value_routes_do_not_expose_his_ids_in_request_contract() { /* reflected request has only fields/isDraft */ }
```

- [x] **Step 2: Run endpoint tests and confirm they fail**

Run: `dotnet test HealthExam.Tests --filter "FullyQualifiedName~RegistrationFormValueEndpointTests|FullyQualifiedName~HisFormEndpointTests"`

Expected: 404 or missing-controller failure.

- [x] **Step 3: Add thin controller actions**

Use the existing `HealthExamControllerBase.Success(...)` envelope and `CancellationToken`; do not duplicate mapping, tenant, admission, or HIS logic in the controller. Document that the public section key is semantic and accepted values are only `HISTORY`/`EXTRA_INFO` as resolved by server configuration.

- [x] **Step 4: Run endpoint tests**

Run: `dotnet test HealthExam.Tests --filter "FullyQualifiedName~RegistrationFormValueEndpointTests|FullyQualifiedName~HisFormEndpointTests"`

Expected: PASS for success, no cross-tenant access, validation errors, and no leaked HIS authority fields.

- [x] **Step 5: Commit public API**

```bash
git add HealthExam.API/Controllers/ExamRecordRegistrationFormController.cs HealthExam.Tests/HisFormEndpointTests.cs HealthExam.Tests/RegistrationFormValueEndpointTests.cs
git commit -m "feat: expose record registration form values"
```

### Task 5: Connect the Step 2 client to the persisted API

**Files:**
- Modify: `Frontend/turbo-web/packages/api/src/health-exam/health-exam-registration-form-service.ts`
- Modify: `Frontend/turbo-web/packages/types/src/health-exam/registration-form.ts`
- Modify: `Frontend/turbo-web/apps/health-exam/src/hooks/api/health-exam-registration-form.ts`
- Modify: `Frontend/turbo-web/apps/health-exam/src/features/registration/components/record-form/dynamic-registration-form/registration-form-sections.tsx`
- Modify: `Frontend/turbo-web/apps/health-exam/src/features/registration/components/record-form/dynamic-registration-form/registration-form-tree.tsx`
- Test: `Frontend/turbo-web/apps/health-exam/test/features/registration/components/record-form/dynamic-registration-form.test.tsx`
- Test: `Frontend/turbo-web/apps/health-exam/test/hooks/api/health-exam-registration-form.test.tsx`

**Interfaces:**
- Produces `useRegistrationRecordFormQuery(recordId)` and `useSaveRegistrationSectionMutation(recordId)`.
- `RegistrationFormTree` consumes externally held values and invokes `onChange(ItemID, value, text)`; it no longer owns disposable local-only values.

- [x] **Step 1: Write failing hook/component tests**

```tsx
it("saves HISTORY with ItemID values and refreshes both sections", async () => {
  render(<RegistrationFormSections recordId="record-1" />);
  await user.type(screen.getByLabelText("Bệnh sử"), "Hen");
  await user.click(screen.getByRole("button", { name: "Lưu tiền sử bệnh" }));
  expect(mockPut).toHaveBeenCalledWith(expect.stringContaining("sections/HISTORY"), expect.objectContaining({ fields: [{ itemId, value: "Hen" }] }));
});
```

- [x] **Step 2: Run frontend focused tests and confirm they fail**

Run: `pnpm --filter health-exam test -- dynamic-registration-form health-exam-registration-form`

Expected: failure because record-scoped query/mutation and controlled tree props do not exist.

- [x] **Step 3: Implement typed client and controlled section saves**

Add GET/PUT methods to the existing generated-style registration form service and invalidate the record-form query after a successful PUT. Pass the current record ID from the edit view; for an unsaved record, keep fields in React Hook Form and disable section save with a clear “Lưu hồ sơ trước” message. Make tree controls use values keyed by actual `ItemID`; labels/group nodes remain non-writable. Render one save button and in-flight/error status per section, including inside the History dialog. Keep `EXTRA_INFO` visible in the page. Do not send blank container/label IDs or client-supplied control metadata.

- [x] **Step 4: Run focused frontend tests**

Run: `pnpm --filter health-exam test -- dynamic-registration-form health-exam-registration-form`

Expected: PASS for editable controls, save payload, loading/disabled/error states, and refreshing both sections after save.

- [x] **Step 5: Commit client integration**

```bash
git add packages/api/src/health-exam/health-exam-registration-form-service.ts packages/types/src/health-exam/registration-form.ts apps/health-exam/src/hooks/api/health-exam-registration-form.ts apps/health-exam/src/features/registration/components/record-form/dynamic-registration-form apps/health-exam/test/features/registration/components/record-form/dynamic-registration-form.test.tsx apps/health-exam/test/hooks/api/health-exam-registration-form.test.tsx
git commit -m "feat: save Step 2 registration sections"
```

### Task 6: Full regression, migration check, and documentation handoff

**Files:**
- Modify: `Backend/health-exam-server/README.md`
- Test: existing backend and frontend suites above

- [x] **Step 1: Write the failing documentation acceptance checklist**

Add a README API example that shows only `fields[].itemId`, `value`, `text`, and `isDraft`, plus the prerequisite HIS settings. It must not show a browser-provided HIS admission/form/template identifier.

- [x] **Step 2: Verify migration applies to an empty development database**

Run: `dotnet ef database update --project HealthExam.Core --startup-project HealthExam.API`

Expected: migration creates the four new `HEX_ExamRecord` columns without modifying existing admission or group-mapping data.

- [x] **Step 3: Run backend regression**

Run: `dotnet test HealthExam.Tests`

Expected: PASS.

- [x] **Step 4: Run frontend typecheck and regression**

Run: `pnpm --filter health-exam typecheck && pnpm --filter health-exam test`

Expected: PASS.

- [x] **Step 5: Commit verification/docs**

```bash
git add README.md
git commit -m "docs: document Step 2 HIS form persistence"
```

## Self-review

- Spec coverage: Tasks 1–2 establish durable pointers and M03 transport; Task 3 handles admission, mapping, validation, merge, retryable state, and tenant safety; Task 4 supplies the public API; Task 5 makes the planned browser flow usable; Task 6 verifies migration and regressions.
- Placeholder scan: no deferred or unspecified implementation steps remain; every mutation route, stored field, service method, test command, and failure behavior is named.
- Type consistency: the public request is `RegistrationSectionSaveRequest`; service methods are `GetAsync`/`SaveSectionAsync`; HIS methods are `ReadFormDataAsync`/`SaveFormDataAsync`; frontend uses the record-scoped registration form endpoints only.
