# `/auth/me` theo HIS · Hồ sơ NB đầy đủ để fill form · `HisSignedFilePath` — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ba chỉnh sửa contract độc lập trên `health-exam-server` để FE `medviet/apps/health-exam` có đủ dữ liệu: (1) `GET /v1/auth/me` trả **đúng và đủ** 10 field HIS `api/Auth/Info` trả; (2) `GET /v1/patients/{id}` trả thêm mã danh mục + BHYT + nghề nghiệp + thân nhân để fill trọn form đăng ký; (3) mọi `ExamRecordResult` (kể cả `exam-history`) mang `HisSignedFilePath`.

**Architecture:** Clean Architecture 4 project (`Domain` → `Application` → `Infrastructure` / `API`). Không migration, không cột mới, không endpoint mới. Item 1 sửa `Application/Auth` + `Infrastructure/Integrations/HisEmr/HisHttpAuthGateway` + contract API. Item 2 thêm một method repository (`IPatientRepository.FindActiveProfileAsync`) trả `PatientProfileSnapshot` (Patient + 3 bảng con đã chọn) và mở rộng `PatientProfileResult` **flat, trùng tên field với `ExamRecordResult`**. Item 3 thêm một field optional ở cuối `ExamRecordResult` và map trong `ExamRecordRepository.MapToResult`.

**Tech Stack:** .NET 8, ASP.NET Core controllers, EF Core 8 + Npgsql (InMemory provider trong test), Newtonsoft.Json (envelope PascalCase), xUnit, `Microsoft.AspNetCore.Mvc.Testing` (`AuthTestHost`), `InMemoryTestDb`.

**Spec:** Thiết kế đã được duyệt trong chat ngày 18/09/2026 (bounded path, không có spec file). Tóm tắt quyết định:

- **Item 1 — `/v1/auth/me`.** HIS `api/Auth/Info` (`his-server/HIS.Server/Service/SAM/AuthService.cs:61-104`, class `UserInfo` dòng 478) trả đúng 10 field: `AccountID (long)`, `EmployeeID (long)`, `EmployeeCode`, `DivisionID`, `UserName`, `PhoneNumber`, `DepartmentID (int?)`, `DepartmentName (string?)`, `IsChangePassword (bool)`, `Permissions (Dictionary<string,int>, hiện HIS luôn để null)`. BE hiện map 5 field, trong đó `FullName`/`Title` **không tồn tại bên HIS** (luôn `null`). Quyết định: response `Data` mang đúng 10 field trên, **bỏ** `FullName`/`Title`, **đổi tên** `Username` → `UserName` cho khớp HIS (breaking với FE — ghi rõ trong docs). Giữ fallback `EmployeeID ?? ActorId`, `UserName ?? ActorName` như cũ.
- **Item 2 — `/v1/patients/{id}`.** Form FE (`record-form-defaults.ts`) dùng **Mã** (`ethnicityCode`, `identityIssuerCode`, `insuranceSubject`, `registrationPlaceCode`, `relativeRelationship`, `occupationCode`) nên `PatientProfileResult` trả thêm Code/Name dịch từ OptionID, và các block BHYT / nghề nghiệp / thân nhân, **flat, cùng tên field với `ExamRecordResult`** để FE tái dùng mapping `defaultsFromExamRecord`. Giữ nguyên 18 field cũ (kể cả OptionID). Chọn dòng con: một phiên bản `Patient` có thể có nhiều dòng active `PatientInsurance`/`PatientEmployment`/`PatientRelative` (`PatientRegistrationWriter.AttachChildFactsAsync` tạo dòng mới khi bộ giá trị khác và **tái dùng** dòng cũ khi trùng) ⇒ lấy theo **hồ sơ khám gần nhất của phiên bản này** (`ExamRecord` cùng `PatientRefID`, `CreatedDate DESC` → dùng đúng `InsuranceRefID`/`EmploymentRefID`/`RelativeRefID` của nó, `null` nghĩa là "lần đó không có"); **chỉ khi** phiên bản chưa có hồ sơ khám nào thì fallback dòng active mới nhất theo `CreatedDate`. `search`/`match` không đổi.
- **Item 3 — `HisSignedFilePath`.** `ExamRecord.HisSignedFilePath` đã có trong DB (migration `20260917055059_AddHisSignedFileInfoToExamRecord`), chưa expose. Thêm `string HisSignedFilePath = ""` ở **cuối** `ExamRecordResult` (sau `HisSignedAt`), map trong `MapToResult`. FE dùng nó làm điều kiện hiện nút; click gọi `GET /v1/exam-records/{id}/registration-form/preview` có sẵn (đã trả PDF ký khi `HisSignStatus = "Signed"`). Không thêm endpoint proxy.
- Ngoài phạm vi (ghi vào docs): `POST /v1/patients/match` vẫn so sánh theo OptionID; FE chưa có `master-data/registration-options` trả OptionID nên hai trường đó vẫn "khác" — cần ticket riêng.

## Global Constraints

- Chỉ sửa `health-exam-server`. Không sửa `frontend/medviet`, không sửa `his-server`.
- Làm trực tiếp trên nhánh hiện tại `feat/patient-exam-history`; không tạo git worktree.
- Không thêm migration, không thêm cột, không thêm NuGet package, không thêm endpoint.
- Mọi query phải lọc `DivisionID == HealthExamContext.DivisionId`.
- Response envelope giữ PascalCase (`HealthExamContractResolver`); tên field mới bám đúng tên đã nêu trong plan — FE đọc theo tên.
- `PatientProfileResult` giữ nguyên 18 field cũ và thứ tự; field mới nối vào **sau** `IsActive`. `ExamRecordResult` chỉ thêm **một** tham số optional ở **cuối** (mọi caller positional hiện có không vỡ).
- Thông báo lỗi tiếng Việt có dấu, giống handler hiện có. Không thêm `ApplicationFailureCode` mới.
- Chạy test: `dotnet test HealthExam.Tests/HealthExam.Tests.csproj --filter "FullyQualifiedName~<TestClass>"` từ thư mục gốc repo (`E:\MedViet\backend\health-exam-server`). Các test class trong plan này đều chạy InMemory — không cần `HEALTHEXAM_TEST_DB`.
- `HealthExam.Tests/API/CompositionRootTests.Di_resolves_all_application_handlers` bắt mọi `*Handler` phải đăng ký DI — plan này **không** thêm handler mới nên không cần sửa DI.
- Theo `CLAUDE.md`: trước khi sửa symbol có sẵn phải chạy `gitnexus_impact({target, direction: "upstream", repo: "health-exam-server"})` và báo blast radius cho user; HIGH/CRITICAL thì cảnh báo trước khi sửa; index báo stale thì chạy `npx gitnexus analyze` hoặc grep caller thủ công và ghi rõ. Trước mỗi commit chạy `gitnexus_detect_changes()`.
- Commit message conventional (`feat(auth): ...`, `feat(patient): ...`, `feat(exam-record): ...`, `docs: ...`) và kết thúc bằng dòng `Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>`.

---

## File Structure

| File | Trách nhiệm | Task |
|---|---|---|
| `HealthExam.Application/Auth/AuthModels.cs` | `CurrentUserResult` — shape nội bộ của "nhân viên đang đăng nhập" | 1 |
| `HealthExam.Infrastructure/Integrations/HisEmr/HisHttpAuthGateway.cs` | Map JSON HIS `Auth/Info` → `CurrentUserResult` | 1 |
| `HealthExam.API/Contracts/HisAuthModels.cs` | `HisCurrentUser` — DTO public của `GET /v1/auth/me` | 1 |
| `HealthExam.API/Controllers/AuthController.cs` | Map `CurrentUserResult` → `HisCurrentUser` | 1 |
| `HealthExam.Tests/Infrastructure/HisHttpAuthGatewayTests.cs`, `HealthExam.Tests/Application/AuthHandlerTests.cs` | Test mapping gateway + fake gateway | 1 |
| `docs/api/auth-me-api.md` (mới) | Contract `GET /v1/auth/me` cho FE, kèm breaking change | 1 |
| `HealthExam.Application/ExamRecords/ExamRecordModels.cs` | `ExamRecordResult` + `HisSignedFilePath` | 2 |
| `HealthExam.Infrastructure/Persistence/Repositories/ExamRecordRepository.cs` | `MapToResult` map field mới; `ResolveRelationshipName` ủy quyền sang helper chung | 2, 4 |
| `HealthExam.Tests/InMemoryTestDb.cs` | `SeedRecord(hisSignedFilePath:)` | 2 |
| `HealthExam.Tests/PatientExamHistoryTests.cs` | Test `HisSignedFilePath` xuất hiện trên kết quả | 2 |
| `docs/api/conclusion-pdf-signing-guide.md` | Ghi chú field `HisSignedFilePath` + cách FE mở PDF | 2 |
| `HealthExam.Application/Patients/PatientModels.cs` | `PatientProfileSnapshot` | 3 |
| `HealthExam.Application/Patients/IPatientRepository.cs` | `FindActiveProfileAsync` | 3 |
| `HealthExam.Infrastructure/Persistence/Repositories/PatientRepository.cs` | Implement chọn dòng con theo hồ sơ khám gần nhất | 3 |
| `HealthExam.Tests/Application/PatientRegistrationTestDoubles.cs` | `FakePatientRepository.FindActiveProfileAsync` | 3 |
| `HealthExam.Tests/Infrastructure/PatientRepositoryProfileTests.cs` (mới) | Test nhánh chọn dòng con trên InMemory | 3 |
| `HealthExam.Application/Patients/RelationshipNames.cs` (mới) | Bảng tên quan hệ dùng chung Application/Infrastructure | 4 |
| `HealthExam.Application/Patients/GetPatientProfile.cs` | `PatientProfileResult` mở rộng + handler dùng snapshot | 4 |
| `HealthExam.Tests/Application/GetPatientProfileTests.cs`, `HealthExam.Tests/PatientEndpointTests.cs` | Test handler + endpoint | 4 |
| `docs/api/patient-profile-search-and-reuse-api.md` §3b | Response mới của `GET /v1/patients/{id}` | 4 |

