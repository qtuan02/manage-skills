# HIS Admission Link Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Link each manually created KSK record to an idempotent HIS admission without changing `his-server`, and let staff retry a pending link.

**Architecture:** `IHisEmrApi` gains one HTTP wire operation for the deployed HIS `CUAdmission` route. `IHisAdmissionService` owns mapping and persistence; `ExamRecordService` attempts it only after local manual registration commits, while the retry controller invokes the same operation explicitly.

**Tech Stack:** .NET 8, ASP.NET Core, EF Core, `HttpClientFactory`, Newtonsoft.Json `JToken`, xUnit.

**Spec:** `docs/superpowers/specs/2026-09-09-his-admission-link-design.md`

## Global Constraints

- Do not modify any path under `../his-server`; use its deployed `POST /api/M02F00000/GetAdmissionInfo` route exactly.
- Do not add automatic HTTP retries; `POST /v1/exam-records/{recordId}/his-admission` is the retry mechanism.
- Commit the local manual record before the HIS mutation; a `HealthExamException` during automatic linking returns its local record as `Pending`.
- Excel import `CreateAsync(..., importBatchId)` must never call HIS.
- Preserve the nine pre-existing dirty KSK-form files. Stage only the files named in each task.
- Retain the existing valid positive `AdmissionID` write contract. A pre-linked record must not make another HIS call.
- Match HIS `AdmissionRequest` JSON property names exactly and never log bearer tokens or admission payload PII.

---

## File structure

| Path | Responsibility |
| --- | --- |
| `HealthExam.Server/Models/HisEmrModels.cs` | JSON-annotated `HisAdmissionWireRequest`. |
| `HealthExam.Server/Service/IHisEmrApi.cs` | Transport port's admission method. |
| `HealthExam.Server/Service/HisEmrHttpClient.cs` | Exact deployed route and non-retrying POST. |
| `HealthExam.Server/Service/HisEmrOptions.cs` | Positive KSK department fallback from env. |
| `HealthExam.Server/Service/IHisAdmissionService.cs` | Application boundary for idempotent admission linking. |
| `HealthExam.Server/Service/HisAdmissionService.cs` | Tenant-safe load, map, validate, persist. |
| `HealthExam.Server/Service/ExamRecordService.cs` | Manual patient-code generation, best-effort linking, link state. |
| `HealthExam.Server/Service/DependencyInjection.cs` | Admission-service registration. |
| `HealthExam.API/Controllers/ExamRecordController.cs` | Retry endpoint. |
| `HealthExam.Tests/FakeHisEmrApi.cs` | Admission fake/captured payload/failure. |
| `HealthExam.Tests/HisAdmissionServiceTests.cs` | Mapping, persistence, validation, ownership, idempotency. |
| `HealthExam.Tests/HisAdmissionEndpointTests.cs` | Endpoint and DI contract. |
| `.env.example`, `README.md` | Deployment configuration. |

### Task 1: Generate manual patient codes before the HIS call

**Files:**
- Modify: `HealthExam.Server/Service/ExamRecordService.cs:145-225,594-664`
- Test: `HealthExam.Tests/ExamRecordDefaultSessionTests.cs`

**Interfaces:**
- Consumes: `ComposeGeneratedPatientCode(string recordCode)`.
- Produces: persisted `PatientCode = "HEX-{RecordCode}"` for a phase-one manual create that omits it.

- [ ] **Step 1: Write the failing test**

```csharp
[Fact]
public async Task Create_for_phase_one_without_patient_code_generates_a_stable_patient_code()
{
    using var db = new InMemoryTestDb();

    var created = await db.Records.CreateInDefaultSessionAsync(new ExamRecordWriteRequest
    {
        FullName = "Nguyễn Văn A",
        VariantCode = "DTK_01"
    });

    Assert.Equal("PHASE1-DEFAULT-0001", created.RecordCode);
    Assert.Equal("HEX-PHASE1-DEFAULT-0001", created.PatientCode);
    Assert.Equal(created.PatientCode, db.RecordOf(created.RecordID).PatientCode);
}
```

- [ ] **Step 2: Verify RED**

Run:

```bash
DOTNET_ROLL_FORWARD=Major dotnet test HealthExam.Tests/HealthExam.Tests.csproj --no-restore --filter FullyQualifiedName~Create_for_phase_one_without_patient_code_generates_a_stable_patient_code
```

