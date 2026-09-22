using Microsoft.EntityFrameworkCore;
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
using HealthExam.Server.Service;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace HealthExam.Tests;

/// <summary>
/// H3-09 — nghe <c>submission.attachment.added/.removed</c> (AttachKind=SCAN_RESULT) để
/// đóng/mở điều kiện (B).
///
/// 🔴 VÌ SAO ĐƯỜNG NÀY KHÔNG PHẢI "LỐI LUI KHI CHƯA CÓ LIS": vendor RIS chỉ báo được tới
/// "Đang thực hiện" — pacs-connect-server chặn cứng mọi NewStatus ngoài {0,1}
/// (docs/handoff/20260826-236-chot-p3-cls.md §5.1, có số đo). Nên đây là đường TỰ ĐỘNG DUY
/// NHẤT đưa một dòng dịch vụ lên "Đã trả KQ", kể cả ở cơ sở đã có PACS/RIS chạy tốt.
/// </summary>
public class ParaclinicalScanResultTests
{
    private static readonly DateTime T0 = new(2026, 8, 26, 2, 0, 0, DateTimeKind.Utc);
    private static readonly Guid AttachmentId = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000001");

    private static WebhookInbox Row(
        string eventName, object data, DateTime? occurredAt = null,
        string eventId = "EVT-SCAN-1", string hostRefId = "DK-2026-001-0001")
    {
        var evt = new
        {
            EventID = eventId,
            Event = eventName,
            OccurredAt = occurredAt ?? T0,
            HostRefType = ModuleCodes.HostRefType,
            HostRefID = hostRefId,
            SubjectID = "",
            Data = data
        };

        return new WebhookInbox
        {
            EventID = eventId,
            EventType = eventName,
            DivisionID = FakeHealthExamContext.DefaultDivisionId,
            OccurredAt = occurredAt ?? T0,
            ReceivedAt = DateTime.UtcNow,
            Payload = JsonConvert.SerializeObject(evt)
        };
    }

    private static object ScanData(object serviceRef, Guid? attachmentId = null, bool? isAbnormal = null)
    {
        var data = JObject.FromObject(new
        {
            AttachKind = AttachKinds.ScanResult,
            AttachmentID = attachmentId ?? AttachmentId,
            ActorKind = 1,
            ActorID = 10234
        });

        if (serviceRef != null)
            foreach (var prop in JObject.FromObject(serviceRef).Properties())
                data[prop.Name] = prop.Value;

        if (isAbnormal.HasValue) data["Metadata"] = JObject.FromObject(new { isAbnormal = isAbnormal.Value });
        return data;
    }

    private static (InMemoryTestDb Db, ExamRecord Record, ParaclinicalOrder Order) Fixture(
        params (long ServiceID, string Code, ParaclinicalItemState State)[] items)
    {
        var db = new InMemoryTestDb();
        var session = db.SeedSession();
        var record = db.SeedRecord(session.SessionID, ExamRecordState.InProgress);
        var order = db.SeedOrder(record, items: items.Length > 0
            ? items
            : new[] { (100001L, "XN_CTM", ParaclinicalItemState.Waiting) });
        return (db, record, order);
    }

    // ───────────────────────────── Gắn KQ scan ⇒ đóng (B) ─────────────────────────────────

    /// <summary>
    /// Gate H3-09 nửa đầu: đính kèm KQ scan ⇒ dịch vụ về "Đã trả KQ" và (B) đủ — đo trong
    /// CÙNG một lần chạy với nửa sau (test kế tiếp).
    /// </summary>
    [Fact]
    public async Task Dinh_kem_KQ_scan_dua_dich_vu_len_da_tra_KQ()
    {
        var (db, record, _) = Fixture();
        using var _db = db;

        var result = await db.Processor.ProcessAsync(
            Row(FormEventNames.AttachmentAdded, ScanData(new { ServiceID = 100001L }, isAbnormal: true)));
        await db.Uow.SaveChangesAsync();

        Assert.Equal(WebhookOutcome.Applied, result.Outcome);

        var item = db.ItemsOf(record.RecordID).Single();
        Assert.Equal(ParaclinicalItemState.Done, item.State);
        Assert.Equal(ParaclinicalResultSources.Scan, item.ResultSourceKind);
        Assert.Equal(AttachmentId, item.AttachmentID);
        Assert.NotNull(item.ResultAt);          // server đóng dấu, không nguồn nào cấp
        Assert.True(item.IsAbnormal);

        var conclusion = db.Conclusion();
        Assert.True((await conclusion.EvaluateConditionBAsync(record.RecordID)).Satisfied);
    }