---

### Task 1: `GET /v1/auth/me` trả đúng 10 field HIS

**Files:**
- Modify: `HealthExam.Application/Auth/AuthModels.cs:26-31` (`CurrentUserResult`)
- Modify: `HealthExam.Infrastructure/Integrations/HisEmr/HisHttpAuthGateway.cs:65-91` (`GetCurrentUserAsync`)
- Modify: `HealthExam.API/Contracts/HisAuthModels.cs:18-25` (`HisCurrentUser`)
- Modify: `HealthExam.API/Controllers/AuthController.cs:52-74` (`Me`)
- Test: `HealthExam.Tests/Infrastructure/HisHttpAuthGatewayTests.cs:73-118`
- Test: `HealthExam.Tests/Application/AuthHandlerTests.cs:56`
- Create: `docs/api/auth-me-api.md`

**Interfaces:**
- Consumes: `HisHttpAuthGateway.SendAsync` (giữ nguyên), `GetCurrentUserQuery` (giữ nguyên).
- Produces: `CurrentUserResult(long? AccountID, long? EmployeeID, string? EmployeeCode, string? DivisionID, string? UserName, string? PhoneNumber, int? DepartmentID, string? DepartmentName, bool IsChangePassword, IReadOnlyDictionary<string,int>? Permissions)`; DTO `HisCurrentUser` cùng 10 property.

- [ ] **Step 1: Impact analysis**

Chạy `gitnexus_impact({target: "CurrentUserResult", direction: "upstream", repo: "health-exam-server"})` và `gitnexus_impact({target: "HisCurrentUser", direction: "upstream", repo: "health-exam-server"})`. Kỳ vọng caller: `HisHttpAuthGateway.GetCurrentUserAsync`, `GetCurrentUserHandler`, `AuthController.Me`, `AuthHandlerTests`, `HisHttpAuthGatewayTests`, `HisAuthEndpointTests`. Báo blast radius cho user. Nếu index stale: `grep -rn "CurrentUserResult\|HisCurrentUser" --include=*.cs .` (bỏ `bin/ obj/`) và ghi rõ.

- [ ] **Step 2: Sửa test gateway để mô tả shape mới (đỏ trước)**

Trong `HealthExam.Tests/Infrastructure/HisHttpAuthGatewayTests.cs`, thay **toàn bộ** hai test `Current_user_maps_the_stable_public_shape` và `Current_user_falls_back_to_query_without_inventing_description` bằng:

```csharp
    [Fact]
    public async Task Current_user_maps_every_field_HIS_Auth_Info_returns()
    {
        var (gateway, _) = CreateGateway(_ => OkEnvelope(new JObject
        {
            ["AccountID"] = 77,
            ["EmployeeID"] = 4210,
            ["EmployeeCode"] = "NV001",
            ["DivisionID"] = "DHTESTING",
            ["UserName"] = "Bác sĩ EMR",
            ["PhoneNumber"] = "0901234567",
            ["DepartmentID"] = 12,
            ["DepartmentName"] = "Khoa Khám bệnh",
            ["IsChangePassword"] = true,
            ["Permissions"] = new JObject { ["KSK.SIGN"] = 1, ["KSK.VIEW"] = 2 }
        }));

        var res = await gateway.GetCurrentUserAsync(new GetCurrentUserQuery(
            DivisionId: "DHTESTING",
            TraceId: "trace-1",
            ActorKind: ActorKind.Employee,
            ActorId: 4210,
            ActorName: "bs.hoa",
            AuthorizationHeader: "Bearer tok"));

        Assert.True(res.IsSuccess);
        Assert.Equal(77, res.Value.AccountID);
        Assert.Equal(4210, res.Value.EmployeeID);
        Assert.Equal("NV001", res.Value.EmployeeCode);
        Assert.Equal("DHTESTING", res.Value.DivisionID);
        Assert.Equal("Bác sĩ EMR", res.Value.UserName);
        Assert.Equal("0901234567", res.Value.PhoneNumber);
        Assert.Equal(12, res.Value.DepartmentID);
        Assert.Equal("Khoa Khám bệnh", res.Value.DepartmentName);
        Assert.True(res.Value.IsChangePassword);
        Assert.NotNull(res.Value.Permissions);
        Assert.Equal(1, res.Value.Permissions!["KSK.SIGN"]);
        Assert.Equal(2, res.Value.Permissions["KSK.VIEW"]);
    }

    [Fact]
    public async Task Current_user_falls_back_to_query_without_inventing_fields()
    {
        // HIS trả Permissions = null cho mọi tài khoản (AuthService.GetInfo không gán) —
        // giữ null, không dựng dictionary rỗng để FE phân biệt "không có" với "rỗng".
        var (gateway, _) = CreateGateway(_ => OkEnvelope(new JObject
        {
            ["Permissions"] = JValue.CreateNull()
        }));

        var res = await gateway.GetCurrentUserAsync(new GetCurrentUserQuery(
            DivisionId: "DHTESTING",
            TraceId: "trace-1",
            ActorKind: ActorKind.Employee,
            ActorId: 4210,
            ActorName: "bs.hoa",
            AuthorizationHeader: "Bearer tok"));

        Assert.True(res.IsSuccess);
        Assert.Null(res.Value.AccountID);
        Assert.Equal(4210, res.Value.EmployeeID);
        Assert.Equal("bs.hoa", res.Value.UserName);
        Assert.Null(res.Value.EmployeeCode);
        Assert.Null(res.Value.DivisionID);
        Assert.Null(res.Value.PhoneNumber);
        Assert.Null(res.Value.DepartmentID);
        Assert.Null(res.Value.DepartmentName);
        Assert.False(res.Value.IsChangePassword);
        Assert.Null(res.Value.Permissions);
    }
```

Trong `HealthExam.Tests/Application/AuthHandlerTests.cs:56` đổi dòng dựng `CurrentUserResult` thành:

```csharp
        gateway.CurrentUserOutcome = ApplicationResult<CurrentUserResult>.Success(new CurrentUserResult(
            AccountID: 1,
            EmployeeID: 10,
            EmployeeCode: "EMP10",
            DivisionID: "DEV",
            UserName: "doc",
            PhoneNumber: null,
            DepartmentID: null,
            DepartmentName: null,
            IsChangePassword: false,
            Permissions: null));
```

- [ ] **Step 3: Chạy test, xác nhận đỏ (lỗi biên dịch)**

Run: `dotnet test HealthExam.Tests/HealthExam.Tests.csproj --filter "FullyQualifiedName~HisHttpAuthGatewayTests|FullyQualifiedName~AuthHandlerTests"`
Expected: FAIL — build error `'CurrentUserResult' does not contain a definition for 'AccountID'` / `'UserName'`.

- [ ] **Step 4: Đổi `CurrentUserResult`**

Trong `HealthExam.Application/Auth/AuthModels.cs`, thêm `using System.Collections.Generic;` lên đầu (sau `#nullable enable`), và thay record `CurrentUserResult` bằng:

```csharp
/// <summary>
/// Đúng shape HIS <c>api/Auth/Info</c> trả (<c>UserInfo</c> bên his-server): không thêm,
/// không bớt, không đổi tên — FE đọc field theo tên HIS. HIS không có FullName/Title;
/// <c>UserName</c> là tên hiển thị của tài khoản (ví dụ "Bác sĩ EMR"), không phải login.
/// </summary>
public sealed record CurrentUserResult(
    long? AccountID,
    long? EmployeeID,
    string? EmployeeCode,
    string? DivisionID,
    string? UserName,
    string? PhoneNumber,
    int? DepartmentID,
    string? DepartmentName,
    bool IsChangePassword,
    IReadOnlyDictionary<string, int>? Permissions);
```

- [ ] **Step 5: Map đủ field trong gateway**

Trong `HealthExam.Infrastructure/Integrations/HisEmr/HisHttpAuthGateway.cs`, thêm `using System.Collections.Generic;` và thay khối dựng `result` trong `GetCurrentUserAsync` (dòng 83-88) bằng:

```csharp
        var result = new CurrentUserResult(
            AccountID: data.Value<long?>("AccountID"),
            EmployeeID: data.Value<long?>("EmployeeID") ?? query.ActorId,
            EmployeeCode: NullIfBlank(data.Value<string>("EmployeeCode")),
            DivisionID: NullIfBlank(data.Value<string>("DivisionID")),
            UserName: NullIfBlank(data.Value<string>("UserName")) ?? query.ActorName,
            PhoneNumber: NullIfBlank(data.Value<string>("PhoneNumber")),
            DepartmentID: data.Value<int?>("DepartmentID"),
            DepartmentName: NullIfBlank(data.Value<string>("DepartmentName")),
            IsChangePassword: data.Value<bool?>("IsChangePassword") ?? false,
            Permissions: ReadPermissions(data["Permissions"]));
```

và thêm helper ngay dưới `NullIfBlank`:

```csharp
    /// <summary>HIS gửi <c>Permissions</c> là object {code: int} hoặc null. Không phải object → null.</summary>
    private static IReadOnlyDictionary<string, int>? ReadPermissions(JToken? token)
    {
        if (token == null || token.Type != JTokenType.Object) return null;
        try
        {
            return token.ToObject<Dictionary<string, int>>();
        }
        catch (JsonException)
        {
            return null;
        }
    }
```

- [ ] **Step 6: Đổi DTO public và controller**

`HealthExam.API/Contracts/HisAuthModels.cs` — thêm `using System.Collections.Generic;` và thay class `HisCurrentUser`:

```csharp
/// <summary>Đúng 10 field HIS api/Auth/Info trả. Không có FullName/Title (HIS không có).</summary>
public sealed class HisCurrentUser
{
    public long? AccountID { get; set; }
    public long? EmployeeID { get; set; }
    public string? EmployeeCode { get; set; }
    public string? DivisionID { get; set; }
    public string? UserName { get; set; }
    public string? PhoneNumber { get; set; }
    public int? DepartmentID { get; set; }
    public string? DepartmentName { get; set; }
    public bool IsChangePassword { get; set; }
    public IReadOnlyDictionary<string, int>? Permissions { get; set; }
}
```

