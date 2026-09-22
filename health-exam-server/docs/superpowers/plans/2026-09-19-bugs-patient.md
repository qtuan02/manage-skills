# Patient Registration Bugs Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Sửa PROJ2363–2364 mà không làm mất phiên bản lịch sử profile NB.

**Architecture:** Giữ `PatientRegistrationWriter` làm điểm ghi chung và `PatientProfileComparer` làm điểm xác định thay đổi. Tái sử dụng mã tỉnh/phường đã thêm trong working tree, sửa round-trip và xác minh migration cùng task.

**Tech Stack:** .NET 8, EF Core/PostgreSQL, xUnit.

**Spec:** `docs/superpowers/specs/2026-09-19-bugs-report-design.md`, sections 1, 3, 7.

## Global Constraints

- “Không backfill giả A/B hoặc thời gian ký cho dữ liệu cũ.”
- “Bảo toàn cơ chế phiên bản profile và snapshot hồ sơ hiện có; không sửa ngược thông tin lịch sử một cách ngầm định.”
- Áp dụng baseline, impact, commit và môi trường test từ `2026-09-19-bugs-report.md`.
- Không tự commit phần code/migration đang có của người dùng; xác nhận ownership trước tích hợp.

## Task 1: Địa chỉ tham gia đối chiếu và giữ đúng ID danh mục

**Files:**
- Modify `HealthExam.Application/Patients/PatientProfileComparer.cs`, `PatientRegistrationWriter.cs`, `PatientModels.cs`.
- Inspect/modify nếu sai mapping: `HealthExam.Application/ExamRecords/CreateExamRecord.cs`, `UpdateExamRecord.cs`.
- Test `HealthExam.Tests/Application/PatientRegistrationWriterTests.cs`, `HealthExam.Tests/ExamRecordRegistrationFieldsTests.cs`.

**Interfaces:** Giữ `PatientProfileComparer.Compare(Patient, PatientProfileValues)` trả `IReadOnlyList<string>`; trả tên `ProvinceCode`, `WardCode` trong `PatientProfileChangedPayload.ChangedFields`. Giữ `UpsertAsync(PatientWriteRequest, CancellationToken)` và quyết định `SetAsActiveProfile` hiện có.

- [ ] Thêm test này vào `PatientRegistrationWriterTests`, dùng helper `ActivePatient` và `Request` có sẵn.

```csharp
[Fact]
public void Address_codes_change_without_false_catalog_changes()
{
    var p = ActivePatient();
    p.ProvinceCode = "79";
    p.WardCode = "26734";
    p.EthnicityOptionID = Guid.NewGuid();
    p.IdentityIssuerOptionID = Guid.NewGuid();
    var request = Request(p.PatientRefID) with
    {
        ProvinceCode = "01", WardCode = "00001",
        EthnicityOptionID = p.EthnicityOptionID,
        IdentityIssuerOptionID = p.IdentityIssuerOptionID
    };
    Assert.Equal(new[] { "ProvinceCode", "WardCode" },
        PatientProfileComparer.Compare(p, PatientProfileValues.From(request)));
}
```

- [ ] Run red: `dotnet test HealthExam.Tests/HealthExam.Tests.csproj --filter Address_codes_change_without_false_catalog_changes`. Expected hiện tại thiếu hai trường trong kết quả, không phải lỗi build.
- [ ] Sau impact `Compare` và `UpsertAsync`, thêm so sánh mã trong comparer, giữ sort cuối hàm:

```csharp
if (!IsStringEqual(source.ProvinceCode, input.ProvinceCode))
    changes.Add(nameof(Patient.ProvinceCode));
if (!IsStringEqual(source.WardCode, input.WardCode))
    changes.Add(nameof(Patient.WardCode));
```

- [ ] Bỏ ghi âm thầm tỉnh/phường trong nhánh `changedFields.Count == 0`; nhánh này chỉ gắn facts khi các giá trị không đổi. Nhánh tạo/version tiếp tục copy mã. Không thêm xử lý loại bỏ dân tộc/nơi cấp khỏi comparer để che lỗi mapping.
- [ ] Mở rộng cùng test fixture: request giữ nguyên trả empty; chỉ đổi một mã trả đúng một trường; chuỗi có space ngoài không đổi; Upsert không quyết định trả `PatientProfileChanged`; quyết định true tạo version active mới và giữ nguyên source; false tạo version không active. Dùng assertion trực tiếp:

```csharp
var p = ActivePatient();
p.ProvinceCode = "79";
p.WardCode = "26734";
var result = await CreateWriter(p).UpsertAsync(Request(p.PatientRefID) with
    { ProvinceCode = "01", WardCode = "00001" });
Assert.False(result.IsSuccess);
Assert.Equal(ApplicationFailureCode.PatientProfileChanged, result.Failure.Code);
Assert.Equal("79", p.ProvinceCode);
Assert.Equal("26734", p.WardCode);
```

- [ ] Chạy `--filter 'FullyQualifiedName~PatientRegistrationWriterTests|FullyQualifiedName~ExamRecordRegistrationFieldsTests'`; kiểm tra request → resolver danh mục → writer giữ đúng GUID dân tộc/nơi cấp. Nếu API round-trip sai, sửa tại mapping nguồn, không đổi comparer thành so sánh tên.
- [ ] Graph check, review diff chỉ các file task, commit `fix: compare patient address codes without false profile changes` sau khi được phép tích hợp baseline người dùng.

## Task 2: Round-trip profile và persistence địa chỉ

**Files:**
- Modify `HealthExam.Application/Patients/GetPatientProfile.cs`, `PatientModels.cs`, `HealthExam.Domain/Patients/Patient.cs`, `HealthExam.Infrastructure/Persistence/HealthExamDbContext.cs` chỉ phần còn thiếu.
- Review migration hiện có `HealthExam.Infrastructure/Persistence/Migrations/20260919031041_AddPatientAddressMasterCodes.cs` và `HealthExamDbContextModelSnapshot.cs` cùng thư mục; không tạo migration địa chỉ trùng.
- Test `HealthExam.Tests/Application/GetPatientProfileTests.cs`, `HealthExam.Tests/Infrastructure/PatientRepositoryProfileTests.cs`, `PatientProfilePersistenceTests.cs`.
- Create docs `docs/api/patient-profile-address-contract.md`.

**Interfaces:** `Patient.ProvinceCode/WardCode` và profile response trả cùng mã canonical; GUID dân tộc/nơi cấp giữ nguyên qua GET → POST. Giữ các field cũ, chỉ bổ sung dữ liệu còn thiếu.

- [ ] Thêm kiểm tra repository trong `PatientRepositoryProfileTests` dùng helper hiện có:

```csharp
[Fact]
public async Task Profile_preserves_address_codes_after_reload()
{
    using var db = new InMemoryTestDb();
    var p = SeedPatient(db);
    var tracked = await db.Db.Patients.AsTracking()
        .SingleAsync(x => x.PatientRefID == p.PatientRefID);
    tracked.ProvinceCode = "79";
    tracked.WardCode = "26734";
    await db.Db.SaveChangesAsync();
    db.Db.ChangeTracker.Clear();
    var result = await new PatientRepository(db.Db)
        .FindActiveProfileAsync(db.Ctx.DivisionId, p.PatientRefID);
    Assert.Equal("79", result.Patient.ProvinceCode);
    Assert.Equal("26734", result.Patient.WardCode);
}
```

- [ ] Chạy test trước sửa; có thể đã xanh nhờ baseline của người dùng. Không làm lại phần đã đạt. Trong `GetPatientProfileTests`, kiểm tra DTO serialize chứa hai mã trên và ID dân tộc/nơi cấp trước khi tạo hồ sơ tiếp theo; kiểm tra thay địa chỉ không làm đổi hồ sơ cũ.
- [ ] Giữ mapping đơn giản `ProvinceCode: patient.ProvinceCode, WardCode: patient.WardCode` ở projection tương ứng; xác minh code → tên qua catalog hiện hành, không parse mã từ chuỗi địa chỉ.
- [ ] Kiểm tra mapping cột, null/default, migration Up/Down và model snapshot khớp; dùng DB test áp dụng từ schema trước migration rồi load cả bệnh nhân cũ/mới. Dữ liệu cũ thiếu mã trả trống, không đoán tỉnh/phường từ text.
- [ ] Run `dotnet test HealthExam.Tests/HealthExam.Tests.csproj --filter 'FullyQualifiedName~GetPatientProfile|FullyQualifiedName~PatientRepositoryProfile|FullyQualifiedName~PatientProfilePersistence|FullyQualifiedName~ExamRecordRegistrationFields'`. PostgreSQL phải được cấu hình test thực sự; ghi rõ nếu chưa chạy.
- [ ] Viết ví dụ GET/POST với cùng `provinceCode`, `wardCode`, `ethnicityOptionID`, `identityIssuerOptionID`; frontend dùng đúng loại ID. Graph check rồi commit `fix: preserve patient address profile round trips` với đường dẫn cụ thể đã sửa.
