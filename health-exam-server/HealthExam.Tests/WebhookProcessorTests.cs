using HealthExam.Domain.Common;
using HealthExam.Domain.ExamSessions;
using HealthExam.Domain.ExamRecords;
using HealthExam.Domain.Imports;
using HealthExam.Domain.Paraclinical;
using HealthExam.Domain.Webhooks;
using HealthExam.Domain.Integrations;
using HealthExam.Domain.Catalogs;
using HealthExam.Application.Webhooks;
using HealthExam.Infrastructure.BackgroundJobs;
using HealthExam.API.Contracts;
using HealthExam.API.Middlewares;
using HealthExam.Application.Common;
using HealthExam.Server.Service;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using Xunit;

namespace HealthExam.Tests;

/// <summary>
/// H2-05 — áp hệ quả 7 sự kiện §5.2 lên máy trạng thái hồ sơ.
///
/// Kiểm THẲNG WebhookProcessor thay vì qua HTTP + worker: thứ cần chốt ở đây là LUẬT
/// (chuyển tiếp nào hợp lệ, gói nào bị bỏ), còn việc rút hàng đợi và chốt transaction là
/// chuyện của WebhookWorker và chỉ chứng minh được trên PostgreSQL thật.
/// </summary>
public class WebhookProcessorTests
{
    private static readonly Guid SubmissionId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly DateTime T0 = new(2026, 8, 24, 2, 0, 0, DateTimeKind.Utc);

    private static WebhookInbox Row(
        string name, string sectionKind = "", DateTime? occurredAt = null,
        string hostRefId = "DK-2026-001-0001", string subjectId = "", string divisionId = "DEV",
        Guid? submissionId = null, FormProgressCount progress = null, string healthClassCode = "",
        string attachKind = "", short state = 0, string eventId = "EVT-0001")
    {
        var evt = new FormWebhookEvent
        {
            EventID = eventId,
            Event = name,
            OccurredAt = occurredAt ?? T0,
            HostRefType = ModuleCodes.HostRefType,
            HostRefID = hostRefId,
            SubjectID = subjectId,
            SubmissionID = submissionId,
            Data = new FormWebhookData
            {
                SectionKind = sectionKind,
                AttachKind = attachKind,
                State = state,
                Progress = progress,
                HealthClassCode = healthClassCode
            }
        };

        return new WebhookInbox
        {
            EventID = eventId,
            EventType = name,
            DivisionID = divisionId,
            SubmissionID = submissionId,
            OccurredAt = occurredAt ?? T0,
            ReceivedAt = DateTime.UtcNow,
            Payload = JsonConvert.SerializeObject(evt)
        };
    }

    // -------------------------------------------------------- 5 chuyển tiếp của bảng §5.2

    /// <summary>Người bệnh ký Tiền sử ở cổng NB (UC04.2) ⇒ hồ sơ chốt đăng ký.</summary>
    [Fact]
    public async Task Ky_HISTORY_dua_ho_so_ve_Cho_kham()
    {
        using var db = new InMemoryTestDb();
        var session = db.SeedSession();
        var record = db.SeedRecord(session.SessionID, ExamRecordState.NotRegistered);

        var (outcome, _) = await db.Processor.ProcessAsync(Row(FormEventNames.SectionSigned, SectionKinds.History));
        await db.Uow.SaveChangesAsync();

        Assert.Equal(WebhookOutcome.Applied, outcome);
        var after = db.RecordOf(record.RecordID);
        Assert.Equal(ExamRecordState.Waiting, after.State);
        Assert.Equal(T0, after.RegisteredAt);
        Assert.Equal(T0, after.LastEventAt);
    }

    [Fact]
    public async Task Nhom_chi_tieu_dau_tien_co_du_lieu_dua_ho_so_ve_Dang_kham()
    {
        using var db = new InMemoryTestDb();
        var session = db.SeedSession();
        var record = db.SeedRecord(session.SessionID, ExamRecordState.Waiting);

        await db.Processor.ProcessAsync(Row(FormEventNames.StateChanged, state: FormSubmissionStates.InProgress));
        await db.Uow.SaveChangesAsync();

        var after = db.RecordOf(record.RecordID);
        Assert.Equal(ExamRecordState.InProgress, after.State);
        Assert.Equal(T0, after.ExamStartedAt);
    }

    [Fact]
    public async Task Ky_CONCLUSION_dua_ho_so_ve_Da_kham_va_chep_loai_suc_khoe()
    {
        using var db = new InMemoryTestDb();
        var session = db.SeedSession();
        var record = db.SeedRecord(session.SessionID, ExamRecordState.InProgress);

        await db.Processor.ProcessAsync(
            Row(FormEventNames.SectionSigned, SectionKinds.Conclusion, healthClassCode: "II"));
        await db.Uow.SaveChangesAsync();

        var after = db.RecordOf(record.RecordID);
        Assert.Equal(ExamRecordState.Completed, after.State);
        Assert.Equal(T0, after.ExamFinishedAt);
        Assert.Equal("II", after.HealthClassCode);
    }

    /// <summary>Ký huỷ kết luận (UC05.4.5) ⇒ mở lại khoá tab Khám LS + CLS.</summary>
    [Fact]
    public async Task Ky_huy_CONCLUSION_dua_ho_so_nguoc_ve_Dang_kham()
    {
        using var db = new InMemoryTestDb();
        var session = db.SeedSession();
        var record = db.SeedRecord(session.SessionID, ExamRecordState.Completed);

        await db.Processor.ProcessAsync(
            Row(FormEventNames.SectionSignCancelled, SectionKinds.Conclusion, occurredAt: T0.AddMinutes(5)));
        await db.Uow.SaveChangesAsync();

        var after = db.RecordOf(record.RecordID);
        Assert.Equal(ExamRecordState.InProgress, after.State);
        Assert.Null(after.ExamFinishedAt);
        Assert.Equal("", after.HealthClassCode);
    }

