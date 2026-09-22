# Health-exam PDF Preview Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a tenant-scoped `health-exam-server` endpoint that renders the latest saved HIS EMR snapshot as an inline PDF without submitting or signing the form.

**Architecture:** Extend the existing `IHisEmrApi` port with a binary PDF operation backed by HIS `M03F10010/VEMR`. Add a preview method to the existing registration-form service, which loads `ExamRecord.HisEmrDataID` under the current division and returns the PDF bytes to a file response from `ExamRecordRegistrationFormController`.

**Tech Stack:** .NET 8, ASP.NET Core MVC, EF Core, `HttpClient`, Newtonsoft.Json, xUnit, ASP.NET `TestHost`.

**Spec:** `docs/superpowers/specs/2026-09-10-health-exam-pdf-preview-design.md`

## Global Constraints

- Preview only reflects the saved HIS EMR snapshot; it never consumes unsaved browser values.
- Preview must not call `M02F01500/SubmitEMR` or `SignEMR` and must not create or update a sign workflow.
- The success response is raw `application/pdf`, not a JSON `ResultData<byte[]>` envelope.
- Record lookup must enforce both `RecordID` and the current `DivisionID`.
- HIS credentials and `X-Trace-Id`/`X-Division-Id` forwarding must use the existing request context behavior.
- Do not add PDF caching, change HIS `VEMR`, change Report Server, or add frontend code in this plan.
- The new HIS preview call must not retry automatically.

---

### Task 1: Add a binary PDF operation to the HIS adapter

**Files:**
- Modify: `HealthExam.Server/Service/IHisEmrApi.cs`
- Modify: `HealthExam.Server/Service/HisEmrHttpClient.cs`
- Modify: `HealthExam.Tests/FakeHisEmrApi.cs`
- Test: `HealthExam.Tests/HisEmrHttpClientTests.cs`

**Interfaces:**
- Consumes: Existing `HisEmrOptions`, `IHealthExamContext`, `BuildUri`, credential validation, and HIS error conventions.
- Produces: `Task<byte[]> RenderFormPdfAsync(Guid emrDataId, CancellationToken ct = default)` on `IHisEmrApi` and `HisEmrHttpClient`.

- [ ] **Step 1: Write failing adapter tests for the binary contract.**

Add tests to `HisEmrHttpClientTests` using the existing `RecordingHandler` and
`CreateClient` helpers:

```csharp
[Fact]
public async Task RenderFormPdfAsync_calls_vemr_with_binary_response_and_forwards_headers()
{
    var emrDataId = Guid.NewGuid();
    var pdf = Encoding.ASCII.GetBytes("%PDF-1.7\npreview");
    var (client, handler, _) = CreateClient(response: new HttpResponseMessage(HttpStatusCode.OK)
    {
        Content = new ByteArrayContent(pdf)
    });

    var result = await client.RenderFormPdfAsync(emrDataId, CancellationToken.None);

    Assert.Equal(pdf, result);
    var call = Assert.Single(handler.Calls);
    Assert.Equal(HttpMethod.Get, call.Method);
    Assert.Equal(
        $"https://his.test/api/M03F10010/VEMR?EMRDataID={emrDataId}&IsJson=false",
        call.Url);
    Assert.Equal("Bearer test-his-token", call.Headers["Authorization"]);
    Assert.Equal("TRACE-HIS-1", call.Headers["X-Trace-Id"]);
    Assert.Equal("DEV", call.Headers["X-Division-Id"]);
}

[Fact]
public async Task RenderFormPdfAsync_rejects_an_empty_success_body()
{
    var (client, _, _) = CreateClient(response: new HttpResponseMessage(HttpStatusCode.OK)
    {
        Content = new ByteArrayContent(Array.Empty<byte>())
    });

    var ex = await Assert.ThrowsAsync<HealthExamException>(() =>
        client.RenderFormPdfAsync(Guid.NewGuid(), CancellationToken.None));

    Assert.Equal(ErrorCodes.HisBadGateway, ex.ErrorCode);
}
```

Also add a downstream JSON error test. It must assert that a HIS error body is
converted to `HealthExamException` and is never returned as PDF bytes.

- [ ] **Step 2: Run the focused adapter tests and verify they fail.**

