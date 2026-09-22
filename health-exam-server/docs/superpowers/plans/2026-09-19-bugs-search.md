# Search Bugs Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Sửa PROJ2365–2367: chuỗi con ở đúng trường và ranh giới ngày Việt Nam.

**Architecture:** Đổi criteria auto ở application và predicate ở repository; giữ scope/limit. Lọc timestamp bằng biên UTC tính từ ngày VN, không sửa dữ liệu thời gian đã lưu.

**Tech Stack:** .NET 8, LINQ/EF Core/PostgreSQL, xUnit.

**Spec:** `docs/superpowers/specs/2026-09-19-bugs-report-design.md`, section 4.

## Global Constraints

- “Không chuyển timezone lần hai cho một giá trị vốn đã là ngày thuần túy.”
- “Giữ giới hạn kết quả, lọc profile active và phạm vi đơn vị hiện có.”
- Không thêm search engine/index mới trước khi có đo đạc; không đổi tìm có dấu thành tìm bỏ dấu ngoài yêu cầu.
- Áp dụng baseline/impact/test/commit từ kế hoạch tổng; chưa có bằng chứng frontend thì chưa đóng ticket UI.

## Task 1: Auto OR bốn trường và contains — PROJ2366

**Files:** Modify `HealthExam.Application/Patients/SearchPatients.cs`, `HealthExam.Infrastructure/Persistence/Repositories/PatientRepository.cs`, `HealthExam.Tests/Application/PatientRegistrationTestDoubles.cs`; test `HealthExam.Tests/Application/SearchPatientsTests.cs`, `HealthExam.Tests/Infrastructure/PatientRepositoryProfileTests.cs`. Inspect `PatientSearchKeywordClassifier.cs` bằng impact trước khi xóa; không xóa nếu còn caller khác.

**Interfaces:** Giữ `SearchPatientsQuery`, `PatientSearchCriteria`, `SearchActiveAsync`; auto luôn có Identity/Phone/Code/Name. Các chế độ explicit giữ một trường.

- [ ] Thêm vào `SearchPatientsTests`:

```csharp
[Theory]
[InlineData("code", "PHASE1")]
[InlineData("phone", "985")]
[InlineData("identity", "201")]
[InlineData("auto", "0042")]
[InlineData("auto", "985")]
[InlineData("auto", "201")]
public async Task Search_matches_middle_of_selected_fields(string type, string keyword)
{
    var patient = CreatePatient("D01", patientCode: "HEX-PHASE1-DEFAULT-0042",
        phoneNumber: "0985230227", identityNumber: "079201006605");
    var result = await CreateHandler(patient).HandleAsync(
        new SearchPatientsQuery("D01", type, keyword));
    Assert.True(result.IsSuccess);
    Assert.Equal(patient.PatientRefID, Assert.Single(result.Value).PatientRefID);
}
```

- [ ] Run red `dotnet test HealthExam.Tests/HealthExam.Tests.csproj --filter Search_matches_middle_of_selected_fields`.
- [ ] Sau impact `SearchPatientsHandler`/`SearchActiveAsync`, thay auto classification và ba prefix predicates:

```csharp
fields = new[] { PatientSearchField.Identity, PatientSearchField.Phone,
    PatientSearchField.Code, PatientSearchField.Name };
// Repository: giữ cùng cờ byIdentity/byPhone/byCode/byName và scope query.
query = query.Where(p =>
    (byIdentity && p.IdentityNumber.Contains(kw)) ||
    (byPhone && p.PhoneNumber.Contains(kw)) ||
    (byCode && p.PatientCode.ToLower().Contains(kwLower)) ||
    (byName && p.FullName.ToLower().Contains(kwLower)));
```

- [ ] Đồng bộ fake repository từ StartsWith sang Contains. Đổi các test cũ cố định prefix/heuristic auto thành hợp đồng mới; không sửa classifier chỉ để tests pass nếu handler không còn dùng nó.
- [ ] Thêm test repository thật, không chỉ fake: seed cùng mẫu, gọi `PatientRepository.SearchActiveAsync` với `new PatientSearchCriteria("985", new[] { PatientSearchField.Phone })`, assert đúng patient ID. Kiểm tra cùng mẫu ở đơn vị khác/inactive không xuất hiện, limit 20, dấu `%`/`_` tìm literal, explicit code không lấy kết quả chỉ khớp phone.
- [ ] Run `--filter 'FullyQualifiedName~SearchPatients|FullyQualifiedName~PatientRepositoryProfile|FullyQualifiedName~PatientEndpoint'` và PostgreSQL test tương ứng; graph check và commit `fix: search patient substrings across all auto fields`.

