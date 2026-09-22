# Section Signing Progress State Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Khi lưu lần đầu phần khám M05, mở trạng thái đang khám; khi ký từng chuyên khoa, cập nhật bước sang đã ký và trả tiến độ ký dạng `1/8`, `2/8`...; chỉ bước ký kết luận cuối mới đóng chữ ký vào PDF.

**Architecture:** Dùng `HEX_ExamRecord` làm state machine của hồ sơ và `HEX_ExamRecordSignStep` làm state machine của từng bước. Bước đầu được xác định từ `HEX_SignStepMap` theo `VariantCode`, không hardcode M05 hay số bước. Tiến độ ký được tính từ các step non-conclusion có trạng thái `Signed`, tách khỏi `ProgressDone/ProgressTotal` hiện đang dành cho tiến độ webhook lâm sàng.

**UI requirement:** Khu vực danh mục process ở màn hình khám phải đọc cùng signing progress từ API: sau lưu M05 hiển thị `0/8`, sau ký bước đầu hiển thị `1/8`, đồng thời dòng step đang chọn đổi trạng thái `Đang khám` → `Đã ký` và hiển thị người ký/thời gian ký.

**Tech Stack:** .NET 8, ASP.NET Core, EF Core/Npgsql, xUnit, PostgreSQL.

**Spec:** Thiết kế đã được chốt trong hội thoại ngày 2026-09-18.

## Global Constraints

- Chỉ sửa `health-exam-server`; không gọi hoặc sửa code `his-server`.
- Không hardcode `M05`, `SWStep=1` hoặc tổng bước `8`; đọc từ `HEX_SignStepMap`.
- Lưu dữ liệu HIS thành công rồi mới cập nhật state local; lỗi HIS không được mở trạng thái hồ sơ.
- Ký section chỉ cập nhật state local; PDF chỉ render/ký một lần ở `POST /conclusion/sign`.
- Khóa theo `RecordID` để tránh hai lượt lưu/ký đồng thời làm sai tiến độ.

---

### Task 1: Mở rộng state machine của hồ sơ và bước ký

**Files:**
- Modify: `HealthExam.Domain/ExamRecords/ExamRecord.cs`
- Modify: `HealthExam.Domain/ExamRecords/ExamRecordSignStep.cs`
- Modify: `HealthExam.Infrastructure/Persistence/HealthExamDbContext.cs`
- Test: `HealthExam.Tests/Signing/SignStepMapTests.cs` hoặc test domain mới trong `HealthExam.Tests/Signing/SectionSigningProgressTests.cs`

**Interfaces:**
- Produces `ExamRecord.BeginExam(DateTime utcNow)` để chuyển `Waiting` → `InProgress` và chỉ set `ExamStartedAt` một lần.
- Produces `ExamRecordSignStepStatus.InProgress`.

- [ ] **Step 1: Viết test thất bại cho transition hồ sơ và status step**

```csharp
[Fact]
public void BeginExam_only_moves_waiting_record_and_stamps_once()
{
    var record = new ExamRecord { State = ExamRecordState.Waiting };
    var first = new DateTime(2026, 9, 18, 8, 0, 0, DateTimeKind.Utc);
    var second = first.AddMinutes(5);

    Assert.True(record.BeginExam(first).IsSuccess);
    Assert.Equal(ExamRecordState.InProgress, record.State);
    Assert.Equal(first, record.ExamStartedAt);

    Assert.True(record.BeginExam(second).IsSuccess);
    Assert.Equal(first, record.ExamStartedAt);
    Assert.Equal("InProgress", ExamRecordSignStepStatus.InProgress);
}
```

- [ ] **Step 2: Chạy test để xác nhận đang fail**

Run: `dotnet test HealthExam.Tests/HealthExam.Tests.csproj --filter FullyQualifiedName~SectionSigningProgressTests`

Expected: FAIL vì chưa có `BeginExam` và `InProgress`.

- [ ] **Step 3: Implement transition tối thiểu**

Thêm `BeginExam` vào `ExamRecord`: chỉ chấp nhận `Waiting` hoặc `InProgress`; với `Waiting` set `State = InProgress` và `ExamStartedAt ??= utcNow`; với state khác trả `InvalidTransition`. Thêm constant `InProgress = "InProgress"` vào `ExamRecordSignStepStatus`. Giữ cấu hình EF status length hiện tại, không tạo migration vì đây là giá trị chuỗi.

- [ ] **Step 4: Chạy test xác nhận pass**

