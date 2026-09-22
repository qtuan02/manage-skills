# Ký mục khám chọn Người xác nhận — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** `POST .../sections/{itemGroupId}/sign` nhận `{ConfirmedByEmployeeID, SignedAt}`; snapshot ghi người xác nhận làm người ký (chứng thư của họ đóng lên PDF ở kết luận) và người bấm làm người thực hiện; `GET .../his-form/sections/{itemGroupId}` trả danh sách người ký hợp lệ (`Signers[]`) + tên người đã ký; `Steps[]` mang `PerformedBy*`; FE thay mock bằng dữ liệu thật và chạy được với BE cũ.

**Architecture:** `HEX_ExamRecordSignStep.SignedBy*`/`SignedAt` = người xác nhận/thời gian thực hiện; thêm 2 cột `PerformedByEmployeeID/Name` = người bấm. Danh sách người ký lấy từ HIS `GetCodeList?key=EmployeeRole&filterCode={SWRoleID}&amount=0` qua `IHisEmrClient`. Handler ký bỏ gate vai trò trên người bấm; kiểm người ký ∈ danh sách HIS + có chứng thư. Vòng ký PDF ở kết luận, hủy ký, luật eligibility không đổi.

**Tech Stack:** .NET 8 / ASP.NET Core / EF Core (Npgsql) / xUnit (`health-exam-server`); React + TanStack Query + react-hook-form + zod + vitest (`turbo-web/apps/health-exam`).

**Spec:** `docs/superpowers/specs/2026-09-19-section-sign-choose-signer-design.md` (Jira PROJ-2374).

## Global Constraints

- Repo BE: `/Volumes/DATA/WORKING/DHSC/DHSC_HOS/HOS_V1/backend/health-exam-server`, nhánh `feat/section-sign-choose-signer` (đã tạo từ `main` 897d0fa). Repo FE: `/Volumes/DATA/WORKING/DHSC/DHSC_HOS/HOS_V1/frontend/turbo-web`, tạo nhánh `feat/section-sign-choose-signer` từ `main`.
- Không sửa luật của `SaveHisFormSection`, `CancelExamSectionSign`, vòng ký PDF trong `SignConclusion`, điều kiện trong `GetConclusionEligibility`, `HEX_SignStepMap`. Hai file kết luận chỉ thêm 2 trường vào mapping `Steps[]` (Task 6).
- Tên trường trên dây (JSON) PascalCase như DTO hiện có: `ConfirmedByEmployeeID`, `SignedAt`, `Signers`, `CurrentStepSignedByEmployeeName`, `SignedByEmployeeName`, `PerformedByEmployeeID`, `PerformedByEmployeeName`.
- HIS `GetCodeList` **phải** có `amount=0` trên URL (mặc định HIS cắt 20 dòng).
- Không có body / body rỗng ở `POST .../sign` → hành vi cũ (người ký = người bấm) và **không** 415.
- FE phải chạy được với BE cũ (không `Signers`): chế độ khoan dung, không gửi `ConfirmedByEmployeeID`.
- Thông điệp lỗi tiếng Việt, cùng giọng với handler hiện có.
- Lệnh test BE (từ thư mục repo BE): `dotnet test HealthExam.Tests/HealthExam.Tests.csproj --filter "FullyQualifiedName~<Tên>"`. Lệnh test FE (từ thư mục repo FE): `pnpm --filter health-exam test -- <đường dẫn test>`.
- Commit message tiếng Việt, kết thúc bằng `Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>`.

---

## File map

Backend (`health-exam-server`):

| File | Trách nhiệm trong plan |
|---|---|
| `HealthExam.Application/His/HisModels.cs` | record `SignRoleEmployee`; `HisRecordSectionResult` +2 trường |
| `HealthExam.Application/Integrations/IHisEmrClient.cs` | `HisOperation.ListSignRoleEmployees`; default method `ListSignRoleEmployeesAsync` |
| `HealthExam.Infrastructure/Integrations/HisEmr/HisEmrClient.cs` | gọi `GetCodeList?key=EmployeeRole`, parser |
| `HealthExam.Domain/ExamRecords/ExamRecordSignStep.cs` | 2 property `PerformedBy*` |
| `HealthExam.Infrastructure/Persistence/HealthExamDbContext.cs` | cấu hình cột |
| `HealthExam.Infrastructure/Persistence/Migrations/*_AddSignStepPerformedBy.cs` | migration |
| `HealthExam.Application/Signing/SigningModels.cs` | `SignExamSectionCommand` mới, `ExamSectionSignResult` +3 trường |
| `HealthExam.Application/Signing/SignExamSection.cs` | luật ký thay |
| `HealthExam.API/Contracts/HisEmrModels.cs` | `ExamSectionSignRequest`, `SignerChoice`, `ExamFormSection` +2 trường |
| `HealthExam.API/Controllers/ExamRecordController.cs` | endpoint sign nhận body |
| `HealthExam.Application/His/GetHisRecordSection.cs` | nạp `Signers` (chưa ký) + tên người ký |
| `HealthExam.API/Controllers/ExamRecordHisFormController.cs` | map 2 trường mới (GET + PUT) |
| `HealthExam.Application/Paraclinical/ParaclinicalModels.cs`, `GetConclusionEligibility.cs`, `SignConclusion.cs`, `HealthExam.API/Contracts/ParaclinicalModels.cs` | `Steps[]` +`PerformedBy*` |
| `HealthExam.Tests/Signing/FakeSigningGateways.cs` | `FakeSignRoleEmployeesHisClient` |
| `HealthExam.Tests/InMemoryTestDb.cs` | `SignExamSection(map, cert, his)` |
| `docs/api/conclusion-pdf-signing-guide.md`, `docs/api/his-section-signing-api.md` | contract |

Frontend (`turbo-web`):

| File | Trách nhiệm |
|---|---|
| `packages/types/src/health-exam/signing.ts` | `SignerChoice`; `ExamSectionSignPayload.ConfirmedByEmployeeID: number \| null`; `ExamSectionSignResult`/`ConclusionSignStep` +`PerformedBy*`, `SignedByEmployeeName` |
| `packages/types/src/health-exam/his-form.ts` | `HisFormSection` +2 trường (optional) |
| `apps/health-exam/src/features/results/types/sign-section-form.ts` | schema có điều kiện, default `confirmedBy` |
| `.../clinical-tab/category-sign-form.tsx` | dropdown từ `signers`, chế độ khoan dung |
| `.../clinical-tab/category-confirm-dialog.tsx` | truyền `signers`/`currentEmployeeId` |
| `.../clinical-tab/clinical-tab.tsx` | đọc `Signers` từ cache section + `useCurrentUserQuery` |
| `.../clinical-tab/category-header.tsx` | 3 ô đọc `PerformedByEmployeeName`/`SignedByEmployeeName` |
| `apps/health-exam/src/constants/mock-doctors.ts` | **xóa** |
| `packages/i18n/src/locales/{vi,en}.json` | key `signForm.noSigners` |

---

### Task 1: HIS client — danh sách nhân viên theo vai trò ký

**Files:**
- Modify: `HealthExam.Application/His/HisModels.cs` (cuối file)
- Modify: `HealthExam.Application/Integrations/IHisEmrClient.cs:8-22` (enum) và cuối interface
- Modify: `HealthExam.Infrastructure/Integrations/HisEmr/HisEmrClient.cs:102-103` (retry set), sau `ListEmployeeSignRoleIdsAsync` (~836), cạnh `ParseSignRoleIds` (~1129)
- Test: `HealthExam.Tests/HisEmrClientTests.cs`

**Interfaces:**
- Produces `public sealed record SignRoleEmployee(long EmployeeID, string EmployeeCode, string EmployeeName);` (namespace `HealthExam.Application.His`).
- Produces `Task<HisClientResult<IReadOnlyList<SignRoleEmployee>>> IHisEmrClient.ListSignRoleEmployeesAsync(long roleId, HisCallContext context, CancellationToken ct = default)`.
- Produces `HisOperation.ListSignRoleEmployees`.

- [ ] **Step 1: Viết test thất bại**

Thêm vào cuối class `HisEmrClientTests` (dùng helper `CreateClient`/`JsonResponse` sẵn có; đảm bảo có `using HealthExam.Application.His;`):

```csharp
    /// <summary>
    /// Danh sách người ký của một bước = HIS GetCodeList key=EmployeeRole. Phải gửi amount=0:
    /// mặc định HIS trả 20 dòng đầu — thiếu bác sĩ mà không hề báo lỗi.
    /// </summary>
    [Fact]
    public async Task ListSignRoleEmployeesAsync_goi_GetCodeList_EmployeeRole_amount_0_va_parse_Data_Data()
    {
        var (client, handler, _) = CreateClient(response: JsonResponse(new
        {
            ErrorCode = 0,
            Data = new
            {
                Filter = "", Amount = 4, Page = 0, Total = 4, Sort = "",
                Data = new[]
                {
                    new { Code = "40", CodeID = 40, CodeName = "BS. Nguyễn Văn An", DisplayName = "BS. Nguyễn Văn An" },
                    new { Code = "40", CodeID = 40, CodeName = "BS. Nguyễn Văn An", DisplayName = "BS. Nguyễn Văn An" },
                    new { Code = "41", CodeID = 41, CodeName = "BS. Trần Thị Bình", DisplayName = "" },
                    new { Code = "", CodeID = 0, CodeName = "rác", DisplayName = "" }
                }
            }
        }));

        var res = await client.ListSignRoleEmployeesAsync(45, new HisCallContext("Bearer explicit-token", "trace-99", "DIV-1"));

        Assert.True(res.IsSuccess);
        Assert.Equal(new[]
        {
            new SignRoleEmployee(40, "40", "BS. Nguyễn Văn An"),
            new SignRoleEmployee(41, "41", "BS. Trần Thị Bình")
        }, res.Value);
        var call = Assert.Single(handler.Calls);
        Assert.Equal("https://his.test/api/GetCodeList?key=EmployeeRole&filterCode=45&amount=0", call.Url);
        Assert.Equal("Bearer explicit-token", call.Headers["Authorization"]);
        Assert.Equal("trace-99", call.Headers["X-Trace-Id"]);
    }

    [Fact]
    public async Task ListSignRoleEmployeesAsync_tra_Fail_khi_HIS_loi()
    {
        var (client, _, _) = CreateClient(response: new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent("Server Error")
        });

        var res = await client.ListSignRoleEmployeesAsync(45, new HisCallContext("Bearer t", "trace", "DIV-1"));

        Assert.False(res.IsSuccess);
        Assert.Null(res.Value);
    }
```

- [ ] **Step 2: Chạy test, xác nhận fail vì chưa compile**

Run: `dotnet test HealthExam.Tests/HealthExam.Tests.csproj --filter "FullyQualifiedName~HisEmrClientTests.ListSignRoleEmployeesAsync"`
Expected: build error `SignRoleEmployee`/`ListSignRoleEmployeesAsync` không tồn tại.

- [ ] **Step 3: Thêm record + enum + default method**

`HealthExam.Application/His/HisModels.cs`, thêm sau `DepartmentCatalogResult`:

```csharp
/// <summary>Một nhân viên giữ vai trò ký của bước — nguồn HIS GetCodeList key=EmployeeRole.</summary>
public sealed record SignRoleEmployee(long EmployeeID, string EmployeeCode, string EmployeeName);
```

`HealthExam.Application/Integrations/IHisEmrClient.cs`: thêm `ListSignRoleEmployees` vào cuối enum `HisOperation` (sau `ListSignRoles`), và thêm vào cuối interface:

```csharp
    /// <summary>
    /// Nhân viên đang giữ vai trò ký <paramref name="roleId"/> — nguồn api/GetCodeList?key=EmployeeRole
    /// (cùng join sAM_AccountInGroups mà HIS dùng để kiểm quyền bước ký). Luôn gửi amount=0.
    /// </summary>
    Task<HisClientResult<System.Collections.Generic.IReadOnlyList<SignRoleEmployee>>> ListSignRoleEmployeesAsync(
        long roleId, HisCallContext context, CancellationToken ct = default)
        => Task.FromResult(HisClientResult<System.Collections.Generic.IReadOnlyList<SignRoleEmployee>>.Fail(HisClientOutcome.NotFound, "Not implemented"));
```

- [ ] **Step 4: Implement trong `HisEmrClient`**

Sửa dòng `retryConnectionOnce` (~103):