    /// <summary>
    /// ★ BẪY FIXTURE — test này tồn tại để GÀI MÌN, không phải để đo thêm một ca nghiệp vụ.
    ///
    /// Mọi fixture khác trong lớp này seed dòng ở <c>Waiting(1)</c>. Nhưng dòng dịch vụ MỚI
    /// SINH RA nằm ở <c>Ordered(0)</c> (ParaclinicalOrderService.cs:215, dùng chung cho chỉ
    /// định tay lẫn bung gói), và trong P3a KHÔNG có đường tự động nào đưa 0 → 1 (dispatch
    /// thuộc P3b; phiếu sinh ra TargetSystem=None, SentStatus=NotSent). Nên <c>0</c> là trạng
    /// thái DUY NHẤT một dòng có thể đang mang khi gói scan tới.
    ///
    /// ⇒ Ai siết máy trạng thái thành "chỉ tới 3 từ 1 hoặc 2" sẽ đóng nốt đường TỰ ĐỘNG duy
    /// nhất còn lại để điều kiện (B) khép — không ai ký được kết luận, ở bất kỳ cơ sở nào.
    /// Mà vì không fixture nào seed ở 0, cả bộ test VẪN XANH và hồi quy chỉ lộ ở cơ sở thật.
    /// Test này là thứ đỏ lên thay cho họ. Chốt Q-235-B3-01, review 236 §3.
    /// </summary>
    [Fact]
    public async Task Goi_scan_dong_duoc_dong_dang_o_Cho_chi_dinh_0()
    {
        var (db, record, _) = Fixture((100001L, "XN_CTM", ParaclinicalItemState.Ordered));
        using var _db = db;

        var result = await db.Processor.ProcessAsync(
            Row(FormEventNames.AttachmentAdded, ScanData(new { ServiceID = 100001L })));
        await db.Uow.SaveChangesAsync();

        Assert.Equal(WebhookOutcome.Applied, result.Outcome);

        var item = db.ItemsOf(record.RecordID).Single();
        Assert.Equal(ParaclinicalItemState.Done, item.State);   // 0 → 3, KHÔNG qua 1 hay 2
        Assert.Equal(ParaclinicalResultSources.Scan, item.ResultSourceKind);
        Assert.Equal(AttachmentId, item.AttachmentID);
    }

    /// <summary>
    /// ★ Gate H3-09 nửa sau, ĐO CÙNG MỘT LẦN CHẠY: huỷ đính kèm ⇒ dịch vụ LÙI về "Chờ thực
    /// hiện", (B) hụt lại, và mọi dấu vết kết quả bị dọn sạch.
    ///
    /// Đây là lý do (B) phải là truy vấn đếm chứ không phải cột cờ: một cờ quên hạ ở đây
    /// nghĩa là ký được kết luận khi kết quả đã bị gỡ.
    /// </summary>
    [Fact]
    public async Task Huy_dinh_kem_tra_dich_vu_ve_cho_thuc_hien_va_mo_lai_dieu_kien_B()
    {
        var (db, record, _) = Fixture();
        using var _db = db;

        var conclusion = db.Conclusion();

        await db.Processor.ProcessAsync(
            Row(FormEventNames.AttachmentAdded, ScanData(new { ServiceID = 100001L })));
        await db.Uow.SaveChangesAsync();
        Assert.True((await conclusion.EvaluateConditionBAsync(record.RecordID)).Satisfied);

        // [Huỷ KQ scan] — form-server soft-delete đính kèm rồi phát sự kiện.
        var removed = await db.Processor.ProcessAsync(
            Row(FormEventNames.AttachmentRemoved, ScanData(null), eventId: "EVT-SCAN-2",
                occurredAt: T0.AddMinutes(5)));
        await db.Uow.SaveChangesAsync();

        Assert.Equal(WebhookOutcome.Applied, removed.Outcome);
        Assert.Contains("AttachmentID", removed.Detail);   // khớp bằng con trỏ, không cần hợp đồng mới

        var item = db.ItemsOf(record.RecordID).Single();
        Assert.Equal(ParaclinicalItemState.Waiting, item.State);
        Assert.Null(item.ResultAt);
        Assert.Null(item.AttachmentID);
        Assert.Null(item.IsAbnormal);
        Assert.Equal("", item.ResultSourceKind);

        Assert.False((await conclusion.EvaluateConditionBAsync(record.RecordID)).Satisfied);
    }

    // ───────────────────────────── Khớp dịch vụ ───────────────────────────────────────────

    [Fact]
    public async Task Khop_theo_OrderItemID_khi_FE_neu_dich_danh()
    {
        var (db, record, _) = Fixture(
            (100001L, "XN_CTM", ParaclinicalItemState.Waiting),
            (100002L, "XN_NT", ParaclinicalItemState.Waiting));
        using var _db = db;

        var target = db.ItemsOf(record.RecordID).Single(x => x.ServiceCode == "XN_NT");

        var result = await db.Processor.ProcessAsync(
            Row(FormEventNames.AttachmentAdded, ScanData(new { OrderItemID = target.OrderItemID })));
        await db.Uow.SaveChangesAsync();

        Assert.Equal(WebhookOutcome.Applied, result.Outcome);
        var items = db.ItemsOf(record.RecordID);
        Assert.Equal(ParaclinicalItemState.Done, items.Single(x => x.ServiceCode == "XN_NT").State);
        Assert.Equal(ParaclinicalItemState.Waiting, items.Single(x => x.ServiceCode == "XN_CTM").State);
    }