Expected: FAIL because phase-one records currently persist an empty `PatientCode`.

- [ ] **Step 3: Implement the narrow policy**

Pass an explicit `generatePatientCodeWhenMissing` flag to `CreateCoreAsync`: `true` from `CreateInDefaultSessionAsync`; `importBatchId.HasValue` from `CreateAsync`. Replace the current assignment with:

```csharp
var autoPatientCode = generatePatientCodeWhenMissing &&
                      string.IsNullOrWhiteSpace(entity.PatientCode);
```

Keep existing allocation and `FillGeneratedPatientCode` calls. Update the three XML comments around `GeneratedPatientCodePrefix` to say they cover Excel imports and phase-one manual registration.

- [ ] **Step 4: Verify GREEN**

Run:

```bash
DOTNET_ROLL_FORWARD=Major dotnet test HealthExam.Tests/HealthExam.Tests.csproj --no-restore --filter "FullyQualifiedName~ExamRecordDefaultSessionTests|FullyQualifiedName~ExamImportTests|FullyQualifiedName~PortalCredentialTests"
```

Expected: PASS; imports preserve their old rule.

- [ ] **Step 5: Commit this deliverable**

```bash
git add HealthExam.Server/Service/ExamRecordService.cs HealthExam.Tests/ExamRecordDefaultSessionTests.cs
git commit -m "feat: generate patient code for manual registration"
```

### Task 2: Add the HIS admission transport contract

**Files:**
- Modify: `HealthExam.Server/Models/HisEmrModels.cs`
- Modify: `HealthExam.Server/Service/IHisEmrApi.cs`
- Modify: `HealthExam.Server/Service/HisEmrHttpClient.cs:25-105`
- Modify: `HealthExam.Tests/FakeHisEmrApi.cs`
- Test: `HealthExam.Tests/HisEmrHttpClientTests.cs`

**Interfaces:**
- Produces: `Task<JToken> CreateAdmissionAsync(HisAdmissionWireRequest request, CancellationToken ct = default)`.
- Consumes: existing `PostAsync`, credential forwarding, and HIS envelope parsing.

- [ ] **Step 1: Write failing wire-contract tests**

```csharp
[Fact]
public async Task CreateAdmissionAsync_calls_deployed_post_route_and_exact_payload()
{
    var (client, handler, _) = CreateClient(response:
        JsonResponse(new { ErrorCode = 0, Message = "", Data = 7001001 }));
    var request = new HisAdmissionWireRequest
    {
        AdmissionCode = "HEX-00000000000000000000000000000001",
        AdmissionDate = new DateTime(2026, 9, 9),
        DepartmentID = 12, IsOutPatient = 2,
        PatientCode = "HEX-PHASE1-DEFAULT-0001",
        FirstName = "A", LastName = "Nguyễn Văn", FullName = "Nguyễn Văn A",
        I_Gender = 1, BirthYear = 1990, IDCard = "012345678901"
    };

    var data = await client.CreateAdmissionAsync(request, CancellationToken.None);

    var call = Assert.Single(handler.Calls);
    Assert.Equal(HttpMethod.Post, call.Method);
    Assert.Equal("https://his.test/api/M02F00000/GetAdmissionInfo", call.Url);
    Assert.Equal("Bearer test-his-token", call.Headers["Authorization"]);
    Assert.Equal(7001001L, data.Value<long>());
    var body = JObject.Parse(call.Body);
    Assert.Equal(2, body["IsOutPatient"]!.Value<int>());
    Assert.Equal("HEX-PHASE1-DEFAULT-0001", body["PatientCode"]!.Value<string>());
    Assert.Equal("Nguyễn Văn", body["LastName"]!.Value<string>());
    Assert.Null(body["MedicalRecordNo"]);
}

[Fact]
public async Task CreateAdmissionAsync_does_not_retry_a_connection_failure()
{
    var handler = new ThrowingHandler(new HttpRequestException("Connection reset"));
    var context = new FakeHealthExamContext();
    context.Headers["Authorization"] = "Bearer token";
    var client = new HisEmrHttpClient(new HttpClient(handler), DefaultOptions, context,
        NullLogger<HisEmrHttpClient>.Instance);

    await Assert.ThrowsAsync<HealthExamException>(() =>
        client.CreateAdmissionAsync(new HisAdmissionWireRequest { AdmissionCode = "HEX-1" }));

    Assert.Equal(1, handler.Attempts);
}
```