```csharp
        var retryConnectionOnce = operation is HisOperation.ListDefinitions or HisOperation.GetDefinition
            or HisOperation.GetDefinitionLayout or HisOperation.ListSignRoles or HisOperation.ListSignRoleEmployees;
```

Thêm method ngay sau `ListEmployeeSignRoleIdsAsync`:

```csharp
    public virtual async Task<HisClientResult<System.Collections.Generic.IReadOnlyList<SignRoleEmployee>>> ListSignRoleEmployeesAsync(
        long roleId, HisCallContext context, CancellationToken ct = default)
    {
        if (!_options.Enabled)
        {
            return HisClientResult<System.Collections.Generic.IReadOnlyList<SignRoleEmployee>>.Fail(
                HisClientOutcome.VendorNotConfigured, "Tích hợp HIS EMR chưa được kích hoạt");
        }

        // amount=0 → HIS trả toàn bộ; bỏ trống thì mặc định 20 dòng, thiếu bác sĩ mà không báo.
        var path = $"{RouteGetCodeList}?key=EmployeeRole&filterCode={roleId}&amount=0";
        var hisReq = new HisRequest(path, "GET", context?.Credential, TraceId: context?.TraceId, DivisionId: context?.DivisionId);
        var res = await SendAsync(HisOperation.ListSignRoleEmployees, hisReq, ct);
        if (!res.IsSuccess)
        {
            return HisClientResult<System.Collections.Generic.IReadOnlyList<SignRoleEmployee>>.Fail(res.Outcome, res.Message, res.Payload);
        }

        return HisClientResult<System.Collections.Generic.IReadOnlyList<SignRoleEmployee>>.Success(
            ParseSignRoleEmployees(res.Value.RawJson));
    }
```

Thêm parser cạnh `ParseSignRoleIds`:

```csharp
    /// <summary>GetCodeList bọc ResponseFilterData: Data.Data[] {Code, CodeID, CodeName, DisplayName}.</summary>
    private static IReadOnlyList<SignRoleEmployee> ParseSignRoleEmployees(string rawJson)
    {
        if (string.IsNullOrWhiteSpace(rawJson)) return Array.Empty<SignRoleEmployee>();
        try
        {
            var root = JToken.Parse(rawJson);
            JArray list = null;
            if (root is JArray rArr) list = rArr;
            else if (root is JObject rObj)
            {
                var dataToken = rObj["Data"] ?? rObj["data"];
                if (dataToken is JArray dArr) list = dArr;
                else if (dataToken is JObject dObj && (dObj["Data"] ?? dObj["data"]) is JArray subArr) list = subArr;
            }
            if (list == null) return Array.Empty<SignRoleEmployee>();

            var byId = new Dictionary<long, SignRoleEmployee>();
            foreach (var row in list)
            {
                var id = row.Value<long?>("CodeID") ?? 0;
                if (id <= 0 || byId.ContainsKey(id)) continue;
                var code = row.Value<string>("Code")?.Trim() ?? "";
                var name = row.Value<string>("DisplayName")?.Trim();
                if (string.IsNullOrWhiteSpace(name)) name = row.Value<string>("CodeName")?.Trim() ?? "";
                byId[id] = new SignRoleEmployee(id, code, name);
            }
            return byId.Values.ToList();
        }
        catch
        {
            return Array.Empty<SignRoleEmployee>();
        }
    }
```

- [ ] **Step 5: Chạy test, xác nhận pass**

Run: `dotnet test HealthExam.Tests/HealthExam.Tests.csproj --filter "FullyQualifiedName~HisEmrClientTests"`
Expected: PASS toàn bộ (kể cả test cũ).

- [ ] **Step 6: Commit**

```bash
git add HealthExam.Application/His/HisModels.cs HealthExam.Application/Integrations/IHisEmrClient.cs HealthExam.Infrastructure/Integrations/HisEmr/HisEmrClient.cs HealthExam.Tests/HisEmrClientTests.cs
git commit -m "feat(his): liệt kê nhân viên theo vai trò ký qua GetCodeList EmployeeRole

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>"
```

---

### Task 2: Cột `PerformedBy*` trên snapshot + migration

**Files:**
- Modify: `HealthExam.Domain/ExamRecords/ExamRecordSignStep.cs` (sau `SignedByEmployeeName`)
- Modify: `HealthExam.Infrastructure/Persistence/HealthExamDbContext.cs:284-296` (khối `ExamRecordSignStep`)
- Create (sinh bằng `dotnet ef`): `HealthExam.Infrastructure/Persistence/Migrations/<timestamp>_AddSignStepPerformedBy.cs` + `.Designer.cs`; cập nhật `HealthExamDbContextModelSnapshot.cs`
- Test: `HealthExam.Tests/Signing/SignStepMapTests.cs` (thêm 1 test model)

**Interfaces:**
- Produces `ExamRecordSignStep.PerformedByEmployeeID: long?`, `ExamRecordSignStep.PerformedByEmployeeName: string = ""`.

- [ ] **Step 1: Viết test model (fail)**

Thêm vào `SignStepMapTests.cs` (namespace `HealthExam.Tests.Signing`):

```csharp
    /// <summary>
    /// PROJ-2374: snapshot ghi cả người bấm ký (PerformedBy*) bên cạnh người ký (SignedBy*),
    /// vì hai người có thể khác nhau (ký thay). Cột tên NOT NULL DEFAULT '' cùng khuôn SignedByEmployeeName.
    /// </summary>
    [Fact]
    public void Snapshot_co_cot_PerformedBy_cung_khuon_SignedBy()
    {
        var db = new InMemoryTestDb();
        var entity = db.Db.Model.FindEntityType(typeof(HealthExam.Domain.ExamRecords.ExamRecordSignStep))!;

        var id = entity.FindProperty("PerformedByEmployeeID")!;
        Assert.True(id.IsNullable);

        var name = entity.FindProperty("PerformedByEmployeeName")!;
        Assert.False(name.IsNullable);
        Assert.Equal(255, name.GetMaxLength());
        Assert.Equal("", name.GetDefaultValue());
    }
```

- [ ] **Step 2: Chạy test, xác nhận fail**

Run: `dotnet test HealthExam.Tests/HealthExam.Tests.csproj --filter "FullyQualifiedName~SignStepMapTests.Snapshot_co_cot_PerformedBy"`
Expected: FAIL (`FindProperty` trả null → NullReferenceException).

- [ ] **Step 3: Thêm property + cấu hình**

`ExamRecordSignStep.cs`, sau `SignedByEmployeeName`:

```csharp
    /// <summary>Người bấm ký (có thể khác người ký khi ký thay — PROJ-2374). Không dùng để tra chứng thư.</summary>
    public long? PerformedByEmployeeID { get; set; }

    public string PerformedByEmployeeName { get; set; } = "";
```

`HealthExamDbContext.cs`, sau dòng cấu hình `SignedByEmployeeName`:

```csharp
            e.Property(x => x.PerformedByEmployeeName).HasMaxLength(255).IsRequired().HasDefaultValue("");
```

- [ ] **Step 4: Sinh migration**

```bash
dotnet ef migrations add AddSignStepPerformedBy --project HealthExam.Infrastructure --startup-project HealthExam.API
```

Mở file migration sinh ra, kiểm tra `Up` chỉ có 2 `AddColumn` trên `HEX_ExamRecordSignStep`: `PerformedByEmployeeID` (`bigint`, nullable) và `PerformedByEmployeeName` (`character varying(255)`, `nullable: false`, `defaultValue: ""`). Có thêm gì khác ⇒ model snapshot đang lệch main, dừng và báo.

- [ ] **Step 5: Chạy test + build**

Run: `dotnet build HealthExamServer.sln && dotnet test HealthExam.Tests/HealthExam.Tests.csproj --no-build --filter "FullyQualifiedName~SignStepMapTests"`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add HealthExam.Domain/ExamRecords/ExamRecordSignStep.cs HealthExam.Infrastructure/Persistence/HealthExamDbContext.cs HealthExam.Infrastructure/Persistence/Migrations HealthExam.Tests/Signing/SignStepMapTests.cs
git commit -m "feat(db): HEX_ExamRecordSignStep thêm PerformedByEmployeeID/Name (người bấm ký)

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>"
```

---

### Task 3: Handler ký mục khám — người ký = người xác nhận, người thực hiện = người bấm

**Files:**
- Modify: `HealthExam.Application/Signing/SigningModels.cs`
- Modify: `HealthExam.Application/Signing/SignExamSection.cs`
- Modify: `HealthExam.Tests/Signing/FakeSigningGateways.cs` (thêm fake HIS)
- Modify: `HealthExam.Tests/InMemoryTestDb.cs:480-485`
- Modify: `HealthExam.Tests/Signing/SignExamSectionTests.cs`
- Modify (chỉ đổi cách gọi): `HealthExam.Tests/ConclusionEligibilityTests.cs:80`, `HealthExam.Tests/Signing/SectionLockTests.cs:58`, `HealthExam.Tests/Signing/ConclusionEligibilityFromMapTests.cs:146`, `HealthExam.Tests/Signing/ConclusionSigningTests.cs:287`, `HealthExam.Tests/Signing/SectionSignConcurrencyTests.cs:88-104`

**Interfaces:**
- Consumes `IHisEmrClient.ListSignRoleEmployeesAsync` (Task 1), `ExamRecordSignStep.PerformedBy*` (Task 2).
- Produces:

```csharp
public sealed record SignExamSectionCommand(
    string DivisionId, Guid RecordId, int ItemGroupId,
    long EmployeeId, string EmployeeCode, string EmployeeName, ActorKind ActorKind,
    string Credential, string TraceId,
    long? ConfirmedByEmployeeId = null, DateTime? SignedAt = null);

public sealed record ExamSectionSignResult(
    int ItemGroupId, int SwStep, string StepName,
    long SignedByEmployeeID, DateTime SignedAt,
    string Status = ExamRecordSignStepStatus.Signed,
    int SigningProgressDone = 0, int SigningProgressTotal = 0,
    string SignedByEmployeeName = "",
    long? PerformedByEmployeeID = null, string PerformedByEmployeeName = "");
```

- `SignExamSectionHandler(IExamRecordRepository, ISignStepMapRepository, ICertificateGateway, IHisEmrClient, IUnitOfWork, IAuditRepository)`.
- Test fake: `FakeSignRoleEmployeesHisClient` với `Add(long roleId, long employeeId, string code, string name)`, `bool Fail`, `List<long> RequestedRoleIds`.

- [ ] **Step 1: Thêm fake HIS vào `FakeSigningGateways.cs`**

Thêm cuối file (thêm `using HealthExam.Application.His;`):

```csharp
/// <summary>
/// HIS giả cho danh sách người ký theo vai trò. Mọi method khác dùng default interface (NotFound).
/// </summary>
public sealed class FakeSignRoleEmployeesHisClient : IHisEmrClient
{
    private readonly List<(long RoleId, SignRoleEmployee Employee)> _rows = new();
    public bool Fail { get; set; }
    public List<long> RequestedRoleIds { get; } = new();

    public FakeSignRoleEmployeesHisClient Add(long roleId, long employeeId, string code, string name)
    {
        _rows.Add((roleId, new SignRoleEmployee(employeeId, code, name)));
        return this;
    }

    public Task<HisClientResult<HisJsonDocument>> SendAsync(
        HisOperation operation, HisRequest request, CancellationToken ct = default)
        => Task.FromResult(HisClientResult<HisJsonDocument>.Fail(
            HisClientOutcome.NotFound, "FakeSignRoleEmployeesHisClient chỉ hỗ trợ ListSignRoleEmployeesAsync"));

    public Task<HisClientResult<IReadOnlyList<SignRoleEmployee>>> ListSignRoleEmployeesAsync(
        long roleId, HisCallContext context, CancellationToken ct = default)
    {
        RequestedRoleIds.Add(roleId);
        if (Fail)
            return Task.FromResult(HisClientResult<IReadOnlyList<SignRoleEmployee>>.Fail(
                HisClientOutcome.BadGateway, "HIS lỗi"));
        return Task.FromResult(HisClientResult<IReadOnlyList<SignRoleEmployee>>.Success(
            _rows.Where(r => r.RoleId == roleId).Select(r => r.Employee).ToList()));
    }
}
```

- [ ] **Step 2: Đổi factory trong `InMemoryTestDb.cs`**

```csharp
    public HealthExam.Application.Signing.SignExamSectionHandler SignExamSection(
        HealthExam.Application.Signing.ISignStepMapRepository map,
        HealthExam.Application.Integrations.ICertificateGateway cert,
        HealthExam.Application.Integrations.IHisEmrClient his)
        => new(new ExamRecordRepository(Db), map, cert, his,
            new HealthExam.Infrastructure.Persistence.UnitOfWork(Db),
            new HealthExam.Infrastructure.Persistence.AuditRepository(Db));