    [Fact]
    public async Task Khop_theo_ServiceCode_trong_Metadata()
    {
        var (db, record, _) = Fixture(
            (100001L, "XN_CTM", ParaclinicalItemState.Waiting),
            (100002L, "XN_NT", ParaclinicalItemState.Waiting));
        using var _db = db;

        var data = JObject.FromObject(new
        {
            AttachKind = AttachKinds.ScanResult,
            AttachmentID = AttachmentId,
            Metadata = new { ServiceCode = "XN_CTM" }
        });

        var result = await db.Processor.ProcessAsync(Row(FormEventNames.AttachmentAdded, data));
        await db.Uow.SaveChangesAsync();

        Assert.Equal(WebhookOutcome.Applied, result.Outcome);
        Assert.Equal(ParaclinicalItemState.Done,
            db.ItemsOf(record.RecordID).Single(x => x.ServiceCode == "XN_CTM").State);
    }

    /// <summary>
    /// ★ Gói không chỉ ra được dịch vụ nào ⇒ BỎ QUA CÓ GHI CHÚ, tuyệt đối không đoán.
    ///
    /// Không có đường "hồ sơ chỉ còn một dịch vụ chưa xong thì chắc là nó": đoán đúng 9 lần
    /// và sai 1 lần ở đây nghĩa là một kết quả xét nghiệm gán nhầm dịch vụ.
    /// </summary>
    [Fact]
    public async Task Goi_khong_neu_dich_vu_nao_thi_bo_qua_chu_khong_doan()
    {
        var (db, record, _) = Fixture();
        using var _db = db;

        var result = await db.Processor.ProcessAsync(
            Row(FormEventNames.AttachmentAdded, ScanData(null)));
        await db.Uow.SaveChangesAsync();

        Assert.Equal(WebhookOutcome.Skipped, result.Outcome);
        Assert.Contains("OrderItemID", result.Detail);
        Assert.Equal(ParaclinicalItemState.Waiting, db.ItemsOf(record.RecordID).Single().State);
    }

    /// <summary>
    /// Gỡ một đính kèm mà chưa dòng nào giữ nó ⇒ Failed (thử lại), KHÔNG phải Skipped: gói
    /// `.added` có thể còn trong hàng đợi, và bỏ qua ở đây là để dịch vụ nằm lại ở "Đã trả
    /// KQ" với một kết quả đã bị xoá.
    /// </summary>
    [Fact]
    public async Task Go_dinh_kem_chua_ai_giu_thi_thu_lai_chu_khong_bo_qua()
    {
        var (db, record, _) = Fixture();
        using var _db = db;

        var result = await db.Processor.ProcessAsync(
            Row(FormEventNames.AttachmentRemoved, ScanData(null)));

        Assert.Equal(WebhookOutcome.Failed, result.Outcome);
        Assert.Contains(AttachmentId.ToString(), result.Detail);
    }

    // ───────────────────────────── Hai trục trạng thái ────────────────────────────────────

    /// <summary>
    /// ★ Sự kiện đính kèm KHÔNG bị lớp chống-đến-muộn của TRỤC HỒ SƠ chặn.
    ///
    /// Hai trục khác nhau: LastEventAt đo tiến độ hồ sơ, còn đính kèm đụng dòng dịch vụ. Áp
    /// lớp đó cho nó thì một gói SCAN_RESULT xảy ra trước lượt ký nhóm khám gần nhất sẽ bị
    /// bỏ, (B) không bao giờ đóng, và KHÔNG có lỗi nào hiện ra.
    /// </summary>
    [Fact]
    public async Task Su_kien_dinh_kem_cu_hon_moc_ho_so_van_duoc_ap()
    {
        var (db, record, _) = Fixture();
        using var _db = db;

        // Hồ sơ đã áp một sự kiện lúc 10:00.
        db.SetLastEventAt(record.RecordID, T0.AddHours(1));

        // Đính kèm XẢY RA lúc 09:30 — cũ hơn mốc của hồ sơ.
        var result = await db.Processor.ProcessAsync(
            Row(FormEventNames.AttachmentAdded, ScanData(new { ServiceID = 100001L }),
                occurredAt: T0.AddMinutes(30)));
        await db.Uow.SaveChangesAsync();

        Assert.Equal(WebhookOutcome.Applied, result.Outcome);
        Assert.Equal(ParaclinicalItemState.Done, db.ItemsOf(record.RecordID).Single().State);

        // …và nó KHÔNG đẩy mốc của trục hồ sơ về quá khứ.
        Assert.Equal(T0.AddHours(1), db.RecordOf(record.RecordID).LastEventAt);
    }

    /// <summary>Gói trùng (bên phát retry) ⇒ không đổi gì thêm, và không bị đếm là đã áp.</summary>
    [Fact]
    public async Task Goi_trung_khong_ap_lan_hai()
    {
        var (db, record, _) = Fixture();
        using var _db = db;

        await db.Processor.ProcessAsync(
            Row(FormEventNames.AttachmentAdded, ScanData(new { ServiceID = 100001L })));
        await db.Uow.SaveChangesAsync();
        var firstResultAt = db.ItemsOf(record.RecordID).Single().ResultAt;

        var again = await db.Processor.ProcessAsync(
            Row(FormEventNames.AttachmentAdded, ScanData(new { ServiceID = 100001L }), eventId: "EVT-SCAN-RETRY"));
        await db.Uow.SaveChangesAsync();

        Assert.Equal(WebhookOutcome.Skipped, again.Outcome);
        Assert.Equal(firstResultAt, db.ItemsOf(record.RecordID).Single().ResultAt);
    }

