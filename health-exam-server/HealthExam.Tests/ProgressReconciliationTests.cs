using System.Net;
using System.Text;
using HealthExam.Domain.Common;
using HealthExam.Domain.ExamSessions;
using HealthExam.Domain.ExamRecords;
using HealthExam.Domain.Imports;
using HealthExam.Domain.Paraclinical;
using HealthExam.Domain.Webhooks;
using HealthExam.Domain.Integrations;
using HealthExam.Domain.Catalogs;
using HealthExam.Application.Webhooks;
using HealthExam.API.Contracts;
using HealthExam.API.Middlewares;
using HealthExam.Application.Common;
using Newtonsoft.Json;
using Xunit;

namespace HealthExam.Tests;

/// <summary>
/// H2-06 — job đối soát kéo.
///
/// Gate của task: "tắt webhook, khám 5 hồ sơ, chạy job → 5/5 hồ sơ về đúng trạng thái".
/// Ở đây "tắt webhook" được mô phỏng đúng như thật: hồ sơ đứng im ở trạng thái cũ vì KHÔNG có
/// sự kiện nào tới, còn form-server thì đã biết phiếu đi tới đâu.
///
/// form-server giả bằng HttpMessageHandler chứ không mock FormServerClient: phần dễ sai của
/// việc này nằm ở bóc envelope và ánh xạ 5xx → 5020, mà mock client là bỏ qua đúng phần đó.
/// </summary>
public class ProgressReconciliationTests
{
    private static Guid Submission(int n) => Guid.Parse($"aaaaaaaa-0000-0000-0000-{n:D12}");

    /// <summary>
    /// Gate chính: 5 hồ sơ mất hết sự kiện, chạy job → 5/5 về đúng trạng thái form-server
    /// đang giữ.
    /// </summary>
    [Fact]
    public async Task Mat_het_webhook_chay_job_thi_nam_tren_nam_ho_so_ve_dung_trang_thai()
    {
        using var db = new InMemoryTestDb();
        var session = db.SeedSession();

        var records = new List<ExamRecord>();
        for (var i = 1; i <= 5; i++)
            records.Add(db.SeedRecord(session.SessionID, ExamRecordState.Waiting,
                recordCode: $"DK-2026-001-000{i}", submissionId: Submission(i)));

        // form-server: cả 5 phiếu đều đã khám xong 17/17 và đã ký kết luận.
        var formServer = FakeFormServer.Always(new FormSubmissionProgressDto
        {
            Sections = new FormProgressCount { Completed = 17, Total = 17 },
            SubmissionState = FormSubmissionStates.Completed,
            CanSignConclusion = false
        });

        var result = await db.Reconciliation(formServer).RunAsync("DEV");

        Assert.Equal(5, result.Examined);
        Assert.Equal(5, result.Healed);
        foreach (var r in records)
        {
            var after = db.RecordOf(r.RecordID);
            Assert.Equal(ExamRecordState.Completed, after.State);
            Assert.Equal((short)17, after.ProgressDone);
            Assert.NotNull(after.ExamFinishedAt);
        }
        Assert.Equal(5, db.Metrics.ReconcileHealed);
    }

    /// <summary>
    /// Job chỉ ĐI TỚI, không lùi: đây là đường chữa cái đã mất, không phải đồng bộ hai chiều.
    /// Một số đọc nhầm mà lùi được trạng thái là biến chốt chặn thành nguồn lỗi.
    /// </summary>
    [Fact]
    public async Task Khong_bao_gio_keo_lui_trang_thai_ho_so()
    {
        using var db = new InMemoryTestDb();
        var session = db.SeedSession();
        var record = db.SeedRecord(session.SessionID, ExamRecordState.InProgress, submissionId: Submission(1));

        var formServer = FakeFormServer.Always(new FormSubmissionProgressDto
        {
            Sections = new FormProgressCount { Completed = 0, Total = 17 },
            SubmissionState = FormSubmissionStates.Draft
        });

        var result = await db.Reconciliation(formServer).RunAsync("DEV");

        Assert.Equal(0, result.Healed);
        Assert.Equal(ExamRecordState.InProgress, db.RecordOf(record.RecordID).State);
    }

