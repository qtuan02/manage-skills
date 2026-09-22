using System;
using System.Linq;
using System.Threading.Tasks;
using HealthExam.Application.Common;
using HealthExam.Application.Paraclinical;
using HealthExam.Domain.Common;
using HealthExam.Domain.ExamRecords;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HealthExam.Tests.Signing;

public class ConclusionSigningTests
{
    /// <summary>
    /// Lõi của thiết kế: render PDF MỘT lần, ký nối chuỗi trong bộ nhớ qua từng marker,
    /// ghi MinIO đúng một lần ở cuối, và có dòng audit cho lượt ký.
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
        // Nối chuỗi thật: FakePdfSigner nối thêm 1 byte mỗi lượt, nên bước 3 nhận PDF đã qua 2 lượt
        // ký và file lưu là bản đã qua cả 3 — không phải bản render gốc.
        Assert.Equal(f.Signer.Requests[0].Pdf.Length + 2, f.Signer.Requests[2].Pdf.Length);
        Assert.Equal(f.Signer.Requests[0].Pdf.Length + 3, f.Store.Objects.Single().Value.Length);
        Assert.Contains(f.Db.AuditRowsOf(f.Record.RecordID),
            x => (x.Payload ?? "").Contains("CONCLUSION_SIGN_COMPLETED"));

