# Clinical HIS Form Value Persistence Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Persist clinical exam results entered in MH5 (Khám lâm sàng) to database and HIS EMR via backend API (`PUT /v1/exam-records/{recordId}/his-form/sections/{itemGroupId}`), and load saved values in `GET /v1/exam-records/{recordId}/his-form/sections/{itemGroupId}` to enable cross-category visibility.

**Architecture:** 
- Backend (`health-exam-server`): Expose `PUT /v1/exam-records/{recordId}/his-form/sections/{itemGroupId}` with read-merge-write semantics to HIS EMR (`M03F10010/CUEMR`), preserving all other sections and persisting `HisEmrDataID` on `HEX_ExamRecord`. Update `GET` to hydrate layout nodes with saved `Value`/`Text` from HIS EMR.
- Frontend (`turbo-web`): Add `saveRecordHisFormSection` in `HealthExamHisFormService`, add `useSaveExamRecordHisFormSectionMutation`, and wire `CategoryPanel` / `ClinicalTab` to submit directly to Backend instead of saving to `localStorage`.

**Tech Stack:** 
- Backend: .NET 8, ASP.NET Core Web API, Entity Framework Core, PostgreSQL, xUnit, Moq, Newtonsoft.Json.
- Frontend: Next.js, React 19, TypeScript, TanStack Query (React Query v5), Vitest, Tailwind CSS.

## Global Constraints

- No breaking changes to existing Step 2 registration-form or M02 signing endpoints.
- Read-Merge-Write must preserve untouched sections when updating an `itemGroupId`.
- Division isolation must be enforced on all queries (`record.DivisionID == divisionId`).
- `node.Value` and `node.Text` in the layout tree must reflect the saved HIS EMR data.

---

### Task 1: Backend DTOs and Service Interface

**Files:**
- Modify: `Backend/health-exam-server/HealthExam.Server/Models/HisFormModels.cs`
- Modify: `Backend/health-exam-server/HealthExam.Server/Service/IExamFormService.cs`

**Interfaces:**
- Produces: `FormSectionSaveRequest`, `FormSectionFieldValue`, `IExamFormService.SaveSectionAsync(Guid recordId, int itemGroupId, FormSectionSaveRequest request, CancellationToken ct)`

- [ ] **Step 1: Write test for contract/interface definitions**
  Add unit test in `Backend/health-exam-server/HealthExam.Tests/ExamFormContractTests.cs` checking `SaveSectionAsync` exists on `IExamFormService`.

- [ ] **Step 2: Add request models in `HisFormModels.cs`**
  ```csharp
  public record FormSectionFieldValue(Guid ItemId, string? Value, string? Text);
  public record FormSectionSaveRequest(List<FormSectionFieldValue>? Fields, bool IsDraft = true);
  ```

- [ ] **Step 3: Add `SaveSectionAsync` to `IExamFormService`**
  ```csharp
  Task<ExamFormSection> SaveSectionAsync(Guid recordId, int itemGroupId, FormSectionSaveRequest request, CancellationToken ct = default);
  ```

- [ ] **Step 4: Verify test passes and commit**
  Run `dotnet test --filter FullyQualifiedName~ExamFormContractTests` in `Backend/health-exam-server`.
  Commit: `feat(his-form): declare FormSectionSaveRequest and SaveSectionAsync interface`.

---

### Task 2: Backend HisExamFormGateway Save and Hydrate Logic

**Files:**
- Modify: `Backend/health-exam-server/HealthExam.Server/Service/HisExamFormGateway.cs`
- Test: `Backend/health-exam-server/HealthExam.Tests/HisExamFormGatewayRecordTests.cs`

**Interfaces:**
- Consumes: `IHisAdmissionService.EnsureAdmissionAsync`, `IHisEmrApi.ReadFormDataAsync`, `IHisEmrApi.SaveFormDataAsync`
- Produces: `HisExamFormGateway.SaveSectionAsync`, updated `HisExamFormGateway.GetSectionAsync`

- [ ] **Step 1: Write failing unit test in `HisExamFormGatewayRecordTests.cs`**
  - Test `SaveSectionAsync` merges fields and calls `SaveFormDataAsync`.
  - Test `GetSectionAsync` populates `Value` and `Text` on nodes when EMR data exists.

- [ ] **Step 2: Implement `GetSectionAsync` hydration**
  In `HisExamFormGateway.GetSectionAsync`:
  If `record.AdmissionID` has a positive value or `record.HisEmrDataID` exists:
  - Call `_his.ReadFormDataAsync(new HisEmrDataReadWireRequest { EMRDataID = record.HisEmrDataID ?? Guid.Empty, TemplateID = definition.TemplateId, AdmissionID = record.AdmissionID ?? 0, IsInherit = false }, ct)`.
  - Parse `Details` into a lookup `existingValues[itemId] = (Value, Text)`.
  - For each node in `sectionLayout`, if node has a matching `ItemID` in `existingValues`, update `node["Value"] = value` and `node["Text"] = text`.

- [ ] **Step 3: Implement `SaveSectionAsync`**
  In `HisExamFormGateway.SaveSectionAsync`:
  - Validate record exists and user has access (`RequireRecordAsync`).
  - Ensure admission (`_hisAdmissions.EnsureAdmissionAsync(recordId, ct)`).
  - Validate `itemGroupId` is in `definition.Details`.
  - Read existing details via `_his.ReadFormDataAsync` to get existing details dictionary.
  - For each field in `request.Fields`, update or add to details dictionary.
  - Send `_his.SaveFormDataAsync` with full details.
  - Update `record.HisEmrDataID = savedEmrDataId`, `record.HisFormTemplateID = definition.TemplateId`, `record.HisFormSyncStatus = "Synced"`.
  - Save changes to DB.
  - Return updated `ExamFormSection` by calling `GetSectionAsync(recordId, itemGroupId, ct)`.