Run:

```bash
dotnet test HealthExam.Tests/HealthExam.Tests.csproj --filter FullyQualifiedName~HisEmrHttpClientTests
```

Expected: compile failure because `IHisEmrApi` and `HisEmrHttpClient` do not yet
define `RenderFormPdfAsync`.

- [ ] **Step 3: Add the HIS adapter contract and binary implementation.**

Add the interface method:

```csharp
Task<byte[]> RenderFormPdfAsync(Guid emrDataId, CancellationToken ct = default);
```

In `HisEmrHttpClient`:

1. Add a route constant for `api/M03F10010/VEMR`.
2. Build the exact query `EMRDataID={id}&IsJson=false`.
3. Create an authenticated GET using the same HIS credential, trace, and
   division headers as existing calls.
4. Read successful responses with `ReadAsByteArrayAsync(ct)`.
5. Reject an empty body with `ErrorCodes.HisBadGateway`.
6. For non-success responses, parse the existing HIS envelope/error shape and
   map it through the existing `HealthExamException` conventions.
7. Do not use the JSON `SendAsync<T>` path and do not retry this operation.

Update `FakeHisEmrApi` with a `RenderFormPdfCalls` counter, a
`LastRenderedEmrDataId` property, a configurable `RenderFormPdfResult`, and a
configurable `RenderFormPdfException`. Its method must only increment the
counter, record the ID, then return or throw the configured result.

- [ ] **Step 4: Run the focused adapter tests and verify they pass.**

Run the same focused command from Step 2.

Expected: all `HisEmrHttpClientTests` pass, including route, headers, binary
body, empty-body, and downstream-error assertions.

- [ ] **Step 5: Commit the adapter change.**

```bash
git add HealthExam.Server/Service/IHisEmrApi.cs \
  HealthExam.Server/Service/HisEmrHttpClient.cs \
  HealthExam.Tests/FakeHisEmrApi.cs \
  HealthExam.Tests/HisEmrHttpClientTests.cs
git commit -m "feat(health-exam): add HIS EMR PDF preview adapter"
```

### Task 2: Add saved-snapshot preview to the registration-form service

**Files:**
- Modify: `HealthExam.Server/Service/IRegistrationFormValueService.cs`
- Modify: `HealthExam.Server/Service/RegistrationFormValueService.cs`
- Modify: `HealthExam.Tests/RegistrationFormValueServiceTests.cs`

**Interfaces:**
- Consumes: `IUnitOfWork`, `IHealthExamContext`, and `IHisEmrApi.RenderFormPdfAsync`.
- Produces: `Task<byte[]> PreviewAsync(Guid recordId, CancellationToken ct = default)` on `IRegistrationFormValueService`.

- [ ] **Step 1: Write service tests for saved data, missing data, and tenant isolation.**

Add tests to `RegistrationFormValueServiceTests`:

```csharp
[Fact]
public async Task Preview_returns_pdf_for_the_saved_his_emr_snapshot_without_mutating_record()
{
    var emrDataId = Guid.NewGuid();
    var (_, record) = SetupRecordAndTemplate(
        Guid.NewGuid(), Guid.NewGuid(), existingEmrDataId: emrDataId, admissionId: 777L);
    var beforeStatus = record.HisFormSyncStatus;
    var pdf = Encoding.ASCII.GetBytes("%PDF-1.7\npreview");
    _fakeHis.RenderFormPdfResult = pdf;

    var result = await _service.PreviewAsync(record.RecordID);

    Assert.Equal(pdf, result);
    Assert.Equal(1, _fakeHis.RenderFormPdfCalls);
    Assert.Equal(emrDataId, _fakeHis.LastRenderedEmrDataId);
    var unchanged = _db.ExamRecords.Find(record.RecordID)!;
    Assert.Equal(emrDataId, unchanged.HisEmrDataID);
    Assert.Equal(beforeStatus, unchanged.HisFormSyncStatus);
}

[Fact]
public async Task Preview_rejects_record_without_saved_emr_id_before_calling_his()
{
    var (_, record) = SetupRecordAndTemplate(Guid.NewGuid(), Guid.NewGuid(), existingEmrDataId: null);

    var ex = await Assert.ThrowsAsync<HealthExamException>(() =>
        _service.PreviewAsync(record.RecordID));

    Assert.Equal(ErrorCodes.InvalidState, ex.ErrorCode);
    Assert.Equal(0, _fakeHis.RenderFormPdfCalls);
}

[Fact]
public async Task Preview_does_not_cross_division_boundary()
{
    var (_, record) = SetupRecordAndTemplate(
        Guid.NewGuid(), Guid.NewGuid(), existingEmrDataId: Guid.NewGuid());
    record.DivisionID = "OTHER";
    _db.ExamRecords.Update(record);
    await _db.SaveChangesAsync();

    var ex = await Assert.ThrowsAsync<HealthExamException>(() =>
        _service.PreviewAsync(record.RecordID));

    Assert.Equal(ErrorCodes.NotFound, ex.ErrorCode);
    Assert.Equal(0, _fakeHis.RenderFormPdfCalls);
}
```