    // ──────────────────── Khớp mập mờ và gói gỡ lạc (review MR !10 B-1/B-2) ───────────────

    /// <summary>
    /// ★ B-1 — hồ sơ có HAI dòng còn sống cùng ServiceID (một do bung gói, một do chỉ định
    /// tay) thì gói scan chỉ nêu ServiceID KHÔNG chỉ ra được dòng nào ⇒ TỪ CHỐI cả lượt.
    ///
    /// Áp cho cả hai là đóng điều kiện (B) bằng MỘT kết quả cho HAI dịch vụ — nút Ký kết luận
    /// mở ra trong khi một dịch vụ chưa có kết quả nào, không lỗi, không cảnh báo.
    /// Chỉ mục unique là (OrderID, ServiceID) tức theo PHIẾU, nên trạng thái này hợp lệ về DB.
    /// </summary>
    [Fact]
    public async Task Hai_dong_cung_ServiceID_thi_tu_choi_chu_khong_ap_cho_ca_hai()
    {
        var (db, record, _) = Fixture((100001L, "XN_CTM", ParaclinicalItemState.Waiting));
        using var _db = db;

        // Dòng thứ hai cùng dịch vụ, ở phiếu khác — đúng thứ CreateManualAsync không chặn.
        db.SeedOrder(record, orderNo: "CD0000000002",
            items: new[] { (100001L, "XN_CTM", ParaclinicalItemState.Waiting) });

        var result = await db.Processor.ProcessAsync(
            Row(FormEventNames.AttachmentAdded, ScanData(new { ServiceID = 100001L })));
        await db.Uow.SaveChangesAsync();

        Assert.Equal(WebhookOutcome.Skipped, result.Outcome);
        Assert.Contains("2 dòng", result.Detail);

        var items = db.ItemsOf(record.RecordID);
        Assert.Equal(2, items.Count);
        Assert.All(items, x => Assert.Equal(ParaclinicalItemState.Waiting, x.State));
        Assert.All(items, x => Assert.Null(x.AttachmentID));

        var conclusion = db.Conclusion();
        Assert.False((await conclusion.EvaluateConditionBAsync(record.RecordID)).Satisfied);
    }

    /// <summary>
    /// B-1 có HAI nhánh khớp (ServiceID :403-405 và ServiceCode :411-413) và gói scan có thể
    /// tới bằng riêng nhánh sau — Metadata chỉ mang ServiceCode, không mang ServiceID. Nhánh
    /// nào không có test là nhánh lệch đi mà không ai thấy.
    /// </summary>
    [Fact]
    public async Task Hai_dong_cung_ServiceCode_cung_bi_tu_choi()
    {
        var (db, record, _) = Fixture((100001L, "XN_CTM", ParaclinicalItemState.Waiting));
        using var _db = db;

        // Dòng thứ hai cùng MÃ dịch vụ ở phiếu khác. ServiceID khác nhau, nên chỉ nhánh
        // ServiceCode mới chạm tới nó.
        db.SeedOrder(record, orderNo: "CD0000000002",
            items: new[] { (100009L, "XN_CTM", ParaclinicalItemState.Waiting) });

        var data = JObject.FromObject(new
        {
            AttachKind = AttachKinds.ScanResult,
            AttachmentID = AttachmentId,
            Metadata = new { ServiceCode = "XN_CTM" }
        });

        var result = await db.Processor.ProcessAsync(Row(FormEventNames.AttachmentAdded, data));
        await db.Uow.SaveChangesAsync();

        Assert.Equal(WebhookOutcome.Skipped, result.Outcome);
        Assert.Contains("ServiceCode", result.Detail);
        Assert.Contains("2 dòng", result.Detail);
        Assert.All(db.ItemsOf(record.RecordID), x => Assert.Equal(ParaclinicalItemState.Waiting, x.State));
    }

    /// <summary>
    /// Gói GỠ không mang AttachmentID ⇒ bỏ qua, và lời từ chối phải nêu đúng thứ đang thiếu.
    ///
    /// Trước bản vá B-2 gói này rơi xuống nhánh ServiceID; giờ thì không, nên lời cũ ("cần
    /// OrderItemID | ServiceID | ServiceCode") chỉ người trực đi sửa một khoá mà đường gỡ
    /// KHÔNG đọc — đúng loại hỏng mà B-2 nêu ở vế báo động nhầm.
    /// </summary>
    [Fact]
    public async Task Goi_go_thieu_AttachmentID_bao_dung_thu_dang_thieu()
    {
        var (db, record, _) = Fixture((100001L, "XN_CTM", ParaclinicalItemState.Done));
        using var _db = db;

        var data = JObject.FromObject(new { AttachKind = AttachKinds.ScanResult, ServiceID = 100001L });

        var result = await db.Processor.ProcessAsync(
            Row(FormEventNames.AttachmentRemoved, data, eventId: "EVT-SCAN-GO-THIEU-ID"));
        await db.Uow.SaveChangesAsync();

        Assert.Equal(WebhookOutcome.Skipped, result.Outcome);
        Assert.Contains("AttachmentID", result.Detail);
        Assert.DoesNotContain("ServiceCode", result.Detail);

        // …và dòng nhập tay không bị đụng tới.
        Assert.Equal(ParaclinicalItemState.Done, db.ItemsOf(record.RecordID).Single().State);
    }

