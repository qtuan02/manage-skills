# Ký số KSK độc lập — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Bác sĩ bấm "Ký số" từng mục khám theo thứ tự bất kỳ (chỉ ghi snapshot cục bộ), tới bước kết luận thì health-exam render PDF một lần rồi gọi thẳng sign-server ký toàn bộ marker `##{Sn}##`.

**Architecture:** Bỏ máy quy trình ký (`SWT`) của his-server khỏi đường ký. Cấu hình bước ký chuyển vào bảng `HEX_SignStepMap` của DB KSK. Chứng thư số lấy thẳng từ ssm-server, ký qua `sign-server POST api/SIGN/Sign`, PDF đã ký lưu MinIO của KSK. his-server chỉ còn phục vụ biểu mẫu/EMR/đăng nhập.

**Tech Stack:** .NET 8, EF Core 8 (Npgsql), xunit 2.9 + `Microsoft.EntityFrameworkCore.InMemory`, Minio .NET SDK, Next.js/TypeScript (turbo-web).

**Spec:** `docs/superpowers/specs/2026-09-18-ksk-standalone-signing-design.md`

## Global Constraints

- `HealthExam.Domain.csproj` **không được có** `PackageReference` hay `ProjectReference` nào. `HealthExam.Tests/Architecture/DependencyRuleTests.cs` cưỡng chế điều này.
- `HealthExam.Application.csproj` chỉ được tham chiếu `HealthExam.Domain.csproj`. Interface cổng ngoài đặt ở `HealthExam.Application.Integrations`, implementation đặt ở `HealthExam.Infrastructure`.
- Package `Minio` chỉ được thêm vào `HealthExam.Infrastructure.csproj`.
- Chạy test: `TZ=Asia/Ho_Chi_Minh dotnet test HealthExamServer.sln`
- Tạo migration: `dotnet ef migrations add <Name> --project HealthExam.Infrastructure --startup-project HealthExam.API`
- **Không bao giờ log response của ssm-server** — nó chứa PIN của chứng thư số.
- Mọi cột `timestamp` dùng `timestamp with time zone`, ghi bằng `DateTime.UtcNow`.
- Tên test viết tiếng Việt không dấu kiểu `Ky_muc_kham_ghi_snapshot`, theo đúng `ConclusionEligibilityTests.cs` đang có.
- Commit message tiếng Anh, kết thúc bằng:
  ```
  Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
  ```

---

## File Structure

**Tạo mới**

| File | Trách nhiệm |
|---|---|
| `HealthExam.Domain/ExamForms/SignStepMap.cs` | Entity cấu hình bước ký |
| `HealthExam.Application/Signing/ISignStepMapRepository.cs` | Đọc cấu hình bước ký |
| `HealthExam.Application/Integrations/ICertificateGateway.cs` | Cổng ssm-server |
| `HealthExam.Application/Integrations/IPdfSigner.cs` | Cổng sign-server |
| `HealthExam.Application/Integrations/IExamFileStore.cs` | Cổng MinIO |
| `HealthExam.Application/Signing/SignExamSection.cs` | Handler ký một mục khám |
| `HealthExam.Application/Signing/CancelExamSectionSign.cs` | Handler hủy ký mục khám |
| `HealthExam.Application/Signing/SigningModels.cs` | Command/Result của hai handler trên |
| `HealthExam.Infrastructure/Persistence/Repositories/SignStepMapRepository.cs` | Impl đọc bảng map |
| `HealthExam.Infrastructure/Integrations/Ssm/SsmCertificateGateway.cs` | Impl ssm-server |
| `HealthExam.Infrastructure/Integrations/SignServer/SignServerPdfSigner.cs` | Impl sign-server |
| `HealthExam.Infrastructure/Integrations/SignServer/SignServerOptions.cs` | Cấu hình sign-server + ssm |
| `HealthExam.Infrastructure/Storage/MinioExamFileStore.cs` | Impl MinIO |
| `HealthExam.Infrastructure/Storage/ExamFileStoreOptions.cs` | Cấu hình MinIO |
| `HealthExam.Tests/Signing/SignStepMapTests.cs` | Test bảng map |
| `HealthExam.Tests/Signing/SignExamSectionTests.cs` | Test ký/hủy ký mục khám |
| `HealthExam.Tests/Signing/ConclusionSigningTests.cs` | Test ký kết luận |
| `HealthExam.Tests/Signing/FakeSigningGateways.cs` | Test double cho 3 cổng ngoài |

**Sửa**

| File | Thay đổi |
|---|---|
| `HealthExam.Domain/ExamRecords/ExamRecordSignStep.cs` | Bỏ `SWTID`, thêm `VariantCode`/`ItemGroupID`/`SignedByEmployeeCode`, rút gọn `Status` |
| `HealthExam.Domain/ExamRecords/ExamRecord.cs` | Bỏ 3 cột HIS, đổi tên 2 cột |
| `HealthExam.Infrastructure/Persistence/HealthExamDbContext.cs` | Cấu hình `SignStepMap` + sửa 2 entity trên |
| `HealthExam.Infrastructure/Persistence/Repositories/ExamRecordRepository.cs` | Cập nhật 3 nhánh `.Include(x => x.SignSteps)` |
| `HealthExam.Application/Paraclinical/GetConclusionEligibility.cs` | Đọc bước từ bảng map |
| `HealthExam.Application/Paraclinical/SignConclusion.cs` | Viết lại theo sign-server |
| `HealthExam.Application/Paraclinical/ParaclinicalModels.cs` | Thêm `ActorCode` vào command, sửa result |
| `HealthExam.Application/RegistrationForms/PreviewRegistrationFormPdf.cs` | Đọc PDF từ MinIO |
| `HealthExam.API/Middlewares/HealthExamRequestContext.cs` | Thêm `ActorCode` |
| `HealthExam.API/Controllers/ExamRecordController.cs` | 2 endpoint mới, truyền `ActorCode` |
| `HealthExam.API/Controllers/ExamRecordHisFormController.cs` | Xóa 3 endpoint ký |
| `HealthExam.API/Extensions/ApplicationServiceExtensions.cs` | Đăng ký DI mới |
| `HealthExam.Infrastructure/Integrations/HisEmr/HisEmrClient.cs` | Xóa các hàm ký |
| `HealthExam.Application/Integrations/IHisEmrClient.cs` | Xóa khai báo tương ứng |
| `.env.example` | Thêm cấu hình mới, bỏ `HIS_EMR_SECTION_SIGNING_ENABLED` |

**Xóa**

`HealthExam.Application/His/SubmitHisSection.cs`, `SignHisSection.cs`, `CancelHisSectionSignature.cs`, `GetHisSectionSigningContexts.cs`, `HisSectionSigningContextResolver.cs`, `GetSignWorkflow.cs`

---

## Task 1: Bảng cấu hình bước ký `HEX_SignStepMap`

**Files:**
- Create: `HealthExam.Domain/ExamForms/SignStepMap.cs`
- Create: `HealthExam.Application/Signing/ISignStepMapRepository.cs`
- Create: `HealthExam.Infrastructure/Persistence/Repositories/SignStepMapRepository.cs`
- Modify: `HealthExam.Infrastructure/Persistence/HealthExamDbContext.cs`
- Test: `HealthExam.Tests/Signing/SignStepMapTests.cs`

**Interfaces:**
- Consumes: không
- Produces:
  - `HealthExam.Domain.ExamForms.SignStepMap` với các property nêu ở Step 3
  - `ISignStepMapRepository.ListAsync(string divisionId, string variantCode, CancellationToken ct) → Task<IReadOnlyList<SignStepMap>>` — chỉ trả dòng `IsActive`, sắp theo `SWStep` tăng dần

- [ ] **Step 1: Viết test thất bại**

Tạo `HealthExam.Tests/Signing/SignStepMapTests.cs`:

```csharp
using System;
using System.Linq;
using System.Threading.Tasks;
using HealthExam.Domain.ExamForms;
using HealthExam.Infrastructure.Persistence.Repositories;
using Xunit;

namespace HealthExam.Tests.Signing;

/// <summary>
/// Bảng map giờ CHÍNH LÀ quy trình ký. Bộ test này canh hai thứ: chỉ trả bước đang bật,
/// và luôn trả theo thứ tự bước để chữ ký đóng lên PDF theo trật tự đọc được.
/// </summary>
public class SignStepMapTests
{
    private const string DivisionId = "DIV01";
    private const string VariantCode = "KSK06-18T";

    [Fact]
    public async Task Chi_tra_buoc_dang_bat_va_sap_theo_SWStep()
    {
        using var db = InMemoryTestDb.CreateContext();

        db.Set<SignStepMap>().AddRange(
            new SignStepMap
            {
                ID = Guid.NewGuid(), DivisionID = DivisionId, VariantCode = VariantCode,
                SWStep = 3, ItemGroupID = 103, StepName = "Bác sĩ khám mắt",
                SignTitle = "Bác sĩ khám mắt", SWRoleID = 45, SignType = 1, SLType = 2,
                SearchPattern = "##{S3}##", IsConclusionStep = false, IsActive = true
            },
            new SignStepMap
            {
                ID = Guid.NewGuid(), DivisionID = DivisionId, VariantCode = VariantCode,
                SWStep = 1, ItemGroupID = 101, StepName = "Bác sĩ khám thể lực",
                SignTitle = "Bác sĩ khám thể lực", SWRoleID = 45, SignType = 1, SLType = 2,
                SearchPattern = "##{S1}##", IsConclusionStep = false, IsActive = true
            },
            new SignStepMap
            {
                ID = Guid.NewGuid(), DivisionID = DivisionId, VariantCode = VariantCode,
                SWStep = 2, ItemGroupID = 102, StepName = "Bước đã tắt",
                SignTitle = "Bước đã tắt", SWRoleID = 45, SignType = 1, SLType = 2,
                SearchPattern = "##{S2}##", IsConclusionStep = false, IsActive = false
            });
        await db.SaveChangesAsync();

        var repo = new SignStepMapRepository(db);
        var steps = await repo.ListAsync(DivisionId, VariantCode);

        Assert.Equal(new[] { 1, 3 }, steps.Select(s => s.SWStep).ToArray());
    }

    [Fact]
    public async Task Khong_tra_buoc_cua_variant_khac()
    {
        using var db = InMemoryTestDb.CreateContext();

        db.Set<SignStepMap>().Add(new SignStepMap
        {
            ID = Guid.NewGuid(), DivisionID = DivisionId, VariantCode = "KSK-TREN18TUOI",
            SWStep = 1, ItemGroupID = 101, StepName = "Bước của biến thể khác",
            SignTitle = "x", SWRoleID = 45, SignType = 1, SLType = 2,
            SearchPattern = "##{S1}##", IsConclusionStep = false, IsActive = true
        });
        await db.SaveChangesAsync();

        var repo = new SignStepMapRepository(db);
        var steps = await repo.ListAsync(DivisionId, VariantCode);

        Assert.Empty(steps);
    }
}
```

- [ ] **Step 2: Chạy test để chắc chắn nó fail**

Chạy: `TZ=Asia/Ho_Chi_Minh dotnet test HealthExamServer.sln --filter SignStepMapTests`
Kỳ vọng: FAIL, lỗi biên dịch `SignStepMap` và `SignStepMapRepository` chưa tồn tại.

- [ ] **Step 3: Tạo entity**

Tạo `HealthExam.Domain/ExamForms/SignStepMap.cs`:

```csharp
using System;

namespace HealthExam.Domain.ExamForms;

/// <summary>
/// Cấu hình một bước ký của một bộ biểu mẫu KSK. Thay cho api/M02F01500/RSWByDocTypeID của HIS:
/// sau khi bỏ HIS khỏi đường ký thì bảng này chính là quy trình ký.
/// </summary>
public class SignStepMap
{
    public Guid ID { get; set; }
    public string DivisionID { get; set; } = "";
    public string VariantCode { get; set; } = "";
    public int SWStep { get; set; }

    /// <summary>Mục khám lâm sàng. Null ở bước kết luận vì bước đó không thuộc mục khám nào.</summary>
    public int? ItemGroupID { get; set; }

    public string StepName { get; set; } = "";
    public string SignTitle { get; set; } = "";
    public long SWRoleID { get; set; }

    /// <summary>1 ký số, 2 ký điện tử, 3 đóng dấu. Khớp SignType của sign-server.</summary>
    public int SignType { get; set; } = 1;

    /// <summary>1 vị trí chính xác, 2 theo chuỗi tìm kiếm, 3 người dùng chọn.</summary>
    public int SLType { get; set; } = 2;

    public string SearchPattern { get; set; } = "";
    public int SLPage { get; set; }
    public float SLX { get; set; }
    public float SLY { get; set; }
    public bool IsConclusionStep { get; set; }
    public bool IsActive { get; set; } = true;
}
```

- [ ] **Step 4: Tạo interface repository**

Tạo `HealthExam.Application/Signing/ISignStepMapRepository.cs`:

```csharp
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using HealthExam.Domain.ExamForms;

namespace HealthExam.Application.Signing;

public interface ISignStepMapRepository
{
    /// <summary>Các bước ký đang bật của một bộ biểu mẫu, sắp theo SWStep tăng dần.</summary>
    Task<IReadOnlyList<SignStepMap>> ListAsync(string divisionId, string variantCode, CancellationToken ct = default);
}
```

- [ ] **Step 5: Tạo implementation**

Tạo `HealthExam.Infrastructure/Persistence/Repositories/SignStepMapRepository.cs`:

```csharp
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HealthExam.Application.Signing;
using HealthExam.Domain.ExamForms;
using Microsoft.EntityFrameworkCore;

namespace HealthExam.Infrastructure.Persistence.Repositories;

public class SignStepMapRepository : ISignStepMapRepository
{
    private readonly HealthExamDbContext _db;

    public SignStepMapRepository(HealthExamDbContext db) => _db = db;

    public async Task<IReadOnlyList<SignStepMap>> ListAsync(
        string divisionId, string variantCode, CancellationToken ct = default)
        => await _db.Set<SignStepMap>()
            .AsNoTracking()
            .Where(x => x.DivisionID == divisionId && x.VariantCode == variantCode && x.IsActive)
            .OrderBy(x => x.SWStep)
            .ToListAsync(ct);
}
```

- [ ] **Step 6: Cấu hình EF**

Trong `HealthExam.Infrastructure/Persistence/HealthExamDbContext.cs`, thêm vào `OnModelCreating` (đặt ngay sau khối cấu hình `HEX_ExamGroupFormSectionMapping`):

```csharp
modelBuilder.Entity<SignStepMap>(e =>
{
    e.ToTable("HEX_SignStepMap");
    e.HasKey(x => x.ID);
    e.Property(x => x.ID).HasDefaultValueSql("gen_random_uuid()");
    e.Property(x => x.DivisionID).HasMaxLength(20).HasDefaultValue("");
    e.Property(x => x.VariantCode).HasMaxLength(50).IsRequired();
    e.Property(x => x.StepName).HasMaxLength(255).HasDefaultValue("");
    e.Property(x => x.SignTitle).HasMaxLength(255).HasDefaultValue("");
    e.Property(x => x.SearchPattern).HasMaxLength(50).HasDefaultValue("");
    e.Property(x => x.SignType).HasDefaultValue(1);
    e.Property(x => x.SLType).HasDefaultValue(2);
    e.Property(x => x.IsActive).HasDefaultValue(true);
    e.HasIndex(x => new { x.DivisionID, x.VariantCode, x.SWStep }).IsUnique();
    e.HasIndex(x => new { x.DivisionID, x.VariantCode, x.ItemGroupID })
        .IsUnique()
        .HasFilter("\"ItemGroupID\" IS NOT NULL");
});
```

Thêm `using HealthExam.Domain.ExamForms;` nếu file chưa có.

- [ ] **Step 7: Chạy test để chắc chắn nó pass**

Chạy: `TZ=Asia/Ho_Chi_Minh dotnet test HealthExamServer.sln --filter SignStepMapTests`
Kỳ vọng: PASS, 2 test.

- [ ] **Step 8: Tạo migration**

```bash
dotnet ef migrations add AddSignStepMap \
  --project HealthExam.Infrastructure --startup-project HealthExam.API
```

Mở file migration vừa sinh, kiểm tra có `CREATE TABLE "HEX_SignStepMap"` và hai unique index. Nếu EF không sinh `HasFilter` thành `WHERE "ItemGroupID" IS NOT NULL`, sửa tay trong migration.

- [ ] **Step 8b: Viết script seed cho KSK06-18T**

`ItemGroupID` của từng mục khám nằm trong cây biểu mẫu trên HIS, không suy ra được từ code. Lấy bằng một lệnh, rồi điền vào script:

```bash
# templateid của KSK06-18T lấy ở màn M03/M03F00031 trên dhtesting
curl -s -H "Authorization: Bearer $HIS_TOKEN" \
  "https://his.dhtesting.dhsc.vn/api/M03F00030/GetTreeTemplateByID?templateID=7f50a56d-c94b-4e0d-b403-37405d01d71a" \
  | jq '.. | objects | select(has("ItemGroupID") and has("ItemGroupName")) | {ItemGroupID, ItemGroupName}'
```

`SWRoleID` lấy từ cột **Quyền** của màn WFMF00030 (ảnh cấu hình KSK02: toàn bộ là "Bác sĩ Khám sức khỏe" — tra `RoleID` của vai trò đó trong `sAM_Roles`).

Tạo `HealthExam.Infrastructure/Persistence/Seeds/sign-step-map-ksk06-18t.sql`. Khung dưới dựng theo workflow KSK02 trong ảnh WFM, chỉ giữ 4 bước có mục khám tương ứng ở FE (thể lực, mắt, TMH, RHM) cộng bước kết luận; hai bước "tâm thần" và "trả KQ CLS" cố ý không khai vì FE không có mục đó:

```sql
INSERT INTO "HEX_SignStepMap"
  ("DivisionID","VariantCode","SWStep","ItemGroupID","StepName","SignTitle","SWRoleID",
   "SignType","SLType","SearchPattern","SLPage","SLX","SLY","IsConclusionStep","IsActive")
VALUES
  ('DIV01','KSK06-18T',2,<ItemGroupID thể lực>,'Bác sĩ khám thể lực','Bác sĩ khám thể lực',<RoleID>,1,2,'##{S2}##',0,0,0,false,true),
  ('DIV01','KSK06-18T',4,<ItemGroupID mắt>,'Bác sĩ khám mắt','Bác sĩ khám mắt',<RoleID>,1,2,'##{S4}##',0,0,0,false,true),
  ('DIV01','KSK06-18T',5,<ItemGroupID TMH>,'Bác sĩ khám tai-mũi-họng','Bác sĩ khám tai-mũi-họng',<RoleID>,1,2,'##{S5}##',0,0,0,false,true),
  ('DIV01','KSK06-18T',6,<ItemGroupID RHM>,'Bác sĩ khám răng-hàm-mặt','Bác sĩ khám răng-hàm-mặt',<RoleID>,1,2,'##{S6}##',0,0,0,false,true),
  ('DIV01','KSK06-18T',8,NULL,'Người kết luận','Người kết luận',<RoleID>,1,2,'##{S8}##',0,0,0,true,true)
ON CONFLICT ("DivisionID","VariantCode","SWStep") DO UPDATE SET
  "ItemGroupID" = EXCLUDED."ItemGroupID",
  "StepName" = EXCLUDED."StepName",
  "SignTitle" = EXCLUDED."SignTitle",
  "SWRoleID" = EXCLUDED."SWRoleID",
  "SearchPattern" = EXCLUDED."SearchPattern",
  "IsConclusionStep" = EXCLUDED."IsConclusionStep",
  "IsActive" = EXCLUDED."IsActive";
```

Thay các `<...>` bằng giá trị thật từ hai lệnh tra ở trên **trước khi commit** — script commit lên phải chạy được ngay. `DivisionID` và `VariantCode` đối chiếu với dòng thật trong `HEX_ExamRecord` của môi trường đích. Script này chạy tay sau `dotnet ef database update`, không nhúng vào migration vì mỗi môi trường có `RoleID` khác nhau.

- [ ] **Step 9: Commit**

```bash
git add HealthExam.Domain/ExamForms/SignStepMap.cs \
        HealthExam.Application/Signing/ISignStepMapRepository.cs \
        HealthExam.Infrastructure/Persistence/Repositories/SignStepMapRepository.cs \
        HealthExam.Infrastructure/Persistence/HealthExamDbContext.cs \
        HealthExam.Infrastructure/Persistence/Migrations/ \
        HealthExam.Infrastructure/Persistence/Seeds/ \
        HealthExam.Tests/Signing/SignStepMapTests.cs
git commit -m "feat(signing): add sign step map configuration table

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

## Task 2: Đổi schema snapshot và hồ sơ

**Files:**
- Modify: `HealthExam.Domain/ExamRecords/ExamRecordSignStep.cs`
- Modify: `HealthExam.Domain/ExamRecords/ExamRecord.cs:61-71`
- Modify: `HealthExam.Infrastructure/Persistence/HealthExamDbContext.cs` (khối `HEX_ExamRecordSignStep` và `HEX_ExamRecord`)
- Modify: `HealthExam.Infrastructure/Persistence/Repositories/ExamRecordRepository.cs`
- Modify: `HealthExam.Application/Paraclinical/SignConclusion.cs`
- Modify: `HealthExam.Application/Paraclinical/GetConclusionEligibility.cs`
- Modify: `HealthExam.Application/RegistrationForms/PreviewRegistrationFormPdf.cs`
- Test: `HealthExam.Tests/Signing/SignStepMapTests.cs` (thêm test mới)

**Interfaces:**
- Consumes: `SignStepMap` từ Task 1
- Produces:
  - `ExamRecordSignStep` với `VariantCode`, `ItemGroupID`, `SignedByEmployeeCode`, `Status ∈ {Snapshot, Signed, Failed}`
  - `ExamRecord.SignStatus`, `ExamRecord.SignedFilePath`
  - Hằng `ExamRecordSignStepStatus.Snapshot` / `.Signed` / `.Failed`
  - Hằng `ExamRecordSignStatus.New` / `.Signed` / `.Failed`

Task này chỉ đổi hình dạng dữ liệu. `SignConclusion.cs` sẽ tạm thời không biên dịch được ở chỗ dùng `SWTID`; sửa tối thiểu để build xanh, còn viết lại toàn bộ là việc của Task 9.

- [ ] **Step 1: Viết test thất bại**

Thêm vào `HealthExam.Tests/Signing/SignStepMapTests.cs`:

```csharp
    /// <summary>
    /// Snapshot là bản ghi "bác sĩ X đã bấm ký mục Y lúc Z". Unique theo (hồ sơ, biến thể, bước)
    /// để bấm ký hai lần cùng một mục không sinh hai chữ ký.
    /// </summary>
    [Fact]
    public async Task Snapshot_luu_variant_itemgroup_va_ma_nhan_vien()
    {
        using var db = InMemoryTestDb.CreateContext();
        var recordId = Guid.NewGuid();

        db.Set<HealthExam.Domain.ExamRecords.ExamRecordSignStep>().Add(
            new HealthExam.Domain.ExamRecords.ExamRecordSignStep
            {
                ID = Guid.NewGuid(),
                DivisionID = DivisionId,
                RecordID = recordId,
                VariantCode = VariantCode,
                ItemGroupID = 101,
                SWStep = 1,
                StepName = "Bác sĩ khám thể lực",
                SWRoleID = 45,
                SignedByEmployeeID = 1274,
                SignedByEmployeeCode = "NV001",
                SignedAt = new DateTime(2026, 9, 18, 2, 30, 0, DateTimeKind.Utc),
                Status = HealthExam.Domain.ExamRecords.ExamRecordSignStepStatus.Snapshot
            });
        await db.SaveChangesAsync();

        var row = db.Set<HealthExam.Domain.ExamRecords.ExamRecordSignStep>().Single();
        Assert.Equal(VariantCode, row.VariantCode);
        Assert.Equal(101, row.ItemGroupID);
        Assert.Equal("NV001", row.SignedByEmployeeCode);
        Assert.Equal("Snapshot", row.Status);
    }
```

- [ ] **Step 2: Chạy test để chắc chắn nó fail**

Chạy: `TZ=Asia/Ho_Chi_Minh dotnet test HealthExamServer.sln --filter SignStepMapTests`
Kỳ vọng: FAIL, lỗi biên dịch — `ExamRecordSignStep` chưa có `VariantCode`, `ItemGroupID`, `SignedByEmployeeCode`, và `ExamRecordSignStepStatus.Snapshot` chưa tồn tại.

- [ ] **Step 3: Sửa entity snapshot**

Thay toàn bộ `HealthExam.Domain/ExamRecords/ExamRecordSignStep.cs`:

```csharp
using System;

namespace HealthExam.Domain.ExamRecords;

public static class ExamRecordSignStepStatus
{
    /// <summary>Bác sĩ đã bấm ký, chưa đóng lên PDF.</summary>
    public const string Snapshot = "Snapshot";

    /// <summary>Đã đóng lên PDF thành công.</summary>
    public const string Signed = "Signed";

    public const string Failed = "Failed";
}

/// <summary>
/// Một lượt "bác sĩ X bấm Ký số ở mục Y lúc Z". Dòng chỉ sinh ra khi có người bấm ký —
/// không đổ sẵn theo quy trình. Sự tồn tại của dòng này CHÍNH LÀ khóa read-only của mục khám.
/// </summary>
public class ExamRecordSignStep
{
    public Guid ID { get; set; }
    public string DivisionID { get; set; } = "";
    public Guid RecordID { get; set; }
    public string VariantCode { get; set; } = "";

    /// <summary>Mục khám sinh ra snapshot này. Null ở bước kết luận.</summary>
    public int? ItemGroupID { get; set; }

    public int SWStep { get; set; }
    public string StepName { get; set; } = "";
    public long SWRoleID { get; set; }
    public string Status { get; set; } = ExamRecordSignStepStatus.Snapshot;
    public long? SignedByEmployeeID { get; set; }

    /// <summary>sign-server nhận empCode, và chứng thư số tra theo mã chứ không theo ID.</summary>
    public string SignedByEmployeeCode { get; set; } = "";

    public DateTime? SignedAt { get; set; }
    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;
    public DateTime ModifiedDate { get; set; } = DateTime.UtcNow;

    public ExamRecord Record { get; set; }
}
```

`SWTID`, `FileDocTypeID`, `LastObservedAt` biến mất.

- [ ] **Step 4: Sửa entity hồ sơ**

Trong `HealthExam.Domain/ExamRecords/ExamRecord.cs`, thay khối dòng 65-71:

```csharp
    public string SignedFilePath { get; set; }
    public string SignStatus { get; set; } = ExamRecordSignStatus.New;
    public long? HisSignedByEmployeeID { get; set; }
    public DateTime? HisSignedAt { get; set; }
```

Bỏ hẳn `HisSignedFileDocID`, `HisSignTransactionID`, `HisSignKeyID`, `HisSignStatus`, `HisSignedFilePath`.

Thêm vào cuối file, ngoài class `ExamRecord`:

```csharp
public static class ExamRecordSignStatus
{
    public const string New = "New";
    public const string Signed = "Signed";
    public const string Failed = "Failed";
}
```

- [ ] **Step 5: Cập nhật cấu hình EF**

Trong `HealthExam.Infrastructure/Persistence/HealthExamDbContext.cs`, khối `HEX_ExamRecordSignStep` (quanh dòng 288) thay thành:

```csharp
e.ToTable("HEX_ExamRecordSignStep");
e.HasKey(x => x.ID);
e.Property(x => x.ID).HasDefaultValueSql("gen_random_uuid()");
e.Property(x => x.DivisionID).HasMaxLength(20).HasDefaultValue("");
e.Property(x => x.VariantCode).HasMaxLength(50).HasDefaultValue("");
e.Property(x => x.StepName).HasMaxLength(255).HasDefaultValue("");
e.Property(x => x.SignedByEmployeeCode).HasMaxLength(50).HasDefaultValue("");
e.Property(x => x.Status).HasMaxLength(30).HasDefaultValue(ExamRecordSignStepStatus.Snapshot);
e.HasIndex(x => new { x.DivisionID, x.RecordID, x.VariantCode, x.SWStep }).IsUnique();
e.HasIndex(x => new { x.DivisionID, x.RecordID, x.Status });
e.HasOne(x => x.Record).WithMany(x => x.SignSteps).HasForeignKey(x => x.RecordID);
```

Trong khối `HEX_ExamRecord` (quanh dòng 215), đổi tên hai property đã cấu hình `HisSignStatus` → `SignStatus`, `HisSignedFilePath` → `SignedFilePath`, và xóa cấu hình của ba cột đã bỏ.

- [ ] **Step 6: Sửa các chỗ đọc cột cũ**

Sửa đúng 3 file, thay tên cột:

- `HealthExam.Application/Paraclinical/GetConclusionEligibility.cs`: `record.HisSignStatus` → `record.SignStatus`; bỏ các dòng đọc `record.HisSignTransactionID` và biến `swtId` (Task 8 viết lại phần này).
- `HealthExam.Application/Paraclinical/SignConclusion.cs`: đổi `HisSignStatus` → `SignStatus`, `HisSignedFilePath` → `SignedFilePath`; xóa mọi dòng gán `HisSignTransactionID`, `HisSignKeyID`, `HisSignedFileDocID`, `SWTID`, `FileDocTypeID`, `LastObservedAt`. Mục tiêu duy nhất của bước này là build xanh — Task 9 xóa và viết lại file.
- `HealthExam.Application/RegistrationForms/PreviewRegistrationFormPdf.cs:49,56,78,80`: `record.HisSignedFilePath` → `record.SignedFilePath`.

`ExamRecordRepository.cs` chỉ cần kiểm lại 3 nhánh có `.Include(x => x.SignSteps)` (dòng 113, 134 và nhánh thứ ba) — không đổi gì nếu chúng không tham chiếu cột đã bỏ.

- [ ] **Step 7: Chạy test để chắc chắn nó pass**

Chạy: `TZ=Asia/Ho_Chi_Minh dotnet test HealthExamServer.sln --filter SignStepMapTests`
Kỳ vọng: PASS, 3 test.

Sau đó chạy toàn bộ: `TZ=Asia/Ho_Chi_Minh dotnet test HealthExamServer.sln`
Kỳ vọng: các test cũ của `SignConclusion` trong `ParaclinicalHandlerTests.cs` sẽ đỏ vì chúng assert `HisSignedFileDocID` và `ProcessID`. Đánh dấu `[Fact(Skip = "Viết lại ở Task 9")]` cho đúng hai test `SignConclusion_submits_rendered_pdf_directly_without_medical_process` và `SignConclusion_fails_with_sign_precondition_when_conditions_not_satisfied`. Mọi test khác phải xanh.

- [ ] **Step 8: Tạo migration**

```bash
dotnet ef migrations add ReshapeSignStepSnapshot \
  --project HealthExam.Infrastructure --startup-project HealthExam.API
```

Mở migration, xác nhận có `DropColumn` cho `SWTID`/`FileDocTypeID`/`LastObservedAt`/`HisSignTransactionID`/`HisSignKeyID`/`HisSignedFileDocID` và `RenameColumn` cho hai cột còn lại.

- [ ] **Step 9: Commit**

```bash
git add HealthExam.Domain/ HealthExam.Infrastructure/ HealthExam.Application/ HealthExam.Tests/
git commit -m "refactor(signing): reshape sign step snapshot for standalone signing

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

## Task 3: Cổng chứng thư số (ssm-server)

**Files:**
- Create: `HealthExam.Application/Integrations/ICertificateGateway.cs`
- Create: `HealthExam.Infrastructure/Integrations/Ssm/SsmCertificateGateway.cs`
- Create: `HealthExam.Infrastructure/Integrations/SignServer/SignServerOptions.cs`
- Test: `HealthExam.Tests/Signing/SsmCertificateGatewayTests.cs`

**Interfaces:**
- Consumes: không
- Produces:
  - `record EmployeeCertificate(string EmployeeCode, string CertName, string Company, string CoPCode, string Pin, DateTime ValidUntil)`
  - `ICertificateGateway.GetAsync(string employeeCode, CancellationToken ct) → Task<CertificateResult>`
  - `record CertificateResult(bool Found, EmployeeCertificate Certificate, string Message)`

- [ ] **Step 1: Viết test thất bại**

Tạo `HealthExam.Tests/Signing/SsmCertificateGatewayTests.cs`:

```csharp
using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using HealthExam.Infrastructure.Integrations.Ssm;
using HealthExam.Infrastructure.Integrations.SignServer;
using Xunit;

namespace HealthExam.Tests.Signing;

public class SsmCertificateGatewayTests
{
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly string _body;
        public HttpRequestMessage LastRequest { get; private set; }

        public StubHandler(HttpStatusCode status, string body)
        {
            _status = status;
            _body = body;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(new HttpResponseMessage(_status)
            {
                Content = new StringContent(_body)
            });
        }
    }

    private static SignServerOptions Options => new()
    {
        SsmBaseUrl = "http://ssm.internal",
        SignServerBaseUrl = "http://sign.internal",
        InternalSecret = "s3cr3t",
        TimeoutSeconds = 60
    };

    /// <summary>
    /// ssm-server bọc CertInfo trong {Data: ...}. Gateway phải bóc đúng lớp đó và gắn
    /// Bearer SECRET_INTER, vì ssm-server chỉ chấp nhận secret nội bộ.
    /// </summary>
    [Fact]
    public async Task Doc_duoc_chung_thu_va_gan_secret_noi_bo()
    {
        var stub = new StubHandler(HttpStatusCode.OK, """
        {"Data":{"Name":"BS A","CertName":"cn=bs-a","Pin":"1234","Company":"VIS",
                 "CoPCode":"CCHN01","Valid":"2027-01-01T00:00:00"}}
        """);
        var gateway = new SsmCertificateGateway(new HttpClient(stub), Options);

        var res = await gateway.GetAsync("NV001");

        Assert.True(res.Found);
        Assert.Equal("cn=bs-a", res.Certificate.CertName);
        Assert.Equal("1234", res.Certificate.Pin);
        Assert.Equal("VIS", res.Certificate.Company);
        Assert.Equal("CCHN01", res.Certificate.CoPCode);
        Assert.Contains("userCode=NV001", stub.LastRequest.RequestUri.ToString());
        Assert.Equal("Bearer s3cr3t", stub.LastRequest.Headers.Authorization.ToString());
    }

    /// <summary>
    /// Chứng thư hết hạn phải bị từ chối tại đây, không để lọt xuống sign-server rồi
    /// mới vỡ giữa lượt ký.
    /// </summary>
    [Fact]
    public async Task Tu_choi_chung_thu_het_han()
    {
        var stub = new StubHandler(HttpStatusCode.OK, """
        {"Data":{"CertName":"cn=bs-a","Pin":"1234","Company":"VIS","CoPCode":"C1",
                 "Valid":"2020-01-01T00:00:00"}}
        """);
        var gateway = new SsmCertificateGateway(new HttpClient(stub), Options);

        var res = await gateway.GetAsync("NV001");

        Assert.False(res.Found);
        Assert.Contains("hết hạn", res.Message);
    }

    [Fact]
    public async Task Khong_co_chung_thu_tra_Found_false()
    {
        var stub = new StubHandler(HttpStatusCode.BadRequest, "không tìm thấy");
        var gateway = new SsmCertificateGateway(new HttpClient(stub), Options);

        var res = await gateway.GetAsync("NV404");

        Assert.False(res.Found);
        Assert.Null(res.Certificate);
    }
}
```