Use the existing in-memory database and `FakeHisEmrApi`; add the required
`System.Text` import if the test file does not already have it.

- [ ] **Step 2: Run the focused service tests and verify they fail.**

Run:

```bash
dotnet test HealthExam.Tests/HealthExam.Tests.csproj --filter FullyQualifiedName~RegistrationFormValueServiceTests
```

Expected: compile failure because `PreviewAsync` is not yet on the service
interface or implementation and the fake does not yet expose the new method.

- [ ] **Step 3: Implement the service method with the existing tenant guard.**

Add the interface method:

```csharp
Task<byte[]> PreviewAsync(Guid recordId, CancellationToken ct = default);
```

Implement it in `RegistrationFormValueService` by:

1. Querying `ExamRecords` with `RecordID == recordId && DivisionID == _context.DivisionId`.
2. Throwing `ErrorCodes.NotFound` when no row is found.
3. Throwing `ErrorCodes.InvalidState` with a clear “form chưa được lưu lên HIS”
   message when `HisEmrDataID` is null or empty.
4. Calling `_hisEmrApi.RenderFormPdfAsync(record.HisEmrDataID.Value, ct)`.
5. Returning the bytes without updating the entity or calling `SaveChangesAsync`.

- [ ] **Step 4: Run the focused service tests and verify they pass.**

Run the same focused command from Step 2.

Expected: saved snapshot, missing ID, tenant isolation, and no-mutation tests
pass.

- [ ] **Step 5: Commit the service change.**

```bash
git add HealthExam.Server/Service/IRegistrationFormValueService.cs \
  HealthExam.Server/Service/RegistrationFormValueService.cs \
  HealthExam.Tests/RegistrationFormValueServiceTests.cs \
  HealthExam.Tests/FakeHisEmrApi.cs
git commit -m "feat(health-exam): render saved registration form PDF"
```

### Task 3: Expose the PDF file endpoint

**Files:**
- Modify: `HealthExam.API/Controllers/ExamRecordRegistrationFormController.cs`
- Modify: `HealthExam.Tests/RegistrationFormValueEndpointTests.cs`

**Interfaces:**
- Consumes: `IRegistrationFormValueService.PreviewAsync`.
- Produces: `GET /v1/exam-records/{recordId}/registration-form/preview` with a raw PDF response.

- [ ] **Step 1: Write the route and binary-response endpoint tests.**

Extend the controller route reflection test to assert that the controller has
an action with `[HttpGet("preview")]`.

Add an endpoint test using the existing `AuthTestHost` and a fake service whose
`PreviewAsync` returns `Encoding.ASCII.GetBytes("%PDF-1.7\npreview")`:

```csharp
[Fact]
public async Task Get_preview_returns_pdf_and_delegates_record_id()
{
    var recordId = Guid.NewGuid();
    var fakeService = new FakeRegistrationFormValueService
    {
        PreviewPdf = Encoding.ASCII.GetBytes("%PDF-1.7\npreview")
    };

    await using var factory = _host.WithWebHostBuilder(builder =>
        builder.ConfigureTestServices(services =>
            services.Replace(ServiceDescriptor.Singleton<IRegistrationFormValueService>(fakeService))));

    using var client = factory.CreateClient();
    client.DefaultRequestHeaders.Add(TestHeaders.Division, "DEV");
    client.DefaultRequestHeaders.Authorization =
        new AuthenticationHeaderValue("Bearer", AuthTestHost.EmployeeToken());

    using var response = await client.GetAsync(
        $"/v1/exam-records/{recordId}/registration-form/preview");

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    Assert.Equal("application/pdf", response.Content.Headers.ContentType?.MediaType);
    Assert.Equal(fakeService.PreviewPdf, await response.Content.ReadAsByteArrayAsync());
    Assert.Equal(recordId, fakeService.LastPreviewRecordId);
}
```