    /// <summary>17 nhóm khám lâm sàng chỉ nhích CACHE, không đụng máy trạng thái (§9.5).</summary>
    [Fact]
    public async Task Ky_CLINICAL_EXAM_chi_cap_nhat_cache_tien_do()
    {
        using var db = new InMemoryTestDb();
        var session = db.SeedSession();
        var record = db.SeedRecord(session.SessionID, ExamRecordState.InProgress);

        await db.Processor.ProcessAsync(
            Row(FormEventNames.SectionSigned, SectionKinds.ClinicalExam,
                progress: new FormProgressCount { Completed = 5, Total = 17 }));
        await db.Uow.SaveChangesAsync();

        var after = db.RecordOf(record.RecordID);
        Assert.Equal(ExamRecordState.InProgress, after.State);
        Assert.Equal((short)5, after.ProgressDone);
        Assert.Equal((short)17, after.ProgressTotal);
        Assert.NotNull(after.ProgressSyncedAt);
    }

    /// <summary>
    /// Hai dòng cuối bảng §5.2 nay ĐÃ hiện thực (P3a) — hành vi đầy đủ nằm ở
    /// ParaclinicalScanResultTests. Ở đây chỉ chốt phần thuộc về bộ xử lý sự kiện: đính kèm
    /// KQ scan KHÔNG đụng máy trạng thái HỒ SƠ, và hồ sơ không có chỉ định nào thì nó bỏ qua
    /// CÓ GHI CHÚ chứ không coi là sự kiện lạ và cũng không đếm vào InvalidTransition.
    /// </summary>
    [Fact]
    public async Task Su_kien_dinh_kem_KQ_scan_khong_dung_may_trang_thai_ho_so()
    {
        using var db = new InMemoryTestDb();
        var session = db.SeedSession();
        var record = db.SeedRecord(session.SessionID, ExamRecordState.InProgress);

        var (outcome, detail) = await db.Processor.ProcessAsync(
            Row(FormEventNames.AttachmentAdded, attachKind: AttachKinds.ScanResult));

        Assert.Equal(WebhookOutcome.Skipped, outcome);
        Assert.Contains("dòng dịch vụ", detail);
        Assert.Equal(ExamRecordState.InProgress, db.RecordOf(record.RecordID).State);
        // KHÔNG đếm vào InvalidTransition: hai máy trạng thái không hề lệch nhau ở đây.
        Assert.Equal(0, db.Metrics.InvalidTransition);
    }

    // -------------------------------------------------------- §5.3 lớp 2: đến muộn

    /// <summary>
    /// Gate 03-task H2-05: sự kiện CONCLUSION xử lý xong, sau đó một sự kiện CŨ HƠN đến muộn
    /// → hồ sơ VẪN "Đã khám". Mạng chậm đủ để "ký kết luận" tới trước "ký nhóm khám thứ 17".
    /// </summary>
    [Fact]
    public async Task Su_kien_cu_den_muon_khong_keo_lui_trang_thai()
    {
        using var db = new InMemoryTestDb();
        var session = db.SeedSession();
        var record = db.SeedRecord(session.SessionID, ExamRecordState.InProgress);

        // Ký kết luận lúc 10:00 — tới nơi trước.
        await db.Processor.ProcessAsync(
            Row(FormEventNames.SectionSigned, SectionKinds.Conclusion, occurredAt: T0.AddHours(1), eventId: "EVT-KL"));
        await db.Uow.SaveChangesAsync();
        Assert.Equal(ExamRecordState.Completed, db.RecordOf(record.RecordID).State);

        // Sự kiện xảy ra lúc 09:30 nhưng tới nơi sau.
        var result = await db.Processor.ProcessAsync(
            Row(FormEventNames.StateChanged, occurredAt: T0.AddMinutes(30),
                state: FormSubmissionStates.InProgress, eventId: "EVT-MUON"));
        await db.Uow.SaveChangesAsync();

        Assert.Equal(WebhookOutcome.Skipped, result.Outcome);
        Assert.Contains("đến muộn", result.Detail);
        Assert.Equal(ExamRecordState.Completed, db.RecordOf(record.RecordID).State);

        // Cờ đếm nằm trên KẾT QUẢ, không phải bộ đếm toàn cục: WebhookWorker chỉ cộng metric
        // SAU KHI transaction commit. Đếm ngay trong processor thì mọi lượt bị rollback vẫn
        // được tính, và bộ đếm nói dối đúng lúc người ta nhìn vào nó.
        Assert.True(result.StaleSkipped);
    }

