# HIS Conclusion Signing and Signed PDF Preview Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the legacy form-server conclusion-signing path with HIS-only orchestration and make the existing PDF preview return the HIS-signed PDF after conclusion signing.

**Architecture:** Add one shared application handler that resolves the unique KSK medical process and conclusion section from live HIS state. Reuse the existing section submit/sign handlers for mutations, add a binary HIS adapter for admission-level signed PDFs, and make conclusion eligibility/signing plus preview consume the shared live context.

**Tech Stack:** .NET 8, ASP.NET Core, EF Core 8, Newtonsoft.Json/System.Text.Json, existing `IHisEmrClient`, xUnit.

**Spec:** `docs/superpowers/specs/2026-09-16-his-conclusion-signing-and-signed-pdf-design.md`

## Global Constraints

- Modify only `/Users/nguyenhoanghai1502/MedViet/Backend/health-exam-server`.
- Do not modify, configure, commit, or deploy `his-server`.
- Do not use `form-server` for conclusion eligibility, signing, or PDF preview.
- Do not accept actor, step, role, process, section, or signatory flow from the conclusion request.
- HIS remains the source of truth for signing state and signed PDF files.
- Do not persist signing state or PDF bytes and do not add a database migration.
- Do not automatically retry `SubmitEMR` or `SignEMR`.
- Keep `GET /v1/exam-records/{recordId}/registration-form/preview` unchanged.
- Treat `ItemGroupID = "M02F01512"` as the exact HIS conclusion-section contract.
- Preserve unrelated dirty and untracked files.
- Run GitNexus upstream impact before every symbol edit and `detect-changes --scope all` before every commit.

## File Map

- Modify `HealthExam.Application/Integrations/IHisEmrClient.cs`: add the signed-admission PDF operation.
- Modify `HealthExam.Application/His/HisHelper.cs`: add the unchanged HIS `VEMRs` route and conclusion section key.
- Modify `HealthExam.Infrastructure/Integrations/HisEmr/HisEmrClient.cs`: fetch and validate signed PDF bytes.
- Create `HealthExam.Application/His/ResolveHisConclusionContext.cs`: resolve the unique KSK process and live conclusion/signing context.
- Modify `HealthExam.Application/His/HisModels.cs`: add conclusion-context query/result models.
- Modify `HealthExam.Application/Paraclinical/GetConclusionEligibility.cs`: replace form-server condition A with live HIS section completion.
- Modify `HealthExam.Application/Paraclinical/SignConclusion.cs`: orchestrate submit/sign/resume/idempotency through existing HIS handlers.
- Modify `HealthExam.Application/Paraclinical/ParaclinicalModels.cs`: remove legacy caller-controlled signing inputs and return live HIS identity/state.
- Modify `HealthExam.API/Contracts/ParaclinicalModels.cs` and `HealthExam.API/Controllers/ExamRecordController.cs`: accept an empty conclusion-sign request.
- Modify `HealthExam.Application/RegistrationForms/PreviewRegistrationFormPdf.cs` and `RegistrationFormModels.cs`: select signed versus draft PDF from live HIS state.
- Modify `HealthExam.API/Controllers/ExamRecordRegistrationFormController.cs`: pass authenticated actor context and return inline PDF.
- Modify `HealthExam.API/Extensions/ApplicationServiceExtensions.cs`: register the shared conclusion-context handler.
- Test in existing HIS, paraclinical, registration-form, PDF-adapter, and API contract test files.

---

### Task 1: Signed admission PDF adapter

**Files:**
- Modify: `HealthExam.Application/Integrations/IHisEmrClient.cs`
- Modify: `HealthExam.Application/His/HisHelper.cs`
- Modify: `HealthExam.Infrastructure/Integrations/HisEmr/HisEmrClient.cs`
- Modify: `HealthExam.Tests/Infrastructure/HisEmrClientPdfTests.cs`
- Modify: `HealthExam.Tests/Application/ExamRecordHandlerTests.cs`
- Modify: `HealthExam.Tests/Application/HisHandlerTests.cs`
- Modify: `HealthExam.Tests/Application/RegistrationFormHandlerTests.cs`
- Modify: `HealthExam.Tests/HisExamFormGatewayDefinitionTests.cs`
- Modify: `HealthExam.Tests/HisExamFormServiceTests.cs`