    /// <summary>
    /// Không có SubmissionState (ví dụ phản hồi /progress ở form-service/02-api-spec §5.4
    /// chưa có trường này) thì vẫn chữa được nhánh RẺ NHẤT: có nhóm khám nào đã xong nghĩa là
    /// chắc chắn đã bắt đầu khám.
    /// </summary>
    [Fact]
    public async Task Thieu_SubmissionState_van_chua_duoc_nhanh_Cho_kham_sang_Dang_kham()
    {
        using var db = new InMemoryTestDb();
        var session = db.SeedSession();
        var record = db.SeedRecord(session.SessionID, ExamRecordState.Waiting, submissionId: Submission(1));

        var formServer = FakeFormServer.Always(new FormSubmissionProgressDto
        {
            Sections = new FormProgressCount { Completed = 3, Total = 17 }
        });

        var result = await db.Reconciliation(formServer).RunAsync("DEV");

        Assert.Equal(1, result.Healed);
        var after = db.RecordOf(record.RecordID);
        Assert.Equal(ExamRecordState.InProgress, after.State);
        Assert.NotNull(after.ExamStartedAt);
    }

    /// <summary>
    /// Đủ 17/17 nhưng KHÔNG có SubmissionState ⇒ không biết KẾT LUẬN đã ký chưa. ĐẾM chứ
    /// không đoán: đoán "chắc là xong" là đẩy hồ sơ sang Đã khám trước khi bác sĩ ký, đúng cái
    /// mà việc không có endpoint PATCH /state đang chặn.
    /// </summary>
    [Fact]
    public async Task Du_tien_do_nhung_thieu_SubmissionState_thi_dem_vao_Undecidable()
    {
        using var db = new InMemoryTestDb();
        var session = db.SeedSession();
        var record = db.SeedRecord(session.SessionID, ExamRecordState.InProgress, submissionId: Submission(1));

        var formServer = FakeFormServer.Always(new FormSubmissionProgressDto
        {
            Sections = new FormProgressCount { Completed = 17, Total = 17 }
        });

        var result = await db.Reconciliation(formServer).RunAsync("DEV");

        Assert.Equal(1, result.Undecidable);
        Assert.Equal(0, result.Healed);
        Assert.Equal(ExamRecordState.InProgress, db.RecordOf(record.RecordID).State);
    }

    /// <summary>
    /// Hồ sơ chưa có SubmissionID thì KHÔNG có gì để hỏi — không được đếm vào Examined, nếu
    /// không thì con số "đã xét" nói dối về mức phủ của job.
    /// </summary>
    [Fact]
    public async Task Ho_so_chua_co_phieu_khong_nam_trong_lo_doi_soat()
    {
        using var db = new InMemoryTestDb();
        var session = db.SeedSession();
        db.SeedRecord(session.SessionID, ExamRecordState.Waiting, recordCode: "R-1");
        db.SeedRecord(session.SessionID, ExamRecordState.Waiting, recordCode: "R-2", submissionId: Submission(2));

        var result = await db.Reconciliation(FakeFormServer.Always(new FormSubmissionProgressDto
        {
            Sections = new FormProgressCount { Completed = 1, Total = 17 }
        })).RunAsync("DEV");

        Assert.Equal(1, result.Examined);
    }

    /// <summary>
    /// Hồ sơ Đã khám và Hủy không nằm trong lô: chúng chỉ rời khỏi đó bằng thao tác có người
    /// đứng sau, và job này không được phép tự lùi trạng thái.
    /// </summary>
    [Fact]
    public async Task Ho_so_da_kham_va_da_huy_khong_bi_doi_soat()
    {
        using var db = new InMemoryTestDb();
        var session = db.SeedSession();
        db.SeedRecord(session.SessionID, ExamRecordState.Completed, recordCode: "R-1", submissionId: Submission(1));
        db.SeedRecord(session.SessionID, ExamRecordState.RegistrationCancelled, recordCode: "R-2", submissionId: Submission(2));
        db.SeedRecord(session.SessionID, ExamRecordState.ExamCancelled, recordCode: "R-3", submissionId: Submission(3));

        var result = await db.Reconciliation(FakeFormServer.Always(new FormSubmissionProgressDto
        {
            Sections = new FormProgressCount { Completed = 17, Total = 17 },
            SubmissionState = FormSubmissionStates.Completed
        })).RunAsync("DEV");

        Assert.Equal(0, result.Examined);
    }