    /// <summary>
    /// Mốc LastEventAt chỉ nhích khi sự kiện ĐƯỢC ÁP. Ghi cả lượt bỏ qua thì một gói lạc mang
    /// thời điểm tương lai sẽ khoá chết mọi sự kiện thật đến sau nó.
    /// </summary>
    [Fact]
    public async Task Luot_bo_qua_khong_nhich_moc_LastEventAt()
    {
        using var db = new InMemoryTestDb();
        var session = db.SeedSession();
        var record = db.SeedRecord(session.SessionID, ExamRecordState.NotRegistered);

        // Chuyển tiếp không hợp lệ (đòi 2→3 khi hồ sơ đang ở 0), mang mốc thời gian tương lai.
        await db.Processor.ProcessAsync(
            Row(FormEventNames.SectionSigned, SectionKinds.Conclusion, occurredAt: T0.AddDays(30)));
        await db.Uow.SaveChangesAsync();
        Assert.Null(db.RecordOf(record.RecordID).LastEventAt);

        // Sự kiện thật, đúng luật, mốc thời gian bình thường — vẫn phải áp được.
        await db.Processor.ProcessAsync(
            Row(FormEventNames.SectionSigned, SectionKinds.History, eventId: "EVT-2"));
        await db.Uow.SaveChangesAsync();

        Assert.Equal(ExamRecordState.Waiting, db.RecordOf(record.RecordID).State);
    }

    // -------------------------------------------------------- §5.3 lớp 3: chuyển tiếp sai

    /// <summary>
    /// Gate 03-task H2-05: chuyển tiếp không hợp lệ → bỏ qua + metric tăng ĐÚNG 1.
    /// Đây là dấu hiệu hai máy trạng thái đang lệch nhau, không phải chuyện thường.
    /// </summary>
    [Fact]
    public async Task Chuyen_tiep_khong_hop_le_bi_bo_qua_va_dem_dung_mot()
    {
        using var db = new InMemoryTestDb();
        var session = db.SeedSession();
        var record = db.SeedRecord(session.SessionID, ExamRecordState.NotRegistered);

        var result = await db.Processor.ProcessAsync(
            Row(FormEventNames.SectionSigned, SectionKinds.Conclusion));
        await db.Uow.SaveChangesAsync();

        Assert.Equal(WebhookOutcome.Skipped, result.Outcome);
        Assert.Equal(ExamRecordState.NotRegistered, db.RecordOf(record.RecordID).State);
        Assert.True(result.InvalidTransition);
    }

    /// <summary>
    /// Hồ sơ ĐÃ ở đúng đích thì bỏ qua nhưng KHÔNG đếm vào InvalidTransition — bên phát gửi
    /// lại bằng EventID khác, hoặc job đối soát đã chữa trước. Đếm cả trường hợp này thì con
    /// số cảnh báo mất nghĩa và không ai còn nhìn nó nữa.
    /// </summary>
    [Fact]
    public async Task Ho_so_da_o_dung_dich_thi_bo_qua_ma_khong_dem_lech()
    {
        using var db = new InMemoryTestDb();
        var session = db.SeedSession();
        db.SeedRecord(session.SessionID, ExamRecordState.Waiting);

        await db.Processor.ProcessAsync(Row(FormEventNames.SectionSigned, SectionKinds.History));

        Assert.Equal(0, db.Metrics.InvalidTransition);
    }

    /// <summary>Huỷ(4/5) là trạng thái CUỐI — không sự kiện nào của form-server kéo nó ra được.</summary>
    [Theory]
    [InlineData(ExamRecordState.RegistrationCancelled)]
    [InlineData(ExamRecordState.ExamCancelled)]
    public async Task Ho_so_da_huy_khong_bi_su_kien_keo_nguoc_lai(ExamRecordState state)
    {
        using var db = new InMemoryTestDb();
        var session = db.SeedSession();
        var record = db.SeedRecord(session.SessionID, state);

        await db.Processor.ProcessAsync(Row(FormEventNames.SectionSigned, SectionKinds.History));
        await db.Uow.SaveChangesAsync();

        Assert.Equal(state, db.RecordOf(record.RecordID).State);
    }

    // -------------------------------------------------------- Tra hồ sơ

    /// <summary>
    /// ★ ĐƯỜNG TRA CHÍNH sau chốt của cụm 236 (24/08, đo trên form-server thật):
    /// <c>HostRefID</c> giữ nguyên mã ĐỢT, còn <c>FRM_Submission.SubjectID</c> là **mã HỒ SƠ**.
    ///
    /// Test này là chốt chặn của cả nhánh: hồ sơ chỉ biết SubmissionID của mình SAU khi nhận
    /// được một sự kiện, nên với sự kiện ĐẦU TIÊN đây là đường duy nhất còn lại. Tra sai chỗ
    /// này thì không hồ sơ nào bao giờ bước ra khỏi trạng thái đầu.
    /// </summary>
    [Fact]
    public async Task Tra_duoc_ho_so_theo_SubjectID_la_MA_HO_SO()
    {
        using var db = new InMemoryTestDb();
        var session = db.SeedSession(sessionCode: "DK-2026-001");
        db.SeedRecord(session.SessionID, ExamRecordState.NotRegistered,
            recordCode: "DK-2026-001-0001", patientCode: "NB0001");
        var target = db.SeedRecord(session.SessionID, ExamRecordState.NotRegistered,
            recordCode: "DK-2026-001-0002", patientCode: "NB0002");

        // Đúng hình dạng gói mà form-server sẽ phát: HostRefID = mã ĐỢT, SubjectID = mã HỒ SƠ.
        await db.Processor.ProcessAsync(
            Row(FormEventNames.SectionSigned, SectionKinds.History,
                hostRefId: "DK-2026-001", subjectId: "DK-2026-001-0002"));
        await db.Uow.SaveChangesAsync();

        Assert.Equal(ExamRecordState.Waiting, db.RecordOf(target.RecordID).State);
    }

