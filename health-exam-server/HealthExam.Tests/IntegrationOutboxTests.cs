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
using HealthExam.Infrastructure.Integrations.Ris;
using HealthExam.Infrastructure.BackgroundJobs;
using HealthExam.Application.Integrations;
using HealthExam.API.Contracts;
using HealthExam.API.Middlewares;
using HealthExam.Application.Common;
using HealthExam.Server.Service;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json.Linq;
using Xunit;

namespace HealthExam.Tests;

/// <summary>
/// P3b — hàng đợi gửi vendor và VÒNG ĐI-VỀ đầy đủ với stub cục bộ (gate G-P3b-1).
///
/// Lời gọi THẬT (G-P3b-2) không nằm ở đây và cố ý không có test nào: không tồn tại tenant
/// sandbox nào trong cấu hình, và bắn phiếu vào RIS đang chạy của một bệnh viện là ghi dữ
/// liệu thật vào hệ production của bên thứ ba (docs/handoff/20260826-236-chot-p3-cls.md §4).
/// </summary>
public class IntegrationOutboxTests
{
    private static RisOptions Configured(string divisionId = FakeHealthExamContext.DefaultDivisionId) => new()
    {
        BaseUrl = "https://ris.test/hisris/ris/00000",
        Username = "hex",
        Password = "s3cret",
        DivisionId = divisionId
    };

    private static (InMemoryTestDb Db, ExamRecord Record, ParaclinicalOrder Order) Fixture(
        RisOptions options = null, RisStubHandler stub = null, short genderId = 1,
        string identity = "079201004321", string patientCode = "BN000123",
        params (long ServiceID, string Code, ParaclinicalItemState State)[] items)
    {
        var db = new InMemoryTestDb();
        db.UseRis(options ?? Configured(), stub);

        var session = db.SeedSession();
        var record = db.SeedRecord(
            session.SessionID, ExamRecordState.InProgress,
            patientCode: patientCode, identityNumber: identity, genderId: genderId);

        var order = db.SeedOrder(record, kind: "CDHA",
            items: items.Length > 0
                ? items
                : new[] { (200001L, "CDHA_XQNT", ParaclinicalItemState.Ordered) });

        return (db, record, order);
    }

    // ───────────────────────────── Chưa cấu hình ──────────────────────────────────────────

    /// <summary>
    /// 🔴 Gate an toàn: chưa cấu hình RIS thì endpoint NÓI RA (5021), không âm thầm xếp hàng.
    ///
    /// Một hàng đợi phình lên vì không ai cấu hình đích đến là cách hỏng tệ nhất của lớp tích
    /// hợp — nhìn từ màn hình thì mọi thứ đang chạy tốt.
    /// </summary>
    [Fact]
    public async Task Chua_cau_hinh_RIS_thi_tu_choi_ngay_5021_va_khong_xep_hang()
    {
        var (db, _, order) = Fixture(options: new RisOptions());
        using var _db = db;

        var ex = await Assert.ThrowsAsync<HealthExamException>(() => db.Outbox.EnqueueOrderAsync(order.OrderID));

        Assert.Equal(ErrorCodes.VendorNotConfigured, ex.ErrorCode);
        Assert.Equal(503, ErrorCodes.ToHttpStatus(ex.ErrorCode));
        Assert.Empty(db.Db.IntegrationOutboxes.AsNoTracking().ToList());
    }

    // ───────────────────────────── Xếp hàng ───────────────────────────────────────────────

    [Fact]
    public async Task Xep_hang_dong_bang_goi_va_cap_so_hieu_dong()
    {
        var (db, _, order) = Fixture();
        using var _db = db;

        var result = await db.Outbox.EnqueueOrderAsync(order.OrderID);

        Assert.False(result.AlreadyQueued);
        Assert.Equal(ParaclinicalTargets.Ris, result.Vendor);
        Assert.Equal(RisOrderStatuses.New, result.Operation);
        Assert.Single(result.LineIDs);

        var row = db.Db.IntegrationOutboxes.AsNoTracking().Single();
        Assert.Equal(OutboxState.Pending, row.State);
        Assert.Equal(JObject.Parse(row.Payload)["orderNumber"]!.Value<string>(), order.OrderNo);

        // Phiếu thuộc về RIS ngay, nhưng CHƯA "đã gửi": gói còn nằm trong hàng đợi.
        var saved = db.Db.ParaclinicalOrders.AsNoTracking().Single(x => x.OrderID == order.OrderID);
        Assert.Equal(ParaclinicalTargets.Ris, saved.TargetSystem);
        Assert.Equal(OrderSentStatus.NotSent, saved.SentStatus);

        // Chưa gửi thì stub chưa nhận gì — xếp hàng KHÔNG được gọi vendor trong request.
        Assert.Empty(db.RisStub.Received);
    }