```

- [ ] **Step 3: Viết lại `SignExamSectionTests.cs` — helper + test mới (fail)**

Thay helper `Command` và `Fixture`, sửa test `Khong_giu_vai_tro_cua_buoc_thi_tra_Forbidden`, thêm 6 test mới:

```csharp
    private const long ActorId = 1274;
    private const long ConfirmerId = 4210;

    private static SignExamSectionCommand Command(
        Guid recordId, long? confirmedBy = null, DateTime? signedAt = null) => new(
        DivisionId, recordId, ItemGroupId, EmployeeId: ActorId, EmployeeCode: "NV001",
        EmployeeName: "BS A", ActorKind.Employee, Credential: "Bearer t", TraceId: "trace",
        ConfirmedByEmployeeId: confirmedBy, SignedAt: signedAt);

    /// <summary>
    /// Ký thay (PROJ-2374): người bấm chọn Người xác nhận trong danh sách HIS theo SWRoleID của
    /// bước; snapshot ghi người xác nhận làm người ký — chứng thư của họ sẽ đóng lên PDF ở kết luận —
    /// và người bấm làm người thực hiện. Mã/tên người ký lấy từ HIS, không tin FE.
    /// </summary>
    [Fact]
    public async Task Chon_nguoi_xac_nhan_thi_snapshot_ghi_nguoi_do_lam_nguoi_ky_va_actor_lam_nguoi_thuc_hien()
    {
        var (handler, db, record, cert, his) = Fixture();
        cert.WithCertificate.Add("NV002");
        var performedAt = new DateTime(2026, 9, 19, 1, 30, 0, DateTimeKind.Utc);

        var res = await handler.HandleAsync(Command(record.RecordID, ConfirmerId, performedAt));

        Assert.True(res.IsSuccess);
        Assert.Equal(ConfirmerId, res.Value.SignedByEmployeeID);
        Assert.Equal("BS B", res.Value.SignedByEmployeeName);
        Assert.Equal(ActorId, res.Value.PerformedByEmployeeID);
        Assert.Equal("BS A", res.Value.PerformedByEmployeeName);
        Assert.Equal(performedAt, res.Value.SignedAt);
        Assert.Equal(new[] { 45L }, his.RequestedRoleIds);

        var snapshot = db.RecordOf(record.RecordID).SignSteps.Single();
        Assert.Equal(ConfirmerId, snapshot.SignedByEmployeeID);
        Assert.Equal("NV002", snapshot.SignedByEmployeeCode);
        Assert.Equal("BS B", snapshot.SignedByEmployeeName);
        Assert.Equal(ActorId, snapshot.PerformedByEmployeeID);
        Assert.Equal("BS A", snapshot.PerformedByEmployeeName);
        Assert.Equal(performedAt, snapshot.SignedAt);
        Assert.Equal(ExamRecordSignStepStatus.Signed, snapshot.Status);
    }

    /// <summary>Ai lưu được mục khám thì bấm ký được: actor KHÔNG cần có vai trò ký của bước.</summary>
    [Fact]
    public async Task Nguoi_bam_khong_co_vai_tro_van_ky_duoc_khi_nguoi_xac_nhan_hop_le()
    {
        var (handler, db, record, cert, _) = Fixture(actorHasRole: false);
        cert.WithCertificate.Add("NV002");

        var res = await handler.HandleAsync(Command(record.RecordID, ConfirmerId));

        Assert.True(res.IsSuccess);
        Assert.Equal(ConfirmerId, db.RecordOf(record.RecordID).SignSteps.Single().SignedByEmployeeID);
    }

    [Fact]
    public async Task Nguoi_xac_nhan_khong_trong_danh_sach_vai_tro_thi_Forbidden()
    {
        var (handler, db, record, _, _) = Fixture();

        var res = await handler.HandleAsync(Command(record.RecordID, confirmedBy: 9999));

        Assert.False(res.IsSuccess);
        Assert.Equal(ApplicationFailureCode.Forbidden, res.Failure.Code);
        Assert.Contains("Người xác nhận không có vai trò ký của bước", res.Failure.Message);
        Assert.Empty(db.RecordOf(record.RecordID).SignSteps);
    }

    /// <summary>HIS không trả được danh sách ⇒ fail-closed, không ký với vai trò chưa xác định.</summary>
    [Fact]
    public async Task HIS_loi_thi_502_va_khong_ghi_snapshot()
    {
        var (handler, db, record, _, his) = Fixture();
        his.Fail = true;

        var res = await handler.HandleAsync(Command(record.RecordID, ConfirmerId));

        Assert.False(res.IsSuccess);
        Assert.Equal(ApplicationFailureCode.HisBadGateway, res.Failure.Code);
        Assert.Empty(db.RecordOf(record.RecordID).SignSteps);
    }

    [Fact]
    public async Task Thoi_gian_ky_o_tuong_lai_thi_BadRequest()
    {
        var (handler, db, record, cert, _) = Fixture();
        cert.WithCertificate.Add("NV002");

        var res = await handler.HandleAsync(Command(record.RecordID, ConfirmerId, DateTime.UtcNow.AddHours(1)));

        Assert.False(res.IsSuccess);
        Assert.Equal(ApplicationFailureCode.BadRequest, res.Failure.Code);
        Assert.Empty(db.RecordOf(record.RecordID).SignSteps);
    }

    /// <summary>Người xác nhận chưa có chứng thư: chặn ngay lúc ký mục, không để kẹt tới ký kết luận.</summary>
    [Fact]
    public async Task Nguoi_xac_nhan_khong_co_chung_thu_thi_SignPrecondition()
    {
        var (handler, db, record, _, _) = Fixture(); // chỉ NV001 có cert

        var res = await handler.HandleAsync(Command(record.RecordID, ConfirmerId));

        Assert.False(res.IsSuccess);
        Assert.Equal(ApplicationFailureCode.SignPrecondition, res.Failure.Code);
        Assert.Empty(db.RecordOf(record.RecordID).SignSteps);
    }

    /// <summary>Không chọn ai (client cũ) ⇒ người ký = người bấm, nhưng vẫn phải có vai trò của bước.</summary>
    [Fact]
    public async Task Khong_giu_vai_tro_cua_buoc_thi_tra_Forbidden()
    {
        var (handler, db, record, _, _) = Fixture(actorHasRole: false);

        var res = await handler.HandleAsync(Command(record.RecordID));

        Assert.False(res.IsSuccess);
        Assert.Equal(ApplicationFailureCode.Forbidden, res.Failure.Code);
        Assert.Empty(db.RecordOf(record.RecordID).SignSteps);
    }

    private static (SignExamSectionHandler Handler, InMemoryTestDb Db, ExamRecord Record,
        FakeCertificateGateway Cert, FakeSignRoleEmployeesHisClient His) Fixture(bool actorHasRole = true)
    {
        var db = new InMemoryTestDb();
        var session = db.SeedSession(divisionId: DivisionId);
        var record = db.SeedRecord(session.SessionID, ExamRecordState.InProgress, divisionId: DivisionId);
        db.SetVariantCode(record.RecordID, VariantCode);

        var map = new FakeSignStepMapRepository();
        map.Steps.Add(Step(1, ItemGroupId));
        map.Steps.Add(Step(2, 102));
        map.Steps.Add(Step(3, null, conclusion: true));

        var cert = new FakeCertificateGateway();
        cert.WithCertificate.Add("NV001");

        var his = new FakeSignRoleEmployeesHisClient().Add(45, ConfirmerId, "NV002", "BS B");
        if (actorHasRole) his.Add(45, ActorId, "NV001", "BS A");

        var handler = db.SignExamSection(map, cert, his);
        return (handler, db, record, cert, his);
    }
```

Các test cũ dùng `var (handler, db, record, _) = Fixture();` → đổi thành 5 phần tử `var (handler, db, record, _, _) = Fixture();` (test chứng thư: `var (handler, db, record, cert, _) = Fixture();`). Test `Ky_step_dang_in_progress...` giữ assert `1274`/`NV001` (fallback actor) và thêm `Assert.Equal(1274, savedStep.PerformedByEmployeeID);`.

Cập nhật 5 call site khác (chỉ đổi chữ ký; actor có trong danh sách nên hành vi cũ giữ nguyên):

`ConclusionEligibilityTests.cs:80`:
```csharp
        var his = new HealthExam.Tests.Signing.FakeSignRoleEmployeesHisClient().Add(ClinicalRoleId, 1274, "NV001", "BS A");
        await db.SignExamSection(map, cert, his).HandleAsync(new SignExamSectionCommand(
            record.DivisionID, record.RecordID, ClinicalItemGroupId, 1274, "NV001", "BS A",
            ActorKind.Employee, "Bearer t", "trace"));
```

`SectionLockTests.cs:58`:
```csharp
        var his = new FakeSignRoleEmployeesHisClient().Add(45, 1274, "NV001", "BS A");
        var signed = await db.SignExamSection(map, cert, his).HandleAsync(new SignExamSectionCommand(
            DivisionId, record.RecordID, SignedItemGroupId, EmployeeId: 1274, EmployeeCode: "NV001",
            EmployeeName: "BS A", ActorKind.Employee, Credential: "Bearer t", TraceId: "trace"));
```

`ConclusionEligibilityFromMapTests.cs:146`:
```csharp
    public Task SignSection(int itemGroupId)
        => Db.SignExamSection(Map, Cert, new FakeSignRoleEmployeesHisClient().Add(ExamRoleId, 1274, "NV001", "BS A"))
            .HandleAsync(new HealthExam.Application.Signing.SignExamSectionCommand(
                DivisionId, Record.RecordID, itemGroupId, 1274, "NV001", "BS A",
                ActorKind.Employee, "Bearer t", "trace"));
```

`ConclusionSigningTests.cs:287`:
```csharp
    public Task SignSection(int itemGroupId, string employeeCode = "NV001", long employeeId = 1001)
        => Db.SignExamSection(Map, Cert, new FakeSignRoleEmployeesHisClient().Add(ExamRoleId, employeeId, employeeCode, "BS"))
            .HandleAsync(new HealthExam.Application.Signing.SignExamSectionCommand(
                DivisionId, Record.RecordID, itemGroupId, employeeId, employeeCode, "BS",
                ActorKind.Employee, "Bearer t", "trace"));
```

`SectionSignConcurrencyTests.cs:88-104` (dựng handler trực tiếp):
```csharp
        var his = new FakeSignRoleEmployeesHisClient().Add(45, 1274, "NV001", "BS A");
        return new SignExamSectionHandler(repo, map, cert, his, uow, new FakeAuditRepository());
    }

    private static SignExamSectionCommand SignCommand(Guid recordId) => new(
        DivisionId, recordId, ItemGroupId, EmployeeId: 1274, EmployeeCode: "NV001",
        EmployeeName: "BS A", ActorKind.Employee, Credential: "Bearer t", TraceId: "trace");
```

(File nào chưa `using HealthExam.Tests.Signing;` thì thêm; `ConclusionEligibilityTests.cs` nằm namespace `HealthExam.Tests` nên dùng tên đầy đủ như trên.)

- [ ] **Step 4: Chạy test, xác nhận fail vì chưa compile**

Run: `dotnet test HealthExam.Tests/HealthExam.Tests.csproj --filter "FullyQualifiedName~SignExamSectionTests"`
Expected: build error (ctor/command mới chưa có).

- [ ] **Step 5: Sửa `SigningModels.cs`**

Thay `SignExamSectionCommand` và `ExamSectionSignResult` bằng đúng chữ ký ở **Interfaces** phía trên (giữ `CancelExamSectionSignCommand` nguyên).

- [ ] **Step 6: Sửa handler `SignExamSection.cs`**

Thay toàn bộ class (giữ interface `ISignExamSectionHandler`; thêm `using HealthExam.Application.His;`):

```csharp
/// <summary>
/// Bác sĩ bấm Ký số ở một mục khám. KHÔNG gọi sign-server: PDF chỉ được render và ký một lần
/// ở bước kết luận, vì sign-server dùng append mode nên nội dung phải chốt trước chữ ký đầu tiên.
///
/// Ký thay (PROJ-2374): người bấm có thể chọn Người xác nhận; snapshot ghi người đó làm người ký
/// (SignedBy*, chứng thư của họ đóng lên PDF) và người bấm làm người thực hiện (PerformedBy*).
/// Người bấm không cần vai trò — chỉ người ký phải thuộc danh sách vai trò của bước (HIS) và có chứng thư.
/// </summary>
public class SignExamSectionHandler : ISignExamSectionHandler
{
    /// <summary>Lệch đồng hồ client cho phép khi FE gửi "bây giờ".</summary>
    private static readonly TimeSpan FutureTolerance = TimeSpan.FromMinutes(5);