- [ ] **Step 2: Verify RED**

Run:

```bash
DOTNET_ROLL_FORWARD=Major dotnet test HealthExam.Tests/HealthExam.Tests.csproj --no-restore --filter FullyQualifiedName~HisEmrHttpClientTests
```

Expected: compilation fails because the DTO and method do not exist.

- [ ] **Step 3: Add DTO, port, client, and fake**

In `HisEmrModels.cs`, add an init-only DTO with exactly these JSON properties:

```csharp
[JsonProperty("IsOutPatient")] public byte IsOutPatient { get; init; } = 2;
[JsonProperty("AdmissionCode")] public string AdmissionCode { get; init; } = "";
[JsonProperty("AdmissionDate")] public DateTime AdmissionDate { get; init; }
[JsonProperty("DepartmentID")] public int DepartmentID { get; init; }
[JsonProperty("PatientCode")] public string PatientCode { get; init; } = "";
[JsonProperty("FirstName")] public string FirstName { get; init; } = "";
[JsonProperty("LastName")] public string LastName { get; init; } = "";
[JsonProperty("FullName")] public string FullName { get; init; } = "";
[JsonProperty("I_Gender")] public byte I_Gender { get; init; }
[JsonProperty("BirthDate")] public DateTime? BirthDate { get; init; }
[JsonProperty("BirthYear")] public int BirthYear { get; init; }
[JsonProperty("IDCard")] public string IDCard { get; init; } = "";
[JsonProperty("MobileNo")] public string MobileNo { get; init; } = "";
[JsonProperty("PersonalEmail")] public string PersonalEmail { get; init; } = "";
[JsonProperty("CurrentAddress")] public string CurrentAddress { get; init; } = "";
```

Add this exact adapter method and port signature:

```csharp
public const string RouteCUAdmission = "api/M02F00000/GetAdmissionInfo";

public Task<JToken> CreateAdmissionAsync(
    HisAdmissionWireRequest request, CancellationToken ct = default)
    => PostAsync(RouteCUAdmission, request, "CUAdmission", ct);
```

Extend `FakeHisEmrApi` with `AdmissionCalls`, `LastAdmission`, `AdmissionData = new JValue(7001001L)`, and `AdmissionException`; increment/capture/throw-or-return in `CreateAdmissionAsync`.

- [ ] **Step 4: Verify GREEN**

Run:

```bash
DOTNET_ROLL_FORWARD=Major dotnet test HealthExam.Tests/HealthExam.Tests.csproj --no-restore --filter "FullyQualifiedName~HisEmrHttpClientTests|FullyQualifiedName~HisExamFormGateway"
```

Expected: PASS, proving form callers still compile and the new mutation does not retry.

- [ ] **Step 5: Commit this deliverable**

```bash
git add HealthExam.Server/Models/HisEmrModels.cs HealthExam.Server/Service/IHisEmrApi.cs HealthExam.Server/Service/HisEmrHttpClient.cs HealthExam.Tests/FakeHisEmrApi.cs HealthExam.Tests/HisEmrHttpClientTests.cs
git commit -m "feat: add HIS admission transport operation"
```

### Task 3: Implement the admission-link application service

**Files:**
- Create: `HealthExam.Server/Service/IHisAdmissionService.cs`
- Create: `HealthExam.Server/Service/HisAdmissionService.cs`
- Modify: `HealthExam.Server/Service/HisEmrOptions.cs`
- Test: `HealthExam.Tests/HisAdmissionServiceTests.cs`

**Interfaces:**
- Consumes: `IHisEmrApi.CreateAdmissionAsync`, `IUnitOfWork`, `IHealthExamContext`, `HisEmrOptions`.
- Produces: `Task EnsureAdmissionAsync(Guid recordId, CancellationToken ct = default)`.

- [ ] **Step 1: Write failing service tests**