    /// <summary>Bấm Gửi ba lần là thao tác thường gặp nhất của người đang sốt ruột — phải ra
    /// ĐÚNG MỘT gói, và không phải một mã lỗi.</summary>
    [Fact]
    public async Task Bam_gui_nhieu_lan_chi_ra_dung_mot_goi()
    {
        var (db, _, order) = Fixture();
        using var _db = db;

        var first = await db.Outbox.EnqueueOrderAsync(order.OrderID);
        var second = await db.Outbox.EnqueueOrderAsync(order.OrderID);
        var third = await db.Outbox.EnqueueOrderAsync(order.OrderID);

        Assert.False(first.AlreadyQueued);
        Assert.True(second.AlreadyQueued);
        Assert.True(third.AlreadyQueued);
        Assert.Equal(first.OutboxID, second.OutboxID);
        Assert.Single(db.Db.IntegrationOutboxes.AsNoTracking().ToList());
    }

    /// <summary>Hồ sơ thiếu trường bắt buộc của vendor ⇒ 4095 kèm danh sách, và KHÔNG có gói
    /// nào nằm lại trong hàng đợi để lượt sau gửi đi một phiếu hỏng.</summary>
    [Fact]
    public async Task Ho_so_thieu_CCCD_thi_4095_va_hang_doi_van_rong()
    {
        var (db, _, order) = Fixture(identity: "");
        using var _db = db;

        var ex = await Assert.ThrowsAsync<HealthExamException>(() => db.Outbox.EnqueueOrderAsync(order.OrderID));

        Assert.Equal(ErrorCodes.VendorPayloadIncomplete, ex.ErrorCode);
        Assert.Equal(422, ErrorCodes.ToHttpStatus(ex.ErrorCode));
        Assert.Empty(db.Db.IntegrationOutboxes.AsNoTracking().ToList());
    }

    [Fact]
    public async Task Phieu_khong_con_dong_song_thi_khong_gui_duoc()
    {
        var (db, _, order) = Fixture(
            items: new[] { (200001L, "CDHA_XQNT", ParaclinicalItemState.Cancelled) });
        using var _db = db;

        var ex = await Assert.ThrowsAsync<HealthExamException>(() => db.Outbox.EnqueueOrderAsync(order.OrderID));
        Assert.Equal(ErrorCodes.InvalidState, ex.ErrorCode);
    }

    // ───────────────────────────── Đẩy đi ─────────────────────────────────────────────────

    /// <summary>
    /// ★ VÒNG ĐI đầy đủ: gói tới đúng đường dẫn hợp đồng, mang Basic, và lượt gửi thành công
    /// đưa dòng dịch vụ từ "Chờ chỉ định" sang "Chờ thực hiện" — nấc mà P3a cố ý để trống.
    /// </summary>
    [Fact]
    public async Task Gui_thanh_cong_thi_phieu_Sent_va_dong_sang_Cho_thuc_hien()
    {
        var (db, record, order) = Fixture();
        using var _db = db;

        await db.Outbox.EnqueueOrderAsync(order.OrderID);
        var row = db.Db.IntegrationOutboxes.AsTracking().Single();

        var sent = await db.SendOnceAsync(row);
        IntegrationOutboxWorker.Stamp(row, sent.Success, sent.Detail, sent.ResponseSnippet);
        await db.Uow.SaveChangesAsync();

        Assert.True(sent.Success);
        Assert.Equal(OutboxState.Sent, row.State);
        Assert.NotNull(row.SentAt);

        // EndsWith chứ không Equal: gốc URL của vendor mang cả mã cơ sở
        // (…/hisris/ris/84006), đường dẫn hợp đồng nối THÊM vào sau chứ không thay thế.
        Assert.EndsWith(RisOptions.OrderPath, db.RisStub.LastPath);
        Assert.StartsWith("Basic ", db.RisStub.LastAuthorization);
        Assert.Single(db.RisStub.Received);

        var saved = db.Db.ParaclinicalOrders.AsNoTracking().Single(x => x.OrderID == order.OrderID);
        Assert.Equal(OrderSentStatus.Sent, saved.SentStatus);
        Assert.NotNull(saved.SentAt);

        var item = db.ItemsOf(record.RecordID).Single();
        Assert.Equal(ParaclinicalItemState.Waiting, item.State);
    }