    /// <summary>
    /// Chốt chặn B-1 KHÔNG được siết quá tay: dòng trùng mã đã HUỶ không phải là mập mờ —
    /// còn đúng một dòng sống thì vẫn áp bình thường.
    /// </summary>
    [Fact]
    public async Task Dong_trung_ma_da_huy_khong_lam_goi_scan_thanh_map_mo()
    {
        var (db, record, _) = Fixture((100001L, "XN_CTM", ParaclinicalItemState.Waiting));
        using var _db = db;

        db.SeedOrder(record, orderNo: "CD0000000002",
            items: new[] { (100001L, "XN_CTM", ParaclinicalItemState.Cancelled) });

        var result = await db.Processor.ProcessAsync(
            Row(FormEventNames.AttachmentAdded, ScanData(new { ServiceID = 100001L })));
        await db.Uow.SaveChangesAsync();

        Assert.Equal(WebhookOutcome.Applied, result.Outcome);
        var items = db.ItemsOf(record.RecordID);
        Assert.Equal(ParaclinicalItemState.Done,
            items.Single(x => x.State != ParaclinicalItemState.Cancelled).State);
    }

    /// <summary>
    /// ★ B-2 — gói GỠ chỉ đi bằng con trỏ <c>AttachmentID</c>. Rơi xuống nhánh ServiceID cho
    /// phép một đính kèm CHƯA TỪNG CÓ TÁC DỤNG GÌ xoá trắng kết quả người dùng NHẬP TAY.
    ///
    /// Đường tới: dòng được nhập KQ tay (MANUAL, AttachmentID=null). Đính kèm scan cho cùng
    /// dịch vụ gắn vào — dòng đã ở "Đã trả KQ" nên máy trạng thái trả NoOp và AttachmentID
    /// KHÔNG bao giờ được ghi. Đính kèm đó bị gỡ ⇒ không dòng nào giữ nó.
    /// </summary>
    [Fact]
    public async Task Goi_go_khong_duoc_roi_xuong_ServiceID_de_xoa_ket_qua_nhap_tay()
    {
        var (db, record, _) = Fixture((100001L, "XN_CTM", ParaclinicalItemState.Done));
        using var _db = db;

        var before = db.ItemsOf(record.RecordID).Single();
        Assert.Equal(ParaclinicalResultSources.Manual, before.ResultSourceKind);
        Assert.Null(before.AttachmentID);

        var stray = Guid.Parse("bbbbbbbb-0000-0000-0000-0000000000ff");
        var result = await db.Processor.ProcessAsync(
            Row(FormEventNames.AttachmentRemoved,
                ScanData(new { ServiceID = 100001L }, attachmentId: stray),
                eventId: "EVT-SCAN-GO-LAC"));
        await db.Uow.SaveChangesAsync();

        Assert.NotEqual(WebhookOutcome.Applied, result.Outcome);

        var after = db.ItemsOf(record.RecordID).Single();
        Assert.Equal(ParaclinicalItemState.Done, after.State);
        Assert.NotNull(after.ResultAt);
        Assert.Equal(ParaclinicalResultSources.Manual, after.ResultSourceKind);
    }

    /// <summary>
    /// ★ B-2 phần hai — gói gỡ lạc chỉ được đi vào dead-letter khi gói <c>.added</c> tương ứng
    /// CÓ THỂ còn đang chờ. Đã có bằng chứng nó bị tiêu thụ rồi thì đây là ca LÀNH TÍNH: bỏ
    /// qua có ghi chú, không dựng báo động cho người trực.
    ///
    /// Bằng chứng lấy từ chính hàng inbox — hàng <c>.added</c> đã xử lý mang RecordID (do
    /// processor ghi) và payload mang đúng AttachmentID này.
    /// </summary>
    [Fact]
    public async Task Goi_go_lac_khong_thanh_dead_letter_khi_goi_added_da_duoc_tieu_thu()
    {
        var (db, record, _) = Fixture((100001L, "XN_CTM", ParaclinicalItemState.Done));
        using var _db = db;

        var stray = Guid.Parse("bbbbbbbb-0000-0000-0000-0000000000ff");

        // Hàng `.added` ĐÃ xử lý: nó NoOp vì dòng đã ở "Đã trả KQ" do nhập tay.
        var consumed = Row(FormEventNames.AttachmentAdded,
            ScanData(new { ServiceID = 100001L }, attachmentId: stray), eventId: "EVT-SCAN-ADD-NOOP");
        consumed.RecordID = record.RecordID;
        consumed.ProcessState = WebhookProcessState.Skipped;
        db.Db.WebhookInboxes.Add(consumed);
        await db.Db.SaveChangesAsync();
        db.Db.ChangeTracker.Clear();

        var result = await db.Processor.ProcessAsync(
            Row(FormEventNames.AttachmentRemoved,
                ScanData(new { ServiceID = 100001L }, attachmentId: stray),
                eventId: "EVT-SCAN-GO-LAC-2"));
        await db.Uow.SaveChangesAsync();

        Assert.Equal(WebhookOutcome.Skipped, result.Outcome);
        Assert.Equal(ParaclinicalItemState.Done, db.ItemsOf(record.RecordID).Single().State);
    }

