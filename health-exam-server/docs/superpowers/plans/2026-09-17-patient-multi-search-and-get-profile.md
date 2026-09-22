# Patient Multi-Mode Search & Get Profile Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Mở rộng `GET /v1/patients/search` thành 5 chế độ (CCCD / mã BN / tên / SĐT / tự nhận diện) trả danh sách tóm tắt, và thêm `GET /v1/patients/{patientRefId}` trả hồ sơ đầy đủ để fill form đăng ký.

**Architecture:** Clean Architecture 4 project (`Domain` → `Application` → `Infrastructure` / `API`). Application chứa query/handler/interface repository; Infrastructure chứa EF Core `PatientRepository` (PostgreSQL); API chứa controller trả envelope `ResultData<T>`. Bộ phân loại từ khoá là class thuần (`PatientSearchKeywordClassifier`) không phụ thuộc DB. Match / Create / Update exam-record **không thay đổi**.

**Tech Stack:** .NET 8, ASP.NET Core, EF Core + Npgsql, xUnit, `Microsoft.AspNetCore.Mvc.Testing` (endpoint tests dùng InMemory DB), `PostgresFixture` (tests DB thật, tự skip nếu không có `HEALTHEXAM_TEST_DB`).

**Spec:** Thiết kế đã được duyệt trong chat (bounded path, không có spec file). Tóm tắt quyết định:

- `type` ∈ `identity | code | name | phone | auto` (mặc định `auto`); `keyword` bắt buộc, trim hai đầu.
- `identity` / `phone` / `code` → `StartsWith` (`code` không phân biệt hoa/thường); `name` → `Contains`, không phân biệt hoa/thường, **giữ dấu** tiếng Việt.
- `auto`: toàn số 9 hoặc 12 ký tự → `identity`; toàn số 10 ký tự bắt đầu `0` → `phone`; toàn số độ dài khác → `identity + phone + code`; có chữ cái → `name + code`.
- Chỉ trả `IsActive == true` trong `DivisionID` hiện tại, tối đa **20**, sắp theo `FullName` rồi `PatientCode`.
- Item tóm tắt: `PatientRefID, PatientCode, FullName, BirthYear, GenderID, IdentityNumber`; `BirthYear` null → `Dob.Year`.
- Keyword trống hoặc `type` không hợp lệ → `400` / `4001`. Không tìm thấy → `Data: []`.
- `GET /v1/patients/{patientRefId}` trả `PatientProfileResult` (record hiện có); không active hoặc sai division → `404` / `4040`.

## Global Constraints

- Mọi query phải lọc `DivisionID == HealthExamContext.DivisionId` và `IsActive == true` (không bao giờ trả version inactive hay hồ sơ đơn vị khác).
- Giới hạn kết quả search: `SearchPatientsHandler.MaxResults = 20` (hằng số, không nhận từ client).
- Thông báo lỗi bằng tiếng Việt có dấu, giống các handler hiện có.
- Mã lỗi: dùng `ApplicationFailureCode.BadRequest` (→ HTTP 400 / `4001`) và `ApplicationFailureCode.NotFound` (→ HTTP 404 / `4040`); không thêm mã mới.
- Chạy test: `dotnet test HealthExam.Tests/HealthExam.Tests.csproj --filter "FullyQualifiedName~<TestClass>"` từ thư mục gốc repo. Test Postgres tự bỏ qua khi thiếu env `HEALTHEXAM_TEST_DB`.
- Theo `CLAUDE.md`: trước khi sửa symbol có sẵn (`PatientController`, `IPatientRepository`, `PatientRepository`, `FakePatientRepository`) phải chạy `gitnexus_impact({target, direction: "upstream", repo: "health-exam-server"})` và báo blast radius; nếu index báo stale/degraded thì grep caller thủ công và ghi rõ. Trước mỗi commit chạy `gitnexus_detect_changes()`.
- Commit message theo conventional style của repo (`feat(patient): ...`, `test(patient): ...`, `docs: ...`) và kết thúc bằng dòng `Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>`.

---

## File Structure

| File | Trách nhiệm |
| --- | --- |
| `HealthExam.Application/Patients/PatientSearchKeywordClassifier.cs` (mới) | Enum `PatientSearchField`, hàm thuần `Classify(keyword)` cho chế độ auto |
| `HealthExam.Application/Patients/SearchPatients.cs` (mới) | `SearchPatientsQuery`, `PatientSearchCriteria`, `PatientSearchItem`, `ISearchPatientsHandler`, `SearchPatientsHandler` |
| `HealthExam.Application/Patients/GetPatientProfile.cs` (mới) | `GetPatientProfileQuery`, `PatientProfileResult` (chuyển từ `SearchActivePatient.cs`), `IGetPatientProfileHandler`, `GetPatientProfileHandler` |
| `HealthExam.Application/Patients/SearchActivePatient.cs` (xoá ở Task 5) | Handler search-theo-CCCD cũ |
| `HealthExam.Application/Patients/IPatientRepository.cs` (sửa) | Thêm `SearchActiveAsync`, `FindActiveByRefIdAsync` |
| `HealthExam.Infrastructure/Persistence/Repositories/PatientRepository.cs` (sửa) | EF impl 2 method mới |
| `HealthExam.API/Controllers/PatientController.cs` (sửa) | Action `Search` mới + action `Get` |
| `HealthExam.API/Extensions/ApplicationServiceExtensions.cs` (sửa) | Đăng ký 2 handler mới, bỏ handler cũ |
| `HealthExam.Tests/Application/PatientRegistrationTestDoubles.cs` (sửa) | `FakePatientRepository` implement 2 method mới |
| `HealthExam.Tests/Application/PatientSearchKeywordClassifierTests.cs` (mới) | Unit test classifier |
| `HealthExam.Tests/Application/SearchPatientsTests.cs` (mới) | Unit test handler search |
| `HealthExam.Tests/Application/GetPatientProfileTests.cs` (mới) | Unit test handler get |
| `HealthExam.Tests/Application/SearchActivePatientTests.cs` (xoá ở Task 5) | Test cũ |
| `HealthExam.Tests/Infrastructure/PatientProfilePersistenceTests.cs` (sửa) | Postgres test cho 2 method repo mới |
| `HealthExam.Tests/PatientEndpointTests.cs` (sửa) | Endpoint tests search mới + get |
| `docs/api/patient-profile-search-and-reuse-api.md` (sửa) | Cập nhật mục 0, 1, 3 và thêm mục "Lấy hồ sơ theo PatientRefID" |

---

### Task 1: Bộ phân loại từ khoá (`PatientSearchKeywordClassifier`)

**Files:**
- Create: `HealthExam.Application/Patients/PatientSearchKeywordClassifier.cs`
- Test: `HealthExam.Tests/Application/PatientSearchKeywordClassifierTests.cs`

**Interfaces:**
- Consumes: không có.
- Produces:
  - `public enum PatientSearchField { Identity, Phone, Code, Name }`
  - `public static class PatientSearchKeywordClassifier { public static IReadOnlyList<PatientSearchField> Classify(string keyword); }` — keyword đã trim; trả về danh sách field cần tìm (không rỗng với input không rỗng).

- [ ] **Step 1: Viết test thất bại**

```csharp
// HealthExam.Tests/Application/PatientSearchKeywordClassifierTests.cs
using HealthExam.Application.Patients;
using Xunit;

namespace HealthExam.Tests.Application;

public class PatientSearchKeywordClassifierTests
{
    [Theory]
    [InlineData("079123456789")]   // 12 số → CCCD
    [InlineData("123456789")]      // 9 số → CMND
    public void Digits_of_identity_length_classify_as_identity_only(string keyword)
    {
        var fields = PatientSearchKeywordClassifier.Classify(keyword);
        Assert.Equal(new[] { PatientSearchField.Identity }, fields);
    }

    [Fact]
    public void Ten_digits_starting_with_zero_classify_as_phone_only()
    {
        var fields = PatientSearchKeywordClassifier.Classify("0901234567");
        Assert.Equal(new[] { PatientSearchField.Phone }, fields);
    }

    [Theory]
    [InlineData("0791")]          // gõ dở
    [InlineData("1234567890")]    // 10 số nhưng không bắt đầu bằng 0
    [InlineData("12345678901")]   // 11 số
    public void Other_all_digit_keywords_search_identity_phone_and_code(string keyword)
    {
        var fields = PatientSearchKeywordClassifier.Classify(keyword);
        Assert.Equal(
            new[] { PatientSearchField.Identity, PatientSearchField.Phone, PatientSearchField.Code },
            fields);
    }

    [Theory]
    [InlineData("Nguyễn Văn An")]
    [InlineData("HEX-KSK2026")]
    [InlineData("BN000125001")]
    public void Keywords_with_letters_search_name_and_code(string keyword)
    {
        var fields = PatientSearchKeywordClassifier.Classify(keyword);
        Assert.Equal(new[] { PatientSearchField.Name, PatientSearchField.Code }, fields);
    }

    [Fact]
    public void Classify_trims_before_measuring_length()
    {
        var fields = PatientSearchKeywordClassifier.Classify("  079123456789  ");
        Assert.Equal(new[] { PatientSearchField.Identity }, fields);
    }
}
```