    private readonly IExamRecordRepository _records;
    private readonly ISignStepMapRepository _map;
    private readonly ICertificateGateway _certificates;
    private readonly IHisEmrClient _his;
    private readonly IUnitOfWork _uow;
    private readonly IAuditRepository _audit;

    public SignExamSectionHandler(
        IExamRecordRepository records, ISignStepMapRepository map,
        ICertificateGateway certificates, IHisEmrClient his, IUnitOfWork uow, IAuditRepository audit)
    {
        _records = records;
        _map = map;
        _certificates = certificates;
        _his = his;
        _uow = uow;
        _audit = audit;
    }

    public async Task<ApplicationResult<ExamSectionSignResult>> HandleAsync(
        SignExamSectionCommand command, CancellationToken ct = default)
    {
        if (command.ActorKind != ActorKind.Employee || command.EmployeeId <= 0
            || string.IsNullOrWhiteSpace(command.EmployeeCode))
        {
            return ApplicationResult<ExamSectionSignResult>.Fail(
                ApplicationFailureCode.Forbidden, "Chỉ nhân viên mới được ký số");
        }

        var now = DateTime.UtcNow;
        var signedAt = command.SignedAt?.ToUniversalTime() ?? now;
        if (signedAt > now + FutureTolerance)
            return ApplicationResult<ExamSectionSignResult>.Fail(
                ApplicationFailureCode.BadRequest, "Thời gian ký không được ở tương lai");

        // Cùng khuôn với SignConclusion: row lock trên HEX_ExamRecord serialise các lượt ký đồng
        // thời của MỘT hồ sơ. LockRecordAsync khóa theo RecordID không lọc tenant → tự đối chiếu
        // DivisionID. Mọi return sớm trước commit chỉ dispose tx → rollback.
        // Hai lượt gọi ngoài (HIS danh sách vai trò, SSM chứng thư) nằm trong tx: ký đồng thời
        // trên cùng hồ sơ là ca hiếm, chấp nhận xếp hàng sau một vòng HIS.
        await using var tx = await _uow.BeginAsync(ct);
        var locked = await _records.LockRecordAsync(command.RecordId, ct);
        if (locked == null || locked.DivisionID != command.DivisionId)
            return ApplicationResult<ExamSectionSignResult>.Fail(
                ApplicationFailureCode.NotFound, "Không tìm thấy hồ sơ khám");

        var record = await _records.GetAsync(command.DivisionId, command.RecordId, forUpdate: true, ct);
        if (record == null)
            return ApplicationResult<ExamSectionSignResult>.Fail(
                ApplicationFailureCode.NotFound, "Không tìm thấy hồ sơ khám");

        if (record.SignStatus == ExamRecordSignStatus.Signed)
            return ApplicationResult<ExamSectionSignResult>.Fail(
                ApplicationFailureCode.InvalidState, "Hồ sơ đã ký kết luận, không sửa được chữ ký");

        var steps = await _map.ListAsync(command.DivisionId, record.VariantCode, ct);
        var step = steps.FirstOrDefault(s => s.ItemGroupID == command.ItemGroupId);
        if (step == null)
            return ApplicationResult<ExamSectionSignResult>.Fail(
                ApplicationFailureCode.SignPrecondition,
                "Mục khám này chưa được cấu hình bước ký");

        // Người ký phải nằm trong danh sách HIS giữ SWRoleID của bước — áp cho cả nhánh không
        // chọn ai (người ký = người bấm). Mã/tên lấy từ HIS, không tin FE. HIS lỗi → fail-closed.
        var signerId = command.ConfirmedByEmployeeId ?? command.EmployeeId;
        var members = await _his.ListSignRoleEmployeesAsync(step.SWRoleID,
            new HisCallContext(command.Credential, command.TraceId, command.DivisionId), ct);
        if (!members.IsSuccess)
            return ApplicationResult<ExamSectionSignResult>.Fail(
                ApplicationFailureCode.HisBadGateway,
                string.IsNullOrWhiteSpace(members.Message) ? "Không tra được danh sách người ký từ HIS" : members.Message);

        var signer = members.Value.FirstOrDefault(m => m.EmployeeID == signerId);
        if (signer == null)
            return ApplicationResult<ExamSectionSignResult>.Fail(
                ApplicationFailureCode.Forbidden,
                $"Người xác nhận không có vai trò ký của bước \"{step.StepName}\"");

        var cert = await _certificates.GetAsync(signer.EmployeeCode, ct);
        if (!cert.Found)
            return ApplicationResult<ExamSectionSignResult>.Fail(
                ApplicationFailureCode.SignPrecondition, cert.Message);

        record.SignSteps ??= new System.Collections.Generic.List<ExamRecordSignStep>();
        var snapshot = record.SignSteps.FirstOrDefault(
            s => s.VariantCode == record.VariantCode && s.SWStep == step.SWStep);

        if (snapshot == null)
        {
            snapshot = new ExamRecordSignStep
            {
                DivisionID = command.DivisionId,
                RecordID = record.RecordID,
                VariantCode = record.VariantCode,
                CreatedDate = now
            };
            record.SignSteps.Add(snapshot);
        }

        snapshot.ItemGroupID = step.ItemGroupID;
        snapshot.SWStep = step.SWStep;
        snapshot.StepName = step.StepName;
        snapshot.SWRoleID = step.SWRoleID;
        snapshot.Status = ExamRecordSignStepStatus.Signed;
        snapshot.SignedByEmployeeID = signer.EmployeeID;
        snapshot.SignedByEmployeeCode = signer.EmployeeCode;
        snapshot.SignedByEmployeeName = signer.EmployeeName;
        snapshot.PerformedByEmployeeID = command.EmployeeId;
        snapshot.PerformedByEmployeeName = command.EmployeeName ?? "";
        snapshot.SignedAt = signedAt;
        snapshot.ModifiedDate = now;

        _audit.Add(new AuditEntry(command.DivisionId, AuditEntityTypes.Record, record.RecordID,
            AuditActions.StateChange, command.EmployeeId.ToString(), null, null,
            new
            {
                Action = "SECTION_SIGN_SNAPSHOT", step.SWStep, ItemGroupID = command.ItemGroupId,
                ActorEmployeeID = command.EmployeeId, SignerEmployeeID = signer.EmployeeID, SignedAt = signedAt
            },
            command.ActorKind));

        var save = await _uow.SaveChangesAsync(ct);
        if (save.Outcome == PersistenceSaveOutcome.UniqueConflict)
        {
            _uow.DiscardPendingChanges();
            return ApplicationResult<ExamSectionSignResult>.Fail(
                ApplicationFailureCode.InvalidState, "Mục khám đang được ký bởi lượt khác");
        }
        await tx.CommitAsync(ct);

        var progress = SigningProgressCalculator.Calculate(steps, record.SignSteps);

        return ApplicationResult<ExamSectionSignResult>.Success(new ExamSectionSignResult(
            command.ItemGroupId, step.SWStep, step.StepName, signer.EmployeeID, signedAt,
            Status: ExamRecordSignStepStatus.Signed,
            SigningProgressDone: progress.Done,
            SigningProgressTotal: progress.Total,
            SignedByEmployeeName: signer.EmployeeName,
            PerformedByEmployeeID: command.EmployeeId,
            PerformedByEmployeeName: command.EmployeeName ?? ""));
    }
}
```

- [ ] **Step 7: Chạy test, xác nhận pass**

Run: `dotnet test HealthExam.Tests/HealthExam.Tests.csproj --filter "FullyQualifiedName~SignExamSectionTests|FullyQualifiedName~SectionLockTests|FullyQualifiedName~ConclusionEligibility|FullyQualifiedName~ConclusionSigningTests|FullyQualifiedName~SectionSignConcurrencyTests"`
Expected: PASS.

- [ ] **Step 8: Commit**

```bash
git add HealthExam.Application/Signing HealthExam.Tests/Signing HealthExam.Tests/InMemoryTestDb.cs HealthExam.Tests/ConclusionEligibilityTests.cs
git commit -m "feat(signing): ký mục khám ghi Người xác nhận làm người ký, người bấm làm người thực hiện

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>"
```

---

### Task 4: Endpoint `POST .../sections/{itemGroupId}/sign` nhận body

**Files:**
- Modify: `HealthExam.API/Contracts/HisEmrModels.cs` (cuối file)
- Modify: `HealthExam.API/Controllers/ExamRecordController.cs:484-503` (action `SignExamSection`)
- Modify: `HealthExam.Tests/Signing/SignExamSectionEndpointTests.cs`
- Modify: `HealthExam.Tests/API/ApiContractSurfaceTests.cs:131-139`

**Interfaces:**
- Produces `public sealed record ExamSectionSignRequest(long? ConfirmedByEmployeeID = null, DateTime? SignedAt = null);` (namespace `HealthExam.API.Contracts`).
- Consumes `SignExamSectionCommand` (Task 3).

- [ ] **Step 1: Sửa fake + test không-body — chạy TRƯỚC (bước duy nhất không đoán được kết quả từ code)**

Trong `SignExamSectionEndpointTests.cs`, thay class fake ở cuối file:

```csharp
/// <summary>
/// Chỉ nạp <see cref="IHisEmrClient.ListSignRoleEmployeesAsync"/> — mọi phương thức khác dùng
/// default interface implementation (báo NotFound), không endpoint nào trong bài test này chạm tới.
/// </summary>
internal sealed class FakeHisEmrClientForEndpoint : IHisEmrClient
{
    private readonly HisClientResult<System.Collections.Generic.IReadOnlyList<SignRoleEmployee>> _members;

    public FakeHisEmrClientForEndpoint(HisClientResult<System.Collections.Generic.IReadOnlyList<SignRoleEmployee>> members)
        => _members = members;

    public static FakeHisEmrClientForEndpoint With(params SignRoleEmployee[] members)
        => new(HisClientResult<System.Collections.Generic.IReadOnlyList<SignRoleEmployee>>.Success(members));

    public Task<HisClientResult<HisJsonDocument>> SendAsync(
        HisOperation operation, HisRequest request, CancellationToken ct = default)
        => Task.FromResult(HisClientResult<HisJsonDocument>.Fail(HisClientOutcome.NotFound, "Not implemented"));

    public Task<HisClientResult<System.Collections.Generic.IReadOnlyList<SignRoleEmployee>>> ListSignRoleEmployeesAsync(
        long roleId, HisCallContext context, CancellationToken ct = default)
        => Task.FromResult(_members);
}
```

Sửa test `Endpoint_lay_role_tu_HIS_va_ghi_snapshot`: fake = `FakeHisEmrClientForEndpoint.With(new SignRoleEmployee(AuthTestHost.EmployeeId, "NV001", "BS Token"))`; giữ `PostAsync(url, null)` (không body, không Content-Type); assert giữ nguyên, thêm `Assert.Equal(AuthTestHost.EmployeeId, body["Data"]!["PerformedByEmployeeID"]!.Value<long>());`.

Sửa test `Endpoint_tra_502_khi_HIS_khong_tra_duoc_role`: fake = `new FakeHisEmrClientForEndpoint(HisClientResult<System.Collections.Generic.IReadOnlyList<SignRoleEmployee>>.Fail(HisClientOutcome.BadGateway, "x"))`; assert giữ nguyên.

Thêm DTO vào `HisEmrModels.cs` (cuối file):

```csharp
/// <summary>
/// Body TÙY CHỌN của POST sections/{itemGroupId}/sign (PROJ-2374). Không gửi ⇒ người ký = người bấm.
/// </summary>
public sealed record ExamSectionSignRequest(long? ConfirmedByEmployeeID = null, DateTime? SignedAt = null);
```

Sửa action trong `ExamRecordController.cs` (bỏ `ResolveSignRoleIdsAsync` khỏi action này; giữ helper cho eligibility/kết luận; thêm `using Microsoft.AspNetCore.Mvc.ModelBinding;`, `using HealthExam.API.Contracts;` nếu thiếu; nếu file chưa bật nullable thì bỏ `?`):

```csharp
    [HttpPost("{recordId:guid}/sections/{itemGroupId:int}/sign")]
    [SwaggerOperation(
        Summary = "Ký số một mục khám",
        Description = "Ghi nhận người ký (Người xác nhận được chọn, hoặc chính người bấm nếu không chọn) và khóa mục đó. Chữ ký chỉ được đóng lên PDF ở bước ký kết luận.")]
    public async Task<ActionResult<ResultData<ExamSectionSignResult>>> SignExamSection(
        Guid recordId, int itemGroupId,
        // EmptyBodyBehavior.Allow: client cũ POST không body/không Content-Type vẫn vào handler thay vì 415.
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] ExamSectionSignRequest? request,
        CancellationToken ct = default)
    {
        var res = await _signExamSectionHandler.HandleAsync(new SignExamSectionCommand(
            HealthExamContext.DivisionId,
            recordId,
            itemGroupId,
            HealthExamContext.ActorId,
            HealthExamContext.ActorCode,
            HealthExamContext.ActorName,
            HealthExamContext.ActorKind,
            Credential,
            HealthExamContext.TraceId,
            request?.ConfirmedByEmployeeID,
            request?.SignedAt), ct);
        return ToActionResult(res);
    }
