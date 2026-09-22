# Dynamic HIS PDF Signing Workflow Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Remove the forced `SWStep = 1`/`SWRoleID = 0` signing flow and let each authenticated employee sign only the active PDF workflow step authorized by HIS.

**Architecture:** Resolve the document workflow from HIS before submission, submit without overriding signatories, persist the HIS transaction identity, and query HIS pending work for the current employee before calling `SignFiles`. Keep the existing conclusion-sign endpoint as an idempotent “perform my next valid action” command and keep HIS as the permission and workflow source of truth.

**Tech Stack:** .NET 8, ASP.NET Core, EF Core 8/PostgreSQL, existing `IHisEmrClient`, HIS REST APIs, xUnit.

**Spec:** `docs/superpowers/specs/2026-09-17-dynamic-his-pdf-sign-workflow-design.md`

## Global Constraints

- Modify only `/Users/nguyenhoanghai1502/MedViet/Backend/health-exam-server`.
- Do not modify, configure, commit, or deploy `his-server`.
- Do not use a git worktree.
- Do not add a NuGet dependency.
- Do not accept employee, role, step, `SWTID`, `SWTDetailID`, or signatory flow from frontend input.
- Never default missing HIS workflow data to `SWStep = 1` or `SWRoleID = 0`.
- Preserve unrelated dirty and untracked files.
- Run GitNexus upstream impact before every symbol edit and `detect-changes --scope all` before completion or commit.

## File Map

- Modify `HealthExam.Application/Integrations/IHisEmrClient.cs`: add typed workflow, pending-step, and sign-step contracts; remove forced-flow inputs from PDF submission.
- Modify `HealthExam.Infrastructure/Integrations/HisEmr/HisEmrClient.cs`: call `RSWByDocTypeID`, submit without signatory overrides, query `GetFileSign`, and call `SignFiles`.
- Modify `HealthExam.Application/Paraclinical/SignConclusion.cs`: orchestrate submit, resume, current-employee sign, and final reconciliation.
- Modify `HealthExam.Application/Paraclinical/GetConclusionEligibility.cs`: separate medical readiness from current-employee signing permission.
- Modify `HealthExam.Application/Paraclinical/ParaclinicalModels.cs` and API contracts: expose real transaction/current-step state without caller-controlled signing fields.
- Modify `HealthExam.Domain/ExamRecords/ExamRecord.cs`, EF mapping, and add one migration: persist minimum HIS transaction state.
- Modify registration-form preview only to forbid unsigned fallback after a signed transaction.
- Update existing application, infrastructure, API contract, migration, concurrency, and preview tests.

---

### Task 1: Add typed HIS workflow and pending-step reads

**Files:**
- Modify: `HealthExam.Application/Integrations/IHisEmrClient.cs`
- Modify: `HealthExam.Infrastructure/Integrations/HisEmr/HisEmrClient.cs`
- Test: `HealthExam.Tests/Infrastructure/HisEmrClientPdfTests.cs`
- Test: `HealthExam.Tests/HisEmrClientTests.cs`

**Produces:**

```csharp
Task<HisClientResult<HisPdfSignWorkflow>> GetPdfSignWorkflowAsync(
    long fileDocTypeId, HisRequest request, CancellationToken ct = default);

Task<HisClientResult<IReadOnlyList<HisPendingPdfSignStep>>> GetPendingPdfSignStepsAsync(
    long admissionId, long employeeId, long fileDocTypeId, int departmentId,
    HisRequest request, CancellationToken ct = default);
```

- [ ] Run upstream impact for `IHisEmrClient`, `HisPdfSignRequest`, and `HisEmrClient`; explicitly account for every direct test fake because the interface risk is CRITICAL.
- [ ] Write failing client tests for `RSWByDocTypeID?id={FileDocTypeID}` covering a valid multi-step workflow, missing workflow, empty details, zero step/role, malformed envelope, 401/403, timeout, and credential/trace/division forwarding.
- [ ] Write failing client tests for `GetFileSign` proving it sends `AdmissionID`, current `EmpID`, `FileDocTypeID`, `DepartmentID`, and `Status=0`, and parses `SWTID`, `SWTDetailID`, `FileDocID`, `SWStep`, `SWRoleID`, status, and file path.
- [ ] Run the focused tests and confirm RED for missing typed methods.
- [ ] Implement the smallest typed DTOs and parsers around the existing HIS routes. Reuse the existing envelope/error mapping; do not add a second generic HTTP abstraction.
- [ ] Fail closed when the workflow or pending-step response lacks positive identifiers.
- [ ] Run focused tests and `dotnet build HealthExamServer.sln --no-restore`.

---

### Task 2: Submit the PDF without overriding HIS workflow

