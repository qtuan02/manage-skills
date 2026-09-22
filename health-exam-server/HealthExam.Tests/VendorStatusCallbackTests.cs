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
using HealthExam.API.Contracts;
using HealthExam.API.Middlewares;
using HealthExam.Application.Common;
using HealthExam.Server.Service;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json.Linq;
using Xunit;

namespace HealthExam.Tests;

/// <summary>
/// P3b — ĐƯỜNG VỀ của vendor, và vòng đi-về khép kín (gate G-P3b-1).
///
/// Ba thứ được chốt ở đây, cả ba đều là chốt CHỐNG SAI DỮ LIỆU Y TẾ chứ không phải chốt hình
/// thức: vendor chỉ tiến không lùi, vendor không bao giờ chạm được "Đã trả KQ", và mọi phép
/// tra đều kèm tenant.
/// </summary>
public class VendorStatusCallbackTests
{
    private static RisOptions Configured() => new()
    {
        BaseUrl = "https://ris.test/hisris/ris/00000",
        Username = "hex",
        Password = "s3cret",
        DivisionId = FakeHealthExamContext.DefaultDivisionId
    };

    private static (InMemoryTestDb Db, ExamRecord Record, ParaclinicalOrder Order) Fixture(
        ParaclinicalItemState state = ParaclinicalItemState.Waiting, long vendorLineNo = 7)
    {
        var db = new InMemoryTestDb().UseRis(Configured());
        var session = db.SeedSession();
        var record = db.SeedRecord(
            session.SessionID, ExamRecordState.InProgress,
            patientCode: "BN000123", identityNumber: "079201004321", genderId: 1);

        var order = db.SeedOrder(record, kind: "CDHA",
            items: new[] { (200001L, "CDHA_XQNT", state) });

        // Dòng đã ra tới vendor: đường về chỉ tra được bằng con số này.
        var item = db.Db.ParaclinicalOrderItems.AsTracking().Single();
        item.VendorLineNo = vendorLineNo;
        db.Db.SaveChanges();
        db.Db.ChangeTracker.Clear();

        return (db, record, order);
    }

    private static VendorStatusRequest Packet(long lineNo, byte newStatus) => new()
    {
        OrderID = RisOrderPayloadBuilder.ComposeLineId(lineNo),
        VoucherType = "2",
        NewStatus = newStatus
    };

    // ───────────────────────────── Áp cờ trạng thái ───────────────────────────────────────

    [Theory]
    [InlineData((byte)0, ParaclinicalItemState.Waiting)]
    [InlineData((byte)1, ParaclinicalItemState.InProgress)]
    public async Task Anh_xa_bang_so_cua_vendor_sang_bang_so_cua_ta(byte newStatus, ParaclinicalItemState expected)
    {
        var (db, record, _) = Fixture(state: ParaclinicalItemState.Ordered);
        using var _db = db;

        var result = await db.Vendor().ApplyAsync(Packet(7, newStatus));

        Assert.Equal((short)expected, result.State);
        Assert.False(result.Duplicated);
        Assert.Equal(expected, db.ItemsOf(record.RecordID).Single().State);
    }

    /// <summary>
    /// Gửi lại cùng một cờ KHÔNG phải lỗi: đường về là cờ trạng thái, không phải sự kiện.
    /// Trả mã lỗi ở đây là dạy vendor thử lại vĩnh viễn một gói vốn đã đúng.
    /// </summary>
    [Fact]
    public async Task Gui_lai_cung_mot_co_thi_no_op_va_bao_Duplicated()
    {
        var (db, record, _) = Fixture(state: ParaclinicalItemState.InProgress);
        using var _db = db;

        var result = await db.Vendor().ApplyAsync(Packet(7, newStatus: 1));

        Assert.True(result.Duplicated);
        Assert.Equal((short)ParaclinicalItemState.InProgress, result.State);
        Assert.Equal(ParaclinicalItemState.InProgress, db.ItemsOf(record.RecordID).Single().State);
    }