    /// <summary>Vendor trả 500 ⇒ KHÔNG đóng dấu Sent, còn lượt thì hẹn giờ thử lại. Đánh dấu
    /// Sent cho một gói bên kia không nhận là để phiếu trông như đã tới nơi.</summary>
    [Fact]
    public async Task Vendor_tra_loi_thi_hen_gio_thu_lai_chu_khong_dong_dau_Sent()
    {
        var stub = new RisStubHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent("boom", Encoding.UTF8, "text/plain")
        });
        var (db, record, order) = Fixture(stub: stub);
        using var _db = db;

        await db.Outbox.EnqueueOrderAsync(order.OrderID);
        var row = db.Db.IntegrationOutboxes.AsTracking().Single();

        // Ghi sổ lượt thử TRƯỚC khi gọi — đúng thứ tự worker làm, xem IntegrationOutboxWorker.
        Assert.True(IntegrationOutboxWorker.Claim(row, DateTime.UtcNow));

        var sent = await db.SendOnceAsync(row);
        IntegrationOutboxWorker.Stamp(row, sent.Success, sent.Detail, sent.ResponseSnippet);
        await db.Uow.SaveChangesAsync();

        Assert.False(sent.Success);
        Assert.Equal(OutboxState.Failed, row.State);
        Assert.Equal(1, row.RetryCount);
        Assert.NotNull(row.NextAttemptAt);
        Assert.Contains("500", row.LastError);
        // Thân phản hồi được GIỮ LẠI: vendor trả 200 kèm thân báo lỗi là chuyện có thật ở
        // lớp này, nên "đã gửi" mà không giữ họ nói gì là mất đúng bằng chứng cần lúc đối soát.
        Assert.Contains("boom", row.ResponseSnippet);

        var saved = db.Db.ParaclinicalOrders.AsNoTracking().Single(x => x.OrderID == order.OrderID);
        Assert.Equal(OrderSentStatus.NotSent, saved.SentStatus);
        Assert.Equal(ParaclinicalItemState.Ordered, db.ItemsOf(record.RecordID).Single().State);
    }

    /// <summary>
    /// 🔴 Hết lượt thử ⇒ DeadLetter, trạng thái RIÊNG chứ không phải "Failed lần thứ 5".
    ///
    /// Chiều ra khác chiều vào: một gói ra bị mất nghĩa là PHÒNG CHỤP KHÔNG BAO GIỜ THẤY chỉ
    /// định, mà màn hình bên ta vẫn hiện phiếu bình thường. Câu truy vấn cảnh báo phải là một
    /// vế đơn giản, không phải phép "State=2 AND RetryCount>=5" mà ai cũng có thể viết thiếu.
    /// </summary>
    [Fact]
    public void Het_luot_thu_thi_roi_han_vao_DeadLetter()
    {
        var row = new IntegrationOutbox { Operation = RisOrderStatuses.New, Vendor = ParaclinicalTargets.Ris };

        // Đúng ngân sách MaxRetry lượt: mỗi lượt GHI SỔ trước (Claim) rồi mới đóng dấu kết quả.
        for (var i = 1; i <= IntegrationOutboxService.MaxRetry; i++)
        {
            Assert.True(IntegrationOutboxWorker.Claim(row, DateTime.UtcNow), $"lượt {i} phải được cấp");
            Assert.Equal((short)i, row.RetryCount);
            IntegrationOutboxWorker.Stamp(row, success: false, $"lỗi lần {i}", snippet: "");
        }

        // Lượt thứ MaxRetry+1 KHÔNG được cấp — và chính chỗ từ chối đó đóng dấu DeadLetter.
        Assert.False(IntegrationOutboxWorker.Claim(row, DateTime.UtcNow));

        Assert.Equal(OutboxState.DeadLetter, row.State);
        Assert.Equal(IntegrationOutboxService.MaxRetry, row.RetryCount);
        // Không hẹn giờ nữa: hẹn tiếp là để hàng đợi tự nhặt lại vĩnh viễn một gói đã bỏ.
        Assert.Null(row.NextAttemptAt);
    }

    /// <summary>
    /// 🔴 NGÂN SÁCH THỬ TIÊU LÚC BẮT ĐẦU LƯỢT, KHÔNG PHẢI LÚC BIẾT KẾT QUẢ.
    ///
    /// Đây là chốt chặn cho lớp lỗi "pod chết giữa chừng": nếu tiến trình bị OOMKilled trong
    /// khe giữa "RIS đã trả 200" và "commit", thì transaction cuốn ngược và KHÔNG nhánh catch
    /// nào chạy được — không có ai còn sống để chạy nó. Nếu số lượt thử được ghi lúc đóng dấu
    /// kết quả thì hàng quay về y như chưa từng gửi, và cùng một phiếu bị bắn sang RIS KHÔNG
    /// GIỚI HẠN LẦN.
    ///
    /// Cách đo: Claim rồi KHÔNG Stamp — mô phỏng đúng cái chết giữa chừng. Số lượt vẫn phải
    /// tiêu, và sau MaxRetry lần chết như thế thì hàng phải vào DeadLetter chứ không quay vòng.
    /// </summary>
    [Fact]
    public void Pod_chet_giua_chung_van_tieu_luot_thu_chu_khong_lap_vo_han()
    {
        var row = new IntegrationOutbox { Operation = RisOrderStatuses.New, Vendor = ParaclinicalTargets.Ris };

        for (var i = 1; i <= IntegrationOutboxService.MaxRetry; i++)
        {
            Assert.True(IntegrationOutboxWorker.Claim(row, DateTime.UtcNow));
            // …rồi tiến trình chết ở đây. Không Stamp, không SaveChanges nào của lượt này.
        }

        Assert.False(IntegrationOutboxWorker.Claim(row, DateTime.UtcNow));
        Assert.Equal(OutboxState.DeadLetter, row.State);
    }

    /// <summary>
    /// Đối xứng với ca trên: Stamp KHÔNG được tự tăng số lượt.
    ///
    /// Tăng ở cả hai chỗ là tiêu ngân sách HAI LẦN cho một lượt — MaxRetry=5 hoá ra chỉ còn
    /// hai lần thử thật, và một sự cố RIS 4 phút biến thành một phiếu chết vô cớ.
    /// </summary>
    [Fact]
    public void Dong_dau_ket_qua_KHONG_tieu_them_luot_thu()
    {
        var row = new IntegrationOutbox { Operation = RisOrderStatuses.New, Vendor = ParaclinicalTargets.Ris };

        IntegrationOutboxWorker.Claim(row, DateTime.UtcNow);
        Assert.Equal(1, row.RetryCount);

        IntegrationOutboxWorker.Stamp(row, success: false, "vendor trả 500", snippet: "");
        Assert.Equal(1, row.RetryCount);

        IntegrationOutboxWorker.Stamp(row, success: true, "ok", snippet: "");
        Assert.Equal(1, row.RetryCount);
        Assert.Equal(OutboxState.Sent, row.State);
    }

    /// <summary>Giãn cách nhân đôi, không cố định — thử lại dày đặc thì cả ngân sách 5 lượt
    /// cháy hết trước khi một sự cố thoáng qua kịp qua.</summary>
    [Fact]
    public void Gian_cach_thu_lai_nhan_doi()
    {
        Assert.Equal(TimeSpan.FromSeconds(30), IntegrationOutboxService.BackoffFor(1));
        Assert.Equal(TimeSpan.FromSeconds(60), IntegrationOutboxService.BackoffFor(2));
        Assert.Equal(TimeSpan.FromSeconds(240), IntegrationOutboxService.BackoffFor(4));
    }

    // ───────────────────────────── Huỷ sau khi đã gửi ─────────────────────────────────────

    /// <summary>
    /// 🔴 Gate an toàn người bệnh: huỷ một chỉ định ĐÃ RA TỚI VENDOR phải tự xếp gói CANCELLED.
    ///
    /// Không có nó thì phiếu vẫn nằm trên worklist phòng chụp và người bệnh bị gọi vào chụp
    /// thứ bác sĩ vừa bỏ.
    /// </summary>
    [Fact]
    public async Task Huy_chi_dinh_da_gui_thi_tu_xep_goi_CANCELLED()
    {
        var (db, record, order) = Fixture();
        using var _db = db;

        await db.Outbox.EnqueueOrderAsync(order.OrderID);
        var newRow = db.Db.IntegrationOutboxes.AsTracking().Single();
        var sent = await db.SendOnceAsync(newRow);
        IntegrationOutboxWorker.Stamp(newRow, sent.Success, sent.Detail, sent.ResponseSnippet);
        await db.Uow.SaveChangesAsync();
        db.Db.ChangeTracker.Clear();

        await db.Orders.CancelAsync(order.OrderID, new ParaclinicalCancelRequest { Reason = "Người bệnh không đến" });

        var rows = db.Db.IntegrationOutboxes.AsNoTracking().OrderBy(x => x.OutboxID).ToList();
        Assert.Equal(2, rows.Count);

        var cancelRow = rows[1];
        Assert.Equal(RisOrderStatuses.Cancelled, cancelRow.Operation);
        Assert.Equal(OutboxState.Pending, cancelRow.State);

        var json = JObject.Parse(cancelRow.Payload);
        Assert.Equal("CANCELLED", json["status"]!.Value<string>());
        Assert.Equal(order.OrderNo, json["orderNumber"]!.Value<string>());
        // Gói huỷ mang ĐÚNG số hiệu dòng đã gửi đi — vendor tra theo con số đó chứ không theo
        // mã dịch vụ.
        Assert.Equal(
            RisOrderPayloadBuilder.ComposeLineId(db.ItemsOf(record.RecordID).Single().VendorLineNo!.Value),
            json["orders"]![0]!["id"]!.Value<string>());
    }

    /// <summary>Phiếu CHƯA từng gửi thì huỷ không sinh gói nào — không có gì bên vendor để huỷ,
    /// và một gói CANCELLED cho phiếu họ chưa từng thấy chỉ tạo ra một lỗi bên kia.</summary>
    [Fact]
    public async Task Huy_phieu_chua_gui_thi_khong_xep_goi_nao()
    {
        var (db, _, order) = Fixture();
        using var _db = db;

        await db.Orders.CancelAsync(order.OrderID, new ParaclinicalCancelRequest { Reason = "Nhầm chỉ định" });

        Assert.Empty(db.Db.IntegrationOutboxes.AsNoTracking().ToList());
    }

    /// <summary>Huỷ hai lần cùng một tập dòng ⇒ vẫn đúng một gói CANCELLED.</summary>
    [Fact]
    public async Task Huy_lai_lan_hai_khong_de_them_goi_CANCELLED()
    {
        var (db, _, order) = Fixture();
        using var _db = db;

        await db.Outbox.EnqueueOrderAsync(order.OrderID);
        var row = db.Db.IntegrationOutboxes.AsTracking().Single();
        var sent = await db.SendOnceAsync(row);
        IntegrationOutboxWorker.Stamp(row, sent.Success, sent.Detail, sent.ResponseSnippet);
        await db.Uow.SaveChangesAsync();
        db.Db.ChangeTracker.Clear();

        await db.Orders.CancelAsync(order.OrderID, new ParaclinicalCancelRequest { Reason = "Huỷ" });
        await db.Orders.CancelAsync(order.OrderID, new ParaclinicalCancelRequest { Reason = "Huỷ lần hai" });

        Assert.Equal(1, db.Db.IntegrationOutboxes.AsNoTracking().Count(x => x.Operation == RisOrderStatuses.Cancelled));
    }

    /// <summary>Khoá chống trùng của gói huỷ phải PHÂN BIỆT được hai tập dòng khác nhau —
    /// cắt cụt chuỗi sẽ làm gói huỷ thứ hai bị coi là trùng và không bao giờ tới vendor.</summary>
    [Fact]
    public void Khoa_chong_trung_cua_goi_huy_phan_biet_theo_tap_dong()
    {
        var a = IntegrationOutboxService.DedupKeyFor("RIS", "CANCELLED", "CD0000000042", new[] { 1L, 2L });
        var b = IntegrationOutboxService.DedupKeyFor("RIS", "CANCELLED", "CD0000000042", new[] { 3L, 4L });
        var aAgain = IntegrationOutboxService.DedupKeyFor("RIS", "CANCELLED", "CD0000000042", new[] { 2L, 1L });

        Assert.NotEqual(a, b);
        Assert.Equal(a, aAgain);   // thứ tự dòng không đổi nghĩa của tập
        Assert.True(a.Length <= OutboxFieldLengths.DedupKey);

        var many = IntegrationOutboxService.DedupKeyFor(
            "RIS", "CANCELLED", "CD0000000042", Enumerable.Range(1, 200).Select(x => (long)x));
        Assert.True(many.Length <= OutboxFieldLengths.DedupKey);
    }
}