**Interfaces:**
- Produces:

```csharp
Task<HisClientResult<byte[]>> RenderSignedAdmissionPdfAsync(
    long admissionId,
    HisRequest request,
    CancellationToken ct = default);
```

- [ ] **Step 1: Run impact before interface/client edits**

```bash
node .gitnexus/run.cjs impact "IHisEmrClient" --direction upstream --repo .
node .gitnexus/run.cjs impact "HisEmrClient" --direction upstream --repo .
```

Expected: record the HIGH/CRITICAL interface blast radius and enumerate all test doubles returned at depth 1. Update every implementation in the same task.

- [ ] **Step 2: Write failing adapter tests**

Add focused tests proving the method calls the deployed route and accepts only a non-empty PDF:

```csharp
[Fact]
public async Task RenderSignedAdmissionPdfAsync_calls_VEMRs_and_returns_pdf()
{
    const long admissionId = 7001;
    var bytes = Encoding.ASCII.GetBytes("%PDF-1.7 signed");
    var client = Client(req =>
    {
        Assert.Contains($"api/M02F01500/VEMRs?admissionID={admissionId}", req.RequestUri!.ToString());
        Assert.Equal("Bearer token123", req.Headers.Authorization?.ToString());
        return Pdf(bytes);
    });

    var result = await client.RenderSignedAdmissionPdfAsync(
        admissionId,
        new HisRequest("", "GET", "Bearer token123", TraceId: "trace-1", DivisionId: "DEV"));

    Assert.True(result.IsSuccess);
    Assert.Equal(bytes, result.Value);
}
```

Also add tests for `admissionId <= 0`, JSON error body, empty body, non-PDF body, 400/no signed files, 401/403, timeout, and connection failure. Map only the HIS message `Không tìm thấy dữ liệu hồ sơ bệnh án` to `HisClientOutcome.NotFound`; keep other HTTP 400 responses as `SignPrecondition`.

- [ ] **Step 3: Run RED**

```bash
dotnet test HealthExam.Tests/HealthExam.Tests.csproj --filter FullyQualifiedName~HisEmrClientPdfTests --no-restore
```

Expected: compile failure because `RenderSignedAdmissionPdfAsync` does not exist.

- [ ] **Step 4: Implement the minimum binary adapter**

Add:

```csharp
public const string RouteVEmrs = "api/M02F01500/VEMRs";
public const string ConclusionSectionKey = "M02F01512";
```

Implement `RenderSignedAdmissionPdfAsync` beside `RenderFormPdfAsync`. Reuse the existing option validation, credential resolution, trace/division forwarding, HTTP outcome mapping, and `%PDF-` validation. The relative URL must be:

```csharp
$"{HisConstants.RouteVEmrs}?admissionID={admissionId}"
```

Do not route binary content through `SendAsync`, because `SendAsync` expects the JSON HIS envelope.

- [ ] **Step 5: Update all existing `IHisEmrClient` fakes with the default interface-compatible method and run GREEN**

```bash
dotnet test HealthExam.Tests/HealthExam.Tests.csproj --filter "FullyQualifiedName~HisEmrClientPdfTests|FullyQualifiedName~RegistrationFormHandlerTests|FullyQualifiedName~HisHandlerTests" --no-restore
dotnet build HealthExamServer.sln --no-restore
```

- [ ] **Step 6: Analyze and commit**

```bash
node .gitnexus/run.cjs detect-changes --scope all --repo .
git add HealthExam.Application/Integrations/IHisEmrClient.cs HealthExam.Application/His/HisHelper.cs HealthExam.Infrastructure/Integrations/HisEmr/HisEmrClient.cs HealthExam.Tests/Infrastructure/HisEmrClientPdfTests.cs HealthExam.Tests/Application/ExamRecordHandlerTests.cs HealthExam.Tests/Application/HisHandlerTests.cs HealthExam.Tests/Application/RegistrationFormHandlerTests.cs HealthExam.Tests/HisExamFormGatewayDefinitionTests.cs HealthExam.Tests/HisExamFormServiceTests.cs
git commit -m "feat(his): fetch signed admission PDF"
```