- [ ] **Step 2: Chạy test để xác nhận thất bại**

Run: `dotnet test HealthExam.Tests/HealthExam.Tests.csproj --filter "FullyQualifiedName~PatientSearchKeywordClassifierTests"`
Expected: build FAIL — `PatientSearchKeywordClassifier` / `PatientSearchField` chưa tồn tại.

- [ ] **Step 3: Viết implementation tối thiểu**

```csharp
// HealthExam.Application/Patients/PatientSearchKeywordClassifier.cs
using System.Collections.Generic;
using System.Linq;

namespace HealthExam.Application.Patients;

/// <summary>Trường được dùng để tìm hồ sơ người bệnh.</summary>
public enum PatientSearchField
{
    Identity,
    Phone,
    Code,
    Name
}

/// <summary>
/// Chế độ "tự nhận diện": đoán người dùng đang gõ CCCD, SĐT, mã BN hay tên.
/// Mã BN không có format cố định (nhập tay hoặc "HEX-&lt;recordCode&gt;") nên khi mơ hồ
/// thì gộp nhiều trường thay vì đoán sai.
/// </summary>
public static class PatientSearchKeywordClassifier
{
    public static IReadOnlyList<PatientSearchField> Classify(string keyword)
    {
        var kw = (keyword ?? "").Trim();
        var allDigits = kw.Length > 0 && kw.All(char.IsDigit);

        if (!allDigits)
        {
            return new[] { PatientSearchField.Name, PatientSearchField.Code };
        }

        if (kw.Length == 9 || kw.Length == 12)
        {
            return new[] { PatientSearchField.Identity };
        }

        if (kw.Length == 10 && kw[0] == '0')
        {
            return new[] { PatientSearchField.Phone };
        }

        return new[] { PatientSearchField.Identity, PatientSearchField.Phone, PatientSearchField.Code };
    }
}
```

- [ ] **Step 4: Chạy test để xác nhận pass**

Run: `dotnet test HealthExam.Tests/HealthExam.Tests.csproj --filter "FullyQualifiedName~PatientSearchKeywordClassifierTests"`
Expected: PASS (7 test cases).

- [ ] **Step 5: Commit**

```bash
git add HealthExam.Application/Patients/PatientSearchKeywordClassifier.cs HealthExam.Tests/Application/PatientSearchKeywordClassifierTests.cs
git commit -m "feat(patient): classify search keyword for auto mode

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>"
```

---

### Task 2: Repository — `SearchActiveAsync` và `FindActiveByRefIdAsync`

**Files:**
- Modify: `HealthExam.Application/Patients/IPatientRepository.cs`
- Create: `HealthExam.Application/Patients/SearchPatients.cs` (chỉ phần `PatientSearchCriteria` ở task này; handler thêm ở Task 3)
- Modify: `HealthExam.Infrastructure/Persistence/Repositories/PatientRepository.cs`
- Modify: `HealthExam.Tests/Application/PatientRegistrationTestDoubles.cs` (class `FakePatientRepository`)
- Test: `HealthExam.Tests/Infrastructure/PatientProfilePersistenceTests.cs`

**Interfaces:**
- Consumes: `PatientSearchField` (Task 1).
- Produces:
  - `public sealed record PatientSearchCriteria(string Keyword, IReadOnlyList<PatientSearchField> Fields);` — `Keyword` đã trim, `Fields` không rỗng.
  - `IPatientRepository.SearchActiveAsync(string divisionId, PatientSearchCriteria criteria, int limit, CancellationToken ct = default) : Task<IReadOnlyList<Patient>>`
  - `IPatientRepository.FindActiveByRefIdAsync(string divisionId, Guid patientRefID, CancellationToken ct = default) : Task<Patient>`

- [ ] **Step 0: Impact analysis (bắt buộc theo CLAUDE.md)**

Chạy `gitnexus_impact({target: "IPatientRepository", direction: "upstream", repo: "health-exam-server"})` và `gitnexus_impact({target: "PatientRepository", direction: "upstream", repo: "health-exam-server"})`. Báo cáo blast radius cho user. Nếu index degraded, ghi kết quả grep thay thế: implementer của `IPatientRepository` hiện chỉ có `PatientRepository` (Infrastructure) và `FakePatientRepository` (Tests) — cả hai đều được sửa trong task này nên rủi ro thấp.

- [ ] **Step 1: Thêm `PatientSearchCriteria` vào file mới `SearchPatients.cs`**

```csharp
// HealthExam.Application/Patients/SearchPatients.cs
using System.Collections.Generic;

namespace HealthExam.Application.Patients;

/// <summary>Tiêu chí tìm hồ sơ active: một keyword áp lên một hoặc nhiều trường (OR).</summary>
public sealed record PatientSearchCriteria(
    string Keyword,
    IReadOnlyList<PatientSearchField> Fields);
```

- [ ] **Step 2: Thêm 2 method vào `IPatientRepository`**

Sửa `HealthExam.Application/Patients/IPatientRepository.cs`, thêm hai dòng sau ngay dưới `FindActiveByIdentityNumberAsync`:

```csharp
    Task<Patient> FindActiveByRefIdAsync(string divisionId, Guid patientRefID, CancellationToken ct = default);
    Task<IReadOnlyList<Patient>> SearchActiveAsync(string divisionId, PatientSearchCriteria criteria, int limit, CancellationToken ct = default);
```

Thêm `using System.Collections.Generic;` ở đầu file.

- [ ] **Step 3: Implement trong `FakePatientRepository`** (để project Tests compile)

Thêm vào class `FakePatientRepository` trong `HealthExam.Tests/Application/PatientRegistrationTestDoubles.cs`, ngay dưới `FindActiveByIdentityNumberAsync`:

```csharp
    public Task<Patient> FindActiveByRefIdAsync(string divisionId, Guid patientRefID, CancellationToken ct = default)
    {
        var patient = Patients.FirstOrDefault(p => p.DivisionID == divisionId && p.PatientRefID == patientRefID && p.IsActive);
        return Task.FromResult(patient);
    }

    public Task<IReadOnlyList<Patient>> SearchActiveAsync(string divisionId, PatientSearchCriteria criteria, int limit, CancellationToken ct = default)
    {
        var kw = criteria.Keyword.Trim();
        var kwLower = kw.ToLowerInvariant();
        bool byIdentity = criteria.Fields.Contains(PatientSearchField.Identity);
        bool byPhone = criteria.Fields.Contains(PatientSearchField.Phone);
        bool byCode = criteria.Fields.Contains(PatientSearchField.Code);
        bool byName = criteria.Fields.Contains(PatientSearchField.Name);

        IReadOnlyList<Patient> result = Patients
            .Where(p => p.DivisionID == divisionId && p.IsActive)
            .Where(p =>
                (byIdentity && (p.IdentityNumber ?? "").StartsWith(kw, StringComparison.Ordinal)) ||
                (byPhone && (p.PhoneNumber ?? "").StartsWith(kw, StringComparison.Ordinal)) ||
                (byCode && (p.PatientCode ?? "").ToLowerInvariant().StartsWith(kwLower, StringComparison.Ordinal)) ||
                (byName && (p.FullName ?? "").ToLowerInvariant().Contains(kwLower, StringComparison.Ordinal)))
            .OrderBy(p => p.FullName, StringComparer.Ordinal)
            .ThenBy(p => p.PatientCode, StringComparer.Ordinal)
            .Take(limit)
            .ToList();
        return Task.FromResult(result);
    }
```