Run: `dotnet test HealthExam.Tests/HealthExam.Tests.csproj --filter FullyQualifiedName~SectionSigningProgressTests`

Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add HealthExam.Domain/ExamRecords/ExamRecord.cs HealthExam.Domain/ExamRecords/ExamRecordSignStep.cs HealthExam.Tests/Signing/SectionSigningProgressTests.cs
git commit -m "feat: add in-progress state for exam signing steps"
```

### Task 2: Tạo calculator tiến độ ký dùng chung

**Files:**
- Create: `HealthExam.Application/Signing/SigningProgress.cs`
- Test: `HealthExam.Tests/Signing/SectionSigningProgressTests.cs`

**Interfaces:**
- Produces `SigningProgressCalculator.Calculate(IReadOnlyList<SignStepMap> mapSteps, IReadOnlyCollection<ExamRecordSignStep> snapshots)`.
- Produces `SigningProgress(int Done, int Total)`.

- [ ] **Step 1: Viết test cho tiến độ và loại trừ conclusion step**

```csharp
[Fact]
public void Calculate_counts_only_signed_clinical_steps()
{
    var maps = new[]
    {
        new SignStepMap { SWStep = 1, ItemGroupID = 101, IsConclusionStep = false },
        new SignStepMap { SWStep = 2, ItemGroupID = 102, IsConclusionStep = false },
        new SignStepMap { SWStep = 3, IsConclusionStep = true }
    };
    var snapshots = new[]
    {
        new ExamRecordSignStep { SWStep = 1, Status = ExamRecordSignStepStatus.Signed },
        new ExamRecordSignStep { SWStep = 2, Status = ExamRecordSignStepStatus.InProgress },
        new ExamRecordSignStep { SWStep = 3, Status = ExamRecordSignStepStatus.Signed }
    };

    var progress = SigningProgressCalculator.Calculate(maps, snapshots);
    Assert.Equal(1, progress.Done);
    Assert.Equal(2, progress.Total);
}
```

- [ ] **Step 2: Chạy test để xác nhận fail**

Run: `dotnet test HealthExam.Tests/HealthExam.Tests.csproj --filter FullyQualifiedName~SectionSigningProgressTests`

Expected: FAIL vì calculator chưa tồn tại.

- [ ] **Step 3: Implement calculator**

Lọc `IsConclusionStep == false`, đếm tổng step active và đếm các map có snapshot cùng `SWStep` với `Status == Signed`. Không dùng `ExamRecord.ProgressDone/ProgressTotal`.

- [ ] **Step 4: Chạy test pass**

Run: `dotnet test HealthExam.Tests/HealthExam.Tests.csproj --filter FullyQualifiedName~SectionSigningProgressTests`

- [ ] **Step 5: Commit**

```bash
git add HealthExam.Application/Signing/SigningProgress.cs HealthExam.Tests/Signing/SectionSigningProgressTests.cs
git commit -m "feat: calculate signed clinical step progress"
```

### Task 3: Lưu lần đầu M05 mở hồ sơ và tạo step InProgress

**Files:**
- Modify: `HealthExam.Application/His/SaveHisFormSection.cs`
- Modify: `HealthExam.Application/His/GetHisRecordSection.cs`
- Modify: `HealthExam.Application/His/HisModels.cs`
- Modify: `HealthExam.API/Controllers/ExamRecordHisFormController.cs`
- Test: `HealthExam.Tests/HisFormEndpointTests.cs` hoặc `HealthExam.Tests/Signing/SectionSigningProgressTests.cs`

**Interfaces:**
- Consumes `ISignStepMapRepository.ListAsync(divisionId, record.VariantCode, ct)`.
- Produces a local `ExamRecordSignStep` for the first non-conclusion map step with `Status = InProgress` after HIS save succeeds.
- Produces `HisRecordSectionResult.RecordState`, `CurrentStepStatus`, `SigningProgressDone`, and `SigningProgressTotal` so the save response can render the state without a second API call.

- [ ] **Step 1: Viết test lưu M05 thành công**

Test phải dựng record `Waiting`, map có step đầu non-conclusion và gọi handler với `ItemGroupId` của step đầu. Mock HIS save trả thành công. Assert:

```csharp
Assert.Equal(ExamRecordState.InProgress, record.State);
Assert.NotNull(record.ExamStartedAt);
var step = Assert.Single(record.SignSteps);
Assert.Equal(ExamRecordSignStepStatus.InProgress, step.Status);
Assert.Equal(firstMap.SWStep, step.SWStep);
```

Thêm test HIS save lỗi và assert record vẫn `Waiting`, không có `SignSteps`.

- [ ] **Step 2: Chạy test để xác nhận fail**

Run: `dotnet test HealthExam.Tests/HealthExam.Tests.csproj --filter FullyQualifiedName~SectionSigningProgressTests`

- [ ] **Step 3: Expose signing state in the section response**

Extend `HisRecordSectionResult` with `RecordState`, `CurrentStepStatus`, `CurrentStepSignedByEmployeeID`, `CurrentStepSignedAt`, `SigningProgressDone`, and `SigningProgressTotal`. Update `GetHisRecordSectionHandler` to load `ISignStepMapRepository`, find the current non-conclusion map step, read its snapshot status/actor/time, and calculate progress with `SigningProgressCalculator`. Update `ExamRecordHisFormController` mapping so GET section and POST save return these fields. The frontend binds `SigningProgressDone/Total` to the left process badge and binds the current-step fields to the selected row/header.

- [ ] **Step 4: Inject sign-step map và cập nhật local state sau HIS save**

Thêm `ISignStepMapRepository` vào constructor `SaveHisFormSectionHandler`. Sau đoạn HIS save thành công và trước `SaveChangesAsync`, lấy map theo `VariantCode`, chọn `!IsConclusionStep` có `SWStep` nhỏ nhất. Chỉ khi `command.ItemGroupId` trùng `ItemGroupID` của map đầu và chưa có snapshot cùng variant/step:

```csharp
if (record.State == ExamRecordState.Waiting)
    record.BeginExam(DateTime.UtcNow);

