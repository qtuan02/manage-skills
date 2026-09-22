using HealthExam.Application.Webhooks;
using HealthExam.API.Contracts;
using HealthExam.API.Middlewares;
using HealthExam.Application.Common;
using HealthExam.Domain.Common;
using HealthExam.Domain.ExamSessions;
using HealthExam.Domain.ExamRecords;
using HealthExam.Domain.Imports;
using HealthExam.Domain.Paraclinical;
using HealthExam.Domain.Webhooks;
using HealthExam.Domain.Integrations;
using HealthExam.Domain.Catalogs;
using HealthExam.Infrastructure.BackgroundJobs;
using HealthExam.Server.Service;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using Xunit;

namespace HealthExam.Tests;

/// <summary>
/// H2-04 — chiều NHẬN của webhook: hộp thư và chống trùng.
///
/// Kiểm ở tầng service trên DB trong bộ nhớ. ⚠️ Provider in-memory KHÔNG có chỉ mục UNIQUE,
/// nên bộ này chốt được NHÁNH XỬ LÝ trùng (trả Duplicated, không ghi hàng thứ hai) chứ KHÔNG
/// chốt được ràng buộc UNIQUE(EventID) — ràng buộc đó chỉ chứng minh được trên PostgreSQL
/// thật, xem phần curl trong docs/handoff/20260824-h2-hop-dong-webhook-form-server.md.
/// </summary>
public class WebhookIngestTests
{
    private static string Raw(FormWebhookEvent evt) => JsonConvert.SerializeObject(evt);

    /// <summary>
    /// Gói mẫu. <paramref name="divisionId"/> mặc định KHỚP tenant của context — vì sau §4.3
    /// (review lần 2 MR !5) trường này là BẮT BUỘC và phải khớp header từng byte, nên một gói
    /// "hợp lệ" không thể thiếu nó nữa. Truyền giá trị khác để dựng các nhánh từ chối.
    /// </summary>
    private static FormWebhookEvent Event(
        string eventId = "EVT-0001",
        string name = FormEventNames.SectionSigned,
        string sectionKind = SectionKinds.History,
        string hostRefId = "DK-2026-001-0001",
        string divisionId = FakeHealthExamContext.DefaultDivisionId) => new()
    {
        EventID = eventId,
        Event = name,
        OccurredAt = new DateTime(2026, 8, 24, 3, 0, 0, DateTimeKind.Utc),
        HostRefType = ModuleCodes.HostRefType,
        HostRefID = hostRefId,
        DivisionID = divisionId,
        SubmissionID = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001"),
        Data = new FormWebhookData { SectionKind = sectionKind }
    };

    [Fact]
    public async Task Thieu_EventID_tra_4001()
    {
        using var db = new InMemoryTestDb();
        var evt = Event(eventId: "");

        var ex = await Assert.ThrowsAsync<HealthExamException>(() => db.Webhooks.ReceiveAsync(evt, Raw(evt)));

        Assert.Equal(ErrorCodes.BadRequest, ex.ErrorCode);
        Assert.Empty(db.InboxRows());
    }

    /// <summary>
    /// Gate 03-task H2-04: cùng EventID gửi 2 lần → lần 2 phải là 200 + Duplicated, KHÔNG
    /// phải 409. Với bên phát, 4xx là "gửi hỏng, gửi lại" — trả 409 cho gói đã xử lý thành
    /// công là tự tạo một vòng retry vô tận.
    /// </summary>
    [Fact]
    public async Task Cung_EventID_gui_hai_lan_chi_ghi_MOT_hang()
    {
        using var db = new InMemoryTestDb();
        var evt = Event();

        var first = await db.Webhooks.ReceiveAsync(evt, Raw(evt));
        var second = await db.Webhooks.ReceiveAsync(evt, Raw(evt));

        Assert.False(first.Duplicated);
        Assert.True(second.Duplicated);
        Assert.Single(db.InboxRows());
        Assert.Equal(1, db.Metrics.Duplicated);
    }

    /// <summary>
    /// Sự kiện lạ KHÔNG được trả lỗi: form-server phát cho mọi host, phần lớn sự kiện của nó
    /// không liên quan tới KSK — trả lỗi là bắt nó retry vĩnh viễn (02-api-spec §5.2).
    /// Vẫn GHI LẠI để đối soát được bên kia đang phát những gì.
    /// </summary>
    [Fact]
    public async Task Su_kien_la_duoc_ghi_lai_va_bo_qua_khong_bao_loi()
    {
        using var db = new InMemoryTestDb();
        var evt = Event(name: "submission.something.else");

        var ack = await db.Webhooks.ReceiveAsync(evt, Raw(evt));

        Assert.False(ack.Accepted);
        Assert.False(ack.Duplicated);

        var row = Assert.Single(db.InboxRows());
        // Skipped NGAY lúc nhận, không xếp hàng chờ worker: worker sẽ chỉ làm đúng việc đánh
        // dấu bỏ qua, mà hàng đợi thì phải để dành cho sự kiện thật.
        Assert.Equal(WebhookProcessState.Skipped, row.ProcessState);
        Assert.Equal(1, db.Metrics.UnknownEvent);
    }