`HealthExam.API/Controllers/AuthController.cs` — thay lambda trong `Me`:

```csharp
        return ToActionResult(res, r => new HisCurrentUser
        {
            AccountID = r.AccountID,
            EmployeeID = r.EmployeeID,
            EmployeeCode = r.EmployeeCode,
            DivisionID = r.DivisionID,
            UserName = r.UserName,
            PhoneNumber = r.PhoneNumber,
            DepartmentID = r.DepartmentID,
            DepartmentName = r.DepartmentName,
            IsChangePassword = r.IsChangePassword,
            Permissions = r.Permissions
        });
```

và sửa `Description` của `[SwaggerOperation]` trên `Me` thành:
`"Chuyển tiếp token Bearer của nhân viên sang HIS api/Auth/Info và trả nguyên 10 field HIS trả (AccountID, EmployeeID, EmployeeCode, DivisionID, UserName, PhoneNumber, DepartmentID, DepartmentName, IsChangePassword, Permissions). Yêu cầu token hợp lệ của nhân viên."`

- [ ] **Step 7: Chạy test, xác nhận xanh**

Run: `dotnet test HealthExam.Tests/HealthExam.Tests.csproj --filter "FullyQualifiedName~HisHttpAuthGatewayTests|FullyQualifiedName~AuthHandlerTests|FullyQualifiedName~HisAuthEndpointTests|FullyQualifiedName~SwaggerBusinessDocumentationTests"`
Expected: PASS toàn bộ.

- [ ] **Step 8: Viết docs cho FE**

Tạo `docs/api/auth-me-api.md`:

````markdown
# `GET /v1/auth/me` — thông tin nhân viên đang đăng nhập

Backend chuyển tiếp Bearer token sang HIS `api/Auth/Info` và trả **nguyên** những gì HIS
trả. Không có field nào do health-exam-server tự bịa.

## Header

```http
Authorization: Bearer <token>
X-Division-Id: <division-id>
```

## Response

```json
{
  "ErrorCode": 0,
  "Message": "",
  "Data": {
    "AccountID": 77,
    "EmployeeID": 4210,
    "EmployeeCode": "NV001",
    "DivisionID": "DHTESTING",
    "UserName": "Bác sĩ EMR",
    "PhoneNumber": "0901234567",
    "DepartmentID": 12,
    "DepartmentName": "Khoa Khám bệnh",
    "IsChangePassword": false,
    "Permissions": null
  },
  "TraceID": "c37c1f3f2f6a4ea8"
}
```

| Trường | Kiểu | Ghi chú |
| --- | --- | --- |
| `AccountID` | `number \| null` | Khoá tài khoản `SAM_Account` bên HIS |
| `EmployeeID` | `number \| null` | Khoá nhân viên; fallback claim `EmployeeID` của JWT nếu HIS không gửi |
| `EmployeeCode` | `string \| null` | Mã nhân viên |
| `DivisionID` | `string \| null` | Đơn vị HIS cấp token |
| `UserName` | `string \| null` | **Tên hiển thị** của tài khoản ("Bác sĩ EMR"), không phải login. Fallback claim `UserName` của JWT |
| `PhoneNumber` | `string \| null` | `iAM_Employee.MobileNo`; tài khoản Admin không có nhân viên thì `""` → trả `null` |
| `DepartmentID` | `number \| null` | Khoa của nhân viên |
| `DepartmentName` | `string \| null` | Tên khoa |
| `IsChangePassword` | `boolean` | Tài khoản bị yêu cầu đổi mật khẩu; HIS không gửi → `false` |
| `Permissions` | `Record<string, number> \| null` | HIS hiện luôn trả `null`; giữ nguyên để FE không phải đoán |

## Thay đổi so với contract cũ (18/09/2026) — **breaking**

- `Username` → **`UserName`** (đúng tên HIS).
- **Bỏ** `FullName`, `Title`: HIS `Auth/Info` không có hai field này, trước đây luôn `null`.
  FE đang dựng tên hiển thị bằng `FullName?.trim() || Username` → đổi thành `UserName`.
- Thêm `AccountID`, `DivisionID`, `PhoneNumber`, `DepartmentID`, `DepartmentName`,
  `IsChangePassword`, `Permissions`.

## Lỗi

Giữ nguyên: `401`/`4010` token hết hạn hoặc HIS từ chối; `403`/`4030` token không phải nhân
viên; `5022` HIS chưa bật hoặc không kết nối được; `5023` HIS quá thời gian chờ.
````

- [ ] **Step 9: Commit**

Chạy `gitnexus_detect_changes()` — kỳ vọng chỉ chạm `CurrentUserResult`, `GetCurrentUserAsync`, `HisCurrentUser`, `AuthController.Me`, test tương ứng.

```bash
git add HealthExam.Application/Auth/AuthModels.cs HealthExam.Infrastructure/Integrations/HisEmr/HisHttpAuthGateway.cs HealthExam.API/Contracts/HisAuthModels.cs HealthExam.API/Controllers/AuthController.cs HealthExam.Tests/Infrastructure/HisHttpAuthGatewayTests.cs HealthExam.Tests/Application/AuthHandlerTests.cs docs/api/auth-me-api.md
git commit -m "feat(auth): return every field HIS Auth/Info exposes on GET /v1/auth/me

FullName/Title never existed on HIS and were always null; Username is
renamed UserName to match HIS. Adds AccountID, DivisionID, PhoneNumber,
DepartmentID, DepartmentName, IsChangePassword, Permissions.

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>"
```

---

### Task 2: `ExamRecordResult.HisSignedFilePath`

**Files:**
- Modify: `HealthExam.Application/ExamRecords/ExamRecordModels.cs:110-116` (`ExamRecordResult` — cuối record)
- Modify: `HealthExam.Infrastructure/Persistence/Repositories/ExamRecordRepository.cs:673-674` (`MapToResult` — hai dòng cuối)
- Modify: `HealthExam.Tests/InMemoryTestDb.cs:264-284,338-339` (`SeedRecord`)
- Test: `HealthExam.Tests/PatientExamHistoryTests.cs:19-33`
- Modify: `docs/api/conclusion-pdf-signing-guide.md` (sau khối "Ý nghĩa các trường quan trọng cho UI")

**Interfaces:**
- Consumes: `ExamRecord.HisSignedFilePath` (`HealthExam.Domain/ExamRecords/ExamRecord.cs:66`).
- Produces: `ExamRecordResult.HisSignedFilePath: string` (`""` khi chưa có), tham số positional cuối cùng có default `""`.

- [ ] **Step 1: Impact analysis**

Chạy `gitnexus_impact({target: "ExamRecordResult", direction: "upstream", repo: "health-exam-server"})` và `gitnexus_impact({target: "MapToResult", direction: "upstream", repo: "health-exam-server"})`. `ExamRecordResult` có nhiều caller (list/detail/history/import/tests) — vì chỉ thêm tham số optional ở cuối nên không caller nào phải sửa; báo blast radius và nói rõ điều đó cho user.

- [ ] **Step 2: Thêm tham số seed + test đỏ**

`HealthExam.Tests/InMemoryTestDb.cs` — thêm tham số vào `SeedRecord` ngay sau `DateTime? hisSignedAt = null,`:

```csharp
        string hisSignedFilePath = null,
```

và trong object initializer của `record`, sau `HisSignedAt = hisSignedAt` thêm:

```csharp
            HisSignedAt = hisSignedAt,
            HisSignedFilePath = hisSignedFilePath
```

Trong `HealthExam.Tests/PatientExamHistoryTests.cs`, sửa test `Ket_qua_ho_so_mang_theo_trang_thai_va_gio_ky_ket_luan`:

```csharp
    [Fact]
    public async Task Ket_qua_ho_so_mang_theo_trang_thai_gio_ky_va_duong_dan_file_ky()
    {
        using var db = new InMemoryTestDb();
        var session = db.SeedSession();
        var signedAt = new DateTime(2026, 3, 14, 9, 12, 0, DateTimeKind.Utc);
        db.SeedRecord(session.SessionID, recordCode: "KSK-001",
            hisSignStatus: "Signed", hisSignedAt: signedAt,
            hisSignedFilePath: "/Signed/2026/03/KSK-001.pdf");

        var repo = new ExamRecordRepository(db.Db);
        var page = await repo.ListAsync(FakeHealthExamContext.DefaultDivisionId, new ExamRecordFilter());

        var item = Assert.Single(page.Items);
        Assert.Equal("Signed", item.HisSignStatus);
        Assert.Equal(signedAt, item.HisSignedAt);
        Assert.Equal("/Signed/2026/03/KSK-001.pdf", item.HisSignedFilePath);
    }
```

và trong `Ho_so_chua_ky_tra_chuoi_rong_va_gio_ky_null` thêm assertion cuối:

```csharp
        Assert.Equal("", item.HisSignedFilePath);
```

- [ ] **Step 3: Chạy test, xác nhận đỏ**

Run: `dotnet test HealthExam.Tests/HealthExam.Tests.csproj --filter "FullyQualifiedName~PatientExamHistoryTests"`
Expected: FAIL — `'ExamRecordResult' does not contain a definition for 'HisSignedFilePath'`.

- [ ] **Step 4: Thêm field và map**

`HealthExam.Application/ExamRecords/ExamRecordModels.cs` — thay phần cuối `ExamRecordResult`:

```csharp
    /// <summary>Giờ ký kết luận hoàn tất; chỉ có giá trị khi HisSignStatus = "Signed".</summary>
    DateTime? HisSignedAt = null,

    /// <summary>
    /// Đường dẫn file PDF đã ký trên HIS (HIS trả về sau khi ký xong). "" khi chưa có.
    /// FE KHÔNG mở trực tiếp được (HIS api/Sign/ViewFile cần Bearer) — dùng nó làm điều kiện
    /// hiện nút và gọi GET /v1/exam-records/{id}/registration-form/preview để lấy PDF.
    /// </summary>
    string HisSignedFilePath = "");
```