record.SignSteps ??= new List<ExamRecordSignStep>();
record.SignSteps.Add(new ExamRecordSignStep
{
    DivisionID = command.DivisionId,
    RecordID = record.RecordID,
    VariantCode = record.VariantCode,
    ItemGroupID = firstStep.ItemGroupID,
    SWStep = firstStep.SWStep,
    StepName = firstStep.StepName,
    SWRoleID = firstStep.SWRoleID,
    Status = ExamRecordSignStepStatus.InProgress,
    CreatedDate = DateTime.UtcNow,
    ModifiedDate = DateTime.UtcNow
});
```

Không tạo snapshot cho các section khác khi chỉ lưu; chỉ section đầu tiên mở process.

- [ ] **Step 5: Chạy test pass**

Run: `dotnet test HealthExam.Tests/HealthExam.Tests.csproj --filter FullyQualifiedName~SectionSigningProgressTests`

- [ ] **Step 6: Commit**

```bash
git add HealthExam.Application/His/SaveHisFormSection.cs HealthExam.Application/His/HisModels.cs HealthExam.API/Controllers/ExamRecordHisFormController.cs HealthExam.Tests/HisFormEndpointTests.cs HealthExam.Tests/Signing/SectionSigningProgressTests.cs
git commit -m "feat: start exam process when first clinical form is saved"
```

### Task 4: Ký section chuyển InProgress → Signed và trả 1/8

**Files:**
- Modify: `HealthExam.Application/Signing/SignExamSection.cs`
- Modify: `HealthExam.Application/Signing/SigningModels.cs`
- Modify: `HealthExam.API/Controllers/ExamRecordController.cs`
- Test: `HealthExam.Tests/Signing/SignExamSectionTests.cs` and `HealthExam.Tests/Signing/SignExamSectionEndpointTests.cs`

**Interfaces:**
- `ExamSectionSignResult` adds `Status`, `SigningProgressDone`, `SigningProgressTotal`.

- [ ] **Step 1: Viết test ký step đang InProgress**

Seed an `InProgress` step, call `SignExamSectionHandler`, then assert:

```csharp
Assert.Equal(ExamRecordSignStepStatus.Signed, result.Value.Status);
Assert.Equal(1, result.Value.SigningProgressDone);
Assert.Equal(8, result.Value.SigningProgressTotal);
Assert.Equal(ExamRecordSignStepStatus.Signed, db.RecordOf(record.RecordID).SignSteps.Single().Status);
```

Also assert a second sign does not create a duplicate step and returns the existing signed state/idempotent error according to the current endpoint contract.

- [ ] **Step 2: Chạy test fail**

Run: `dotnet test HealthExam.Tests/HealthExam.Tests.csproj --filter FullyQualifiedName~SignExamSection`

- [ ] **Step 3: Update handler**

Allow an existing snapshot with `Status == InProgress`; reject only `Signed`/`Snapshot` according to current edit-lock semantics. Set the step to `Signed`, stamp employee fields and `SignedAt`, calculate progress via `SigningProgressCalculator`, and return the new fields. Keep the existing row lock and transaction.

- [ ] **Step 4: Chạy test pass**

Run: `dotnet test HealthExam.Tests/HealthExam.Tests.csproj --filter FullyQualifiedName~SignExamSection`

- [ ] **Step 5: Commit**

```bash
git add HealthExam.Application/Signing/SignExamSection.cs HealthExam.Application/Signing/SigningModels.cs HealthExam.API/Controllers/ExamRecordController.cs HealthExam.Tests/Signing/SignExamSectionTests.cs HealthExam.Tests/Signing/SignExamSectionEndpointTests.cs
git commit -m "feat: mark clinical step signed and return signing progress"
```

### Task 5: Eligibility và ký kết luận chỉ nhận step Signed

**Files:**
- Modify: `HealthExam.Application/Paraclinical/GetConclusionEligibility.cs`
- Modify: `HealthExam.Application/Paraclinical/SignConclusion.cs`
- Test: `HealthExam.Tests/Signing/ConclusionEligibilityFromMapTests.cs`
- Test: `HealthExam.Tests/Signing/ConclusionSigningTests.cs`

**Interfaces:**
- Consumes `ExamRecordSignStep.Status` as the source of truth; `InProgress` is visible but does not satisfy condition A or `MissingSteps`.

- [ ] **Step 1: Viết regression tests**

Seed a clinical step with `Status = InProgress`; assert eligibility returns `CanSignConclusion = false`, condition A detail still reports the step as missing, and `MissingSteps` contains its `SWStep`. Seed `Signed` and assert it is counted.

- [ ] **Step 2: Chạy test fail**

Run: `dotnet test HealthExam.Tests/HealthExam.Tests.csproj --filter FullyQualifiedName~ConclusionEligibilityFromMapTests|FullyQualifiedName~ConclusionSigningTests`

- [ ] **Step 3: Implement status-aware checks**

Change all checks that currently test snapshot existence to require `Status == ExamRecordSignStepStatus.Signed` for clinical steps. Keep the conclusion snapshot lifecycle (`Snapshot` during PDF signing, then `Signed` after upload) unchanged. The final PDF loop may only run after all clinical snapshots are signed.

- [ ] **Step 4: Chạy test pass**

Run: `dotnet test HealthExam.Tests/HealthExam.Tests.csproj --filter FullyQualifiedName~ConclusionEligibilityFromMapTests|FullyQualifiedName~ConclusionSigningTests`

- [ ] **Step 5: Commit**

```bash
git add HealthExam.Application/Paraclinical/GetConclusionEligibility.cs HealthExam.Application/Paraclinical/SignConclusion.cs HealthExam.Tests/Signing/ConclusionEligibilityFromMapTests.cs HealthExam.Tests/Signing/ConclusionSigningTests.cs
git commit -m "fix: require signed clinical steps before conclusion signing"
```

### Task 6: API contract, docs và verification

**Files:**
- Modify: `docs/api/his-section-signing-api.md`
- Modify: `docs/api/conclusion-pdf-signing-guide.md`
- Modify: `HealthExam.Tests/API/ApiContractSurfaceTests.cs` only if response schema coverage requires it

- [ ] **Step 1: Document the state sequence**

Document exactly:

```text
Waiting --save first clinical form--> InProgress
step InProgress --section sign--> Signed
sign progress: 0/8 -> 1/8 -> ... -> 8/8
all clinical steps Signed --conclusion sign--> PDF Signed
```

Document that `ExamRecord.ProgressDone/ProgressTotal` remains webhook progress; signing progress comes from section-sign response and eligibility `Steps`.

Document the UI acceptance examples:

```text
POST save first M05 -> RecordState=InProgress, CurrentStepStatus=InProgress,
                      SigningProgressDone=0, SigningProgressTotal=8