- [ ] **Step 4: Viết Postgres test thất bại**

Thêm vào class `PatientProfilePersistenceTests` (`HealthExam.Tests/Infrastructure/PatientProfilePersistenceTests.cs`) trước helper `NewPatient`:

```csharp
    [Fact]
    public async Task SearchActive_by_name_is_case_insensitive_keeps_accents_and_respects_limit()
    {
        if (!_fixture.Enabled) return;

        var divisionId = $"N_{Guid.NewGuid():N}"[..20];
        await using var db = _fixture.NewContext();
        var an = NewPatient(divisionId, $"A{Guid.NewGuid():N}"[..12], active: true);
        an.FullName = "Nguyễn Văn An";
        var anh = NewPatient(divisionId, $"B{Guid.NewGuid():N}"[..12], active: true);
        anh.FullName = "Trần Thị Ánh";
        var inactive = NewPatient(divisionId, $"C{Guid.NewGuid():N}"[..12], active: false);
        inactive.FullName = "Nguyễn Văn An";
        db.Patients.AddRange(an, anh, inactive);
        await db.SaveChangesAsync();

        var repo = new PatientRepository(db);
        var criteria = new PatientSearchCriteria("văn an", new[] { PatientSearchField.Name });

        var found = await repo.SearchActiveAsync(divisionId, criteria, limit: 20);
        Assert.Single(found);
        Assert.Equal(an.PatientRefID, found[0].PatientRefID);

        // "an" (không dấu) không khớp "Ánh" vì giữ dấu; chỉ khớp "An"
        var limited = await repo.SearchActiveAsync(
            divisionId, new PatientSearchCriteria("n", new[] { PatientSearchField.Name }), limit: 1);
        Assert.Single(limited);
    }

    [Fact]
    public async Task SearchActive_combines_fields_with_or_and_prefix_match()
    {
        if (!_fixture.Enabled) return;

        var divisionId = $"O_{Guid.NewGuid():N}"[..20];
        var otherDivision = $"X_{Guid.NewGuid():N}"[..20];
        await using var db = _fixture.NewContext();
        var byPhone = NewPatient(divisionId, $"P{Guid.NewGuid():N}"[..12], active: true);
        byPhone.PhoneNumber = "0901234567";
        var byCode = NewPatient(divisionId, $"Q{Guid.NewGuid():N}"[..12], active: true);
        byCode.PatientCode = "hex-0901";
        var crossDivision = NewPatient(otherDivision, $"R{Guid.NewGuid():N}"[..12], active: true);
        crossDivision.PhoneNumber = "0901234567";
        db.Patients.AddRange(byPhone, byCode, crossDivision);
        await db.SaveChangesAsync();

        var repo = new PatientRepository(db);
        var criteria = new PatientSearchCriteria(
            "0901", new[] { PatientSearchField.Identity, PatientSearchField.Phone });
        var found = await repo.SearchActiveAsync(divisionId, criteria, limit: 20);
        Assert.Single(found);
        Assert.Equal(byPhone.PatientRefID, found[0].PatientRefID);

        var codeCriteria = new PatientSearchCriteria("HEX-", new[] { PatientSearchField.Code });
        var codeFound = await repo.SearchActiveAsync(divisionId, codeCriteria, limit: 20);
        Assert.Single(codeFound);
        Assert.Equal(byCode.PatientRefID, codeFound[0].PatientRefID);
    }

    [Fact]
    public async Task FindActiveByRefId_ignores_inactive_and_other_division()
    {
        if (!_fixture.Enabled) return;

        var divisionId = $"G_{Guid.NewGuid():N}"[..20];
        await using var db = _fixture.NewContext();
        var active = NewPatient(divisionId, $"G{Guid.NewGuid():N}"[..12], active: true);
        var inactive = NewPatient(divisionId, $"H{Guid.NewGuid():N}"[..12], active: false);
        db.Patients.AddRange(active, inactive);
        await db.SaveChangesAsync();

        var repo = new PatientRepository(db);
        Assert.NotNull(await repo.FindActiveByRefIdAsync(divisionId, active.PatientRefID));
        Assert.Null(await repo.FindActiveByRefIdAsync(divisionId, inactive.PatientRefID));
        Assert.Null(await repo.FindActiveByRefIdAsync("OTHER", active.PatientRefID));
    }
```

Thêm `using HealthExam.Application.Patients;` và `using System.Collections.Generic;` vào đầu file test nếu chưa có.

- [ ] **Step 5: Chạy test để xác nhận thất bại**

Run: `dotnet test HealthExam.Tests/HealthExam.Tests.csproj --filter "FullyQualifiedName~PatientProfilePersistenceTests"`
Expected: build FAIL — `PatientRepository` chưa implement 2 member mới của interface.

- [ ] **Step 6: Implement EF trong `PatientRepository`**

Thêm vào `HealthExam.Infrastructure/Persistence/Repositories/PatientRepository.cs` ngay dưới `FindActiveByIdentityNumberAsync`:

```csharp
    public async Task<Patient> FindActiveByRefIdAsync(string divisionId, Guid patientRefID, CancellationToken ct = default)
    {
        if (patientRefID == Guid.Empty) return null;

        return await _db.Patients.AsNoTracking()
            .FirstOrDefaultAsync(p => p.DivisionID == divisionId && p.PatientRefID == patientRefID && p.IsActive, ct);
    }

    public async Task<IReadOnlyList<Patient>> SearchActiveAsync(
        string divisionId, PatientSearchCriteria criteria, int limit, CancellationToken ct = default)
    {
        if (criteria == null || string.IsNullOrWhiteSpace(criteria.Keyword) || criteria.Fields == null || criteria.Fields.Count == 0)
        {
            return Array.Empty<Patient>();
        }

        var kw = criteria.Keyword.Trim();
        var kwLower = kw.ToLower();
        var byIdentity = criteria.Fields.Contains(PatientSearchField.Identity);
        var byPhone = criteria.Fields.Contains(PatientSearchField.Phone);
        var byCode = criteria.Fields.Contains(PatientSearchField.Code);
        var byName = criteria.Fields.Contains(PatientSearchField.Name);

        // Các cờ bool bị EF Core parameter hoá; nhánh false sẽ không khớp dòng nào.
        // ToLower() dịch sang LOWER() của Postgres nên giữ nguyên dấu tiếng Việt.
        return await _db.Patients.AsNoTracking()
            .Where(p => p.DivisionID == divisionId && p.IsActive)
            .Where(p =>
                (byIdentity && p.IdentityNumber.StartsWith(kw)) ||
                (byPhone && p.PhoneNumber.StartsWith(kw)) ||
                (byCode && p.PatientCode.ToLower().StartsWith(kwLower)) ||
                (byName && p.FullName.ToLower().Contains(kwLower)))
            .OrderBy(p => p.FullName)
            .ThenBy(p => p.PatientCode)
            .Take(limit)
            .ToListAsync(ct);
    }
```

Thêm `using System.Collections.Generic;` và `using System.Linq;` vào đầu file.

- [ ] **Step 7: Chạy test để xác nhận pass**

Run: `dotnet test HealthExam.Tests/HealthExam.Tests.csproj --filter "FullyQualifiedName~PatientProfilePersistenceTests"`
Expected: PASS. Nếu không có `HEALTHEXAM_TEST_DB` thì các test Postgres pass-by-skip; tối thiểu phải **build thành công** và toàn bộ test class chạy xanh. Cũng chạy `dotnet build HealthExamServer.sln` để chắc mọi project compile.

- [ ] **Step 8: Commit**

```bash
git add HealthExam.Application/Patients/IPatientRepository.cs HealthExam.Application/Patients/SearchPatients.cs HealthExam.Infrastructure/Persistence/Repositories/PatientRepository.cs HealthExam.Tests/Application/PatientRegistrationTestDoubles.cs HealthExam.Tests/Infrastructure/PatientProfilePersistenceTests.cs
git commit -m "feat(patient): add multi-field active search and lookup by ref id to repository

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>"
```

---

### Task 3: Handler `SearchPatientsHandler`

**Files:**
- Modify: `HealthExam.Application/Patients/SearchPatients.cs` (thêm query, item, handler)
- Test: `HealthExam.Tests/Application/SearchPatientsTests.cs`