    /// <summary>Đính kèm KHÁC SCAN_RESULT (VD chữ ký người bệnh) không đụng gì tới chỉ định.</summary>
    [Fact]
    public async Task Dinh_kem_khong_phai_KQ_scan_khong_dung_toi_chi_dinh()
    {
        var (db, record, _) = Fixture();
        using var _db = db;

        var data = JObject.FromObject(new { AttachKind = "SIGNATURE_IMAGE", AttachmentID = AttachmentId });
        var result = await db.Processor.ProcessAsync(Row(FormEventNames.AttachmentAdded, data));

        Assert.Equal(WebhookOutcome.Skipped, result.Outcome);
        Assert.Equal(ParaclinicalItemState.Waiting, db.ItemsOf(record.RecordID).Single().State);
    }
}

/// <summary>
/// §3.6 của chốt 236 — sự kiện đính kèm bị bỏ qua ở giai đoạn P2 KHÔNG mất, và P3a nạp lại
/// được từ chính hàng inbox, không cần xin form-server gửi lại.
/// </summary>
public class ScanResultBackfillTests
{
    private static WebhookInbox Skipped(string eventType, string eventId)
        => new()
        {
            EventID = eventId,
            EventType = eventType,
            DivisionID = FakeHealthExamContext.DefaultDivisionId,
            OccurredAt = new DateTime(2026, 8, 20, 2, 0, 0, DateTimeKind.Utc),
            ReceivedAt = new DateTime(2026, 8, 20, 2, 0, 5, DateTimeKind.Utc),
            ProcessState = WebhookProcessState.Skipped,
            Payload = "{}",
            LastError = "Chỉ định CLS (P3) chưa hiện thực"
        };

    [Fact]
    public async Task Nap_lai_dung_hai_su_kien_dinh_kem_bi_bo_qua()
    {
        using var db = new InMemoryTestDb();

        db.Db.WebhookInboxes.AddRange(
            Skipped(FormEventNames.AttachmentAdded, "EVT-P2-1"),
            Skipped(FormEventNames.AttachmentRemoved, "EVT-P2-2"),
            // Nhiễu: sự kiện Skipped KHÔNG phải đính kèm (tên lạ) — không được nạp lại.
            Skipped("submission.section.saved", "EVT-P2-3"));
        await db.Db.SaveChangesAsync();
        db.Db.ChangeTracker.Clear();

        var result = await db.Webhooks.BackfillScanResultsAsync();

        Assert.Equal(2, result.Requeued);
        Assert.Contains("EVT-P2-1", result.EventIDs);
        Assert.Contains("EVT-P2-2", result.EventIDs);

        var rows = db.InboxRows();
        Assert.Equal(WebhookProcessState.New, rows.Single(x => x.EventID == "EVT-P2-1").ProcessState);
        Assert.Equal(WebhookProcessState.Skipped, rows.Single(x => x.EventID == "EVT-P2-3").ProcessState);
    }

    /// <summary>Tenant khác không bị đụng tới: hộp thư chung một bảng, nhiều đơn vị.</summary>
    [Fact]
    public async Task Khong_nap_lai_su_kien_cua_don_vi_khac()
    {
        using var db = new InMemoryTestDb();

        var other = Skipped(FormEventNames.AttachmentAdded, "EVT-KHAC");
        other.DivisionID = "DON-VI-KHAC";
        db.Db.WebhookInboxes.Add(other);
        await db.Db.SaveChangesAsync();
        db.Db.ChangeTracker.Clear();

        var result = await db.Webhooks.BackfillScanResultsAsync();

        Assert.Equal(0, result.Requeued);
        Assert.Equal(WebhookProcessState.Skipped, db.InboxRows().Single().ProcessState);
    }

    // ──────────── F-2 · Cửa sổ Take(max) cắt mất phần nào thì phải NÓI RA ─────────────────

    /// <summary>
    /// Cửa sổ mặc định 100 hàng, mà một tenant tồn đọng cả nghìn sự kiện P2 là bình thường.
    /// Không trả Remaining thì người vận hành tưởng đã chạy xong — và ở đường này, dừng giữa
    /// chừng cắt đúng vào giữa cặp .added/.removed: phần lọt vào LUÔN là .added (nó cũ hơn),
    /// nên dòng lên "Đã trả KQ" giữ AttachmentID của một đính kèm đã bị xoá.
    /// </summary>
    [Fact]
    public async Task Remaining_noi_ra_phan_cua_so_Take_cat_mat()
    {
        using var db = new InMemoryTestDb();

        for (var i = 1; i <= 5; i++)
            db.Db.WebhookInboxes.Add(Skipped(FormEventNames.AttachmentAdded, $"EVT-P2-{i}"));
        await db.Db.SaveChangesAsync();
        db.Db.ChangeTracker.Clear();

        var result = await db.Webhooks.BackfillScanResultsAsync(max: 2);

        Assert.Equal(2, result.Requeued);
        Assert.Equal(3, result.Remaining);        // ← thiếu con số này là im lặng bỏ dở
        Assert.NotNull(result.Cursor);
    }

