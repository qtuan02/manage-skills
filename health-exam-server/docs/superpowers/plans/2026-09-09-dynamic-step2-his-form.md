# Dynamic Step 2 HIS Form Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Render the HIS-defined Tiền sử bệnh and Thông tin bổ sung structures for the configured Nhóm khám in registration Step 2, initially `DTK_03 → KSK-TREN18TUOI`, without value entry or HIS writes.

**Architecture:** `health-exam-server` owns tenant-scoped mappings from a statutory group to a HIS template and from the two semantic sections to HIS `ItemGroupID`s. A group-facing API resolves the mapping, reuses the M03 bridge, and normalizes only `HISTORY` and `EXTRA_INFO`; the frontend consumes that contract with a generic read-only tree renderer.

**Tech Stack:** .NET 8, EF Core, PostgreSQL, xUnit; React, TypeScript, TanStack Query, Vitest.

**Spec:** `docs/superpowers/specs/2026-09-09-dynamic-step2-his-form-design.md`

## Global Constraints

- HIS M03 is the sole form-definition source; frontend must never call `his-server` or parse raw M03 JSON.
- Scope every mapping lookup and cache key by tenant.
- Seed exactly `DTK_03 → KSK-TREN18TUOI`, `HISTORY → 64`, and `EXTRA_INFO → 63`.
- Leave the ten-group `GET /v1/master-data/registration-options` contract unchanged.
- Step 2 uses only active mapped groups from `GET /v1/exam-groups/available`.
- Render-only: no React Hook Form binding for dynamic nodes and no HIS mutation.
- Missing mappings/sections/HIS templates must show an explicit retriable error, never a hard-coded fallback.
- Preserve and integrate pre-existing dirty HIS-form bridge edits; do not revert them.

---

## File structure

Backend: create `HealthExam.Core/EntityFramework/Entity/ExamGroupFormMapping.cs`, `HealthExam.Server/Models/RegistrationFormModels.cs`, `HealthExam.Server/Service/ExamGroupRegistrationFormService.cs`, and `HealthExam.API/Controllers/ExamGroupController.cs`; modify DbContext, UnitOfWork, `IExamFormService`, and `HisExamFormGateway.Definition.cs`; create EF migration, persistence/service/endpoint tests.

Frontend: create `packages/types/src/health-exam/registration-form.ts`, `packages/api/src/health-exam/health-exam-registration-form-service.ts`, its package test, and `apps/health-exam/src/hooks/api/health-exam-registration-form.ts`; modify HTTP singleton, group select and Step 2; create dynamic renderer components/tests; remove only the legacy Step 2 history/additional-info components after replacement coverage passes.

### Task 1: Persist the tenant-scoped template/section configuration

**Files:**
- Create: `HealthExam.Core/EntityFramework/Entity/ExamGroupFormMapping.cs`
- Modify: `HealthExam.Core/EntityFramework/HealthExamDbContext.cs`
- Modify: `HealthExam.Core/EntityFramework/Repositories/UnitOfWork.cs`
- Create: `HealthExam.Core/Migrations/<timestamp>_AddExamGroupFormMappings.cs`
- Modify: `HealthExam.Core/Migrations/HealthExamDbContextModelSnapshot.cs`
- Test: `HealthExam.Tests/ExamGroupFormMappingPersistenceTests.cs`

**Interfaces:** Produces `ExamGroupFormMapping { MappingID, DivisionID, VariantCode, TemplateCode, IsActive, Sections }` and `ExamGroupFormSectionMapping { SectionMappingID, MappingID, SectionKind, ItemGroupID }`; later work reads them through `IUnitOfWork`.

- [ ] **Step 1: Write failing persistence tests**

```csharp
[Fact]
public async Task Seeded_dtk03_mapping_has_the_two_confirmed_his_sections()
{
    await using var db = _fixture.NewContext();
    var mapping = await db.ExamGroupFormMappings.Include(x => x.Sections)
        .SingleAsync(x => x.DivisionID == "DEV" && x.VariantCode == "DTK_03");

    Assert.Equal("KSK-TREN18TUOI", mapping.TemplateCode);
    Assert.Contains(mapping.Sections, x => x.SectionKind == "HISTORY" && x.ItemGroupID == 64);
    Assert.Contains(mapping.Sections, x => x.SectionKind == "EXTRA_INFO" && x.ItemGroupID == 63);
}
```