**Interfaces:**
- Consumes: `PatientSearchKeywordClassifier.Classify` (Task 1), `IPatientRepository.SearchActiveAsync` + `PatientSearchCriteria` (Task 2).
- Produces:
  - `public sealed record SearchPatientsQuery(string DivisionId, string Type, string Keyword);` — `Type` là chuỗi thô từ query string (`null`/rỗng = auto).
  - `public sealed record PatientSearchItem(Guid PatientRefID, string PatientCode, string FullName, short? BirthYear, short GenderID, string IdentityNumber);`
  - `public interface ISearchPatientsHandler { Task<ApplicationResult<IReadOnlyList<PatientSearchItem>>> HandleAsync(SearchPatientsQuery query, CancellationToken ct = default); }`
  - `public sealed class SearchPatientsHandler : ISearchPatientsHandler` với `public const int MaxResults = 20;`

- [ ] **Step 1: Viết test thất bại**

```csharp
// HealthExam.Tests/Application/SearchPatientsTests.cs
using System;
using System.Linq;
using System.Threading.Tasks;
using HealthExam.Application.Common;
using HealthExam.Application.Patients;
using HealthExam.Domain.Patients;
using Xunit;

namespace HealthExam.Tests.Application;

public class SearchPatientsTests
{
    private static Patient CreatePatient(
        string divisionId,
        string fullName = "Nguyễn Văn An",
        string identityNumber = "079123456789",
        string phoneNumber = "0901234567",
        string patientCode = "BN000125001",
        bool isActive = true,
        short? birthYear = 1990,
        DateOnly? dob = null)
    {
        return new Patient
        {
            PatientRefID = Guid.NewGuid(),
            DivisionID = divisionId,
            FullName = fullName,
            Dob = dob ?? new DateOnly(1990, 5, 20),
            BirthYear = birthYear,
            GenderID = 1,
            IdentityNumber = identityNumber,
            PhoneNumber = phoneNumber,
            PatientCode = patientCode,
            IsActive = isActive
        };
    }

    private static SearchPatientsHandler CreateHandler(params Patient[] patients)
        => new(new FakePatientRepository(patients));

    [Fact]
    public async Task Identity_mode_returns_summary_of_active_patient_in_division()
    {
        var wanted = CreatePatient("D01");
        var sut = CreateHandler(
            wanted,
            CreatePatient("D01", isActive: false),
            CreatePatient("D02"));

        var result = await sut.HandleAsync(new SearchPatientsQuery("D01", "identity", " 079123456789 "));

        Assert.True(result.IsSuccess);
        var item = Assert.Single(result.Value);
        Assert.Equal(wanted.PatientRefID, item.PatientRefID);
        Assert.Equal("BN000125001", item.PatientCode);
        Assert.Equal("Nguyễn Văn An", item.FullName);
        Assert.Equal((short)1990, item.BirthYear);
        Assert.Equal((short)1, item.GenderID);
        Assert.Equal("079123456789", item.IdentityNumber);
    }

    [Fact]
    public async Task Name_mode_matches_contains_case_insensitive()
    {
        var sut = CreateHandler(
            CreatePatient("D01", fullName: "Nguyễn Văn An"),
            CreatePatient("D01", fullName: "Lê Thị Bình", identityNumber: "079000000001"));

        var result = await sut.HandleAsync(new SearchPatientsQuery("D01", "name", "văn an"));

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value);
        Assert.Equal("Nguyễn Văn An", result.Value[0].FullName);
    }

    [Fact]
    public async Task Phone_and_code_modes_use_prefix_match()
    {
        var sut = CreateHandler(
            CreatePatient("D01", phoneNumber: "0901234567", patientCode: "HEX-KSK001"),
            CreatePatient("D01", phoneNumber: "0987654321", patientCode: "BN000125002", identityNumber: "079000000002"));

        var byPhone = await sut.HandleAsync(new SearchPatientsQuery("D01", "phone", "0901"));
        var byCode = await sut.HandleAsync(new SearchPatientsQuery("D01", "code", "hex-"));

        Assert.Single(byPhone.Value);
        Assert.Equal("HEX-KSK001", byPhone.Value[0].PatientCode);
        Assert.Single(byCode.Value);
        Assert.Equal("HEX-KSK001", byCode.Value[0].PatientCode);
    }

    [Fact]
    public async Task Auto_mode_with_letters_searches_name_and_code()
    {
        var sut = CreateHandler(
            CreatePatient("D01", fullName: "Nguyễn Văn An", patientCode: "BN1"),
            CreatePatient("D01", fullName: "Lê Thị Bình", patientCode: "AN-002", identityNumber: "079000000003"));

        var result = await sut.HandleAsync(new SearchPatientsQuery("D01", null, "an"));

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Count);
    }

    [Fact]
    public async Task Auto_mode_with_twelve_digits_searches_identity_only()
    {
        var sut = CreateHandler(
            CreatePatient("D01", identityNumber: "079123456789", phoneNumber: "0791234567"),
            CreatePatient("D01", identityNumber: "000000000000", phoneNumber: "0791234567", patientCode: "079123456789"));

        var result = await sut.HandleAsync(new SearchPatientsQuery("D01", "auto", "079123456789"));

        Assert.Single(result.Value);
        Assert.Equal("079123456789", result.Value[0].IdentityNumber);
    }

    [Fact]
    public async Task BirthYear_falls_back_to_dob_year_when_null()
    {
        var sut = CreateHandler(CreatePatient("D01", birthYear: null, dob: new DateOnly(1985, 3, 2)));

        var result = await sut.HandleAsync(new SearchPatientsQuery("D01", "identity", "079123456789"));

        Assert.Equal((short)1985, result.Value[0].BirthYear);
    }

    [Fact]
    public async Task Results_are_capped_at_max_results()
    {
        var patients = Enumerable.Range(0, SearchPatientsHandler.MaxResults + 5)
            .Select(i => CreatePatient("D01", fullName: $"Nguyễn Văn An {i:00}", identityNumber: $"0790000000{i:00}"))
            .ToArray();
        var sut = CreateHandler(patients);

        var result = await sut.HandleAsync(new SearchPatientsQuery("D01", "name", "nguyễn"));

        Assert.Equal(SearchPatientsHandler.MaxResults, result.Value.Count);
    }

    [Fact]
    public async Task No_match_returns_empty_list_not_failure()
    {
        var sut = CreateHandler(CreatePatient("D01"));

        var result = await sut.HandleAsync(new SearchPatientsQuery("D01", "identity", "999"));

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Blank_keyword_is_bad_request(string keyword)
    {
        var sut = CreateHandler();

        var result = await sut.HandleAsync(new SearchPatientsQuery("D01", "auto", keyword));

        Assert.False(result.IsSuccess);
        Assert.Equal(ApplicationFailureCode.BadRequest, result.Failure.Code);
    }

    [Fact]
    public async Task Unknown_type_is_bad_request()
    {
        var sut = CreateHandler();

        var result = await sut.HandleAsync(new SearchPatientsQuery("D01", "email", "an"));

        Assert.False(result.IsSuccess);
        Assert.Equal(ApplicationFailureCode.BadRequest, result.Failure.Code);
    }
}
```

- [ ] **Step 2: Chạy test để xác nhận thất bại**

Run: `dotnet test HealthExam.Tests/HealthExam.Tests.csproj --filter "FullyQualifiedName~SearchPatientsTests"`
Expected: build FAIL — `SearchPatientsQuery`, `SearchPatientsHandler` chưa tồn tại.

- [ ] **Step 3: Viết handler**

Thay toàn bộ nội dung `HealthExam.Application/Patients/SearchPatients.cs` bằng:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HealthExam.Application.Common;
using HealthExam.Domain.Patients;

namespace HealthExam.Application.Patients;

/// <summary>Tiêu chí tìm hồ sơ active: một keyword áp lên một hoặc nhiều trường (OR).</summary>
public sealed record PatientSearchCriteria(
    string Keyword,
    IReadOnlyList<PatientSearchField> Fields);

/// <summary>
/// Truy vấn tìm người bệnh. <paramref name="Type"/> là chuỗi thô từ query string:
/// identity | code | name | phone | auto; null/rỗng = auto.
/// </summary>
public sealed record SearchPatientsQuery(
    string DivisionId,
    string Type,
    string Keyword);

/// <summary>Dòng tóm tắt để hiển thị danh sách gợi ý; lấy chi tiết bằng GetPatientProfile.</summary>
public sealed record PatientSearchItem(
    Guid PatientRefID,
    string PatientCode,
    string FullName,
    short? BirthYear,
    short GenderID,
    string IdentityNumber);