- [ ] **Step 2: Chạy test để chắc chắn nó fail**

Chạy: `TZ=Asia/Ho_Chi_Minh dotnet test HealthExamServer.sln --filter SsmCertificateGatewayTests`
Kỳ vọng: FAIL, lỗi biên dịch `SsmCertificateGateway` và `SignServerOptions` chưa tồn tại.

- [ ] **Step 3: Tạo interface và model**

Tạo `HealthExam.Application/Integrations/ICertificateGateway.cs`:

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;

namespace HealthExam.Application.Integrations;

/// <summary>
/// Chứng thư số của một nhân viên. CHỨA PIN — không bao giờ ghi object này ra log.
/// </summary>
public sealed record EmployeeCertificate(
    string EmployeeCode,
    string CertName,
    string Company,
    string CoPCode,
    string Pin,
    DateTime ValidUntil);

public sealed record CertificateResult(bool Found, EmployeeCertificate Certificate, string Message);

public interface ICertificateGateway
{
    Task<CertificateResult> GetAsync(string employeeCode, CancellationToken ct = default);
}
```

- [ ] **Step 4: Tạo options**

Tạo `HealthExam.Infrastructure/Integrations/SignServer/SignServerOptions.cs`:

```csharp
using System;

namespace HealthExam.Infrastructure.Integrations.SignServer;

public class SignServerOptions
{
    public string SsmBaseUrl { get; init; } = "";
    public string SignServerBaseUrl { get; init; } = "";
    public string InternalSecret { get; init; } = "";
    public int TimeoutSeconds { get; init; } = 60;

    public static SignServerOptions FromEnvironment() => new()
    {
        SsmBaseUrl = Environment.GetEnvironmentVariable("SSM_BASE_URL")?.Trim() ?? "",
        SignServerBaseUrl = Environment.GetEnvironmentVariable("SIGN_SERVER_BASE_URL")?.Trim() ?? "",
        InternalSecret = Environment.GetEnvironmentVariable("SECRET_INTER")?.Trim() ?? "",
        TimeoutSeconds = int.TryParse(
            Environment.GetEnvironmentVariable("SIGN_SERVER_TIMEOUT_SECONDS"), out var t) && t > 0 ? t : 60
    };
}
```

- [ ] **Step 5: Tạo implementation**

Tạo `HealthExam.Infrastructure/Integrations/Ssm/SsmCertificateGateway.cs`:

```csharp
using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using HealthExam.Application.Integrations;
using HealthExam.Infrastructure.Integrations.SignServer;
using Newtonsoft.Json.Linq;

namespace HealthExam.Infrastructure.Integrations.Ssm;

/// <summary>
/// Gọi thẳng ssm-server thay cho api/Util/CertInfo của his-server. Đường này chính là
/// đường his-server vẫn dùng: URL_SSM_API/api/SSM/SignInfo + Bearer SECRET_INTER.
/// </summary>
public class SsmCertificateGateway : ICertificateGateway
{
    private readonly HttpClient _http;
    private readonly SignServerOptions _options;

    public SsmCertificateGateway(HttpClient http, SignServerOptions options)
    {
        _http = http;
        _options = options;
    }

    public async Task<CertificateResult> GetAsync(string employeeCode, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(employeeCode))
            return new CertificateResult(false, null, "Thiếu mã nhân viên");

        var uri = $"{_options.SsmBaseUrl.TrimEnd('/')}/api/SSM/SignInfo?userCode={Uri.EscapeDataString(employeeCode)}";
        using var message = new HttpRequestMessage(HttpMethod.Get, uri);
        message.Headers.TryAddWithoutValidation("Authorization", $"Bearer {_options.InternalSecret}");

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(message, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return new CertificateResult(false, null, "Không kết nối được dịch vụ chứng thư số");
        }

        // KHÔNG log body: nó chứa PIN.
        if (!response.IsSuccessStatusCode)
            return new CertificateResult(false, null, "Nhân viên chưa có chứng thư số");

        var raw = await response.Content.ReadAsStringAsync(ct);
        JToken node;
        try { node = JToken.Parse(raw); }
        catch { return new CertificateResult(false, null, "Dịch vụ chứng thư số trả dữ liệu không hợp lệ"); }

        var data = node["Data"] ?? (node is JArray arr && arr.Count > 0 ? arr[0] : node);
        var certName = data?.Value<string>("CertName");
        if (string.IsNullOrWhiteSpace(certName))
            return new CertificateResult(false, null, "Nhân viên chưa có chứng thư số");

        var validUntil = data.Value<DateTime?>("Valid") ?? DateTime.MaxValue;
        if (validUntil < DateTime.Now)
            return new CertificateResult(false, null, "Chứng thư số đã hết hạn");

        return new CertificateResult(true, new EmployeeCertificate(
            employeeCode,
            certName,
            data.Value<string>("Company") ?? "",
            data.Value<string>("CoPCode") ?? "",
            data.Value<string>("Pin") ?? "",
            validUntil), "");
    }
}
```

- [ ] **Step 6: Chạy test để chắc chắn nó pass**

Chạy: `TZ=Asia/Ho_Chi_Minh dotnet test HealthExamServer.sln --filter SsmCertificateGatewayTests`
Kỳ vọng: PASS, 3 test.

- [ ] **Step 7: Commit**

```bash
git add HealthExam.Application/Integrations/ICertificateGateway.cs \
        HealthExam.Infrastructure/Integrations/ \
        HealthExam.Tests/Signing/SsmCertificateGatewayTests.cs
git commit -m "feat(signing): add ssm-server certificate gateway

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

## Task 4: Cổng ký PDF (sign-server)

**Files:**
- Create: `HealthExam.Application/Integrations/IPdfSigner.cs`
- Create: `HealthExam.Infrastructure/Integrations/SignServer/SignServerPdfSigner.cs`
- Test: `HealthExam.Tests/Signing/SignServerPdfSignerTests.cs`

**Interfaces:**
- Consumes: `EmployeeCertificate` (Task 3), `SignServerOptions` (Task 3)
- Produces:
  - `record PdfSignRequest(byte[] Pdf, EmployeeCertificate Certificate, string EmployeeName, DateTime DisplayDate, string SignTitle, int SignType, int SignLocationType, string SearchPattern, int Page, float PositionX, float PositionY)`
  - `record PdfSignOutcome(bool Succeeded, byte[] Pdf, string Message)`
  - `IPdfSigner.SignAsync(PdfSignRequest request, CancellationToken ct) → Task<PdfSignOutcome>`

- [ ] **Step 1: Viết test thất bại**

Tạo `HealthExam.Tests/Signing/SignServerPdfSignerTests.cs`:

```csharp
using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HealthExam.Application.Integrations;
using HealthExam.Infrastructure.Integrations.SignServer;
using Xunit;

namespace HealthExam.Tests.Signing;

public class SignServerPdfSignerTests
{
    private sealed class CapturingHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly byte[] _body;
        public string CapturedForm { get; private set; }

        public CapturingHandler(HttpStatusCode status, byte[] body)
        {
            _status = status;
            _body = body;
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CapturedForm = await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(_status) { Content = new ByteArrayContent(_body) };
        }
    }

    private static readonly byte[] SignedPdf = Encoding.ASCII.GetBytes("%PDF-1.7 signed");
    private static readonly byte[] DraftPdf = Encoding.ASCII.GetBytes("%PDF-1.7 draft");

    private static SignServerOptions Options => new()
    {
        SsmBaseUrl = "http://ssm.internal",
        SignServerBaseUrl = "http://sign.internal",
        InternalSecret = "s3cr3t",
        TimeoutSeconds = 60
    };

    private static PdfSignRequest Request(byte[] pdf) => new(
        pdf,
        new EmployeeCertificate("NV001", "cn=bs-a", "VIS", "CCHN01", "1234", DateTime.MaxValue),
        "BS Nguyễn Văn A",
        new DateTime(2026, 9, 18, 9, 30, 0, DateTimeKind.Local),
        "Bác sĩ khám thể lực",
        SignType: 1,
        SignLocationType: 2,
        SearchPattern: "##{S1}##",
        Page: 0,
        PositionX: 0,
        PositionY: 0);

    /// <summary>
    /// Gửi 'file' mà KHÔNG gửi 'filePath' thì sign-server trả bytes PDF đã ký. Đó là điều kiện
    /// để nối chuỗi 8 chữ ký trong bộ nhớ, chỉ ghi MinIO một lần ở cuối.
    /// </summary>
    [Fact]
    public async Task Gui_file_khong_gui_filePath_thi_nhan_lai_bytes_da_ky()
    {
        var handler = new CapturingHandler(HttpStatusCode.OK, SignedPdf);
        var signer = new SignServerPdfSigner(new HttpClient(handler), Options);

        var res = await signer.SignAsync(Request(DraftPdf));

        Assert.True(res.Succeeded);
        Assert.Equal(SignedPdf, res.Pdf);
        Assert.DoesNotContain("name=\"filePath\"", handler.CapturedForm);
    }

    /// <summary>
    /// Ngày hiển thị cạnh chữ ký phải là lúc bác sĩ BẤM ký, định dạng yyyy-MM-dd HH:mm
    /// giống payload his-server vẫn gửi, chứ không phải lúc chạy vòng lặp ký.
    /// </summary>
    [Fact]
    public async Task Gui_dung_marker_va_ngay_hien_thi_cua_snapshot()
    {
        var handler = new CapturingHandler(HttpStatusCode.OK, SignedPdf);
        var signer = new SignServerPdfSigner(new HttpClient(handler), Options);

        await signer.SignAsync(Request(DraftPdf));

        Assert.Contains("##{S1}##", handler.CapturedForm);
        Assert.Contains("2026-09-18 09:30", handler.CapturedForm);
        Assert.Contains("cn=bs-a", handler.CapturedForm);
        Assert.Contains("NV001", handler.CapturedForm);
    }

    [Fact]
    public async Task Sign_server_loi_thi_tra_that_bai_va_giu_nguyen_pdf_cu()
    {
        var handler = new CapturingHandler(HttpStatusCode.BadRequest,
            Encoding.UTF8.GetBytes("không tìm thấy chuỗi ##{S1}##"));
        var signer = new SignServerPdfSigner(new HttpClient(handler), Options);

        var res = await signer.SignAsync(Request(DraftPdf));

        Assert.False(res.Succeeded);
        Assert.Null(res.Pdf);
        Assert.Contains("##{S1}##", res.Message);
    }

    /// <summary>
    /// sign-server trả 200 kèm JSON lỗi trong vài nhánh. Bytes không mở đầu bằng %PDF-
    /// thì coi là thất bại, tuyệt đối không nối tiếp vào lượt ký sau.
    /// </summary>
    [Fact]
    public async Task Body_khong_phai_pdf_thi_coi_la_that_bai()
    {
        var handler = new CapturingHandler(HttpStatusCode.OK,
            Encoding.UTF8.GetBytes("{\"ErrorCode\":1}"));
        var signer = new SignServerPdfSigner(new HttpClient(handler), Options);

        var res = await signer.SignAsync(Request(DraftPdf));

        Assert.False(res.Succeeded);
    }
}
```

- [ ] **Step 2: Chạy test để chắc chắn nó fail**

Chạy: `TZ=Asia/Ho_Chi_Minh dotnet test HealthExamServer.sln --filter SignServerPdfSignerTests`
Kỳ vọng: FAIL, lỗi biên dịch `SignServerPdfSigner`, `PdfSignRequest`, `IPdfSigner` chưa tồn tại.

- [ ] **Step 3: Tạo interface**

Tạo `HealthExam.Application/Integrations/IPdfSigner.cs`:

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;

namespace HealthExam.Application.Integrations;

public sealed record PdfSignRequest(
    byte[] Pdf,
    EmployeeCertificate Certificate,
    string EmployeeName,
    /// <summary>Ngày in cạnh chữ ký — thời điểm bác sĩ bấm ký, không phải lúc chạy ký.</summary>
    DateTime DisplayDate,
    string SignTitle,
    int SignType,
    int SignLocationType,
    string SearchPattern,
    int Page,
    float PositionX,
    float PositionY);

public sealed record PdfSignOutcome(bool Succeeded, byte[] Pdf, string Message);

public interface IPdfSigner
{
    Task<PdfSignOutcome> SignAsync(PdfSignRequest request, CancellationToken ct = default);
}
```

- [ ] **Step 4: Tạo implementation**

Tạo `HealthExam.Infrastructure/Integrations/SignServer/SignServerPdfSigner.cs`:

```csharp
using System;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HealthExam.Application.Integrations;

namespace HealthExam.Infrastructure.Integrations.SignServer;

/// <summary>
/// Gọi sign-server POST api/SIGN/Sign. Gửi 'file' và KHÔNG gửi 'filePath' nên sign-server
/// trả về bytes PDF đã ký thay vì ghi MinIO — cho phép nối chuỗi nhiều chữ ký trong bộ nhớ.
/// sign-server dùng UseAppendMode() nên chữ ký trước được giữ nguyên và thứ tự ký không quan trọng.
/// </summary>
public class SignServerPdfSigner : IPdfSigner
{
    private readonly HttpClient _http;
    private readonly SignServerOptions _options;

    public SignServerPdfSigner(HttpClient http, SignServerOptions options)
    {
        _http = http;
        _options = options;
    }