        // Người kết luận vừa là người ký (SignedBy*) vừa là người thực hiện (PerformedBy*) —
        // khác các bước mục khám nơi hai vai có thể tách rời (ký thay, PROJ-2374).
        var conclusionSnapshot = f.Db.RecordOf(f.Record.RecordID).SignSteps.Single(s => s.SWStep == 3);
        Assert.Equal(1009, conclusionSnapshot.PerformedByEmployeeID);
        Assert.Equal("BS KL", conclusionSnapshot.PerformedByEmployeeName);
    }

    /// <summary>
    /// Mỗi marker phải ký bằng chứng thư của ĐÚNG bác sĩ đã bấm mục đó, và in ngày lúc họ bấm.
    /// Ký hộ bằng cert người kết luận là làm hỏng ý nghĩa cả quy trình.
    /// Snapshot bước 1 được lùi hẳn một ngày để phân biệt "ngày bấm ký" với "ngày chạy ký".
    /// </summary>
    [Fact]
    public async Task Moi_buoc_dung_chung_thu_va_ngay_cua_bac_si_buoc_do()
    {
        var step1SignedAt = DateTime.UtcNow.AddDays(-1);
        TzGuard.RequireLocalOffsetKhacUtc(step1SignedAt);
        var f = new ConclusionFixture();
        await f.SignSection(101, "NV001", 1001);
        await f.SignSection(102, "NV002", 1002);
        f.BackdateSnapshot(swStep: 1, signedAtUtc: step1SignedAt);

        var res = await f.SignConclusion(employeeCode: "NV009", employeeId: 1009);

        Assert.True(res.IsSuccess);
        Assert.Equal("NV001", f.Signer.Requests[0].Certificate.EmployeeCode);
        Assert.Equal("NV002", f.Signer.Requests[1].Certificate.EmployeeCode);
        Assert.Equal("NV009", f.Signer.Requests[2].Certificate.EmployeeCode);
        // R1: tên in cạnh chữ ký là tên bác sĩ trong snapshot, không phải tên bước.
        Assert.Equal("BS", f.Signer.Requests[0].EmployeeName);
        Assert.Equal("BS KL", f.Signer.Requests[2].EmployeeName);

        var snapshotTime = f.Db.RecordOf(f.Record.RecordID).SignSteps
            .Single(s => s.SWStep == 1).SignedAt!.Value;
        Assert.Equal(step1SignedAt.ToString("yyyy-MM-dd HH:mm"), snapshotTime.ToString("yyyy-MM-dd HH:mm"));
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
        var steps = f.Db.RecordOf(f.Record.RecordID).SignSteps.OrderBy(s => s.SWStep).ToList();
        Assert.Equal(3, steps.Count);
        Assert.Equal(ExamRecordSignStepStatus.Signed, steps[0].Status);
        Assert.Equal(ExamRecordSignStepStatus.Signed, steps[1].Status);
        Assert.Equal(ExamRecordSignStepStatus.Snapshot, steps[2].Status);
    }

    [Fact]
    public async Task Step_dang_in_progress_thi_tu_choi_ky_ket_luan_va_khong_goi_sign_server()
    {
        var f = new ConclusionFixture();
        // Section 101 is signed, section 102 is only in-progress
        await f.SignSection(101, "NV001", 1001);
        f.Db.Db.Set<ExamRecordSignStep>().Add(new ExamRecordSignStep
        {
            ID = Guid.NewGuid(),
            DivisionID = ConclusionFixture.DivisionId,
            RecordID = f.Record.RecordID,
            VariantCode = ConclusionFixture.VariantCode,
            ItemGroupID = 102,
            SWStep = 2,
            StepName = "Bước 2",
            SWRoleID = ConclusionFixture.ExamRoleId,
            Status = ExamRecordSignStepStatus.InProgress,
            CreatedDate = DateTime.UtcNow,
            ModifiedDate = DateTime.UtcNow
        });
        f.Db.Db.SaveChanges();
        f.Db.Db.ChangeTracker.Clear();

        var res = await f.SignConclusion();

        Assert.False(res.IsSuccess);
        Assert.Equal(ApplicationFailureCode.SignPrecondition, res.Failure.Code);
        Assert.Contains("Còn 1 mục khám chưa ký số", res.Failure.Message);
        Assert.Empty(f.Signer.Requests);
        Assert.Equal(0, f.His.RenderCount);
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
    /// Xoá vết theo dõi giữa hai lượt để chốt short-circuit đọc từ kho, không phải từ identity map.
    /// </summary>
    [Fact]
    public async Task Goi_lai_sau_khi_da_ky_khong_ky_them_lan_nua()
    {
        var f = new ConclusionFixture();
        await f.SignAllSections();
        await f.SignConclusion();
        var countAfterFirst = f.Signer.Requests.Count;
        f.Db.Db.ChangeTracker.Clear();

        var res = await f.SignConclusion();

        Assert.True(res.IsSuccess);
        Assert.Equal(ExamRecordSignStatus.Signed, res.Value.Status);
        Assert.Equal(countAfterFirst, f.Signer.Requests.Count);
        Assert.Equal(1, f.His.RenderCount);
        Assert.Single(f.Store.Objects);
    }

    /// <summary>
    /// VariantCode của hồ sơ đổi được; snapshot của bộ biểu mẫu cũ không được coi là "đã ký"
    /// cho bộ hiện tại — nếu không, đổi biểu mẫu là qua mặt được toàn bộ khâu ký mục khám.
    /// </summary>
    [Fact]
    public async Task Snapshot_cua_variant_khac_khong_duoc_tinh()
    {
        var f = new ConclusionFixture();
        f.SeedForeignSnapshot(swStep: 1, variantCode: "KSK-KHAC");
        await f.SignSection(102, "NV002", 1002);

        var res = await f.SignConclusion();

        Assert.False(res.IsSuccess);
        Assert.Equal(ApplicationFailureCode.SignPrecondition, res.Failure.Code);
        var payload = Assert.IsType<ConclusionSignResult>(res.Failure.Payload);
        Assert.Equal(new[] { 1 }, payload.MissingSteps.ToArray());
        Assert.Empty(f.Signer.Requests);
        Assert.Equal(0, f.His.RenderCount);
    }

    /// <summary>
    /// Khoá hàng theo RecordID không lọc tenant — phải đối chiếu DivisionID sau khi khoá, nếu
    /// không hồ sơ của đơn vị khác ký được bằng cách đoán RecordID.
    /// </summary>
    [Fact]
    public async Task Sai_DivisionId_thi_NotFound_va_khong_cham_cong_ngoai()
    {
        var f = new ConclusionFixture();
        await f.SignAllSections();

        var res = await f.SignConclusion(divisionId: "DIV-KHAC");

        Assert.False(res.IsSuccess);
        Assert.Equal(ApplicationFailureCode.NotFound, res.Failure.Code);
        Assert.Empty(f.Signer.Requests);
        Assert.Equal(0, f.His.RenderCount);
        Assert.Equal(ExamRecordSignStatus.New, f.Db.RecordOf(f.Record.RecordID).SignStatus);
    }

    /// <summary>
    /// R11: nửa "ký kết luận" của test HIS-era đã xoá — sau khi ký xong, eligibility phải phản
    /// ánh mọi bước đã Signed, không thiếu bước nào, và nút ký phải xám.
    /// </summary>
    [Fact]
    public async Task Sau_khi_ky_ket_luan_eligibility_bao_da_ky_va_khong_cho_ky_nua()
    {
        var f = new ConclusionFixture();
        await f.SignAllSections();
        var signed = await f.SignConclusion();
        Assert.True(signed.IsSuccess);
        f.Db.Db.ChangeTracker.Clear();

        var res = await f.Db.ConclusionEligibility(f.Map).HandleAsync(
            new GetConclusionEligibilityQuery(
                ConclusionFixture.DivisionId, f.Record.RecordID, "Bearer t", "TRACE", "1009",
                ActorKind.Employee, RoleIds: new[] { ConclusionFixture.ConclusionRoleId }));

        Assert.True(res.IsSuccess);
        Assert.False(res.Value.CanSignConclusion);
        Assert.True(res.Value.IsSigned);
        Assert.True(res.Value.CanCancelSign);
        Assert.True(res.Value.IsReadOnly);
        Assert.Equal(3, res.Value.Steps.Count);
        Assert.All(res.Value.Steps, s => Assert.Equal(ExamRecordSignStepStatus.Signed, s.Status));
        Assert.Empty(res.Value.MissingSteps);
        Assert.Equal(1009, res.Value.SignedByEmployeeID);
    }

    [Fact]
    public async Task Ky_ket_luan_chuyen_trang_thai_ho_so_thanh_Completed()
    {
        var f = new ConclusionFixture();
        await f.SignAllSections();

        var res = await f.SignConclusion();

        Assert.True(res.IsSuccess);
        var record = f.Db.RecordOf(f.Record.RecordID);
        Assert.Equal(ExamRecordState.Completed, record.State);
        Assert.NotNull(record.ExamFinishedAt);
        Assert.Equal(ExamRecordSignStatus.Signed, record.SignStatus);
    }

    [Fact]
    public async Task Huy_ky_ket_luan_tra_trang_thai_ho_so_ve_InProgress_va_reset_chu_ky()
    {
        var f = new ConclusionFixture();
        await f.SignAllSections();
        var signed = await f.SignConclusion();
        Assert.True(signed.IsSuccess);

        var cancelHandler = f.Db.CancelConclusionSign(f.Map);
        var res = await cancelHandler.HandleAsync(new CancelConclusionSignCommand(
            ConclusionFixture.DivisionId, "1009", ActorKind.Employee, f.Record.RecordID));

        Assert.True(res.IsSuccess);
        Assert.Equal(ExamRecordSignStatus.New, res.Value.Status);
        Assert.Null(res.Value.SignedFilePath);
        Assert.Null(res.Value.SignedByEmployeeID);
        Assert.Null(res.Value.SignedAt);

        var record = f.Db.RecordOf(f.Record.RecordID);
        Assert.Equal(ExamRecordState.InProgress, record.State);
        Assert.Equal(ExamRecordSignStatus.New, record.SignStatus);
        Assert.Null(record.SignedFilePath);
        Assert.Null(record.HisSignedByEmployeeID);
        Assert.Null(record.HisSignedAt);

        var conclusionStep = record.SignSteps.Single(s => s.SWStep == 3);
        Assert.Equal(ExamRecordSignStepStatus.InProgress, conclusionStep.Status);
        Assert.Null(conclusionStep.SignedByEmployeeID);

        // Audit log phải ghi lại
        Assert.Contains(f.Db.AuditRowsOf(f.Record.RecordID),
            x => (x.Payload ?? "").Contains("CONCLUSION_SIGN_CANCELLED"));
    }

    [Fact]
    public async Task Huy_ky_ket_luan_tu_choi_neu_nguoi_huy_khong_phai_nguoi_da_ky()
    {
        var f = new ConclusionFixture();
        await f.SignAllSections();
        var signed = await f.SignConclusion(employeeCode: "NV009", employeeId: 1009);
        Assert.True(signed.IsSuccess);

        var cancelHandler = f.Db.CancelConclusionSign(f.Map);
        // Thử hủy với actorId khác (1001)
        var res = await cancelHandler.HandleAsync(new CancelConclusionSignCommand(
            ConclusionFixture.DivisionId, "1001", ActorKind.Employee, f.Record.RecordID));

        Assert.False(res.IsSuccess);
        Assert.Equal(ApplicationFailureCode.Forbidden, res.Failure.Code);

        // Record vẫn giữ nguyên Completed & Signed
        var record = f.Db.RecordOf(f.Record.RecordID);
        Assert.Equal(ExamRecordState.Completed, record.State);
        Assert.Equal(ExamRecordSignStatus.Signed, record.SignStatus);
    }

    [Fact]
    public async Task Huy_ky_ket_luan_khi_ho_so_chua_ky_thi_bao_InvalidState()
    {
        var f = new ConclusionFixture();
        await f.SignAllSections();

        var cancelHandler = f.Db.CancelConclusionSign(f.Map);
        var res = await cancelHandler.HandleAsync(new CancelConclusionSignCommand(
            ConclusionFixture.DivisionId, "1009", ActorKind.Employee, f.Record.RecordID));

        Assert.False(res.IsSuccess);
        Assert.Equal(ApplicationFailureCode.InvalidState, res.Failure.Code);
    }
}

