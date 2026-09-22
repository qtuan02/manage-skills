# Patient Exam History (Đợt khám trước) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Thêm `GET /v1/patients/{patientRefId}/exam-history` trả danh sách phân trang các hồ sơ KSK **đã ký kết luận** của người bệnh, gộp theo dòng phiên bản hồ sơ (`ProfileLineageID`).

**Architecture:** Clean Architecture 4 project (`Domain` → `Application` → `Infrastructure` / `API`). Handler mới nằm ở `Application/ExamRecords`, phụ thuộc **hai** repository interface: `IPatientRepository` để phân giải `PatientRefID → ProfileLineageID`, và `IExamRecordRepository` để lấy trang hồ sơ đã ký. Controller `PatientController` chỉ dựng query và trả envelope. Không migration (không thêm cột), không sửa `GET /v1/exam-records`.

**Tech Stack:** .NET 8, ASP.NET Core controllers, EF Core 8 + Npgsql, xUnit, `Microsoft.AspNetCore.Mvc.Testing` (endpoint tests dùng InMemory DB), `InMemoryTestDb` (test repository).

**Spec:** Thiết kế đã được duyệt trong chat (bounded path, không có spec file). Tóm tắt quyết định:

- Route: `GET /v1/patients/{patientRefId}/exam-history?page=1&size=5`, trả `ResultData<PageResult<ExamRecordResult>>` — đúng `ExamRecordItem` + `PaginationData` mà FE `medviet` đã có, **không thêm DTO mới**.
- `patientRefId` được hiểu là **người**, không phải một phiên bản: phân giải sang `ProfileLineageID` rồi lấy hồ sơ khám của MỌI phiên bản cùng dòng. Lọc thẳng `ExamRecord.PatientRefID = @id` sẽ mất đợt cũ vì `Patient` được version hóa (`HealthExam.Domain/Patients/Patient.cs:32-34`).
- Phân giải lineage **không lọc `IsActive`**: hồ sơ khám cũ trỏ vào phiên bản đã bị thay thế, chính nó là đường vào dòng hồ sơ.
- Điều kiện "đã ký kết luận": `HisSignStatus == "Signed"` (chuỗi `SignConclusion.cs:127,250` ghi), cứng — không có tham số bật/tắt.
- `ExamRecordResult` được bổ sung `HisSignStatus` + `HisSignedAt` để FE hiển thị cột "Kết luận" kèm ngày ký. Hai field optional ở CUỐI record nên mọi caller positional hiện có không vỡ.
- Sắp xếp: `Session.ExamDate DESC` → `HisSignedAt DESC` → `RecordCode`.
- Phân trang: `page` mặc định 1, `size` mặc định **5**, kẹp trần **200**. `page < 1` → 1; `size < 1` → 5; `size > 200` → 200 (im lặng, giống `ListExamRecordsHandler`).
- `PatientRefID` rỗng → 400 / `4001`. Không tìm thấy trong `X-Division-Id` → 404 / `4040`.

## Global Constraints

- Chỉ sửa `health-exam-server`. Không sửa `frontend/medviet`, không sửa `his-server`.
- Làm trực tiếp trên nhánh hiện tại `feat/patient-multi-search`; không tạo git worktree.
- Mọi query phải lọc `DivisionID == HealthExamContext.DivisionId`. Không endpoint nào được trả hồ sơ của đơn vị khác.
- Không thêm migration, không thêm cột, không thêm NuGet package.
- Không đổi hành vi `GET /v1/exam-records` (đặc biệt: `size` mặc định của nó vẫn là **20**).
- So sánh trạng thái ký bằng `x.HisSignStatus == "Signed"` (so khớp chính xác, hằng số `SignedStatus`). KHÔNG dùng `ToLower()` / `StringComparison` trong biểu thức LINQ gửi xuống DB.
- Thông báo lỗi bằng tiếng Việt có dấu, giống các handler hiện có.
- Mã lỗi: chỉ dùng `ApplicationFailureCode.BadRequest` (→ HTTP 400 / `4001`) và `ApplicationFailureCode.NotFound` (→ HTTP 404 / `4040`). Không thêm mã mới.
- Chạy test: `dotnet test HealthExam.Tests/HealthExam.Tests.csproj --filter "FullyQualifiedName~<TestClass>"` từ thư mục gốc repo.
- `HealthExam.Tests/API/CompositionRootTests.Di_resolves_all_application_handlers` tự động bắt mọi interface tên `*Handler` phải được đăng ký DI — quên `AddScoped` là đỏ test, không phải lỗi runtime.
- Theo `CLAUDE.md`: trước khi sửa symbol có sẵn (`ExamRecordResult`, `MapToResult`, `IPatientRepository`, `PatientRepository`, `IExamRecordRepository`, `ExamRecordRepository`, `PatientController`, `FakePatientRepository`, `FakeExamRecordRepository`, `InMemoryTestDb.SeedRecord`) phải chạy `gitnexus_impact({target, direction: "upstream", repo: "health-exam-server"})` và báo blast radius; index báo stale/degraded thì grep caller thủ công và ghi rõ. Trước mỗi commit chạy `gitnexus_detect_changes()`.
- Commit message theo conventional style của repo (`feat(patient): ...`, `test(patient): ...`) và kết thúc bằng dòng `Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>`.

---

## File Structure

| File | Trách nhiệm |
| --- | --- |
| `HealthExam.Application/ExamRecords/ExamRecordModels.cs` (sửa) | `ExamRecordResult` + 2 field `HisSignStatus`, `HisSignedAt` |
| `HealthExam.Infrastructure/Persistence/Repositories/ExamRecordRepository.cs` (sửa) | `MapToResult` chép 2 field mới; thêm `ListSignedByPatientLineageAsync` |
| `HealthExam.Application/ExamRecords/IExamRecordRepository.cs` (sửa) | Khai báo `ListSignedByPatientLineageAsync` |
| `HealthExam.Application/Patients/IPatientRepository.cs` (sửa) | Khai báo `FindProfileLineageIdAsync` |
| `HealthExam.Infrastructure/Persistence/Repositories/PatientRepository.cs` (sửa) | EF impl `FindProfileLineageIdAsync` |
| `HealthExam.Application/ExamRecords/GetPatientExamHistory.cs` (mới) | `GetPatientExamHistoryQuery`, `IGetPatientExamHistoryHandler`, `GetPatientExamHistoryHandler` |
| `HealthExam.API/Controllers/PatientController.cs` (sửa) | Action `ExamHistory` |
| `HealthExam.API/Extensions/ApplicationServiceExtensions.cs` (sửa) | Đăng ký handler mới |
| `HealthExam.Tests/InMemoryTestDb.cs` (sửa) | `SeedRecord` nhận `hisSignedAt`, `profileLineageID`; luôn đặt `ProfileLineageID` |
| `HealthExam.Tests/PatientExamHistoryTests.cs` (mới) | Test mapping + test 2 repository trên InMemory DB |
| `HealthExam.Tests/Application/PatientExamHistoryHandlerTests.cs` (mới) | Test handler với fake repository |
| `HealthExam.Tests/Application/PatientRegistrationTestDoubles.cs` (sửa) | `FakePatientRepository.FindProfileLineageIdAsync` |
| `HealthExam.Tests/Application/ExamRecordHandlerTests.cs` (sửa) | `FakeExamRecordRepository.ListSignedByPatientLineageAsync` |
| `HealthExam.Tests/PatientEndpointTests.cs` (sửa) | Test endpoint: envelope phân trang, 404, size mặc định 5 |