```csharp
[Fact]
public async Task EnsureAdmission_maps_KSK_data_and_persists_a_positive_his_id()
{
    using var db = new InMemoryTestDb();
    var session = db.SeedSession(examDate: new DateOnly(2026, 9, 9));
    var record = db.SeedRecord(session.SessionID, patientCode: "PAT-001",
        fullName: "Nguyễn Văn An", genderId: 1);
    var row = db.Db.ExamRecords.Single(x => x.RecordID == record.RecordID);
    row.Dob = new DateOnly(1990, 1, 2);
    row.IdentityNumber = "012345678901";
    row.PhoneNumber = "0901000001";
    row.Email = "an@example.test";
    row.Address = "Hà Nội";
    await db.Db.SaveChangesAsync();
    db.Db.ChangeTracker.Clear();
    var his = new FakeHisEmrApi { AdmissionData = new JValue(9000123L) };

    await CreateService(db, his, departmentId: 12).EnsureAdmissionAsync(record.RecordID);

    Assert.Equal(9000123L, db.RecordOf(record.RecordID).AdmissionID);
    Assert.Equal($"HEX-{record.RecordID:N}", his.LastAdmission!.AdmissionCode);
    Assert.Equal(2, his.LastAdmission.IsOutPatient);
    Assert.Equal("An", his.LastAdmission.FirstName);
    Assert.Equal("Nguyễn Văn", his.LastAdmission.LastName);
    Assert.Equal(new DateTime(2026, 9, 9), his.LastAdmission.AdmissionDate);
    Assert.Equal(12, his.LastAdmission.DepartmentID);
}

[Fact]
public async Task EnsureAdmission_skips_his_when_record_is_already_linked()
{
    using var db = new InMemoryTestDb();
    var session = db.SeedSession();
    var record = db.SeedRecord(session.SessionID, admissionId: 9000123);
    var his = new FakeHisEmrApi();

    await CreateService(db, his, departmentId: 12).EnsureAdmissionAsync(record.RecordID);

    Assert.Equal(0, his.AdmissionCalls);
}
```

Also test `null`, `0`, `-1`, `1.5`, and a JSON string response all throw `HisBadGateway` and leave `AdmissionID` null; a no-department case throws `VendorNotConfigured` without calling HIS; a record in another division returns `NotFound`.

```csharp
[Theory]
[InlineData("null")]
[InlineData("0")]
[InlineData("-1")]
[InlineData("1.5")]
[InlineData("\"9000123\"")]
public async Task EnsureAdmission_rejects_invalid_his_data(string json)
{
    using var db = new InMemoryTestDb();
    var session = db.SeedSession();
    var record = db.SeedRecord(session.SessionID, patientCode: "PAT-001");
    var his = new FakeHisEmrApi { AdmissionData = JToken.Parse(json) };

    var ex = await Assert.ThrowsAsync<HealthExamException>(() =>
        CreateService(db, his, departmentId: 12).EnsureAdmissionAsync(record.RecordID));

    Assert.Equal(ErrorCodes.HisBadGateway, ex.ErrorCode);
    Assert.Null(db.RecordOf(record.RecordID).AdmissionID);
}

private static HisAdmissionService CreateService(
    InMemoryTestDb db, FakeHisEmrApi his, int? departmentId)
    => new(db.Uow, db.Ctx, his, new HisEmrOptions
    {
        Enabled = true,
        BaseUrl = "https://his.test",
        KskDepartmentId = departmentId
    }, NullLogger<HisAdmissionService>.Instance);
```

- [ ] **Step 2: Verify RED**

Run:

```bash
DOTNET_ROLL_FORWARD=Major dotnet test HealthExam.Tests/HealthExam.Tests.csproj --no-restore --filter FullyQualifiedName~HisAdmissionServiceTests
```

Expected: compilation fails because the application service is absent.

- [ ] **Step 3: Implement the service and option**

Create:

```csharp
public interface IHisAdmissionService
{
    Task EnsureAdmissionAsync(Guid recordId, CancellationToken ct = default);
}
```

`HisAdmissionService` constructor takes `IUnitOfWork`, `IHealthExamContext`, `IHisEmrApi`, `HisEmrOptions`, and `ILogger<HisAdmissionService>`. Query tenant-owned `ExamRecord` with `.Include(x => x.Session).AsTracking()`; return immediately if `AdmissionID > 0`. Choose a positive `Session.DepartmentID`, otherwise positive `options.KskDepartmentId`, otherwise throw:

```csharp
throw HealthExamException.VendorNotConfigured(
    "Chưa cấu hình khoa KSK HIS cho lượt tiếp nhận");
```

Map from persisted values:

```csharp
new HisAdmissionWireRequest
{
    AdmissionCode = $"HEX-{record.RecordID:N}",
    AdmissionDate = session.ExamDate.ToDateTime(TimeOnly.MinValue),
    DepartmentID = departmentId, IsOutPatient = 2,
    PatientCode = record.PatientCode,
    FirstName = name.FirstName, LastName = name.LastName, FullName = record.FullName,
    I_Gender = checked((byte)record.GenderID),
    BirthDate = record.Dob?.ToDateTime(TimeOnly.MinValue),
    BirthYear = record.BirthYear ?? record.Dob?.Year ?? 0,
    IDCard = record.IdentityNumber, MobileNo = record.PhoneNumber,
    PersonalEmail = record.Email, CurrentAddress = record.Address
};
```

Use a private splitter that removes empty whitespace tokens, makes the last token `FirstName`, and joins preceding tokens into `LastName`. Accept only `JTokenType.Integer` and a `long > 0`; persist it with `_uow.SaveChangesAsync(ct)`. Add `KskDepartmentIdEnv = "HIS_EMR_KSK_DEPARTMENT_ID"` and nullable `KskDepartmentId`; parse only a positive integer in `FromEnvironment`.

- [ ] **Step 4: Verify GREEN**

Run:

```bash
DOTNET_ROLL_FORWARD=Major dotnet test HealthExam.Tests/HealthExam.Tests.csproj --no-restore --filter "FullyQualifiedName~HisAdmissionServiceTests|FullyQualifiedName~HisAdmissionLinkTests"
```

Expected: PASS; malformed HIS data cannot change local admission linkage.

- [ ] **Step 5: Commit this deliverable**

```bash
git add HealthExam.Server/Service/IHisAdmissionService.cs HealthExam.Server/Service/HisAdmissionService.cs HealthExam.Server/Service/HisEmrOptions.cs HealthExam.Tests/HisAdmissionServiceTests.cs
git commit -m "feat: link health exam records to HIS admissions"
```

### Task 4: Link manual registration best-effort and return its state

**Files:**
- Modify: `HealthExam.Server/Models/ExamRecordModels.cs`
- Modify: `HealthExam.Server/Service/ExamRecordService.cs:16-40,145-150,829-905`
- Modify: `HealthExam.Server/Service/DependencyInjection.cs:10-85`
- Test: `HealthExam.Tests/HisAdmissionServiceTests.cs`
- Test: `HealthExam.Tests/HisAdmissionLinkTests.cs`

**Interfaces:**
- Consumes: `IHisAdmissionService.EnsureAdmissionAsync`.
- Produces: `ExamRecordItem.HisAdmissionLinkStatus` set to `Linked` or `Pending`.

- [ ] **Step 1: Write failing automatic-link tests**

```csharp
[Fact]
public async Task Manual_create_links_after_local_persistence_and_returns_linked()
{
    using var db = new InMemoryTestDb();
    var his = new FakeHisEmrApi { AdmissionData = new JValue(9000123L) };
    var admissions = CreateService(db, his, departmentId: 12);
    var records = new ExamRecordService(db.Uow, db.Ctx, db.Sessions, db.MasterData,
        db.Audit, NullLogger<ExamRecordService>.Instance, admissions);

    var created = await records.CreateInDefaultSessionAsync(new ExamRecordWriteRequest
    {
        FullName = "Nguyễn Văn A", VariantCode = "DTK_01"
    });

    Assert.Equal(9000123L, created.AdmissionID);
    Assert.Equal("Linked", created.HisAdmissionLinkStatus);
    Assert.Equal(1, his.AdmissionCalls);
}

[Fact]
public async Task Manual_create_keeps_committed_record_pending_when_his_fails()
{
    using var db = new InMemoryTestDb();
    var his = new FakeHisEmrApi
    {
        AdmissionException = new HealthExamException(ErrorCodes.HisBadGateway, "HIS unavailable")
    };
    var admissions = CreateService(db, his, departmentId: 12);
    var records = new ExamRecordService(db.Uow, db.Ctx, db.Sessions, db.MasterData,
        db.Audit, NullLogger<ExamRecordService>.Instance, admissions);

    var created = await records.CreateInDefaultSessionAsync(new ExamRecordWriteRequest
    {
        FullName = "Nguyễn Văn A", VariantCode = "DTK_01"
    });

    Assert.Null(created.AdmissionID);
    Assert.Equal("Pending", created.HisAdmissionLinkStatus);
    Assert.NotNull(db.RecordOf(created.RecordID));
    Assert.Equal(1, his.AdmissionCalls);
}

[Fact]
public async Task Record_item_exposes_pending_when_it_has_no_admission()
{
    using var db = new InMemoryTestDb();
    var session = db.SeedSession();
    var record = db.SeedRecord(session.SessionID);

    var item = await db.Records.GetAsync(record.RecordID);

    Assert.Equal("Pending", item.HisAdmissionLinkStatus);
}
```