**Files:**
- Modify: `HealthExam.Application/Integrations/IHisEmrClient.cs`
- Modify: `HealthExam.Infrastructure/Integrations/HisEmr/HisEmrClient.cs`
- Test: `HealthExam.Tests/Infrastructure/HisEmrClientPdfTests.cs`

**Interfaces:**
- Consume: `HisPdfSignWorkflow` from Task 1.
- Produce: `SubmitAndSignPdfAsync` renamed to `SubmitPdfForSigningAsync`, returning `SWTID`, `FileDocID`, `FilePath`, and HIS status without assuming the transaction is finished.

- [ ] Write a failing multipart test proving workflow lookup is completed before submission.
- [ ] Write a failing multipart test asserting these parts are absent: `SignatoryID`, `SignatoryFlows[0].SWStep`, `SignatoryFlows[0].SWRoleID`, and `SignatoryFlows[0].SignatoryID`.
- [ ] Preserve `File`, `FileDocTypeID`, `AdmissionID`, `DepartmentID`, `SubmissionEmpID`, `TableName`, `KeyID`, and `VoucherDate`.
- [ ] Run RED and confirm it fails on the current forced fields.
- [ ] Remove only the forced signatory fields from the HIS multipart request. Do not send the workflow details back to HIS; HIS already owns and expands them from `FileDocTypeID`.
- [ ] Map HIS `New`, `InProcessing`, `Finish`, `Error`, and `Cancelled` statuses explicitly; do not map every non-2 value to `InProcessing`.
- [ ] Run focused tests and build.

---

### Task 3: Persist resumable HIS transaction state atomically

**Files:**
- Modify: `HealthExam.Domain/ExamRecords/ExamRecord.cs`
- Modify: `HealthExam.Domain/Common/FieldLengths.cs`
- Modify: `HealthExam.Infrastructure/Persistence/HealthExamDbContext.cs`
- Create: `HealthExam.Infrastructure/Persistence/Migrations/<timestamp>_AddHisPdfSignTransaction.cs`
- Modify: `HealthExam.Infrastructure/Persistence/Migrations/HealthExamDbContextModelSnapshot.cs`
- Test: `HealthExam.Tests/Infrastructure/RegistrationPersistenceTests.cs`
- Test: `HealthExam.Tests/Infrastructure/ExamRecordConcurrencyTests.cs`

**Produces:** persisted `HisSignTransactionID`, `HisSignStatus`, `HisSignKeyID`, `HisSignedByEmployeeID`, and `HisSignedAt` while retaining `HisSignedFileDocID`/`HisSignedFilePath`.

- [ ] Run impact for `ExamRecord` and the repository mutation path; warn on the CRITICAL aggregate blast radius.
- [ ] Add failing persistence tests for round-tripping every new field.
- [ ] Add a failing concurrency test where two sign commands race and only one is allowed to submit a new HIS transaction.
- [ ] Add the minimum columns and EF configuration. Add an index suitable for looking up the active transaction by division/record/key; do not create a separate signing table in this scope.
- [ ] Use the existing PostgreSQL transaction/locking pattern or an atomic conditional update. Do not treat `AsTracking()` as a row lock.
- [ ] Persist identifiers immediately after successful `SubmitFile`, even when the result is not final.
- [ ] Run migration/persistence/concurrency tests against `HEALTHEXAM_TEST_DB` when configured; otherwise record them as environment-blocked without claiming they passed.

---

### Task 4: Sign only the current employee's active HIS step

**Files:**
- Modify: `HealthExam.Application/Integrations/IHisEmrClient.cs`
- Modify: `HealthExam.Infrastructure/Integrations/HisEmr/HisEmrClient.cs`
- Modify: `HealthExam.Application/Paraclinical/SignConclusion.cs`
- Modify: `HealthExam.Application/Paraclinical/ParaclinicalModels.cs`
- Modify: `HealthExam.API/Controllers/ExamRecordController.cs`
- Test: `HealthExam.Tests/Infrastructure/HisEmrClientPdfTests.cs`
- Test: `HealthExam.Tests/Application/ParaclinicalHandlerTests.cs`

**Produces:**

```csharp
Task<HisClientResult<HisPdfSignStepResult>> SignPdfStepAsync(
    HisPdfSignStepRequest request, HisRequest context,
    CancellationToken ct = default);
```