```

Run: `dotnet test HealthExam.Tests/HealthExam.Tests.csproj --filter "FullyQualifiedName~SignExamSectionEndpointTests.Endpoint_lay_role_tu_HIS_va_ghi_snapshot"`
Expected: PASS (200, không 415). **Nếu 415**: bỏ tham số `[FromBody]`, đọc thủ công đầu action —

```csharp
        ExamSectionSignRequest? request = null;
        if (Request.ContentLength > 0)
            request = await System.Text.Json.JsonSerializer.DeserializeAsync<ExamSectionSignRequest>(
                Request.Body, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true }, ct);
```

rồi chạy lại tới PASS trước khi sang Step 2.

- [ ] **Step 2: Thêm test có body + 403 + contract**

(Code đã có từ Step 1 nên các test này phải PASS ngay; FAIL là bug — sửa handler/controller, không sửa test.)

```csharp
    /// <summary>Body JSON (FE 19/09): người ký = ConfirmedByEmployeeID, giờ ký = SignedAt, người thực hiện = token.</summary>
    [Fact]
    public async Task Endpoint_nhan_body_chon_nguoi_xac_nhan()
    {
        var hisClient = FakeHisEmrClientForEndpoint.With(
            new SignRoleEmployee(AuthTestHost.EmployeeId, "NV001", "BS Token"),
            new SignRoleEmployee(40, "40", "BS. Nguyễn Văn An"));
        var certGateway = new FakeCertificateGateway();
        certGateway.WithCertificate.Add("40");

        await using var factory = BuildFactory(hisClient, certGateway, $"sign-section-endpoint-{Guid.NewGuid():N}");
        var recordId = await SeedRecordWithSignStepMapAsync(factory);

        var client = CreateEmployeeClient(factory);
        var response = await client.PostAsync(
            $"/v1/exam-records/{recordId}/sections/{ItemGroupId}/sign",
            new StringContent("{\"ConfirmedByEmployeeID\":40,\"SignedAt\":\"2026-09-19T01:30:00.000Z\"}",
                System.Text.Encoding.UTF8, "application/json"));
        var body = JObject.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(40, body["Data"]!["SignedByEmployeeID"]!.Value<long>());
        Assert.Equal("BS. Nguyễn Văn An", body["Data"]!["SignedByEmployeeName"]!.Value<string>());
        Assert.Equal(AuthTestHost.EmployeeId, body["Data"]!["PerformedByEmployeeID"]!.Value<long>());
        Assert.Equal(new DateTime(2026, 9, 19, 1, 30, 0, DateTimeKind.Utc),
            body["Data"]!["SignedAt"]!.Value<DateTime>().ToUniversalTime());

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HealthExamDbContext>();
        var snapshot = await db.ExamRecordSignSteps.SingleAsync(x => x.RecordID == recordId);
        Assert.Equal("40", snapshot.SignedByEmployeeCode);
        Assert.Equal(40, snapshot.SignedByEmployeeID);
        Assert.Equal(AuthTestHost.EmployeeId, snapshot.PerformedByEmployeeID);
    }

    [Fact]
    public async Task Endpoint_tra_403_khi_nguoi_xac_nhan_khong_co_vai_tro()
    {
        var hisClient = FakeHisEmrClientForEndpoint.With(
            new SignRoleEmployee(AuthTestHost.EmployeeId, "NV001", "BS Token"));
        var certGateway = new FakeCertificateGateway();
        certGateway.WithCertificate.Add("NV001");

        await using var factory = BuildFactory(hisClient, certGateway, $"sign-section-endpoint-{Guid.NewGuid():N}");
        var recordId = await SeedRecordWithSignStepMapAsync(factory);

        var client = CreateEmployeeClient(factory);
        var response = await client.PostAsync(
            $"/v1/exam-records/{recordId}/sections/{ItemGroupId}/sign",
            new StringContent("{\"ConfirmedByEmployeeID\":9999}", System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HealthExamDbContext>();
        Assert.Empty(await db.ExamRecordSignSteps.Where(x => x.RecordID == recordId).ToListAsync());
    }
```

Trong `ApiContractSurfaceTests.cs`, sau các assert `sectionProps`:

```csharp
        var signReqProps = typeof(HealthExam.API.Contracts.ExamSectionSignRequest).GetProperties().Select(p => p.Name).ToList();
        Assert.Contains("ConfirmedByEmployeeID", signReqProps);
        Assert.Contains("SignedAt", signReqProps);
        var signResProps = typeof(HealthExam.Application.Signing.ExamSectionSignResult).GetProperties().Select(p => p.Name).ToList();
        Assert.Contains("SignedByEmployeeName", signResProps);
        Assert.Contains("PerformedByEmployeeID", signResProps);
        Assert.Contains("PerformedByEmployeeName", signResProps);
```

- [ ] **Step 3: Chạy test**

Run: `dotnet test HealthExam.Tests/HealthExam.Tests.csproj --filter "FullyQualifiedName~SignExamSectionEndpointTests|FullyQualifiedName~ApiContractSurfaceTests|FullyQualifiedName~ConclusionEndpointTests"`
Expected: PASS.

- [ ] **Step 4: Commit**

```bash
git add HealthExam.API/Contracts/HisEmrModels.cs HealthExam.API/Controllers/ExamRecordController.cs HealthExam.Tests/Signing/SignExamSectionEndpointTests.cs HealthExam.Tests/API/ApiContractSurfaceTests.cs
git commit -m "feat(api): POST sections/{itemGroupId}/sign nhận ConfirmedByEmployeeID + SignedAt (tùy chọn)

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>"
```

---

### Task 5: GET section trả `Signers[]` (khi chưa ký) + `CurrentStepSignedByEmployeeName`

**Files:**
- Modify: `HealthExam.Application/His/HisModels.cs` (`HisRecordSectionResult`)
- Modify: `HealthExam.Application/His/GetHisRecordSection.cs:186-215`
- Modify: `HealthExam.API/Contracts/HisEmrModels.cs` (`ExamFormSection`, `SignerChoice`)
- Modify: `HealthExam.API/Controllers/ExamRecordHisFormController.cs:88-99` và `:134-146`
- Modify: `HealthExam.Tests/API/ApiContractSurfaceTests.cs`
- Create: `HealthExam.Tests/Signing/GetHisRecordSectionSignersTests.cs`

**Interfaces:**
- Produces `HisRecordSectionResult(..., int SigningProgressTotal = 0, string? CurrentStepSignedByEmployeeName = null, IReadOnlyList<SignRoleEmployee>? Signers = null)` — handler LUÔN truyền list; fake test cũ bỏ trống ⇒ null, controller `?? Array.Empty`.
- Produces contract `public sealed record SignerChoice(long EmployeeID, string EmployeeCode, string EmployeeName);` và `ExamFormSection(..., int SigningProgressTotal = 0, string? CurrentStepSignedByEmployeeName = null, IReadOnlyList<SignerChoice>? Signers = null)` — controller luôn truyền list nên trên dây `Signers` luôn là mảng.

- [ ] **Step 1: Viết test handler (fail)**

Tạo `HealthExam.Tests/Signing/GetHisRecordSectionSignersTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HealthExam.Application.Common;
using HealthExam.Application.His;
using HealthExam.Domain.ExamForms;
using HealthExam.Domain.ExamRecords;
using HealthExam.Infrastructure.Persistence.Repositories;
using Xunit;

namespace HealthExam.Tests.Signing;

/// <summary>
/// GET section mang sẵn danh sách người ký của bước (PROJ-2374) để FE vẽ dropdown "Người xác
/// nhận" mà không gọi thêm — chỉ khi bước chưa ký, vì đã ký thì hộp ký không mở và GetCodeList
/// amount=0 trả cả bệnh viện. Kèm tên người đã ký để dòng meta không phải in mã.
/// </summary>
public class GetHisRecordSectionSignersTests
{
    private const string DivisionId = "DIV01";
    private const string VariantCode = "KSK06-18T";
    private const int ItemGroupId = 101;

    [Fact]
    public async Task Buoc_chua_ky_thi_tra_danh_sach_nguoi_ky_theo_SWRoleID()
    {
        var (db, record, map) = Seed();
        var his = new FakeSignRoleEmployeesHisClient()
            .Add(45, 40, "40", "BS. Nguyễn Văn An")
            .Add(45, 41, "41", "BS. Trần Thị Bình")
            .Add(99, 77, "77", "Khác vai trò");
        SeedSnapshot(db, record, ExamRecordSignStepStatus.InProgress);

        var res = await Handler(db, map, his).HandleAsync(Query(record.RecordID));

        Assert.True(res.IsSuccess);
        Assert.Equal(new[] { 45L }, his.RequestedRoleIds);
        Assert.Equal(new[] { 40L, 41L }, res.Value.Signers.Select(s => s.EmployeeID));
        Assert.Null(res.Value.CurrentStepSignedByEmployeeName);
    }

    [Fact]
    public async Task Buoc_da_ky_thi_khong_goi_HIS_va_tra_ten_nguoi_ky()
    {
        var (db, record, map) = Seed();
        var his = new FakeSignRoleEmployeesHisClient().Add(45, 41, "41", "BS. Trần Thị Bình");
        SeedSnapshot(db, record, ExamRecordSignStepStatus.Signed);

        var res = await Handler(db, map, his).HandleAsync(Query(record.RecordID));

        Assert.True(res.IsSuccess);
        Assert.Empty(his.RequestedRoleIds);
        Assert.Empty(res.Value.Signers);
        Assert.Equal("BS. Trần Thị Bình", res.Value.CurrentStepSignedByEmployeeName);
        Assert.Equal(41, res.Value.CurrentStepSignedByEmployeeID);
    }

    /// <summary>HIS lỗi không được làm hỏng màn khám: danh sách rỗng, request vẫn thành công.</summary>
    [Fact]
    public async Task HIS_loi_thi_Signers_rong_va_van_thanh_cong()
    {
        var (db, record, map) = Seed();
        var his = new FakeSignRoleEmployeesHisClient { Fail = true };

        var res = await Handler(db, map, his).HandleAsync(Query(record.RecordID));

        Assert.True(res.IsSuccess);
        Assert.Empty(res.Value.Signers);
    }

    [Fact]
    public async Task Muc_khong_co_buoc_ky_thi_khong_goi_HIS_va_Signers_rong()
    {
        var (db, record, _) = Seed();
        var emptyMap = new FakeSignStepMapRepository();
        var his = new FakeSignRoleEmployeesHisClient().Add(45, 40, "40", "BS. Nguyễn Văn An");

        var res = await Handler(db, emptyMap, his).HandleAsync(Query(record.RecordID));

        Assert.True(res.IsSuccess);
        Assert.Empty(res.Value.Signers);
        Assert.Empty(his.RequestedRoleIds);
    }

    private static GetHisRecordSectionQuery Query(Guid recordId)
        => new(DivisionId, recordId, ItemGroupId, "Bearer t", "trace");

    private static void SeedSnapshot(InMemoryTestDb db, ExamRecord record, string status)
    {
        var signed = status == ExamRecordSignStepStatus.Signed;
        db.RecordOf(record.RecordID).SignSteps = new List<ExamRecordSignStep>
        {
            new()
            {
                DivisionID = DivisionId, RecordID = record.RecordID, VariantCode = VariantCode,
                ItemGroupID = ItemGroupId, SWStep = 1, StepName = "Khám thể lực", SWRoleID = 45,
                Status = status,
                SignedByEmployeeID = signed ? 41 : null,
                SignedByEmployeeCode = signed ? "41" : "",
                SignedByEmployeeName = signed ? "BS. Trần Thị Bình" : "",
                SignedAt = signed ? DateTime.UtcNow : null
            }
        };
        db.Db.SaveChanges();
        db.Db.ChangeTracker.Clear();
    }

    private static (InMemoryTestDb Db, ExamRecord Record, FakeSignStepMapRepository Map) Seed()
    {
        var db = new InMemoryTestDb();
        var session = db.SeedSession(divisionId: DivisionId);
        var record = db.SeedRecord(session.SessionID, ExamRecordState.InProgress, divisionId: DivisionId);
        db.SetVariantCode(record.RecordID, VariantCode);

        var map = new FakeSignStepMapRepository();
        map.Steps.Add(new SignStepMap
        {
            ID = Guid.NewGuid(), DivisionID = DivisionId, VariantCode = VariantCode,
            SWStep = 1, ItemGroupID = ItemGroupId, StepName = "Khám thể lực", SignTitle = "Khám thể lực",
            SWRoleID = 45, SignType = 1, SLType = 2, SearchPattern = "##{S1}##", IsActive = true
        });
        return (db, record, map);
    }

    private static GetHisRecordSectionHandler Handler(
        InMemoryTestDb db, FakeSignStepMapRepository map, FakeSignRoleEmployeesHisClient his)
        => new(new ExamRecordRepository(db.Db), new FakeDefinitionHandler(), his,
            new HealthExam.Infrastructure.Persistence.UnitOfWork(db.Db), icd10Handler: null, signStepMap: map);

    private sealed class FakeDefinitionHandler : IGetHisFormDefinitionHandler
    {
        public Task<ApplicationResult<HisFormDefinitionResult>> HandleAsync(
            GetHisFormDefinitionQuery query, CancellationToken ct = default)
            => Task.FromResult(ApplicationResult<HisFormDefinitionResult>.Success(new HisFormDefinitionResult(
                Guid.NewGuid(), "KSK-TREN18TUOI", "Mẫu", 1, "V1", false, true,
                $"[{{\"ItemGroupID\":{ItemGroupId}}}]", "[]")));
    }
}
```

(`SeedRecord` mặc định không có `AdmissionID` nên handler không gọi `REMR`; nếu có, `FakeSignRoleEmployeesHisClient.SendAsync` trả NotFound và handler nuốt lỗi — vẫn OK.)

- [ ] **Step 2: Chạy test, xác nhận fail**

Run: `dotnet test HealthExam.Tests/HealthExam.Tests.csproj --filter "FullyQualifiedName~GetHisRecordSectionSignersTests"`
Expected: build error `Signers`/`CurrentStepSignedByEmployeeName` chưa có.

- [ ] **Step 3: Mở rộng result + handler**

`HisModels.cs` — `HisRecordSectionResult` thêm 2 tham số cuối (không khai lại property trong thân record — positional record bắt member cùng tên phải cùng kiểu, dễ vấp CS8866):

```csharp
    int SigningProgressTotal = 0,
    string? CurrentStepSignedByEmployeeName = null,
    System.Collections.Generic.IReadOnlyList<SignRoleEmployee>? Signers = null);
```

`GetHisRecordSection.cs` — thay khối từ `string? currentStepStatus = null;` tới `return`:

```csharp
        string? currentStepStatus = null;
        long? currentStepSignedByEmployeeId = null;
        string? currentStepSignedByEmployeeName = null;
        DateTime? currentStepSignedAt = null;
        var progressDone = 0;
        var progressTotal = 0;
        IReadOnlyList<SignRoleEmployee> signers = Array.Empty<SignRoleEmployee>();

        if (_signStepMap != null)
        {
            var mapSteps = await _signStepMap.ListAsync(query.DivisionId, record.VariantCode, ct);
            var progress = SigningProgressCalculator.Calculate(mapSteps, record.SignSteps);
            progressDone = progress.Done;
            progressTotal = progress.Total;

            var currentMapStep = mapSteps.FirstOrDefault(s => !s.IsConclusionStep && s.ItemGroupID == query.ItemGroupId);
            if (currentMapStep != null)
            {
                var snapshot = record.SignSteps?.FirstOrDefault(s =>
                    s.VariantCode == record.VariantCode && s.SWStep == currentMapStep.SWStep);
                if (snapshot != null)
                {
                    currentStepStatus = snapshot.Status;
                    currentStepSignedByEmployeeId = snapshot.SignedByEmployeeID;
                    currentStepSignedByEmployeeName = string.IsNullOrWhiteSpace(snapshot.SignedByEmployeeName)
                        ? null : snapshot.SignedByEmployeeName;
                    currentStepSignedAt = snapshot.SignedAt;
                }

                // Danh sách "Người xác nhận" cho hộp ký (PROJ-2374), chỉ khi bước chưa ký — đã ký thì hộp
                // không mở, khỏi tốn một lượt GetCodeList amount=0 trả cả bệnh viện. HIS lỗi ⇒ rỗng, không
                // hỏng màn khám — cùng chính sách với đọc REMR phía trên; FE báo "không tải được danh sách".
                if (_client != null && snapshot?.Status != ExamRecordSignStepStatus.Signed)
                {
                    var members = await _client.ListSignRoleEmployeesAsync(currentMapStep.SWRoleID,
                        new HisCallContext(query.Credential, query.TraceId, query.DivisionId), ct);
                    if (members.IsSuccess) signers = members.Value;
                }
            }
        }

        return ApplicationResult<HisRecordSectionResult>.Success(
            new HisRecordSectionResult(
                record.RecordID,
                record.AdmissionID,
                query.ItemGroupId,
                sectionLayout.ToJsonString(),
                record.State,
                currentStepStatus,
                currentStepSignedByEmployeeId,
                currentStepSignedAt,
                progressDone,
                progressTotal,
                currentStepSignedByEmployeeName,
                signers));
```

- [ ] **Step 4: Chạy test handler, xác nhận pass**

Run: `dotnet test HealthExam.Tests/HealthExam.Tests.csproj --filter "FullyQualifiedName~GetHisRecordSectionSignersTests|FullyQualifiedName~SectionSigningProgressTests"`
Expected: PASS.

- [ ] **Step 5: Contract + controller mapping**

`ApiContractSurfaceTests.cs`, cạnh các `sectionProps` assert:

```csharp
        Assert.Contains("CurrentStepSignedByEmployeeName", sectionProps);
        Assert.Contains("Signers", sectionProps);
        var signerProps = typeof(HealthExam.API.Contracts.SignerChoice).GetProperties().Select(p => p.Name).ToList();
        Assert.Equal(new[] { "EmployeeID", "EmployeeCode", "EmployeeName" }, signerProps);
```

`HisEmrModels.cs`:

```csharp
public sealed record SignerChoice(long EmployeeID, string EmployeeCode, string EmployeeName);
public sealed record ExamFormSection(
    Guid RecordId,
    long? AdmissionId,
    int ItemGroupId,
    JArray Layout,
    short RecordState = 1,
    string? CurrentStepStatus = null,
    long? CurrentStepSignedByEmployeeID = null,
    DateTime? CurrentStepSignedAt = null,
    int SigningProgressDone = 0,
    int SigningProgressTotal = 0,
    string? CurrentStepSignedByEmployeeName = null,
    System.Collections.Generic.IReadOnlyList<SignerChoice>? Signers = null);
```

`ExamRecordHisFormController.cs`: cả hai chỗ `new ExamFormSection(...)` (GET và PUT) thêm 2 đối số cuối (thêm `using System.Linq;` và `using HealthExam.Application.His;` nếu thiếu):

```csharp
            result.Value.SigningProgressTotal,
            result.Value.CurrentStepSignedByEmployeeName,
            (result.Value.Signers ?? Array.Empty<SignRoleEmployee>())
                .Select(s => new SignerChoice(s.EmployeeID, s.EmployeeCode, s.EmployeeName)).ToList());
