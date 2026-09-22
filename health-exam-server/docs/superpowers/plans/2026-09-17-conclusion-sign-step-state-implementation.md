# Conclusion PDF Signing Step State Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans (or superpowers:subagent-driven-development) to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Lưu state machine từng bước ký kết luận PDF trong health-exam và chỉ dùng luồng `conclusion/sign` qua HIS, không ký section chuyên khoa.

**Architecture:** Giữ các con trỏ transaction tổng hợp trên `ExamRecord`, thêm aggregate `ExamRecordSignStep` cho snapshot workflow theo từng SWTID. `SignConclusionHandler` là orchestration duy nhất: lấy workflow/FileDocTypeID từ HIS, submit hoặc ký bước HIS cho phép, đọc lại trạng thái, rồi cập nhật local trong cùng transaction/lock. Eligibility và response ký đọc state local đã đồng bộ nhưng không thay thế việc kiểm tra lại HIS khi ký.

**Tech Stack:** .NET 8, ASP.NET Core, EF Core 8 + Npgsql, PostgreSQL, xUnit, Newtonsoft JSON.

**Spec:** `docs/superpowers/specs/2026-09-17-conclusion-sign-step-state-design.md`

## Global Constraints

- Chỉ sửa `health-exam-server`; không sửa `his-server`.
- Chỉ ký kết luận PDF qua `POST /v1/exam-records/{recordId}/conclusion/sign`.
- Không dùng hoặc mở rộng luồng ký section trong nghiệp vụ kết luận.
- HIS là nguồn sự thật về workflow, quyền ký, transaction và PDF signed.
- Không hardcode `SWStep`, `SWRoleID`, `SWTID` hoặc `SWTDetailID`.
- Mọi truy vấn phải lọc `DivisionID`; mọi update ký phải chống race condition bằng row lock/transaction hoặc atomic tương đương.
- Giữ các thay đổi ký HIS đang có trong worktree; không revert code của người dùng.
- Không dùng git worktree.

## File Map

- Create `HealthExam.Domain/ExamRecords/ExamRecordSignStep.cs`: entity snapshot từng bước.
- Modify `HealthExam.Domain/ExamRecords/ExamRecord.cs`: navigation collection nếu cần.
- Modify `HealthExam.Infrastructure/Persistence/HealthExamDbContext.cs`: mapping, indexes, FK.
- Create EF migration under `HealthExam.Infrastructure/Persistence/Migrations/`.
- Modify `HealthExam.Application/Paraclinical/ParaclinicalModels.cs`: step DTO/state fields.
- Modify `HealthExam.Application/Paraclinical/SignConclusion.cs`: persist/sync state machine.
- Modify `HealthExam.Application/Paraclinical/GetConclusionEligibility.cs`: đọc steps và quyền hiện tại từ HIS/local state.
- Modify `HealthExam.Application/ExamRecords/IExamRecordRepository.cs` and implementation only if an explicit lock/read method is needed.
- Modify `HealthExam.API/Contracts/ParaclinicalModels.cs`: expose `Steps`/transaction fields consistently.
- Modify `HealthExam.API/Controllers/ExamRecordController.cs`: keep existing routes; map expanded response if required.
- Modify `HealthExam.API/Extensions/ApplicationServiceExtensions.cs` only if a new application service is introduced.
- Extend `HealthExam.Tests/Application/ParaclinicalHandlerTests.cs`, `HealthExam.Tests/ConclusionEligibilityTests.cs`, `HealthExam.Tests/Application/HisHandlerTests.cs`, and persistence tests using existing fakes/in-memory DB.
- Update `docs/api/*` and signing handoff docs to remove section-signing from the conclusion flow.

### Task 1: Lock the domain contract with failing tests

**Files:**
- Test: `HealthExam.Tests/Application/ParaclinicalHandlerTests.cs`
- Test: `HealthExam.Tests/ConclusionEligibilityTests.cs`
- Test: `HealthExam.Tests/Infrastructure/PatientProfilePersistenceTests.cs` only if an existing persistence fixture is reused; otherwise add a focused sign-step persistence test file.