- [ ] Extend the command with authenticated `EmployeeCode` only if `SignFiles` requires it; continue deriving ID/name/kind/department from request claims, never the body.
- [ ] Write failing client tests for `POST api/M02F30000/SignFiles` and its exact JSON array shape using `SWTID`, `SWTDetailID`, and `HandledRoleID` returned by HIS.
- [ ] Write failing application tests for: first call submits; same eligible employee signs step 1; a non-eligible employee receives no pending step and cannot sign; employee B signs step 2 after step 1; retry after completion performs no mutation; ambiguous rows fail closed.
- [ ] On every command, reconcile the persisted transaction with HIS before deciding the next action.
- [ ] Filter pending rows by persisted `FileDocID` or exact stable `KeyID`; never select merely by admission.
- [ ] Call `SignFiles` only for the unique row HIS exposes to the authenticated employee.
- [ ] Refresh transaction/file state after signing and persist the observed state.
- [ ] Return success for accepted intermediate steps with `Status = InProcessing`; return `Signed` only when HIS reports the complete transaction finished.
- [ ] Run focused handler/client tests and build.

---

### Task 5: Align eligibility and public response

**Files:**
- Modify: `HealthExam.Application/Paraclinical/GetConclusionEligibility.cs`
- Modify: `HealthExam.Application/Paraclinical/ParaclinicalModels.cs`
- Modify: `HealthExam.API/Contracts/ParaclinicalModels.cs`
- Test: `HealthExam.Tests/Application/ParaclinicalHandlerTests.cs`
- Test: `HealthExam.Tests/API/ApiContractSurfaceTests.cs`

- [ ] Write failing tests proving `CanSignConclusion` distinguishes medical readiness from `CanCurrentEmployeeSign` once a transaction exists.
- [ ] Add normalized fields: `SWTID`, `CurrentStep`, `RequiredRoleID`, `CanCurrentEmployeeSign`, `SignedByEmployeeID`, and `SignedAt`. Preserve existing response fields where frontend compatibility requires them.
- [ ] Keep the request body empty; add a reflection/OpenAPI test proving workflow decision fields are not exposed.
- [ ] Reuse the same readiness evaluator in GET eligibility and POST sign so the button cannot be enabled by weaker conditions than the command enforces.
- [ ] Return a clear conflict when the record is ready but the active step belongs to another employee/role.
- [ ] Run API/application tests and build.

---

### Task 6: Make signed preview fail closed

**Files:**
- Modify: `HealthExam.Application/RegistrationForms/PreviewRegistrationFormPdf.cs`
- Test: `HealthExam.Tests/Application/RegistrationFormHandlerTests.cs`

- [ ] Write a failing test: a record marked `Signed` whose HIS signed file is missing must return an integration/conflict failure and must not call draft `VEMR`.
- [ ] Preserve draft fallback only before any signing transaction has reached `Signed`.
- [ ] Keep `/registration-form/preview` unchanged and continue downloading signed bytes from `api/Sign/ViewFile?filePath=...`.
- [ ] Run preview tests and build.

---

### Task 7: Regression and two-user staging acceptance

- [ ] Run signing-focused tests:

```bash
dotnet test HealthExam.Tests/HealthExam.Tests.csproj --filter "FullyQualifiedName~HisEmrClientPdfTests|FullyQualifiedName~ParaclinicalHandlerTests|FullyQualifiedName~RegistrationFormHandlerTests|FullyQualifiedName~ApiContractSurfaceTests" --no-restore
```

- [ ] Run the complete non-database suite and then the PostgreSQL tests when `HEALTHEXAM_TEST_DB` is configured.
- [ ] Run `dotnet build HealthExamServer.sln --no-restore`.
- [ ] Run `node .gitnexus/run.cjs detect-changes --scope all --repo .`; treat partial/truncated output as unresolved.
- [ ] Verify the `his-server` worktree has no changes made by this implementation.
- [ ] Stage with a workflow containing at least two roles and two employees:
  1. Submit once and record one `SWTID`/`FileDocID`.
  2. Employee A sees and signs only step 1.
  3. Employee A cannot sign step 2.
  4. Employee B sees step 2 only after step 1 completes and signs it.
  5. A retry creates no second transaction.
  6. Preview returns the final HIS file and visually shows both signatures.
  7. `pdfsig` must not report a digest mismatch; if it does, block production acceptance and escalate the returned-file contract with HIS without modifying HIS in this task.

## Definition of Done

- No production payload contains hardcoded `SWStep = 1` or `SWRoleID = 0`.
- `SubmitFile` does not contain top-level `SignatoryID` or fabricated `SignatoryFlows`.
- HIS workflow is fetched and validated by `FileDocTypeID` before submission.
- Each signing action uses the unique pending `SWTDetailID` returned by HIS for the authenticated employee.
- Multi-step signing resumes safely across users and requests without duplicate submission.
- Final preview returns only the signed HIS PDF.
- Focused tests, build, GitNexus change analysis, and two-user staging acceptance pass.