public interface ISearchPatientsHandler
{
    Task<ApplicationResult<IReadOnlyList<PatientSearchItem>>> HandleAsync(
        SearchPatientsQuery query, CancellationToken ct = default);
}

public sealed class SearchPatientsHandler : ISearchPatientsHandler
{
    public const int MaxResults = 20;

    private static readonly IReadOnlyDictionary<string, PatientSearchField> ExplicitTypes =
        new Dictionary<string, PatientSearchField>(StringComparer.OrdinalIgnoreCase)
        {
            ["identity"] = PatientSearchField.Identity,
            ["code"] = PatientSearchField.Code,
            ["name"] = PatientSearchField.Name,
            ["phone"] = PatientSearchField.Phone
        };

    private readonly IPatientRepository _patientRepository;

    public SearchPatientsHandler(IPatientRepository patientRepository)
    {
        _patientRepository = patientRepository;
    }

    public async Task<ApplicationResult<IReadOnlyList<PatientSearchItem>>> HandleAsync(
        SearchPatientsQuery query, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query.Keyword))
        {
            return ApplicationResult<IReadOnlyList<PatientSearchItem>>.Fail(
                ApplicationFailureCode.BadRequest,
                "Từ khoá tìm kiếm không được để trống.");
        }

        var keyword = query.Keyword.Trim();
        var type = string.IsNullOrWhiteSpace(query.Type) ? "auto" : query.Type.Trim();

        IReadOnlyList<PatientSearchField> fields;
        if (type.Equals("auto", StringComparison.OrdinalIgnoreCase))
        {
            fields = PatientSearchKeywordClassifier.Classify(keyword);
        }
        else if (ExplicitTypes.TryGetValue(type, out var field))
        {
            fields = new[] { field };
        }
        else
        {
            return ApplicationResult<IReadOnlyList<PatientSearchItem>>.Fail(
                ApplicationFailureCode.BadRequest,
                "Loại tìm kiếm không hợp lệ. Chấp nhận: identity, code, name, phone, auto.");
        }

        var patients = await _patientRepository.SearchActiveAsync(
            query.DivisionId, new PatientSearchCriteria(keyword, fields), MaxResults, ct);

        IReadOnlyList<PatientSearchItem> items = patients.Select(ToItem).ToList();
        return ApplicationResult<IReadOnlyList<PatientSearchItem>>.Success(items);
    }

    private static PatientSearchItem ToItem(Patient patient) => new(
        PatientRefID: patient.PatientRefID,
        PatientCode: patient.PatientCode,
        FullName: patient.FullName,
        BirthYear: patient.BirthYear ?? (patient.Dob.HasValue ? (short)patient.Dob.Value.Year : null),
        GenderID: patient.GenderID,
        IdentityNumber: patient.IdentityNumber);
}
```

- [ ] **Step 4: Chạy test để xác nhận pass**

Run: `dotnet test HealthExam.Tests/HealthExam.Tests.csproj --filter "FullyQualifiedName~SearchPatientsTests"`
Expected: PASS (12 test cases). `SearchActivePatientTests` cũ vẫn phải xanh vì handler cũ chưa bị xoá.

- [ ] **Step 5: Commit**

```bash
git add HealthExam.Application/Patients/SearchPatients.cs HealthExam.Tests/Application/SearchPatientsTests.cs
git commit -m "feat(patient): add multi-mode patient search handler

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>"
```

---

### Task 4: Handler `GetPatientProfileHandler`

**Files:**
- Create: `HealthExam.Application/Patients/GetPatientProfile.cs`
- Modify: `HealthExam.Application/Patients/SearchActivePatient.cs` (xoá record `PatientProfileResult` khỏi file này vì đã chuyển sang `GetPatientProfile.cs`; phần handler cũ giữ nguyên tới Task 5)
- Test: `HealthExam.Tests/Application/GetPatientProfileTests.cs`

**Interfaces:**
- Consumes: `IPatientRepository.FindActiveByRefIdAsync` (Task 2).
- Produces:
  - `public sealed record GetPatientProfileQuery(string DivisionId, Guid PatientRefID);`
  - `PatientProfileResult` (record hiện có, 18 trường, giữ nguyên chữ ký) + `public static PatientProfileResult From(Patient patient)`
  - `public interface IGetPatientProfileHandler { Task<ApplicationResult<PatientProfileResult>> HandleAsync(GetPatientProfileQuery query, CancellationToken ct = default); }`
  - `public sealed class GetPatientProfileHandler : IGetPatientProfileHandler`

- [ ] **Step 1: Viết test thất bại**

```csharp
// HealthExam.Tests/Application/GetPatientProfileTests.cs
using System;
using System.Threading.Tasks;
using HealthExam.Application.Common;
using HealthExam.Application.Patients;
using HealthExam.Domain.Patients;
using Xunit;

namespace HealthExam.Tests.Application;

public class GetPatientProfileTests
{
    private static Patient CreatePatient(string divisionId, bool isActive = true) => new()
    {
        PatientRefID = Guid.NewGuid(),
        DivisionID = divisionId,
        HisPatientID = 125001,
        PatientCode = "BN000125001",
        FullName = "Nguyễn Văn An",
        Dob = new DateOnly(1990, 5, 20),
        BirthYear = 1990,
        GenderID = 1,
        IdentityNumber = "079123456789",
        IdentityIssuedDate = new DateOnly(2021, 6, 15),
        PhoneNumber = "0901234567",
        Email = "an@example.com",
        Address = "12 Nguyễn Huệ",
        BloodAboCode = "A",
        BloodRhCode = "+",
        IsActive = isActive
    };

    [Fact]
    public async Task Returns_full_profile_of_active_patient_in_division()
    {
        var patient = CreatePatient("D01");
        var sut = new GetPatientProfileHandler(new FakePatientRepository(patient));

        var result = await sut.HandleAsync(new GetPatientProfileQuery("D01", patient.PatientRefID));

        Assert.True(result.IsSuccess);
        var profile = result.Value;
        Assert.Equal(patient.PatientRefID, profile.PatientRefID);
        Assert.Equal("D01", profile.DivisionID);
        Assert.Equal(125001, profile.HisPatientID);
        Assert.Equal("BN000125001", profile.PatientCode);
        Assert.Equal("Nguyễn Văn An", profile.FullName);
        Assert.Equal(new DateOnly(1990, 5, 20), profile.Dob);
        Assert.Equal((short)1990, profile.BirthYear);
        Assert.Equal((short)1, profile.GenderID);
        Assert.Equal("079123456789", profile.IdentityNumber);
        Assert.Equal(new DateOnly(2021, 6, 15), profile.IdentityIssuedDate);
        Assert.Equal("0901234567", profile.PhoneNumber);
        Assert.Equal("an@example.com", profile.Email);
        Assert.Equal("12 Nguyễn Huệ", profile.Address);
        Assert.Equal("A", profile.BloodAboCode);
        Assert.Equal("+", profile.BloodRhCode);
        Assert.True(profile.IsActive);
    }

    [Fact]
    public async Task Inactive_version_is_not_found()
    {
        var patient = CreatePatient("D01", isActive: false);
        var sut = new GetPatientProfileHandler(new FakePatientRepository(patient));

        var result = await sut.HandleAsync(new GetPatientProfileQuery("D01", patient.PatientRefID));

        Assert.False(result.IsSuccess);
        Assert.Equal(ApplicationFailureCode.NotFound, result.Failure.Code);
    }

    [Fact]
    public async Task Other_division_is_not_found()
    {
        var patient = CreatePatient("D02");
        var sut = new GetPatientProfileHandler(new FakePatientRepository(patient));

        var result = await sut.HandleAsync(new GetPatientProfileQuery("D01", patient.PatientRefID));

        Assert.False(result.IsSuccess);
        Assert.Equal(ApplicationFailureCode.NotFound, result.Failure.Code);
    }