- [ ] **Step 2: Run the test and confirm RED**

Run: `dotnet test HealthExam.Tests --filter FullyQualifiedName~ExamGroupFormMappingPersistenceTests`

Expected: FAIL because the entities and seed do not exist.

- [ ] **Step 3: Implement entities, EF constraints, repositories, migration, and seed**

```csharp
public sealed class ExamGroupFormMapping : AuditableEntity
{
    public Guid MappingID { get; set; }
    public string DivisionID { get; set; } = "";
    public string VariantCode { get; set; } = "";
    public string TemplateCode { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public List<ExamGroupFormSectionMapping> Sections { get; set; } = [];
}

public sealed class ExamGroupFormSectionMapping
{
    public Guid SectionMappingID { get; set; }
    public Guid MappingID { get; set; }
    public string SectionKind { get; set; } = "";
    public int ItemGroupID { get; set; }
    public ExamGroupFormMapping Mapping { get; set; } = null!;
}
```

Configure required/length fields, parent-child cascade delete, unique `(DivisionID, VariantCode)`, and unique `(MappingID, SectionKind)`. Generate migration with `dotnet ef migrations add AddExamGroupFormMappings --project HealthExam.Core --startup-project HealthExam.API`; inspect it, then seed the established development tenant with one fixed-GUID mapping plus `HISTORY/64` and `EXTRA_INFO/63` children.

- [ ] **Step 4: Run persistence tests and build**

Run: `dotnet test HealthExam.Tests --filter FullyQualifiedName~ExamGroupFormMappingPersistenceTests && dotnet build HealthExam.API`

Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add HealthExam.Core HealthExam.Tests/ExamGroupFormMappingPersistenceTests.cs
git commit -m "feat: configure HIS templates by exam group"
```

### Task 2: Expose normalized forms through group-based backend APIs

**Files:**
- Create: `HealthExam.Server/Models/RegistrationFormModels.cs`
- Create: `HealthExam.Server/Service/ExamGroupRegistrationFormService.cs`
- Modify: `HealthExam.Server/Service/IExamFormService.cs`
- Modify: `HealthExam.Server/Service/HisExamFormGateway.Definition.cs`
- Create: `HealthExam.API/Controllers/ExamGroupController.cs`
- Test: `HealthExam.Tests/ExamGroupRegistrationFormServiceTests.cs`
- Test: `HealthExam.Tests/ExamGroupEndpointTests.cs`

**Interfaces:** Produces `ListAvailableAsync()` and `GetRegistrationFormAsync(string variantCode)` returning `AvailableExamGroupItem` and `RegistrationFormDefinition`.

- [ ] **Step 1: Write failing mapping/normalization tests**

```csharp
[Fact]
public async Task Registration_form_contains_only_mapped_sections()
{
    SeedMapping("DEV", "DTK_03", "KSK-TREN18TUOI", ("HISTORY", 64), ("EXTRA_INFO", 63));
    his.Layout = JArray.Parse("""
      [{"ItemGroupID":64,"OrderString":"0001","ItemDesc":"Tiền sử"},
       {"ItemGroupID":63,"OrderString":"0002","ItemDesc":"Bổ sung"},
       {"ItemGroupID":99,"OrderString":"0003","ItemDesc":"Nội khoa"}]
      """);

    var result = await service.GetRegistrationFormAsync("DTK_03");

    Assert.Equal(["HISTORY", "EXTRA_INFO"], result.Sections.Select(x => x.Kind));
    Assert.DoesNotContain(result.Sections.SelectMany(x => x.Nodes), x => x.ItemGroupID == 99);
}