    /// <summary>
    /// 🔴 Vendor CHỈ ĐƯỢC TIẾN. Gói ĐÒI LÙI ⇒ 4090, và dòng dịch vụ KHÔNG đổi.
    ///
    /// Lùi được nghĩa là một gói đến muộn (mạng chớp, vendor retry) kéo dòng dịch vụ đã xong
    /// ngược về "chờ" — và điều kiện (B) mở ra lại sau khi bác sĩ đã đọc xong kết quả.
    ///
    /// ★ Vì sao 4090 chứ KHÔNG phải 200 kèm Duplicated=true như bản đầu: "đòi lùi" và "gửi
    /// lại đúng cờ cũ" là hai chuyện khác nhau và đòi hai câu trả lời khác nhau. Gộp lại là
    /// trả cờ "trùng" cho một lệnh vừa BỊ TỪ CHỐI — bên vendor đọc ra "đã đúng rồi" và thôi
    /// không hỏi lại, trong khi thứ họ vừa yêu cầu đã bị bỏ. Tài liệu ở
    /// VendorIntegrationController vẫn luôn ghi "đòi LÙI ⇒ 4090"; đây là chỗ mã đuổi kịp.
    /// </summary>
    [Fact]
    public async Task Vendor_doi_lui_trang_thai_thi_4090_chu_khong_bao_trung()
    {
        var (db, record, _) = Fixture(state: ParaclinicalItemState.InProgress);
        using var _db = db;

        var ex = await Assert.ThrowsAsync<HealthExamException>(
            () => db.Vendor().ApplyAsync(Packet(7, newStatus: 0)));   // đòi về Chờ thực hiện

        Assert.Equal(ErrorCodes.InvalidState, ex.ErrorCode);
        Assert.Equal(ParaclinicalItemState.InProgress, db.ItemsOf(record.RecordID).Single().State);
    }

    /// <summary>
    /// Ngược lại: gửi lại ĐÚNG cờ hiện tại là chuyện thường của mọi cầu tích hợp ⇒ 200 kèm
    /// Duplicated=true, không phải lỗi. Trả mã lỗi ở đây là dạy vendor thử lại vĩnh viễn một
    /// gói vốn đã đúng.
    ///
    /// Đi kèm ca trên như một CẶP: một mình ca 4090 thì ai đó "sửa" bằng cách chặn cứng mọi
    /// gói không-tiến vẫn xanh, mà như thế là đóng luôn đường gửi lại hợp lệ.
    /// </summary>
    [Fact]
    public async Task Vendor_gui_lai_dung_co_hien_tai_thi_200_kem_Duplicated()
    {
        var (db, record, _) = Fixture(state: ParaclinicalItemState.InProgress);
        using var _db = db;

        var result = await db.Vendor().ApplyAsync(Packet(7, newStatus: 1));   // đúng cờ đang có

        Assert.True(result.Duplicated);
        Assert.Equal((short)ParaclinicalItemState.InProgress, result.State);
        Assert.Equal(ParaclinicalItemState.InProgress, db.ItemsOf(record.RecordID).Single().State);
    }

    /// <summary>
    /// 🔴 Vendor KHÔNG BAO GIỜ đưa được dòng lên "Đã trả KQ" — và đây là giới hạn CỦA HỌ:
    /// DTO có ba trường, NewStatus chỉ nhận {0,1}. Vì thế điều kiện (B) chỉ đóng được bằng
    /// SCAN_RESULT hoặc bấm tay (docs/handoff/20260826-236-chot-p3-cls.md §5.1).
    ///
    /// Giá trị lạ ⇒ 4001, KHÔNG rơi êm về "Chờ thực hiện": một vendor gửi giá trị ngoài hợp
    /// đồng nghĩa là bên kia vừa đổi thứ gì đó, và đoán hộ họ sẽ ghi một trạng thái không ai
    /// nói lên hồ sơ thật.
    /// </summary>
    [Theory]
    [InlineData((byte)2)]
    [InlineData((byte)3)]
    [InlineData((byte)9)]
    public async Task Gia_tri_ngoai_hop_dong_thi_4001_chu_khong_doan(byte newStatus)
    {
        var (db, record, _) = Fixture();
        using var _db = db;

        var ex = await Assert.ThrowsAsync<HealthExamException>(() => db.Vendor().ApplyAsync(Packet(7, newStatus)));

        Assert.Equal(ErrorCodes.BadRequest, ex.ErrorCode);
        Assert.Equal(ParaclinicalItemState.Waiting, db.ItemsOf(record.RecordID).Single().State);
        Assert.Null(VendorStatusService.MapVendorStatus(newStatus));
    }

    /// <summary>Dịch vụ đã HUỶ là trạng thái cuối — gói vendor tới sau đó bị từ chối (4090),
    /// không lặng lẽ hồi sinh một chỉ định đã bỏ.</summary>
    [Fact]
    public async Task Dich_vu_da_huy_thi_goi_vendor_bi_tu_choi()
    {
        var (db, record, _) = Fixture(state: ParaclinicalItemState.Cancelled);
        using var _db = db;

        var ex = await Assert.ThrowsAsync<HealthExamException>(() => db.Vendor().ApplyAsync(Packet(7, 1)));

        Assert.Equal(ErrorCodes.InvalidState, ex.ErrorCode);
        Assert.Equal(ParaclinicalItemState.Cancelled, db.ItemsOf(record.RecordID).Single().State);
    }

