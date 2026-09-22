# Section Digital Signing Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement per-section digital-signing orchestration entirely in `health-exam-server`, using existing HIS APIs unchanged and returning live HIS state.

**Architecture:** A pure resolver combines current medical-process JSON, sign-workflow JSON, and authenticated employee identity to derive step, role, payload, state, and allowed actions. Existing handlers call unchanged HIS routes and refresh process state before returning a normalized response.

**Tech Stack:** .NET 8, ASP.NET Core, Newtonsoft.Json/System.Text.Json, existing `IHisEmrClient`, xUnit.

**Spec:** `docs/superpowers/specs/2026-09-16-section-digital-signing-design.md`

## Global Constraints

- Modify only `/Users/nguyenhoanghai1502/MedViet/Backend/health-exam-server`.
- Do not modify, commit, configure, or deploy `his-server`.
- Do not use a git worktree; execute in the current checkout.
- Do not add a migration or NuGet dependency.
- HIS remains the sole source of workflow and state.
- Frontend requests contain no employee, role, step, or signatory flow.
- Derive employee from authenticated context and step/role from live HIS data.
- Re-read `RMedicalProcessByID` after every successful mutation.
- Preserve unrelated working-tree changes.
- Run GitNexus upstream impact before symbol edits and `detect-changes --scope all` before commits.

## File Map

- Create `HealthExam.Application/His/HisSectionSigningContextResolver.cs`.
- Create `HealthExam.Application/His/GetHisSectionSigningContexts.cs`.
- Modify `HealthExam.Application/His/HisModels.cs` and the three section mutation handlers.
- Modify `HealthExam.API/Contracts/HisEmrModels.cs`, `ExamRecordHisFormController.cs`, and DI registration.
- Modify `HisEmrOptions.cs` and `.env.example` for rollout.
- Add one resolver test file and update existing handler/endpoint tests.

---

### Task 1: Pure signing-context resolver

**Files:**
- Create: `HealthExam.Application/His/HisSectionSigningContextResolver.cs`
- Create: `HealthExam.Tests/Application/HisSectionSigningContextResolverTests.cs`
- Modify: `HealthExam.Application/His/HisModels.cs`

**Produces:**

```csharp
public interface IHisSectionSigningContextResolver
{
    ApplicationResult<IReadOnlyList<HisSectionSigningResult>> ResolveAll(
        HisJsonDocument process, HisJsonDocument workflow,
        string actorId, ActorKind actorKind);
}
```

- [ ] Run impact:

```bash
node .gitnexus/run.cjs impact "HisJsonDocument" --direction upstream --repo .
node .gitnexus/run.cjs impact "HisModels.cs" --direction upstream --repo .
```

- [ ] Add failing tests for process-detail aliases, ordered workflow steps, all five state mappings, invalid employee, missing workflow step, and later-step cancellation guard.

```csharp
[Fact]
public void Finish_maps_to_signed_and_uses_authenticated_employee()
{
    var result = _resolver.ResolveAll(
        Doc(ProcessJson(signStatus: 2, swStep: 1, employeeId: 100)),
        Doc(WorkflowJson(step: 1, roleId: 10)),
        "100", ActorKind.Employee);

    Assert.True(result.IsSuccess);
    var section = Assert.Single(result.Value);
    Assert.Equal("Signed", section.Status);
    Assert.Equal(100, section.EmployeeID);
    Assert.Equal(10, section.HandledRoleID);
}
```

- [ ] Run RED:

```bash
dotnet test HealthExam.Tests/HealthExam.Tests.csproj --filter FullyQualifiedName~HisSectionSigningContextResolverTests
```

- [ ] Implement `HisSectionSigningResult` with public fields plus internal `EmployeeID`, `HandledRoleID`, and `SignatoryFlowsJson`. Parse defensively and fail instead of defaulting missing step/role to zero.

- [ ] Run GREEN/build, analyze, and commit:

```bash
dotnet test HealthExam.Tests/HealthExam.Tests.csproj --filter FullyQualifiedName~HisSectionSigningContextResolverTests
dotnet build HealthExamServer.sln --no-restore
node .gitnexus/run.cjs detect-changes --scope all --repo .
git add HealthExam.Application/His/HisSectionSigningContextResolver.cs HealthExam.Application/His/HisModels.cs HealthExam.Tests/Application/HisSectionSigningContextResolverTests.cs
git commit -m "feat(his): resolve section signing context"
```

---

### Task 2: Live section-status handler

**Files:**
- Create: `HealthExam.Application/His/GetHisSectionSigningContexts.cs`
- Modify: `HealthExam.API/Extensions/ApplicationServiceExtensions.cs`
- Modify: `HealthExam.Tests/Application/HisHandlerTests.cs`

**Produces:**

```csharp
public interface IGetHisSectionSigningContextsHandler
{
    Task<ApplicationResult<IReadOnlyList<HisSectionSigningResult>>> HandleAsync(
        GetHisSectionSigningContextsQuery query, CancellationToken ct = default);
}
```

- [ ] Run impact for `HisProcessValidator`, `GetSignWorkflowHandler`, and `AddHealthExamApplication`.
- [ ] Add failing tests proving ownership validation, `FileDocTypeID` extraction, existing `RSWByDocTypeID` use, and resolver delegation.
- [ ] Run RED:

```bash
dotnet test HealthExam.Tests/HealthExam.Tests.csproj --filter "FullyQualifiedName~HisHandlerTests&Name~SigningContexts"
```