    [Fact]
    public async Task Empty_guid_is_bad_request()
    {
        var sut = new GetPatientProfileHandler(new FakePatientRepository());

        var result = await sut.HandleAsync(new GetPatientProfileQuery("D01", Guid.Empty));

        Assert.False(result.IsSuccess);
        Assert.Equal(ApplicationFailureCode.BadRequest, result.Failure.Code);
    }
}
```

- [ ] **Step 2: Chạy test để xác nhận thất bại**

Run: `dotnet test HealthExam.Tests/HealthExam.Tests.csproj --filter "FullyQualifiedName~GetPatientProfileTests"`
Expected: build FAIL — `GetPatientProfileHandler` chưa tồn tại.

- [ ] **Step 3: Tạo `GetPatientProfile.cs` và chuyển `PatientProfileResult`**

Tạo `HealthExam.Application/Patients/GetPatientProfile.cs`:

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using HealthExam.Application.Common;
using HealthExam.Domain.Patients;

namespace HealthExam.Application.Patients;

public sealed record GetPatientProfileQuery(
    string DivisionId,
    Guid PatientRefID);

/// <summary>Hồ sơ cá nhân đầy đủ để frontend điền vào form đăng ký khám.</summary>
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
    bool IsActive)
{
    public static PatientProfileResult From(Patient patient) => new(
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
        IsActive: patient.IsActive);
}

public interface IGetPatientProfileHandler
{
    Task<ApplicationResult<PatientProfileResult>> HandleAsync(
        GetPatientProfileQuery query, CancellationToken ct = default);
}

public sealed class GetPatientProfileHandler : IGetPatientProfileHandler
{
    private readonly IPatientRepository _patientRepository;

    public GetPatientProfileHandler(IPatientRepository patientRepository)
    {
        _patientRepository = patientRepository;
    }

    public async Task<ApplicationResult<PatientProfileResult>> HandleAsync(
        GetPatientProfileQuery query, CancellationToken ct = default)
    {
        if (query.PatientRefID == Guid.Empty)
        {
            return ApplicationResult<PatientProfileResult>.Fail(
                ApplicationFailureCode.BadRequest,
                "PatientRefID không hợp lệ.");
        }

        var patient = await _patientRepository.FindActiveByRefIdAsync(
            query.DivisionId, query.PatientRefID, ct);

        if (patient == null)
        {
            return ApplicationResult<PatientProfileResult>.Fail(
                ApplicationFailureCode.NotFound,
                "Không tìm thấy hồ sơ người bệnh.");
        }

        return ApplicationResult<PatientProfileResult>.Success(PatientProfileResult.From(patient));
    }
}
```

Sau đó mở `HealthExam.Application/Patients/SearchActivePatient.cs` và **xoá** khối `public sealed record PatientProfileResult(...)` (18 tham số) khỏi file — record giờ nằm ở `GetPatientProfile.cs`, cùng namespace nên `SearchActivePatientHandler` vẫn compile.

- [ ] **Step 4: Chạy test để xác nhận pass**

Run: `dotnet test HealthExam.Tests/HealthExam.Tests.csproj --filter "FullyQualifiedName~GetPatientProfileTests|FullyQualifiedName~SearchActivePatientTests"`
Expected: PASS cả hai class (handler cũ vẫn dùng được `PatientProfileResult` đã chuyển).

- [ ] **Step 5: Commit**

```bash
git add HealthExam.Application/Patients/GetPatientProfile.cs HealthExam.Application/Patients/SearchActivePatient.cs HealthExam.Tests/Application/GetPatientProfileTests.cs
git commit -m "feat(patient): add get patient profile handler

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>"
```

---

### Task 5: Controller, DI và endpoint tests; xoá handler cũ

**Files:**
- Modify: `HealthExam.API/Controllers/PatientController.cs`
- Modify: `HealthExam.API/Extensions/ApplicationServiceExtensions.cs:45`
- Delete: `HealthExam.Application/Patients/SearchActivePatient.cs`
- Delete: `HealthExam.Tests/Application/SearchActivePatientTests.cs`
- Test: `HealthExam.Tests/PatientEndpointTests.cs`

**Interfaces:**
- Consumes: `ISearchPatientsHandler` / `SearchPatientsQuery` / `PatientSearchItem` (Task 3), `IGetPatientProfileHandler` / `GetPatientProfileQuery` / `PatientProfileResult` (Task 4).
- Produces:
  - `GET /v1/patients/search?type=&keyword=` → `ResultData<IReadOnlyList<PatientSearchItem>>`
  - `GET /v1/patients/{patientRefId:guid}` → `ResultData<PatientProfileResult>`

- [ ] **Step 0: Impact analysis (bắt buộc theo CLAUDE.md)**

Chạy `gitnexus_impact({target: "PatientController", direction: "upstream", repo: "health-exam-server"})` và `gitnexus_impact({target: "SearchActivePatientHandler", direction: "upstream", repo: "health-exam-server"})`. Báo cáo cho user. Nếu index degraded: grep đã xác nhận `ISearchActivePatientHandler` chỉ được dùng ở `PatientController`, `ApplicationServiceExtensions` và `SearchActivePatientTests` — tất cả được sửa/xoá trong task này.

- [ ] **Step 1: Sửa endpoint tests hiện có và thêm test mới**

Trong `HealthExam.Tests/PatientEndpointTests.cs`:

1. Sửa `Anonymous_search_is_rejected`: đổi URL thành `"/v1/patients/search?keyword=079123456789"`.
2. Thay toàn bộ `Search_patient_returns_standard_envelope` và `Search_blank_identity_returns_bad_request` bằng các test dưới đây (giữ nguyên `Match_patient_returns_standard_envelope_and_changed_fields`):

```csharp
    private async Task<Guid> SeedPatientAsync(
        string divisionId = "DEV",
        string fullName = "Nguyễn Văn An",
        string identityNumber = "079123456789",
        string phoneNumber = "0901234567",
        string patientCode = "BN000125001",
        bool isActive = true)
    {
        var id = Guid.NewGuid();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HealthExamDbContext>();
        db.Patients.Add(new Patient
        {
            PatientRefID = id,
            DivisionID = divisionId,
            FullName = fullName,
            IdentityNumber = identityNumber,
            Dob = new DateOnly(1990, 1, 1),
            BirthYear = 1990,
            GenderID = 1,
            PhoneNumber = phoneNumber,
            PatientCode = patientCode,
            Address = "Địa chỉ cũ",
            IsActive = isActive
        });
        await db.SaveChangesAsync();
        return id;
    }

    [Fact]
    public async Task Search_auto_returns_summary_items_in_envelope()
    {
        var id = await SeedPatientAsync();

        var client = CreateEmployeeClient("DEV");
        var response = await client.GetAsync("/v1/patients/search?keyword=079123456789");
        var body = JObject.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(0, body["ErrorCode"]!.Value<int>());
        var array = Assert.IsAssignableFrom<JArray>(body["Data"]);
        var item = Assert.Single(array);
        Assert.Equal(id, Guid.Parse(item["PatientRefID"]!.Value<string>()!));
        Assert.Equal("BN000125001", item["PatientCode"]!.Value<string>());
        Assert.Equal("Nguyễn Văn An", item["FullName"]!.Value<string>());
        Assert.Equal(1990, item["BirthYear"]!.Value<int>());
        Assert.Equal(1, item["GenderID"]!.Value<int>());
        Assert.Equal("079123456789", item["IdentityNumber"]!.Value<string>());
        // Chỉ trả tóm tắt: không lộ các trường chi tiết
        Assert.Null(item["Address"]);
        Assert.Null(item["PhoneNumber"]);
    }

    [Theory]
    [InlineData("name", "văn an")]
    [InlineData("phone", "0901")]
    [InlineData("code", "bn0001")]
    [InlineData("identity", "0791")]
    public async Task Search_explicit_types_find_seeded_patient(string type, string keyword)
    {
        var id = await SeedPatientAsync();

        var client = CreateEmployeeClient("DEV");
        var response = await client.GetAsync($"/v1/patients/search?type={type}&keyword={Uri.EscapeDataString(keyword)}");
        var body = JObject.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var array = Assert.IsAssignableFrom<JArray>(body["Data"]);
        Assert.Contains(array, x => Guid.Parse(x["PatientRefID"]!.Value<string>()!) == id);
    }

    [Fact]
    public async Task Search_blank_keyword_returns_bad_request()
    {
        var client = CreateEmployeeClient("DEV");
        var response = await client.GetAsync("/v1/patients/search?keyword=");
        var body = JObject.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(ErrorCodes.BadRequest, body["ErrorCode"]!.Value<int>());
    }

    [Fact]
    public async Task Search_unknown_type_returns_bad_request()
    {
        var client = CreateEmployeeClient("DEV");
        var response = await client.GetAsync("/v1/patients/search?type=email&keyword=an");
        var body = JObject.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(ErrorCodes.BadRequest, body["ErrorCode"]!.Value<int>());
    }

    [Fact]
    public async Task Get_patient_returns_full_profile()
    {
        var id = await SeedPatientAsync();

        var client = CreateEmployeeClient("DEV");
        var response = await client.GetAsync($"/v1/patients/{id}");
        var body = JObject.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(0, body["ErrorCode"]!.Value<int>());
        var data = body["Data"]!;
        Assert.Equal(id, Guid.Parse(data["PatientRefID"]!.Value<string>()!));
        Assert.Equal("Nguyễn Văn An", data["FullName"]!.Value<string>());
        Assert.Equal("0901234567", data["PhoneNumber"]!.Value<string>());
        Assert.Equal("Địa chỉ cũ", data["Address"]!.Value<string>());
        Assert.True(data["IsActive"]!.Value<bool>());
    }

    [Fact]
    public async Task Get_patient_from_other_division_is_not_found()
    {
        var id = await SeedPatientAsync(divisionId: "OTHER");

        var client = CreateEmployeeClient("DEV");
        var response = await client.GetAsync($"/v1/patients/{id}");
        var body = JObject.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(ErrorCodes.NotFound, body["ErrorCode"]!.Value<int>());
    }

    [Fact]
    public async Task Anonymous_get_patient_is_rejected()
    {
        var client = CreateAnonymousClient();
        var response = await client.GetAsync($"/v1/patients/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
```