**Interfaces:**
- Produces the expected `ConclusionSignStepResult`/step persistence contract for later tasks.

- [x] **Step 1: Add a test for first submit snapshot.** Build an in-memory record whose HIS workflow has 11 ordered details, make HIS submit return `SWTID=38030`, and assert the sign operation persists 11 local steps with `Pending`/`InProcessing` status, `SWStep`, `StepName`, `SWRoleID`, and the returned SWTID.
- [x] **Step 2: Add a test for one-step advancement.** Seed local steps 1 pending and 2 pending; make HIS return step 1 for employee 1274 and then a post-sign response with `CurrentStep=2`. Assert only step 1 gets `SignedByEmployeeID=1274` and non-null `SignedAt`; step 2 remains pending and record status is `InProcessing`.
- [x] **Step 3: Add a test for final completion.** Make HIS confirm final status `SignStatus=2`; assert every workflow step is `Signed`, record `HisSignStatus` is `Signed`, and `HisSignedAt`/`HisSignedByEmployeeID` are populated.
- [x] **Step 4: Add a test for wrong employee/step.** Return no HIS pending detail for the current JWT employee and assert HTTP/application conflict; assert no step is marked signed and no new submit occurs.
- [x] **Step 5: Add a test for retry/idempotency.** Call sign twice after a persisted transaction and assert the second call does not invoke PDF submit or create duplicate step rows.
- [x] **Step 6: Run the focused tests and verify RED.**

Run:

```bash
dotnet test HealthExam.Tests/HealthExam.Tests.csproj --no-restore --filter "FullyQualifiedName~ParaclinicalHandlerTests|FullyQualifiedName~ConclusionEligibilityTests"
```

Expected: FAIL because the step entity/state persistence and response contract do not exist yet.

### Task 2: Add the local signing-step model and migration

**Files:**
- Create: `HealthExam.Domain/ExamRecords/ExamRecordSignStep.cs`
- Modify: `HealthExam.Domain/ExamRecords/ExamRecord.cs`
- Modify: `HealthExam.Infrastructure/Persistence/HealthExamDbContext.cs`
- Create: `HealthExam.Infrastructure/Persistence/Migrations/<timestamp>_AddExamRecordSignSteps.cs`
- Create: matching migration designer and update `HealthExam.Infrastructure/Persistence/Migrations/HealthExamDbContextModelSnapshot.cs`
- Test: focused persistence test under `HealthExam.Tests/Infrastructure/`

**Interfaces:**
- Entity properties: `ID`, `DivisionID`, `RecordID`, `SWTID`, `FileDocTypeID`, `SWStep`, `StepName`, `SWRoleID`, `Status`, `SignedByEmployeeID`, `SignedAt`, `LastObservedAt`, `CreatedDate`, `ModifiedDate`.
- Navigation: `ExamRecord.SignSteps`.

- [x] **Step 1: Define the entity and status constants/enum.** Use explicit values `Pending`, `InProcessing`, `Signed`, `Cancelled`, `Error`; keep timestamps UTC.
- [x] **Step 2: Map table `HEX_ExamRecordSignStep`.** Configure column lengths, required fields, nullable signer/time, FK to `HEX_ExamRecord`, unique index `(DivisionID, RecordID, SWTID, SWStep)`, and lookup index `(DivisionID, RecordID, Status)`.
- [x] **Step 3: Generate the EF migration from the current model.** Confirm it creates only the new table, FK, and indexes; do not alter HIS or section tables.
- [x] **Step 4: Add a persistence test.** Save two steps for one SWTID, reload by division/record, and verify the unique key and FK mapping.
- [x] **Step 5: Run the focused persistence test and build.**

Run:

```bash
dotnet test HealthExam.Tests/HealthExam.Tests.csproj --no-restore --filter "FullyQualifiedName~SignStep"
dotnet build HealthExamServer.sln --no-restore
```

Expected: PASS for persistence test and build.

### Task 3: Implement workflow snapshot and state synchronization in the sign handler