    /// <summary>
    /// Dự phòng: bên phát gửi Mã NB ở SubjectID thay vì mã hồ sơ. Bộ phát webhook của
    /// form-server CHƯA tồn tại nên hôm nay không ai chứng minh được nó gửi gì — nhận rộng ở
    /// bên nhận rẻ hơn nhiều so với một sự kiện khám rơi lặng lẽ.
    /// </summary>
    [Fact]
    public async Task Tra_duoc_ho_so_khi_SubjectID_lai_la_Ma_NB()
    {
        using var db = new InMemoryTestDb();
        var session = db.SeedSession(sessionCode: "DK-2026-001");
        db.SeedRecord(session.SessionID, ExamRecordState.NotRegistered,
            recordCode: "DK-2026-001-0001", patientCode: "NB0001");
        var target = db.SeedRecord(session.SessionID, ExamRecordState.NotRegistered,
            recordCode: "DK-2026-001-0002", patientCode: "NB0002");

        await db.Processor.ProcessAsync(
            Row(FormEventNames.SectionSigned, SectionKinds.History,
                hostRefId: "DK-2026-001", subjectId: "NB0002"));
        await db.Uow.SaveChangesAsync();

        Assert.Equal(ExamRecordState.Waiting, db.RecordOf(target.RecordID).State);
    }

    /// <summary>
    /// HostRefID khớp mã ĐỢT nhưng KHÔNG có SubjectID ⇒ dừng hẳn. Tuyệt đối không lấy hồ sơ
    /// đầu tiên của đợt: áp một sự kiện khám lên nhầm người là lỗi không ai phát hiện ra cho
    /// tới khi phiếu in sai.
    /// </summary>
    [Fact]
    public async Task HostRefID_la_ma_dot_ma_thieu_SubjectID_thi_khong_ap_len_ai()
    {
        using var db = new InMemoryTestDb();
        var session = db.SeedSession(sessionCode: "DK-2026-001");
        var a = db.SeedRecord(session.SessionID, recordCode: "DK-2026-001-0001", patientCode: "NB0001");
        var b = db.SeedRecord(session.SessionID, recordCode: "DK-2026-001-0002", patientCode: "NB0002");

        var result = await db.Processor.ProcessAsync(
            Row(FormEventNames.SectionSigned, SectionKinds.History, hostRefId: "DK-2026-001"));
        await db.Uow.SaveChangesAsync();

        Assert.Equal(WebhookOutcome.Failed, result.Outcome);
        Assert.Equal(ExamRecordState.NotRegistered, db.RecordOf(a.RecordID).State);
        Assert.Equal(ExamRecordState.NotRegistered, db.RecordOf(b.RecordID).State);
        Assert.True(result.RecordNotFound);
    }

    /// <summary>Mọi đường tra đều lọc tenant: cùng mã hồ sơ ở hai đơn vị là hai người khác nhau.</summary>
    [Fact]
    public async Task Su_kien_cua_tenant_khac_khong_dung_vao_ho_so_nay()
    {
        using var db = new InMemoryTestDb();
        var session = db.SeedSession();
        var record = db.SeedRecord(session.SessionID, recordCode: "DK-2026-001-0001");

        var (outcome, _) = await db.Processor.ProcessAsync(
            Row(FormEventNames.SectionSigned, SectionKinds.History, divisionId: "BV-KHAC"));
        await db.Uow.SaveChangesAsync();

        Assert.Equal(WebhookOutcome.Failed, outcome);
        Assert.Equal(ExamRecordState.NotRegistered, db.RecordOf(record.RecordID).State);
    }

    /// <summary>
    /// Bản nháp KHÔNG trả SubmissionID (phiếu chỉ có mã khi được lưu thật), nên webhook là
    /// đường DUY NHẤT hồ sơ biết phiếu của mình là phiếu nào. Không chép về thì job đối soát
    /// kéo không có gì để hỏi.
    /// </summary>
    [Fact]
    public async Task Su_kien_dau_tien_chep_SubmissionID_ve_ho_so()
    {
        using var db = new InMemoryTestDb();
        var session = db.SeedSession();
        var record = db.SeedRecord(session.SessionID, ExamRecordState.NotRegistered);
        Assert.Null(record.SubmissionID);

        await db.Processor.ProcessAsync(
            Row(FormEventNames.SectionSigned, SectionKinds.History, submissionId: SubmissionId));
        await db.Uow.SaveChangesAsync();

        Assert.Equal(SubmissionId, db.RecordOf(record.RecordID).SubmissionID);
    }

    /// <summary>SubmissionID là đường tra CHẮC CHẮN NHẤT, không phụ thuộc HostRefID đang mang nghĩa gì.</summary>
    [Fact]
    public async Task Tra_duoc_ho_so_theo_SubmissionID_du_HostRefID_khong_khop_gi()
    {
        using var db = new InMemoryTestDb();
        var session = db.SeedSession();
        var record = db.SeedRecord(session.SessionID, ExamRecordState.InProgress, submissionId: SubmissionId);

        await db.Processor.ProcessAsync(
            Row(FormEventNames.SectionSigned, SectionKinds.Conclusion,
                hostRefId: "KHONG-KHOP-GI", submissionId: SubmissionId));
        await db.Uow.SaveChangesAsync();

        Assert.Equal(ExamRecordState.Completed, db.RecordOf(record.RecordID).State);
    }

    // -------------------------------------------------------- Nhật ký