Before committing, inspect `git diff --cached --name-only` and unstage unrelated test changes already present in the working tree.

---

### Task 2: Shared live HIS conclusion context

**Files:**
- Create: `HealthExam.Application/His/ResolveHisConclusionContext.cs`
- Modify: `HealthExam.Application/His/HisModels.cs`
- Modify: `HealthExam.API/Extensions/ApplicationServiceExtensions.cs`
- Modify: `HealthExam.Tests/Application/HisHandlerTests.cs`

**Interfaces:**
- Consumes: `IExamRecordRepository`, `IRegistrationFormRepository`, `IHisEmrClient`, and `IGetHisSectionSigningContextsHandler`.
- Produces:

```csharp
public sealed record ResolveHisConclusionContextQuery(
    string DivisionId,
    Guid RecordId,
    string Credential,
    string TraceId,
    string ActorId,
    ActorKind ActorKind);

public sealed record HisConclusionContextResult(
    Guid RecordId,
    long AdmissionId,
    Guid ProcessId,
    string MedicalTypeCode,
    HisSectionSigningResult Conclusion,
    IReadOnlyList<HisSectionSigningResult> Sections);

public interface IResolveHisConclusionContextHandler
{
    Task<ApplicationResult<HisConclusionContextResult>> HandleAsync(
        ResolveHisConclusionContextQuery query,
        CancellationToken ct = default);
}
```

- [ ] **Step 1: Run impact before edits**

```bash
node .gitnexus/run.cjs impact "GetHisSectionSigningContextsHandler" --direction upstream --repo .
node .gitnexus/run.cjs impact "IRegistrationFormRepository" --direction upstream --repo .
```

- [ ] **Step 2: Write failing resolver tests**

Test these exact cases:

1. Missing record returns NotFound without calling HIS.
2. Missing/invalid `AdmissionID` returns InvalidState.
3. Missing active mapping or blank `MedicalTypeCode` returns InvalidState.
4. HIS process list is filtered by exact case-insensitive `MedicalTypeCode`.
5. Zero matching processes returns InvalidState.
6. Two matching processes returns InvalidState; never select the first.
7. Process ID aliases `Id`, `MedicalProcessID`, and `MedicalProcessId` parse correctly.
8. Missing exact ordinal `M02F01512` returns HisBadGateway.
9. Success returns the process, conclusion, and all live section states.

Representative success assertion:

```csharp
Assert.Equal(processId, result.Value.ProcessId);
Assert.Equal(HisConstants.ConclusionSectionKey, result.Value.Conclusion.SectionKey);
Assert.Equal("KSK03", result.Value.MedicalTypeCode);
```

- [ ] **Step 3: Run RED**

```bash
dotnet test HealthExam.Tests/HealthExam.Tests.csproj --filter "FullyQualifiedName~HisHandlerTests&Name~ConclusionContext" --no-restore
```

- [ ] **Step 4: Implement process and section resolution**

The handler must:

```csharp
var record = await _records.GetAsync(query.DivisionId, query.RecordId, false, ct);
var mapping = await _forms.GetActiveMappingAsync(query.DivisionId, record.VariantCode, ct);
```

Call `$"{HisConstants.RouteRMedicalProcess}?admissionID={record.AdmissionID.Value}"`, parse only the returned array, and require exactly one process whose `MedicalTypeCode` equals the mapping value with `OrdinalIgnoreCase`. Pass the resolved `processId` to the existing signing-context handler and select:

```csharp
var conclusion = sections.SingleOrDefault(x =>
    string.Equals(x.SectionKey, HisConstants.ConclusionSectionKey, StringComparison.Ordinal));
```

Do not match `SectionName`, do not accept a process/section from the request, and do not duplicate workflow parsing already owned by `GetHisSectionSigningContextsHandler`.

- [ ] **Step 5: Register the handler and run GREEN**

```csharp
services.AddScoped<IResolveHisConclusionContextHandler, ResolveHisConclusionContextHandler>();
```