Update the nested `FakeRegistrationFormValueService` with `PreviewPdf`,
`LastPreviewRecordId`, and the new interface method.

- [ ] **Step 2: Run endpoint tests and verify they fail.**

Run:

```bash
dotnet test HealthExam.Tests/HealthExam.Tests.csproj --filter FullyQualifiedName~RegistrationFormValueEndpointTests
```

Expected: compile or assertion failure because the preview action and fake
service method do not yet exist.

- [ ] **Step 3: Add the controller action.**

Add this action to `ExamRecordRegistrationFormController`:

```csharp
[HttpGet("preview")]
public async Task<IActionResult> Preview(
    [FromRoute] Guid recordId,
    CancellationToken ct = default)
{
    var pdf = await _service.PreviewAsync(recordId, ct);
    return File(pdf, "application/pdf");
}
```

The action must not wrap the PDF in `Success(...)`; existing exception
middleware remains responsible for the standard error envelope on failures.
Returning `File(pdf, "application/pdf")` without a download filename keeps the
browser previewable response inline.

- [ ] **Step 4: Run endpoint tests and verify they pass.**

Run the same focused command from Step 2.

Expected: route, authentication, content type, raw PDF body, and service
delegation assertions pass.

- [ ] **Step 5: Commit the endpoint change.**

```bash
git add HealthExam.API/Controllers/ExamRecordRegistrationFormController.cs \
  HealthExam.Tests/RegistrationFormValueEndpointTests.cs
git commit -m "feat(health-exam): expose registration form PDF preview"
```

### Task 4: Document and verify the complete flow

**Files:**
- Modify: `README.md`
- Reference: `docs/superpowers/specs/2026-09-10-health-exam-pdf-preview-design.md`

**Interfaces:**
- Consumes: The completed adapter, service, and controller from Tasks 1–3.
- Produces: Documented endpoint and a full repository verification result.

- [ ] **Step 1: Add the endpoint to the README API inventory.**

Add the following row beside the existing registration-form routes:

```text
| GET | `/v1/exam-records/{recordId}/registration-form/preview` — PDF của snapshot HIS EMR đã lưu; không submit/ký |
```

Keep the documentation explicit that preview requires a previously saved
`HisEmrDataID` and does not create a sign workflow.

- [ ] **Step 2: Run the full automated test suite.**

Run:

```bash
TZ=Asia/Ho_Chi_Minh dotnet test HealthExamServer.sln
```

Expected: all tests pass. If the PostgreSQL test database is not configured,
the suite may skip the repository's existing PostgreSQL-only tests as described
in `README.md`; do not treat those skips as preview failures.

- [ ] **Step 3: Run a compile-only verification for the API project.**

Run:

```bash
dotnet build HealthExam.API/HealthExam.API.csproj --no-restore
```

Expected: build succeeds with no new warnings caused by the preview change.

- [ ] **Step 4: Review the final diff against the spec.**

Verify the diff has all of the following and nothing outside scope:

- One new public route with the exact path in the spec.
- One binary HIS adapter operation using `VEMR` and `IsJson=false`.
- Record and division ownership enforcement before the HIS call.
- No call to `SubmitEMR`, `SignEMR`, or sign-server.
- Raw PDF success response and standard JSON error responses.
- Tests for success, missing saved EMR ID, tenant isolation, downstream errors,
  headers, and no mutation.

- [ ] **Step 5: Commit documentation and verification changes.**

```bash
git add README.md
git commit -m "docs(health-exam): document saved form PDF preview"
```

## Final handoff

After Task 4, report the exact route, the test command result, and whether a
non-production HIS smoke test was available. A smoke test is only valid when a
test record already has a saved `HisEmrDataID`; it must not use a real patient
record.