```

- [ ] **Step 6: Chạy suite BE**

Run: `dotnet build HealthExamServer.sln && dotnet test HealthExam.Tests/HealthExam.Tests.csproj --no-build`
Expected: build sạch, tất cả PASS.

- [ ] **Step 7: Commit**

```bash
git add HealthExam.Application/His HealthExam.API/Contracts/HisEmrModels.cs HealthExam.API/Controllers/ExamRecordHisFormController.cs HealthExam.Tests/API/ApiContractSurfaceTests.cs HealthExam.Tests/Signing/GetHisRecordSectionSignersTests.cs
git commit -m "feat(api): GET his-form/sections trả Signers[] khi bước chưa ký và tên người đã ký

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>"
```

---

### Task 6: `Steps[]` (eligibility + kết luận) mang `PerformedBy*`

**Files:**
- Modify: `HealthExam.Application/Paraclinical/ParaclinicalModels.cs:106-114` (`ConclusionSignStepResult`)
- Modify: `HealthExam.Application/Paraclinical/GetConclusionEligibility.cs:71-76`
- Modify: `HealthExam.Application/Paraclinical/SignConclusion.cs:243-250`
- Modify: `HealthExam.API/Contracts/ParaclinicalModels.cs:186-196` (class `ConclusionSignStep`, tài liệu contract)
- Test: `HealthExam.Tests/Signing/ConclusionEligibilityFromMapTests.cs`

**Interfaces:**
- Produces `ConclusionSignStepResult(..., string SignedByEmployeeName, long? PerformedByEmployeeID = null, string PerformedByEmployeeName = "")`.

- [ ] **Step 1: Viết test (fail)**

Thêm vào `ConclusionEligibilityFromMapTests` (helper `SignSection(int)` và `Eligibility(long[] roleIds = null)` đã có ở ~146-155 trong fixture của file — đọc đầu file để lấy đúng tên class fixture, ở đây gọi là `Fixture`):

```csharp
    /// <summary>Dòng meta FE đọc "Người thực hiện" từ Steps[] — BE phải trả PerformedBy* sau khi ký mục.</summary>
    [Fact]
    public async Task Steps_mang_PerformedBy_sau_khi_ky_muc()
    {
        var f = new Fixture();
        await f.SignSection(101);

        var res = await f.Eligibility();

        Assert.True(res.IsSuccess);
        var step = res.Value.Steps.Single(s => s.ItemGroupID == 101);
        Assert.Equal(1274, step.PerformedByEmployeeID);
        Assert.Equal("BS A", step.PerformedByEmployeeName);
        Assert.Equal(1274, step.SignedByEmployeeID);
    }
```

- [ ] **Step 2: Chạy test, xác nhận fail**

Run: `dotnet test HealthExam.Tests/HealthExam.Tests.csproj --filter "FullyQualifiedName~ConclusionEligibilityFromMapTests.Steps_mang_PerformedBy"`
Expected: build error `PerformedByEmployeeID` chưa có.

- [ ] **Step 3: Thêm trường + mapping**

`ParaclinicalModels.cs` (Application):

```csharp
public sealed record ConclusionSignStepResult(
    int SwStep,
    string StepName,
    int? ItemGroupID,
    long SwRoleId,
    string Status,
    long? SignedByEmployeeID,
    DateTime? SignedAt,
    string SignedByEmployeeName,
    long? PerformedByEmployeeID = null,
    string PerformedByEmployeeName = "");
```

`GetConclusionEligibility.cs:71-76` và `SignConclusion.cs:246-249`: sau `snap?.SignedByEmployeeName ?? ""` thêm hai đối số `snap?.PerformedByEmployeeID, snap?.PerformedByEmployeeName ?? ""`.

`HealthExam.API/Contracts/ParaclinicalModels.cs` class `ConclusionSignStep`: thêm

```csharp
    public long? PerformedByEmployeeID { get; set; }
    public string PerformedByEmployeeName { get; set; } = "";
```

- [ ] **Step 4: Chạy test**

Run: `dotnet test HealthExam.Tests/HealthExam.Tests.csproj --filter "FullyQualifiedName~ConclusionEligibility|FullyQualifiedName~ConclusionSigningTests|FullyQualifiedName~ConclusionEndpointTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add HealthExam.Application/Paraclinical HealthExam.API/Contracts/ParaclinicalModels.cs HealthExam.Tests/Signing/ConclusionEligibilityFromMapTests.cs
git commit -m "feat(api): Steps[] eligibility/kết luận mang PerformedByEmployeeID/Name

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>"
```

---

### Task 7: Docs API + đẩy nhánh BE

**Files:**
- Modify: `docs/api/conclusion-pdf-signing-guide.md` (§1 bảng endpoint, §2 GET section, §3 DTO)
- Modify: `docs/api/his-section-signing-api.md` §3.3 (ghi chú trỏ sang guide)

- [ ] **Step 1: Cập nhật guide**

§1 dòng endpoint 1: cột body đổi từ `rỗng` thành ``tùy chọn `{ConfirmedByEmployeeID?, SignedAt?}` `` và thêm ngay dưới bảng:

```markdown
**Body `POST sections/{itemGroupId}/sign` (PROJ-2374, tùy chọn):**