    /// <summary>
    /// form-server chết KHÔNG được làm hỏng cả lượt: hồ sơ sau nó có thể đang trỏ vào một pod
    /// còn sống, và lượt sau vẫn xét lại hồ sơ này.
    /// </summary>
    [Fact]
    public async Task Form_server_chet_thi_dem_vao_Unreachable_chu_khong_nem_loi()
    {
        using var db = new InMemoryTestDb();
        var session = db.SeedSession();
        var record = db.SeedRecord(session.SessionID, ExamRecordState.Waiting, submissionId: Submission(1));

        var result = await db.Reconciliation(FakeFormServer.Status(HttpStatusCode.InternalServerError))
            .RunAsync("DEV");

        Assert.Equal(1, result.Examined);
        Assert.Equal(1, result.Unreachable);
        Assert.Equal(0, result.Healed);
        Assert.Equal(ExamRecordState.Waiting, db.RecordOf(record.RecordID).State);
    }

    /// <summary>Chuyển tiếp do job chữa ghi Action=RECONCILE, không lẫn với WEBHOOK: con số
    /// "bao nhiêu hồ sơ phải chữa bằng đường kéo" chính là thước đo sức khoẻ của đường đẩy.</summary>
    [Fact]
    public async Task Chua_bang_job_ghi_nhat_ky_RECONCILE()
    {
        using var db = new InMemoryTestDb();
        var session = db.SeedSession();
        var record = db.SeedRecord(session.SessionID, ExamRecordState.Waiting, submissionId: Submission(1));

        await db.Reconciliation(FakeFormServer.Always(new FormSubmissionProgressDto
        {
            Sections = new FormProgressCount { Completed = 2, Total = 17 },
            SubmissionState = FormSubmissionStates.InProgress
        })).RunAsync("DEV");

        var audit = Assert.Single(db.AuditRowsOf(record.RecordID));
        Assert.Equal(AuditActions.Reconcile, audit.Action);
        Assert.Equal(ActorKind.Integration, audit.ActorKind);
        Assert.Equal((short)ExamRecordState.Waiting, audit.FromState);
        Assert.Equal((short)ExamRecordState.InProgress, audit.ToState);
    }

    /// <summary>Chỉ đối soát tenant được yêu cầu — cùng mã hồ sơ ở hai đơn vị là hai người khác nhau.</summary>
    [Fact]
    public async Task Chi_doi_soat_tenant_duoc_yeu_cau()
    {
        using var db = new InMemoryTestDb();
        var session = db.SeedSession();
        db.SeedRecord(session.SessionID, ExamRecordState.Waiting, recordCode: "R-1", submissionId: Submission(1));
        db.SeedRecord(session.SessionID, ExamRecordState.Waiting, recordCode: "R-2",
            divisionId: "BV-KHAC", submissionId: Submission(2));

        var result = await db.Reconciliation(FakeFormServer.Always(new FormSubmissionProgressDto
        {
            Sections = new FormProgressCount { Completed = 1, Total = 17 }
        })).RunAsync("DEV");

        Assert.Equal(1, result.Examined);
    }

    /// <summary>
    /// Trạng thái không đổi nhưng tiến độ lệch ⇒ vẫn phải làm mới cache, và đếm vào
    /// ProgressRefreshed để phân biệt với lượt phải chữa trạng thái.
    /// </summary>
    [Fact]
    public async Task Chi_lech_tien_do_thi_lam_moi_cache_va_dem_rieng()
    {
        using var db = new InMemoryTestDb();
        var session = db.SeedSession();
        var record = db.SeedRecord(session.SessionID, ExamRecordState.InProgress,
            submissionId: Submission(1), progressDone: 3, progressTotal: 17);

        var result = await db.Reconciliation(FakeFormServer.Always(new FormSubmissionProgressDto
        {
            Sections = new FormProgressCount { Completed = 11, Total = 17 },
            SubmissionState = FormSubmissionStates.InProgress
        })).RunAsync("DEV");

        Assert.Equal(0, result.Healed);
        Assert.Equal(1, result.ProgressRefreshed);
        Assert.Equal((short)11, db.RecordOf(record.RecordID).ProgressDone);
    }
    // ================================================================ review lần 2 MR !5