- [ ] **Step 2: Verify RED**

Run:

```bash
DOTNET_ROLL_FORWARD=Major dotnet test HealthExam.Tests/HealthExam.Tests.csproj --no-restore --filter "FullyQualifiedName~Manual_create_links_after_local_persistence|FullyQualifiedName~Manual_create_keeps_committed_record_pending"
```

Expected: compilation fails because record creation has no admission dependency or link state.

- [ ] **Step 3: Implement the post-persistence link**

Append optional `IHisAdmissionService hisAdmissions = null` after the existing optional logger in the `ExamRecordService` constructor and assign it. In `CreateInDefaultSessionAsync`, first call the existing core creation. If no service exists or the returned ID is positive, return it. Otherwise:

```csharp
try
{
    await _hisAdmissions.EnsureAdmissionAsync(created.RecordID, ct);
    return await GetAsync(created.RecordID, ct);
}
catch (HealthExamException ex)
{
    _logger?.LogWarning(ex,
        "HIS admission pending for record {RecordId} (Division: {Division}, TraceId: {TraceId}, ErrorCode: {ErrorCode})",
        created.RecordID, _ctx.DivisionId, _ctx.TraceId, ex.ErrorCode);
    return created;
}
```

Do not catch `OperationCanceledException`. Add `HisAdmissionLinkStatus` to `ExamRecordItem`; have `ToItem` set it to `"Linked"` only for positive `AdmissionID`, otherwise `"Pending"`. Register `IHisAdmissionService` as scoped.

- [ ] **Step 4: Verify GREEN**

Run:

```bash
DOTNET_ROLL_FORWARD=Major dotnet test HealthExam.Tests/HealthExam.Tests.csproj --no-restore --filter "FullyQualifiedName~ExamRecordDefaultSessionTests|FullyQualifiedName~HisAdmissionServiceTests|FullyQualifiedName~HisAdmissionLinkTests"
```

Expected: PASS; direct existing positive `AdmissionID` behavior remains intact.

- [ ] **Step 5: Commit this deliverable**

```bash
git add HealthExam.Server/Models/ExamRecordModels.cs HealthExam.Server/Service/ExamRecordService.cs HealthExam.Server/Service/DependencyInjection.cs HealthExam.Tests/HisAdmissionServiceTests.cs HealthExam.Tests/HisAdmissionLinkTests.cs
git commit -m "feat: link manual registration to HIS admission"
```

### Task 5: Add the idempotent retry endpoint and documentation

**Files:**
- Modify: `HealthExam.API/Controllers/ExamRecordController.cs:21-105`
- Modify: `.env.example:17-21`
- Modify: `README.md:56-60,207-221`
- Test: `HealthExam.Tests/HisAdmissionEndpointTests.cs`

**Interfaces:**
- Consumes: `IHisAdmissionService.EnsureAdmissionAsync` and `ExamRecordService.GetAsync`.
- Produces: `POST /v1/exam-records/{recordId}/his-admission` returning `ResultData<ExamRecordItem>`.

- [ ] **Step 1: Write failing endpoint/DI tests**

