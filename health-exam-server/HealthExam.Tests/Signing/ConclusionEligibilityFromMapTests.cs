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
        Assert.Equal(ExamRecordSignStepStatus.Signed, steps[0].Status);
        Assert.Equal(1274, steps[0].SignedByEmployeeID);
        Assert.Equal("BS A", steps[0].SignedByEmployeeName);
        Assert.Null(steps[1].SignedByEmployeeID);
    }

    [Fact]
    public async Task Step_dang_in_progress_khong_thoa_man_dieu_kien_a_va_nam_trong_missing_steps()
    {
        var f = new EligibilityFixture();
        // Seed an in-progress step for section 101 and signed step for section 102
        f.Db.Db.Set<ExamRecordSignStep>().Add(new ExamRecordSignStep
        {
            ID = Guid.NewGuid(),
            DivisionID = EligibilityFixture.DivisionId,
            RecordID = f.Record.RecordID,
            VariantCode = EligibilityFixture.VariantCode,
            ItemGroupID = 101,
            SWStep = 1,
            StepName = "Bước 1",
            SWRoleID = 45,
            Status = ExamRecordSignStepStatus.InProgress,
            CreatedDate = DateTime.UtcNow,
            ModifiedDate = DateTime.UtcNow
        });
        f.Db.Db.SaveChanges();
        f.Db.Db.ChangeTracker.Clear();

        await f.SignSection(102);

        var res = await f.Eligibility();

        Assert.False(res.Value.CanSignConclusion);
        var condA = res.Value.Conditions.Single(c => c.Code == "A");
        Assert.False(condA.Satisfied);
        Assert.Equal("1/2 mục khám đã ký số", condA.Detail);
        Assert.Contains(1, res.Value.MissingSteps);
        Assert.DoesNotContain(2, res.Value.MissingSteps);
    }

    /// <summary>Dòng meta FE đọc "Người thực hiện" từ Steps[] — BE phải trả PerformedBy* sau khi ký mục.</summary>
    [Fact]
    public async Task Steps_mang_PerformedBy_sau_khi_ky_muc()
    {
        var f = new EligibilityFixture();
        await f.SignSection(101);

        var res = await f.Eligibility();

        Assert.True(res.IsSuccess);
        var step = res.Value.Steps.Single(s => s.ItemGroupID == 101);
        Assert.Equal(1274, step.PerformedByEmployeeID);
        Assert.Equal("BS A", step.PerformedByEmployeeName);
        Assert.Equal(1274, step.SignedByEmployeeID);
    }
}

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
        // Fixture giữ DivisionId riêng (DIV01) khác mặc định của FakeHealthExamContext (DEV) —
        // phải seed session/record dưới ĐÚNG DivisionId đó, nếu không GetAsync lọc theo
        // DivisionID sẽ không thấy hồ sơ và mọi test ở đây rơi vào NotFound thay vì so đúng kỳ vọng.
        var session = Db.SeedSession(divisionId: DivisionId);
        Record = Db.SeedRecord(session.SessionID, ExamRecordState.InProgress, divisionId: DivisionId);
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
        => Db.SignExamSection(Map, Cert, new FakeSignRoleEmployeesHisClient().Add(ExamRoleId, 1274, "NV001", "BS A"))
            .HandleAsync(new HealthExam.Application.Signing.SignExamSectionCommand(
                DivisionId, Record.RecordID, itemGroupId, 1274, "NV001", "BS A",
                ActorKind.Employee, "Bearer t", "trace"));

    public Task<HealthExam.Application.Common.ApplicationResult<
        HealthExam.Application.Paraclinical.ConclusionEligibilityResult>> Eligibility(
        long[] roleIds = null)
        => Db.ConclusionEligibility(Map).HandleAsync(
            new HealthExam.Application.Paraclinical.GetConclusionEligibilityQuery(
                DivisionId, Record.RecordID, "Bearer t", "TRACE", "1274", ActorKind.Employee,
                RoleIds: roleIds ?? new long[] { ExamRoleId, ConclusionRoleId }));

    public void Dispose() => Db.Dispose();
}