    /// <summary>
    /// Payload lưu NGUYÊN VĂN chuỗi nhận được, không phải bản dựng lại từ đối tượng đã bind:
    /// dựng lại là mất mọi trường ta chưa khai kiểu — đúng những trường cần nhất khi đối soát
    /// một sự việc lạ.
    /// </summary>
    [Fact]
    public async Task Payload_giu_nguyen_van_ke_ca_truong_chua_khai_kieu()
    {
        using var db = new InMemoryTestDb();
        var evt = Event();
        var raw = """
                  {"EventID":"EVT-0001","Event":"submission.section.signed",
                   "OccurredAt":"2026-08-24T03:00:00Z","HostRefID":"DK-2026-001-0001",
                   "Data":{"SectionKind":"HISTORY"},"TruongLa":{"Sau":"nay"}}
                  """;

        await db.Webhooks.ReceiveAsync(evt, raw);

        Assert.Contains("TruongLa", db.InboxRows()[0].Payload);
    }

    /// <summary>
    /// Tenant được lưu vào hàng hộp thư LÚC NHẬN. Không lưu thì worker — chạy ngoài mọi
    /// request — không có gì để lọc, và RecordCode trùng ở hai đơn vị sẽ khớp nhầm người.
    ///
    /// Sau §4.2 giá trị lưu là của THÂN GÓI ĐÃ KÝ chứ không phải của header; hai vế bắt buộc
    /// khớp từng byte nên với gói hợp lệ chúng luôn bằng nhau — điều test này chốt là hàng
    /// KHÔNG bao giờ mang một tenant khác với tenant đã ký.
    /// </summary>
    [Fact]
    public async Task Hang_hop_thu_giu_tenant_cua_luc_nhan()
    {
        using var db = new InMemoryTestDb(new FakeHealthExamContext { DivisionId = "BV02" });
        var evt = Event(divisionId: "BV02");

        await db.Webhooks.ReceiveAsync(evt, Raw(evt));

        Assert.Equal("BV02", db.InboxRows()[0].DivisionID);
    }

    /// <summary>
    /// Thiếu OccurredAt thì rơi về giờ nhận chứ KHÔNG từ chối: từ chối là mất hẳn một sự kiện
    /// khám thật để đổi lấy sự nghiêm ngặt, còn rơi về giờ nhận chỉ làm hỏng thứ tự của đúng
    /// gói đó (và đã có log cảnh báo).
    /// </summary>
    [Fact]
    public async Task Thieu_OccurredAt_thi_lay_gio_nhan_chu_khong_tu_choi()
    {
        using var db = new InMemoryTestDb();
        var evt = Event();
        evt.OccurredAt = null;

        await db.Webhooks.ReceiveAsync(evt, Raw(evt));

        var row = db.InboxRows()[0];
        Assert.Equal(row.ReceivedAt, row.OccurredAt);
        Assert.Equal(WebhookProcessState.New, row.ProcessState);
    }

    // ================================================================ sau review MR !5

    /// <summary>
    /// ★ BLOCKER 2 — DivisionID trong thân gói ĐÃ KÝ lệch header thì TỪ CHỐI.
    ///
    /// Không có phép đối chiếu này thì giữ nguyên byte thân gói + chữ ký hợp lệ và chỉ đổi
    /// header X-Division-Id là áp được sự kiện lên hồ sơ cùng mã của tenant khác — đo được
    /// trên service thật. Tệ hơn: EventID bị tiêu thụ ở tenant sai nên gói THẬT gửi sau đó
    /// nhận "Duplicated" và mất hẳn.
    ///
    /// Từ chối (không ghi hàng nào) chứ không "tin thân gói rồi ghi": lệch nghĩa là có người
    /// sửa header hoặc cấu hình proxy sai, và từ chối thì EventID chưa bị tiêu thụ nên gói
    /// thật vẫn vào được sau khi sửa.
    /// </summary>
    [Fact]
    public async Task DivisionID_trong_than_goi_lech_header_thi_tu_choi_va_khong_ghi_gi()
    {
        using var db = new InMemoryTestDb();
        var evt = Event();
        evt.DivisionID = "BV-KHAC";

        var ex = await Assert.ThrowsAsync<HealthExamException>(() => db.Webhooks.ReceiveAsync(evt, Raw(evt)));

        Assert.Equal(ErrorCodes.BadRequest, ex.ErrorCode);
        Assert.Empty(db.InboxRows());
    }