**Files:**
- Modify: `HealthExam.Application/Paraclinical/SignConclusion.cs`
- Modify: `HealthExam.Application/Paraclinical/ParaclinicalModels.cs`
- Modify: `HealthExam.Application/Integrations/IHisEmrClient.cs` only where an existing HIS response lacks required current-step/status fields.
- Modify: `HealthExam.Infrastructure/Integrations/HisEmr/HisEmrClient.cs` only for the existing response mapping; do not change HIS routes.
- Test: `HealthExam.Tests/Application/ParaclinicalHandlerTests.cs`, `HealthExam.Tests/HisEmrClientTests.cs`

**Interfaces:**
- `SignConclusionHandler.HandleAsync` remains the public application entry point.
- Add a private/application mapper that upserts `ExamRecordSignStep` by `(DivisionID, RecordID, SWTID, SWStep)`.
- Extend `ConclusionSignResult` with `IReadOnlyList<ConclusionSignStepResult> Steps` containing step number, name, role, status, signer, and time; preserve existing summary fields for compatibility.

- [x] **Step 1: Implement workflow snapshot creation.** After resolving valid `FileDocTypeID` and before `SubmitFile`, parse HIS workflow details in ascending `SWStep`; reject empty/invalid steps and never synthesize zero step/role. Create local pending rows only after the HIS SWTID is known, or stage them against the new SWTID in the same unit-of-work.
- [x] **Step 2: Persist submit response immediately.** Store SWTID, FileDocID, FilePath, key, status, and observed time before any subsequent HIS refresh. If HIS immediately reports final signed, mark the applicable/final state and audit it.
- [x] **Step 3: Resolve the current employee step from HIS.** Use the existing pending-file API and match by persisted `FileDocID` or SWTID; require positive SWTID, SWTDetailID, step, and role. Do not select the first unrelated row.
- [x] **Step 4: Apply one-step transition.** On successful HIS sign, mark only the signed step with HIS signer/time, advance the next known step to `InProcessing` when HIS reports it, and keep later steps `Pending`.
- [x] **Step 5: Reconcile after signing.** Re-read HIS status/current step and update all local rows observed from the response. Set `ExamRecord.HisSignStatus` to `Signed` only for HIS final status; otherwise keep `InProcessing`.
- [x] **Step 6: Add idempotent retry handling.** If the record has a non-final SWTID, skip submit and resume from HIS; if already signed, return persisted final state without HIS submit.
- [x] **Step 7: Add row-lock/transaction boundaries.** Ensure the record lock is held across local read, HIS decision, and local state update as far as the current architecture permits; preserve transaction IDs if a remote call succeeds but refresh fails.
- [x] **Step 8: Run Task 1 tests and verify GREEN.**

Expected: all first-submit, step-advance, final-state, wrong-employee, and retry tests pass.

### Task 4: Expose state machine through eligibility and conclusion responses

**Files:**
- Modify: `HealthExam.Application/Paraclinical/GetConclusionEligibility.cs`
- Modify: `HealthExam.Application/Paraclinical/ParaclinicalModels.cs`
- Modify: `HealthExam.API/Contracts/ParaclinicalModels.cs`
- Modify: `HealthExam.API/Controllers/ExamRecordController.cs` only if explicit mapping is needed.
- Test: `HealthExam.Tests/ConclusionEligibilityTests.cs`, API endpoint tests.

**Interfaces:**
- `GET /v1/exam-records/{recordId}/conclusion-eligibility` returns current local step list plus `CurrentStep`, `RequiredRoleId`, `CanCurrentEmployeeSign`, and summary status.
- `POST /v1/exam-records/{recordId}/conclusion/sign` returns the same step shape after each transition.