```bash
dotnet test HealthExam.Tests/HealthExam.Tests.csproj --filter "FullyQualifiedName~HisHandlerTests&Name~ConclusionContext" --no-restore
dotnet build HealthExamServer.sln --no-restore
```

- [ ] **Step 6: Analyze and commit only Task 2 files**

```bash
node .gitnexus/run.cjs detect-changes --scope all --repo .
git commit -m "feat(his): resolve live conclusion context"
```

---

### Task 3: HIS-only conclusion eligibility and signing

**Files:**
- Modify: `HealthExam.Application/Paraclinical/GetConclusionEligibility.cs`
- Modify: `HealthExam.Application/Paraclinical/SignConclusion.cs`
- Modify: `HealthExam.Application/Paraclinical/ParaclinicalModels.cs`
- Modify: `HealthExam.API/Contracts/ParaclinicalModels.cs`
- Modify: `HealthExam.API/Controllers/ExamRecordController.cs`
- Modify: `HealthExam.Tests/Application/ParaclinicalHandlerTests.cs`
- Modify: `HealthExam.Tests/ConclusionEligibilityTests.cs`
- Modify: `HealthExam.Tests/API/ApiContractSurfaceTests.cs`

**Interfaces:**
- Consumes: `IResolveHisConclusionContextHandler`, `ISubmitHisSectionHandler`, `ISignHisSectionHandler`, and existing condition-B repository logic.
- Produces:

```csharp
public sealed record GetConclusionEligibilityQuery(
    string DivisionId,
    Guid RecordId,
    string Credential,
    string TraceId,
    string ActorId,
    ActorKind ActorKind);

public sealed record SignConclusionCommand(
    string DivisionId,
    string ActorId,
    string ActorName,
    ActorKind ActorKind,
    Guid RecordId,
    string Credential,
    string TraceId);

public sealed record ConclusionSignResult(
    Guid ProfileID,
    Guid ProcessID,
    string SectionKey,
    string Status,
    long? SignedByEmployeeID,
    DateTime? SignedAt,
    IReadOnlyList<ConclusionConditionResult> Conditions,
    IReadOnlyList<ConclusionSignStepResult> Steps);
```

- [ ] **Step 1: Run impact and record CRITICAL risk**

```bash
node .gitnexus/run.cjs impact "SignConclusionHandler" --direction upstream --repo .
node .gitnexus/run.cjs impact "GetConclusionEligibilityHandler" --direction upstream --repo .
node .gitnexus/run.cjs impact "ConclusionSignRequest" --direction upstream --repo .
```

Expected: `SignConclusionHandler` is CRITICAL because controller, DI, legacy adapters, and tests consume it. All direct consumers must be updated in this task.

- [ ] **Step 2: Replace legacy tests with failing HIS-only behavior tests**

Delete assertions that expect `IFormServerClient.GetProgressAsync` or `.SignAsync`. Add tests proving:

- condition A is satisfied only when every non-conclusion HIS section is `Signed`;
- condition B still comes from `EvaluateConditionBAsync`;
- no form-server client is constructed or called;
- New conclusion performs submit, observes InProcessing, performs sign, and observes Signed;
- InProcessing performs sign only;
- Signed returns success without mutation;
- a failed submit/sign returns the live state and does not make the next call repeat a completed mutation;
- a final non-Signed state returns SignPrecondition/InvalidState rather than success.

Representative orchestration assertion:

```csharp
Assert.Equal(1, submit.CallCount);
Assert.Equal(1, sign.CallCount);
Assert.Equal(HisConstants.ConclusionSectionKey, result.Value.SectionKey);
Assert.Equal("Signed", result.Value.Status);
```

Add an OpenAPI/reflection test that `ConclusionSignRequest` has no public properties named `ConclusionSectionID`, `Actor`, or `SignatoryFlows`.

- [ ] **Step 3: Run RED**

```bash
dotnet test HealthExam.Tests/HealthExam.Tests.csproj --filter "FullyQualifiedName~ParaclinicalHandlerTests|FullyQualifiedName~ConclusionEligibilityTests|FullyQualifiedName~ApiContractSurfaceTests" --no-restore
```

- [ ] **Step 4: Replace form-server condition A**