    /// <summary>
    /// Chuyển tiếp do webhook ghi Action=WEBHOOK, ActorKind=Integration, ActorID=0. Người bấm
    /// nút Ký ngồi bên form-server — bịa một ActorID của IAM vào đây là ghi nhật ký sai.
    /// </summary>
    [Fact]
    public async Task Chuyen_tiep_do_webhook_ghi_nhat_ky_mang_danh_he_thong()
    {
        using var db = new InMemoryTestDb();
        var session = db.SeedSession();
        var record = db.SeedRecord(session.SessionID, ExamRecordState.NotRegistered);

        var row = Row(FormEventNames.SectionSigned, SectionKinds.History);
        row.TraceID = "TRACE-FORM-99";
        await db.Processor.ProcessAsync(row);
        await db.Uow.SaveChangesAsync();

        var audit = Assert.Single(db.AuditRowsOf(record.RecordID));
        Assert.Equal(AuditActions.Webhook, audit.Action);
        Assert.Equal(ActorKind.Integration, audit.ActorKind);
        Assert.Equal(0, audit.ActorID);
        Assert.Equal("DEV", audit.DivisionID);
        Assert.Equal("TRACE-FORM-99", audit.TraceID);
        Assert.Equal((short)ExamRecordState.NotRegistered, audit.FromState);
        Assert.Equal((short)ExamRecordState.Waiting, audit.ToState);
    }

    // ================================================================ sau review MR !5

    /// <summary>
    /// ★ Đọc NGUYÊN VĂN gói do form-server THẬT phát ra (chép từ HEX_WebhookInbox.Payload của
    /// phiên nghiệm thu đầu-cuối, docs/handoff/20260824-236-e2e-hai-service.md).
    ///
    /// Vì sao phải có ca này: mọi test khác trong file serialize CHÍNH model của bên nhận rồi
    /// đọc lại, nên chúng không bao giờ đỏ dù dây thật lệch — đúng nhận xét của 236 về chất
    /// lượng bộ test. Ca này là ca duy nhất nói được "hai bên còn khớp nhau".
    /// </summary>
    [Fact]
    public async Task Goi_THAT_cua_form_server_state_changed_duoc_ap_dung()
    {
        const string payload = """
            { "Data": { "State": 1, "Reason": "SECTION_SAVED", "ActorID": 10234, "ToState": 1,
                        "ActorKind": 1, "FromState": 0, "SectionID": 12, "StateName": "Đang khám",
                        "SectionKind": "CLINICAL_EXAM" },
              "Event": "submission.state.changed", "EventID": "bb3b621c-b49f-4f9b-831a-f327038e9bb2",
              "TraceID": "01M0T5CJ4KZDZG4M64J0QPF1EA", "HostRefID": "KSK-E2E2-236",
              "SubjectID": "KSK-E2E2-236-0002", "DivisionID": "DEV",
              "OccurredAt": "2026-08-24T22:14:25.0521579+07:00", "HostRefType": "HEALTH_EXAM_SESSION",
              "SubmissionID": "04243d31-6255-4bf1-b6a1-700174c51684" }
            """;

        using var db = new InMemoryTestDb();
        var session = db.SeedSession(sessionCode: "KSK-E2E2-236");
        var record = db.SeedRecord(session.SessionID, ExamRecordState.Waiting,
            recordCode: "KSK-E2E2-236-0002");

        var result = await db.Processor.ProcessAsync(RawRow(payload, "submission.state.changed",
            new DateTime(2026, 8, 24, 15, 14, 25, DateTimeKind.Utc)));
        await db.Uow.SaveChangesAsync();

        Assert.Equal(WebhookOutcome.Applied, result.Outcome);

        var saved = db.RecordOf(record.RecordID);
        Assert.Equal(ExamRecordState.InProgress, saved.State);
        // SubjectID = RecordCode là đường tra CHÍNH (chốt 236 §3) — gói thật đi qua đúng đường đó.
        Assert.Equal(Guid.Parse("04243d31-6255-4bf1-b6a1-700174c51684"), saved.SubmissionID);
    }

    /// <summary>★ Gói THẬT của sự kiện ký KẾT LUẬN — chốt luôn Loại sức khoẻ đi kèm.</summary>
    [Fact]
    public async Task Goi_THAT_cua_form_server_conclusion_mang_HealthClassCode()
    {
        const string payload = """
            { "Data": { "State": 3, "ActorID": 10234, "Progress": {"Total":17,"Completed":3},
                        "SignedAt": "2026-08-24T22:15:39.3709163+07:00", "ActorKind": 1, "SectionID": 35,
                        "StateName": "Đã khám", "SectionCode": "HEX_SEC_CONCLUSION",
                        "SectionKind": "CONCLUSION", "HealthClassCode": "II" },
              "Event": "submission.section.signed", "EventID": "b8f43924-986e-4d13-870f-7c749e34cd82",
              "HostRefID": "KSK-E2E2-236", "SubjectID": "KSK-E2E2-236-0002", "DivisionID": "DEV",
              "SubmissionID": "04243d31-6255-4bf1-b6a1-700174c51684" }
            """;

        using var db = new InMemoryTestDb();
        var session = db.SeedSession(sessionCode: "KSK-E2E2-236");
        var record = db.SeedRecord(session.SessionID, ExamRecordState.InProgress,
            recordCode: "KSK-E2E2-236-0002");

        var result = await db.Processor.ProcessAsync(RawRow(payload, "submission.section.signed"));
        await db.Uow.SaveChangesAsync();

        Assert.Equal(WebhookOutcome.Applied, result.Outcome);
        var saved = db.RecordOf(record.RecordID);
        Assert.Equal(ExamRecordState.Completed, saved.State);
        Assert.Equal("II", saved.HealthClassCode);
    }