POST sign M05        -> CurrentStepStatus=Signed, SigningProgressDone=1,
                      SigningProgressTotal=8, CurrentStepSignedAt != null
```

The process list badge in the screen shown by the product requirement must render `SigningProgressDone/SigningProgressTotal`; it must not derive the value from webhook `ProgressDone/ProgressTotal`.

- [ ] **Step 2: Run build and focused tests**

```bash
dotnet build HealthExamServer.sln --no-restore
dotnet test HealthExam.Tests/HealthExam.Tests.csproj --no-build --filter "FullyQualifiedName~SectionSigningProgressTests|FullyQualifiedName~SignExamSection|FullyQualifiedName~ConclusionEligibilityFromMapTests|FullyQualifiedName~ConclusionSigningTests"
```

Expected: build succeeds and all focused tests pass.

- [ ] **Step 3: Run diff checks**

```bash
git diff --check
git status --short
```

- [ ] **Step 4: Commit**

```bash
git add docs/api/his-section-signing-api.md docs/api/conclusion-pdf-signing-guide.md HealthExam.Tests/API/ApiContractSurfaceTests.cs
git commit -m "docs: describe clinical signing progress state"
```

## Self-Review

- M05 is identified through the first non-conclusion `SignStepMap`, not a literal item group or step number.
- `InProgress` never satisfies conclusion eligibility; only `Signed` advances `Done`.
- Existing webhook clinical progress fields are not reused for signing progress.
- Existing PDF rendering/signing remains exclusively in `SignConclusionHandler`.
- HIS failures occur before local state transition and leave the record unchanged.