    public async Task<PdfSignOutcome> SignAsync(PdfSignRequest request, CancellationToken ct = default)
    {
        if (request.Pdf == null || request.Pdf.Length < 5)
            return new PdfSignOutcome(false, null, "Không có dữ liệu PDF để ký");

        var uri = $"{_options.SignServerBaseUrl.TrimEnd('/')}/api/SIGN/Sign";
        using var form = new MultipartFormDataContent();
        var pdfContent = new ByteArrayContent(request.Pdf);
        pdfContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/pdf");
        form.Add(pdfContent, "file", "health-exam.pdf");

        void Add(string name, string value) => form.Add(new StringContent(value ?? "", Encoding.UTF8), name);

        Add("company", request.Certificate.Company);
        Add("certName", request.Certificate.CertName);
        Add("copCode", request.Certificate.CoPCode);
        Add("pin", request.Certificate.Pin);
        Add("empCode", request.Certificate.EmployeeCode);
        Add("name", request.EmployeeName);
        Add("title", request.SignTitle);
        Add("date", request.DisplayDate.ToString("yyyy-MM-dd HH:mm"));
        Add("signType", request.SignType.ToString());
        Add("signLocationType", request.SignLocationType.ToString());
        Add("searchPattern", request.SearchPattern);
        Add("mode", "0");
        Add("page", request.Page.ToString());
        Add("positionX", request.PositionX.ToString());
        Add("positionY", request.PositionY.ToString());

        HttpResponseMessage response;
        try
        {
            response = await _http.PostAsync(uri, form, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return new PdfSignOutcome(false, null, "Không kết nối được dịch vụ ký số");
        }

        var bytes = await response.Content.ReadAsByteArrayAsync(ct);

        if (!response.IsSuccessStatusCode)
            return new PdfSignOutcome(false, null, Encoding.UTF8.GetString(bytes));

        var isPdf = bytes.Length >= 5 && bytes[0] == '%' && bytes[1] == 'P'
                    && bytes[2] == 'D' && bytes[3] == 'F' && bytes[4] == '-';
        if (!isPdf)
            return new PdfSignOutcome(false, null, "Dịch vụ ký số không trả về PDF hợp lệ");

        return new PdfSignOutcome(true, bytes, "");
    }
}
```

- [ ] **Step 5: Chạy test để chắc chắn nó pass**

Chạy: `TZ=Asia/Ho_Chi_Minh dotnet test HealthExamServer.sln --filter SignServerPdfSignerTests`
Kỳ vọng: PASS, 4 test.

- [ ] **Step 6: Commit**

```bash
git add HealthExam.Application/Integrations/IPdfSigner.cs \
        HealthExam.Infrastructure/Integrations/SignServer/SignServerPdfSigner.cs \
        HealthExam.Tests/Signing/SignServerPdfSignerTests.cs
git commit -m "feat(signing): add sign-server pdf signer gateway

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

## Task 5: Lưu trữ PDF trên MinIO

**Files:**
- Create: `HealthExam.Application/Integrations/IExamFileStore.cs`
- Create: `HealthExam.Infrastructure/Storage/ExamFileStoreOptions.cs`
- Create: `HealthExam.Infrastructure/Storage/MinioExamFileStore.cs`
- Modify: `HealthExam.Infrastructure/HealthExam.Infrastructure.csproj`
- Test: `HealthExam.Tests/Signing/ExamFileStorePathTests.cs`

**Interfaces:**
- Consumes: không
- Produces:
  - `IExamFileStore.UploadPdfAsync(string objectPath, byte[] pdf, CancellationToken ct) → Task<bool>`
  - `IExamFileStore.DownloadAsync(string objectPath, CancellationToken ct) → Task<byte[]>` (trả `null` nếu không có)
  - `static string ExamFileStorePaths.ConclusionPdf(string divisionId, DateTime nowUtc, Guid recordId)`

MinIO thật không test được bằng unit test. Test ở đây chỉ canh quy tắc đặt đường dẫn — thứ dễ sai và gây mất file. Kết nối thật kiểm bằng tay ở Step 6.

- [ ] **Step 1: Viết test thất bại**

Tạo `HealthExam.Tests/Signing/ExamFileStorePathTests.cs`:

```csharp
using System;
using HealthExam.Infrastructure.Storage;
using Xunit;

namespace HealthExam.Tests.Signing;

public class ExamFileStorePathTests
{
    /// <summary>
    /// Đường dẫn phải phân tầng theo tenant và tháng, nếu không một bucket phẳng sẽ có
    /// hàng trăm nghìn object và liệt kê không nổi.
    /// </summary>
    [Fact]
    public async Task Duong_dan_phan_tang_theo_tenant_va_thang()
    {
        var recordId = Guid.Parse("11111111-2222-3333-4444-555555555555");
        var path = ExamFileStorePaths.ConclusionPdf(
            "DIV01", new DateTime(2026, 9, 18, 0, 0, 0, DateTimeKind.Utc), recordId);

        Assert.Equal("DIV01/2026/09/11111111-2222-3333-4444-555555555555.pdf", path);
        await Task.CompletedTask;
    }
}
```

- [ ] **Step 2: Chạy test để chắc chắn nó fail**

Chạy: `TZ=Asia/Ho_Chi_Minh dotnet test HealthExamServer.sln --filter ExamFileStorePathTests`
Kỳ vọng: FAIL, `ExamFileStorePaths` chưa tồn tại.

- [ ] **Step 3: Thêm package MinIO**

```bash
dotnet add HealthExam.Infrastructure package Minio --version 6.0.3
```

Chỉ thêm vào `HealthExam.Infrastructure`. Thêm vào `HealthExam.Domain` hay `HealthExam.Application` sẽ làm đỏ `DependencyRuleTests`.

- [ ] **Step 4: Tạo interface và quy tắc đường dẫn**

Tạo `HealthExam.Application/Integrations/IExamFileStore.cs`:

```csharp
using System.Threading;
using System.Threading.Tasks;

namespace HealthExam.Application.Integrations;

public interface IExamFileStore
{
    Task<bool> UploadPdfAsync(string objectPath, byte[] pdf, CancellationToken ct = default);

    /// <summary>Trả null nếu object không tồn tại.</summary>
    Task<byte[]> DownloadAsync(string objectPath, CancellationToken ct = default);
}
```

Tạo `HealthExam.Infrastructure/Storage/ExamFileStoreOptions.cs`:

```csharp
using System;

namespace HealthExam.Infrastructure.Storage;

public class ExamFileStoreOptions
{
    public string Endpoint { get; init; } = "";
    public string AccessKey { get; init; } = "";
    public string SecretKey { get; init; } = "";
    public string Bucket { get; init; } = "";
    public bool UseSsl { get; init; }

    public static ExamFileStoreOptions FromEnvironment() => new()
    {
        Endpoint = Environment.GetEnvironmentVariable("MINIO_ENDPOINT")?.Trim() ?? "",
        AccessKey = Environment.GetEnvironmentVariable("MINIO_ACCESS_KEY")?.Trim() ?? "",
        SecretKey = Environment.GetEnvironmentVariable("MINIO_SECRET_KEY")?.Trim() ?? "",
        Bucket = Environment.GetEnvironmentVariable("MINIO_BUCKET_HEALTH_EXAM")?.Trim() ?? "",
        UseSsl = string.Equals(
            Environment.GetEnvironmentVariable("MINIO_USE_SSL"), "true", StringComparison.OrdinalIgnoreCase)
    };
}

public static class ExamFileStorePaths
{
    public static string ConclusionPdf(string divisionId, DateTime nowUtc, Guid recordId)
        => $"{divisionId}/{nowUtc:yyyy}/{nowUtc:MM}/{recordId}.pdf";
}
```

- [ ] **Step 5: Tạo implementation**

Tạo `HealthExam.Infrastructure/Storage/MinioExamFileStore.cs`:

```csharp
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using HealthExam.Application.Integrations;
using Minio;
using Minio.DataModel.Args;
using Minio.Exceptions;

namespace HealthExam.Infrastructure.Storage;

public class MinioExamFileStore : IExamFileStore
{
    private readonly IMinioClient _client;
    private readonly ExamFileStoreOptions _options;

    public MinioExamFileStore(IMinioClient client, ExamFileStoreOptions options)
    {
        _client = client;
        _options = options;
    }

    public async Task<bool> UploadPdfAsync(string objectPath, byte[] pdf, CancellationToken ct = default)
    {
        try
        {
            using var stream = new MemoryStream(pdf);
            await _client.PutObjectAsync(new PutObjectArgs()
                .WithBucket(_options.Bucket)
                .WithObject(objectPath)
                .WithStreamData(stream)
                .WithObjectSize(pdf.LongLength)
                .WithContentType("application/pdf"), ct);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public async Task<byte[]> DownloadAsync(string objectPath, CancellationToken ct = default)
    {
        try
        {
            using var buffer = new MemoryStream();
            await _client.GetObjectAsync(new GetObjectArgs()
                .WithBucket(_options.Bucket)
                .WithObject(objectPath)
                .WithCallbackStream(s => s.CopyTo(buffer)), ct);
            return buffer.ToArray();
        }
        catch (ObjectNotFoundException)
        {
            return null;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
```

- [ ] **Step 6: Chạy test và kiểm tra kết nối thật**

Chạy: `TZ=Asia/Ho_Chi_Minh dotnet test HealthExamServer.sln --filter ExamFileStorePathTests`
Kỳ vọng: PASS.

Kiểm tra kết nối MinIO bằng tay: đặt các biến `MINIO_*` trỏ vào MinIO của môi trường dev rồi chạy `dotnet run --project HealthExam.API`, xác nhận service khởi động không ném lỗi cấu hình.

- [ ] **Step 7: Commit**

```bash
git add HealthExam.Application/Integrations/IExamFileStore.cs \
        HealthExam.Infrastructure/Storage/ \
        HealthExam.Infrastructure/HealthExam.Infrastructure.csproj \
        HealthExam.Tests/Signing/ExamFileStorePathTests.cs
git commit -m "feat(signing): add minio exam file store

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

## Task 6: Ký một mục khám (snapshot)

**Files:**
- Create: `HealthExam.Application/Signing/SigningModels.cs`
- Create: `HealthExam.Application/Signing/SignExamSection.cs`
- Create: `HealthExam.Tests/Signing/FakeSigningGateways.cs`
- Create: `HealthExam.Tests/Signing/SignExamSectionTests.cs`
- Modify: `HealthExam.API/Middlewares/HealthExamRequestContext.cs`
- Modify: `HealthExam.API/Controllers/ExamRecordController.cs`
- Modify: `HealthExam.API/Extensions/ApplicationServiceExtensions.cs`

**Interfaces:**
- Consumes: `ISignStepMapRepository` (Task 1), `ICertificateGateway` (Task 3), `IExamRecordRepository`, `IUnitOfWork`, `IAuditRepository`
- Produces:
  - `record SignExamSectionCommand(string DivisionId, Guid RecordId, int ItemGroupId, long EmployeeId, string EmployeeCode, string EmployeeName, ActorKind ActorKind, IReadOnlyCollection<long> RoleIds)`
  - `record ExamSectionSignResult(int ItemGroupId, int SwStep, string StepName, long SignedByEmployeeID, DateTime SignedAt)`
  - `ISignExamSectionHandler.HandleAsync(command, ct) → Task<ApplicationResult<ExamSectionSignResult>>`
  - `HealthExamContext.ActorCode`

- [ ] **Step 1: Viết test double**

Tạo `HealthExam.Tests/Signing/FakeSigningGateways.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HealthExam.Application.Integrations;
using HealthExam.Application.Signing;
using HealthExam.Domain.ExamForms;

namespace HealthExam.Tests.Signing;

public sealed class FakeSignStepMapRepository : ISignStepMapRepository
{
    public List<SignStepMap> Steps { get; } = new();

    public Task<IReadOnlyList<SignStepMap>> ListAsync(
        string divisionId, string variantCode, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<SignStepMap>>(
            Steps.Where(s => s.DivisionID == divisionId && s.VariantCode == variantCode && s.IsActive)
                 .OrderBy(s => s.SWStep).ToList());
}

public sealed class FakeCertificateGateway : ICertificateGateway
{
    public HashSet<string> WithCertificate { get; } = new();
    public int CallCount { get; private set; }

    public Task<CertificateResult> GetAsync(string employeeCode, CancellationToken ct = default)
    {
        CallCount++;
        return Task.FromResult(WithCertificate.Contains(employeeCode)
            ? new CertificateResult(true, new EmployeeCertificate(
                employeeCode, $"cn={employeeCode}", "VIS", "C1", "1234", DateTime.MaxValue), "")
            : new CertificateResult(false, null, "Nhân viên chưa có chứng thư số"));
    }
}

public sealed class FakePdfSigner : IPdfSigner
{
    public List<PdfSignRequest> Requests { get; } = new();
    public HashSet<string> FailForPattern { get; } = new();

    public Task<PdfSignOutcome> SignAsync(PdfSignRequest request, CancellationToken ct = default)
    {
        Requests.Add(request);
        if (FailForPattern.Contains(request.SearchPattern))
            return Task.FromResult(new PdfSignOutcome(false, null, $"không tìm thấy {request.SearchPattern}"));

        var next = new byte[request.Pdf.Length + 1];
        Array.Copy(request.Pdf, next, request.Pdf.Length);
        next[^1] = (byte)Requests.Count;
        return Task.FromResult(new PdfSignOutcome(true, next, ""));
    }
}

public sealed class FakeExamFileStore : IExamFileStore
{
    public Dictionary<string, byte[]> Objects { get; } = new();
    public bool FailUpload { get; set; }

    public Task<bool> UploadPdfAsync(string objectPath, byte[] pdf, CancellationToken ct = default)
    {
        if (FailUpload) return Task.FromResult(false);
        Objects[objectPath] = pdf;
        return Task.FromResult(true);
    }

    public Task<byte[]> DownloadAsync(string objectPath, CancellationToken ct = default)
        => Task.FromResult(Objects.TryGetValue(objectPath, out var v) ? v : null);
}
```

- [ ] **Step 2: Viết test thất bại**

Tạo `HealthExam.Tests/Signing/SignExamSectionTests.cs`:

```csharp
using System;
using System.Linq;
using System.Threading.Tasks;
using HealthExam.Application.Common;
using HealthExam.Application.Signing;
using HealthExam.Domain.Common;
using HealthExam.Domain.ExamForms;
using HealthExam.Domain.ExamRecords;
using Xunit;

namespace HealthExam.Tests.Signing;

public class SignExamSectionTests
{
    private const string DivisionId = "DIV01";
    private const string VariantCode = "KSK06-18T";
    private const int ItemGroupId = 101;

    private static SignStepMap Step(int swStep, int? itemGroupId, long roleId = 45, bool conclusion = false) => new()
    {
        ID = Guid.NewGuid(), DivisionID = DivisionId, VariantCode = VariantCode,
        SWStep = swStep, ItemGroupID = itemGroupId, StepName = $"Bước {swStep}",
        SignTitle = $"Bước {swStep}", SWRoleID = roleId, SignType = 1, SLType = 2,
        SearchPattern = $"##{{S{swStep}}}##", IsConclusionStep = conclusion, IsActive = true
    };

    private static SignExamSectionCommand Command(Guid recordId, long roleId = 45) => new(
        DivisionId, recordId, ItemGroupId, EmployeeId: 1274, EmployeeCode: "NV001",
        EmployeeName: "BS A", ActorKind.Employee, RoleIds: new[] { roleId });

    /// <summary>
    /// Bấm Ký số KHÔNG gọi sign-server. Nó chỉ ghi lại ai ký mục nào lúc nào — PDF còn chưa
    /// tồn tại ở thời điểm này vì các mục khác chưa nhập xong.
    /// </summary>
    [Fact]
    public async Task Ky_muc_kham_chi_ghi_snapshot_khong_ky_pdf()
    {
        var (handler, db, record, _, signer) = Fixture();

        var res = await handler.HandleAsync(Command(record.RecordID));

        Assert.True(res.IsSuccess);
        Assert.Equal(1, res.Value.SwStep);
        Assert.Empty(signer.Requests);

        var snapshot = db.RecordOf(record.RecordID).SignSteps.Single();
        Assert.Equal(ItemGroupId, snapshot.ItemGroupID);
        Assert.Equal("NV001", snapshot.SignedByEmployeeCode);
        Assert.Equal(ExamRecordSignStepStatus.Snapshot, snapshot.Status);
    }

    /// <summary>
    /// Chứng thư số kiểm tại đây theo quyết định thiết kế: bác sĩ biết mình thiếu chứng thư
    /// ngay lúc ký, thay vì để người kết luận phát hiện hộ.
    /// </summary>
    [Fact]
    public async Task Khong_co_chung_thu_so_thi_tu_choi_va_khong_ghi_snapshot()
    {
        var (handler, db, record, cert, _) = Fixture();
        cert.WithCertificate.Clear();

        var res = await handler.HandleAsync(Command(record.RecordID));

        Assert.False(res.IsSuccess);
        Assert.Equal(ApplicationFailureCode.SignPrecondition, res.Failure.Code);
        Assert.Empty(db.RecordOf(record.RecordID).SignSteps);
    }

    [Fact]
    public async Task Khong_giu_vai_tro_cua_buoc_thi_tra_Forbidden()
    {
        var (handler, db, record, _, _) = Fixture();

        var res = await handler.HandleAsync(Command(record.RecordID, roleId: 99));

        Assert.False(res.IsSuccess);
        Assert.Equal(ApplicationFailureCode.Forbidden, res.Failure.Code);
        Assert.Empty(db.RecordOf(record.RecordID).SignSteps);
    }

    [Fact]
    public async Task Muc_kham_khong_co_trong_bang_map_thi_bao_loi_cau_hinh()
    {
        var (handler, db, record, _, _) = Fixture();
        var cmd = Command(record.RecordID) with { ItemGroupId = 999 };

        var res = await handler.HandleAsync(cmd);

        Assert.False(res.IsSuccess);
        Assert.Equal(ApplicationFailureCode.SignPrecondition, res.Failure.Code);
    }

    /// <summary>
    /// Bấm hai lần không được sinh hai dòng — unique (hồ sơ, biến thể, bước) sẽ vỡ ở DB thật,
    /// nên handler phải tự ghi đè.
    /// </summary>
    [Fact]
    public async Task Bam_ky_hai_lan_chi_con_mot_snapshot()
    {
        var (handler, db, record, _, _) = Fixture();

        await handler.HandleAsync(Command(record.RecordID));
        await handler.HandleAsync(Command(record.RecordID));

        Assert.Single(db.RecordOf(record.RecordID).SignSteps);
    }

    [Fact]
    public async Task Ho_so_da_ky_xong_thi_khong_ky_them_muc_nao()
    {
        var (handler, db, record, _, _) = Fixture();
        db.SetSignStatus(record.RecordID, ExamRecordSignStatus.Signed);

        var res = await handler.HandleAsync(Command(record.RecordID));

        Assert.False(res.IsSuccess);
        Assert.Equal(ApplicationFailureCode.InvalidState, res.Failure.Code);
    }

    private static (SignExamSectionHandler Handler, InMemoryTestDb Db, ExamRecord Record,
        FakeCertificateGateway Cert, FakePdfSigner Signer) Fixture()
    {
        var db = new InMemoryTestDb();
        var session = db.SeedSession();
        var record = db.SeedRecord(session.SessionID, ExamRecordState.InProgress);
        db.SetVariantCode(record.RecordID, VariantCode);

        var map = new FakeSignStepMapRepository();
        map.Steps.Add(Step(1, ItemGroupId));
        map.Steps.Add(Step(2, 102));
        map.Steps.Add(Step(3, null, conclusion: true));

        var cert = new FakeCertificateGateway();
        cert.WithCertificate.Add("NV001");

        var signer = new FakePdfSigner();
        var handler = db.SignExamSection(map, cert);
        return (handler, db, record, cert, signer);
    }
}
```

- [ ] **Step 3: Chạy test để chắc chắn nó fail**

Chạy: `TZ=Asia/Ho_Chi_Minh dotnet test HealthExamServer.sln --filter SignExamSectionTests`
Kỳ vọng: FAIL, lỗi biên dịch `SignExamSectionCommand`, `SignExamSectionHandler`, và ba helper `InMemoryTestDb.SetVariantCode`, `.SetSignStatus`, `.SignExamSection` chưa tồn tại.

- [ ] **Step 4: Thêm helper vào InMemoryTestDb**

Trong `HealthExam.Tests/InMemoryTestDb.cs`, thêm cạnh `SetHealthClass`:

```csharp
    public void SetVariantCode(Guid recordId, string code)
    {
        var record = Db.Set<ExamRecord>().Single(x => x.RecordID == recordId);
        record.VariantCode = code;
        Db.SaveChanges();
    }

    public void SetSignStatus(Guid recordId, string status)
    {
        var record = Db.Set<ExamRecord>().Single(x => x.RecordID == recordId);
        record.SignStatus = status;
        Db.SaveChanges();
    }