    /// <summary>
    /// ★ Bản form-server CHƯA vá gửi ToState mà không gửi State. Không nhận bí danh thì
    /// State=0 (Draft), mọi gói state.changed bị bỏ qua, hồ sơ kẹt "Chờ khám" — hỏng CÂM.
    /// </summary>
    [Fact]
    public async Task Goi_chi_co_ToState_van_duoc_doc_dung()
    {
        const string payload = """
            { "Data": { "FromState": 0, "ToState": 1, "StateName": "Đang khám", "Reason": "SECTION_SAVED" },
              "Event": "submission.state.changed", "EventID": "EVT-TOSTATE",
              "HostRefID": "DK-2026-001", "SubjectID": "DK-2026-001-0001", "DivisionID": "DEV" }
            """;

        using var db = new InMemoryTestDb();
        var session = db.SeedSession();
        var record = db.SeedRecord(session.SessionID, ExamRecordState.Waiting);

        var result = await db.Processor.ProcessAsync(RawRow(payload, "submission.state.changed"));
        await db.Uow.SaveChangesAsync();

        Assert.Equal(WebhookOutcome.Applied, result.Outcome);
        Assert.Equal(ExamRecordState.InProgress, db.RecordOf(record.RecordID).State);
    }

    /// <summary>
    /// ★ M5/N2 — job đối soát chữa 2→3 TRƯỚC, gói CONCLUSION thật tới SAU.
    ///
    /// Trước khi vá, nhánh "hồ sơ đã ở đích" bỏ qua trắng và hồ sơ mất Loại sức khoẻ VĨNH
    /// VIỄN: đường kéo /progress không mang HealthClassCode, còn đường đẩy thì vừa bị bỏ qua.
    /// </summary>
    [Fact]
    public async Task Doi_soat_chua_truoc_thi_webhook_van_bo_sung_HealthClassCode()
    {
        using var db = new InMemoryTestDb();
        var session = db.SeedSession();
        // Đúng dấu vết job đối soát để lại: State=3 nhưng KHÔNG có Loại sức khoẻ.
        var record = db.SeedRecord(session.SessionID, ExamRecordState.Completed);

        var result = await db.Processor.ProcessAsync(
            Row(FormEventNames.SectionSigned, SectionKinds.Conclusion, healthClassCode: "III"));
        await db.Uow.SaveChangesAsync();

        Assert.Equal(WebhookOutcome.Applied, result.Outcome);
        var saved = db.RecordOf(record.RecordID);
        Assert.Equal("III", saved.HealthClassCode);
        Assert.Equal(ExamRecordState.Completed, saved.State);
    }

    /// <summary>Đã đủ dữ liệu rồi thì vẫn là Skipped — không đẻ ra dòng audit thừa.</summary>
    [Fact]
    public async Task Ho_so_da_o_dich_va_da_du_du_lieu_thi_van_bo_qua()
    {
        using var db = new InMemoryTestDb();
        var session = db.SeedSession();
        var record = db.SeedRecord(session.SessionID, ExamRecordState.Completed);

        var tracked = await db.Db.ExamRecords.AsTracking().FirstAsync(x => x.RecordID == record.RecordID);
        tracked.HealthClassCode = "II";
        tracked.ExamFinishedAt = T0;
        await db.Uow.SaveChangesAsync();
        db.Db.ChangeTracker.Clear();

        var result = await db.Processor.ProcessAsync(
            Row(FormEventNames.SectionSigned, SectionKinds.Conclusion, healthClassCode: "II"));

        Assert.Equal(WebhookOutcome.Skipped, result.Outcome);
        Assert.False(result.InvalidTransition);
    }

    /// <summary>
    /// ★ BLOCKER 1 — HealthClassCode dài hơn cột thì BỎ giá trị, KHÔNG gán.
    ///
    /// Gán mù làm lệnh ghi hỏng ở tầng DB, và trước khi worker tách transaction theo hàng thì
    /// đúng một ô như thế làm ĐỨNG cả hàng đợi. Chuyển tiếp trạng thái vẫn phải áp: sự kiện
    /// khám là thật, chỉ có ô phân loại là rác.
    /// </summary>
    [Fact]
    public async Task HealthClassCode_qua_dai_thi_bo_gia_tri_nhung_van_ap_chuyen_tiep()
    {
        using var db = new InMemoryTestDb();
        var session = db.SeedSession();
        var record = db.SeedRecord(session.SessionID, ExamRecordState.InProgress);

        var result = await db.Processor.ProcessAsync(
            Row(FormEventNames.SectionSigned, SectionKinds.Conclusion,
                healthClassCode: new string('X', RecordFieldLengths.HealthClassCode + 10)));
        await db.Uow.SaveChangesAsync();

        Assert.Equal(WebhookOutcome.Applied, result.Outcome);
        var saved = db.RecordOf(record.RecordID);
        Assert.Equal(ExamRecordState.Completed, saved.State);
        Assert.Equal("", saved.HealthClassCode);
    }

    /// <summary>
    /// ★ Con trỏ SubmissionID chỉ chép LẦN ĐẦU (hợp đồng §5). Ghi đè thì hai phiếu cùng trỏ
    /// một hồ sơ sẽ làm con trỏ nhảy, và job đối soát từ đó hỏi nhầm phiếu.
    /// </summary>
    [Fact]
    public async Task SubmissionID_khong_bi_ghi_de_boi_phieu_thu_hai()
    {
        var first = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var second = Guid.Parse("22222222-2222-2222-2222-222222222222");

        using var db = new InMemoryTestDb();
        var session = db.SeedSession();
        var record = db.SeedRecord(session.SessionID, ExamRecordState.NotRegistered, submissionId: first);

        await db.Processor.ProcessAsync(
            Row(FormEventNames.SectionSigned, SectionKinds.History, submissionId: second, eventId: "EVT-2"));
        await db.Uow.SaveChangesAsync();

        Assert.Equal(first, db.RecordOf(record.RecordID).SubmissionID);
    }

