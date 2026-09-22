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
    private const long ActorId = 1274;
    private const long ConfirmerId = 4210;

    private static SignStepMap Step(int swStep, int? itemGroupId, long roleId = 45, bool conclusion = false) => new()
    {
        ID = Guid.NewGuid(), DivisionID = DivisionId, VariantCode = VariantCode,
        SWStep = swStep, ItemGroupID = itemGroupId, StepName = $"Bước {swStep}",
        SignTitle = $"Bước {swStep}", SWRoleID = roleId, SignType = 1, SLType = 2,
        SearchPattern = $"##{{S{swStep}}}##", IsConclusionStep = conclusion, IsActive = true
    };

    private static SignExamSectionCommand Command(
        Guid recordId, long? confirmedBy = null, DateTime? signedAt = null) => new(
        DivisionId, recordId, ItemGroupId, EmployeeId: ActorId, EmployeeCode: "NV001",
        EmployeeName: "BS A", ActorKind.Employee, Credential: "Bearer t", TraceId: "trace",
        ConfirmedByEmployeeId: confirmedBy, SignedAt: signedAt);

    /// <summary>
    /// Bấm Ký số KHÔNG gọi sign-server. Nó chỉ ghi lại ai ký mục nào lúc nào — PDF còn chưa
    /// tồn tại ở thời điểm này vì các mục khác chưa nhập xong.
    ///
    /// Đảm bảo này là CẤU TRÚC, không phải hành vi: <see cref="SignExamSectionHandler"/> không
    /// có <c>IPdfSigner</c> trong constructor, nên nó không có cách nào gọi sign-server dù có
    /// muốn — không cần (và không thể) chốt bằng assertion trên <c>FakePdfSigner</c> ở đây.
    /// </summary>
    [Fact]
    public async Task Ky_muc_kham_ghi_signed_va_tra_tien_do()
    {
        var (handler, db, record, _, _) = Fixture();

        var res = await handler.HandleAsync(Command(record.RecordID));

        Assert.True(res.IsSuccess);
        Assert.Equal(1, res.Value.SwStep);
        Assert.Equal(ExamRecordSignStepStatus.Signed, res.Value.Status);
        Assert.Equal(1, res.Value.SigningProgressDone);
        Assert.Equal(2, res.Value.SigningProgressTotal);

        var snapshot = db.RecordOf(record.RecordID).SignSteps.Single();
        Assert.Equal(ItemGroupId, snapshot.ItemGroupID);
        Assert.Equal("NV001", snapshot.SignedByEmployeeCode);
        Assert.Equal("BS A", snapshot.SignedByEmployeeName);
        Assert.Equal(ExamRecordSignStepStatus.Signed, snapshot.Status);
    }

    [Fact]
    public async Task Ky_step_dang_in_progress_chuyen_sang_signed_va_tra_tien_do()
    {
        var (handler, db, record, _, _) = Fixture();

        // Seed an InProgress step TRACKED (e.g. created during first form save) — dùng
        // ExamRecordSignSteps.Add + SaveChanges + ChangeTracker.Clear() vì db.RecordOf(...)
        // trả về thực thể AsNoTracking: gán rồi SaveChanges trên nó không ghi gì cả.
        var seededId = Guid.NewGuid();
        db.Db.ExamRecordSignSteps.Add(new ExamRecordSignStep
        {
            ID = seededId,
            DivisionID = DivisionId,
            RecordID = record.RecordID,
            VariantCode = VariantCode,
            ItemGroupID = ItemGroupId,
            SWStep = 1,
            StepName = "Bước 1",
            SWRoleID = 45,
            Status = ExamRecordSignStepStatus.InProgress,
            CreatedDate = DateTime.UtcNow,
            ModifiedDate = DateTime.UtcNow
        });
        db.Db.SaveChanges();
        db.Db.ChangeTracker.Clear();

        var res = await handler.HandleAsync(Command(record.RecordID));

        Assert.True(res.IsSuccess);
        Assert.Equal(ExamRecordSignStepStatus.Signed, res.Value.Status);
        Assert.Equal(1, res.Value.SigningProgressDone);
        Assert.Equal(2, res.Value.SigningProgressTotal);

        // Phải là ĐÚNG dòng đã seed (cùng ID) được ghi đè, không phải một dòng mới chèn thêm
        // cạnh dòng InProgress cũ — đó mới thật sự là chốt UPSERT.
        var savedStep = db.RecordOf(record.RecordID).SignSteps.Single();
        Assert.Equal(seededId, savedStep.ID);
        Assert.Equal(ExamRecordSignStepStatus.Signed, savedStep.Status);
        Assert.Equal(1274, savedStep.SignedByEmployeeID);
        Assert.Equal("NV001", savedStep.SignedByEmployeeCode);
        Assert.NotNull(savedStep.SignedAt);
        Assert.Equal(1274, savedStep.PerformedByEmployeeID);
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

    /// <summary>
    /// HIS trả tên rỗng cho người ký ⇒ chặn NGAY, trước cả kiểm tra chứng thư — không để chữ ký
    /// đóng lên PDF với tên trống (kết luận sẽ fallback in tên bước nếu lọt qua đây). Cấp sẵn
    /// chứng thư cho mã này để chốt đúng nhánh mới, không phải nhánh "chưa có chứng thư".
    /// </summary>
    [Fact]
    public async Task HIS_tra_ten_rong_thi_SignPrecondition_va_khong_ghi_snapshot()
    {
        var (handler, db, record, cert, his) = Fixture();
        his.Add(45, 7777, "NV007", "");
        cert.WithCertificate.Add("NV007");

        var res = await handler.HandleAsync(Command(record.RecordID, confirmedBy: 7777));

        Assert.False(res.IsSuccess);
        Assert.Equal(ApplicationFailureCode.SignPrecondition, res.Failure.Code);
        Assert.Contains("HIS không trả mã/tên", res.Failure.Message);
        Assert.Empty(db.RecordOf(record.RecordID).SignSteps);
    }

    /// <summary>Tương tự trên, nhưng HIS trả mã rỗng — cấp chứng thư cho mã rỗng để chốt đúng nhánh mới.</summary>
    [Fact]
    public async Task HIS_tra_ma_rong_thi_SignPrecondition_va_khong_ghi_snapshot()
    {
        var (handler, db, record, cert, his) = Fixture();
        his.Add(45, 8888, "", "BS D");
        cert.WithCertificate.Add("");

        var res = await handler.HandleAsync(Command(record.RecordID, confirmedBy: 8888));

        Assert.False(res.IsSuccess);
        Assert.Equal(ApplicationFailureCode.SignPrecondition, res.Failure.Code);
        Assert.Contains("HIS không trả mã/tên", res.Failure.Message);
        Assert.Empty(db.RecordOf(record.RecordID).SignSteps);
    }

    [Fact]
    public async Task Nguoi_bam_khong_phai_nguoi_thuc_hien_thi_Forbidden()
    {
        var (handler, db, record, _, _) = Fixture();
        db.Db.ExamRecordSignSteps.Add(new ExamRecordSignStep
        {
            ID = Guid.NewGuid(),
            DivisionID = DivisionId,
            RecordID = record.RecordID,
            VariantCode = VariantCode,
            ItemGroupID = ItemGroupId,
            SWStep = 1,
            StepName = "Bước 1",
            SWRoleID = 45,
            Status = ExamRecordSignStepStatus.InProgress,
            PerformedByEmployeeID = 9999, // Người khác đã thực hiện
            PerformedByEmployeeName = "BS Khác",
            CreatedDate = DateTime.UtcNow,
            ModifiedDate = DateTime.UtcNow
        });
        db.Db.SaveChanges();
        db.Db.ChangeTracker.Clear();

        var res = await handler.HandleAsync(Command(record.RecordID));

        Assert.False(res.IsSuccess);
        Assert.Equal(ApplicationFailureCode.Forbidden, res.Failure.Code);
        Assert.Contains("Chỉ người thực hiện bước này mới được ký số", res.Failure.Message);
        Assert.Equal(ExamRecordSignStepStatus.InProgress, db.RecordOf(record.RecordID).SignSteps.Single().Status);
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
    ///
    /// Xoá vết theo dõi giữa hai lượt gọi: không xoá thì <c>ExamRecordRepository.GetAsync</c>
    /// ở lượt 2 trả về CHÍNH thực thể đã tracked của lượt 1 từ identity map, và test không còn
    /// phân biệt được "handler tự ghi đè đúng" với "EF chỉ đưa lại cùng một instance trong bộ nhớ".
    /// </summary>
    [Fact]
    public async Task Bam_ky_hai_lan_chi_con_mot_snapshot()
    {
        var (handler, db, record, _, _) = Fixture();

        await handler.HandleAsync(Command(record.RecordID));
        db.Db.ChangeTracker.Clear();
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

    /// <summary>
    /// Khóa mục khám là trạng thái SUY RA từ sự tồn tại của snapshot. Hủy ký tức là xóa dòng đó,
    /// không có cột cờ nào phải hạ.
    /// </summary>
    [Fact]
    public async Task Huy_ky_xoa_snapshot_va_mo_khoa_muc_kham()
    {
        var (handler, db, record, _, _) = Fixture();
        await handler.HandleAsync(Command(record.RecordID));
        Assert.Single(db.RecordOf(record.RecordID).SignSteps);

        var cancel = db.CancelExamSectionSign();
        var res = await cancel.HandleAsync(new CancelExamSectionSignCommand(
            DivisionId, record.RecordID, ItemGroupId, EmployeeId: 1274, ActorKind.Employee));

        Assert.True(res.IsSuccess);
        var step = Assert.Single(db.RecordOf(record.RecordID).SignSteps);
        Assert.Equal(ExamRecordSignStepStatus.InProgress, step.Status);
        Assert.Null(step.SignedByEmployeeID);
        Assert.Null(step.SignedAt);
    }

    /// <summary>Hủy ký rồi ký lại bằng người xác nhận KHÁC: chỉ còn một snapshot, mang đúng người mới.</summary>
    [Fact]
    public async Task Huy_ky_roi_ky_lai_boi_nguoi_xac_nhan_khac_chi_con_mot_snapshot_dung_nguoi_moi()
    {
        var (handler, db, record, cert, his) = Fixture();
        await handler.HandleAsync(Command(record.RecordID, ConfirmerId));
        db.Db.ChangeTracker.Clear();

        var cancel = db.CancelExamSectionSign();
        await cancel.HandleAsync(new CancelExamSectionSignCommand(
            DivisionId, record.RecordID, ItemGroupId, EmployeeId: ActorId, ActorKind.Employee));
        db.Db.ChangeTracker.Clear();

        his.Add(45, 5555, "NV003", "BS C");
        cert.WithCertificate.Add("NV003");

        var res = await handler.HandleAsync(Command(record.RecordID, 5555));
        db.Db.ChangeTracker.Clear();

        Assert.True(res.IsSuccess);
        var snapshot = db.RecordOf(record.RecordID).SignSteps.Single();
        Assert.Equal(5555, snapshot.SignedByEmployeeID);
        Assert.Equal("BS C", snapshot.SignedByEmployeeName);
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
}