## Task 2: Ranh giới ngày và hợp đồng UI — PROJ2365/2367

**Files:** Modify `HealthExam.Infrastructure/Persistence/Repositories/ExamRecordRepository.cs`; test `HealthExam.Tests/ExamRecordListFilterTests.cs`, `ExamRecordListFilterPostgresTests.cs`; inspect `HealthExam.API/Controllers/ExamRecordController.cs`, `HealthExam.Application/ExamRecords/ExamRecordModels.cs`, `HealthExam.API/Contracts/` mapping ngày. Create `docs/api/exam-record-date-filter-contract.md`.

**Interfaces:** Giữ `ExamRecordFilter.From/To` dạng DateOnly. Baseline backend lọc ngày tạo `CreatedDate`; không lén đổi sang `Session.ExamDate`. Ngày hiển thị phải được đối chiếu ở frontend như gate nghiệm thu.

- [ ] Ghi bảng bằng chứng từ GET list: `createdDate`, `examDate`, from/to của hai màn hình; xác minh cột “Ngày KSK” bind field nào. Nếu sản phẩm thực sự yêu cầu ngày đợt thay ngày tạo, dừng phần thay contract và xin quyết định cụ thể; vẫn có thể chạy test biên ngày tạo dưới đây. Không rewrite timestamp DB.
- [ ] Thêm test vào `ExamRecordListFilterTests`:

```csharp
[Fact]
public async Task Vietnam_day_includes_early_morning_and_excludes_next_midnight()
{
    using var db = new InMemoryTestDb();
    var session = db.SeedSession();
    db.SeedRecord(session.SessionID, recordCode: "IN",
        createdDate: new DateTime(2026, 9, 18, 18, 0, 0, DateTimeKind.Utc));
    db.SeedRecord(session.SessionID, recordCode: "OUT",
        createdDate: new DateTime(2026, 9, 19, 17, 0, 0, DateTimeKind.Utc));
    var result = await db.Records.ListAsync(new ExamRecordListFilter
    { From = new DateOnly(2026, 9, 19), To = new DateOnly(2026, 9, 19) }, 1, 20);
    Assert.Equal("IN", Assert.Single(result.Items).RecordCode);
}
```

- [ ] Run red `dotnet test HealthExam.Tests/HealthExam.Tests.csproj --filter Vietnam_day_includes_early_morning_and_excludes_next_midnight`.
- [ ] Sau impact `ListAsync`, đổi hai mốc trong repository; dùng offset VN tường minh, không timezone local của server:

```csharp
var from = new DateTimeOffset(filter.From.Value.ToDateTime(TimeOnly.MinValue),
    TimeSpan.FromHours(7)).UtcDateTime;
// To = inclusive local date; UTC upper bound is exclusive.
var toExclusive = new DateTimeOffset(filter.To.Value.AddDays(1)
    .ToDateTime(TimeOnly.MinValue), TimeSpan.FromHours(7)).UtcDateTime;
```

- [ ] Với `DateOnly.MaxValue`, bỏ upper predicate (không có ngày kế tiếp) thay vì gọi AddDays và overflow. Kiểm tra from-only/to-only, từ > đến, biên 17:00 UTC hôm trước và biên ngày sau. Cập nhật test cũ ghim UTC midnight thành mốc VN, giữ ý nghĩa “lọc CreatedDate không phải RegisteredAt”.
- [ ] Run test filter trên cả hai TZ và provider PostgreSQL; trường DateOnly không chuyển timezone. Ghi ví dụ `19/09/2026 → [18/09 17:00Z, 19/09 17:00Z)` trong API docs.
- [ ] Graph check, diff và commit `fix: filter exam records by Vietnam calendar boundaries`. Chỉ đóng 2365/2367 khi hai màn hình hiển thị/lọc cùng ngày sau reload.