    /// <summary>Tiến độ âm hoặc Completed &gt; Total không được ghi thẳng vào cache.</summary>
    [Theory]
    [InlineData(99, -3, false, 0, 0)]     // số âm  -> bỏ hẳn
    [InlineData(40, 17, true, 17, 17)]    // vượt trần -> kẹp về Total
    public async Task Tien_do_khong_hop_le_khong_lam_ban_cache(
        short done, short total, bool applied, short expectedDone, short expectedTotal)
    {
        using var db = new InMemoryTestDb();
        var session = db.SeedSession();
        var record = db.SeedRecord(session.SessionID, ExamRecordState.InProgress);

        var result = await db.Processor.ProcessAsync(
            Row(FormEventNames.SectionSigned, SectionKinds.ClinicalExam,
                progress: new FormProgressCount { Completed = done, Total = total }));
        await db.Uow.SaveChangesAsync();

        Assert.Equal(applied ? WebhookOutcome.Applied : WebhookOutcome.Skipped, result.Outcome);
        var saved = db.RecordOf(record.RecordID);
        Assert.Equal(expectedDone, saved.ProgressDone);
        Assert.Equal(expectedTotal, saved.ProgressTotal);
    }

    /// <summary>Hồ sơ đã HUỶ là trạng thái cuối — cache tiến độ cũng phải dừng theo.</summary>
    [Theory]
    [InlineData(ExamRecordState.RegistrationCancelled)]
    [InlineData(ExamRecordState.ExamCancelled)]
    public async Task Ho_so_da_huy_khong_bi_nhich_cache_tien_do(ExamRecordState state)
    {
        using var db = new InMemoryTestDb();
        var session = db.SeedSession();
        var record = db.SeedRecord(session.SessionID, state);

        var result = await db.Processor.ProcessAsync(
            Row(FormEventNames.SectionSigned, SectionKinds.ClinicalExam,
                progress: new FormProgressCount { Completed = 5, Total = 17 }));
        await db.Uow.SaveChangesAsync();

        Assert.Equal(WebhookOutcome.Skipped, result.Outcome);
        Assert.Equal(0, db.RecordOf(record.RecordID).ProgressDone);
    }

    /// <summary>
    /// ★ M6 — hai tên bên phát CÓ phát mà bên nhận không dùng phải được nhận diện, không rơi
    /// vào rổ "sự kiện lạ". Chúng chiếm 44% lưu lượng; gộp vào là làm hỏng chỉ số cảnh báo.
    /// </summary>
    [Theory]
    [InlineData("submission.created")]
    [InlineData("submission.section.saved")]
    public void Ten_su_kien_biet_nhung_khong_tieu_thu_duoc_nhan_dien_rieng(string name)
    {
        Assert.False(FormEventNames.IsKnown(name));
        Assert.True(FormEventNames.IsKnownIgnored(name));
    }

    [Fact]
    public void Ten_su_kien_thuc_su_la_thi_khong_bi_nham_sang_nhom_bo_qua()
    {
        Assert.False(FormEventNames.IsKnown("submission.something.new"));
        Assert.False(FormEventNames.IsKnownIgnored("submission.something.new"));
    }

    /// <summary>
    /// ★ M2 — giãn cách phải TĂNG theo số lần thử. Không giãn thì 5 lượt cháy hết trong ~2
    /// giây, tức toàn bộ ngân sách thử lại tiêu xong trước khi một sự cố tạm thời kịp qua.
    /// </summary>
    [Fact]
    public void Gian_cach_thu_lai_tang_dan()
    {
        Assert.Equal(TimeSpan.FromSeconds(30), WebhookWorker.BackoffFor(1));
        Assert.Equal(TimeSpan.FromMinutes(1), WebhookWorker.BackoffFor(2));
        Assert.Equal(TimeSpan.FromMinutes(2), WebhookWorker.BackoffFor(3));
        Assert.Equal(TimeSpan.FromMinutes(4), WebhookWorker.BackoffFor(4));

        // Tổng ngân sách 5 lượt phải đủ dài để một lần restart của bên kia đi qua.
        var total = TimeSpan.Zero;
        for (var i = 1; i <= WebhookWorker.MaxRetry; i++) total += WebhookWorker.BackoffFor(i);
        Assert.True(total > TimeSpan.FromMinutes(15), $"Tổng giãn cách chỉ {total.TotalMinutes:0.#} phút");
    }

    /// <summary>Dựng hàng inbox từ NGUYÊN VĂN một gói json — không serialize lại model của bên nhận.</summary>
    // ================================================================ §4.7 review lần 2 MR !5