    public HealthExam.Application.Signing.SignExamSectionHandler SignExamSection(
        HealthExam.Application.Signing.ISignStepMapRepository map,
        HealthExam.Application.Integrations.ICertificateGateway cert)
        => new(new ExamRecordRepository(Db), map, cert, new UnitOfWork(Db), new AuditRepository(Db));
```

Nếu tên lớp `UnitOfWork`/`AuditRepository` trong file khác với trên, dùng đúng lớp mà `Conclusion()` ở dòng 452 đang dựng.

- [ ] **Step 5: Tạo command và result**

Tạo `HealthExam.Application/Signing/SigningModels.cs`:

```csharp
using System;
using System.Collections.Generic;
using HealthExam.Domain.Common;

namespace HealthExam.Application.Signing;

public sealed record SignExamSectionCommand(
    string DivisionId,
    Guid RecordId,
    int ItemGroupId,
    long EmployeeId,
    string EmployeeCode,
    string EmployeeName,
    ActorKind ActorKind,
    IReadOnlyCollection<long> RoleIds);

public sealed record ExamSectionSignResult(
    int ItemGroupId,
    int SwStep,
    string StepName,
    long SignedByEmployeeID,
    DateTime SignedAt);

public sealed record CancelExamSectionSignCommand(
    string DivisionId,
    Guid RecordId,
    int ItemGroupId,
    long EmployeeId,
    ActorKind ActorKind);
```

- [ ] **Step 6: Tạo handler**

Tạo `HealthExam.Application/Signing/SignExamSection.cs`:

```csharp
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HealthExam.Application.Common;
using HealthExam.Application.ExamRecords;
using HealthExam.Application.Integrations;
using HealthExam.Domain.Common;
using HealthExam.Domain.ExamRecords;

namespace HealthExam.Application.Signing;

public interface ISignExamSectionHandler
{
    Task<ApplicationResult<ExamSectionSignResult>> HandleAsync(
        SignExamSectionCommand command, CancellationToken ct = default);
}

/// <summary>
/// Bác sĩ bấm Ký số ở một mục khám. KHÔNG gọi sign-server: PDF chỉ được render và ký một lần
/// ở bước kết luận, vì sign-server dùng append mode nên nội dung phải chốt trước chữ ký đầu tiên.
/// </summary>
public class SignExamSectionHandler : ISignExamSectionHandler
{
    private readonly IExamRecordRepository _records;
    private readonly ISignStepMapRepository _map;
    private readonly ICertificateGateway _certificates;
    private readonly IUnitOfWork _uow;
    private readonly IAuditRepository _audit;

    public SignExamSectionHandler(
        IExamRecordRepository records, ISignStepMapRepository map,
        ICertificateGateway certificates, IUnitOfWork uow, IAuditRepository audit)
    {
        _records = records;
        _map = map;
        _certificates = certificates;
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

        if (!command.RoleIds.Contains(step.SWRoleID))
            return ApplicationResult<ExamSectionSignResult>.Fail(
                ApplicationFailureCode.Forbidden,
                $"Bạn không có vai trò ký của bước \"{step.StepName}\"");

        var cert = await _certificates.GetAsync(command.EmployeeCode, ct);
        if (!cert.Found)
            return ApplicationResult<ExamSectionSignResult>.Fail(
                ApplicationFailureCode.SignPrecondition, cert.Message);

        var now = DateTime.UtcNow;
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
        snapshot.Status = ExamRecordSignStepStatus.Snapshot;
        snapshot.SignedByEmployeeID = command.EmployeeId;
        snapshot.SignedByEmployeeCode = command.EmployeeCode;
        snapshot.SignedAt = now;
        snapshot.ModifiedDate = now;

        _audit.Add(new AuditEntry(command.DivisionId, AuditEntityTypes.Record, record.RecordID,
            AuditActions.StateChange, command.EmployeeId.ToString(), null, null,
            new { Action = "SECTION_SIGN_SNAPSHOT", step.SWStep, ItemGroupID = command.ItemGroupId },
            command.ActorKind));

        await _uow.SaveChangesAsync(ct);

        return ApplicationResult<ExamSectionSignResult>.Success(new ExamSectionSignResult(
            command.ItemGroupId, step.SWStep, step.StepName, command.EmployeeId, now));
    }
}
```

- [ ] **Step 7: Chạy test để chắc chắn nó pass**

Chạy: `TZ=Asia/Ho_Chi_Minh dotnet test HealthExamServer.sln --filter SignExamSectionTests`
Kỳ vọng: PASS, 6 test.

- [ ] **Step 8: Thêm ActorCode vào request context**

Trong `HealthExam.API/Middlewares/HealthExamRequestContext.cs`, thêm ngay dưới `ActorName` (dòng 64):

```csharp
    /// <summary>Mã nhân viên. sign-server và ssm-server tra chứng thư theo mã, không theo ID.</summary>
    public string ActorCode => Claim(ClaimNames.EmployeeCode);
```

- [ ] **Step 9: Thêm endpoint**

Trong `HealthExam.API/Controllers/ExamRecordController.cs`, thêm trước `MapToItem`:

```csharp
    [HttpPost("{recordId:guid}/sections/{itemGroupId:int}/sign")]
    [SwaggerOperation(
        Summary = "Ký số một mục khám",
        Description = "Ghi nhận bác sĩ đã ký mục khám và khóa mục đó. Chữ ký chỉ được đóng lên PDF ở bước ký kết luận.")]
    public async Task<ActionResult<ResultData<ExamSectionSignResult>>> SignExamSection(
        Guid recordId, int itemGroupId, CancellationToken ct = default)
    {
        var res = await _signExamSectionHandler.HandleAsync(new SignExamSectionCommand(
            HealthExamContext.DivisionId,
            recordId,
            itemGroupId,
            HealthExamContext.ActorId,
            HealthExamContext.ActorCode,
            HealthExamContext.ActorName,
            HealthExamContext.ActorKind,
            User.FindAll("RoleID").Select(c => long.TryParse(c.Value, out var r) ? r : 0).Where(r => r > 0).ToList()), ct);
        return ToActionResult(res);
    }
```

Thêm `ISignExamSectionHandler _signExamSectionHandler` vào constructor của controller theo đúng cách các handler khác đang được inject, và `using HealthExam.Application.Signing;`.

- [ ] **Step 10: Đăng ký DI**

Trong `HealthExam.API/Extensions/ApplicationServiceExtensions.cs`, thêm:

```csharp
services.AddSingleton(SignServerOptions.FromEnvironment());
services.AddSingleton(ExamFileStoreOptions.FromEnvironment());
services.AddScoped<ISignStepMapRepository, SignStepMapRepository>();
services.AddHttpClient<ICertificateGateway, SsmCertificateGateway>();
services.AddHttpClient<IPdfSigner, SignServerPdfSigner>((sp, http) =>
    http.Timeout = TimeSpan.FromSeconds(sp.GetRequiredService<SignServerOptions>().TimeoutSeconds));
services.AddSingleton<IMinioClient>(sp =>
{
    var o = sp.GetRequiredService<ExamFileStoreOptions>();
    var builder = new MinioClient().WithEndpoint(o.Endpoint).WithCredentials(o.AccessKey, o.SecretKey);
    if (o.UseSsl) builder = builder.WithSSL();
    return builder.Build();
});
services.AddScoped<IExamFileStore, MinioExamFileStore>();
services.AddScoped<ISignExamSectionHandler, SignExamSectionHandler>();
```

- [ ] **Step 11: Chạy toàn bộ test**

Chạy: `TZ=Asia/Ho_Chi_Minh dotnet test HealthExamServer.sln`
Kỳ vọng: xanh, trừ hai test đã `Skip` ở Task 2. `CompositionRootTests` phải xanh — nếu đỏ thì thiếu đăng ký DI ở Step 10.

- [ ] **Step 12: Commit**

```bash
git add HealthExam.Application/Signing/ HealthExam.API/ HealthExam.Tests/
git commit -m "feat(signing): sign exam section as local snapshot

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

## Task 7: Hủy ký một mục khám

**Files:**
- Create: `HealthExam.Application/Signing/CancelExamSectionSign.cs`
- Modify: `HealthExam.Tests/Signing/SignExamSectionTests.cs`
- Modify: `HealthExam.API/Controllers/ExamRecordController.cs`
- Modify: `HealthExam.API/Extensions/ApplicationServiceExtensions.cs`

**Interfaces:**
- Consumes: `CancelExamSectionSignCommand` (Task 6), `IExamRecordRepository`, `IUnitOfWork`, `IAuditRepository`
- Produces: `ICancelExamSectionSignHandler.HandleAsync(command, ct) → Task<ApplicationResult<bool>>`

- [ ] **Step 1: Viết test thất bại**

Thêm vào `HealthExam.Tests/Signing/SignExamSectionTests.cs`:

```csharp
    /// <summary>
    /// Khóa mục khám là trạng thái SUY RA từ sự tồn tại của snapshot. Hủy ký tức là xóa dòng đó,
    /// không có cột cờ nào phải hạ.
    /// </summary>
    [Fact]
    public async Task Huy_ky_xoa_snapshot_va_mo_khoa_muc_kham()
    {
        var (handler, db, record, cert, _) = Fixture();
        await handler.HandleAsync(Command(record.RecordID));
        Assert.Single(db.RecordOf(record.RecordID).SignSteps);

        var cancel = db.CancelExamSectionSign();
        var res = await cancel.HandleAsync(new CancelExamSectionSignCommand(
            DivisionId, record.RecordID, ItemGroupId, EmployeeId: 1274, ActorKind.Employee));

        Assert.True(res.IsSuccess);
        Assert.Empty(db.RecordOf(record.RecordID).SignSteps);
    }

    [Fact]
    public async Task Huy_ky_khi_ho_so_da_ky_ket_luan_thi_bi_tu_choi()
    {
        var (handler, db, record, _, _) = Fixture();
        await handler.HandleAsync(Command(record.RecordID));
        db.SetSignStatus(record.RecordID, ExamRecordSignStatus.Signed);

        var cancel = db.CancelExamSectionSign();
        var res = await cancel.HandleAsync(new CancelExamSectionSignCommand(
            DivisionId, record.RecordID, ItemGroupId, EmployeeId: 1274, ActorKind.Employee));

        Assert.False(res.IsSuccess);
        Assert.Equal(ApplicationFailureCode.InvalidState, res.Failure.Code);
        Assert.Single(db.RecordOf(record.RecordID).SignSteps);
    }

    [Fact]
    public async Task Huy_ky_muc_chua_tung_ky_thi_bao_khong_tim_thay()
    {
        var (_, db, record, _, _) = Fixture();

        var cancel = db.CancelExamSectionSign();
        var res = await cancel.HandleAsync(new CancelExamSectionSignCommand(
            DivisionId, record.RecordID, ItemGroupId, EmployeeId: 1274, ActorKind.Employee));

        Assert.False(res.IsSuccess);
        Assert.Equal(ApplicationFailureCode.NotFound, res.Failure.Code);
    }
```

Thêm helper vào `InMemoryTestDb.cs`:

```csharp
    public HealthExam.Application.Signing.CancelExamSectionSignHandler CancelExamSectionSign()
        => new(new ExamRecordRepository(Db), new UnitOfWork(Db), new AuditRepository(Db));
```

- [ ] **Step 2: Chạy test để chắc chắn nó fail**

Chạy: `TZ=Asia/Ho_Chi_Minh dotnet test HealthExamServer.sln --filter SignExamSectionTests`
Kỳ vọng: FAIL, `CancelExamSectionSignHandler` chưa tồn tại.

- [ ] **Step 3: Tạo handler**

Tạo `HealthExam.Application/Signing/CancelExamSectionSign.cs`:

```csharp
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HealthExam.Application.Common;
using HealthExam.Application.ExamRecords;
using HealthExam.Domain.Common;
using HealthExam.Domain.ExamRecords;

namespace HealthExam.Application.Signing;

public interface ICancelExamSectionSignHandler
{
    Task<ApplicationResult<bool>> HandleAsync(
        CancelExamSectionSignCommand command, CancellationToken ct = default);
}

public class CancelExamSectionSignHandler : ICancelExamSectionSignHandler
{
    private readonly IExamRecordRepository _records;
    private readonly IUnitOfWork _uow;
    private readonly IAuditRepository _audit;

    public CancelExamSectionSignHandler(
        IExamRecordRepository records, IUnitOfWork uow, IAuditRepository audit)
    {
        _records = records;
        _uow = uow;
        _audit = audit;
    }

    public async Task<ApplicationResult<bool>> HandleAsync(
        CancelExamSectionSignCommand command, CancellationToken ct = default)
    {
        var record = await _records.GetAsync(command.DivisionId, command.RecordId, forUpdate: true, ct);
        if (record == null)
            return ApplicationResult<bool>.Fail(ApplicationFailureCode.NotFound, "Không tìm thấy hồ sơ khám");

        if (record.SignStatus == ExamRecordSignStatus.Signed)
            return ApplicationResult<bool>.Fail(
                ApplicationFailureCode.InvalidState, "Hồ sơ đã ký kết luận, không hủy ký từng mục được");

        var snapshot = record.SignSteps?.FirstOrDefault(s => s.ItemGroupID == command.ItemGroupId);
        if (snapshot == null)
            return ApplicationResult<bool>.Fail(
                ApplicationFailureCode.NotFound, "Mục khám này chưa được ký");

        record.SignSteps.Remove(snapshot);

        _audit.Add(new AuditEntry(command.DivisionId, AuditEntityTypes.Record, record.RecordID,
            AuditActions.StateChange, command.EmployeeId.ToString(), null, null,
            new { Action = "SECTION_SIGN_CANCELLED", snapshot.SWStep, ItemGroupID = command.ItemGroupId },
            command.ActorKind));

        await _uow.SaveChangesAsync(ct);
        return ApplicationResult<bool>.Success(true);
    }
}
```

Nếu `record.SignSteps.Remove(...)` không xóa hẳn dòng trong DB (EF chỉ gỡ khỏi collection), thêm phương thức `RemoveSignStep(ExamRecordSignStep step)` vào `IExamRecordRepository` và gọi nó thay vì `Remove` trên collection.

- [ ] **Step 4: Chạy test để chắc chắn nó pass**

Chạy: `TZ=Asia/Ho_Chi_Minh dotnet test HealthExamServer.sln --filter SignExamSectionTests`
Kỳ vọng: PASS, 9 test.

- [ ] **Step 5: Thêm endpoint và DI**

Trong `ExamRecordController.cs`, thêm cạnh endpoint ký mục khám:

```csharp
    [HttpPost("{recordId:guid}/sections/{itemGroupId:int}/sign/cancel")]
    [SwaggerOperation(
        Summary = "Hủy ký một mục khám",
        Description = "Xóa chữ ký đã ghi nhận và mở khóa mục khám để chỉnh sửa.")]
    public async Task<ActionResult<ResultData<bool>>> CancelExamSectionSign(
        Guid recordId, int itemGroupId, CancellationToken ct = default)
    {
        var res = await _cancelExamSectionSignHandler.HandleAsync(new CancelExamSectionSignCommand(
            HealthExamContext.DivisionId, recordId, itemGroupId,
            HealthExamContext.ActorId, HealthExamContext.ActorKind), ct);
        return ToActionResult(res);
    }
```

Trong `ApplicationServiceExtensions.cs`:

```csharp
services.AddScoped<ICancelExamSectionSignHandler, CancelExamSectionSignHandler>();
```

- [ ] **Step 6: Chạy toàn bộ test**

Chạy: `TZ=Asia/Ho_Chi_Minh dotnet test HealthExamServer.sln`
Kỳ vọng: xanh trừ hai test đã `Skip`.

- [ ] **Step 7: Commit**

```bash
git add HealthExam.Application/Signing/CancelExamSectionSign.cs HealthExam.API/ HealthExam.Tests/
git commit -m "feat(signing): cancel exam section signature

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

## Task 8: Điều kiện ký kết luận đọc từ bảng map

**Files:**
- Modify: `HealthExam.Application/Paraclinical/GetConclusionEligibility.cs`
- Modify: `HealthExam.Application/Paraclinical/ParaclinicalModels.cs`
- Test: `HealthExam.Tests/Signing/ConclusionEligibilityFromMapTests.cs`

**Interfaces:**
- Consumes: `ISignStepMapRepository` (Task 1), snapshot của Task 6
- Produces:
  - `ConclusionEligibilityResult` bổ sung `IReadOnlyList<int> MissingSteps`
  - `ConclusionSignStepResult` đổi thành `(int SwStep, string StepName, int? ItemGroupID, long SwRoleId, string Status, long? SignedByEmployeeID, DateTime? SignedAt)`
  - `GetConclusionEligibilityQuery` bổ sung `IReadOnlyCollection<long> RoleIds`

- [ ] **Step 1: Viết test thất bại**

Tạo `HealthExam.Tests/Signing/ConclusionEligibilityFromMapTests.cs`:

```csharp
using System;
using System.Linq;
using System.Threading.Tasks;
using HealthExam.Domain.Common;
using HealthExam.Domain.ExamRecords;
using Xunit;

namespace HealthExam.Tests.Signing;

public class ConclusionEligibilityFromMapTests
{
    /// <summary>
    /// Đã chốt "chặn khi thiếu snapshot". API phải nêu ĐÚNG bước nào thiếu, nếu không người
    /// kết luận chỉ thấy nút xám mà không biết phải đi giục ai.
    /// </summary>
    [Fact]
    public async Task Thieu_snapshot_thi_khong_cho_ky_va_neu_ro_buoc_thieu()
    {
        var f = new EligibilityFixture();
        await f.SignSection(itemGroupId: 101);

        var res = await f.Eligibility();

        Assert.False(res.Value.CanSignConclusion);
        Assert.Equal(new[] { 2 }, res.Value.MissingSteps.ToArray());
    }

    [Fact]
    public async Task Du_moi_buoc_va_dung_vai_tro_thi_cho_ky()
    {
        var f = new EligibilityFixture();
        await f.SignSection(101);
        await f.SignSection(102);

        var res = await f.Eligibility();

        Assert.True(res.Value.CanSignConclusion);
        Assert.Empty(res.Value.MissingSteps);
    }