```json
{ "ConfirmedByEmployeeID": 40, "SignedAt": "2026-09-19T01:30:00.000Z" }
```

- `ConfirmedByEmployeeID` — Người xác nhận, **là người ký thật**: snapshot ghi `SignedBy*` = người này và chứng thư của họ đóng lên PDF ở kết luận. Phải nằm trong `Signers[]` của `GET his-form/sections/{itemGroupId}` (HIS `GetCodeList?key=EmployeeRole&filterCode={SWRoleID}&amount=0`); không thuộc ⇒ `403` "Người xác nhận không có vai trò ký của bước …". Bỏ trống/`null` ⇒ người ký = người bấm (vẫn phải có vai trò).
- `SignedAt` — thời gian thực hiện, ghi vào `SignedAt` và là **ngày ký in trên PDF**. Tương lai quá 5 phút ⇒ `400`. Bỏ trống ⇒ now.
- Người bấm KHÔNG cần vai trò ký; được ghi vào `PerformedByEmployeeID/Name`. HIS không trả được danh sách ⇒ `502`, không ghi snapshot. Không body/không Content-Type vẫn hợp lệ.
- **Deploy:** FE trước hoặc cùng lúc BE. FE cũ gửi `ConfirmedByEmployeeID` mock nên BE mới trước sẽ 403 mọi lượt ký. Migration `AddSignStepPerformedBy` áp trước khi bump tag.
```

§2 (liệt kê trường của `GET/PUT his-form/sections/{itemGroupId}`): thêm

```markdown
- `CurrentStepSignedByEmployeeName` — tên người đã ký bước (null khi chưa ký).
- `Signers[] {EmployeeID, EmployeeCode, EmployeeName}` — nhân viên giữ `SWRoleID` của bước, để FE vẽ dropdown "Người xác nhận"; chỉ nạp khi bước chưa `Signed`; `[]` khi mục không có bước ký, đã ký, hoặc HIS lỗi (FE báo không tải được danh sách và ký bằng tài khoản của mình).
```

§3 `ExamSectionSignResult`: thêm `"SignedByEmployeeName": "BS. Nguyễn Văn An", "PerformedByEmployeeID": 1274, "PerformedByEmployeeName": "BS A"` vào JSON mẫu; `ConclusionEligibilityResult.Steps[]` mẫu thêm `"PerformedByEmployeeID": 1274, "PerformedByEmployeeName": "BS A"` ở bước đã ký và `null`/`""` ở bước chưa ký.

`his-section-signing-api.md` §3.3, thêm dòng đầu mục: `> Đường ký mục khám hiện hành (không qua process HIS) là `POST /v1/exam-records/{recordId}/sections/{itemGroupId}/sign` — xem `conclusion-pdf-signing-guide.md` §1 (body tùy chọn chọn Người xác nhận, PROJ-2374).`

- [ ] **Step 2: Kiểm tra sạch, commit, push**

```bash
git diff --check
git add docs/api
git commit -m "docs(api): body chọn Người xác nhận khi ký mục khám, Signers[] ở GET section, PerformedBy* ở Steps[]

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>"
git push -u origin feat/section-sign-choose-signer
```

(Tạo MR sau khi push thành công — memory `gitlab-mr-push-option-pitfall`: chỉ `-o merge_request.create` khi commit đã sạch hook.)

---

### Task 8: FE — types + hộp Ký số đọc `Signers` thật (khoan dung khi rỗng)

**Repo:** `turbo-web`. Tạo nhánh: `git checkout -b feat/section-sign-choose-signer main`.

**Files:**
- Modify: `packages/types/src/health-exam/signing.ts`
- Modify: `packages/types/src/health-exam/his-form.ts` (`HisFormSection`)
- Modify: `apps/health-exam/src/features/results/types/sign-section-form.ts`
- Modify: `apps/health-exam/src/features/results/components/clinical-tab/category-sign-form.tsx`
- Modify: `apps/health-exam/src/features/results/components/clinical-tab/category-confirm-dialog.tsx`
- Delete: `apps/health-exam/src/constants/mock-doctors.ts`
- Modify: `packages/i18n/src/locales/vi.json`, `packages/i18n/src/locales/en.json` (`healthCheckup.results.clinical.signForm.noSigners`)
- Create: `apps/health-exam/test/features/results/components/clinical-tab/category-sign-form.test.tsx`

**Interfaces:**
- Produces `export interface SignerChoice { EmployeeID: number; EmployeeCode: string; EmployeeName: string; }` (signing.ts).
- Produces `ExamSectionSignPayload.ConfirmedByEmployeeID: number | null`.
- Produces `HisFormSection.CurrentStepSignedByEmployeeName?: string | null; HisFormSection.Signers?: SignerChoice[]` (optional — BE cũ không trả).
- Produces `signSectionFormSchema(requireConfirmer: boolean)`, `signSectionFormDefaults(defaultConfirmedBy?: string)`.
- Produces props `CategorySignForm({ formId, signers, currentEmployeeId, onSubmit })` và `CategoryConfirmDialog({ ..., signers, currentEmployeeId })`.

- [ ] **Step 1: Viết test form ký (fail)**

`category-sign-form.test.tsx`:

```tsx
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";

import type { SignerChoice } from "@medviet/types/health-exam/signing";
import CategorySignForm from "~/features/results/components/clinical-tab/category-sign-form";

const SIGNERS: SignerChoice[] = [
  { EmployeeID: 1274, EmployeeCode: "1274", EmployeeName: "BS. Nguyễn Văn An" },
  { EmployeeID: 4210, EmployeeCode: "4210", EmployeeName: "BS. Trần Thị Bình" },
];

/**
 * Hộp Ký số (PROJ-2374): ô Người xác nhận lấy từ `Signers[]` của GET section
 * (nhân viên giữ vai trò ký của bước), mặc định là chính người đăng nhập nếu
 * có trong danh sách. Danh sách rỗng (HIS lỗi / BE cũ) → vẫn ký được bằng tài
 * khoản của mình, payload không mang `ConfirmedByEmployeeID`.
 */
describe("CategorySignForm", () => {
  it("liệt kê Signers và chọn sẵn người đăng nhập", async () => {
    const user = userEvent.setup();
    render(
      <CategorySignForm currentEmployeeId={4210} formId="f" onSubmit={vi.fn()} signers={SIGNERS} />,
    );

    const combobox = screen.getByRole("combobox", { name: "Người xác nhận" });
    expect(combobox).toHaveTextContent("BS. Trần Thị Bình");
    await user.click(combobox);
    expect(await screen.findByRole("option", { name: "BS. Nguyễn Văn An" })).toBeVisible();
  });

  it("người đăng nhập không có trong Signers thì để trống và bắt chọn", async () => {
    const user = userEvent.setup();
    const onSubmit = vi.fn();
    render(
      <>
        <CategorySignForm currentEmployeeId={7} formId="f" onSubmit={onSubmit} signers={SIGNERS} />
        <button form="f" type="submit">go</button>
      </>,
    );
    expect(screen.getByText("Chọn bác sĩ…")).toBeVisible();
    await user.click(screen.getByRole("button", { name: "go" }));
    expect(await screen.findByText("Vui lòng chọn người xác nhận.")).toBeVisible();
    expect(onSubmit).not.toHaveBeenCalled();
  });

  it("Signers rỗng thì báo không tải được, khoá ô chọn nhưng vẫn gửi được (không ConfirmedByEmployeeID)", async () => {
    const user = userEvent.setup();
    const onSubmit = vi.fn();
    render(
      <>
        <CategorySignForm currentEmployeeId={4210} formId="f" onSubmit={onSubmit} signers={[]} />
        <button form="f" type="submit">go</button>
      </>,
    );
    expect(
      screen.getByText("Không tải được danh sách người xác nhận — sẽ ký bằng tài khoản của bạn."),
    ).toBeVisible();
    expect(screen.getByRole("combobox", { name: "Người xác nhận" })).toBeDisabled();
    await user.click(screen.getByRole("button", { name: "go" }));
    expect(onSubmit).toHaveBeenCalledTimes(1);
    expect(onSubmit.mock.calls[0][0].ConfirmedByEmployeeID).toBeNull();
    expect(onSubmit.mock.calls[0][1]).toBe("");
  });
});
```

- [ ] **Step 2: Chạy test, xác nhận fail**

Run: `pnpm --filter health-exam test -- test/features/results/components/clinical-tab/category-sign-form.test.tsx`
Expected: FAIL (props chưa có; text chưa có).

- [ ] **Step 3: Types**

`signing.ts`:

```ts
/** Một nhân viên giữ vai trò ký của bước — phần tử `Signers[]` của `GET .../his-form/sections/{itemGroupId}`. */
export interface SignerChoice {
  EmployeeID: number;
  EmployeeCode: string;
  EmployeeName: string;
}

/**
 * Body của `POST .../sections/{itemGroupId}/sign` (BE nhận từ PROJ-2374).
 * `ConfirmedByEmployeeID` là NGƯỜI KÝ THẬT (chứng thư của họ đóng lên PDF);
 * `null` khi không có danh sách để chọn → BE ký bằng người bấm. `SignedAt` ISO.
 */
export interface ExamSectionSignPayload {
  ConfirmedByEmployeeID: number | null;
  SignedAt: string;
}
```

`ExamSectionSignResult` thêm: `SignedByEmployeeName: string | null; PerformedByEmployeeID: number | null; PerformedByEmployeeName: string | null;`. `ConclusionSignStep` thêm (optional, BE cũ không trả): `PerformedByEmployeeID?: number | null; PerformedByEmployeeName?: string | null;`.

`his-form.ts` — `HisFormSection` thêm (import `SignerChoice` từ `./signing`):

```ts
  /** Tên người đã ký bước — `null` khi chưa ký. Optional: BE trước PROJ-2374 không trả. */
  CurrentStepSignedByEmployeeName?: string | null;
  /** Nhân viên giữ vai trò ký của bước, chỉ khi bước chưa ký; `[]`/thiếu khi không có bước, đã ký, HIS lỗi hoặc BE cũ. */
  Signers?: SignerChoice[];
```

- [ ] **Step 4: Schema + component**

`sign-section-form.ts`:

```ts
/**
 * `requireConfirmer` = có danh sách để chọn. Không có (HIS lỗi / BE cũ) thì ô
 * bỏ trống hợp lệ và payload không mang `ConfirmedByEmployeeID` — BE ký bằng người bấm.
 */
export function signSectionFormSchema(requireConfirmer: boolean) {
  return z.object({
    confirmedBy: requireConfirmer
      ? z.string().min(1, { error: "Vui lòng chọn người xác nhận." })
      : z.string(),
    signedDate: z.string().regex(ISO_DATE_PATTERN, { error: "Vui lòng chọn ngày ký." }),
    signedTime: z.string().regex(/^\d{2}:\d{2}(:\d{2})?$/, { error: "Vui lòng nhập giờ ký." }),
  });
}

export type SignSectionFormValues = z.infer<ReturnType<typeof signSectionFormSchema>>;

/** Mặc định = bây giờ, theo giờ máy; Người xác nhận = người đăng nhập nếu nằm trong Signers. */
export function signSectionFormDefaults(defaultConfirmedBy = ""): SignSectionFormValues {
  const now = dayjs();
  return {
    confirmedBy: defaultConfirmedBy,
    signedDate: now.format(ISO_DATE_FORMAT),
    signedTime: now.format(TIME_WITH_SECONDS_FORMAT),
  };
}

