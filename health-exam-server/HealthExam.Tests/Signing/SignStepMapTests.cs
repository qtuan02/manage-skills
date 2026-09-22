using System;
using System.Linq;
using System.Threading.Tasks;
using HealthExam.Domain.ExamForms;
using HealthExam.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
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
                SignedByEmployeeName = "BS Nguyen Van A",
                SignedAt = new DateTime(2026, 9, 18, 2, 30, 0, DateTimeKind.Utc),
                Status = HealthExam.Domain.ExamRecords.ExamRecordSignStepStatus.Snapshot
            });
        await db.SaveChangesAsync();

        var row = db.Set<HealthExam.Domain.ExamRecords.ExamRecordSignStep>().Single();
        Assert.Equal(VariantCode, row.VariantCode);
        Assert.Equal(101, row.ItemGroupID);
        Assert.Equal("NV001", row.SignedByEmployeeCode);
        Assert.Equal("BS Nguyen Van A", row.SignedByEmployeeName);
        Assert.Equal("Snapshot", row.Status);
    }

    /// <summary>
    /// Bấm ký hai lần cùng một mục của cùng một hồ sơ/biến thể phải chạm cùng một khoá duy nhất —
    /// nếu không, lượt bấm thứ hai sẽ sinh thêm một chữ ký thay vì bị chặn.
    /// </summary>
    [Fact]
    public void Snapshot_co_unique_theo_ho_so_variant_va_buoc()
    {
        using var db = InMemoryTestDb.CreateContext();
        var entity = db.Model.FindEntityType(typeof(HealthExam.Domain.ExamRecords.ExamRecordSignStep))!;

        var idx = entity.GetIndexes().Single(x =>
            x.Properties.Select(p => p.Name).SequenceEqual(
                new[] { "DivisionID", "RecordID", "VariantCode", "SWStep" }));

        Assert.True(idx.IsUnique);
    }

    /// <summary>
    /// DivisionID của bảng map là khoá tenant — nằm trong cả hai unique index. Cột nullable thì
    /// NULL không so bằng NULL và unique mất tác dụng cho dòng thiếu tenant.
    /// </summary>
    [Fact]
    public void SignStepMap_DivisionID_khong_nullable()
    {
        using var db = InMemoryTestDb.CreateContext();
        var entity = db.Model.FindEntityType(typeof(SignStepMap))!;

        Assert.False(entity.FindProperty(nameof(SignStepMap.DivisionID))!.IsNullable);
    }

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
}