```csharp
[Fact]
public void Retry_endpoint_defines_the_exact_route_and_result_contract()
{
    var method = typeof(ExamRecordController).GetMethod(nameof(ExamRecordController.EnsureHisAdmission));
    var post = method!.GetCustomAttribute<HttpPostAttribute>();

    Assert.Equal("{recordId:guid}/his-admission", post!.Template);
    Assert.Equal(typeof(Guid), method.GetParameters()[0].ParameterType);
    var actionResult = method.ReturnType.GenericTypeArguments[0];
    Assert.Equal(typeof(ActionResult<>), actionResult.GetGenericTypeDefinition());
    Assert.Equal(typeof(ResultData<ExamRecordItem>), actionResult.GenericTypeArguments[0]);
}

[Fact]
public async Task DI_resolves_the_his_admission_application_service()
{
    await using var scope = _host.Services.CreateAsyncScope();
    Assert.IsType<HisAdmissionService>(
        scope.ServiceProvider.GetRequiredService<IHisAdmissionService>());
}
```

- [ ] **Step 2: Verify RED**

Run:

```bash
DOTNET_ROLL_FORWARD=Major dotnet test HealthExam.Tests/HealthExam.Tests.csproj --no-restore --filter FullyQualifiedName~HisAdmissionEndpointTests
```

Expected: compilation fails because `EnsureHisAdmission` does not exist.

- [ ] **Step 3: Implement endpoint and docs**

Inject `IHisAdmissionService` in `ExamRecordController` and add:

```csharp
[HttpPost("{recordId:guid}/his-admission")]
[SwaggerOperation(
    Summary = "Tạo hoặc liên kết lượt tiếp nhận HIS cho hồ sơ",
    Description = "Tạo/cập nhật lượt tiếp nhận HIS khi hồ sơ chưa có AdmissionID. Gọi lại an toàn: hồ sơ đã liên kết không gọi HIS lần nữa.")]
public async Task<ActionResult<ResultData<ExamRecordItem>>> EnsureHisAdmission(
    Guid recordId, CancellationToken ct = default)
{
    await _hisAdmissions.EnsureAdmissionAsync(recordId, ct);
    return Success(await _service.GetAsync(recordId, ct));
}
```

Add `HIS_EMR_KSK_DEPARTMENT_ID=0` beside existing HIS settings in `.env.example`. In README, state it is a positive HIS department fallback used only when `ExamSession.DepartmentID` is zero; missing/zero/invalid keeps automatic linkage pending and makes retry return configuration error. State that admission creation never retries automatically.

- [ ] **Step 4: Verify GREEN**

Run:

```bash
DOTNET_ROLL_FORWARD=Major dotnet test HealthExam.Tests/HealthExam.Tests.csproj --no-restore --filter "FullyQualifiedName~HisAdmissionEndpointTests|FullyQualifiedName~SwaggerBusinessDocumentationTests|FullyQualifiedName~ExamRecordCreateContractTests"
```

Expected: PASS; the action is documented and creates no body contract.

- [ ] **Step 5: Commit this deliverable**

```bash
git add HealthExam.API/Controllers/ExamRecordController.cs HealthExam.Tests/HisAdmissionEndpointTests.cs .env.example README.md docs/superpowers/plans/2026-09-09-his-admission-link.md
git commit -m "feat: add HIS admission retry endpoint"
```

### Task 6: Full verification and graph review

**Files:**
- Verify: all files above.

**Interfaces:**
- Verifies: public registration and retry behavior without a source change in `his-server`.

- [ ] **Step 1: Run format/diff safety checks**

```bash
git diff --check
git status --short
```

Expected: no whitespace errors; only intended admission files plus known pre-existing KSK-form worktree files are modified.

- [ ] **Step 2: Run all tests**

```bash
DOTNET_ROLL_FORWARD=Major dotnet test HealthExam.Tests/HealthExam.Tests.csproj --no-restore
```

Expected: all tests pass.

- [ ] **Step 3: Run required GitNexus analysis**

```bash
node .gitnexus/run.cjs impact "IHisAdmissionService" --direction upstream --repo .
node .gitnexus/run.cjs impact "ExamRecordService" --direction upstream --repo .
node .gitnexus/run.cjs detect-changes --scope all --repo .
```

Expected: report the known critical KSK-form changes separately; inspect admission callers and do not stage, revert, or claim ownership of form edits.

- [ ] **Step 4: Publish the exact verification outcome**

Report the passed-test count, admission-only commit hashes, remaining unrelated dirty files, and the production value that a default phase-one session needs:

```dotenv
HIS_EMR_KSK_DEPARTMENT_ID=<positive HIS department ID>
```