`HealthExam.Infrastructure/Persistence/Repositories/ExamRecordRepository.cs` — cuối `MapToResult`:

```csharp
        HisSignStatus: x.HisSignStatus ?? "",
        HisSignedAt: x.HisSignedAt,
        HisSignedFilePath: x.HisSignedFilePath ?? "");
```

- [ ] **Step 5: Chạy test, xác nhận xanh**

Run: `dotnet test HealthExam.Tests/HealthExam.Tests.csproj --filter "FullyQualifiedName~PatientExamHistoryTests|FullyQualifiedName~PatientEndpointTests|FullyQualifiedName~ExamRecordListFilterTests"`
Expected: PASS.

- [ ] **Step 6: Docs**

Trong `docs/api/conclusion-pdf-signing-guide.md`, ngay sau khối liệt kê "Ý nghĩa các trường quan trọng cho UI" (kết thúc ở dòng "`SignedByEmployeeID` và `SignedAt`: ...") thêm:

```markdown

#### `HisSignedFilePath` trên mọi `ExamRecordItem` (18/09/2026)

`GET /v1/exam-records`, `GET /v1/exam-records/{id}` và `GET /v1/patients/{id}/exam-history`
trả thêm `HisSignedFilePath: string` — đường dẫn PDF đã ký mà HIS trả về sau khi ký xong;
`""` khi chưa ký. **FE không mở trực tiếp được** đường dẫn này (HIS `api/Sign/ViewFile` yêu
cầu Bearer của nhân viên và FE không nói chuyện thẳng với HIS). Cách dùng:

- `HisSignStatus === "Signed" && HisSignedFilePath !== ""` → hiện nút "Xem PDF".
- Bấm → `GET /v1/exam-records/{RecordID}/registration-form/preview` (đã trả đúng file ký khi
  hồ sơ ở trạng thái `Signed`), nhận `application/pdf` và mở blob.
```

- [ ] **Step 7: Commit**

Chạy `gitnexus_detect_changes()` — kỳ vọng chạm `ExamRecordResult`, `MapToResult`, `SeedRecord`, test history.

```bash
git add HealthExam.Application/ExamRecords/ExamRecordModels.cs HealthExam.Infrastructure/Persistence/Repositories/ExamRecordRepository.cs HealthExam.Tests/InMemoryTestDb.cs HealthExam.Tests/PatientExamHistoryTests.cs docs/api/conclusion-pdf-signing-guide.md
git commit -m "feat(exam-record): expose HisSignedFilePath on record results

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>"
```

---

### Task 3: Repository — `FindActiveProfileAsync` chọn BHYT / nghề nghiệp / thân nhân

**Files:**
- Modify: `HealthExam.Application/Patients/PatientModels.cs` (thêm `PatientProfileSnapshot` ở cuối file)
- Modify: `HealthExam.Application/Patients/IPatientRepository.cs:14-21`
- Modify: `HealthExam.Infrastructure/Persistence/Repositories/PatientRepository.cs:49-55` (thêm method ngay sau `FindActiveByRefIdAsync`)
- Modify: `HealthExam.Tests/Application/PatientRegistrationTestDoubles.cs:64-68` (thêm method vào `FakePatientRepository`)
- Create: `HealthExam.Tests/Infrastructure/PatientRepositoryProfileTests.cs`

**Interfaces:**
- Consumes: `Patient.IdentityIssuerOption`, `Patient.EthnicityOption`, `PatientInsurance.InsuranceObjectOption`, `PatientInsurance.RegistrationPlaceOption`, `PatientEmployment.OccupationOption`, `ExamRecord.{PatientRefID, InsuranceRefID, EmploymentRefID, RelativeRefID, CreatedDate}`.
- Produces:
  - `public sealed record PatientProfileSnapshot(Patient Patient, PatientInsurance Insurance, PatientEmployment Employment, PatientRelative Relative);` (namespace `HealthExam.Application.Patients`; `Insurance`/`Employment`/`Relative` có thể `null`).
  - `Task<PatientProfileSnapshot> IPatientRepository.FindActiveProfileAsync(string divisionId, Guid patientRefID, CancellationToken ct = default)` — `null` khi không có phiên bản active trong đơn vị.

- [ ] **Step 1: Impact analysis**

Chạy `gitnexus_impact({target: "IPatientRepository", direction: "upstream", repo: "health-exam-server"})`. Thêm method vào interface buộc mọi implementation phải thêm: chỉ có `PatientRepository` và `FakePatientRepository` (xác nhận bằng `grep -rn ": IPatientRepository" --include=*.cs .` bỏ `bin/ obj/`). Báo blast radius.

- [ ] **Step 2: Viết test repository (đỏ)**

Tạo `HealthExam.Tests/Infrastructure/PatientRepositoryProfileTests.cs`:

```csharp
using System;
using System.Threading.Tasks;
using HealthExam.Domain.Catalogs;
using HealthExam.Domain.ExamRecords;
using HealthExam.Domain.ExamSessions;
using HealthExam.Domain.Patients;
using HealthExam.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HealthExam.Tests.Infrastructure;

/// <summary>
/// `FindActiveProfileAsync`: một phiên bản Patient có thể có NHIỀU dòng active
/// PatientInsurance/PatientEmployment/PatientRelative (mỗi bộ giá trị khác là một dòng, dòng
/// cũ được tái dùng khi trùng). Quy tắc chọn: theo hồ sơ khám GẦN NHẤT của phiên bản; chưa có
/// hồ sơ khám nào thì dòng active mới nhất theo CreatedDate. Chạy InMemory — chốt nhánh LINQ.
/// </summary>
public sealed class PatientRepositoryProfileTests
{
    private static readonly DateTime T0 = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Tra_null_khi_phien_ban_khong_active_hoac_khac_don_vi()
    {
        using var db = new InMemoryTestDb();
        var inactive = SeedPatient(db, isActive: false);
        var otherDivision = SeedPatient(db, divisionId: "OTHER");
        var repo = new PatientRepository(db.Db);

        Assert.Null(await repo.FindActiveProfileAsync(db.Ctx.DivisionId, inactive.PatientRefID));
        Assert.Null(await repo.FindActiveProfileAsync(db.Ctx.DivisionId, otherDivision.PatientRefID));
        Assert.Null(await repo.FindActiveProfileAsync(db.Ctx.DivisionId, Guid.Empty));
    }

    [Fact]
    public async Task Nap_san_danh_muc_cua_patient_va_khong_co_dong_con_thi_ba_khoi_null()
    {
        using var db = new InMemoryTestDb();
        var issuer = db.SeedMaster(MasterDataCategories.IdentityIssuer, "CCS", "Cục Cảnh sát QLHC về TTXH");
        var ethnicity = db.SeedMaster(MasterDataCategories.Ethnicity, "KINH", "Kinh");
        var patient = SeedPatient(db, identityIssuerOptionID: issuer.OptionID, ethnicityOptionID: ethnicity.OptionID);
        var repo = new PatientRepository(db.Db);

        var snap = await repo.FindActiveProfileAsync(db.Ctx.DivisionId, patient.PatientRefID);

        Assert.NotNull(snap);
        Assert.Equal(patient.PatientRefID, snap.Patient.PatientRefID);
        Assert.Equal("CCS", snap.Patient.IdentityIssuerOption.Code);
        Assert.Equal("KINH", snap.Patient.EthnicityOption.Code);
        Assert.Null(snap.Insurance);
        Assert.Null(snap.Employment);
        Assert.Null(snap.Relative);
    }

    [Fact]
    public async Task Chon_dong_con_theo_ho_so_kham_gan_nhat_ke_ca_khi_no_tai_dung_dong_cu()
    {
        using var db = new InMemoryTestDb();
        var insObj = db.SeedMaster(MasterDataCategories.InsuranceObject, "HT", "Hưu trí");
        var place = db.SeedMaster(MasterDataCategories.RegistrationPlace, "79001", "BV Quận 1");
        var occ = db.SeedMaster(MasterDataCategories.Occupation, "GV", "Giáo viên");
        var patient = SeedPatient(db);

        // Dòng A tạo trước, dòng B tạo sau. Hồ sơ khám mới nhất lại dùng A (tái dùng dòng cũ).
        var insA = SeedInsurance(db, patient.PatientRefID, "DN4790000001", insObj.OptionID, place.OptionID, T0);
        var insB = SeedInsurance(db, patient.PatientRefID, "DN4790000002", null, null, T0.AddDays(1));
        var empA = SeedEmployment(db, patient.PatientRefID, occ.OptionID, "NV01", T0);
        var empB = SeedEmployment(db, patient.PatientRefID, null, "NV02", T0.AddDays(1));
        var relA = SeedRelative(db, patient.PatientRefID, "SPOUSE", "Trần Thị B", T0);
        var relB = SeedRelative(db, patient.PatientRefID, "CHILD", "Nguyễn Văn C", T0.AddDays(1));

        var session = db.SeedSession();
        SeedRecord(db, session.SessionID, patient.PatientRefID, insB.InsuranceRefID, empB.EmploymentRefID, relB.RelativeRefID, T0.AddDays(2));
        SeedRecord(db, session.SessionID, patient.PatientRefID, insA.InsuranceRefID, empA.EmploymentRefID, relA.RelativeRefID, T0.AddDays(3));

        var repo = new PatientRepository(db.Db);
        var snap = await repo.FindActiveProfileAsync(db.Ctx.DivisionId, patient.PatientRefID);

        Assert.NotNull(snap);
        Assert.Equal(insA.InsuranceRefID, snap.Insurance.InsuranceRefID);
        Assert.Equal("HT", snap.Insurance.InsuranceObjectOption.Code);
        Assert.Equal("79001", snap.Insurance.RegistrationPlaceOption.Code);
        Assert.Equal(empA.EmploymentRefID, snap.Employment.EmploymentRefID);
        Assert.Equal("GV", snap.Employment.OccupationOption.Code);
        Assert.Equal(relA.RelativeRefID, snap.Relative.RelativeRefID);
    }

    [Fact]
    public async Task Ho_so_kham_gan_nhat_khong_co_bhyt_thi_khong_muon_bhyt_cua_dot_cu()
    {
        using var db = new InMemoryTestDb();
        var patient = SeedPatient(db);
        var ins = SeedInsurance(db, patient.PatientRefID, "DN4790000001", null, null, T0);
        var rel = SeedRelative(db, patient.PatientRefID, "SPOUSE", "Trần Thị B", T0);
        var session = db.SeedSession();
        SeedRecord(db, session.SessionID, patient.PatientRefID, ins.InsuranceRefID, null, rel.RelativeRefID, T0.AddDays(1));
        SeedRecord(db, session.SessionID, patient.PatientRefID, null, null, rel.RelativeRefID, T0.AddDays(2));

        var repo = new PatientRepository(db.Db);
        var snap = await repo.FindActiveProfileAsync(db.Ctx.DivisionId, patient.PatientRefID);

        Assert.Null(snap.Insurance);
        Assert.Null(snap.Employment);
        Assert.Equal(rel.RelativeRefID, snap.Relative.RelativeRefID);
    }

    [Fact]
    public async Task Chua_co_ho_so_kham_thi_lay_dong_active_moi_nhat_theo_CreatedDate()
    {
        using var db = new InMemoryTestDb();
        var patient = SeedPatient(db);
        SeedInsurance(db, patient.PatientRefID, "DN4790000001", null, null, T0);
        var newer = SeedInsurance(db, patient.PatientRefID, "DN4790000002", null, null, T0.AddDays(1));
        var inactiveNewest = SeedInsurance(db, patient.PatientRefID, "DN4790000003", null, null, T0.AddDays(2), isActive: false);
        var rel = SeedRelative(db, patient.PatientRefID, "CHILD", "Nguyễn Văn C", T0);

        var repo = new PatientRepository(db.Db);
        var snap = await repo.FindActiveProfileAsync(db.Ctx.DivisionId, patient.PatientRefID);

        Assert.Equal(newer.InsuranceRefID, snap.Insurance.InsuranceRefID);
        Assert.NotEqual(inactiveNewest.InsuranceRefID, snap.Insurance.InsuranceRefID);
        Assert.Null(snap.Employment);
        Assert.Equal(rel.RelativeRefID, snap.Relative.RelativeRefID);
    }

    [Fact]
    public async Task Dong_con_cua_phien_ban_khac_khong_bi_lan_sang()
    {
        using var db = new InMemoryTestDb();
        var patient = SeedPatient(db);
        var other = SeedPatient(db);
        SeedInsurance(db, other.PatientRefID, "DN4790000009", null, null, T0.AddDays(5));

        var repo = new PatientRepository(db.Db);
        var snap = await repo.FindActiveProfileAsync(db.Ctx.DivisionId, patient.PatientRefID);

        Assert.NotNull(snap);
        Assert.Null(snap.Insurance);
    }

    // ---- seed helpers: dựng thẳng entity, chỉ những cột InMemory cần ----

    private static Patient SeedPatient(
        InMemoryTestDb db,
        bool isActive = true,
        string divisionId = null,
        Guid? identityIssuerOptionID = null,
        Guid? ethnicityOptionID = null)
    {
        var id = Guid.NewGuid();
        var patient = new Patient
        {
            PatientRefID = id,
            DivisionID = divisionId ?? db.Ctx.DivisionId,
            FullName = "Nguyễn Văn An",
            IdentityNumber = "079123456789",
            GenderID = 1,
            IdentityIssuerOptionID = identityIssuerOptionID,
            EthnicityOptionID = ethnicityOptionID,
            IsActive = isActive,
            ProfileLineageID = id,
            CreatedDate = T0
        };
        db.Db.Patients.Add(patient);
        db.Db.SaveChanges();
        db.Db.ChangeTracker.Clear();
        return patient;
    }

    private static PatientInsurance SeedInsurance(
        InMemoryTestDb db, Guid patientRefID, string number, Guid? objectOptionID, Guid? placeOptionID,
        DateTime createdDate, bool isActive = true)
    {
        var row = new PatientInsurance
        {
            InsuranceRefID = Guid.NewGuid(),
            DivisionID = db.Ctx.DivisionId,
            PatientRefID = patientRefID,
            InsuranceNumber = number,
            InsuranceObjectOptionID = objectOptionID,
            RegistrationPlaceOptionID = placeOptionID,
            ValidFrom = new DateOnly(2026, 1, 1),
            ValidTo = new DateOnly(2026, 12, 31),
            IsActive = isActive,
            CreatedDate = createdDate
        };
        db.Db.PatientInsurances.Add(row);
        db.Db.SaveChanges();
        db.Db.ChangeTracker.Clear();
        return row;
    }

    private static PatientEmployment SeedEmployment(
        InMemoryTestDb db, Guid patientRefID, Guid? occupationOptionID, string staffCode, DateTime createdDate)
    {
        var row = new PatientEmployment
        {
            EmploymentRefID = Guid.NewGuid(),
            DivisionID = db.Ctx.DivisionId,
            PatientRefID = patientRefID,
            OccupationOptionID = occupationOptionID,
            StaffCode = staffCode,
            OrgDeptName = "Phòng Hành chính",
            JobTitle = "Chuyên viên",
            IsActive = true,
            CreatedDate = createdDate
        };
        db.Db.PatientEmployments.Add(row);
        db.Db.SaveChanges();
        db.Db.ChangeTracker.Clear();
        return row;
    }

    private static PatientRelative SeedRelative(
        InMemoryTestDb db, Guid patientRefID, string relationshipCode, string fullName, DateTime createdDate)
    {
        var row = new PatientRelative
        {
            RelativeRefID = Guid.NewGuid(),
            DivisionID = db.Ctx.DivisionId,
            PatientRefID = patientRefID,
            RelationshipCode = relationshipCode,
            FullName = fullName,
            IdentityNumber = "079000000001",
            PhoneNumber = "0912345678",
            IsActive = true,
            CreatedDate = createdDate
        };
        db.Db.PatientRelatives.Add(row);
        db.Db.SaveChanges();
        db.Db.ChangeTracker.Clear();
        return row;
    }

    private static void SeedRecord(
        InMemoryTestDb db, Guid sessionID, Guid patientRefID,
        Guid? insuranceRefID, Guid? employmentRefID, Guid? relativeRefID, DateTime createdDate)
    {
        db.Db.ExamRecords.Add(new ExamRecord
        {
            RecordID = Guid.NewGuid(),
            DivisionID = db.Ctx.DivisionId,
            SessionID = sessionID,
            RecordCode = "KSK-" + Guid.NewGuid().ToString("N")[..8],
            PatientRefID = patientRefID,
            InsuranceRefID = insuranceRefID,
            EmploymentRefID = employmentRefID,
            RelativeRefID = relativeRefID,
            VariantCode = "DTK_01",
            FormCode = ExamGroups.FormCodePrefix + "DTK_01",
            State = ExamRecordState.Completed,
            CreatedDate = createdDate
        });
        db.Db.SaveChanges();
        db.Db.ChangeTracker.Clear();
    }
}
```

- [ ] **Step 3: Chạy test, xác nhận đỏ**

Run: `dotnet test HealthExam.Tests/HealthExam.Tests.csproj --filter "FullyQualifiedName~PatientRepositoryProfileTests"`
Expected: FAIL — `'PatientRepository' does not contain a definition for 'FindActiveProfileAsync'`.

- [ ] **Step 4: Thêm snapshot record + interface**

Cuối `HealthExam.Application/Patients/PatientModels.cs` thêm:

```csharp
/// <summary>
/// Hồ sơ người bệnh active kèm ba dòng con ĐÃ CHỌN để điền form: BHYT / nghề nghiệp / thân nhân
/// như lần đăng ký gần nhất của phiên bản này. Từng dòng con có thể null.
/// </summary>
public sealed record PatientProfileSnapshot(
    Patient Patient,
    PatientInsurance Insurance,
    PatientEmployment Employment,
    PatientRelative Relative);
```

Trong `HealthExam.Application/Patients/IPatientRepository.cs`, sau `FindActiveByRefIdAsync` thêm:

```csharp
    /// <summary>
    /// Phiên bản active của PatientRefID trong đơn vị, nạp sẵn IdentityIssuerOption/EthnicityOption,
    /// kèm dòng BHYT / nghề nghiệp / thân nhân theo HỒ SƠ KHÁM GẦN NHẤT của phiên bản đó
    /// (CreatedDate DESC; ref null nghĩa là lần đó không khai). Chưa có hồ sơ khám nào thì lấy
    /// dòng active mới nhất theo CreatedDate. null = không active hoặc khác đơn vị.
    /// </summary>
    Task<PatientProfileSnapshot> FindActiveProfileAsync(string divisionId, Guid patientRefID, CancellationToken ct = default);
```

- [ ] **Step 5: Implement trong `PatientRepository`**

Trong `HealthExam.Infrastructure/Persistence/Repositories/PatientRepository.cs`, ngay sau `FindActiveByRefIdAsync` thêm:

```csharp
    public async Task<PatientProfileSnapshot> FindActiveProfileAsync(string divisionId, Guid patientRefID, CancellationToken ct = default)
    {
        if (patientRefID == Guid.Empty) return null;

        var patient = await _db.Patients.AsNoTracking()
            .Include(p => p.IdentityIssuerOption)
            .Include(p => p.EthnicityOption)
            .FirstOrDefaultAsync(p => p.DivisionID == divisionId && p.PatientRefID == patientRefID && p.IsActive, ct);
        if (patient == null) return null;

        // Dòng con đúng như lần đăng ký gần nhất của phiên bản này. Không lấy "dòng mới nhất":
        // PatientRegistrationWriter tái dùng dòng cũ khi bộ giá trị trùng, nên dòng mới nhất
        // theo CreatedDate có thể không phải dòng vừa được dùng.
        var latest = await _db.ExamRecords.AsNoTracking()
            .Where(x => x.DivisionID == divisionId && x.PatientRefID == patientRefID)
            .OrderByDescending(x => x.CreatedDate)
            .Select(x => new { x.InsuranceRefID, x.EmploymentRefID, x.RelativeRefID })
            .FirstOrDefaultAsync(ct);

        var insurances = _db.PatientInsurances.AsNoTracking()
            .Include(i => i.InsuranceObjectOption)
            .Include(i => i.RegistrationPlaceOption)
            .Where(i => i.DivisionID == divisionId && i.PatientRefID == patientRefID);
        var employments = _db.PatientEmployments.AsNoTracking()
            .Include(e => e.OccupationOption)
            .Where(e => e.DivisionID == divisionId && e.PatientRefID == patientRefID);
        var relatives = _db.PatientRelatives.AsNoTracking()
            .Where(r => r.DivisionID == divisionId && r.PatientRefID == patientRefID);

        PatientInsurance insurance;
        PatientEmployment employment;
        PatientRelative relative;
        if (latest != null)
        {
            insurance = latest.InsuranceRefID.HasValue
                ? await insurances.FirstOrDefaultAsync(i => i.InsuranceRefID == latest.InsuranceRefID.Value, ct)
                : null;
            employment = latest.EmploymentRefID.HasValue
                ? await employments.FirstOrDefaultAsync(e => e.EmploymentRefID == latest.EmploymentRefID.Value, ct)
                : null;
            relative = latest.RelativeRefID.HasValue
                ? await relatives.FirstOrDefaultAsync(r => r.RelativeRefID == latest.RelativeRefID.Value, ct)
                : null;
        }
        else
        {
            insurance = await insurances.Where(i => i.IsActive).OrderByDescending(i => i.CreatedDate).FirstOrDefaultAsync(ct);
            employment = await employments.Where(e => e.IsActive).OrderByDescending(e => e.CreatedDate).FirstOrDefaultAsync(ct);
            relative = await relatives.Where(r => r.IsActive).OrderByDescending(r => r.CreatedDate).FirstOrDefaultAsync(ct);
        }

        return new PatientProfileSnapshot(patient, insurance, employment, relative);
    }
```

- [ ] **Step 6: Thêm method vào `FakePatientRepository`**

Trong `HealthExam.Tests/Application/PatientRegistrationTestDoubles.cs`, sau `FindActiveByRefIdAsync` thêm (fake không có ExamRecord — chỉ nhánh fallback "dòng active mới nhất"; nhánh theo hồ sơ khám đã chốt ở `PatientRepositoryProfileTests`):

```csharp
    public Task<PatientProfileSnapshot> FindActiveProfileAsync(string divisionId, Guid patientRefID, CancellationToken ct = default)
    {
        var patient = Patients.FirstOrDefault(p => p.DivisionID == divisionId && p.PatientRefID == patientRefID && p.IsActive);
        if (patient == null) return Task.FromResult<PatientProfileSnapshot>(null);

        var insurance = Insurances.Where(i => i.DivisionID == divisionId && i.PatientRefID == patientRefID && i.IsActive)
            .OrderByDescending(i => i.CreatedDate).FirstOrDefault();
        var employment = Employments.Where(e => e.DivisionID == divisionId && e.PatientRefID == patientRefID && e.IsActive)
            .OrderByDescending(e => e.CreatedDate).FirstOrDefault();
        var relative = Relatives.Where(r => r.DivisionID == divisionId && r.PatientRefID == patientRefID && r.IsActive)
            .OrderByDescending(r => r.CreatedDate).FirstOrDefault();

        return Task.FromResult(new PatientProfileSnapshot(patient, insurance, employment, relative));
    }
```

- [ ] **Step 7: Chạy test, xác nhận xanh**

Run: `dotnet test HealthExam.Tests/HealthExam.Tests.csproj --filter "FullyQualifiedName~PatientRepositoryProfileTests|FullyQualifiedName~GetPatientProfileTests|FullyQualifiedName~MatchPatientProfileTests"`
Expected: PASS (các test cũ vẫn xanh vì handler chưa đổi).

- [ ] **Step 8: Commit**

Chạy `gitnexus_detect_changes()` — kỳ vọng chạm `IPatientRepository`, `PatientRepository`, `FakePatientRepository`, `PatientModels`.

```bash
git add HealthExam.Application/Patients/PatientModels.cs HealthExam.Application/Patients/IPatientRepository.cs HealthExam.Infrastructure/Persistence/Repositories/PatientRepository.cs HealthExam.Tests/Application/PatientRegistrationTestDoubles.cs HealthExam.Tests/Infrastructure/PatientRepositoryProfileTests.cs
git commit -m "feat(patient): load active profile with insurance, employment and relative as last registered

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>"
```

---

### Task 4: `PatientProfileResult` đầy đủ + handler + endpoint + docs

**Files:**
- Create: `HealthExam.Application/Patients/RelationshipNames.cs`
- Modify: `HealthExam.Infrastructure/Persistence/Repositories/ExamRecordRepository.cs:695-705` (`ResolveRelationshipName` ủy quyền)
- Modify: `HealthExam.Application/Patients/GetPatientProfile.cs:13-53,72-92` (`PatientProfileResult`, `GetPatientProfileHandler.HandleAsync`)
- Test: `HealthExam.Tests/Application/GetPatientProfileTests.cs`
- Test: `HealthExam.Tests/PatientEndpointTests.cs:174-191`
- Modify: `docs/api/patient-profile-search-and-reuse-api.md:151-192` (§3b)

**Interfaces:**
- Consumes: `PatientProfileSnapshot`, `IPatientRepository.FindActiveProfileAsync` (Task 3).
- Produces: `PatientProfileResult` = 18 field cũ + 24 field mới (đúng tên trong Step 4); `RelationshipNames.Of(string code) : string`.

- [ ] **Step 1: Impact analysis**

Chạy `gitnexus_impact` cho `PatientProfileResult`, `GetPatientProfileHandler`, `ResolveRelationshipName` (direction upstream). Kỳ vọng caller: `PatientController.Get`, `GetPatientProfileTests`, `PatientEndpointTests`; `ResolveRelationshipName` chỉ có `MapToResult`. Báo blast radius.

- [ ] **Step 2: Test handler (đỏ)**

Trong `HealthExam.Tests/Application/GetPatientProfileTests.cs`, thêm `using System.Collections.Generic;`, `using HealthExam.Domain.Catalogs;` và thêm hai test sau test `Returns_full_profile_of_active_patient_in_division`:

```csharp
    [Fact]
    public async Task Profile_carries_option_codes_insurance_employment_and_relative()
    {
        var patient = CreatePatient("D01");
        patient.IdentityIssuerOptionID = Guid.NewGuid();
        patient.IdentityIssuerOption = new MasterDataOption { OptionID = patient.IdentityIssuerOptionID.Value, Code = "CCS", Name = "Cục Cảnh sát" };
        patient.EthnicityOptionID = Guid.NewGuid();
        patient.EthnicityOption = new MasterDataOption { OptionID = patient.EthnicityOptionID.Value, Code = "KINH", Name = "Kinh" };

        var repo = new FakePatientRepository(patient);
        repo.Insurances.Add(new PatientInsurance
        {
            InsuranceRefID = Guid.NewGuid(),
            DivisionID = "D01",
            PatientRefID = patient.PatientRefID,
            InsuranceNumber = "DN4790000001",
            InsuranceObjectOptionID = Guid.NewGuid(),
            InsuranceObjectOption = new MasterDataOption { Code = "HT", Name = "Hưu trí" },
            RegistrationPlaceOptionID = Guid.NewGuid(),
            RegistrationPlaceOption = new MasterDataOption { Code = "79001", Name = "BV Quận 1" },
            ValidFrom = new DateOnly(2026, 1, 1),
            ValidTo = new DateOnly(2026, 12, 31),
            IsActive = true
        });
        repo.Employments.Add(new PatientEmployment
        {
            EmploymentRefID = Guid.NewGuid(),
            DivisionID = "D01",
            PatientRefID = patient.PatientRefID,
            OccupationOptionID = Guid.NewGuid(),
            OccupationOption = new MasterDataOption { Code = "GV", Name = "Giáo viên" },
            StaffCode = "NV01",
            OrgDeptName = "Phòng Hành chính",
            JobTitle = "Chuyên viên",
            IsActive = true
        });
        repo.Relatives.Add(new PatientRelative
        {
            RelativeRefID = Guid.NewGuid(),
            DivisionID = "D01",
            PatientRefID = patient.PatientRefID,
            RelationshipCode = "SPOUSE",
            FullName = "Trần Thị B",
            IdentityNumber = "079000000001",
            PhoneNumber = "0912345678",
            IsActive = true
        });
        var sut = new GetPatientProfileHandler(repo);

        var result = await sut.HandleAsync(new GetPatientProfileQuery("D01", patient.PatientRefID));

        Assert.True(result.IsSuccess);
        var p = result.Value;
        Assert.Equal("CCS", p.IdentityIssuerCode);
        Assert.Equal("Cục Cảnh sát", p.IdentityIssuerName);
        Assert.Equal("KINH", p.EthnicityCode);
        Assert.Equal("Kinh", p.EthnicityName);

        Assert.Equal(repo.Insurances[0].InsuranceRefID, p.InsuranceRefID);
        Assert.Equal("DN4790000001", p.InsuranceNumber);
        Assert.Equal("HT", p.InsuranceObjectCode);
        Assert.Equal("Hưu trí", p.InsuranceObjectName);
        Assert.Equal(new DateOnly(2026, 1, 1), p.InsuranceValidFrom);
        Assert.Equal(new DateOnly(2026, 12, 31), p.InsuranceValidTo);
        Assert.Equal("79001", p.RegistrationPlaceCode);
        Assert.Equal("BV Quận 1", p.RegistrationPlaceName);

        Assert.Equal(repo.Employments[0].EmploymentRefID, p.EmploymentRefID);
        Assert.Equal("GV", p.OccupationCode);
        Assert.Equal("Giáo viên", p.OccupationName);
        Assert.Equal("NV01", p.StaffCode);
        Assert.Equal("Phòng Hành chính", p.OrgDeptName);
        Assert.Equal("Chuyên viên", p.JobTitle);

        Assert.Equal(repo.Relatives[0].RelativeRefID, p.RelativeRefID);
        Assert.Equal("SPOUSE", p.RelativeRelationshipCode);
        Assert.Equal("Vợ-chồng", p.RelativeRelationshipName);
        Assert.Equal("Trần Thị B", p.RelativeFullName);
        Assert.Equal("079000000001", p.RelativeIdentityNumber);
        Assert.Equal("0912345678", p.RelativePhoneNumber);
    }

    [Fact]
    public async Task Profile_without_child_rows_returns_empty_strings_and_null_refs()
    {
        var patient = CreatePatient("D01");
        var sut = new GetPatientProfileHandler(new FakePatientRepository(patient));

        var result = await sut.HandleAsync(new GetPatientProfileQuery("D01", patient.PatientRefID));

        Assert.True(result.IsSuccess);
        var p = result.Value;
        Assert.Equal("", p.IdentityIssuerCode);
        Assert.Equal("", p.EthnicityCode);
        Assert.Null(p.InsuranceRefID);
        Assert.Equal("", p.InsuranceNumber);
        Assert.Equal("", p.InsuranceObjectCode);
        Assert.Null(p.InsuranceValidFrom);
        Assert.Equal("", p.RegistrationPlaceCode);
        Assert.Null(p.EmploymentRefID);
        Assert.Equal("", p.OccupationCode);
        Assert.Equal("", p.StaffCode);
        Assert.Null(p.RelativeRefID);
        Assert.Equal("", p.RelativeRelationshipCode);
        Assert.Equal("", p.RelativeRelationshipName);
        Assert.Equal("", p.RelativeFullName);
    }
```