    /// <summary>
    /// ★ §4.6 — MỘT hồ sơ hỏng KHÔNG được giết cả lượt đối soát.
    ///
    /// Trước bản vá, chỉ hai loại lỗi được bắt (DependencyUnavailable, NotFound); mọi lỗi khác
    /// ném thẳng ra khỏi vòng lặp. Cộng với thứ tự "lâu chưa nghe tin nhất đi trước", nó thành
    /// lỗi TỰ KHOÁ VĨNH VIỄN: hồ sơ hỏng không bao giờ được nhích ProgressSyncedAt ⇒ luôn đứng
    /// đầu hàng ⇒ lượt nào cũng chết ở đúng nó ⇒ MỌI hồ sơ xếp sau không bao giờ được đối soát.
    /// </summary>
    [Fact]
    public async Task Mot_ho_so_hong_khong_giet_ca_luot_doi_soat()
    {
        using var db = new InMemoryTestDb();
        var session = db.SeedSession();

        // Hồ sơ hỏng được tạo TRƯỚC ⇒ ProgressSyncedAt null, CreatedDate cũ nhất ⇒ đứng đầu lô.
        var poison = db.SeedRecord(session.SessionID, ExamRecordState.Waiting,
            recordCode: "DK-2026-001-0001", submissionId: Submission(1),
            createdDate: new DateTime(2026, 8, 20, 0, 0, 0, DateTimeKind.Utc));
        var good1 = db.SeedRecord(session.SessionID, ExamRecordState.Waiting,
            recordCode: "DK-2026-001-0002", submissionId: Submission(2),
            createdDate: new DateTime(2026, 8, 21, 0, 0, 0, DateTimeKind.Utc));
        var good2 = db.SeedRecord(session.SessionID, ExamRecordState.Waiting,
            recordCode: "DK-2026-001-0003", submissionId: Submission(3),
            createdDate: new DateTime(2026, 8, 22, 0, 0, 0, DateTimeKind.Utc));

        var done = new FormSubmissionProgressDto
        {
            Sections = new FormProgressCount { Completed = 17, Total = 17 },
            SubmissionState = FormSubmissionStates.Completed
        };
        // Lượt đầu: 4030 "phiếu thuộc tenant khác" — mã lỗi KHÔNG thuộc hai nhánh bắt riêng.
        var formServer = FakeFormServer.Sequence(
            FakeFormServer.Err(4030, "Phiếu không thuộc đợt khám trong token"),
            FakeFormServer.Ok(done));

        var result = await db.Reconciliation(formServer).RunAsync("DEV");

        Assert.Equal(3, result.Examined);
        Assert.Equal(1, result.Failed);          // đếm được, không nuốt lặng
        Assert.Equal(2, result.Healed);          // hai hồ sơ SAU hồ sơ hỏng vẫn được chữa
        Assert.Equal(ExamRecordState.Waiting, db.RecordOf(poison.RecordID).State);
        Assert.Equal(ExamRecordState.Completed, db.RecordOf(good1.RecordID).State);
        Assert.Equal(ExamRecordState.Completed, db.RecordOf(good2.RecordID).State);
    }

    // ================================================================ review lần 3 MR !5