    [Fact]
    public async Task OrderID_sai_quy_uoc_thi_4001()
    {
        var (db, _, _) = Fixture();
        using var _db = db;

        var ex = await Assert.ThrowsAsync<HealthExamException>(
            () => db.Vendor().ApplyAsync(new VendorStatusRequest { OrderID = "khong-phai-so", NewStatus = 1 }));

        Assert.Equal(ErrorCodes.BadRequest, ex.ErrorCode);
    }

    /// <summary>
    /// ★ Tra KÈM TENANT. Cùng một số hiệu dòng ở đơn vị khác thì KHÔNG được đụng tới: hệ
    /// thống chạy nhiều DB tenant, pilot dùng chung DB với prod, và một kết quả trả về của
    /// đơn vị khác trông vẫn hoàn toàn hợp lý.
    /// </summary>
    [Fact]
    public async Task Khong_ap_duoc_len_dich_vu_cua_don_vi_khac()
    {
        var (db, record, _) = Fixture();
        using var _db = db;

        db.Ctx.DivisionId = "DON_VI_KHAC";

        var ex = await Assert.ThrowsAsync<HealthExamException>(() => db.Vendor().ApplyAsync(Packet(7, 1)));

        Assert.Equal(ErrorCodes.NotFound, ex.ErrorCode);
        Assert.Equal(ParaclinicalItemState.Waiting, db.ItemsOf(record.RecordID).Single().State);
    }

    // ───────────────────────────── VÒNG ĐI–VỀ KHÉP KÍN ────────────────────────────────────

    /// <summary>
    /// ★★ GATE G-P3b-1 — vòng đi-về đầy đủ với stub cục bộ, KHÔNG một con số nào tự khai:
    ///
    /// 1. Xếp hàng và gửi phiếu ⇒ stub GIỮ LẠI nguyên văn gói trên dây;
    /// 2. Đọc <c>orders[0].id</c> RA TỪ CHÍNH GÓI ĐÓ (không phải từ biến trong test);
    /// 3. Phát lại con số đó vào đường về như VietRad sẽ làm;
    /// 4. Dòng dịch vụ đổi trạng thái đúng như hợp đồng.
    ///
    /// Bước 2 là điểm mấu chốt: nếu quy ước ghép ID và quy ước đọc ID lệch nhau thì hai chiều
    /// kiểm riêng vẫn xanh, còn vòng này thì đỏ — và ngoài đời, lệch quy ước nghĩa là cầu im
    /// lặng không áp trạng thái nào.
    /// </summary>
    [Fact]
    public async Task Vong_di_ve_khep_kin_voi_stub_cuc_bo()
    {
        var db = new InMemoryTestDb().UseRis(Configured());
        using var _db = db;

        var session = db.SeedSession();
        var record = db.SeedRecord(
            session.SessionID, ExamRecordState.InProgress,
            patientCode: "BN000123", identityNumber: "079201004321", genderId: 1);
        var order = db.SeedOrder(record, kind: "CDHA",
            items: new[] { (200001L, "CDHA_XQNT", ParaclinicalItemState.Ordered) });

        // (1) đi ra
        await db.Outbox.EnqueueOrderAsync(order.OrderID);
        var row = db.Db.IntegrationOutboxes.AsTracking().Single();
        var sent = await db.SendOnceAsync(row);
        IntegrationOutboxWorker.Stamp(row, sent.Success, sent.Detail, sent.ResponseSnippet);
        await db.Uow.SaveChangesAsync();
        db.Db.ChangeTracker.Clear();

        Assert.True(sent.Success);
        Assert.Equal(ParaclinicalItemState.Waiting, db.ItemsOf(record.RecordID).Single().State);

        // (2) đọc số hiệu RA TỪ GÓI ĐÃ GỬI
        var onWire = JObject.Parse(db.RisStub.Received.Single());
        var wireLineId = onWire["orders"]![0]!["id"]!.Value<string>();

        // (3) vendor gọi về đúng con số đó
        var back = await db.Vendor().ApplyAsync(new VendorStatusRequest
        {
            OrderID = wireLineId,
            VoucherType = onWire["voucherType"]!.Value<string>(),
            NewStatus = 1
        });

        // (4) hệ quả
        Assert.False(back.Duplicated);
        Assert.Equal((short)ParaclinicalItemState.InProgress, back.State);
        Assert.Equal("CDHA_XQNT", back.ServiceCode);
        Assert.Equal(ParaclinicalItemState.InProgress, db.ItemsOf(record.RecordID).Single().State);

        // Và vendor vẫn KHÔNG đóng được điều kiện (B): dòng chưa lên "Đã trả KQ".
        Assert.True(ParaclinicalOrderItem.BlocksConditionB(db.ItemsOf(record.RecordID).Single().State));
    }
}