- [ ] **Step 3: Chạy test, xác nhận đỏ**

Run: `dotnet test HealthExam.Tests/HealthExam.Tests.csproj --filter "FullyQualifiedName~GetPatientProfileTests"`
Expected: FAIL — `'PatientProfileResult' does not contain a definition for 'IdentityIssuerCode'`.

- [ ] **Step 4: Helper tên quan hệ dùng chung**

Tạo `HealthExam.Application/Patients/RelationshipNames.cs`:

```csharp
namespace HealthExam.Application.Patients;

/// <summary>
/// Tên hiển thị của mã quan hệ thân nhân (PatientRelative.RelationshipCode). Một bảng dùng chung
/// cho ExamRecordResult và PatientProfileResult — không để hai chỗ lệch chữ.
/// </summary>
public static class RelationshipNames
{
    public static string Of(string code) =>
        code?.ToUpperInvariant() switch
        {
            "FATHER" => "Cha",
            "MOTHER" => "Mẹ",
            "SPOUSE" => "Vợ-chồng",
            "CHILD" => "Con",
            "GUARDIAN" => "Người giám hộ",
            "OTHER" => "Khác",
            _ => code ?? ""
        };
}
```

Trong `HealthExam.Infrastructure/Persistence/Repositories/ExamRecordRepository.cs`, thay **toàn bộ** thân `ResolveRelationshipName` bằng ủy quyền (thêm `using HealthExam.Application.Patients;` nếu file chưa có):

```csharp
    private static string ResolveRelationshipName(string code) => RelationshipNames.Of(code);
```

- [ ] **Step 5: Mở rộng `PatientProfileResult` và handler**

Trong `HealthExam.Application/Patients/GetPatientProfile.cs` thay **toàn bộ** record `PatientProfileResult` (dòng 13-53) bằng:

```csharp
/// <summary>
/// Hồ sơ cá nhân đầy đủ để frontend điền vào form đăng ký khám. 18 field đầu giữ nguyên;
/// các field từ IdentityIssuerCode trở đi TRÙNG TÊN với ExamRecordResult để FE dùng lại
/// cùng một mapping (form đọc Mã, không đọc OptionID). Dòng BHYT / nghề nghiệp / thân nhân
/// là dòng của lần đăng ký gần nhất (xem IPatientRepository.FindActiveProfileAsync).
/// </summary>
public sealed record PatientProfileResult(
    Guid PatientRefID,
    string DivisionID,
    long? HisPatientID,
    string PatientCode,
    string FullName,
    DateOnly? Dob,
    short? BirthYear,
    short GenderID,
    string IdentityNumber,
    DateOnly? IdentityIssuedDate,
    Guid? IdentityIssuerOptionID,
    string PhoneNumber,
    string Email,
    string Address,
    Guid? EthnicityOptionID,
    string BloodAboCode,
    string BloodRhCode,
    bool IsActive,
    string IdentityIssuerCode,
    string IdentityIssuerName,
    string EthnicityCode,
    string EthnicityName,
    Guid? InsuranceRefID,
    string InsuranceNumber,
    string InsuranceObjectCode,
    string InsuranceObjectName,
    DateOnly? InsuranceValidFrom,
    DateOnly? InsuranceValidTo,
    string RegistrationPlaceCode,
    string RegistrationPlaceName,
    Guid? EmploymentRefID,
    string OccupationCode,
    string OccupationName,
    string StaffCode,
    string OrgDeptName,
    string JobTitle,
    Guid? RelativeRefID,
    string RelativeRelationshipCode,
    string RelativeRelationshipName,
    string RelativeFullName,
    string RelativeIdentityNumber,
    string RelativePhoneNumber)
{
    public static PatientProfileResult From(PatientProfileSnapshot snapshot)
    {
        var patient = snapshot.Patient;
        var insurance = snapshot.Insurance;
        var employment = snapshot.Employment;
        var relative = snapshot.Relative;

        return new(
            PatientRefID: patient.PatientRefID,
            DivisionID: patient.DivisionID,
            HisPatientID: patient.HisPatientID,
            PatientCode: patient.PatientCode,
            FullName: patient.FullName,
            Dob: patient.Dob,
            BirthYear: patient.BirthYear,
            GenderID: patient.GenderID,
            IdentityNumber: patient.IdentityNumber,
            IdentityIssuedDate: patient.IdentityIssuedDate,
            IdentityIssuerOptionID: patient.IdentityIssuerOptionID,
            PhoneNumber: patient.PhoneNumber,
            Email: patient.Email,
            Address: patient.Address,
            EthnicityOptionID: patient.EthnicityOptionID,
            BloodAboCode: patient.BloodAboCode,
            BloodRhCode: patient.BloodRhCode,
            IsActive: patient.IsActive,
            IdentityIssuerCode: patient.IdentityIssuerOption?.Code ?? "",
            IdentityIssuerName: patient.IdentityIssuerOption?.Name ?? "",
            EthnicityCode: patient.EthnicityOption?.Code ?? "",
            EthnicityName: patient.EthnicityOption?.Name ?? "",
            InsuranceRefID: insurance?.InsuranceRefID,
            InsuranceNumber: insurance?.InsuranceNumber ?? "",
            InsuranceObjectCode: insurance?.InsuranceObjectOption?.Code ?? "",
            InsuranceObjectName: insurance?.InsuranceObjectOption?.Name ?? "",
            InsuranceValidFrom: insurance?.ValidFrom,
            InsuranceValidTo: insurance?.ValidTo,
            RegistrationPlaceCode: insurance?.RegistrationPlaceOption?.Code ?? "",
            RegistrationPlaceName: insurance?.RegistrationPlaceOption?.Name ?? "",
            EmploymentRefID: employment?.EmploymentRefID,
            OccupationCode: employment?.OccupationOption?.Code ?? "",
            OccupationName: employment?.OccupationOption?.Name ?? "",
            StaffCode: employment?.StaffCode ?? "",
            OrgDeptName: employment?.OrgDeptName ?? "",
            JobTitle: employment?.JobTitle ?? "",
            RelativeRefID: relative?.RelativeRefID,
            RelativeRelationshipCode: relative?.RelationshipCode ?? "",
            RelativeRelationshipName: RelationshipNames.Of(relative?.RelationshipCode),
            RelativeFullName: relative?.FullName ?? "",
            RelativeIdentityNumber: relative?.IdentityNumber ?? "",
            RelativePhoneNumber: relative?.PhoneNumber ?? "");
    }
}
```

Trong `GetPatientProfileHandler.HandleAsync`, thay khối từ `var patient = await _patientRepository.FindActiveByRefIdAsync(` đến `return ... Success(PatientProfileResult.From(patient));` bằng:

```csharp
        var snapshot = await _patientRepository.FindActiveProfileAsync(
            query.DivisionId, query.PatientRefID, ct);

        if (snapshot == null)
        {
            return ApplicationResult<PatientProfileResult>.Fail(
                ApplicationFailureCode.NotFound,
                "Không tìm thấy hồ sơ người bệnh.");
        }

        return ApplicationResult<PatientProfileResult>.Success(PatientProfileResult.From(snapshot));
```

`FindActiveByRefIdAsync` vẫn được `MatchPatientProfile`/writer dùng — **không xoá**.

- [ ] **Step 6: Chạy test handler, xác nhận xanh**

Run: `dotnet test HealthExam.Tests/HealthExam.Tests.csproj --filter "FullyQualifiedName~GetPatientProfileTests|FullyQualifiedName~PatientExamHistoryTests|FullyQualifiedName~ExamRecordListFilterTests"`
Expected: PASS (bao gồm test cũ `Returns_full_profile_of_active_patient_in_division` và `RelativeRelationshipName` của history không đổi).

- [ ] **Step 7: Test endpoint (đỏ → xanh)**

Trong `HealthExam.Tests/PatientEndpointTests.cs`, thêm test ngay sau `Get_patient_returns_full_profile`:

```csharp
    [Fact]
    public async Task Get_patient_returns_insurance_employment_and_relative_of_last_registration()
    {
        var id = await SeedPatientAsync();
        Guid insuranceRefId, relativeRefId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HealthExamDbContext>();
            var insuranceObject = new MasterDataOption
            {
                OptionID = Guid.NewGuid(), DivisionID = "DEV", Category = MasterDataCategories.InsuranceObject,
                Code = "HT", Name = "Hưu trí", IsActive = true
            };
            db.MasterDataOptions.Add(insuranceObject);
            var insurance = new PatientInsurance
            {
                InsuranceRefID = Guid.NewGuid(), DivisionID = "DEV", PatientRefID = id,
                InsuranceNumber = "DN4790000001", InsuranceObjectOptionID = insuranceObject.OptionID,
                ValidFrom = new DateOnly(2026, 1, 1), ValidTo = new DateOnly(2026, 12, 31), IsActive = true
            };
            db.PatientInsurances.Add(insurance);
            var relative = new PatientRelative
            {
                RelativeRefID = Guid.NewGuid(), DivisionID = "DEV", PatientRefID = id,
                RelationshipCode = "SPOUSE", FullName = "Trần Thị B", PhoneNumber = "0912345678", IsActive = true
            };
            db.PatientRelatives.Add(relative);
            await db.SaveChangesAsync();
            insuranceRefId = insurance.InsuranceRefID;
            relativeRefId = relative.RelativeRefID;
        }

        var client = CreateEmployeeClient("DEV");
        var response = await client.GetAsync($"/v1/patients/{id}");
        var body = JObject.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var data = body["Data"]!;
        Assert.Equal(insuranceRefId, Guid.Parse(data["InsuranceRefID"]!.Value<string>()!));
        Assert.Equal("DN4790000001", data["InsuranceNumber"]!.Value<string>());
        Assert.Equal("HT", data["InsuranceObjectCode"]!.Value<string>());
        Assert.Equal("Hưu trí", data["InsuranceObjectName"]!.Value<string>());
        Assert.Equal("2026-01-01", data["InsuranceValidFrom"]!.Value<string>());
        Assert.Equal(JTokenType.Null, data["EmploymentRefID"]!.Type);
        Assert.Equal("", data["OccupationCode"]!.Value<string>());
        Assert.Equal(relativeRefId, Guid.Parse(data["RelativeRefID"]!.Value<string>()!));
        Assert.Equal("SPOUSE", data["RelativeRelationshipCode"]!.Value<string>());
        Assert.Equal("Vợ-chồng", data["RelativeRelationshipName"]!.Value<string>());
        Assert.Equal("Trần Thị B", data["RelativeFullName"]!.Value<string>());
        Assert.Equal("", data["IdentityIssuerCode"]!.Value<string>());
    }
```

Bổ sung `using HealthExam.Domain.Catalogs;` và `using HealthExam.Domain.Patients;` nếu file chưa có. Nếu `InsuranceValidFrom` không serialize thành `"2026-01-01"` (kiểm tra cách `DateOnly` được ghi ở test khác trong repo: `grep -rn "DateOnly" HealthExam.API/Middlewares HealthExam.API/Program.cs`), đổi assertion sang đúng format đang dùng — không đổi serializer.

Run: `dotnet test HealthExam.Tests/HealthExam.Tests.csproj --filter "FullyQualifiedName~PatientEndpointTests"`
Expected: PASS.

- [ ] **Step 8: Docs §3b**

Trong `docs/api/patient-profile-search-and-reuse-api.md`, thay khối JSON response ở §3b (dòng 160-183) bằng:

````json
{
  "ErrorCode": 0,
  "Message": "",
  "Data": {
    "PatientRefID": "a768f42f-d8fa-4b06-98e3-25385b4b2521",
    "DivisionID": "D01",
    "HisPatientID": 125001,
    "PatientCode": "BN000125001",
    "FullName": "Nguyễn Văn An",
    "Dob": "1990-05-20",
    "BirthYear": 1990,
    "GenderID": 1,
    "IdentityNumber": "079123456789",
    "IdentityIssuedDate": "2021-06-15",
    "IdentityIssuerOptionID": "31a75517-80ea-4b89-983c-19d26dc87efe",
    "PhoneNumber": "0901234567",
    "Email": "an@example.com",
    "Address": "12 Nguyễn Huệ, TP.HCM",
    "EthnicityOptionID": "0b0b108d-5dab-462e-a12f-94f57d667ff7",
    "BloodAboCode": "A",
    "BloodRhCode": "+",
    "IsActive": true,

    "IdentityIssuerCode": "CCS",
    "IdentityIssuerName": "Cục Cảnh sát QLHC về TTXH",
    "EthnicityCode": "KINH",
    "EthnicityName": "Kinh",

    "InsuranceRefID": "0f1c4a1e-6a4f-4d8e-9a2b-7c1d2e3f4a5b",
    "InsuranceNumber": "DN4790000001",
    "InsuranceObjectCode": "HT",
    "InsuranceObjectName": "Hưu trí",
    "InsuranceValidFrom": "2026-01-01",
    "InsuranceValidTo": "2026-12-31",
    "RegistrationPlaceCode": "79001",
    "RegistrationPlaceName": "BV Quận 1",

    "EmploymentRefID": "5a6b7c8d-9e0f-4a1b-8c2d-3e4f5a6b7c8d",
    "OccupationCode": "GV",
    "OccupationName": "Giáo viên",
    "StaffCode": "NV01",
    "OrgDeptName": "Phòng Hành chính",
    "JobTitle": "Chuyên viên",

    "RelativeRefID": "9d8c7b6a-5f4e-4d3c-8b2a-1f0e9d8c7b6a",
    "RelativeRelationshipCode": "SPOUSE",
    "RelativeRelationshipName": "Vợ-chồng",
    "RelativeFullName": "Trần Thị B",
    "RelativeIdentityNumber": "079000000001",
    "RelativePhoneNumber": "0912345678"
  },
  "TraceID": "c37c1f3f2f6a4ea8"
}
````

và ngay sau khối JSON, trước câu "Frontend lưu response này làm `sourceProfile`…", thêm:

```markdown
Từ 18/09/2026 response có thêm 24 trường (từ `IdentityIssuerCode` trở đi). Chúng **trùng tên
với `ExamRecordItem`** của `GET /v1/exam-records/{id}` nên frontend dùng lại cùng một mapping
sang form (`ethnicityCode`, `identityIssuerCode`, `insuranceSubject` ← `InsuranceObjectCode`,
`registrationPlaceCode`, `occupationCode`, `relativeRelationship` ← `RelativeRelationshipCode`, …).
Quy ước giá trị: chuỗi rỗng `""` = không có; `*RefID` `null` = không có dòng tương ứng.

Dòng BHYT / nghề nghiệp / thân nhân trả về là dòng **của lần đăng ký gần nhất** của phiên bản
hồ sơ này (một phiên bản có thể tích luỹ nhiều dòng qua nhiều đợt khám). Lần đăng ký gần nhất
không khai BHYT thì `InsuranceRefID: null` — không mượn BHYT của đợt cũ hơn. Phiên bản chưa có
đợt khám nào thì lấy dòng active mới nhất.

Lưu ý: `POST /v1/patients/match` vẫn so sánh `IdentityIssuerOptionID`/`EthnicityOptionID`
theo UUID; frontend chưa có nguồn để gửi ngược OptionID nên hai trường đó vẫn có thể báo
"khác" — ngoài phạm vi thay đổi này.
```

- [ ] **Step 9: Chạy toàn bộ test InMemory liên quan**

Run: `dotnet test HealthExam.Tests/HealthExam.Tests.csproj --filter "FullyQualifiedName~Patient|FullyQualifiedName~ExamRecord|FullyQualifiedName~Auth|FullyQualifiedName~CompositionRootTests|FullyQualifiedName~SwaggerBusinessDocumentationTests"`
Expected: PASS. (Các test `*PostgresTests` cần `HEALTHEXAM_TEST_DB` — nếu env chưa đặt, chúng tự skip hoặc không nằm trong filter; ghi rõ trong báo cáo test nào đã chạy.)

- [ ] **Step 10: Commit**

Chạy `gitnexus_detect_changes()` — kỳ vọng chạm `PatientProfileResult`, `GetPatientProfileHandler.HandleAsync`, `ResolveRelationshipName`, `RelationshipNames`, test/doc tương ứng.

```bash
git add HealthExam.Application/Patients/RelationshipNames.cs HealthExam.Application/Patients/GetPatientProfile.cs HealthExam.Infrastructure/Persistence/Repositories/ExamRecordRepository.cs HealthExam.Tests/Application/GetPatientProfileTests.cs HealthExam.Tests/PatientEndpointTests.cs docs/api/patient-profile-search-and-reuse-api.md
git commit -m "feat(patient): return option codes, insurance, employment and relative on GET /v1/patients/{id}

Field names mirror ExamRecordResult so the frontend reuses its
record-to-form mapping instead of a second table.

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>"
```

---

## Self-review

- **Spec coverage:** Item 1 → Task 1 (10 field, bỏ FullName/Title, rename UserName, docs breaking). Item 3 → Task 2 (field cuối `ExamRecordResult`, map, docs cách FE mở PDF qua preview). Item 2 → Task 3 (chọn dòng con theo hồ sơ khám gần nhất, fallback, không lẫn phiên bản khác) + Task 4 (Code/Name danh mục, 3 block flat trùng tên `ExamRecordResult`, Employment, docs §3b, ghi chú match ngoài scope). `search`/`match` không đổi — không task nào chạm `SearchPatients.cs`/`MatchPatientProfile.cs`.
- **Placeholder scan:** không có TBD/TODO; mọi bước code có code; bước docs có nội dung đầy đủ.
- **Type consistency:** `PatientProfileSnapshot(Patient, PatientInsurance, PatientEmployment, PatientRelative)` (Task 3) là tham số của `PatientProfileResult.From` (Task 4); `FindActiveProfileAsync(string, Guid, CancellationToken)` cùng chữ ký ở interface, `PatientRepository`, `FakePatientRepository`; `RelationshipNames.Of` được gọi ở cả `ExamRecordRepository` và `PatientProfileResult.From`; `CurrentUserResult` 10 tham số cùng thứ tự ở gateway, fake test, DTO; `HisSignedFilePath` là tham số thứ nhất sau `HisSignedAt` ở cả record và `MapToResult`.