**Interfaces sinh ra ở plan này (các task sau dùng lại nguyên văn):**

```csharp
// IPatientRepository (Task 2)
Task<Guid?> FindProfileLineageIdAsync(string divisionId, Guid patientRefID, CancellationToken ct = default);

// IExamRecordRepository (Task 3)
Task<PageResult<ExamRecordResult>> ListSignedByPatientLineageAsync(
    string divisionId, Guid profileLineageID, int page, int size, CancellationToken ct = default);

// GetPatientExamHistory.cs (Task 4)
public sealed record GetPatientExamHistoryQuery(string DivisionId, Guid PatientRefID, int Page = 1, int Size = 0);
public interface IGetPatientExamHistoryHandler
{
    Task<ApplicationResult<PageResult<ExamRecordResult>>> HandleAsync(
        GetPatientExamHistoryQuery query, CancellationToken ct = default);
}
public sealed class GetPatientExamHistoryHandler : IGetPatientExamHistoryHandler
{
    public const int DefaultSize = 5;
    public const int MaxSize = 200;
}
```

---

### Task 1: `ExamRecordResult` mang trạng thái ký kết luận

**Files:**
- Modify: `HealthExam.Application/ExamRecords/ExamRecordModels.cs` (cuối record `ExamRecordResult`, quanh dòng 100)
- Modify: `HealthExam.Infrastructure/Persistence/Repositories/ExamRecordRepository.cs` (cuối `MapToResult`, quanh dòng 625)
- Modify: `HealthExam.Tests/InMemoryTestDb.cs` (`SeedRecord`, dòng 264-341)
- Test: `HealthExam.Tests/PatientExamHistoryTests.cs` (mới)

**Interfaces:**
- Consumes: không có (task đầu tiên)
- Produces: `ExamRecordResult.HisSignStatus` (`string`, mặc định `""`), `ExamRecordResult.HisSignedAt` (`DateTime?`, mặc định `null`); `InMemoryTestDb.SeedRecord(..., DateTime? hisSignedAt = null, Guid? profileLineageID = null)` và mọi `Patient` do `SeedRecord` tạo đều có `ProfileLineageID` khác `Guid.Empty`.

- [ ] **Step 1: Chạy impact analysis trước khi sửa**

Chạy và báo blast radius cho user:

```
gitnexus_impact({target: "ExamRecordResult", direction: "upstream", repo: "health-exam-server"})
gitnexus_impact({target: "MapToResult", direction: "upstream", repo: "health-exam-server"})
gitnexus_impact({target: "SeedRecord", direction: "upstream", repo: "health-exam-server"})
```

Nếu kết quả HIGH/CRITICAL thì báo user trước khi đi tiếp. Nếu index stale/degraded, xác nhận thủ công bằng:

```bash
grep -rn "new ExamRecordResult" --include=*.cs HealthExam.Application HealthExam.Infrastructure HealthExam.API HealthExam.Tests
```

Kỳ vọng: chỉ 3 chỗ dựng positional, đều trong `HealthExam.Tests` (`ExamRecordHandlerTests.cs:1074`, `ExamRecordHandlerTests.cs:1221`, `ImportHandlerTests.cs:612`). Vì hai field mới nằm ở CUỐI record và có giá trị mặc định nên cả ba vẫn biên dịch được — **không sửa chúng**.

- [ ] **Step 2: Viết test thất bại**

Tạo `HealthExam.Tests/PatientExamHistoryTests.cs`:

```csharp
using System;
using System.Linq;
using System.Threading.Tasks;
using HealthExam.Application.ExamRecords;
using HealthExam.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HealthExam.Tests;

/// <summary>
/// Danh sách "Đợt khám trước": trạng thái ký kết luận trên kết quả, phân giải dòng hồ sơ
/// người bệnh, và truy vấn hồ sơ đã ký. Chạy trên InMemory provider — đủ để chốt NHÁNH LINQ
/// (lineage, lọc Signed, thứ tự, phân trang); ràng buộc DB thật không thuộc phạm vi ở đây.
/// </summary>
public sealed class PatientExamHistoryTests
{
    [Fact]
    public async Task Ket_qua_ho_so_mang_theo_trang_thai_va_gio_ky_ket_luan()
    {
        using var db = new InMemoryTestDb();
        var session = db.SeedSession();
        var signedAt = new DateTime(2026, 3, 14, 9, 12, 0, DateTimeKind.Utc);
        db.SeedRecord(session.SessionID, recordCode: "KSK-001",
            hisSignStatus: "Signed", hisSignedAt: signedAt);

        var repo = new ExamRecordRepository(db.Db);
        var page = await repo.ListAsync(FakeHealthExamContext.DefaultDivisionId, new ExamRecordFilter());

        var item = Assert.Single(page.Items);
        Assert.Equal("Signed", item.HisSignStatus);
        Assert.Equal(signedAt, item.HisSignedAt);
    }

    [Fact]
    public async Task Ho_so_chua_ky_tra_chuoi_rong_va_gio_ky_null()
    {
        using var db = new InMemoryTestDb();
        var session = db.SeedSession();
        db.SeedRecord(session.SessionID, recordCode: "KSK-002");

        var repo = new ExamRecordRepository(db.Db);
        var page = await repo.ListAsync(FakeHealthExamContext.DefaultDivisionId, new ExamRecordFilter());

        var item = Assert.Single(page.Items);
        Assert.Equal("", item.HisSignStatus);
        Assert.Null(item.HisSignedAt);
    }
}
```

- [ ] **Step 3: Chạy test để xác nhận nó đỏ**

Run: `dotnet test HealthExam.Tests/HealthExam.Tests.csproj --filter "FullyQualifiedName~PatientExamHistoryTests"`

Expected: FAIL — lỗi biên dịch `CS1739`/`CS0117`: `SeedRecord` không có tham số `hisSignedAt`, `ExamRecordResult` không có thuộc tính `HisSignStatus`.

- [ ] **Step 4: Thêm 2 field vào `ExamRecordResult`**

Trong `HealthExam.Application/ExamRecords/ExamRecordModels.cs`, sửa hai dòng cuối của record:

```csharp
    string PatientSubjectCode = "",
    string PatientSubjectName = "");
```

thành:

```csharp
    string PatientSubjectCode = "",
    string PatientSubjectName = "",

    /// <summary>
    /// Trạng thái ký kết luận trên HIS: "" (chưa ký) | "InProcessing" | "Signed".
    /// Đặt ở CUỐI record kèm giá trị mặc định để mọi chỗ dựng positional sẵn có không vỡ.
    /// </summary>
    string HisSignStatus = "",

    /// <summary>Giờ ký kết luận hoàn tất; chỉ có giá trị khi HisSignStatus = "Signed".</summary>
    DateTime? HisSignedAt = null);
```

- [ ] **Step 5: Chép 2 field trong `MapToResult`**

Trong `HealthExam.Infrastructure/Persistence/Repositories/ExamRecordRepository.cs`, sửa dòng cuối của `MapToResult`:

```csharp
        RegistrationPlaceCode: x.Insurance?.RegistrationPlaceOption?.Code ?? "",
        RegistrationPlaceName: x.Insurance?.RegistrationPlaceOption?.Name ?? "");
```

thành:

```csharp
        RegistrationPlaceCode: x.Insurance?.RegistrationPlaceOption?.Code ?? "",
        RegistrationPlaceName: x.Insurance?.RegistrationPlaceOption?.Name ?? "",
        HisSignStatus: x.HisSignStatus ?? "",
        HisSignedAt: x.HisSignedAt);
```

- [ ] **Step 6: Mở rộng `InMemoryTestDb.SeedRecord`**

Trong `HealthExam.Tests/InMemoryTestDb.cs`, thêm hai tham số vào cuối danh sách tham số của `SeedRecord` (sau `string hisSignStatus = null`):

```csharp
        string hisSignStatus = null,
        DateTime? hisSignedAt = null,
        Guid? profileLineageID = null)
```

Sửa khối tạo `Patient` (đang bắt đầu ở `var patient = new Patient`) thành:

```csharp
        var divId = divisionId ?? Ctx.DivisionId;
        // ProfileLineageID phải khác Guid.Empty y như dữ liệu thật: migration
        // 20260916092419_AddPatientProfileVersionNumbers backfill nó bằng COALESCE(root, chính nó),
        // và PatientRegistrationWriter luôn đặt. Fixture để trống thì mọi Patient dùng chung
        // lineage Guid.Empty và test gộp-theo-dòng sẽ xanh giả.
        var patientRefID = Guid.NewGuid();
        var patient = new Patient
        {
            PatientRefID = patientRefID,
            DivisionID = divId,
            FullName = fullName,
            PatientCode = patientCode,
            IdentityNumber = identityNumber,
            GenderID = genderId,
            ProfileLineageID = profileLineageID ?? patientRefID,
            CreatedDate = createdDate ?? new DateTime(2026, 8, 24, 0, 0, 0, DateTimeKind.Utc)
        };
```

Và trong khối tạo `ExamRecord`, thêm một dòng ngay sau `HisSignStatus = hisSignStatus`:

```csharp
            HisSignStatus = hisSignStatus,
            HisSignedAt = hisSignedAt
```

- [ ] **Step 7: Chạy test để xác nhận nó xanh**

Run: `dotnet test HealthExam.Tests/HealthExam.Tests.csproj --filter "FullyQualifiedName~PatientExamHistoryTests"`

Expected: PASS (2 test).

- [ ] **Step 8: Chạy lại bộ test hồ sơ khám để chắc không vỡ chỗ khác**

Run: `dotnet test HealthExam.Tests/HealthExam.Tests.csproj --filter "FullyQualifiedName~ExamRecord"`

Expected: PASS, không test nào đỏ vì thêm field.

- [ ] **Step 9: Commit**

```bash
git add HealthExam.Application/ExamRecords/ExamRecordModels.cs HealthExam.Infrastructure/Persistence/Repositories/ExamRecordRepository.cs HealthExam.Tests/InMemoryTestDb.cs HealthExam.Tests/PatientExamHistoryTests.cs
git commit -m "feat(exam-record): expose HIS conclusion sign status on record result

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>"
```

---

### Task 2: Phân giải `PatientRefID` → `ProfileLineageID`

**Files:**
- Modify: `HealthExam.Application/Patients/IPatientRepository.cs`
- Modify: `HealthExam.Infrastructure/Persistence/Repositories/PatientRepository.cs`
- Modify: `HealthExam.Tests/Application/PatientRegistrationTestDoubles.cs` (`FakePatientRepository`, dòng 29-68)
- Test: `HealthExam.Tests/PatientExamHistoryTests.cs`

**Interfaces:**
- Consumes: `InMemoryTestDb.SeedRecord(..., profileLineageID:)` từ Task 1
- Produces: `IPatientRepository.FindProfileLineageIdAsync(string divisionId, Guid patientRefID, CancellationToken ct = default)` → `Task<Guid?>`; trả `null` khi `patientRefID == Guid.Empty`, khi không có hàng, hoặc khi hàng thuộc đơn vị khác. **Không lọc `IsActive`.**

- [ ] **Step 1: Chạy impact analysis**

```
gitnexus_impact({target: "IPatientRepository", direction: "upstream", repo: "health-exam-server"})
```

`IPatientRepository` là ranh giới interface/DI — plan trước đã ghi nhận mức **CRITICAL**. Báo user blast radius trước khi sửa. Thay đổi ở đây là **thêm method**, không đổi chữ ký cũ, nên chỉ các lớp implement bị ảnh hưởng. Xác nhận danh sách implement:

```bash
grep -rn ": IPatientRepository" --include=*.cs HealthExam.Infrastructure HealthExam.Tests
```

Kỳ vọng đúng 2: `PatientRepository`, `FakePatientRepository`.

- [ ] **Step 2: Viết test thất bại**

Thêm vào `HealthExam.Tests/PatientExamHistoryTests.cs` (trong cùng class `PatientExamHistoryTests`):

```csharp
    [Fact]
    public async Task Lineage_tra_ve_ca_khi_phien_ban_da_bi_thay_the()
    {
        using var db = new InMemoryTestDb();
        var session = db.SeedSession();
        var lineageID = Guid.NewGuid();
        var oldVersion = db.SeedRecord(session.SessionID, recordCode: "KSK-001",
            profileLineageID: lineageID);

        // Phiên bản cũ: đúng tình huống hồ sơ khám năm ngoái trỏ vào bản đã bị thay thế.
        var tracked = db.Db.Patients.AsTracking()
            .Single(p => p.PatientRefID == oldVersion.PatientRefID.Value);
        tracked.IsActive = false;
        db.Db.SaveChanges();
        db.Db.ChangeTracker.Clear();

        var repo = new PatientRepository(db.Db);
        var found = await repo.FindProfileLineageIdAsync(
            FakeHealthExamContext.DefaultDivisionId, oldVersion.PatientRefID.Value);

        Assert.Equal(lineageID, found);
    }

    [Fact]
    public async Task Lineage_khong_vuot_don_vi_va_tra_null_khi_khong_co()
    {
        using var db = new InMemoryTestDb();
        var session = db.SeedSession(divisionId: "D_OTHER");
        var other = db.SeedRecord(session.SessionID, recordCode: "KSK-001", divisionId: "D_OTHER");

        var repo = new PatientRepository(db.Db);

        Assert.Null(await repo.FindProfileLineageIdAsync(
            FakeHealthExamContext.DefaultDivisionId, other.PatientRefID.Value));
        Assert.Null(await repo.FindProfileLineageIdAsync(
            FakeHealthExamContext.DefaultDivisionId, Guid.NewGuid()));
        Assert.Null(await repo.FindProfileLineageIdAsync(
            FakeHealthExamContext.DefaultDivisionId, Guid.Empty));
    }
```

- [ ] **Step 3: Chạy test để xác nhận nó đỏ**

Run: `dotnet test HealthExam.Tests/HealthExam.Tests.csproj --filter "FullyQualifiedName~PatientExamHistoryTests"`

Expected: FAIL — `CS1061`: `PatientRepository` không có `FindProfileLineageIdAsync`.