    /// <summary>
    /// Bước kết luận có vai trò riêng. Bác sĩ khám đủ điều kiện dữ liệu nhưng không giữ vai trò
    /// kết luận thì nút phải xám — đây chính là chỗ CanCurrentEmployeeSign cũ trả true cho mọi người.
    /// </summary>
    [Fact]
    public async Task Khong_giu_vai_tro_ket_luan_thi_khong_cho_ky()
    {
        var f = new EligibilityFixture();
        await f.SignSection(101);
        await f.SignSection(102);

        var res = await f.Eligibility(roleIds: new long[] { 45 });

        Assert.False(res.Value.CanSignConclusion);
    }

    [Fact]
    public async Task Tra_day_du_trang_thai_tung_buoc()
    {
        var f = new EligibilityFixture();
        await f.SignSection(101);

        var res = await f.Eligibility();

        var steps = res.Value.Steps.OrderBy(s => s.SwStep).ToList();
        Assert.Equal(3, steps.Count);
        Assert.Equal(ExamRecordSignStepStatus.Snapshot, steps[0].Status);
        Assert.Equal(1274, steps[0].SignedByEmployeeID);
        Assert.Null(steps[1].SignedByEmployeeID);
    }
}
```

`EligibilityFixture` được tạo ở Step 3.

- [ ] **Step 2: Chạy test để chắc chắn nó fail**

Chạy: `TZ=Asia/Ho_Chi_Minh dotnet test HealthExamServer.sln --filter ConclusionEligibilityFromMapTests`
Kỳ vọng: FAIL, `EligibilityFixture` và `MissingSteps` chưa tồn tại.

- [ ] **Step 3: Tạo fixture dùng chung**

Thêm vào cuối `HealthExam.Tests/Signing/ConclusionEligibilityFromMapTests.cs`:

```csharp
internal sealed class EligibilityFixture : IDisposable
{
    public const string DivisionId = "DIV01";
    public const string VariantCode = "KSK06-18T";
    private const long ExamRoleId = 45;
    private const long ConclusionRoleId = 60;

    public InMemoryTestDb Db { get; }
    public ExamRecord Record { get; }
    public FakeSignStepMapRepository Map { get; } = new();
    public FakeCertificateGateway Cert { get; } = new();

    public EligibilityFixture()
    {
        Db = new InMemoryTestDb();
        var session = Db.SeedSession();
        Record = Db.SeedRecord(session.SessionID, ExamRecordState.InProgress);
        Db.SetVariantCode(Record.RecordID, VariantCode);

        Map.Steps.Add(NewStep(1, 101, ExamRoleId, false));
        Map.Steps.Add(NewStep(2, 102, ExamRoleId, false));
        Map.Steps.Add(NewStep(3, null, ConclusionRoleId, true));
        Cert.WithCertificate.Add("NV001");
    }

    private static HealthExam.Domain.ExamForms.SignStepMap NewStep(
        int swStep, int? itemGroupId, long roleId, bool conclusion) => new()
    {
        ID = Guid.NewGuid(), DivisionID = DivisionId, VariantCode = VariantCode,
        SWStep = swStep, ItemGroupID = itemGroupId, StepName = $"Bước {swStep}",
        SignTitle = $"Bước {swStep}", SWRoleID = roleId, SignType = 1, SLType = 2,
        SearchPattern = $"##{{S{swStep}}}##", IsConclusionStep = conclusion, IsActive = true
    };

    public Task SignSection(int itemGroupId)
        => Db.SignExamSection(Map, Cert).HandleAsync(
            new HealthExam.Application.Signing.SignExamSectionCommand(
                DivisionId, Record.RecordID, itemGroupId, 1274, "NV001", "BS A",
                ActorKind.Employee, new[] { ExamRoleId }));

    public Task<HealthExam.Application.Common.ApplicationResult<
        HealthExam.Application.Paraclinical.ConclusionEligibilityResult>> Eligibility(
        long[] roleIds = null)
        => Db.ConclusionEligibility(Map).HandleAsync(
            new HealthExam.Application.Paraclinical.GetConclusionEligibilityQuery(
                DivisionId, Record.RecordID, "Bearer t", "TRACE", "1274", ActorKind.Employee,
                roleIds ?? new long[] { ExamRoleId, ConclusionRoleId }));

    public void Dispose() => Db.Dispose();
}
```

Thêm helper vào `InMemoryTestDb.cs`:

```csharp
    public HealthExam.Application.Paraclinical.GetConclusionEligibilityHandler ConclusionEligibility(
        HealthExam.Application.Signing.ISignStepMapRepository map)
        => new(new ParaclinicalRepository(Db), new ExamRecordRepository(Db), map);
```

Dùng đúng lớp `ParaclinicalRepository` mà `Conclusion()` ở dòng 452 đang dựng.

- [ ] **Step 4: Sửa model kết quả**

Trong `HealthExam.Application/Paraclinical/ParaclinicalModels.cs`, thay `ConclusionSignStepResult` và bổ sung `ConclusionEligibilityResult`:

```csharp
public sealed record ConclusionSignStepResult(
    int SwStep,
    string StepName,
    int? ItemGroupID,
    long SwRoleId,
    string Status,
    long? SignedByEmployeeID,
    DateTime? SignedAt);
```

Thay `ConclusionEligibilityResult` (dòng 66-78) thành:

```csharp
public sealed record ConclusionEligibilityResult(
    Guid ProfileID,
    string RecordCode,
    Guid? SubmissionID,
    IReadOnlyList<ConclusionConditionResult> Conditions,
    bool CanSignConclusion,
    long? SignedByEmployeeID = null,
    DateTime? SignedAt = null,
    IReadOnlyList<ConclusionSignStepResult> Steps = null,
    IReadOnlyList<int> MissingSteps = null);
```

`SwtId`, `CurrentStep`, `RequiredRoleId`, `CanCurrentEmployeeSign` biến mất — `CanSignConclusion` giờ đã gồm cả kiểm tra vai trò.

Trong `GetConclusionEligibilityQuery` (dòng 170-177) thêm tham số cuối, **sau** `DepartmentId`:

```csharp
    IReadOnlyCollection<long> RoleIds = null);
```

Vì tham số này đứng sau các tham số mặc định, mọi chỗ gọi phải truyền theo tên: `RoleIds: ...`. Fixture ở Step 3 cũng vậy — sửa lời gọi thành:

```csharp
            new HealthExam.Application.Paraclinical.GetConclusionEligibilityQuery(
                DivisionId, Record.RecordID, "Bearer t", "TRACE", "1274", ActorKind.Employee,
                RoleIds: roleIds ?? new long[] { ExamRoleId, ConclusionRoleId }));
```

Trong `SignConclusionCommand` thêm hai tham số trước `DepartmentId`: `string ActorCode`, `IReadOnlyCollection<long> RoleIds`.

- [ ] **Step 5: Viết lại phần trạng thái ký của eligibility**

Trong `HealthExam.Application/Paraclinical/GetConclusionEligibility.cs`:

- thêm `ISignStepMapRepository _map` vào constructor, bỏ `IResolveHisConclusionContextHandler`
- điều kiện A đổi thành: mọi bước không phải kết luận đều có snapshot
- thay toàn bộ khối tính `swtId`/`currentStep`/`requiredRoleId`/`canCurrentEmployeeSign` (dòng 137-178) bằng:

```csharp
        var mapSteps = await _map.ListAsync(query.DivisionId, record.VariantCode, ct);
        var snapshots = record.SignSteps?.ToList() ?? new List<ExamRecordSignStep>();

        var stepResults = mapSteps.Select(m =>
        {
            var snap = snapshots.FirstOrDefault(s => s.SWStep == m.SWStep);
            return new ConclusionSignStepResult(
                m.SWStep, m.StepName, m.ItemGroupID, m.SWRoleID,
                snap?.Status ?? "",
                snap?.SignedByEmployeeID,
                snap?.SignedAt);
        }).ToList();

        var conclusionStep = mapSteps.FirstOrDefault(m => m.IsConclusionStep);
        var missingSteps = mapSteps
            .Where(m => !m.IsConclusionStep && snapshots.All(s => s.SWStep != m.SWStep))
            .Select(m => m.SWStep)
            .ToList();

        var isSigned = record.SignStatus == ExamRecordSignStatus.Signed;
        var holdsConclusionRole = conclusionStep != null
            && query.RoleIds != null && query.RoleIds.Contains(conclusionStep.SWRoleID);

        var canSign = !isSigned
            && conditions.All(x => x.Satisfied)
            && missingSteps.Count == 0
            && holdsConclusionRole;
```

Trả `canSign`, `stepResults`, `missingSteps` vào `ConclusionEligibilityResult`.

Điều kiện A dựng lại từ `mapSteps` thay vì gọi HIS:

```csharp
        var clinicalSteps = mapSteps.Where(m => !m.IsConclusionStep).ToList();
        var signedClinical = clinicalSteps.Count(m => snapshots.Any(s => s.SWStep == m.SWStep));
        var conditionA = new ConclusionConditionResult(
            "A", LabelA, clinicalSteps.Count > 0 && signedClinical == clinicalSteps.Count,
            $"{signedClinical}/{clinicalSteps.Count} mục khám đã ký số",
            SourceHealthExam);
```

Bỏ mọi `using` và tham chiếu tới `IResolveHisConclusionContextHandler`.

- [ ] **Step 6: Sửa controller truyền RoleIds**

Trong `ExamRecordController.cs` endpoint `conclusion-eligibility` (dòng 416) và `conclusion/sign` (dòng 439), thêm tham số cuối:

```csharp
User.FindAll("RoleID").Select(c => long.TryParse(c.Value, out var r) ? r : 0).Where(r => r > 0).ToList()
```

và với `conclusion/sign` thêm `HealthExamContext.ActorCode` đúng vị trí `ActorCode` trong command.

- [ ] **Step 7: Chạy test để chắc chắn nó pass**

Chạy: `TZ=Asia/Ho_Chi_Minh dotnet test HealthExamServer.sln --filter ConclusionEligibilityFromMapTests`
Kỳ vọng: PASS, 4 test.

`ConclusionEligibilityTests.cs` cũ sẽ đỏ ở các assert về `CanSignConclusion` vì giờ đòi thêm snapshot và vai trò. Cập nhật fixture của chúng để seed đủ snapshot và `RoleIds`, giữ nguyên ý định test điều kiện (B).

- [ ] **Step 8: Commit**

```bash
git add HealthExam.Application/Paraclinical/ HealthExam.API/ HealthExam.Tests/
git commit -m "feat(signing): compute conclusion eligibility from local sign step map

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

## Task 9: Ký kết luận qua sign-server

**Files:**
- Modify: `HealthExam.Application/Paraclinical/SignConclusion.cs` (viết lại toàn bộ)
- Modify: `HealthExam.Tests/Application/ParaclinicalHandlerTests.cs` (xóa 2 test đã Skip)
- Test: `HealthExam.Tests/Signing/ConclusionSigningTests.cs`

**Interfaces:**
- Consumes: `ISignStepMapRepository`, `ICertificateGateway`, `IPdfSigner`, `IExamFileStore`, `IHisEmrClient.RenderFormPdfAsync`
- Produces: `ConclusionSignResult(Guid RecordID, string Status, string SignedFilePath, long? SignedByEmployeeID, DateTime? SignedAt, IReadOnlyList<ConclusionConditionResult> Conditions, IReadOnlyList<ConclusionSignStepResult> Steps, IReadOnlyList<int> MissingSteps)`

- [ ] **Step 1: Viết test thất bại**

Tạo `HealthExam.Tests/Signing/ConclusionSigningTests.cs`:

```csharp
using System;
using System.Linq;
using System.Threading.Tasks;
using HealthExam.Application.Common;
using HealthExam.Domain.ExamRecords;
using Xunit;

namespace HealthExam.Tests.Signing;

public class ConclusionSigningTests
{
    /// <summary>
    /// Lõi của thiết kế: render PDF MỘT lần, ký nối chuỗi trong bộ nhớ qua từng marker,
    /// ghi MinIO đúng một lần ở cuối.
    /// </summary>
    [Fact]
    public async Task Ky_lan_luot_tung_marker_va_chi_ghi_MinIO_mot_lan()
    {
        var f = new ConclusionFixture();
        await f.SignAllSections();

        var res = await f.SignConclusion();

        Assert.True(res.IsSuccess);
        Assert.Equal(ExamRecordSignStatus.Signed, res.Value.Status);
        Assert.Equal(new[] { "##{S1}##", "##{S2}##", "##{S3}##" },
            f.Signer.Requests.Select(r => r.SearchPattern).ToArray());
        Assert.Single(f.Store.Objects);
        Assert.Equal(1, f.His.RenderCount);
    }

    /// <summary>
    /// Mỗi marker phải ký bằng chứng thư của ĐÚNG bác sĩ đã bấm mục đó, và in ngày lúc họ bấm.
    /// Ký hộ bằng cert người kết luận là làm hỏng ý nghĩa cả quy trình.
    /// </summary>
    [Fact]
    public async Task Moi_buoc_dung_chung_thu_va_ngay_cua_bac_si_buoc_do()
    {
        var f = new ConclusionFixture();
        await f.SignSection(101, "NV001", 1001);
        await f.SignSection(102, "NV002", 1002);

        var res = await f.SignConclusion(employeeCode: "NV009", employeeId: 1009);

        Assert.True(res.IsSuccess);
        Assert.Equal("NV001", f.Signer.Requests[0].Certificate.EmployeeCode);
        Assert.Equal("NV002", f.Signer.Requests[1].Certificate.EmployeeCode);
        Assert.Equal("NV009", f.Signer.Requests[2].Certificate.EmployeeCode);

        var snapshotTime = f.Db.RecordOf(f.Record.RecordID).SignSteps
            .Single(s => s.SWStep == 1).SignedAt!.Value;
        Assert.Equal(snapshotTime.ToLocalTime().ToString("yyyy-MM-dd HH:mm"),
            f.Signer.Requests[0].DisplayDate.ToString("yyyy-MM-dd HH:mm"));
    }

    [Fact]
    public async Task Thieu_buoc_thi_tu_choi_va_khong_goi_sign_server()
    {
        var f = new ConclusionFixture();
        await f.SignSection(101, "NV001", 1001);

        var res = await f.SignConclusion();

        Assert.False(res.IsSuccess);
        Assert.Equal(ApplicationFailureCode.SignPrecondition, res.Failure.Code);
        Assert.Empty(f.Signer.Requests);
        Assert.Equal(0, f.His.RenderCount);
    }

    /// <summary>
    /// Ký hỏng giữa chừng KHÔNG được để lại trạng thái nửa vời: chưa upload gì, hồ sơ vẫn New,
    /// gọi lại là chạy lại sạch từ đầu.
    /// </summary>
    [Fact]
    public async Task Ky_hong_giua_chung_khong_de_lai_file_hay_trang_thai_nua_voi()
    {
        var f = new ConclusionFixture();
        await f.SignAllSections();
        f.Signer.FailForPattern.Add("##{S2}##");

        var res = await f.SignConclusion();

        Assert.False(res.IsSuccess);
        Assert.Empty(f.Store.Objects);
        Assert.Equal(ExamRecordSignStatus.New, f.Db.RecordOf(f.Record.RecordID).SignStatus);
        Assert.All(f.Db.RecordOf(f.Record.RecordID).SignSteps,
            s => Assert.Equal(ExamRecordSignStepStatus.Snapshot, s.Status));
    }

    [Fact]
    public async Task Upload_hong_thi_danh_dau_Failed()
    {
        var f = new ConclusionFixture();
        await f.SignAllSections();
        f.Store.FailUpload = true;

        var res = await f.SignConclusion();

        Assert.False(res.IsSuccess);
        Assert.Equal(ExamRecordSignStatus.Failed, f.Db.RecordOf(f.Record.RecordID).SignStatus);
    }

    /// <summary>
    /// Gọi lại sau khi đã ký xong phải trả trạng thái cũ, tuyệt đối không ký chồng lần hai —
    /// sign-server dùng append mode nên lần hai sẽ thêm một bộ chữ ký trùng vào PDF.
    /// </summary>
    [Fact]
    public async Task Goi_lai_sau_khi_da_ky_khong_ky_them_lan_nua()
    {
        var f = new ConclusionFixture();
        await f.SignAllSections();
        await f.SignConclusion();
        var countAfterFirst = f.Signer.Requests.Count;

        var res = await f.SignConclusion();

        Assert.True(res.IsSuccess);
        Assert.Equal(countAfterFirst, f.Signer.Requests.Count);
        Assert.Single(f.Store.Objects);
    }
}
```

- [ ] **Step 2: Tạo fixture**

Thêm vào cuối `HealthExam.Tests/Signing/ConclusionSigningTests.cs`:

```csharp
internal sealed class ConclusionFixture : IDisposable
{
    private const string DivisionId = "DIV01";
    private const string VariantCode = "KSK06-18T";
    private const long ExamRoleId = 45;
    private const long ConclusionRoleId = 60;

    public InMemoryTestDb Db { get; }
    public ExamRecord Record { get; }
    public FakeSignStepMapRepository Map { get; } = new();
    public FakeCertificateGateway Cert { get; } = new();
    public FakePdfSigner Signer { get; } = new();
    public FakeExamFileStore Store { get; } = new();
    public FakeRenderingHisClient His { get; } = new();

    public ConclusionFixture()
    {
        Db = new InMemoryTestDb();
        var session = Db.SeedSession();
        Record = Db.SeedRecord(session.SessionID, ExamRecordState.InProgress);
        Db.SetVariantCode(Record.RecordID, VariantCode);

        Map.Steps.Add(NewStep(1, 101, ExamRoleId, false));
        Map.Steps.Add(NewStep(2, 102, ExamRoleId, false));
        Map.Steps.Add(NewStep(3, null, ConclusionRoleId, true));

        foreach (var code in new[] { "NV001", "NV002", "NV009" }) Cert.WithCertificate.Add(code);
    }

    private static HealthExam.Domain.ExamForms.SignStepMap NewStep(
        int swStep, int? itemGroupId, long roleId, bool conclusion) => new()
    {
        ID = Guid.NewGuid(), DivisionID = DivisionId, VariantCode = VariantCode,
        SWStep = swStep, ItemGroupID = itemGroupId, StepName = $"Bước {swStep}",
        SignTitle = $"Bước {swStep}", SWRoleID = roleId, SignType = 1, SLType = 2,
        SearchPattern = $"##{{S{swStep}}}##", IsConclusionStep = conclusion, IsActive = true
    };

    public Task SignSection(int itemGroupId, string employeeCode = "NV001", long employeeId = 1001)
        => Db.SignExamSection(Map, Cert).HandleAsync(
            new HealthExam.Application.Signing.SignExamSectionCommand(
                DivisionId, Record.RecordID, itemGroupId, employeeId, employeeCode, "BS",
                HealthExam.Domain.Common.ActorKind.Employee, new[] { ExamRoleId }));

    public async Task SignAllSections()
    {
        await SignSection(101, "NV001", 1001);
        await SignSection(102, "NV002", 1002);
    }

    public Task<HealthExam.Application.Common.ApplicationResult<
        HealthExam.Application.Paraclinical.ConclusionSignResult>> SignConclusion(
        string employeeCode = "NV009", long employeeId = 1009)
        => Db.SignConclusion(Map, Cert, Signer, Store, His).HandleAsync(
            new HealthExam.Application.Paraclinical.SignConclusionCommand(
                DivisionId, employeeId.ToString(), "BS KL",
                HealthExam.Domain.Common.ActorKind.Employee, Record.RecordID,
                "Bearer t", "TRACE", employeeCode,
                new[] { ConclusionRoleId }, DepartmentId: 458));

    public void Dispose() => Db.Dispose();
}
```

Thêm `FakeRenderingHisClient` vào `HealthExam.Tests/Signing/FakeSigningGateways.cs`:

```csharp
public sealed class FakeRenderingHisClient : IHisEmrClient
{
    public int RenderCount { get; private set; }

    public Task<HisClientResult<byte[]>> RenderFormPdfAsync(
        Guid emrDataId, HisRequest request, CancellationToken ct = default)
    {
        RenderCount++;
        return Task.FromResult(HisClientResult<byte[]>.Success(
            System.Text.Encoding.ASCII.GetBytes("%PDF-1.7 draft")));
    }
}
```

Thêm helper vào `InMemoryTestDb.cs`:

```csharp
    public HealthExam.Application.Paraclinical.SignConclusionHandler SignConclusion(
        HealthExam.Application.Signing.ISignStepMapRepository map,
        HealthExam.Application.Integrations.ICertificateGateway cert,
        HealthExam.Application.Integrations.IPdfSigner signer,
        HealthExam.Application.Integrations.IExamFileStore store,
        HealthExam.Application.Integrations.IHisEmrClient his)
        => new(new ParaclinicalRepository(Db), new ExamRecordRepository(Db), map, cert,
               signer, store, his, new UnitOfWork(Db), new AuditRepository(Db));
```

Trong fixture, `SeedRecord` phải đặt `HisEmrDataID` khác `Guid.Empty` — nếu `SeedRecord` chưa làm, thêm một dòng gán trong constructor của fixture.

- [ ] **Step 3: Chạy test để chắc chắn nó fail**

Chạy: `TZ=Asia/Ho_Chi_Minh dotnet test HealthExamServer.sln --filter ConclusionSigningTests`
Kỳ vọng: FAIL, `SignConclusionHandler` chưa nhận các dependency mới.

- [ ] **Step 4: Viết lại SignConclusion**

Thay toàn bộ `HealthExam.Application/Paraclinical/SignConclusion.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HealthExam.Application.Common;
using HealthExam.Application.ExamRecords;
using HealthExam.Application.Integrations;
using HealthExam.Application.Signing;
using HealthExam.Domain.Common;
using HealthExam.Domain.ExamRecords;

namespace HealthExam.Application.Paraclinical;

public interface ISignConclusionHandler
{
    Task<ApplicationResult<ConclusionSignResult>> HandleAsync(
        SignConclusionCommand command, CancellationToken ct = default);
}

/// <summary>
/// Ký kết luận: chốt snapshot bước kết luận, render PDF một lần, rồi ký nối chuỗi từng marker
/// qua sign-server. PDF chỉ nằm trong bộ nhớ suốt vòng lặp và chỉ ghi MinIO một lần ở cuối,
/// nên ký hỏng giữa chừng không để lại file hay trạng thái nửa vời.
/// </summary>
public class SignConclusionHandler : ISignConclusionHandler
{
    private readonly IParaclinicalRepository _paraclinical;
    private readonly IExamRecordRepository _records;
    private readonly ISignStepMapRepository _map;
    private readonly ICertificateGateway _certificates;
    private readonly IPdfSigner _signer;
    private readonly IExamFileStore _store;
    private readonly IHisEmrClient _his;
    private readonly IUnitOfWork _uow;
    private readonly IAuditRepository _audit;

    public SignConclusionHandler(
        IParaclinicalRepository paraclinical, IExamRecordRepository records,
        ISignStepMapRepository map, ICertificateGateway certificates, IPdfSigner signer,
        IExamFileStore store, IHisEmrClient his, IUnitOfWork uow, IAuditRepository audit)
    {
        _paraclinical = paraclinical;
        _records = records;
        _map = map;
        _certificates = certificates;
        _signer = signer;
        _store = store;
        _his = his;
        _uow = uow;
        _audit = audit;
    }

    public async Task<ApplicationResult<ConclusionSignResult>> HandleAsync(
        SignConclusionCommand command, CancellationToken ct = default)
    {
        var record = await _records.GetAsync(command.DivisionId, command.RecordId, forUpdate: true, ct);
        if (record == null)
            return Fail(ApplicationFailureCode.NotFound, "Không tìm thấy hồ sơ khám", null);

        var mapSteps = await _map.ListAsync(command.DivisionId, record.VariantCode, ct);
        record.SignSteps ??= new List<ExamRecordSignStep>();

        if (record.SignStatus == ExamRecordSignStatus.Signed)
            return ApplicationResult<ConclusionSignResult>.Success(
                Build(record, mapSteps, Array.Empty<int>(), Array.Empty<ConclusionConditionResult>()));

        if (command.ActorKind != ActorKind.Employee
            || !long.TryParse(command.ActorId, out var employeeId) || employeeId <= 0
            || string.IsNullOrWhiteSpace(command.ActorCode))
        {
            return Fail(ApplicationFailureCode.Forbidden, "Chỉ nhân viên mới được ký kết luận", record);
        }

        var (satisfiedB, pendingB, totalB) = await _paraclinical.EvaluateConditionBAsync(
            command.DivisionId, record.RecordID, ct);
        var conditionB = new ConclusionConditionResult("B", "Cận lâm sàng", satisfiedB,
            totalB == 0 ? "Không có chỉ định cận lâm sàng"
                : satisfiedB ? $"{totalB}/{totalB} chỉ định đã trả kết quả hoặc đã huỷ"
                : $"Còn {pendingB} chỉ định CLS chưa trả kết quả",
            "health-exam-server");
        var conditions = new List<ConclusionConditionResult> { conditionB };

        if (!satisfiedB)
            return Fail(ApplicationFailureCode.SignPrecondition,
                "Chưa đủ điều kiện ký kết luận", record, mapSteps, conditions);

        var conclusionStep = mapSteps.FirstOrDefault(m => m.IsConclusionStep);
        if (conclusionStep == null)
            return Fail(ApplicationFailureCode.SignPrecondition,
                "Chưa cấu hình bước ký kết luận cho biểu mẫu này", record, mapSteps, conditions);

        if (command.RoleIds == null || !command.RoleIds.Contains(conclusionStep.SWRoleID))
            return Fail(ApplicationFailureCode.Forbidden,
                "Bạn không có vai trò ký kết luận", record, mapSteps, conditions);

        var missing = mapSteps
            .Where(m => !m.IsConclusionStep && record.SignSteps.All(s => s.SWStep != m.SWStep))
            .Select(m => m.SWStep).ToList();
        if (missing.Count > 0)
            return Fail(ApplicationFailureCode.SignPrecondition,
                $"Còn {missing.Count} mục khám chưa ký số", record, mapSteps, conditions, missing);

        var cert = await _certificates.GetAsync(command.ActorCode, ct);
        if (!cert.Found)
            return Fail(ApplicationFailureCode.SignPrecondition, cert.Message, record, mapSteps, conditions);

        var now = DateTime.UtcNow;
        var conclusionSnapshot = record.SignSteps.FirstOrDefault(s => s.SWStep == conclusionStep.SWStep);
        if (conclusionSnapshot == null)
        {
            conclusionSnapshot = new ExamRecordSignStep
            {
                DivisionID = command.DivisionId,
                RecordID = record.RecordID,
                VariantCode = record.VariantCode,
                CreatedDate = now
            };
            record.SignSteps.Add(conclusionSnapshot);
        }
        conclusionSnapshot.ItemGroupID = null;
        conclusionSnapshot.SWStep = conclusionStep.SWStep;
        conclusionSnapshot.StepName = conclusionStep.StepName;
        conclusionSnapshot.SWRoleID = conclusionStep.SWRoleID;
        conclusionSnapshot.Status = ExamRecordSignStepStatus.Snapshot;
        conclusionSnapshot.SignedByEmployeeID = employeeId;
        conclusionSnapshot.SignedByEmployeeCode = command.ActorCode;
        conclusionSnapshot.SignedAt = now;
        conclusionSnapshot.ModifiedDate = now;
        await _uow.SaveChangesAsync(ct);

        if (!record.HisEmrDataID.HasValue || record.HisEmrDataID == Guid.Empty)
            return Fail(ApplicationFailureCode.SignPrecondition,
                "Hồ sơ chưa có biểu mẫu trên HIS để render PDF", record, mapSteps, conditions);

        var hisRequest = new HisRequest("", "GET", command.Credential,
            TraceId: command.TraceId, DivisionId: command.DivisionId);
        var pdf = await _his.RenderFormPdfAsync(record.HisEmrDataID.Value, hisRequest, ct);
        if (!pdf.IsSuccess)
            return Fail(ApplicationFailureCode.HisBadGateway, pdf.Message, record, mapSteps, conditions);

        var bytes = pdf.Value;
        foreach (var step in mapSteps.OrderBy(m => m.SWStep))
        {
            var snap = record.SignSteps.First(s => s.SWStep == step.SWStep);
            var stepCert = await _certificates.GetAsync(snap.SignedByEmployeeCode, ct);
            if (!stepCert.Found)
                return Fail(ApplicationFailureCode.SignPrecondition,
                    $"Bước \"{step.StepName}\": {stepCert.Message}", record, mapSteps, conditions);

            var outcome = await _signer.SignAsync(new PdfSignRequest(
                bytes, stepCert.Certificate, snap.StepName,
                (snap.SignedAt ?? now).ToLocalTime(),
                step.SignTitle, step.SignType, step.SLType, step.SearchPattern,
                step.SLPage, step.SLX, step.SLY), ct);

            if (!outcome.Succeeded)
                return Fail(ApplicationFailureCode.HisBadGateway,
                    $"Bước \"{step.StepName}\": {outcome.Message}", record, mapSteps, conditions);

            bytes = outcome.Pdf;
        }

        var objectPath = Storage.ConclusionPdfPath(command.DivisionId, now, record.RecordID);
        if (!await _store.UploadPdfAsync(objectPath, bytes, ct))
        {
            record.SignStatus = ExamRecordSignStatus.Failed;
            await _uow.SaveChangesAsync(ct);
            return Fail(ApplicationFailureCode.HisBadGateway,
                "Ký xong nhưng không lưu được file, vui lòng ký lại", record, mapSteps, conditions);
        }

        record.SignedFilePath = objectPath;
        record.SignStatus = ExamRecordSignStatus.Signed;
        record.HisSignedByEmployeeID = employeeId;
        record.HisSignedAt = now;
        foreach (var s in record.SignSteps)
        {
            s.Status = ExamRecordSignStepStatus.Signed;
            s.ModifiedDate = now;
        }

        _audit.Add(new AuditEntry(command.DivisionId, AuditEntityTypes.Record, record.RecordID,
            AuditActions.StateChange, command.ActorId, null, null,
            new { Action = "CONCLUSION_SIGN_COMPLETED", FilePath = objectPath, Steps = mapSteps.Count },
            command.ActorKind));
        await _uow.SaveChangesAsync(ct);

        return ApplicationResult<ConclusionSignResult>.Success(
            Build(record, mapSteps, Array.Empty<int>(), conditions));
    }

    private static ConclusionSignResult Build(
        ExamRecord record,
        IReadOnlyList<Domain.ExamForms.SignStepMap> mapSteps,
        IReadOnlyList<int> missing,
        IReadOnlyList<ConclusionConditionResult> conditions)
    {
        var steps = mapSteps.Select(m =>
        {
            var snap = record.SignSteps?.FirstOrDefault(s => s.SWStep == m.SWStep);
            return new ConclusionSignStepResult(
                m.SWStep, m.StepName, m.ItemGroupID, m.SWRoleID,
                snap?.Status ?? "", snap?.SignedByEmployeeID, snap?.SignedAt);
        }).ToList();

        return new ConclusionSignResult(
            record.RecordID, record.SignStatus, record.SignedFilePath,
            record.HisSignedByEmployeeID, record.HisSignedAt, conditions, steps, missing);
    }

    private static ApplicationResult<ConclusionSignResult> Fail(
        ApplicationFailureCode code, string message, ExamRecord record,
        IReadOnlyList<Domain.ExamForms.SignStepMap> mapSteps = null,
        IReadOnlyList<ConclusionConditionResult> conditions = null,
        IReadOnlyList<int> missing = null)
    {
        var payload = record == null ? null : Build(
            record,
            mapSteps ?? Array.Empty<Domain.ExamForms.SignStepMap>(),
            missing ?? Array.Empty<int>(),
            conditions ?? Array.Empty<ConclusionConditionResult>());
        return ApplicationResult<ConclusionSignResult>.Fail(code, message, payload);
    }

    private static class Storage
    {
        public static string ConclusionPdfPath(string divisionId, DateTime nowUtc, Guid recordId)
            => $"{divisionId}/{nowUtc:yyyy}/{nowUtc:MM}/{recordId}.pdf";
    }
}
```

Quy tắc đường dẫn lặp ở đây vì `ExamFileStorePaths` nằm trong Infrastructure mà Application không được tham chiếu. Hai chỗ phải giống nhau; `ExamFileStorePathTests` canh đúng chuỗi này.

Cập nhật `ConclusionSignResult` trong `ParaclinicalModels.cs`:

```csharp
public sealed record ConclusionSignResult(
    Guid RecordID,
    string Status,
    string SignedFilePath,
    long? SignedByEmployeeID,
    DateTime? SignedAt,
    IReadOnlyList<ConclusionConditionResult> Conditions,
    IReadOnlyList<ConclusionSignStepResult> Steps,
    IReadOnlyList<int> MissingSteps);
```

- [ ] **Step 5: Chạy test để chắc chắn nó pass**

Chạy: `TZ=Asia/Ho_Chi_Minh dotnet test HealthExamServer.sln --filter ConclusionSigningTests`
Kỳ vọng: PASS, 6 test.

- [ ] **Step 6: Xóa hai test đã Skip**

Trong `HealthExam.Tests/Application/ParaclinicalHandlerTests.cs`, xóa `SignConclusion_submits_rendered_pdf_directly_without_medical_process`, `SignConclusion_fails_with_sign_precondition_when_conditions_not_satisfied` và lớp `FakeDirectPdfSigningClient`. `ConclusionSigningTests` đã phủ hết.

- [ ] **Step 7: Chạy toàn bộ test**

Chạy: `TZ=Asia/Ho_Chi_Minh dotnet test HealthExamServer.sln`
Kỳ vọng: toàn bộ xanh, không còn test nào `Skip`.

- [ ] **Step 8: Commit**

```bash
git add HealthExam.Application/Paraclinical/ HealthExam.Tests/
git commit -m "feat(signing): sign conclusion pdf through sign-server

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

## Task 10: Xem PDF kết luận

**Files:**
- Modify: `HealthExam.Application/RegistrationForms/PreviewRegistrationFormPdf.cs`
- Test: `HealthExam.Tests/Signing/ConclusionPdfPreviewTests.cs`

**Interfaces:**
- Consumes: `IExamFileStore` (Task 5), `record.SignedFilePath` (Task 2)
- Produces: không có API mới

- [ ] **Step 1: Viết test thất bại**

Tạo `HealthExam.Tests/Signing/ConclusionPdfPreviewTests.cs`:

```csharp
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using HealthExam.Domain.ExamRecords;
using Xunit;

namespace HealthExam.Tests.Signing;