- [ ] Implement using `ValidateAndGetOwnedProcessAsync`; do not duplicate ownership logic. Extract `FileDocTypeID/FileDocTypeId`, fetch workflow using `HisConstants.RouteRSWByDocTypeId`, and call `ResolveAll` with authenticated actor data carried by the query.
- [ ] Register resolver/handler, run tests/build and commit after detect-changes:

```bash
git add HealthExam.Application/His/GetHisSectionSigningContexts.cs HealthExam.API/Extensions/ApplicationServiceExtensions.cs HealthExam.Tests/Application/HisHandlerTests.cs
git commit -m "feat(his): read live section signing status"
```

---

### Task 3: Derived submit/sign/cancel payloads

**Files:**
- Modify: `HealthExam.Application/His/SubmitHisSection.cs`
- Modify: `HealthExam.Application/His/SignHisSection.cs`
- Modify: `HealthExam.Application/His/CancelHisSectionSignature.cs`
- Modify: `HealthExam.Application/His/HisModels.cs`
- Modify: `HealthExam.Tests/Application/HisHandlerTests.cs`

- [ ] Run upstream impact for all handlers and command records.
- [ ] Add failing tests proving commands contain no caller-controlled decision fields, payloads use resolved values, invalid transitions return conflict, and every success triggers a fresh read.

```csharp
Assert.Equal(processId, body["Id"]!.Value<Guid>());
Assert.Equal("KSK_NOI", body["ItemGroupID"]!.Value<string>());
Assert.Equal(1, body["SWStep"]!.Value<int>());
Assert.Equal(100, body["EmpID"]!.Value<long>());
Assert.Equal(10, body["HandledRoleID"]!.Value<long>());
```

- [ ] Run RED with `HisHandlerTests`.
- [ ] Reduce commands to identity/credential fields. Inject the live-status handler, select exact ordinal `SectionKey`, require the matching capability, build the unchanged HIS payload from resolved context, call the existing route, then read and return refreshed state.
- [ ] Preserve trimmed cancellation reason validation. Return refresh failure rather than guessed success.
- [ ] Run focused tests/build, detect changes, and commit:

```bash
git add HealthExam.Application/His/SubmitHisSection.cs HealthExam.Application/His/SignHisSection.cs HealthExam.Application/His/CancelHisSectionSignature.cs HealthExam.Application/His/HisModels.cs HealthExam.Tests/Application/HisHandlerTests.cs
git commit -m "feat(his): derive section signing payloads"
```

---

### Task 4: Public API and rollout flag

**Files:**
- Modify: `HealthExam.API/Contracts/HisEmrModels.cs`
- Modify: `HealthExam.API/Controllers/ExamRecordHisFormController.cs`
- Modify: `HealthExam.Infrastructure/Integrations/HisEmr/HisEmrOptions.cs`
- Modify: `.env.example`
- Modify: `HealthExam.Tests/HisFormEndpointTests.cs`
- Modify: `HealthExam.Tests/HisExamFormServiceTests.cs`

- [ ] Run impact for the controller, request records, and options.
- [ ] Add failing tests for `GET processes/{processId}/sections`, no body on submit/sign, reason-only cancel, and `HIS_EMR_SECTION_SIGNING_ENABLED` parsing/default false.
- [ ] Run RED:

```bash
dotnet test HealthExam.Tests/HealthExam.Tests.csproj --filter "FullyQualifiedName~HisFormEndpointTests|FullyQualifiedName~HisExamFormServiceTests"
```

- [ ] Add `HisSectionSigningItem.From(HisSectionSigningResult)`, the list action, and normalized result mapping. Gate all four actions with `SectionSigningEnabled`; document `HIS_EMR_SECTION_SIGNING_ENABLED=false`.
- [ ] Run focused tests/build, detect changes, and commit:

```bash
git add HealthExam.API/Contracts/HisEmrModels.cs HealthExam.API/Controllers/ExamRecordHisFormController.cs HealthExam.Infrastructure/Integrations/HisEmr/HisEmrOptions.cs .env.example HealthExam.Tests/HisFormEndpointTests.cs HealthExam.Tests/HisExamFormServiceTests.cs
git commit -m "feat(api): expose section digital signing"
```

---

### Task 5: Final regression and release gate

- [ ] Run signing-focused tests:

```bash
dotnet test HealthExam.Tests/HealthExam.Tests.csproj --filter "FullyQualifiedName~HisSectionSigningContextResolverTests|FullyQualifiedName~HisHandlerTests|FullyQualifiedName~HisEmrClientTests|FullyQualifiedName~HisFormEndpointTests|FullyQualifiedName~HisExamFormServiceTests"
```

- [ ] Run the full non-PostgreSQL suite. Baseline: 894 pass; 19 PostgreSQL tests require `HEALTHEXAM_TEST_DB`.
- [ ] If `HEALTHEXAM_TEST_DB` is available, run all 913 tests.
- [ ] Run final checks:

```bash
dotnet build HealthExamServer.sln --no-restore
node .gitnexus/run.cjs detect-changes --scope all --repo .
git status --short
git -C /Users/nguyenhoanghai1502/MedViet/Backend/his-server status --short
```

- [ ] Verify no HIS source diff and no health-exam migration.
- [ ] Staging smoke: GET sections → submit → sign → cancel; each mutation returns freshly read HIS state with the same trace ID.

## Release Gate

- Signing-focused tests pass and build has zero errors.
- No `his-server` source change exists.
- No health-exam database migration exists.
- Frontend cannot submit actor/workflow decision fields.
- Rollback is `HIS_EMR_SECTION_SIGNING_ENABLED=false`.