    /// <summary>
    /// ★ TEST QUAN TRỌNG NHẤT của F-2 — nó chứng minh lời khuyên "gọi lặp cho tới khi
    /// Remaining = 0" THẬT SỰ DỪNG.
    ///
    /// Sự kiện nào không chỉ ra được dòng dịch vụ thì worker đóng dấu Skipped LẦN NỮA
    /// (WebhookWorker.Stamp :287-292). Bộ lọc chỉ nhìn ProcessState, và KHÔNG cột nào phân
    /// biệt "chưa nạp bao giờ" với "đã nạp rồi bị bỏ qua lại" — LastError bị Stamp ghi đè,
    /// RetryCount thì chính backfill đặt về 0. Nên nếu không khoá vũng bằng mốc thời gian,
    /// hàng đó rơi lại vào lượt sau, VĨNH VIỄN.
    ///
    /// Không phải ca hiếm: hôm nay form-server chưa đẩy Metadata (F-1) nên MỌI sự kiện scan
    /// rơi vào đúng ca này ⇒ vòng lặp sẽ chạy mãi trên mọi tenant.
    /// </summary>
    [Fact]
    public async Task Hang_bi_bo_qua_LAI_khong_roi_nguoc_vao_vung_dang_duyet()
    {
        using var db = new InMemoryTestDb();

        foreach (var id in new[] { "EVT-P2-1", "EVT-P2-2" })
        {
            var row = Skipped(FormEventNames.AttachmentAdded, id);
            row.ProcessedAt = new DateTime(2026, 8, 20, 2, 0, 5, DateTimeKind.Utc);
            db.Db.WebhookInboxes.Add(row);
        }
        await db.Db.SaveChangesAsync();
        db.Db.ChangeTracker.Clear();

        // Lượt 1: lấy đúng một hàng, còn một hàng.
        var pass1 = await db.Webhooks.BackfillScanResultsAsync(max: 1);
        Assert.Equal(1, pass1.Requeued);
        Assert.Equal(1, pass1.Remaining);

        var doneFirst = pass1.EventIDs.Single();

        // Worker chạy, KHÔNG khớp được dòng dịch vụ ⇒ đóng dấu Skipped lần nữa, ProcessedAt
        // là giờ xử lý (sau mốc của lượt 1). Đây là đúng hành vi thật hôm nay dưới F-1.
        var replayed = db.Db.WebhookInboxes.AsTracking().Single(x => x.EventID == doneFirst);
        replayed.ProcessState = WebhookProcessState.Skipped;
        replayed.ProcessedAt = pass1.Cursor!.Value.AddSeconds(1);
        replayed.LastError = "Gói scan không chỉ ra được dòng dịch vụ";   // Stamp ghi đè dấu nạp lại
        await db.Db.SaveChangesAsync();
        db.Db.ChangeTracker.Clear();

        // Lượt 2 KÈM ĐÚNG mốc của lượt 1 ⇒ chỉ còn hàng chưa đụng tới, và vũng cạn.
        var pass2 = await db.Webhooks.BackfillScanResultsAsync(max: 1, before: pass1.Cursor);

        Assert.Equal(1, pass2.Requeued);
        Assert.DoesNotContain(doneFirst, pass2.EventIDs);   // ← hàng đã nạp KHÔNG quay lại
        Assert.Equal(0, pass2.Remaining);                   // ← vòng lặp DỪNG được
    }