Both eligibility and sign-time revalidation must call the shared conclusion resolver. Define condition A as:

```csharp
var clinicalSections = context.Sections.Where(x =>
    !string.Equals(x.SectionKey, HisConstants.ConclusionSectionKey, StringComparison.Ordinal));
var conditionAOk = clinicalSections.Any() && clinicalSections.All(x => x.Status == "Signed");
```

The detail reports signed/total HIS clinical sections and `Source = "his-server"`. Do not use `ExamRecord.ProgressDone/ProgressTotal`, because those fields are a delayed display cache.

- [ ] **Step 5: Implement resumable/idempotent conclusion orchestration**

Use exact state branching:

```csharp
var conclusion = context.Conclusion;
if (conclusion.Status == "New")
{
    var submitted = await _submit.HandleAsync(new SubmitHisSectionCommand(
        command.DivisionId, command.RecordId, context.ProcessId,
        HisConstants.ConclusionSectionKey, command.Credential, command.TraceId,
        command.ActorId, command.ActorKind), ct);
    if (!submitted.IsSuccess)
        return ApplicationResult<ConclusionSignResult>.Fail(
            submitted.Failure.Code, submitted.Failure.Message, submitted.Failure.Payload);
    conclusion = submitted.Value;
}

if (conclusion.Status == "InProcessing")
{
    var signed = await _sign.HandleAsync(new SignHisSectionCommand(
        command.DivisionId, command.RecordId, context.ProcessId,
        HisConstants.ConclusionSectionKey, command.Credential, command.TraceId,
        command.ActorId, command.ActorKind), ct);
    if (!signed.IsSuccess)
        return ApplicationResult<ConclusionSignResult>.Fail(
            signed.Failure.Code, signed.Failure.Message, signed.Failure.Payload);
    conclusion = signed.Value;
}

if (conclusion.Status != "Signed")
    return ApplicationResult<ConclusionSignResult>.Fail(
        ApplicationFailureCode.SignPrecondition,
        $"Kết luận chưa hoàn tất ký số trên HIS (trạng thái: {conclusion.Status})");
```

Audit only the observed final state. Never record Signed before HIS returns Signed.

- [ ] **Step 6: Reduce the public request and forward trusted context**

Make `ConclusionSignRequest` an empty class for source compatibility:

```csharp
public sealed class ConclusionSignRequest { }
```

The controller must build the command exclusively from route and request context:

```csharp
new SignConclusionCommand(
    HealthExamContext.DivisionId,
    HealthExamContext.ActorId.ToString(),
    HealthExamContext.ActorName,
    HealthExamContext.ActorKind,
    recordId,
    Credential,
    HealthExamContext.TraceId)
```

Add the same credential/actor fields to the eligibility query so button state reflects the current employee's live HIS permissions.

- [ ] **Step 7: Run GREEN and regression tests**

```bash
dotnet test HealthExam.Tests/HealthExam.Tests.csproj --filter "FullyQualifiedName~ParaclinicalHandlerTests|FullyQualifiedName~ConclusionEligibilityTests|FullyQualifiedName~ApiContractSurfaceTests" --no-restore
dotnet build HealthExamServer.sln --no-restore
```

- [ ] **Step 8: Analyze and commit**

```bash
node .gitnexus/run.cjs detect-changes --scope all --repo .
git commit -m "feat(conclusion): sign through HIS workflow"
```

---

### Task 4: State-aware signed PDF preview

**Files:**
- Modify: `HealthExam.Application/RegistrationForms/RegistrationFormModels.cs`
- Modify: `HealthExam.Application/RegistrationForms/PreviewRegistrationFormPdf.cs`
- Modify: `HealthExam.API/Controllers/ExamRecordRegistrationFormController.cs`
- Modify: `HealthExam.Tests/Application/RegistrationFormHandlerTests.cs`
- Modify: `HealthExam.Tests/API/RegistrationFormEndpointTests.cs`

**Interfaces:**
- Consumes: `IResolveHisConclusionContextHandler`, `IHisEmrClient.RenderSignedAdmissionPdfAsync`, and existing `RenderFormPdfAsync`.
- Produces the unchanged endpoint `GET /v1/exam-records/{recordId}/registration-form/preview`.