    /// <summary>Khớp thì đi tiếp bình thường — phép đối chiếu không được chặn đường thật.</summary>
    [Fact]
    public async Task DivisionID_khop_header_thi_nhan_binh_thuong()
    {
        using var db = new InMemoryTestDb();
        var evt = Event();
        evt.DivisionID = db.Ctx.DivisionId;

        var result = await db.Webhooks.ReceiveAsync(evt, Raw(evt));

        Assert.False(result.Duplicated);
        Assert.True(result.Accepted);
        Assert.Single(db.InboxRows());
    }

    /// <summary>
    /// ★ §4.3 — gói KHÔNG khai DivisionID bị TỪ CHỐI, không còn tụt về tin header.
    ///
    /// Đường cũ làm chốt chặn tenant thành TUỲ CHỌN CỦA BÊN GỬI: bỏ trường đó ra là bên nhận
    /// chỉ còn header để tin, mà header nằm ngoài vùng chữ ký. Bộ phát luôn ký DivisionID vào
    /// thân gói nên không có ai bị mất gói vì luật này.
    /// </summary>
    [Fact]
    public async Task Goi_khong_khai_DivisionID_bi_TU_CHOI()
    {
        using var db = new InMemoryTestDb();
        var evt = Event(divisionId: "");

        var ex = await Assert.ThrowsAsync<HealthExamException>(() => db.Webhooks.ReceiveAsync(evt, Raw(evt)));

        Assert.Equal(ErrorCodes.BadRequest, ex.ErrorCode);
        // Từ chối TRƯỚC mọi thao tác ghi ⇒ EventID chưa bị tiêu thụ, gói thật vẫn vào được sau
        // khi bên phát sửa. Đây là điểm phân biệt "từ chối" với "nhận rồi chết ở trong".
        Assert.Empty(db.InboxRows());
    }

    /// <summary>
    /// ★ §4.2 — lệch HOA/THƯỜNG cũng là lệch. Bản trước so bằng OrdinalIgnoreCase rồi LƯU giá
    /// trị header: cùng một EventID vào được HAI hàng, và hàng thứ hai mang tenant viết thường
    /// mà PostgreSQL so '=' có phân biệt hoa thường ⇒ không bao giờ tra ra hồ sơ, thử lại đủ
    /// lượt rồi nằm dead-letter. Triệu chứng phía người dùng là hồ sơ ĐỨNG IM.
    /// </summary>
    [Fact]
    public async Task Lech_hoa_thuong_cua_tenant_bi_TU_CHOI_chu_khong_lot_vao_roi_chet_o_trong()
    {
        using var db = new InMemoryTestDb();
        var evt = Event(divisionId: FakeHealthExamContext.DefaultDivisionId.ToLowerInvariant());

        var ex = await Assert.ThrowsAsync<HealthExamException>(() => db.Webhooks.ReceiveAsync(evt, Raw(evt)));

        Assert.Equal(ErrorCodes.BadRequest, ex.ErrorCode);
        Assert.Empty(db.InboxRows());
    }

    /// <summary>
    /// ★ §4.2 — hàng inbox lưu giá trị ĐÃ KÝ trong thân gói, không lưu header.
    /// </summary>
    [Fact]
    public async Task Hang_inbox_luu_DivisionID_cua_THAN_GOI_da_ky()
    {
        using var db = new InMemoryTestDb();
        var evt = Event();

        var result = await db.Webhooks.ReceiveAsync(evt, Raw(evt));

        Assert.False(result.Duplicated);
        Assert.Equal(evt.DivisionID, db.InboxRows().Single().DivisionID);
    }

    /// <summary>
    /// ★ M1 — EventID dài hơn cột trả 4001, KHÔNG để lệnh INSERT ném 22001 thành HTTP 500.
    /// Bên phát đọc 5xx là "thử lại được" nên 500 ở đây là một vòng retry vĩnh viễn cho gói
    /// không bao giờ ghi được.
    /// </summary>
    [Fact]
    public async Task EventID_dai_hon_tran_tra_4001_chu_khong_phai_500()
    {
        using var db = new InMemoryTestDb();
        var evt = Event(eventId: new string('E', WebhookFieldLengths.EventID + 20));

        var ex = await Assert.ThrowsAsync<HealthExamException>(() => db.Webhooks.ReceiveAsync(evt, Raw(evt)));

        Assert.Equal(ErrorCodes.BadRequest, ex.ErrorCode);
        Assert.Empty(db.InboxRows());
    }