[Fact]
public async Task Unmapped_group_fails_without_calling_his()
{
    await Assert.ThrowsAsync<HealthExamException>(() => service.GetRegistrationFormAsync("DTK_01"));
    Assert.Equal(0, his.TemplateListCalls);
}
```

- [ ] **Step 2: Run service tests and confirm RED**

Run: `dotnet test HealthExam.Tests --filter FullyQualifiedName~ExamGroupRegistrationFormServiceTests`

Expected: FAIL because the service and DTOs do not exist.

- [ ] **Step 3: Implement contracts, mapping lookup, HIS read, normalization, and endpoints**

```csharp
public sealed class RegistrationFormDefinition
{
    public string VariantCode { get; init; } = "";
    public string TemplateCode { get; init; } = "";
    public string TemplateName { get; init; } = "";
    public IReadOnlyList<RegistrationFormSection> Sections { get; init; } = [];
}
public sealed class RegistrationFormSection
{
    public string Kind { get; init; } = "";
    public int ItemGroupID { get; init; }
    public string Title { get; init; } = "";
    public IReadOnlyList<RegistrationFormNode> Nodes { get; init; } = [];
}
```

`RegistrationFormNode` carries `NodeID`, `ParentNodeID`, `ItemGroupID`, `Order`, `Level`, `Label`, `ControlType`, `DataType`, `Required`, and `ReadOnly`; do not expose `JToken`. Validate a trimmed statutory variant, one active tenant mapping, exactly one section mapping for each kind, and existence of both mapped group IDs in HIS layout. Return sections ordered HISTORY then EXTRA_INFO and nodes ordered `OrderString`.

Parameterize `IExamFormService.GetDefinitionAsync(string templateCode, CancellationToken)` and cache by normalized base URL + `DivisionID` + `TemplateCode`; keep existing narrow `KSK-TREN18TUOI` routes compatible. Add:

```http
GET /v1/exam-groups/available
GET /v1/exam-groups/{variantCode}/registration-form
```

`available` joins active mappings to `ExamGroups.All`, omits invalid stale mappings, and uses statutory `OrderNo`.

- [ ] **Step 4: Run all focused backend tests**

Run: `dotnet test HealthExam.Tests --filter "FullyQualifiedName~ExamGroupRegistrationFormServiceTests|FullyQualifiedName~ExamGroupEndpointTests|FullyQualifiedName~HisExamFormGatewayDefinitionTests"`

Expected: PASS; includes mapping tenant isolation, inactive/missing mapping rejection, IDs 64/63 filtering, and template/tenant cache isolation.

- [ ] **Step 5: Commit**

```bash
git add HealthExam.Server HealthExam.API/Controllers/ExamGroupController.cs HealthExam.Tests/ExamGroupRegistrationFormServiceTests.cs HealthExam.Tests/ExamGroupEndpointTests.cs
git commit -m "feat: expose registration forms by exam group"
```

### Task 3: Add typed frontend reads and query-backed group select

**Files:**
- Create: `packages/types/src/health-exam/registration-form.ts`
- Create: `packages/api/src/health-exam/health-exam-registration-form-service.ts`
- Test: `packages/api/test/health-exam/health-exam-registration-form-service.test.ts`
- Modify: `apps/health-exam/src/libs/http-client.ts`
- Create: `apps/health-exam/src/hooks/api/health-exam-registration-form.ts`
- Modify: `apps/health-exam/src/components/select/select-exam-object-group.tsx`
- Test: `apps/health-exam/test/components/select/select-exam-object-group.test.tsx`

**Interfaces:** Produces `HealthExamRegistrationFormService.listAvailableGroups()`, `.getRegistrationForm(variantCode)`, `useAvailableExamGroupsQuery()`, and `useRegistrationFormQuery(variantCode)`.

- [ ] **Step 1: Write failing package HTTP tests**

```ts
it("reads mapped groups and a normalized group form", async () => {
  const service = new HealthExamRegistrationFormService(client);
  await service.listAvailableGroups();
  await service.getRegistrationForm("DTK_03");

  expect(client.get).toHaveBeenNthCalledWith(1, "/v1/exam-groups/available");
  expect(client.get).toHaveBeenNthCalledWith(2, "/v1/exam-groups/DTK_03/registration-form");
});
```

- [ ] **Step 2: Run package test and confirm RED**

Run: `pnpm --filter @medviet/api test -- health-exam-registration-form-service.test.ts`

Expected: FAIL because the module does not exist.

- [ ] **Step 3: Implement types, service, singleton, hooks, and select switch**

```ts
export class HealthExamRegistrationFormService {
  constructor(private client: HttpClient) {}
  listAvailableGroups = () => this.client.get<AvailableExamGroupItem[]>("/v1/exam-groups/available");
  getRegistrationForm = (variantCode: string) =>
    this.client.get<RegistrationFormDefinition>(`/v1/exam-groups/${encodeURIComponent(variantCode)}/registration-form`);
}
```

Use a session-long cache policy for available groups; form query keys include `variantCode` and are disabled when it is blank. Make `SelectExamObjectGroup` read `useAvailableExamGroupsQuery`, preserving current accessible loading/error/retry behavior and server order; it must stop reading `registration-options.ExamGroups`.

- [ ] **Step 4: Run package and select tests**

Run: `pnpm --filter @medviet/api test -- health-exam-registration-form-service.test.ts && pnpm --filter health-exam test -- select-exam-object-group.test.tsx`

Expected: PASS; only mapped groups become choices.

- [ ] **Step 5: Commit**

```bash
git add packages/types packages/api apps/health-exam/src/libs/http-client.ts apps/health-exam/src/hooks/api apps/health-exam/src/components/select/select-exam-object-group.tsx apps/health-exam/test/components/select/select-exam-object-group.test.tsx
git commit -m "feat: load available health exam groups"
```

### Task 4: Replace fixed Step 2 blocks with read-only normalized renderer

**Files:**
- Create: `apps/health-exam/src/features/registration/components/record-form/dynamic-registration-form/registration-form-sections.tsx`
- Create: `apps/health-exam/src/features/registration/components/record-form/dynamic-registration-form/registration-form-tree.tsx`
- Modify: `apps/health-exam/src/features/registration/components/record-form/step-examination.tsx`
- Modify: `packages/i18n/src/locales/vi.json`
- Test: `apps/health-exam/test/features/registration/components/record-form/dynamic-registration-form.test.tsx`
- Delete after tests pass: Step 2 `medical-history/` and `additional-info/` modules only; retain confirmation/results modules.

**Interfaces:** Consumes `useRegistrationFormQuery(variantCode)` and `RegistrationFormDefinition`; produces display-only Step 2 sections and makes no form writes.

- [ ] **Step 1: Write failing renderer/state tests**

```tsx
it("replaces legacy Step 2 blocks with both HIS sections", async () => {
  getRegistrationForm.mockResolvedValue(formWithHistoryAndExtra);
  render(<StepExaminationHarness initialVariantCode="DTK_03" />);

  expect(await screen.findByRole("group", { name: "Tiền sử bệnh" })).toBeInTheDocument();
  expect(screen.getByRole("group", { name: "Thông tin bổ sung" })).toBeInTheDocument();
  expect(screen.queryByRole("button", { name: "Khai báo tiền sử bệnh" })).not.toBeInTheDocument();
});