public class ConclusionPdfPreviewTests
{
    /// <summary>
    /// Hồ sơ đã ký thì luôn trả bản đã ký. Trả bản nháp render lại là phát ra một tài liệu
    /// KHÔNG có chữ ký nào mà người đọc tưởng là bản chính thức.
    /// </summary>
    [Fact]
    public async Task Da_ky_thi_tra_file_tu_MinIO_chu_khong_render_lai()
    {
        var f = new ConclusionFixture();
        await f.SignAllSections();
        await f.SignConclusion();
        var renderCountAfterSign = f.His.RenderCount;

        var pdf = await f.Db.PreviewConclusionPdf(f.Store, f.His)
            .HandleAsync(f.Record.RecordID, "DIV01", "Bearer t");

        Assert.True(pdf.IsSuccess);
        Assert.Equal(f.Store.Objects.Values.Single(), pdf.Value);
        Assert.Equal(renderCountAfterSign, f.His.RenderCount);
    }

    /// <summary>
    /// Đã Signed mà file biến mất khỏi MinIO là sự cố dữ liệu — phải báo lỗi, tuyệt đối
    /// không âm thầm thay bằng bản nháp.
    /// </summary>
    [Fact]
    public async Task Da_ky_nhung_mat_file_thi_bao_loi_chu_khong_tra_ban_nhap()
    {
        var f = new ConclusionFixture();
        await f.SignAllSections();
        await f.SignConclusion();
        f.Store.Objects.Clear();

        var pdf = await f.Db.PreviewConclusionPdf(f.Store, f.His)
            .HandleAsync(f.Record.RecordID, "DIV01", "Bearer t");

        Assert.False(pdf.IsSuccess);
    }

    [Fact]
    public async Task Chua_ky_thi_tra_ban_nhap_render_tu_HIS()
    {
        var f = new ConclusionFixture();

        var pdf = await f.Db.PreviewConclusionPdf(f.Store, f.His)
            .HandleAsync(f.Record.RecordID, "DIV01", "Bearer t");

        Assert.True(pdf.IsSuccess);
        Assert.Equal(Encoding.ASCII.GetBytes("%PDF-1.7 draft"), pdf.Value);
    }
}
```

- [ ] **Step 2: Chạy test để chắc chắn nó fail**

Chạy: `TZ=Asia/Ho_Chi_Minh dotnet test HealthExamServer.sln --filter ConclusionPdfPreviewTests`
Kỳ vọng: FAIL, helper `PreviewConclusionPdf` chưa tồn tại.

- [ ] **Step 3: Sửa handler preview**

Trong `HealthExam.Application/RegistrationForms/PreviewRegistrationFormPdf.cs`:

- thêm `IExamFileStore _store` vào constructor
- thay khối dòng 49-56 và 78-80:

```csharp
        if (record.SignStatus == ExamRecordSignStatus.Signed)
        {
            if (string.IsNullOrWhiteSpace(record.SignedFilePath))
                return ApplicationResult<byte[]>.Fail(
                    ApplicationFailureCode.NotFound,
                    "Hồ sơ đã ký nhưng chưa có đường dẫn file");

            var signed = await _store.DownloadAsync(record.SignedFilePath, ct);
            if (signed == null || signed.Length == 0)
                return ApplicationResult<byte[]>.Fail(
                    ApplicationFailureCode.NotFound,
                    "Không đọc được file đã ký, vui lòng báo quản trị");

            return ApplicationResult<byte[]>.Success(signed);
        }
```

Bỏ mọi lời gọi `_client.ViewSignedFileAsync`.

Thêm helper vào `InMemoryTestDb.cs`:

```csharp
    public HealthExam.Application.RegistrationForms.PreviewRegistrationFormPdfHandler PreviewConclusionPdf(
        HealthExam.Application.Integrations.IExamFileStore store,
        HealthExam.Application.Integrations.IHisEmrClient his)
        => new(new ExamRecordRepository(Db), his, store);
```

Điều chỉnh thứ tự tham số cho khớp constructor thật của handler.

- [ ] **Step 4: Chạy test để chắc chắn nó pass**

Chạy: `TZ=Asia/Ho_Chi_Minh dotnet test HealthExamServer.sln --filter ConclusionPdfPreviewTests`
Kỳ vọng: PASS, 3 test.

- [ ] **Step 5: Commit**

```bash
git add HealthExam.Application/RegistrationForms/PreviewRegistrationFormPdf.cs HealthExam.Tests/
git commit -m "feat(signing): serve signed conclusion pdf from minio

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

## Task 11: Dọn code ký cũ của HIS

**Files:**
- Delete: `HealthExam.Application/His/SubmitHisSection.cs`, `SignHisSection.cs`, `CancelHisSectionSignature.cs`, `GetHisSectionSigningContexts.cs`, `HisSectionSigningContextResolver.cs`, `GetSignWorkflow.cs`
- Modify: `HealthExam.API/Controllers/ExamRecordHisFormController.cs:219-320`
- Modify: `HealthExam.Application/Integrations/IHisEmrClient.cs`
- Modify: `HealthExam.Infrastructure/Integrations/HisEmr/HisEmrClient.cs`
- Modify: `HealthExam.API/Extensions/ApplicationServiceExtensions.cs`
- Modify: `.env.example`
- Test: `HealthExam.Tests/Architecture/DependencyRuleTests.cs`

**Interfaces:**
- Consumes: không
- Produces: không

- [ ] **Step 1: Viết test thất bại**

Thêm vào `HealthExam.Tests/Architecture/DependencyRuleTests.cs`:

```csharp
    /// <summary>
    /// Luồng ký phải sạch bóng SWT của HIS. Còn sót một lời gọi là còn một đường tạo hồ sơ ký
    /// chờ vĩnh viễn trên HIS mà không ai theo dõi.
    /// </summary>
    [Fact]
    public void Khong_con_route_ky_nao_cua_his_server()
    {
        var root = FindRepoRoot();
        var banned = new[] { "M02F30000/SubmitFile", "M02F30000/GetFileSign", "M02F30000/SignFiles",
                             "M02F01500/SubmitEMR", "M02F01500/SignEMR", "M02F01500/CancleEMR",
                             "M02F01500/RSWByDocTypeID", "api/Util/CertInfo", "api/Sign/ViewFile" };

        var files = Directory.GetFiles(Path.Combine(root, "HealthExam.Application"), "*.cs", SearchOption.AllDirectories)
            .Concat(Directory.GetFiles(Path.Combine(root, "HealthExam.Infrastructure"), "*.cs", SearchOption.AllDirectories))
            .Concat(Directory.GetFiles(Path.Combine(root, "HealthExam.API"), "*.cs", SearchOption.AllDirectories))
            .Where(f => !f.Contains("/Migrations/"));

        foreach (var file in files)
        {
            var text = File.ReadAllText(file);
            foreach (var route in banned)
                Assert.False(text.Contains(route, StringComparison.Ordinal),
                    $"{Path.GetFileName(file)} còn tham chiếu {route}");
        }
    }
```

Dùng lại hàm tìm thư mục gốc mà các test khác trong file đang dùng; nếu chưa có, thêm hàm `FindRepoRoot()` đi ngược từ `AppContext.BaseDirectory` tới thư mục chứa `HealthExamServer.sln`.

- [ ] **Step 2: Chạy test để chắc chắn nó fail**

Chạy: `TZ=Asia/Ho_Chi_Minh dotnet test HealthExamServer.sln --filter DependencyRuleTests`
Kỳ vọng: FAIL, liệt kê `HisEmrClient.cs` và `ExamRecordHisFormController.cs`.

- [ ] **Step 3: Xóa handler ký theo section**

```bash
git rm HealthExam.Application/His/SubmitHisSection.cs \
       HealthExam.Application/His/SignHisSection.cs \
       HealthExam.Application/His/CancelHisSectionSignature.cs \
       HealthExam.Application/His/GetHisSectionSigningContexts.cs \
       HealthExam.Application/His/HisSectionSigningContextResolver.cs \
       HealthExam.Application/His/GetSignWorkflow.cs
```

- [ ] **Step 4: Xóa endpoint và đăng ký DI**

Trong `ExamRecordHisFormController.cs`, xóa 3 action `sections/{sectionKey}/submit`, `sections/{sectionKey}/sign`, `sections/{sectionKey}/sign/cancel` (dòng 219-320) và các field/tham số constructor chỉ phục vụ chúng. Giữ nguyên `processes/{processId}/sign-workflow` nếu FE đang dùng để hiển thị; nếu không, xóa luôn.

Trong `ApplicationServiceExtensions.cs`, xóa các dòng `AddScoped` cho 6 handler vừa xóa.

- [ ] **Step 5: Xóa các hàm ký trong HIS client**

Trong `HealthExam.Application/Integrations/IHisEmrClient.cs`, xóa khai báo: `GetPdfSignWorkflowAsync`, `GetPendingPdfSignStepsAsync`, `SubmitAndSignPdfAsync`, `SubmitPdfForSigningAsync`, `SignPdfStepAsync`, `ViewSignedFileAsync`, và các record `HisPdfSignRequest`, `HisPdfSignResult`, `HisPdfSignStepRequest`, `HisPdfSignStepResult`, `HisPdfSignWorkflow`, `HisPdfSignWorkflowStep`, `HisPendingPdfSignStep`.

Trong `HisEmrClient.cs`, xóa phần cài đặt tương ứng cùng các hằng `RouteGetFileSign`, `RouteSignFiles`, `RouteSubmitFile`, `RouteViewFile`, `RouteRSWByDocTypeId`, `RouteSubmitEMR`, `RouteSignEMR`, `RouteCancleEMR`, và lớp `SubmitFileData`. Xóa các giá trị `HisOperation` không còn dùng: `GetSignWorkflow`, `SubmitSection`, `SignSection`, `CancelSectionSignature`.

- [ ] **Step 6: Cập nhật .env.example**

Xóa dòng `HIS_EMR_SECTION_SIGNING_ENABLED=false`. Thêm:

```
# Ký số KSK — sign-server + ssm-server + MinIO
SSM_BASE_URL=http://ssm-server:9930
SIGN_SERVER_BASE_URL=http://sign-server:9940
SIGN_SERVER_TIMEOUT_SECONDS=60
MINIO_ENDPOINT=minio:9000
MINIO_ACCESS_KEY=
MINIO_SECRET_KEY=
MINIO_BUCKET_HEALTH_EXAM=health-exam
MINIO_USE_SSL=false
```

Cập nhật cùng nội dung vào `Deploy/` nếu ở đó có file biến môi trường.

- [ ] **Step 7: Chạy toàn bộ test**

Chạy: `TZ=Asia/Ho_Chi_Minh dotnet test HealthExamServer.sln`
Kỳ vọng: toàn bộ xanh, gồm cả test mới ở Step 1. Nếu `HisEmrClientTests.cs` có test cho các hàm vừa xóa thì xóa luôn test đó.

- [ ] **Step 8: Commit**

```bash
git add -A
git commit -m "refactor(signing): remove his-server signing workflow integration

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

## Task 12: Frontend — khóa mục đã ký và hiển thị bước

**Files:**
- Create: `frontend/turbo-web/packages/api/src/health-exam/health-exam-signing-service.ts`
- Create: `frontend/turbo-web/packages/api/test/health-exam/health-exam-signing-service.test.ts`
- Modify: màn hình khám lâm sàng và tab kết luận trong app KSK

**Interfaces:**
- Consumes: 4 endpoint của Task 6, 7, 8, 9
- Produces:
  - `signExamSection(recordId: string, itemGroupId: number)`
  - `cancelExamSectionSign(recordId: string, itemGroupId: number)`
  - `getConclusionEligibility(recordId: string)`
  - `signConclusion(recordId: string)`

- [ ] **Step 1: Xác định file màn hình cần sửa**

```bash
cd frontend/turbo-web
grep -rn "Ký số" --include=*.tsx apps packages | head -20
```

Ghi lại đường dẫn component danh mục khám và component tab kết luận. Hai file đó là đích sửa ở Step 5 và 6.

- [ ] **Step 2: Viết test thất bại**

Tạo `packages/api/test/health-exam/health-exam-signing-service.test.ts`, theo đúng khuôn của `health-exam-his-form-service.test.ts` đang có:

```typescript
import { describe, expect, it, vi } from 'vitest'
import { createHealthExamSigningService } from '../../src/health-exam/health-exam-signing-service'

describe('health-exam signing service', () => {
  it('ký mục khám gọi đúng đường dẫn và không cần body', async () => {
    const post = vi.fn().mockResolvedValue({ ErrorCode: 0, Data: { SwStep: 1 } })
    const service = createHealthExamSigningService({ post } as never)

    await service.signExamSection('rec-1', 101)

    expect(post).toHaveBeenCalledWith('/v1/exam-records/rec-1/sections/101/sign', undefined)
  })

  it('hủy ký mục khám gọi đúng đường dẫn', async () => {
    const post = vi.fn().mockResolvedValue({ ErrorCode: 0, Data: true })
    const service = createHealthExamSigningService({ post } as never)

    await service.cancelExamSectionSign('rec-1', 101)

    expect(post).toHaveBeenCalledWith('/v1/exam-records/rec-1/sections/101/sign/cancel', undefined)
  })
})
```

- [ ] **Step 3: Chạy test để chắc chắn nó fail**

Chạy: `cd frontend/turbo-web && pnpm --filter @dhsc/api test health-exam-signing-service`
Kỳ vọng: FAIL, module chưa tồn tại. Nếu tên package khác `@dhsc/api`, dùng tên thật trong `packages/api/package.json`.

- [ ] **Step 4: Tạo service**

Tạo `packages/api/src/health-exam/health-exam-signing-service.ts`:

```typescript
import type { HealthExamHttpClient } from './envelope'

export interface ConclusionSignStep {
  SwStep: number
  StepName: string
  ItemGroupID: number | null
  SwRoleId: number
  Status: '' | 'Snapshot' | 'Signed' | 'Failed'
  SignedByEmployeeID: number | null
  SignedAt: string | null
}

export interface ConclusionEligibility {
  CanSignConclusion: boolean
  MissingSteps: number[]
  Steps: ConclusionSignStep[]
}

export function createHealthExamSigningService(http: HealthExamHttpClient) {
  return {
    signExamSection: (recordId: string, itemGroupId: number) =>
      http.post(`/v1/exam-records/${recordId}/sections/${itemGroupId}/sign`, undefined),

    cancelExamSectionSign: (recordId: string, itemGroupId: number) =>
      http.post(`/v1/exam-records/${recordId}/sections/${itemGroupId}/sign/cancel`, undefined),

    getConclusionEligibility: (recordId: string) =>
      http.get<ConclusionEligibility>(`/v1/exam-records/${recordId}/conclusion-eligibility`),

    signConclusion: (recordId: string) =>
      http.post(`/v1/exam-records/${recordId}/conclusion/sign`, undefined),
  }
}
```

Điều chỉnh kiểu `HealthExamHttpClient` cho khớp client thật trong `envelope.ts`.

- [ ] **Step 5: Chạy test để chắc chắn nó pass**

Chạy: `cd frontend/turbo-web && pnpm --filter @dhsc/api test health-exam-signing-service`
Kỳ vọng: PASS, 2 test.

- [ ] **Step 6: Khóa mục khám đã ký**

Trong component danh mục khám (tìm được ở Step 1):

- đọc `Steps` từ `getConclusionEligibility`, lập map `ItemGroupID → step`
- mục có `Status === 'Snapshot'` hoặc `'Signed'`: ẩn nút **Chỉnh sửa** và **Xóa**, hiện dòng `Người thực hiện` / `Ký số` bằng `SignedByEmployeeID` và `SignedAt`
- mục đã ký hiện thêm nút **Hủy ký** gọi `cancelExamSectionSign`, sau đó refetch eligibility
- nút **Ký số** gọi `signExamSection`, sau đó refetch eligibility
- lỗi trả về hiện nguyên `Message` của server — các thông báo đã viết sẵn tiếng Việt và nêu đúng lý do (thiếu chứng thư số, sai vai trò, chưa cấu hình bước ký)

- [ ] **Step 7: Hiển thị bước ký ở tab Kết luận**

Trong component tab kết luận:

- liệt kê `Steps` theo `SwStep`: tên bước, trạng thái, người ký, thời điểm
- bước thuộc `MissingSteps` tô khác màu kèm chữ "Chưa ký"
- nút **Ký kết luận** `disabled` khi `CanSignConclusion === false`, tooltip nêu số bước còn thiếu
- bấm ký gọi `signConclusion`, thành công thì refetch và mở PDF đã ký

- [ ] **Step 8: Kiểm tra bằng tay**

Chạy FE trỏ vào health-exam-server local. Trên một hồ sơ thật:

1. Ký mục "Khám thể lực" → nút Chỉnh sửa/Xóa biến mất, hiện người ký và giờ ký
2. Bấm Hủy ký → mục mở khóa lại
3. Ký đủ mọi mục → nút Ký kết luận sáng
4. Bấm Ký kết luận → PDF trả về có đủ chữ ký, đúng vị trí marker

- [ ] **Step 9: Commit**

```bash
cd frontend/turbo-web
git add packages/api/src/health-exam/health-exam-signing-service.ts \
        packages/api/test/health-exam/health-exam-signing-service.test.ts \
        apps
git commit -m "feat(health-exam): lock signed exam sections and show sign steps

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

## Sau khi xong

Trước khi mở MR, chạy đối chiếu bảng map với biểu mẫu thật — đây là loại lỗi chỉ lộ ra lúc ký kết luận:

```bash
# Render PDF nháp của một hồ sơ, rồi với từng SearchPattern trong HEX_SignStepMap:
curl -F "file=@draft.pdf" -F "text=##{S1}##" http://sign-server:9940/api/File/DetectPDF
```

Mọi marker đã khai trong bảng map phải được `DetectPDF` tìm thấy. Marker nào không có trong `.repx` thì sửa cấu hình hoặc sửa biểu mẫu trước khi triển khai.