- [ ] **Step 4: Khai báo trên interface**

Trong `HealthExam.Application/Patients/IPatientRepository.cs`, thêm ngay sau dòng khai báo `FindActiveByRefIdAsync`:

```csharp
    /// <summary>
    /// Trả ProfileLineageID của một PatientRefID bất kỳ trong đơn vị — kể cả phiên bản đã
    /// inactive. Đây là cách duy nhất đi từ "một phiên bản hồ sơ" sang "người đó": hồ sơ khám
    /// cũ neo vào phiên bản tại thời điểm khám, nên lọc IsActive ở đây sẽ cắt mất lịch sử.
    /// null = không có hàng nào khớp trong đơn vị này.
    /// </summary>
    Task<Guid?> FindProfileLineageIdAsync(string divisionId, Guid patientRefID, CancellationToken ct = default);
```

- [ ] **Step 5: Cài đặt EF**

Trong `HealthExam.Infrastructure/Persistence/Repositories/PatientRepository.cs`, thêm ngay sau method `FindActiveByRefIdAsync` (kết thúc ở dòng 55):

```csharp
    public async Task<Guid?> FindProfileLineageIdAsync(string divisionId, Guid patientRefID, CancellationToken ct = default)
    {
        if (patientRefID == Guid.Empty) return null;

        return await _db.Patients.AsNoTracking()
            .Where(p => p.DivisionID == divisionId && p.PatientRefID == patientRefID)
            .Select(p => (Guid?)p.ProfileLineageID)
            .FirstOrDefaultAsync(ct);
    }
```

- [ ] **Step 6: Cập nhật `FakePatientRepository`**

Trong `HealthExam.Tests/Application/PatientRegistrationTestDoubles.cs`, thêm ngay sau method `FindActiveByRefIdAsync` (kết thúc ở dòng 68):

```csharp
    public Task<Guid?> FindProfileLineageIdAsync(string divisionId, Guid patientRefID, CancellationToken ct = default)
    {
        if (patientRefID == Guid.Empty) return Task.FromResult<Guid?>(null);

        var patient = Patients.FirstOrDefault(p => p.DivisionID == divisionId && p.PatientRefID == patientRefID);
        return Task.FromResult(patient == null ? (Guid?)null : patient.ProfileLineageID);
    }
```

- [ ] **Step 7: Chạy test để xác nhận nó xanh**

Run: `dotnet test HealthExam.Tests/HealthExam.Tests.csproj --filter "FullyQualifiedName~PatientExamHistoryTests"`

Expected: PASS (4 test).

- [ ] **Step 8: Chạy bộ test người bệnh để chắc không vỡ**

Run: `dotnet test HealthExam.Tests/HealthExam.Tests.csproj --filter "FullyQualifiedName~Patient"`

Expected: PASS.

- [ ] **Step 9: Commit**

```bash
git add HealthExam.Application/Patients/IPatientRepository.cs HealthExam.Infrastructure/Persistence/Repositories/PatientRepository.cs HealthExam.Tests/Application/PatientRegistrationTestDoubles.cs HealthExam.Tests/PatientExamHistoryTests.cs
git commit -m "feat(patient): resolve profile lineage id from any patient ref id

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>"
```

---

### Task 3: Truy vấn hồ sơ đã ký kết luận theo dòng hồ sơ

**Files:**
- Modify: `HealthExam.Application/ExamRecords/IExamRecordRepository.cs`
- Modify: `HealthExam.Infrastructure/Persistence/Repositories/ExamRecordRepository.cs`
- Modify: `HealthExam.Tests/Application/ExamRecordHandlerTests.cs` (`FakeExamRecordRepository`, quanh dòng 1151)
- Test: `HealthExam.Tests/PatientExamHistoryTests.cs`

**Interfaces:**
- Consumes: `ExamRecordResult.HisSignStatus` / `HisSignedAt` (Task 1); `InMemoryTestDb.SeedRecord(..., hisSignStatus:, hisSignedAt:, profileLineageID:)` (Task 1)
- Produces: `IExamRecordRepository.ListSignedByPatientLineageAsync(string divisionId, Guid profileLineageID, int page, int size, CancellationToken ct = default)` → `Task<PageResult<ExamRecordResult>>`. `page`/`size` truyền vào **đã được chuẩn hoá** (>= 1) ở handler; repository không tự kẹp. `FakeExamRecordRepository.HistoryPage` (settable) và `LastHistoryDivisionId` / `LastHistoryLineageID` / `LastHistoryPage` / `LastHistorySize` cho test handler ở Task 4.

- [ ] **Step 1: Chạy impact analysis**

```
gitnexus_impact({target: "IExamRecordRepository", direction: "upstream", repo: "health-exam-server"})
gitnexus_impact({target: "ExamRecordRepository", direction: "upstream", repo: "health-exam-server"})
```

Báo blast radius cho user. Xác nhận danh sách implement:

```bash
grep -rn ": IExamRecordRepository" --include=*.cs HealthExam.Infrastructure HealthExam.Tests
```

Kỳ vọng đúng 3: `ExamRecordRepository`, `FakeExamRecordRepository`, và `ThrowingExamRecordRepository` (`WebhookHandlerTests.cs:296`) — lớp thứ ba **kế thừa** `FakeExamRecordRepository` nên không cần sửa.

- [ ] **Step 2: Viết test thất bại**

Thêm vào `HealthExam.Tests/PatientExamHistoryTests.cs`:

```csharp
    [Fact]
    public async Task Chi_tra_ho_so_da_ky_ket_luan()
    {
        using var db = new InMemoryTestDb();
        var session = db.SeedSession();
        var lineageID = Guid.NewGuid();
        db.SeedRecord(session.SessionID, recordCode: "KSK-SIGNED",
            profileLineageID: lineageID, hisSignStatus: "Signed",
            hisSignedAt: new DateTime(2026, 3, 14, 9, 0, 0, DateTimeKind.Utc));
        db.SeedRecord(session.SessionID, recordCode: "KSK-INPROGRESS",
            profileLineageID: lineageID, hisSignStatus: "InProcessing");
        db.SeedRecord(session.SessionID, recordCode: "KSK-UNSIGNED",
            profileLineageID: lineageID);

        var repo = new ExamRecordRepository(db.Db);
        var page = await repo.ListSignedByPatientLineageAsync(
            FakeHealthExamContext.DefaultDivisionId, lineageID, 1, 5);

        Assert.Equal(1, page.Total);
        Assert.Equal("KSK-SIGNED", Assert.Single(page.Items).RecordCode);
    }

    [Fact]
    public async Task Gop_ho_so_kham_cua_moi_phien_ban_cung_dong()
    {
        using var db = new InMemoryTestDb();
        var lineageID = Guid.NewGuid();
        var older = db.SeedSession(sessionCode: "DK-2025", examDate: new DateOnly(2025, 4, 1));
        var newer = db.SeedSession(sessionCode: "DK-2026", examDate: new DateOnly(2026, 4, 1));

        // Hai ĐỢT khác nhau, hai PHIÊN BẢN hồ sơ khác nhau, cùng một dòng — cùng một người.
        db.SeedRecord(older.SessionID, recordCode: "KSK-2025-0001",
            profileLineageID: lineageID, hisSignStatus: "Signed",
            hisSignedAt: new DateTime(2025, 4, 1, 10, 0, 0, DateTimeKind.Utc));
        db.SeedRecord(newer.SessionID, recordCode: "KSK-2026-0001",
            profileLineageID: lineageID, hisSignStatus: "Signed",
            hisSignedAt: new DateTime(2026, 4, 1, 10, 0, 0, DateTimeKind.Utc));

        // Người khác, dòng khác: không được lọt vào.
        db.SeedRecord(newer.SessionID, recordCode: "KSK-2026-0002",
            profileLineageID: Guid.NewGuid(), hisSignStatus: "Signed",
            hisSignedAt: new DateTime(2026, 4, 1, 11, 0, 0, DateTimeKind.Utc));

        var repo = new ExamRecordRepository(db.Db);
        var page = await repo.ListSignedByPatientLineageAsync(
            FakeHealthExamContext.DefaultDivisionId, lineageID, 1, 5);

        Assert.Equal(2, page.Total);
        // Mới nhất lên đầu, theo ngày khám của đợt.
        Assert.Equal(new[] { "KSK-2026-0001", "KSK-2025-0001" },
            page.Items.Select(x => x.RecordCode).ToArray());
    }

    [Fact]
    public async Task Khong_tra_ho_so_cua_don_vi_khac()
    {
        using var db = new InMemoryTestDb();
        var lineageID = Guid.NewGuid();
        var session = db.SeedSession(divisionId: "D_OTHER");
        db.SeedRecord(session.SessionID, recordCode: "KSK-OTHER", divisionId: "D_OTHER",
            profileLineageID: lineageID, hisSignStatus: "Signed",
            hisSignedAt: new DateTime(2026, 4, 1, 10, 0, 0, DateTimeKind.Utc));

        var repo = new ExamRecordRepository(db.Db);
        var page = await repo.ListSignedByPatientLineageAsync(
            FakeHealthExamContext.DefaultDivisionId, lineageID, 1, 5);

        Assert.Equal(0, page.Total);
        Assert.Empty(page.Items);
    }

    [Fact]
    public async Task Phan_trang_giu_nguyen_tong_va_cat_dung_trang()
    {
        using var db = new InMemoryTestDb();
        var lineageID = Guid.NewGuid();
        for (var year = 2021; year <= 2023; year++)
        {
            var session = db.SeedSession(
                sessionCode: $"DK-{year}", examDate: new DateOnly(year, 4, 1));
            db.SeedRecord(session.SessionID, recordCode: $"KSK-{year}",
                profileLineageID: lineageID, hisSignStatus: "Signed",
                hisSignedAt: new DateTime(year, 4, 1, 10, 0, 0, DateTimeKind.Utc));
        }

        var repo = new ExamRecordRepository(db.Db);
        var first = await repo.ListSignedByPatientLineageAsync(
            FakeHealthExamContext.DefaultDivisionId, lineageID, 1, 2);
        var second = await repo.ListSignedByPatientLineageAsync(
            FakeHealthExamContext.DefaultDivisionId, lineageID, 2, 2);

        Assert.Equal(3, first.Total);
        Assert.Equal(new[] { "KSK-2023", "KSK-2022" }, first.Items.Select(x => x.RecordCode).ToArray());
        Assert.Equal(3, second.Total);
        Assert.Equal(2, second.Size);
        Assert.Equal("KSK-2021", Assert.Single(second.Items).RecordCode);
    }
```

- [ ] **Step 3: Chạy test để xác nhận nó đỏ**

Run: `dotnet test HealthExam.Tests/HealthExam.Tests.csproj --filter "FullyQualifiedName~PatientExamHistoryTests"`

Expected: FAIL — `CS1061`: `ExamRecordRepository` không có `ListSignedByPatientLineageAsync`.

- [ ] **Step 4: Khai báo trên interface**

Trong `HealthExam.Application/ExamRecords/IExamRecordRepository.cs`, thêm ngay sau khai báo `ListAsync`:

```csharp
    /// <summary>
    /// Các hồ sơ KSK ĐÃ KÝ KẾT LUẬN của một dòng hồ sơ người bệnh (mọi phiên bản cùng
    /// ProfileLineageID), mới nhất lên đầu. page/size phải đã chuẩn hoá (>= 1) từ handler.
    /// </summary>
    Task<PageResult<ExamRecordResult>> ListSignedByPatientLineageAsync(
        string divisionId, Guid profileLineageID, int page, int size, CancellationToken ct = default);
```

- [ ] **Step 5: Cài đặt EF**

Trong `HealthExam.Infrastructure/Persistence/Repositories/ExamRecordRepository.cs`, thêm ngay sau method `ListAsync` (kết thúc ở dòng 88, trước `ApplyContainsFilter`):

```csharp
    /// <summary>
    /// Trạng thái ký kết luận hoàn tất. So khớp CHÍNH XÁC chuỗi mà SignConclusion ghi —
    /// không ToLower()/StringComparison, vì cả hai chặn EF dùng chỉ mục và không cần thiết:
    /// chỉ một chỗ duy nhất trong hệ ghi giá trị này.
    /// </summary>
    private const string SignedStatus = "Signed";

    public async Task<PageResult<ExamRecordResult>> ListSignedByPatientLineageAsync(
        string divisionId, Guid profileLineageID, int page, int size, CancellationToken ct = default)
    {
        // Mọi PatientRefID cùng dòng: hồ sơ khám neo vào PHIÊN BẢN tại thời điểm khám, nên
        // lọc thẳng theo một PatientRefID sẽ mất các đợt trước lần sửa thông tin gần nhất.
        var versionIDs = _db.Patients
            .Where(p => p.DivisionID == divisionId && p.ProfileLineageID == profileLineageID)
            .Select(p => p.PatientRefID);

        var q = _db.ExamRecords
            .Where(x => x.DivisionID == divisionId
                     && x.PatientRefID.HasValue
                     && versionIDs.Contains(x.PatientRefID.Value)
                     && x.HisSignStatus == SignedStatus);

        var total = await q.CountAsync(ct);
        var rows = await q
            .OrderByDescending(x => x.Session.ExamDate)
            .ThenByDescending(x => x.HisSignedAt)
            .ThenBy(x => x.RecordCode)
            .Skip((page - 1) * size).Take(size)
            .Include(x => x.Session)
            .Include(x => x.Patient).ThenInclude(p => p.IdentityIssuerOption)
            .Include(x => x.Patient).ThenInclude(p => p.EthnicityOption)
            .Include(x => x.Insurance).ThenInclude(i => i.InsuranceObjectOption)
            .Include(x => x.Insurance).ThenInclude(i => i.RegistrationPlaceOption)
            .Include(x => x.Employment).ThenInclude(e => e.OccupationOption)
            .Include(x => x.Relative)
            .Include(x => x.PatientTypeOption)
            .Include(x => x.PatientSubjectOption)
            .Include(x => x.PaymentSourceOption)
            .Include(x => x.ExamLocationOption)
            .ToListAsync(ct);

        var items = rows
            .Select(r => MapToResult(r, r.Session?.SessionCode ?? "", r.Session?.ExamDate ?? default))
            .ToList();
        return new PageResult<ExamRecordResult>(items, page, size, total);
    }
```