internal sealed class ConclusionFixture : IDisposable
{
    public const string DivisionId = "DIV01";
    public const string VariantCode = "KSK06-18T";
    public const long ExamRoleId = 45;
    public const long ConclusionRoleId = 60;

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
        // Seed dưới ĐÚNG DivisionId của fixture (khác mặc định DEV) và gán sẵn HisEmrDataID —
        // thiếu một trong hai thì mọi lượt ký rơi vào NotFound / "chưa có biểu mẫu trên HIS".
        var session = Db.SeedSession(divisionId: DivisionId);
        Record = Db.SeedRecord(session.SessionID, ExamRecordState.InProgress,
            divisionId: DivisionId, hisEmrDataId: Guid.NewGuid());
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
        => Db.SignExamSection(Map, Cert, new FakeSignRoleEmployeesHisClient().Add(ExamRoleId, employeeId, employeeCode, "BS"))
            .HandleAsync(new HealthExam.Application.Signing.SignExamSectionCommand(
                DivisionId, Record.RecordID, itemGroupId, employeeId, employeeCode, "BS",
                ActorKind.Employee, "Bearer t", "trace"));

    public async Task SignAllSections()
    {
        await SignSection(101, "NV001", 1001);
        await SignSection(102, "NV002", 1002);
    }