- [ ] **Step 4: Run tests to verify**
  Run `dotnet test --filter FullyQualifiedName~HisExamFormGatewayRecordTests` in `Backend/health-exam-server`.
  Commit: `feat(his-form): implement SaveSectionAsync and GetSectionAsync hydration in HisExamFormGateway`.

---

### Task 3: Backend Controller Endpoint

**Files:**
- Modify: `Backend/health-exam-server/HealthExam.API/Controllers/ExamRecordHisFormController.cs`
- Test: `Backend/health-exam-server/HealthExam.Tests/HisFormEndpointTests.cs`

**Interfaces:**
- Produces: `PUT /v1/exam-records/{recordId}/his-form/sections/{itemGroupId}`

- [ ] **Step 1: Write controller endpoint test in `HisFormEndpointTests.cs`**
  Add test verifying `PUT /v1/exam-records/{recordId}/his-form/sections/{itemGroupId}` invokes `SaveSectionAsync` and returns HTTP 200 with `ExamFormSection`.

- [ ] **Step 2: Add `SaveSection` action in `ExamRecordHisFormController`**
  ```csharp
  [HttpPut("sections/{itemGroupId:int}")]
  [SwaggerOperation(Summary = "Lưu giá trị của một phân đoạn biểu mẫu khám chuyên khoa KSK")]
  public async Task<ActionResult<ResultData<ExamFormSection>>> SaveSection(
      [FromRoute] Guid recordId,
      [FromRoute] int itemGroupId,
      [FromBody] FormSectionSaveRequest? request,
      CancellationToken ct = default)
      => Success(await _service.SaveSectionAsync(recordId, itemGroupId, request ?? new(null), ct));
  ```

- [ ] **Step 3: Run backend test suite**
  Run `dotnet test` in `Backend/health-exam-server` to confirm all tests pass.
  Commit: `feat(his-form): add PUT endpoint for saving clinical his-form sections`.

---

### Task 4: Frontend API Types and Client Service

**Files:**
- Modify: `Frontend/turbo-web/packages/types/src/health-exam/his-form.ts`
- Modify: `Frontend/turbo-web/packages/api/src/health-exam/health-exam-his-form-service.ts`
- Modify: `Frontend/turbo-web/apps/health-exam/src/hooks/api/health-exam-his-form.ts`

**Interfaces:**
- Produces: `HisSectionSaveRequest`, `HealthExamHisFormService.saveRecordHisFormSection`, `useSaveExamRecordHisFormSectionMutation`

- [ ] **Step 1: Add types in `packages/types/src/health-exam/his-form.ts`**
  ```typescript
  export interface HisSectionFieldValue {
    itemId: string;
    value?: string;
    text?: string;
  }
  export interface HisSectionSaveRequest {
    fields: HisSectionFieldValue[];
    isDraft?: boolean;
  }
  ```

- [ ] **Step 2: Add `saveRecordHisFormSection` to `health-exam-his-form-service.ts`**
  ```typescript
  saveRecordHisFormSection = (
    recordId: string,
    itemGroupId: number,
    request: HisSectionSaveRequest,
  ): Promise<ExamFormSectionResponse> =>
    this.client.put<ExamFormSectionResponse>(
      `/v1/exam-records/${encodeURIComponent(recordId)}/his-form/sections/${encodeURIComponent(itemGroupId)}`,
      request,
    );
  ```

- [ ] **Step 3: Add mutation hook in `apps/health-exam/src/hooks/api/health-exam-his-form.ts`**
  ```typescript
  export function useSaveExamRecordHisFormSectionMutation(
    recordId: string,
    itemGroupId: number,
  ) { ... }
  ```

- [ ] **Step 4: Verify typecheck**
  Run `bun run --filter @medviet/health-exam typecheck`.
  Commit: `feat(results): add types and mutation hook for saving his-form sections`.

---

### Task 5: Frontend Component Integration & State Synchronization

**Files:**
- Modify: `Frontend/turbo-web/apps/health-exam/src/features/results/components/clinical-tab/category-panel.tsx`
- Modify: `Frontend/turbo-web/apps/health-exam/src/features/results/components/clinical-tab/clinical-tab.tsx`
- Test: `Frontend/turbo-web/apps/health-exam/test/features/results/components/clinical-tab/category-panel.test.tsx`

- [ ] **Step 1: Wire `handleSave` in `category-panel.tsx` or `clinical-tab.tsx`**
  - When saving a category with `hisLayout` / HIS item group:
    - Map `values: Record<string, string>` into `HisSectionFieldValue[]` by matching each `nodeId` in `hisLayout` to its `node.ItemID` or `node.NodeID`.
    - Call `saveRecordHisFormSectionMutation.mutate(...)`.
    - Invalidate `examRecordHisFormSection` query key.
    - Synchronize category display status to `"saved"` / `"examining"`.

- [ ] **Step 2: Run vitest and typecheck**
  - Run `bun x vitest run test/features/results/components/clinical-tab/category-panel.test.tsx`.
  - Run `bun run --filter @medviet/health-exam typecheck`.

- [ ] **Step 3: Commit and verify end-to-end**
  Commit: `feat(results): wire clinical category save to backend his-form section API`.