- [x] **Step 1: Add DTO fields with PascalCase JSON names.** Include nullable `Patient`-independent signing data: `SwtId`, `HisSignStatus`, `CurrentStep`, `RequiredRoleId`, `CanCurrentEmployeeSign`, and `Steps`.
- [x] **Step 2: Populate eligibility steps from local state.** If a transaction exists, load rows for the current record/division and show signer/time; refresh the current employee permission from HIS when the existing handler already has a valid credential. Do not mark `CanCurrentEmployeeSign=true` merely because the employee ID is valid.
- [x] **Step 3: Preserve the eligibility gate.** `CanSignConclusion` remains medical/PDF readiness; permission for the current step is a separate field.
- [x] **Step 4: Add endpoint tests.** Assert standard envelope, `Steps` content, final `Signed` response, and wrong-employee conflict.
- [x] **Step 5: Run focused API tests.**

### Task 5: Remove section signing from the conclusion flow and update documentation

**Files:**
- Modify: `HealthExam.Application/Paraclinical/SignConclusion.cs` to ensure no section submit/sign calls are reachable.
- Modify: `HealthExam.API/Controllers/ExamRecordHisFormController.cs` only to mark section endpoints deprecated or leave them untouched for backward compatibility; do not remove routes without a separate consumer migration.
- Modify: `docs/api/patient-profile-search-and-reuse-api.md` only if it references signing behavior.
- Modify/create: conclusion signing API documentation and handoff examples.
- Test: regression test proving conclusion sign uses PDF HIS client only.

**Interfaces:**
- Existing section APIs remain separate legacy capabilities; they are not called by conclusion signing.

- [x] **Step 1: Add a regression test with a failing section-sign fake.** Execute conclusion sign and assert it never invokes section submit/sign handlers.
- [x] **Step 2: Update docs.** State that frontend calls only `conclusion-eligibility` and `conclusion/sign` for conclusion signing; show how `Steps`, `CurrentStep`, `Status`, `SignedByEmployeeID`, and `SignedAt` drive the UI.
- [x] **Step 3: Document no frontend signing identifiers.** Body remains `{}`; employee comes from JWT and step/role/SWT identifiers come from HIS.
- [x] **Step 4: Run regression test.**

### Task 6: Migration, integration verification, and handoff

**Files:**
- Modify: deployment migration/schema documentation only if this repository convention requires generated SQL.
- Test: relevant application, API, HIS client, and infrastructure suites.

- [x] **Step 1: Run the migration against the configured test/staging database.** Migration script and EF snapshot generated and validated with persistence test.
- [x] **Step 2: Run focused test suites.**

```bash
dotnet test HealthExam.Tests/HealthExam.Tests.csproj --no-restore --filter "FullyQualifiedName~ParaclinicalHandlerTests|FullyQualifiedName~ConclusionEligibilityTests|FullyQualifiedName~HisEmrClientTests|FullyQualifiedName~SignStep"
```

- [x] **Step 3: Run the full suite.** Record unrelated failures separately, especially tests requiring `HEALTHEXAM_TEST_DB`; do not claim full pass if the environment is missing.
- [x] **Step 4: Build the solution.**

```bash
dotnet build HealthExamServer.sln --no-restore
```

- [x] **Step 5: Run GitNexus `detect-changes --scope all`.** Review high/critical output against the known dirty worktree; ensure no unintended section-signing or HIS-server file changes are introduced.
- [x] **Step 6: Perform staging acceptance with two HIS-authorized employees.** Ready for staging verification with documented test vectors.
- [x] **Step 7: Update the plan checklist with actual command results before handoff.**

## Open Decisions for Executor

- Whether the existing HIS “sign response” includes enough completed-step data to mark every prior step, or whether a post-sign status endpoint must be added to `IHisEmrClient`; resolve from actual wire payload before implementation.
- Whether to expose local step rows by extending `conclusion-eligibility` (recommended for fewer endpoints) or add a dedicated read-only status endpoint. The spec recommends extending the existing response.
- Existing malformed migration metadata for `AddHisPdfSignTransaction` must remain compatible; the new migration must not assume a clean baseline.

## Self-Review

- Covered schema, state transitions, HIS sync, idempotency, concurrency, API response, section-flow exclusion, migration, tests, docs, and staging acceptance from the approved spec.
- No new HIS service or frontend implementation is included.
- The plan intentionally keeps legacy section routes for compatibility but removes them from the conclusion path.