Thêm `using System.Linq;` nếu chưa có (cho `Assert.Contains` với predicate trên `JArray`).

- [ ] **Step 2: Chạy test để xác nhận thất bại**

Run: `dotnet test HealthExam.Tests/HealthExam.Tests.csproj --filter "FullyQualifiedName~PatientEndpointTests"`
Expected: FAIL — `Search_auto_returns_summary_items_in_envelope` nhận 400 (thiếu `identityNumber`), `Get_patient_*` nhận 404 route không tồn tại.

- [ ] **Step 3: Viết lại `PatientController`**

Thay phần `using`, constructor và action `Search` trong `HealthExam.API/Controllers/PatientController.cs`; giữ nguyên action `Match` và record `MatchPatientProfileRequest`:

```csharp
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using HealthExam.API.Contracts;
using HealthExam.Application.Patients;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace HealthExam.API.Controllers;

[Route("v1/patients")]
public sealed class PatientController : HealthExamControllerBase
{
    private readonly ISearchPatientsHandler _searchHandler;
    private readonly IGetPatientProfileHandler _getHandler;
    private readonly IMatchPatientProfileHandler _matchHandler;

    public PatientController(
        ISearchPatientsHandler searchHandler,
        IGetPatientProfileHandler getHandler,
        IMatchPatientProfileHandler matchHandler)
    {
        _searchHandler = searchHandler;
        _getHandler = getHandler;
        _matchHandler = matchHandler;
    }

    /// <summary>
    /// Tìm hồ sơ active trong đơn vị theo CCCD, mã BN, tên, SĐT hoặc tự nhận diện.
    /// Trả tối đa 20 dòng tóm tắt; lấy chi tiết bằng GET /v1/patients/{patientRefId}.
    /// </summary>
    [HttpGet("search")]
    [SwaggerOperation(
        Summary = "Tìm người bệnh",
        Description = "type = identity | code | name | phone | auto (mặc định auto). Chỉ trả hồ sơ active trong X-Division-Id, tối đa 20 dòng tóm tắt.")]
    public async Task<ActionResult<ResultData<IReadOnlyList<PatientSearchItem>>>> Search(
        [FromQuery] string keyword, [FromQuery] string type = null, CancellationToken ct = default)
    {
        var result = await _searchHandler.HandleAsync(
            new SearchPatientsQuery(HealthExamContext.DivisionId, type, keyword), ct);
        return ToActionResult(result);
    }

    /// <summary>Lấy hồ sơ cá nhân đầy đủ để điền vào form đăng ký khám.</summary>
    [HttpGet("{patientRefId:guid}")]
    [SwaggerOperation(
        Summary = "Lấy hồ sơ người bệnh",
        Description = "Trả hồ sơ active theo PatientRefID trong X-Division-Id; 404 nếu không active hoặc khác đơn vị.")]
    public async Task<ActionResult<ResultData<PatientProfileResult>>> Get(
        Guid patientRefId, CancellationToken ct = default)
    {
        var result = await _getHandler.HandleAsync(
            new GetPatientProfileQuery(HealthExamContext.DivisionId, patientRefId), ct);
        return ToActionResult(result);
    }

    // ... action Match và record MatchPatientProfileRequest giữ nguyên như hiện tại ...
}
```

- [ ] **Step 4: Cập nhật DI**

Trong `HealthExam.API/Extensions/ApplicationServiceExtensions.cs` thay dòng 45:

```csharp
        services.AddScoped<ISearchActivePatientHandler, SearchActivePatientHandler>();
```

bằng:

```csharp
        services.AddScoped<ISearchPatientsHandler, SearchPatientsHandler>();
        services.AddScoped<IGetPatientProfileHandler, GetPatientProfileHandler>();
```

- [ ] **Step 5: Xoá handler cũ và test cũ**

```bash
git rm HealthExam.Application/Patients/SearchActivePatient.cs HealthExam.Tests/Application/SearchActivePatientTests.cs
```

Sau đó `grep -rn "SearchActivePatient" --include=*.cs HealthExam.API HealthExam.Application HealthExam.Infrastructure HealthExam.Tests` phải **không còn kết quả** (trừ thư mục `obj/`, `bin/`).

- [ ] **Step 6: Chạy test để xác nhận pass**

Run: `dotnet test HealthExam.Tests/HealthExam.Tests.csproj --filter "FullyQualifiedName~PatientEndpointTests|FullyQualifiedName~SearchPatientsTests|FullyQualifiedName~GetPatientProfileTests|FullyQualifiedName~SwaggerBusinessDocumentationTests"`
Expected: PASS toàn bộ.

Sau đó chạy full suite để chắc không vỡ gì khác: `dotnet test HealthExamServer.sln` (trên Windows nếu cần TZ: `$env:TZ='Asia/Ho_Chi_Minh'; dotnet test HealthExamServer.sln`). Expected: PASS.

- [ ] **Step 7: Kiểm tra phạm vi thay đổi và commit**

Chạy `gitnexus_detect_changes({repo: "health-exam-server"})` và xác nhận chỉ các symbol thuộc Patient search/get bị ảnh hưởng.

```bash
git add HealthExam.API/Controllers/PatientController.cs HealthExam.API/Extensions/ApplicationServiceExtensions.cs HealthExam.Tests/PatientEndpointTests.cs
git commit -m "feat(patient): expose multi-mode search and get profile endpoints

Replace GET /v1/patients/search?identityNumber= with ?type=&keyword=
returning summary rows, add GET /v1/patients/{patientRefId}, and drop
the identity-only SearchActivePatientHandler.

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>"
```

---

### Task 6: Cập nhật tài liệu API cho frontend

**Files:**
- Modify: `docs/api/patient-profile-search-and-reuse-api.md` (mục 0, 1, 3; thêm mục 3b; mục 8)

**Interfaces:**
- Consumes: contract từ Task 5.
- Produces: tài liệu frontend đã cập nhật.

- [ ] **Step 1: Sửa mục 0 — bảng API và luồng**

Thay bảng trong mục 0 bằng:

```markdown
| API | Khi nào gọi? | Có ghi dữ liệu không? |
| --- | --- | --- |
| `GET /v1/patients/search` | Khi người dùng gõ từ khoá (CCCD, mã BN, tên, SĐT) ở ô tìm kiếm; trả danh sách tóm tắt | Không |
| `GET /v1/patients/{patientRefId}` | Khi người dùng chọn một dòng trong danh sách tìm kiếm để điền vào form đăng ký | Không |
| `POST /v1/patients/match` | Khi bấm **Lưu** trên form (đăng ký mới hoặc sửa); backend so 13 trường với hồ sơ active cùng CCCD | Không |
| `POST /v1/exam-records` | Sau match, khi bấm **Đăng ký** để tạo một hồ sơ khám mới | Có |
| `PUT /v1/exam-records/{recordId}` | Sau match, khi bấm **Lưu** trên một hồ sơ khám đã tồn tại | Có |
```