    /// <summary>
    /// ★★ D2 — hỏng ở BƯỚC GHI cũng không được giết cả lượt, và KHÔNG được khai khống.
    ///
    /// Bản vá §4.6 bọc try/catch quanh từng hồ sơ nhưng không dọn change tracker. Reconcile()
    /// sửa thực thể trong bộ nhớ trước, SaveChanges sau; lượt lưu hỏng làm transaction của hồ
    /// sơ đó rollback nhưng thực thể bẩn Ở LẠI — nên SaveChanges của hồ sơ kế tiếp gửi lại
    /// đúng câu UPDATE hỏng và hỏng theo, hết cả lô.
    ///
    /// Nặng hơn "chưa vá hết": trước §4.6 lỗi thoát ra thành 4030 KÊU TO, sau §4.6 là HTTP 200
    /// kèm Healed khống (đo trên PostgreSQL: {Examined:4, Healed:4, Failed:4} trên 4 hồ sơ,
    /// thực tế chữa được 0). Bảng theo dõi đọc con số ấy thấy job chạy hoàn hảo.
    ///
    /// Test này giữ CẢ HAI vế: hồ sơ xếp sau vẫn được chữa, và Healed không đếm hồ sơ hỏng.
    /// </summary>
    [Fact]
    public async Task Hong_o_buoc_GHI_khong_giet_ca_luot_va_khong_dem_khong_vao_Healed()
    {
        // Hồ sơ đầu lô bị tầng DB từ chối lệnh ghi — đúng vai của trigger trong lượt đo thật.
        using var db = new InMemoryTestDb(
            failWriteFor: r => r.RecordCode == "DK-2026-001-0001");
        var session = db.SeedSession();

        var poison = db.SeedRecord(session.SessionID, ExamRecordState.Waiting,
            recordCode: "DK-2026-001-0001", submissionId: Submission(1),
            createdDate: new DateTime(2026, 8, 20, 0, 0, 0, DateTimeKind.Utc));
        var after = new List<ExamRecord>();
        for (var i = 2; i <= 4; i++)
            after.Add(db.SeedRecord(session.SessionID, ExamRecordState.Waiting,
                recordCode: $"DK-2026-001-000{i}", submissionId: Submission(i),
                createdDate: new DateTime(2026, 8, 20 + i, 0, 0, 0, DateTimeKind.Utc)));

        // form-server lành với MỌI hồ sơ: chỗ hỏng duy nhất là bước ghi.
        var formServer = FakeFormServer.Always(new FormSubmissionProgressDto
        {
            Sections = new FormProgressCount { Completed = 17, Total = 17 },
            SubmissionState = FormSubmissionStates.Completed
        });

        var result = await db.Reconciliation(formServer).RunAsync("DEV");

        Assert.Equal(4, result.Examined);
        Assert.Equal(1, result.Failed);
        // ★ Ba hồ sơ XẾP SAU hồ sơ hỏng vẫn được chữa — đây là điều change tracker bẩn phá.
        Assert.Equal(3, result.Healed);
        Assert.All(after, r => Assert.Equal(ExamRecordState.Completed, db.RecordOf(r.RecordID).State));

        // ★ Và hồ sơ hỏng KHÔNG được tính là đã chữa: lượt ghi của nó đã rollback.
        Assert.Equal(ExamRecordState.Waiting, db.RecordOf(poison.RecordID).State);
        Assert.Equal(3, db.Metrics.ReconcileHealed);

        // Dòng nhật ký "đã chữa" cũng phải biến mất theo lượt ghi đã rollback — một dòng audit
        // cho việc chưa xảy ra còn tệ hơn không có dòng nào.
        Assert.Empty(db.AuditRowsOf(poison.RecordID));
    }

    /// <summary>
    /// Dừng theo yêu cầu (CancellationToken) vẫn phải dừng THẬT — chốt chặn §4.6 không được
    /// nuốt luôn tín hiệu tắt máy, nếu không thì pod không bao giờ thoát được vòng lặp.
    /// </summary>
    [Fact]
    public async Task Yeu_cau_dung_thi_dung_that_chu_khong_bi_nuot()
    {
        using var db = new InMemoryTestDb();
        var session = db.SeedSession();
        db.SeedRecord(session.SessionID, ExamRecordState.Waiting,
            recordCode: "DK-2026-001-0001", submissionId: Submission(1));

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => db.Reconciliation(FakeFormServer.Status(HttpStatusCode.OK)).RunAsync("DEV", ct: cts.Token));
    }

    /// <summary>
    /// ★ N2 — Loại sức khoẻ I..V lấy được qua ĐƯỜNG KÉO. Gói CONCLUSION mất hẳn thì đây là
    /// đường duy nhất còn lại; không có nó thì hồ sơ mất phân loại vĩnh viễn.
    /// </summary>
    [Fact]
    public async Task Doi_soat_bo_sung_duoc_Loai_suc_khoe_khi_goi_CONCLUSION_mat_han()
    {
        using var db = new InMemoryTestDb();
        var session = db.SeedSession();
        var record = db.SeedRecord(session.SessionID, ExamRecordState.InProgress,
            recordCode: "DK-2026-001-0001", submissionId: Submission(1));

        var formServer = FakeFormServer.Always(new FormSubmissionProgressDto
        {
            Sections = new FormProgressCount { Completed = 17, Total = 17 },
            SubmissionState = FormSubmissionStates.Completed,
            HealthClassCode = "III"
        });

        var result = await db.Reconciliation(formServer).RunAsync("DEV");

        Assert.Equal(1, result.Healed);
        var after = db.RecordOf(record.RecordID);
        Assert.Equal(ExamRecordState.Completed, after.State);
        Assert.Equal("III", after.HealthClassCode);
    }

    /// <summary>
    /// ★ N2 mặt trái — bên phát VẮNG MẶT trường (chưa ký kết luận) thì GIỮ NGUYÊN giá trị cũ.
    /// Coi null là "xoá" nghĩa là mỗi lượt job chạy qua sẽ xoá mất phân loại webhook đã ghi.
    /// </summary>
    [Fact]
    public async Task Progress_khong_mang_Loai_suc_khoe_thi_KHONG_xoa_gia_tri_da_co()
    {
        using var db = new InMemoryTestDb();
        var session = db.SeedSession();
        var record = db.SeedRecord(session.SessionID, ExamRecordState.InProgress,
            recordCode: "DK-2026-001-0001", submissionId: Submission(1));
        db.SetHealthClass(record.RecordID, "II");

        var formServer = FakeFormServer.Always(new FormSubmissionProgressDto
        {
            Sections = new FormProgressCount { Completed = 17, Total = 17 },
            SubmissionState = FormSubmissionStates.Completed,
            HealthClassCode = null
        });

        await db.Reconciliation(formServer).RunAsync("DEV");

        Assert.Equal("II", db.RecordOf(record.RecordID).HealthClassCode);
    }

    /// <summary>
    /// ★ §4.7 — mốc do job điền phải được ĐÁNH DẤU là ước lượng, nếu không thì gói thật tới
    /// sau không có cách nào biết để đè lên.
    /// </summary>
    [Fact]
    public async Task Moc_gio_do_job_dien_duoc_danh_dau_la_uoc_luong()
    {
        using var db = new InMemoryTestDb();
        var session = db.SeedSession();
        var record = db.SeedRecord(session.SessionID, ExamRecordState.Waiting,
            recordCode: "DK-2026-001-0001", submissionId: Submission(1));

        var formServer = FakeFormServer.Always(new FormSubmissionProgressDto
        {
            Sections = new FormProgressCount { Completed = 17, Total = 17 },
            SubmissionState = FormSubmissionStates.Completed
        });

        await db.Reconciliation(formServer).RunAsync("DEV");

        var after = db.RecordOf(record.RecordID);
        Assert.NotNull(after.ExamStartedAt);
        Assert.NotNull(after.ExamFinishedAt);
        Assert.True(after.ExamStartedAtEstimated);
        Assert.True(after.ExamFinishedAtEstimated);
    }

}