- [ ] **Step 6: Cập nhật `FakeExamRecordRepository`**

Trong `HealthExam.Tests/Application/ExamRecordHandlerTests.cs`, thêm vào phần thuộc tính của `FakeExamRecordRepository` (ngay sau `public ExamRecordFilter LastListFilter { get; private set; }`, dòng 1158):

```csharp
    public string LastHistoryDivisionId { get; private set; }
    public Guid? LastHistoryLineageID { get; private set; }
    public int LastHistoryPage { get; private set; }
    public int LastHistorySize { get; private set; }
    public PageResult<ExamRecordResult> HistoryPage { get; set; }
```

Và thêm method ngay sau `ListAsync` (kết thúc ở dòng 1177):

```csharp
    public Task<PageResult<ExamRecordResult>> ListSignedByPatientLineageAsync(
        string divisionId, Guid profileLineageID, int page, int size, CancellationToken ct = default)
    {
        LastHistoryDivisionId = divisionId;
        LastHistoryLineageID = profileLineageID;
        LastHistoryPage = page;
        LastHistorySize = size;
        return Task.FromResult(HistoryPage ?? new PageResult<ExamRecordResult>(
            Array.Empty<ExamRecordResult>(), page, size, 0));
    }
```

- [ ] **Step 7: Chạy test để xác nhận nó xanh**

Run: `dotnet test HealthExam.Tests/HealthExam.Tests.csproj --filter "FullyQualifiedName~PatientExamHistoryTests"`

Expected: PASS (8 test).

- [ ] **Step 8: Chạy bộ test hồ sơ khám + webhook**

Run: `dotnet test HealthExam.Tests/HealthExam.Tests.csproj --filter "FullyQualifiedName~ExamRecord"`

Sau đó: `dotnet test HealthExam.Tests/HealthExam.Tests.csproj --filter "FullyQualifiedName~Webhook"`

Expected: PASS cả hai.

- [ ] **Step 9: Commit**

```bash
git add HealthExam.Application/ExamRecords/IExamRecordRepository.cs HealthExam.Infrastructure/Persistence/Repositories/ExamRecordRepository.cs HealthExam.Tests/Application/ExamRecordHandlerTests.cs HealthExam.Tests/PatientExamHistoryTests.cs
git commit -m "feat(exam-record): query signed records by patient profile lineage

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>"
```

---

### Task 4: Handler, DI và endpoint `GET /v1/patients/{patientRefId}/exam-history`

**Files:**
- Create: `HealthExam.Application/ExamRecords/GetPatientExamHistory.cs`
- Modify: `HealthExam.API/Controllers/PatientController.cs`
- Modify: `HealthExam.API/Extensions/ApplicationServiceExtensions.cs` (dòng 47)
- Test: `HealthExam.Tests/Application/PatientExamHistoryHandlerTests.cs` (mới)
- Test: `HealthExam.Tests/PatientEndpointTests.cs`

**Interfaces:**
- Consumes: `IPatientRepository.FindProfileLineageIdAsync` (Task 2); `IExamRecordRepository.ListSignedByPatientLineageAsync`, `FakeExamRecordRepository.HistoryPage` / `LastHistory*` (Task 3)
- Produces: `GetPatientExamHistoryQuery`, `IGetPatientExamHistoryHandler`, `GetPatientExamHistoryHandler` (với `DefaultSize = 5`, `MaxSize = 200`); endpoint `GET /v1/patients/{patientRefId}/exam-history`

- [ ] **Step 1: Chạy impact analysis**

```
gitnexus_impact({target: "PatientController", direction: "upstream", repo: "health-exam-server"})
```

Báo blast radius. Thay đổi là **thêm action + thêm tham số constructor**; `CompositionRootTests.Api_resolves_every_controller` sẽ bắt nếu quên đăng ký DI.

- [ ] **Step 2: Viết test handler thất bại**

Tạo `HealthExam.Tests/Application/PatientExamHistoryHandlerTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using HealthExam.Application.Common;
using HealthExam.Application.ExamRecords;
using HealthExam.Domain.Patients;
using Xunit;

namespace HealthExam.Tests.Application;

public class PatientExamHistoryHandlerTests
{
    private const string Division = "D01";

    private static (GetPatientExamHistoryHandler Sut, FakeExamRecordRepository Records, Patient Patient)
        Build(Guid? lineageID = null)
    {
        var patient = new Patient
        {
            PatientRefID = Guid.NewGuid(),
            DivisionID = Division,
            FullName = "Nguyễn Văn An",
            ProfileLineageID = lineageID ?? Guid.NewGuid(),
            // Phiên bản đã bị thay thế vẫn phải tra được lịch sử khám.
            IsActive = false
        };
        var patients = new FakePatientRepository(patient);
        var records = new FakeExamRecordRepository();
        return (new GetPatientExamHistoryHandler(patients, records), records, patient);
    }

    [Fact]
    public async Task PatientRefID_rong_tra_BadRequest()
    {
        var (sut, _, _) = Build();

        var result = await sut.HandleAsync(new GetPatientExamHistoryQuery(Division, Guid.Empty));

        Assert.False(result.IsSuccess);
        Assert.Equal(ApplicationFailureCode.BadRequest, result.Failure.Code);
    }

    [Fact]
    public async Task Khong_tim_thay_nguoi_benh_tra_NotFound()
    {
        var (sut, _, _) = Build();

        var result = await sut.HandleAsync(new GetPatientExamHistoryQuery(Division, Guid.NewGuid()));

        Assert.False(result.IsSuccess);
        Assert.Equal(ApplicationFailureCode.NotFound, result.Failure.Code);
    }

    [Fact]
    public async Task Ho_so_don_vi_khac_tra_NotFound()
    {
        var (sut, _, patient) = Build();

        var result = await sut.HandleAsync(new GetPatientExamHistoryQuery("D99", patient.PatientRefID));

        Assert.False(result.IsSuccess);
        Assert.Equal(ApplicationFailureCode.NotFound, result.Failure.Code);
    }

    [Fact]
    public async Task Tra_cuu_theo_lineage_cua_phien_ban_duoc_truyen()
    {
        var lineageID = Guid.NewGuid();
        var (sut, records, patient) = Build(lineageID);

        var result = await sut.HandleAsync(new GetPatientExamHistoryQuery(Division, patient.PatientRefID));

        Assert.True(result.IsSuccess);
        Assert.Equal(Division, records.LastHistoryDivisionId);
        Assert.Equal(lineageID, records.LastHistoryLineageID);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public async Task Size_khong_hop_le_ve_mac_dinh_5(int size)
    {
        var (sut, records, patient) = Build();

        await sut.HandleAsync(new GetPatientExamHistoryQuery(Division, patient.PatientRefID, Page: 0, Size: size));

        Assert.Equal(1, records.LastHistoryPage);
        Assert.Equal(5, records.LastHistorySize);
    }

    [Fact]
    public async Task Size_vuot_tran_bi_kep_200()
    {
        var (sut, records, patient) = Build();

        await sut.HandleAsync(new GetPatientExamHistoryQuery(Division, patient.PatientRefID, Page: 2, Size: 10000));

        Assert.Equal(2, records.LastHistoryPage);
        Assert.Equal(200, records.LastHistorySize);
    }

    [Fact]
    public async Task Tra_thang_trang_ket_qua_cua_repository()
    {
        var (sut, records, patient) = Build();
        records.HistoryPage = new PageResult<ExamRecordResult>(
            new List<ExamRecordResult>(), 3, 5, 42);

        var result = await sut.HandleAsync(new GetPatientExamHistoryQuery(Division, patient.PatientRefID, Page: 3));

        Assert.True(result.IsSuccess);
        Assert.Equal(42, result.Value.Total);
        Assert.Equal(3, result.Value.Page);
    }
}
```