    /// <summary>
    /// ★ §4.7 — gói CONCLUSION THẬT tới sau job đối soát phải ĐÈ được mốc giờ khám xong.
    ///
    /// Đây đúng kịch bản M5 sinh ra để chữa: gói mất (hoặc được nạp lại bằng webhook-requeue),
    /// job chữa 2→3 trước và đặt ExamFinishedAt = GIỜ CHẠY JOB. Bản vá M5 chỉ ghi khi mốc còn
    /// TRỐNG, nên giờ khám xong THẬT trong gói bị bỏ và hồ sơ giữ giờ chạy job VĨNH VIỄN —
    /// một mốc nghiệp vụ sai không chữa lại được, và không chỗ nào báo lỗi.
    /// </summary>
    [Fact]
    public async Task Goi_CONCLUSION_that_de_duoc_len_moc_gio_do_job_uoc_luong()
    {
        using var db = new InMemoryTestDb();
        var session = db.SeedSession();
        var record = db.SeedRecord(session.SessionID, ExamRecordState.Completed);

        var jobTime = new DateTime(2026, 8, 24, 10, 0, 0, DateTimeKind.Utc);
        db.SetExamFinished(record.RecordID, jobTime, estimated: true);

        var realTime = new DateTime(2026, 8, 24, 3, 30, 0, DateTimeKind.Utc);
        var result = await db.Processor.ProcessAsync(
            Row(FormEventNames.SectionSigned, SectionKinds.Conclusion,
                occurredAt: realTime, healthClassCode: "III"));
        await db.Uow.SaveChangesAsync();

        Assert.Equal(WebhookOutcome.Applied, result.Outcome);
        var saved = db.RecordOf(record.RecordID);
        Assert.Equal(realTime, saved.ExamFinishedAt);      // giờ THẬT, không phải giờ chạy job
        Assert.False(saved.ExamFinishedAtEstimated);       // hết là ước lượng
        Assert.Equal("III", saved.HealthClassCode);
    }

    /// <summary>
    /// …nhưng mốc do MỘT SỰ KIỆN THẬT ghi trước đó thì KHÔNG bị đè. Chỉ mốc ước lượng mới
    /// nhường chỗ — nếu không, thứ tự đến của gói mạng sẽ quyết định dữ liệu lâm sàng.
    /// </summary>
    [Fact]
    public async Task Moc_gio_do_su_kien_that_ghi_thi_khong_bi_goi_den_sau_de_len()
    {
        using var db = new InMemoryTestDb();
        var session = db.SeedSession();
        var record = db.SeedRecord(session.SessionID, ExamRecordState.Completed);

        var firstReal = new DateTime(2026, 8, 24, 3, 0, 0, DateTimeKind.Utc);
        db.SetExamFinished(record.RecordID, firstReal, estimated: false);

        var result = await db.Processor.ProcessAsync(
            Row(FormEventNames.SectionSigned, SectionKinds.Conclusion,
                occurredAt: new DateTime(2026, 8, 24, 9, 0, 0, DateTimeKind.Utc)));
        await db.Uow.SaveChangesAsync();

        Assert.Equal(firstReal, db.RecordOf(record.RecordID).ExamFinishedAt);
        Assert.Equal(WebhookOutcome.Skipped, result.Outcome);
    }

    /// <summary>
    /// Cùng một lỗi ở ĐẦU KIA của ca khám: job chữa 1→2 rồi đặt ExamStartedAt = giờ chạy job,
    /// gói state.changed thật tới sau phải thay được bằng giờ thật.
    /// </summary>
    [Fact]
    public async Task Goi_state_changed_that_de_duoc_len_moc_bat_dau_kham_uoc_luong()
    {
        using var db = new InMemoryTestDb();
        var session = db.SeedSession();
        var record = db.SeedRecord(session.SessionID, ExamRecordState.InProgress);

        var jobTime = new DateTime(2026, 8, 24, 10, 0, 0, DateTimeKind.Utc);
        db.SetExamStarted(record.RecordID, jobTime, estimated: true);

        var realTime = new DateTime(2026, 8, 24, 2, 15, 0, DateTimeKind.Utc);
        var result = await db.Processor.ProcessAsync(
            Row(FormEventNames.StateChanged, occurredAt: realTime,
                state: FormSubmissionStates.InProgress));
        await db.Uow.SaveChangesAsync();

        Assert.Equal(WebhookOutcome.Applied, result.Outcome);
        var saved = db.RecordOf(record.RecordID);
        Assert.Equal(realTime, saved.ExamStartedAt);
        Assert.False(saved.ExamStartedAtEstimated);
    }

    /// <summary>
    /// Chuyển tiếp BÌNH THƯỜNG (không có job xen vào) không được đánh dấu nhầm là ước lượng —
    /// nếu không thì một gói lạc đến sau sẽ đè lên giờ khám thật.
    /// </summary>
    [Fact]
    public async Task Chuyen_tiep_binh_thuong_ghi_moc_THAT_khong_phai_uoc_luong()
    {
        using var db = new InMemoryTestDb();
        var session = db.SeedSession();
        var record = db.SeedRecord(session.SessionID, ExamRecordState.InProgress);

        await db.Processor.ProcessAsync(Row(FormEventNames.SectionSigned, SectionKinds.Conclusion));
        await db.Uow.SaveChangesAsync();

        var saved = db.RecordOf(record.RecordID);
        Assert.NotNull(saved.ExamFinishedAt);
        Assert.False(saved.ExamFinishedAtEstimated);
    }

    private static WebhookInbox RawRow(string payload, string eventType, DateTime? occurredAt = null)
    {
        var envelope = JsonConvert.DeserializeObject<FormWebhookEvent>(payload);
        return new WebhookInbox
        {
            EventID = envelope.EventID,
            EventType = eventType,
            DivisionID = "DEV",
            SubmissionID = envelope.SubmissionID,
            OccurredAt = occurredAt ?? envelope.OccurredAt?.UtcDateTime ?? T0,
            ReceivedAt = DateTime.UtcNow,
            Payload = payload
        };
    }
}