    /// <summary>
    /// Nửa kia của cùng một chốt: BỎ mốc đi thì đúng hàng vừa bị bỏ qua lại rơi ngược vào vũng.
    /// Test này giữ cho lý do tồn tại của tham số <c>before</c> không bị ai đó dọn đi vì tưởng
    /// là thừa — nó ghim đúng cái vòng lặp vô hạn mà mốc đó sinh ra để chặn.
    /// </summary>
    [Fact]
    public async Task Bo_moc_di_thi_hang_do_roi_nguoc_lai_vung_va_vong_lap_khong_can()
    {
        using var db = new InMemoryTestDb();

        var row = Skipped(FormEventNames.AttachmentAdded, "EVT-P2-1");
        row.ProcessedAt = new DateTime(2026, 8, 20, 2, 0, 5, DateTimeKind.Utc);
        db.Db.WebhookInboxes.Add(row);
        await db.Db.SaveChangesAsync();
        db.Db.ChangeTracker.Clear();

        var pass1 = await db.Webhooks.BackfillScanResultsAsync();
        Assert.Equal(1, pass1.Requeued);

        var replayed = db.Db.WebhookInboxes.AsTracking().Single();
        replayed.ProcessState = WebhookProcessState.Skipped;
        replayed.ProcessedAt = pass1.Cursor!.Value.AddSeconds(1);
        await db.Db.SaveChangesAsync();
        db.Db.ChangeTracker.Clear();

        // Không truyền mốc = mở vũng MỚI tính tới "bây giờ" ⇒ ôm lại chính hàng vừa nạp.
        //
        // Dựng "bây giờ" bằng mốc tường minh chứ không gọi tay không: cả test chạy trong vài
        // mili-giây nên DateTime.UtcNow của lượt hai vẫn còn SỚM HƠN mốc bỏ qua vừa gán, và
        // test sẽ xanh vì lý do sai. Mốc dưới đây chính là thứ lượt gọi không tham số sinh ra
        // khi người vận hành chạy lại sau một phút — tức ca thật.
        var khongMoc = await db.Webhooks.BackfillScanResultsAsync(
            before: pass1.Cursor!.Value.AddMinutes(1));
        Assert.Equal(1, khongMoc.Requeued);   // ← hàng đã nạp QUAY LẠI: đây là vòng lặp vô hạn

        // Truyền lại mốc cũ ⇒ hàng đó nằm ngoài, không có gì để làm.
        db.Db.ChangeTracker.Clear();
        var replayed2 = db.Db.WebhookInboxes.AsTracking().Single();
        replayed2.ProcessState = WebhookProcessState.Skipped;
        replayed2.ProcessedAt = pass1.Cursor!.Value.AddSeconds(2);
        await db.Db.SaveChangesAsync();
        db.Db.ChangeTracker.Clear();

        var coMoc = await db.Webhooks.BackfillScanResultsAsync(before: pass1.Cursor);
        Assert.Equal(0, coMoc.Requeued);
        Assert.Equal(0, coMoc.Remaining);
    }

    /// <summary>
    /// ★ Mốc đi qua query string thì Kind KHÔNG cố định — và lệch Kind ở đây là lệch 7 tiếng.
    ///
    /// `ToUniversalTime()` trên <c>Kind=Unspecified</c> COI LÀ GIỜ ĐỊA PHƯƠNG và trừ offset,
    /// tức dưới TZ=Asia/Ho_Chi_Minh thì LÙI mốc 7 tiếng. Mốc lùi ⇒ vũng nhỏ đi ⇒ Remaining
    /// báo 0 trong khi còn hàng chưa xử lý: người vận hành nhận "đã chạy xong" KÈM CON SỐ —
    /// đúng cái sai F-2 sinh ra để chặn, chỉ khác là nay nghe đáng tin hơn.
    ///
    /// Ba Kind phải cùng chỉ về MỘT thời điểm. Test chạy dưới TZ=Asia/Ho_Chi_Minh (lệch +7)
    /// nên nếu ai gộp lại thành một nhánh `ToUniversalTime()` thì nhánh Unspecified đỏ.
    /// </summary>
    [Fact]
    public async Task Moc_chan_hieu_dung_ca_ba_Kind_khong_lech_mui_gio()
    {
        var moc = new DateTime(2026, 8, 20, 3, 0, 0, DateTimeKind.Utc);

        // Không có vế này thì dưới TZ=UTC cả vế 2 lẫn vế 3 xanh VÔ ĐIỀU KIỆN (ToUniversalTime
        // trên Unspecified và ToLocalTime đều thành no-op) ⇒ chốt hồi quy tự vô hiệu, im lặng.
        TzGuard.RequireLocalOffsetKhacUtc(moc);

        // Hàng bị bỏ qua lúc 02:00:05 UTC — NẰM TRONG vũng tính tới 03:00 UTC.
        async Task<WebhookRequeueResult> Chay(DateTime? before)
        {
            using var db = new InMemoryTestDb();
            var row = Skipped(FormEventNames.AttachmentAdded, "EVT-P2-1");
            row.ProcessedAt = new DateTime(2026, 8, 20, 2, 0, 5, DateTimeKind.Utc);
            db.Db.WebhookInboxes.Add(row);
            await db.Db.SaveChangesAsync();
            db.Db.ChangeTracker.Clear();
            return await db.Webhooks.BackfillScanResultsAsync(before: before);
        }

        // 1. Utc — dùng thẳng.
        Assert.Equal(1, (await Chay(moc)).Requeued);

        // 2. Unspecified (chuỗi query KHÔNG có 'Z') — phải hiểu là UTC, KHÔNG đổi múi.
        //    Gộp vào ToUniversalTime() ⇒ mốc thành 2026-08-19T20:00Z ⇒ hàng rơi ra ngoài ⇒ 0.
        Assert.Equal(1, (await Chay(DateTime.SpecifyKind(moc, DateTimeKind.Unspecified))).Requeued);

        // 3. Local — ĐỔI MÚI mới đúng, vì nó thật sự là giờ địa phương.
        Assert.Equal(1, (await Chay(moc.ToLocalTime())).Requeued);

        // Chốt ngược: mốc SỚM hơn thời điểm bỏ qua thì hàng phải nằm ngoài vũng. Thiếu vế này
        // thì ba khẳng định trên vẫn xanh kể cả khi bộ lọc bỏ quên mốc.
        Assert.Equal(0, (await Chay(new DateTime(2026, 8, 20, 1, 0, 0, DateTimeKind.Utc))).Requeued);
    }
}