/// <summary>
/// form-server giả cho test job đối soát. Trả envelope ĐÚNG hình dạng thật (ErrorCode /
/// Message / Data / TraceID) để phần bóc envelope của FormServerClient cũng được đi qua.
/// </summary>
public sealed class FakeFormServer : HttpMessageHandler
{
    private readonly Func<HttpResponseMessage> _responder;

    public int CallCount { get; private set; }

    private FakeFormServer(Func<HttpResponseMessage> responder) => _responder = responder;

    public static FakeFormServer Always(FormSubmissionProgressDto data)
        => new(() => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                JsonConvert.SerializeObject(new { ErrorCode = 0, Message = "", Data = data, TraceID = "FORM-TRACE" }),
                Encoding.UTF8, "application/json")
        });

    public static FakeFormServer Status(HttpStatusCode status)
        => new(() => new HttpResponseMessage(status) { Content = new StringContent("") });

    /// <summary>
    /// Trả envelope 200 nhưng mang MÃ LỖI nghiệp vụ (vd 4030 phiếu thuộc tenant khác) — loại
    /// lỗi KHÔNG thuộc hai nhánh được bắt riêng, tức đúng thứ trước đây giết cả lượt đối soát.
    /// </summary>
    public static FakeFormServer BusinessError(int errorCode, string message)
        => new(() => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                JsonConvert.SerializeObject(new { ErrorCode = errorCode, Message = message, Data = (object)null, TraceID = "FORM-TRACE" }),
                Encoding.UTF8, "application/json")
        });

    /// <summary>Lượt gọi thứ n trả thứ n trong danh sách; hết danh sách thì lặp lại phần tử cuối.</summary>
    public static FakeFormServer Sequence(params Func<HttpResponseMessage>[] steps)
    {
        var i = 0;
        return new(() => steps[Math.Min(i++, steps.Length - 1)]());
    }

    public static Func<HttpResponseMessage> Ok(FormSubmissionProgressDto data)
        => () => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                JsonConvert.SerializeObject(new { ErrorCode = 0, Message = "", Data = data, TraceID = "FORM-TRACE" }),
                Encoding.UTF8, "application/json")
        };

    public static Func<HttpResponseMessage> Err(int errorCode, string message)
        => () => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                JsonConvert.SerializeObject(new { ErrorCode = errorCode, Message = message, Data = (object)null, TraceID = "FORM-TRACE" }),
                Encoding.UTF8, "application/json")
        };

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        CallCount++;
        return Task.FromResult(_responder());
    }
}