    /// <summary>TraceID dài quá thì CẮT, không từ chối: một mã lần vết không đáng để mất một
    /// sự kiện khám. traceparent W3C dài 55 ký tự trong khi cột là 50.</summary>
    [Fact]
    public async Task TraceID_dai_hon_tran_thi_bi_cat_chu_khong_lam_hong_goi()
    {
        using var db = new InMemoryTestDb(new FakeHealthExamContext { TraceId = new string('T', 80) });
        var evt = Event();

        var result = await db.Webhooks.ReceiveAsync(evt, Raw(evt));

        Assert.False(result.Duplicated);
        Assert.Equal(WebhookFieldLengths.TraceID, db.InboxRows().Single().TraceID.Length);
    }

    /// <summary>
    /// ★ M7 — nạp lại dead-letter. Cần vì bên phát coi 4xx là hỏng VĨNH VIỄN và chỉ thử một
    /// lần: một cửa sổ lệch bí mật chữ ký là mất trắng sự kiện mà không đầu nào phát lại được.
    /// </summary>
    [Fact]
    public async Task Nap_lai_dead_letter_dua_hang_ve_hang_doi()
    {
        using var db = new InMemoryTestDb();
        var evt = Event();
        await db.Webhooks.ReceiveAsync(evt, Raw(evt));

        var row = await db.Db.WebhookInboxes.AsTracking().FirstAsync();
        row.ProcessState = WebhookProcessState.Failed;
        row.RetryCount = WebhookWorker.MaxRetry;
        row.NextAttemptAt = DateTime.UtcNow.AddHours(1);
        await db.Uow.SaveChangesAsync();
        db.Db.ChangeTracker.Clear();

        var result = await db.Webhooks.RequeueDeadLettersAsync();

        Assert.Equal(1, result.Requeued);
        Assert.Equal(evt.EventID, result.EventIDs.Single());

        var after = db.InboxRows().Single();
        Assert.Equal(WebhookProcessState.New, after.ProcessState);
        Assert.Equal(0, after.RetryCount);
        Assert.Null(after.NextAttemptAt);
    }

    /// <summary>
    /// ★ minor §4.4 — <c>max</c> phải kẹp CẢ HAI ĐẦU. Bản trước chỉ chặn phía dưới, nên
    /// <c>?max=2147483647</c> kéo toàn bộ dead-letter vào bộ nhớ rồi ghi trong MỘT SaveChanges
    /// — một lời gọi tay đúng lúc đang sự cố là cách làm sự cố nặng thêm.
    /// </summary>
    [Fact]
    public async Task Nap_lai_dead_letter_kep_tran_TREN_cua_max()
    {
        using var db = new InMemoryTestDb();

        for (var i = 1; i <= TestWebhookIngestService.MaxRequeue + 5; i++)
        {
            var evt = Event(eventId: $"EVT-DL-{i:D5}");
            await db.Webhooks.ReceiveAsync(evt, Raw(evt));
        }
        foreach (var row in await db.Db.WebhookInboxes.AsTracking().ToListAsync())
        {
            row.ProcessState = WebhookProcessState.Failed;
            row.RetryCount = WebhookWorker.MaxRetry;
        }
        await db.Uow.SaveChangesAsync();
        db.Db.ChangeTracker.Clear();

        var result = await db.Webhooks.RequeueDeadLettersAsync(max: int.MaxValue);

        Assert.Equal(TestWebhookIngestService.MaxRequeue, result.Requeued);
    }

    /// <summary>Hàng CÒN lượt thử không bị đụng tới: worker tự lo, và ép chạy sớm chỉ làm
    /// hỏng phần giãn cách vừa thêm vào.</summary>
    [Fact]
    public async Task Nap_lai_khong_dung_toi_hang_con_luot_thu()
    {
        using var db = new InMemoryTestDb();
        var evt = Event();
        await db.Webhooks.ReceiveAsync(evt, Raw(evt));

        var row = await db.Db.WebhookInboxes.AsTracking().FirstAsync();
        row.ProcessState = WebhookProcessState.Failed;
        row.RetryCount = 2;
        await db.Uow.SaveChangesAsync();
        db.Db.ChangeTracker.Clear();

        var result = await db.Webhooks.RequeueDeadLettersAsync();

        Assert.Equal(0, result.Requeued);
        Assert.Equal(WebhookProcessState.Failed, db.InboxRows().Single().ProcessState);
    }
}