    /// <summary>Snapshot "lạc" của một bộ biểu mẫu khác trên cùng hồ sơ — ghi thẳng, không qua handler.</summary>
    public void SeedForeignSnapshot(int swStep, string variantCode)
    {
        Db.Db.Set<ExamRecordSignStep>().Add(new ExamRecordSignStep
        {
            ID = Guid.NewGuid(), DivisionID = DivisionId, RecordID = Record.RecordID,
            VariantCode = variantCode, ItemGroupID = 101, SWStep = swStep, StepName = $"Bước {swStep}",
            SWRoleID = ExamRoleId, Status = ExamRecordSignStepStatus.Snapshot,
            SignedByEmployeeID = 1001, SignedByEmployeeCode = "NV001", SignedByEmployeeName = "BS",
            SignedAt = DateTime.UtcNow
        });
        Db.Db.SaveChanges();
        Db.Db.ChangeTracker.Clear();
    }

    /// <summary>Lùi giờ bấm ký của một snapshot — để test phân biệt được ngày bấm với ngày chạy ký.</summary>
    public void BackdateSnapshot(int swStep, DateTime signedAtUtc)
    {
        var snap = Db.Db.Set<ExamRecordSignStep>().AsTracking()
            .Single(s => s.RecordID == Record.RecordID && s.SWStep == swStep);
        snap.SignedAt = signedAtUtc;
        Db.Db.SaveChanges();
        Db.Db.ChangeTracker.Clear();
    }

    public Task<ApplicationResult<ConclusionSignResult>> SignConclusion(
        string employeeCode = "NV009", long employeeId = 1009, string divisionId = DivisionId)
        => Db.SignConclusion(Map, Cert, Signer, Store, His).HandleAsync(
            new SignConclusionCommand(
                divisionId, employeeId.ToString(), "BS KL",
                ActorKind.Employee, Record.RecordID,
                "Bearer t", "TRACE", employeeCode,
                new[] { ConclusionRoleId }, DepartmentId: 458));

    public void Dispose() => Db.Dispose();
}