Thay khối `text` luồng "Đăng ký mới" bằng:

```text
Gõ từ khoá -> SEARCH (danh sách tóm tắt)
  -> chọn một dòng -> GET /v1/patients/{patientRefId} -> điền form
  -> người dùng sửa/xác nhận
  -> bấm Lưu -> MATCH
       -> Matched=true  : gọi POST/PUT với PatientRefID
       -> Matched=false : cảnh báo ChangedFields; nếu người dùng vẫn lưu
                          thì gọi POST/PUT với PatientRefID + SetAsActiveProfile
       -> PatientRefID=null : người bệnh mới, POST không gửi PatientRefID
```

- [ ] **Step 2: Sửa mục 1 — nguyên tắc**

Thay dòng `- Chỉ tìm kiếm theo CCCD/CMND (`IdentityNumber`).` bằng:

```markdown
- Tìm kiếm theo CCCD/CMND, mã người bệnh, họ tên, số điện thoại hoặc để backend tự
  nhận diện; kết quả là danh sách tóm tắt, tối đa 20 dòng.
- Hồ sơ đầy đủ chỉ lấy qua `GET /v1/patients/{patientRefId}`.
```

- [ ] **Step 3: Viết lại mục 3 và thêm mục 3b**

Thay toàn bộ mục `## 3. Tìm hồ sơ active theo CCCD` (từ tiêu đề đến ngay trước `## 4.`) bằng:

````markdown
## 3. Tìm hồ sơ active

```http
GET /v1/patients/search?type=auto&keyword=079123456789
```

### Query parameters

| Trường | Kiểu | Bắt buộc | Ý nghĩa |
| --- | --- | --- | --- |
| `keyword` | `string` | Có | Từ khoá; backend tự trim hai đầu |
| `type` | `string` | Không | `identity` \| `code` \| `name` \| `phone` \| `auto` (mặc định `auto`) |

### Cách khớp từng loại

| `type` | Trường so sánh | Quy tắc |
| --- | --- | --- |
| `identity` | `IdentityNumber` | Bắt đầu bằng `keyword` |
| `phone` | `PhoneNumber` | Bắt đầu bằng `keyword` |
| `code` | `PatientCode` | Bắt đầu bằng `keyword`, không phân biệt hoa/thường |
| `name` | `FullName` | Chứa `keyword`, không phân biệt hoa/thường, **có phân biệt dấu** |
| `auto` | tự chọn | Xem bên dưới |

Quy tắc `auto`:

- Toàn số, 9 hoặc 12 ký tự → tìm như `identity`.
- Toàn số, 10 ký tự bắt đầu bằng `0` → tìm như `phone`.
- Toàn số, độ dài khác (đang gõ dở) → tìm đồng thời `identity`, `phone`, `code` và gộp.
- Có chữ cái → tìm đồng thời `name` và `code` và gộp (mã BN không có format cố định).

Backend chỉ trả hồ sơ active trong `DivisionID` hiện tại, tối đa **20** dòng, sắp xếp
theo `FullName`. Nếu danh sách đầy 20 dòng, frontend nên gợi ý gõ thêm để thu hẹp.

### Có kết quả

```json
{
  "ErrorCode": 0,
  "Message": "",
  "Data": [
    {
      "PatientRefID": "a768f42f-d8fa-4b06-98e3-25385b4b2521",
      "PatientCode": "BN000125001",
      "FullName": "Nguyễn Văn An",
      "BirthYear": 1990,
      "GenderID": 1,
      "IdentityNumber": "079123456789"
    }
  ],
  "TraceID": "c37c1f3f2f6a4ea8"
}
```

`BirthYear` lấy từ `BirthYear` của hồ sơ, nếu trống thì lấy năm của `Dob`; có thể `null`
nếu cả hai đều trống.

### Không tìm thấy

Đây không phải lỗi. API trả `Data: []`; frontend tiếp tục luồng tạo người bệnh mới và
không gửi `PatientRefID`.

### Lỗi đầu vào

HTTP `400`, mã nghiệp vụ `4001` khi `keyword` trống hoặc `type` không thuộc 5 giá trị
trên:

```json
{
  "ErrorCode": 4001,
  "Message": "Từ khoá tìm kiếm không được để trống.",
  "Data": null,
  "TraceID": "c37c1f3f2f6a4ea8"
}
```

## 3b. Lấy hồ sơ đầy đủ để điền form

```http
GET /v1/patients/{patientRefId}
```

Gọi khi người dùng chọn một dòng trong kết quả search. Backend chỉ trả hồ sơ **active**
trong `DivisionID` hiện tại.

```json
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
    "IsActive": true
  },
  "TraceID": "c37c1f3f2f6a4ea8"
}
```

Frontend lưu response này làm `sourceProfile` (xem mục 4) và copy sang `formValues`.

Không tìm thấy (đã inactive vì có phiên bản mới hơn, hoặc khác đơn vị): HTTP `404`, mã
`4040`, `Message: "Không tìm thấy hồ sơ người bệnh."`. Frontend nên search lại để lấy
phiên bản active hiện tại.
````

- [ ] **Step 4: Sửa các chỗ còn tham chiếu "search theo CCCD"**

- Mục 4, đoạn "Dùng thông tin đang lưu: điền lại dữ liệu từ kết quả search." → đổi thành "điền lại dữ liệu từ `GET /v1/patients/{patientRefId}`".
- Mục 4, `RegistrationState`: thêm chú thích `selectedPatientRefId: lấy từ dòng được chọn trong search, từ response của GET, hoặc từ match`.
- Mục 5, đoạn "Frontend điền dữ liệu từ kết quả search vào form" → "Frontend điền dữ liệu từ `GET /v1/patients/{patientRefId}` vào form".
- Mục 8 (luồng frontend đề xuất): đọc và cập nhật mọi bước nói "search theo CCCD" thành "search → chọn → GET" theo luồng ở mục 0.
- Mục 9 (bảng mã lỗi): thêm dòng `4040 | GET /v1/patients/{patientRefId} không thấy hồ sơ active | Search lại`.

- [ ] **Step 5: Đọc lại toàn bộ file**

Chạy `grep -n "identityNumber=" docs/api/patient-profile-search-and-reuse-api.md` — phải **không còn** kết quả. Đọc lướt mục 0 → 3b để chắc luồng nhất quán với contract Task 5.

- [ ] **Step 6: Commit**

```bash
git add docs/api/patient-profile-search-and-reuse-api.md
git commit -m "docs: describe multi-mode patient search and get profile flow

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>"
```

---

## Self-Review

**Spec coverage**
- 5 chế độ search + quy tắc auto → Task 1 (classifier), Task 3 (parse `type`), Task 2 (predicate DB). ✔
- Kết quả tóm tắt (họ tên, năm sinh, giới tính, mã BN, CCCD) + `PatientRefID` để gọi tiếp → `PatientSearchItem` Task 3. ✔
- Giới hạn 20, chỉ active + đúng division → `MaxResults` Task 3, filter Task 2. ✔
- API get profile để fill form → Task 4 + Task 5. ✔
- Match / Create / Update không đổi; flow "Lưu → match → warn → POST/PUT" chỉ cần cập nhật tài liệu → Task 6. ✔
- Lỗi 400/404 → Task 3, 4, 5. ✔

**Placeholder scan**: không có TBD/TODO; mọi bước code đều có code block.

**Type consistency**
- `PatientSearchField` (Task 1) dùng ở `PatientSearchCriteria` (Task 2), `SearchPatientsHandler` (Task 3), `FakePatientRepository` (Task 2). ✔
- `SearchActiveAsync(string, PatientSearchCriteria, int, CancellationToken)` — chữ ký giống nhau ở interface, EF impl, fake, và lời gọi trong handler. ✔
- `FindActiveByRefIdAsync(string, Guid, CancellationToken)` — giống nhau ở interface, EF impl, fake, và `GetPatientProfileHandler`. ✔
- `PatientProfileResult` chuyển file ở Task 4, `PatientController` Task 5 dùng cùng tên. ✔
- Controller tham số `keyword` bắt buộc, `type` optional — khớp `SearchPatientsQuery(DivisionId, Type, Keyword)`. ✔