- [ ] **Step 1: Run impact**

```bash
node .gitnexus/run.cjs impact "PreviewRegistrationFormPdfHandler" --direction upstream --repo .
node .gitnexus/run.cjs impact "PreviewRegistrationFormPdfQuery" --direction upstream --repo .
```

- [ ] **Step 2: Write failing preview-selection tests**

Add four mutually exclusive cases:

1. Conclusion Signed + signed PDF available -> return `VEMRs`; draft renderer call count remains zero.
2. Conclusion Signed + signed PDF unavailable -> return failure; draft renderer call count remains zero.
3. Conclusion not Signed + `VEMRs` available -> return signed aggregate.
4. Conclusion not Signed + `VEMRs` reports no signed files -> fallback to draft `VEMR` by `HisEmrDataID`.

Representative safety assertion:

```csharp
Assert.False(result.IsSuccess);
Assert.Equal(1, his.SignedPreviewCalls);
Assert.Equal(0, his.DraftPreviewCalls);
```

Also retain tenant/missing-record tests and require valid `AdmissionID` for signed preview. A missing `HisEmrDataID` is only an error when draft fallback is actually needed.

- [ ] **Step 3: Run RED**

```bash
dotnet test HealthExam.Tests/HealthExam.Tests.csproj --filter "FullyQualifiedName~RegistrationFormHandlerTests|FullyQualifiedName~RegistrationFormEndpointTests" --no-restore
```

- [ ] **Step 4: Add actor context to preview query and implement selection**

```csharp
public sealed record PreviewRegistrationFormPdfQuery(
    string DivisionId,
    Guid RecordId,
    string Credential = null,
    string TraceId = null,
    string ActorId = null,
    ActorKind ActorKind = ActorKind.Employee);
```

Selection rule:

```csharp
var contextResult = await _conclusion.HandleAsync(
    new ResolveHisConclusionContextQuery(
        query.DivisionId, query.RecordId, query.Credential, query.TraceId,
        query.ActorId, query.ActorKind),
    ct);
if (!contextResult.IsSuccess)
    return ApplicationResult<byte[]>.Fail(
        contextResult.Failure.Code,
        contextResult.Failure.Message,
        contextResult.Failure.Payload);
var context = contextResult.Value;
var hisRequest = new HisRequest(
    "", "GET", query.Credential,
    TraceId: query.TraceId,
    DivisionId: query.DivisionId);
var signed = await _client.RenderSignedAdmissionPdfAsync(
    record.AdmissionID!.Value, hisRequest, ct);

if (context.Conclusion.Status == "Signed")
    return HisOutcomeMapper.ToApplicationResult(signed);

if (signed.IsSuccess)
    return ApplicationResult<byte[]>.Success(signed.Value);

if (signed.Outcome != HisClientOutcome.NotFound)
    return HisOutcomeMapper.ToApplicationResult(signed);

if (!record.HisEmrDataID.HasValue || record.HisEmrDataID == Guid.Empty)
    return ApplicationResult<byte[]>.Fail(
        ApplicationFailureCode.InvalidState,
        "Biểu mẫu chưa được lưu lên HIS");

var draft = await _client.RenderFormPdfAsync(record.HisEmrDataID.Value, hisRequest, ct);
return HisOutcomeMapper.ToApplicationResult(draft);
```

Only the precise HIS outcome meaning “no signed files” may trigger draft fallback before conclusion signing. Authentication, timeout, malformed response, or connection failures must remain failures instead of being hidden by fallback.

- [ ] **Step 5: Return inline PDF and run GREEN**

Pass `HealthExamContext.ActorId/ActorKind` from the controller. Return:

```csharp
return File(res.Value, "application/pdf", enableRangeProcessing: false);
```

If the framework overload adds attachment disposition, use a `FileContentResult` with `FileDownloadName = null` and assert `Content-Disposition` is absent or inline; never force attachment.

```bash
dotnet test HealthExam.Tests/HealthExam.Tests.csproj --filter "FullyQualifiedName~RegistrationFormHandlerTests|FullyQualifiedName~RegistrationFormEndpointTests|FullyQualifiedName~HisEmrClientPdfTests" --no-restore
dotnet build HealthExamServer.sln --no-restore
```