it("shows retry and not stale fields after a request error", async () => {
  getRegistrationForm.mockRejectedValueOnce(new Error("HIS unavailable"));
  render(<StepExaminationHarness initialVariantCode="DTK_03" />);
  expect(await screen.findByRole("button", { name: "Thử lại" })).toBeInTheDocument();
  expect(screen.queryByText("Glucose máu")).not.toBeInTheDocument();
});
```

- [ ] **Step 2: Run renderer test and confirm RED**

Run: `pnpm --filter health-exam test -- dynamic-registration-form.test.tsx`

Expected: FAIL because dynamic renderer components do not exist.

- [ ] **Step 3: Implement section/tree rendering and mount it in Step 2**

`RegistrationFormSections` watches only `variantCode`. It shows translated no-selection guidance, two skeleton fieldsets while loading, a retriable accessible error state, or the two server-defined sections. `RegistrationFormTree` sorts by `Order`, represents parent/child indentation from `ParentNodeID`/`Level`, marks required labels, and renders disabled/read-only visual primitives for HIS `ControlType` (`TXT`, `CMB`, `CHK`, and an unknown fallback). It never uses `Controller`, `register`, or `setValue`.

Replace `MedicalHistoryBar` and `AdditionalInfoSection` in `StepExamination` with this renderer in the same location. Remove legacy imports and delete legacy Step 2 code only after coverage moves. Add strings for loading, error, retry, no group, and read-only display.

- [ ] **Step 4: Run renderer tests and typecheck**

Run: `pnpm --filter health-exam test -- dynamic-registration-form.test.tsx && pnpm --filter health-exam typecheck`

Expected: PASS; changing a group cannot show a prior tree and read-only nodes do not dirty form state.

- [ ] **Step 5: Commit**

```bash
git add apps/health-exam/src/features/registration apps/health-exam/test/features/registration packages/i18n/src/locales/vi.json
git commit -m "feat: render Step 2 sections from HIS"
```

### Task 5: Verify end-to-end contracts and document configuration

**Files:**
- Modify: `README.md`
- Modify only if proven necessary: `docs/superpowers/specs/2026-09-09-dynamic-step2-his-form-design.md`

- [ ] **Step 1: Document migration verification**

Add this operator query and expected results:

```sql
SELECT m."DivisionID", m."VariantCode", m."TemplateCode", s."SectionKind", s."ItemGroupID"
FROM "HEX_ExamGroupFormMapping" m
JOIN "HEX_ExamGroupFormSectionMapping" s ON s."MappingID" = m."MappingID"
WHERE m."VariantCode" = 'DTK_03'
ORDER BY s."SectionKind";
```

Expected: `KSK-TREN18TUOI` has `EXTRA_INFO/63` and `HISTORY/64`. Document authenticated curl calls for `/v1/exam-groups/available` and `/v1/exam-groups/DTK_03/registration-form` including `X-Division-Id`.

- [ ] **Step 2: Run focused backend verification**

Run: `dotnet test HealthExam.Tests --filter "FullyQualifiedName~ExamGroupFormMappingPersistenceTests|FullyQualifiedName~ExamGroupRegistrationFormServiceTests|FullyQualifiedName~ExamGroupEndpointTests|FullyQualifiedName~HisExamFormGateway"`

Expected: PASS.

- [ ] **Step 3: Run frontend verification**

Run: `pnpm --filter @medviet/api test -- health-exam-registration-form-service.test.ts && pnpm --filter health-exam test -- dynamic-registration-form.test.tsx && pnpm --filter health-exam typecheck`

Expected: PASS.

- [ ] **Step 4: Manually exercise the UI in a non-production environment**

1. Open a new registration and visit Step 2.
2. Confirm only “Người từ đủ 18 tuổi trở lên” is selectable.
3. Select it and confirm two disabled HIS-derived sections appear in M03 order.
4. Make HIS unavailable only in a non-production environment; confirm error/retry and no legacy fields.

- [ ] **Step 5: Commit documentation and inspect final diff**

```bash
git add README.md docs/superpowers/specs/2026-09-09-dynamic-step2-his-form-design.md
git commit -m "docs: document Step 2 HIS form configuration"
git diff --check HEAD~1..HEAD
git status --short
```

Expected: no whitespace errors and no unrelated files included.

## Plan self-review

- **Spec coverage:** Tasks 1–2 implement mapping, seed, APIs, tenant cache and section validation; Task 3 moves the select to mapped groups; Task 4 replaces both hard-coded Step 2 blocks with a read-only dynamic renderer and all required states; Task 5 verifies and documents deployment.
- **Placeholder scan:** No unresolved implementation placeholder is present. EF creates the migration timestamp according to repository convention.
- **Type consistency:** backend `AvailableExamGroupItem`/`RegistrationFormDefinition` are the frontend wire types; Task 3 establishes the hooks consumed in Task 4.