export function toSignSectionPayload(values: SignSectionFormValues): ExamSectionSignPayload {
  return {
    ConfirmedByEmployeeID: values.confirmedBy ? Number(values.confirmedBy) : null,
    SignedAt: dayjs(`${values.signedDate}T${values.signedTime}`).toISOString(),
  };
}
```

`category-sign-form.tsx`: xóa import `MOCK_DOCTORS` + hằng `DOCTOR_ITEMS`; phần đầu component:

```tsx
export default function CategorySignForm({
  formId,
  signers,
  currentEmployeeId,
  onSubmit,
}: {
  formId: string;
  /** `Signers[]` của GET section — nhân viên giữ vai trò ký của bước. Rỗng ⇒ chế độ khoan dung. */
  signers: SignerChoice[];
  /** `EmployeeID` của người đăng nhập — chọn sẵn nếu có trong `signers`. */
  currentEmployeeId: number | null;
  /** Payload gửi kèm `signExamSection` + tên hiển thị cho dòng meta ("" khi không chọn). */
  onSubmit: (payload: ExamSectionSignPayload, confirmedByName: string) => void;
}) {
  const { t, i18n } = useTranslation();
  const items = signers.map((s) => ({ value: String(s.EmployeeID), label: s.EmployeeName }));
  const noSigners = signers.length === 0;
  const defaultConfirmedBy =
    currentEmployeeId != null && signers.some((s) => s.EmployeeID === currentEmployeeId)
      ? String(currentEmployeeId)
      : "";
  const form = useForm({
    resolver: zodResolver(signSectionFormSchema(!noSigners)),
    defaultValues: signSectionFormDefaults(defaultConfirmedBy),
  });
```

Trong `handleSubmit`: `const doctor = items.find((item) => item.value === values.confirmedBy);`. `Select`: `items={items}`, `disabled={noSigners}`; `SelectTrigger` thêm `disabled={noSigners}`; map `items` thay `DOCTOR_ITEMS`. Dưới `</Select>`:

```tsx
            {noSigners ? (
              <p className="text-[11px] text-muted-foreground" role="status">
                {t(`${I18N}.noSigners`)}
              </p>
            ) : null}
```

`category-confirm-dialog.tsx`: props thêm `signers: SignerChoice[]; currentEmployeeId: number | null;` và truyền xuống `<CategorySignForm currentEmployeeId={currentEmployeeId} signers={signers} ... />`. Nút xác nhận KHÔNG disable theo `signers` (chế độ khoan dung).

i18n `vi.json` `healthCheckup.results.clinical.signForm.noSigners`: `"Không tải được danh sách người xác nhận — sẽ ký bằng tài khoản của bạn."`; `en.json`: `"Could not load the confirmer list — signing as yourself."`.

Xóa `apps/health-exam/src/constants/mock-doctors.ts` (`git rm`).

- [ ] **Step 5: Chạy test form + typecheck**

Run: `pnpm --filter health-exam test -- test/features/results/components/clinical-tab/category-sign-form.test.tsx && pnpm --filter health-exam typecheck`
Expected: test PASS; typecheck còn lỗi ở `clinical-tab.tsx` (thiếu props) — xử lý ở Task 9.

- [ ] **Step 6: Commit**

```bash
git add packages/types/src/health-exam packages/i18n/src/locales apps/health-exam/src/features/results/types/sign-section-form.ts apps/health-exam/src/features/results/components/clinical-tab/category-sign-form.tsx apps/health-exam/src/features/results/components/clinical-tab/category-confirm-dialog.tsx apps/health-exam/test/features/results/components/clinical-tab/category-sign-form.test.tsx
git rm apps/health-exam/src/constants/mock-doctors.ts
git commit -m "feat(health-exam): hộp Ký số đọc Signers từ GET section thay mock, khoan dung khi không có danh sách

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>"
```

---

### Task 9: FE — nối `clinical-tab`, dòng meta 3 ô, test tích hợp

**Files:**
- Modify: `apps/health-exam/src/features/results/components/clinical-tab/clinical-tab.tsx` (~268-272 hooks, ~756 `CategoryConfirmDialog`)
- Modify: `apps/health-exam/src/features/results/components/clinical-tab/category-header.tsx:59-95`
- Modify: `apps/health-exam/test/features/results/components/clinical-tab/category-header.test.tsx`
- Modify: `apps/health-exam/test/features/results/components/clinical-tab/clinical-tab.test.tsx` (~124 `sectionWithHeight`, ~226 `getCurrentUser`, ~676-760 test ký)
- Modify: `apps/health-exam/test/fixtures/his-form-section-67-kham-the-luc.json` (thêm 2 trường)

**Interfaces:**
- Consumes `useCurrentUserQuery()` (`~/hooks/api/auth`) → `data?.EmployeeID`.
- Consumes cache `healthExamHisFormQueryKeys.bySection(recordId, itemGroupId)` (`HisFormSection`).

- [ ] **Step 1: Sửa test tích hợp (fail)**

`clinical-tab.test.tsx`:
- `sectionWithHeight`: sau khi clone, gán

```ts
  section.CurrentStepSignedByEmployeeName = null;
  section.Signers = [
    { EmployeeID: 1274, EmployeeCode: "1274", EmployeeName: "BS. Nguyễn Văn An" },
    { EmployeeID: 4210, EmployeeCode: "4210", EmployeeName: "BS. Trần Thị Bình" },
  ];
```

- Fixture JSON: thêm `"CurrentStepSignedByEmployeeName": null, "Signers": []` sau `"ItemGroupId": 67`.
- Test ký (~676): `getCurrentUser` mock hiện `EmployeeID: 1` (không trong Signers) → ô trống, giữ đoạn "chưa chọn → lỗi". `signExamSection.mockResolvedValue({...})` thêm `SignedByEmployeeName: "BS. Trần Thị Bình", PerformedByEmployeeID: 1, PerformedByEmployeeName: "BS. Đang Đăng Nhập"`. Trong `getConclusionEligibility` `Steps[0]` đổi `SignedByEmployeeID: 4210`, thêm `SignedByEmployeeName: "BS. Trần Thị Bình", PerformedByEmployeeID: 1, PerformedByEmployeeName: "BS. Đang Đăng Nhập"`. Assert dòng meta: thay `getByText("NV 9999")` bằng `getByText("BS. Đang Đăng Nhập")`; giữ `getByText("BS. Trần Thị Bình")` và giờ. `toHaveBeenCalledWith` giữ nguyên (`ConfirmedByEmployeeID: 4210`).
- Thêm test ngay sau test ký:

```tsx
  it("hộp Ký số chọn sẵn người đăng nhập khi có trong Signers", async () => {
    const user = userEvent.setup();
    getCurrentUser.mockResolvedValue({ ...ME, EmployeeID: 4210 });
    saveSection.mockResolvedValue(sectionWithHeight("170"));
    renderResults();
    await categoryList();
    const body = panel(/^KHÁM THỂ LỰC/);
    await user.click(screen.getByRole("button", { name: "Thêm" }));
    await user.type(within(body).getByLabelText("Chiều cao (cm)"), "170");
    await user.click(screen.getByRole("button", { name: "Lưu" }));
    await within(body).findByText("Đang khám");

    await user.click(screen.getByRole("button", { name: "Ký số" }));
    expect(
      screen.getByRole("combobox", { name: "Người xác nhận" }),
    ).toHaveTextContent("BS. Trần Thị Bình");
  });
```

(`ME` = object đang truyền cho `getCurrentUser.mockResolvedValue` ở ~226 — tách thành hằng nếu chưa có; `renderResults`/`categoryList`/`panel` là helper sẵn trong file — dùng đúng tên đang có.)

`category-header.test.tsx`: test đã ký — thêm vào `signStep`: `SignedByEmployeeName: "BS. Trần Thị Bình", PerformedByEmployeeID: 1234, PerformedByEmployeeName: "BS. Nguyễn Văn An"`; kỳ vọng "Người thực hiện" = `BS. Nguyễn Văn An`, "Người xác nhận" = `BS. Trần Thị Bình` (không còn đọc `confirmedByName` khi `SignedByEmployeeName` có), giờ = `signedAt` với giây. Test chưa ký giữ 3 dấu gạch.

- [ ] **Step 2: Chạy test, xác nhận fail**

Run: `pnpm --filter health-exam test -- test/features/results/components/clinical-tab`
Expected: FAIL ở clinical-tab (props/hook chưa nối) và header (chưa đọc `PerformedByEmployeeName`).

- [ ] **Step 3: Nối `clinical-tab.tsx`**

Cạnh `const eligibilityQuery = useConclusionEligibilityQuery(recordId);` thêm (import `useCurrentUserQuery` từ `~/hooks/api/auth`):

```tsx
  const currentUserQuery = useCurrentUserQuery();
  const currentEmployeeId = currentUserQuery.data?.EmployeeID ?? null;
```

Trước `return (`:

```tsx
  // Danh sách Người xác nhận của danh mục đang chọn — GET section đã nằm trong cache của
  // `CategoryPanel` trước khi [Ký số] bấm được (cùng lý do `submitCategory` đọc cache).
  // Thiếu (BE cũ / HIS lỗi) ⇒ [] ⇒ hộp ký ở chế độ khoan dung.
  const signers =
    confirmKind === "sign"
      ? (queryClient.getQueryData<HisFormSection>(
          healthExamHisFormQueryKeys.bySection(recordId, selectedItemGroupId),
        )?.Signers ?? [])
      : [];
```

`<CategoryConfirmDialog ... currentEmployeeId={currentEmployeeId} signers={signers} />`.

- [ ] **Step 4: Header meta 3 ô**

`category-header.tsx`:

```tsx
  const performedBy =
    signStep?.PerformedByEmployeeName ||
    (signStep?.PerformedByEmployeeID != null
      ? t(`${I18N}.signedByValue`, { id: signStep.PerformedByEmployeeID })
      : emptyValue);
  const confirmedBy =
    signStep?.SignedByEmployeeName ||
    category.confirmedByName ||
    (signStep?.SignedByEmployeeID != null
      ? t(`${I18N}.signedByValue`, { id: signStep.SignedByEmployeeID })
      : emptyValue);
  const signedAt = signStep ? (category.signedAt ?? signStep.SignedAt) : null;
  ...
        <MetaItem label={t(`${I18N}.performedBy`)} value={signStep ? performedBy : emptyValue} />
        <MetaItem label={t(`${I18N}.confirmedBy`)} value={signStep ? confirmedBy : emptyValue} />
```

(giữ `MetaItem` thời gian như cũ; không xóa key i18n nào). Cập nhật comment đầu file test header nếu mô tả cũ không còn đúng.

- [ ] **Step 5: Chạy test + lint + typecheck**

Run: `pnpm --filter health-exam test -- test/features/results && pnpm --filter health-exam typecheck && pnpm --filter health-exam lint`
Expected: PASS, không lỗi type/lint (kể cả sweep i18n key).

- [ ] **Step 6: Commit + push**

```bash
git add apps/health-exam packages/i18n packages/types
git commit -m "feat(health-exam): Người xác nhận mặc định = người đăng nhập, dòng meta đọc Người thực hiện/Người ký từ BE

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>"
git push -u origin feat/section-sign-choose-signer
```

---

## Self-Review

- **Spec coverage:** §1 cột `PerformedBy*` + migration → Task 2; §3.1 body + luật 1–6 → Task 3/4; §3.2 `Signers` (gate chưa ký) + tên → Task 5; §3.3 HIS client → Task 1; §3.4 `Steps[]` PerformedBy → Task 6; §4 FE (khoan dung, 3 ô meta) → Task 8/9; §5 (cert kiểm lúc ký, không body, audit, thứ tự deploy) → Task 3/4/7; §6 test → Task 1–6, 8–9; docs → Task 7. Hủy ký/kết luận: không đổi luật — không task.
- **Placeholder:** không có TBD; mọi bước code có nội dung; tên helper test FE (`renderResults`/`categoryList`/`panel`/`ME`) và fixture class trong `ConclusionEligibilityFromMapTests` ghi rõ "dùng đúng tên đang có" vì đọc từ file hiện hữu.
- **Type consistency:** `SignRoleEmployee(EmployeeID, EmployeeCode, EmployeeName)` thống nhất Task 1→3→5; `SignExamSectionCommand` 11 tham số (Task 3) khớp controller Task 4 và 5 call site test; `ExamSectionSignResult` +`SignedByEmployeeName`, `PerformedByEmployeeID`, `PerformedByEmployeeName` (Task 3) ↔ endpoint test Task 4 ↔ FE type Task 8; `ConclusionSignStepResult` +`PerformedBy*` (Task 6) ↔ FE `ConclusionSignStep` (Task 8) ↔ header Task 9; `HisRecordSectionResult.Signers`/`ExamFormSection.Signers` → FE `HisFormSection.Signers?: SignerChoice[]`; props `signers`/`currentEmployeeId` khớp Task 8 ↔ 9; `ExamSectionSignPayload.ConfirmedByEmployeeID: number | null` ↔ BE `long?`.