- [ ] **Step 3: Chạy test để xác nhận nó đỏ**

Run: `dotnet test HealthExam.Tests/HealthExam.Tests.csproj --filter "FullyQualifiedName~PatientExamHistoryHandlerTests"`

Expected: FAIL — `CS0246`: không tìm thấy `GetPatientExamHistoryHandler` / `GetPatientExamHistoryQuery`.

- [ ] **Step 4: Viết handler**

Tạo `HealthExam.Application/ExamRecords/GetPatientExamHistory.cs`:

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using HealthExam.Application.Common;
using HealthExam.Application.Patients;

namespace HealthExam.Application.ExamRecords;

/// <summary>
/// "Đợt khám trước" — các hồ sơ KSK ĐÃ KÝ KẾT LUẬN của người bệnh đang mở.
///
/// Size mặc định 5 chứ không phải 20 như GET /v1/exam-records: khối này là chú thích dưới
/// bảng Danh sách hồ sơ khám trong một cột hẹp, không phải màn hình chính.
/// </summary>
public sealed record GetPatientExamHistoryQuery(
    string DivisionId,
    Guid PatientRefID,
    int Page = 1,
    int Size = 0);

public interface IGetPatientExamHistoryHandler
{
    Task<ApplicationResult<PageResult<ExamRecordResult>>> HandleAsync(
        GetPatientExamHistoryQuery query, CancellationToken ct = default);
}

public sealed class GetPatientExamHistoryHandler : IGetPatientExamHistoryHandler
{
    public const int DefaultSize = 5;
    public const int MaxSize = 200;

    private readonly IPatientRepository _patientRepository;
    private readonly IExamRecordRepository _examRecordRepository;

    public GetPatientExamHistoryHandler(
        IPatientRepository patientRepository,
        IExamRecordRepository examRecordRepository)
    {
        _patientRepository = patientRepository;
        _examRecordRepository = examRecordRepository;
    }