- [ ] **Step 6: Analyze and commit**

```bash
node .gitnexus/run.cjs detect-changes --scope all --repo .
git commit -m "feat(pdf): preview HIS signed exam file"
```

---

### Task 5: Remove legacy conclusion coupling and release gate

**Files:**
- Modify: `HealthExam.Tests/TestParaclinicalAdapters.cs`
- Modify: `HealthExam.Tests/InMemoryTestDb.cs`
- Modify: `docs/api/his-section-signing-api.md`
- Modify only if now unused by conclusion: conclusion-specific references to `IFormServerClient`; do not delete the integration if other features still consume it.

**Interfaces:**
- Produces: documented HIS-only conclusion request and preview behavior.

- [ ] **Step 1: Search for stale public contract and conclusion dependencies**

```bash
rg -n "ConclusionSectionID|SignatoryFlows|ActorInfo|form-server.*kết luận|Ký kết luận.*form-server" HealthExam.API HealthExam.Application HealthExam.Tests docs/api
```

Expected: no production conclusion route accepts legacy decision fields and no conclusion handler calls form-server. Keep unrelated form-server webhook/progress code outside this feature unchanged.

- [ ] **Step 2: Update API documentation with executable examples**

Document:

```http
POST /v1/exam-records/{recordId}/conclusion/sign
Authorization: Bearer <his_employee_token>
Content-Type: application/json

{}
```

Document that the existing preview route returns the signed HIS PDF after conclusion signing and may show available signed sections earlier.

- [ ] **Step 3: Run signing/preview-focused verification**

```bash
dotnet test HealthExam.Tests/HealthExam.Tests.csproj --filter "FullyQualifiedName~HisHandlerTests|FullyQualifiedName~ParaclinicalHandlerTests|FullyQualifiedName~ConclusionEligibilityTests|FullyQualifiedName~RegistrationFormHandlerTests|FullyQualifiedName~RegistrationFormEndpointTests|FullyQualifiedName~HisEmrClientPdfTests|FullyQualifiedName~ApiContractSurfaceTests" --no-restore
```

- [ ] **Step 4: Run full local verification**

```bash
dotnet format HealthExamServer.sln --verify-no-changes --no-restore --include HealthExam.Application HealthExam.Infrastructure HealthExam.API HealthExam.Tests
dotnet build HealthExamServer.sln --no-restore
dotnet test HealthExamServer.sln --no-build --filter "FullyQualifiedName!~Postgres&FullyQualifiedName!~PatientProfilePersistenceTests"
git diff --check
node .gitnexus/run.cjs detect-changes --scope all --repo .
```

If PostgreSQL test configuration is available, also run the full suite. Formatting failures in unrelated pre-existing files must be reported, not silently fixed as part of this feature.

- [ ] **Step 5: Verify scope and commit documentation/test adapters**

```bash
git -C ../his-server status --short
git status --short
git diff --cached --name-only
git commit -m "docs: document HIS conclusion signing flow"
```

The HIS working tree must have no changes made by this plan. The health-exam diff must contain no signing-state migration, PDF cache, or form-server conclusion call.

- [ ] **Step 6: Staging acceptance**

Run with an authorized HIS employee:

1. Confirm eligibility reports all non-conclusion HIS sections signed and all CLS complete/cancelled.
2. Call conclusion sign with `{}`.
3. Confirm response identifies process, `M02F01512`, `Signed`, signer, and signing time.
4. Call the unchanged preview endpoint.
5. Open the returned PDF and visually confirm the HIS digital signature is displayed.
6. Repeat conclusion sign and confirm no second mutation occurs.

## Release Gate

- Build succeeds with zero new warnings.
- Focused and full non-PostgreSQL tests pass.
- Conclusion eligibility and signing make no form-server calls.
- The public conclusion request exposes no actor/workflow decision fields.
- A Signed conclusion can never return the unsigned draft PDF.
- Repeated conclusion sign is idempotent from live HIS state.
- No `his-server` file changes and no health-exam migration exist.
- The staging PDF visibly contains the HIS-rendered digital signature.