    public async Task<ApplicationResult<PageResult<ExamRecordResult>>> HandleAsync(
        GetPatientExamHistoryQuery query, CancellationToken ct = default)
    {
        if (query.PatientRefID == Guid.Empty)
        {
            return ApplicationResult<PageResult<ExamRecordResult>>.Fail(
                ApplicationFailureCode.BadRequest,
                "PatientRefID không hợp lệ.");
        }

        // Một người khám nhiều đợt có thể mang nhiều PatientRefID (mỗi lần sửa thông tin cá
        // nhân sinh một phiên bản). Dòng hồ sơ mới là danh tính của NGƯỜI.
        var lineageID = await _patientRepository.FindProfileLineageIdAsync(
            query.DivisionId, query.PatientRefID, ct);

        if (lineageID == null)
        {
            return ApplicationResult<PageResult<ExamRecordResult>>.Fail(
                ApplicationFailureCode.NotFound,
                "Không tìm thấy hồ sơ người bệnh.");
        }

        var page = query.Page < 1 ? 1 : query.Page;
        var size = query.Size < 1 ? DefaultSize : (query.Size > MaxSize ? MaxSize : query.Size);

        var result = await _examRecordRepository.ListSignedByPatientLineageAsync(
            query.DivisionId, lineageID.Value, page, size, ct);

        return ApplicationResult<PageResult<ExamRecordResult>>.Success(result);
    }
}
```

- [ ] **Step 5: Chạy test handler để xác nhận nó xanh**

Run: `dotnet test HealthExam.Tests/HealthExam.Tests.csproj --filter "FullyQualifiedName~PatientExamHistoryHandlerTests"`

Expected: PASS (8 test).

- [ ] **Step 6: Commit handler**

```bash
git add HealthExam.Application/ExamRecords/GetPatientExamHistory.cs HealthExam.Tests/Application/PatientExamHistoryHandlerTests.cs
git commit -m "feat(patient): add get patient exam history handler

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>"
```

- [ ] **Step 7: Viết test endpoint thất bại**

Trong `HealthExam.Tests/PatientEndpointTests.cs`, thêm `using` còn thiếu ở đầu file:

```csharp
using HealthExam.Domain.ExamRecords;
using HealthExam.Domain.ExamSessions;
```

Rồi thêm helper seed và 4 test vào trong class:

```csharp
    private async Task<Guid> SeedSignedHistoryAsync(
        string divisionId = "DEV",
        string recordCode = "KSK-2026-0001",
        string sessionCode = "DK-2026",
        DateOnly? examDate = null,
        Guid? lineageID = null,
        string hisSignStatus = "Signed")
    {
        var lineage = lineageID ?? Guid.NewGuid();
        var patientRefID = Guid.NewGuid();
        var sessionID = Guid.NewGuid();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HealthExamDbContext>();
        db.Patients.Add(new Patient
        {
            PatientRefID = patientRefID,
            DivisionID = divisionId,
            FullName = "Nguyễn Văn An",
            IdentityNumber = "079123456789",
            PatientCode = "BN000125001",
            ProfileLineageID = lineage,
            IsActive = true
        });
        db.ExamSessions.Add(new ExamSession
        {
            SessionID = sessionID,
            DivisionID = divisionId,
            SessionCode = sessionCode,
            SessionName = "Đợt khám công ty A",
            ExamDate = examDate ?? new DateOnly(2026, 3, 14)
        });
        db.ExamRecords.Add(new ExamRecord
        {
            RecordID = Guid.NewGuid(),
            DivisionID = divisionId,
            SessionID = sessionID,
            RecordCode = recordCode,
            PatientRefID = patientRefID,
            VariantCode = "DTK_01",
            HealthClassCode = "II",
            State = ExamRecordState.Completed,
            HisSignStatus = hisSignStatus,
            HisSignedAt = new DateTime(2026, 3, 14, 9, 12, 0, DateTimeKind.Utc)
        });
        await db.SaveChangesAsync();
        return patientRefID;
    }

    [Fact]
    public async Task Exam_history_tra_envelope_phan_trang_size_mac_dinh_5()
    {
        var patientRefID = await SeedSignedHistoryAsync();

        var client = CreateEmployeeClient("DEV");
        var response = await client.GetAsync($"/v1/patients/{patientRefID}/exam-history");
        var body = JObject.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(0, body["ErrorCode"]!.Value<int>());
        Assert.Equal(1, body["Data"]!["Page"]!.Value<int>());
        Assert.Equal(5, body["Data"]!["Size"]!.Value<int>());
        Assert.Equal(1, body["Data"]!["Total"]!.Value<int>());

        var items = Assert.IsAssignableFrom<JArray>(body["Data"]!["Items"]);
        var item = Assert.Single(items);
        Assert.Equal("KSK-2026-0001", item["RecordCode"]!.Value<string>());
        Assert.Equal("DK-2026", item["SessionCode"]!.Value<string>());
        Assert.Equal("II", item["HealthClassCode"]!.Value<string>());
        Assert.Equal("Signed", item["HisSignStatus"]!.Value<string>());
        Assert.NotNull(item["HisSignedAt"]);
    }

    [Fact]
    public async Task Exam_history_bo_qua_ho_so_chua_ky_ket_luan()
    {
        var patientRefID = await SeedSignedHistoryAsync(
            recordCode: "KSK-CHUA-KY", hisSignStatus: "InProcessing");

        var client = CreateEmployeeClient("DEV");
        var response = await client.GetAsync($"/v1/patients/{patientRefID}/exam-history");
        var body = JObject.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(0, body["Data"]!["Total"]!.Value<int>());
        Assert.Empty(Assert.IsAssignableFrom<JArray>(body["Data"]!["Items"]));
    }

    [Fact]
    public async Task Exam_history_cua_patientRefId_la_tra_404()
    {
        var client = CreateEmployeeClient("DEV");
        var response = await client.GetAsync($"/v1/patients/{Guid.NewGuid()}/exam-history");
        var body = JObject.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(ErrorCodes.NotFound, body["ErrorCode"]!.Value<int>());
    }

    [Fact]
    public async Task Exam_history_khong_cho_khach_vang_lai()
    {
        var patientRefID = await SeedSignedHistoryAsync();

        var client = CreateAnonymousClient("DEV");
        var response = await client.GetAsync($"/v1/patients/{patientRefID}/exam-history");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
```

- [ ] **Step 8: Chạy test endpoint để xác nhận nó đỏ**

Run: `dotnet test HealthExam.Tests/HealthExam.Tests.csproj --filter "FullyQualifiedName~PatientEndpointTests"`

Expected: FAIL — `Exam_history_tra_envelope_phan_trang_size_mac_dinh_5` và `Exam_history_bo_qua_ho_so_chua_ky_ket_luan` trả `404` vì route chưa tồn tại. (`Exam_history_cua_patientRefId_la_tra_404` có thể xanh giả ở bước này — đúng như kỳ vọng, nó chỉ có nghĩa sau khi route tồn tại.)

- [ ] **Step 9: Thêm action vào `PatientController`**

Trong `HealthExam.API/Controllers/PatientController.cs`, bổ sung `using` còn thiếu ở đầu file:

```csharp
using HealthExam.Application.Common;
using HealthExam.Application.ExamRecords;
```

Thay khối field + constructor thành:

```csharp
    private readonly ISearchPatientsHandler _searchHandler;
    private readonly IGetPatientProfileHandler _getHandler;
    private readonly IMatchPatientProfileHandler _matchHandler;
    private readonly IGetPatientExamHistoryHandler _examHistoryHandler;

    public PatientController(
        ISearchPatientsHandler searchHandler,
        IGetPatientProfileHandler getHandler,
        IMatchPatientProfileHandler matchHandler,
        IGetPatientExamHistoryHandler examHistoryHandler)
    {
        _searchHandler = searchHandler;
        _getHandler = getHandler;
        _matchHandler = matchHandler;
        _examHistoryHandler = examHistoryHandler;
    }
```

Thêm action ngay sau action `Get`:

```csharp
    /// <summary>
    /// Danh sách các đợt khám trước của người bệnh — những hồ sơ KSK đã ký kết luận.
    /// </summary>
    [HttpGet("{patientRefId:guid}/exam-history")]
    [SwaggerOperation(
        Summary = "Lịch sử khám của người bệnh",
        Description = "Trả các hồ sơ KSK đã ký kết luận (HisSignStatus = Signed) của MỌI phiên bản hồ sơ cùng dòng (ProfileLineageID) trong X-Division-Id, mới nhất lên đầu theo ngày khám của đợt. size mặc định 5, tối đa 200; page mặc định 1. 404 nếu PatientRefID không thuộc đơn vị.")]
    public async Task<ActionResult<ResultData<PageResult<ExamRecordResult>>>> ExamHistory(
        Guid patientRefId,
        [FromQuery] int page = 1,
        [FromQuery] int size = 0,
        CancellationToken ct = default)
    {
        var result = await _examHistoryHandler.HandleAsync(
            new GetPatientExamHistoryQuery(HealthExamContext.DivisionId, patientRefId, page, size), ct);
        return ToActionResult(result);
    }
```

- [ ] **Step 10: Đăng ký DI**

Trong `HealthExam.API/Extensions/ApplicationServiceExtensions.cs`, thêm ngay sau dòng 47 (`services.AddScoped<IMatchPatientProfileHandler, MatchPatientProfileHandler>();`):

```csharp
        services.AddScoped<IGetPatientExamHistoryHandler, GetPatientExamHistoryHandler>();
```

- [ ] **Step 11: Chạy test endpoint để xác nhận nó xanh**

Run: `dotnet test HealthExam.Tests/HealthExam.Tests.csproj --filter "FullyQualifiedName~PatientEndpointTests"`

Expected: PASS.

- [ ] **Step 12: Chạy toàn bộ test**

Run: `dotnet test HealthExam.Tests/HealthExam.Tests.csproj`

Expected: PASS toàn bộ. Đặc biệt xác nhận `CompositionRootTests.Di_resolves_all_application_handlers` và `CompositionRootTests.Api_resolves_every_controller` xanh.

- [ ] **Step 13: Kiểm tra phạm vi thay đổi trước khi commit**

```
gitnexus_detect_changes()
```

Kỳ vọng phạm vi ảnh hưởng chỉ gồm: `ExamRecordResult`, `MapToResult`, `IPatientRepository`/`PatientRepository`, `IExamRecordRepository`/`ExamRecordRepository`, `PatientController`, `ApplicationServiceExtensions`, handler mới và các file test. Kết quả partial/truncated thì chạy lại.

- [ ] **Step 14: Commit**

```bash
git add HealthExam.API/Controllers/PatientController.cs HealthExam.API/Extensions/ApplicationServiceExtensions.cs HealthExam.Tests/PatientEndpointTests.cs
git commit -m "feat(patient): expose signed exam history endpoint

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>"
```

---

## Bàn giao cho frontend

Sau khi 4 task xong, báo lại cho repo `frontend/medviet` (KHÔNG sửa trong plan này):

- `apps/health-exam/src/components/workspace/exam-history-block.tsx` đang gọi `useExamRecordListQuery({ patientCode, page, size })` (ràng buộc của #304 vì lúc đó chưa có endpoint theo người bệnh). Đổi sang `GET /v1/patients/{patientRefId}/exam-history`, lấy `patientRefId` từ `record.PatientRefID` thay cho `record.PatientCode`.
- Hình dạng trả về không đổi: `PaginationData<ExamRecordItem>` — không cần type mới. Hai field mới `HisSignStatus`, `HisSignedAt` bổ sung vào `packages/types/src/health-exam/exam-record.ts`.
- Cỡ trang `[5, 10, 20]` và mặc định 5 trong `exam-history-search-params.ts` khớp sẵn với server.
- `exam-history-conclusion.ts`: nhánh `cancelled` và `none` thành code chết vì endpoint chỉ trả hồ sơ đã ký — dọn khi đổi hook.
