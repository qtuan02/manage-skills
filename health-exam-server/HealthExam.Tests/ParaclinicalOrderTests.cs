using Microsoft.EntityFrameworkCore;
using HealthExam.Domain.Common;
using HealthExam.Domain.ExamSessions;
using HealthExam.Domain.ExamRecords;
using HealthExam.Domain.Imports;
using HealthExam.Domain.Paraclinical;
using HealthExam.Domain.Webhooks;
using HealthExam.Domain.Integrations;
using HealthExam.Domain.Catalogs;
using HealthExam.API.Contracts;
using HealthExam.API.Middlewares;
using HealthExam.Application.Common;
using HealthExam.Server.Service;
using Xunit;

namespace HealthExam.Tests;

/// <summary>
/// H3-02 + H3-03 — danh mục dịch vụ 3 tầng và bốn đường ghi của chỉ định CLS.
///
/// Kiểm THẲNG tầng service: thứ cần chốt là LUẬT (bung gói ra đúng mấy dòng, huỷ dòng đã có
/// KQ trả mã nào), còn ràng buộc UNIQUE và transaction thì provider in-memory không có và
/// chỉ chứng minh được trên PostgreSQL thật — xem chú thích của InMemoryTestDb.
/// </summary>
public class ParaclinicalOrderTests
{
    private static (InMemoryTestDb Db, ExamRecord Record, ExamPackage Package) Fixture(
        int serviceCount = 3)
    {
        var db = new InMemoryTestDb();
        var session = db.SeedSession();
        var record = db.SeedRecord(session.SessionID, ExamRecordState.InProgress);

        var services = new List<(long, string, string, string, string)>
        {
            (100001, "XN_CTM", "Tổng phân tích tế bào máu", "XN", "XN_HUYETHOC"),
            (100002, "XN_NT", "Tổng phân tích nước tiểu", "XN", "XN_SINHHOA"),
            (200001, "CDHA_XQNT", "X-quang ngực thẳng", "CDHA", "CDHA_XQUANG"),
            (200002, "CDHA_SA", "Siêu âm ổ bụng", "CDHA", "CDHA_SIEUAM"),
            (300001, "TDCN_DTD", "Điện tâm đồ", "TDCN", "TDCN_TIM")
        };

        var package = db.SeedPackage(services: services.Take(serviceCount).ToArray());
        return (db, record, package);
    }

    // ───────────────────────────── Danh mục 3 tầng (H3-02) ────────────────────────────────

    [Fact]
    public async Task Tang_1_tra_du_ba_loai_kem_so_dich_vu()
    {
        var (db, _, _) = Fixture(serviceCount: 5);
        using var _db = db;

        var categories = await db.Catalog.ListCategoriesAsync();

        Assert.Equal(3, categories.Count);
        Assert.Equal(2, categories.Single(x => x.CategoryCode == "XN").ServiceCount);
        Assert.Equal(2, categories.Single(x => x.CategoryCode == "CDHA").ServiceCount);
        Assert.Equal(1, categories.Single(x => x.CategoryCode == "TDCN").ServiceCount);
    }

    [Fact]
    public async Task Tang_2_loc_theo_loai_va_dem_dung_so_dich_vu()
    {
        var (db, _, _) = Fixture(serviceCount: 5);
        using var _db = db;

        var groups = await db.Catalog.ListGroupsAsync("CDHA", keyword: null);

        Assert.Equal(2, groups.Count);
        Assert.All(groups, g => Assert.Equal("CDHA", g.CategoryCode));
        Assert.All(groups, g => Assert.Equal(1, g.ServiceCount));
    }

    /// <summary>Gate H3-02: tìm theo từ khoá ở tầng 3.</summary>
    [Fact]
    public async Task Tang_3_tim_theo_tu_khoa_va_phan_trang()
    {
        var (db, _, _) = Fixture(serviceCount: 5);
        using var _db = db;

        var found = await db.Catalog.ListServicesAsync(null, null, "nước tiểu", 1, 20);
        Assert.Equal(1, found.Total);
        Assert.Equal("XN_NT", found.Items[0].ServiceCode);

        var page1 = await db.Catalog.ListServicesAsync(null, null, null, 1, 2);
        Assert.Equal(5, page1.Total);
        Assert.Equal(2, page1.Items.Count);
    }

    /// <summary>
    /// Gói bị soft-delete thì dịch vụ của nó biến khỏi pop-up. Nếu không thì "xoá gói" không
    /// xoá gì cả từ góc nhìn người chỉ định.
    /// </summary>
    [Fact]
    public async Task Goi_bi_soft_delete_thi_dich_vu_khong_con_trong_danh_muc()
    {
        using var db = new InMemoryTestDb();
        db.SeedPackage("GOI-CU", isActive: false,
            services: new (long, string, string, string, string)[] { (100001, "XN_CTM", "CTM", "XN", "XN_HUYETHOC") });

        var services = await db.Catalog.ListServicesAsync(null, null, null, 1, 20);
        Assert.Equal(0, services.Total);
    }

    // ───────────────────────────── Chỉ định tay (H3-03) ───────────────────────────────────

    /// <summary>Một lần bấm Lưu với hai loại CLS ⇒ HAI phiếu: vendor nhận theo phiếu, một
    /// phiếu trộn XN với CĐHA thì không gửi đi đâu được.</summary>
    [Fact]
    public async Task Chi_dinh_tay_tach_phieu_theo_loai_CLS()
    {
        var (db, record, _) = Fixture(serviceCount: 5);
        using var _db = db;

        var orders = await db.Orders.CreateManualAsync(record.RecordID,
            new ParaclinicalOrderCreateRequest { ServiceIDs = new List<long> { 100001, 200001, 200002 } });

        Assert.Equal(2, orders.Count);
        Assert.Single(orders.Single(o => o.ParaclinicalKind == "XN").Items);
        Assert.Equal(2, orders.Single(o => o.ParaclinicalKind == "CDHA").Items.Count);

        // Mã phiếu theo nếp HIS: 2 ký tự đầu rồi thuần số (đường về của RIS cắt Substring(2)).
        Assert.All(orders, o =>
        {
            Assert.StartsWith("CD", o.OrderNo);
            Assert.True(long.TryParse(o.OrderNo[2..], out _), $"Phần sau tiền tố phải thuần số: {o.OrderNo}");
        });

        // Dòng vừa tạo nằm ở "Chờ chỉ định" — chưa gửi tới phòng thực hiện.
        Assert.All(db.ItemsOf(record.RecordID), i => Assert.Equal(ParaclinicalItemState.Ordered, i.State));
    }

    /// <summary>Chỉ định một ServiceID không có trong danh mục ⇒ 4001 và KHÔNG ghi gì. Bỏ qua
    /// lặng lẽ là để bác sĩ tưởng đã chỉ định đủ.</summary>
    [Fact]
    public async Task Chi_dinh_dich_vu_la_thi_hong_ca_luot()
    {
        var (db, record, _) = Fixture();
        using var _db = db;

        var ex = await Assert.ThrowsAsync<HealthExamException>(() =>
            db.Orders.CreateManualAsync(record.RecordID,
                new ParaclinicalOrderCreateRequest { ServiceIDs = new List<long> { 100001, 999999 } }));

        Assert.Equal(ErrorCodes.BadRequest, ex.ErrorCode);
        Assert.Contains("999999", ex.Message);
        Assert.Empty(db.ItemsOf(record.RecordID));
    }

    // ───────────────────────────── Bung theo gói (H3-03) ──────────────────────────────────

    /// <summary>Gate H3-03: bung gói N dịch vụ ⇒ đúng N dòng, cả N mang SourcePackageID.</summary>
    [Fact]
    public async Task Bung_goi_ra_dung_so_dong_va_giu_SourcePackageID()
    {
        var (db, record, package) = Fixture(serviceCount: 5);
        using var _db = db;

        var orders = await db.Orders.CreateFromPackageAsync(record.RecordID,
            new ParaclinicalOrderFromPackageRequest { PackageID = package.PackageID });

        var items = db.ItemsOf(record.RecordID);
        Assert.Equal(5, items.Count);
        Assert.All(items, i => Assert.Equal(package.PackageID, i.SourcePackageID));
        Assert.All(orders, o => Assert.Equal(package.PackageID, o.SourcePackageID));

        // 5 dịch vụ thuộc 3 loại CLS ⇒ 3 phiếu.
        Assert.Equal(3, orders.Count);
    }

    /// <summary>Bấm "Chỉ định theo gói" hai lần KHÔNG được sinh hai bộ chỉ định song song —
    /// điều kiện (B) sẽ phải đợi cả hai.</summary>
    [Fact]
    public async Task Bung_goi_lan_hai_khong_nhan_doi_chi_dinh()
    {
        var (db, record, package) = Fixture(serviceCount: 3);
        using var _db = db;

        await db.Orders.CreateFromPackageAsync(record.RecordID,
            new ParaclinicalOrderFromPackageRequest { PackageID = package.PackageID });

        var ex = await Assert.ThrowsAsync<HealthExamException>(() =>
            db.Orders.CreateFromPackageAsync(record.RecordID,
                new ParaclinicalOrderFromPackageRequest { PackageID = package.PackageID }));

        Assert.Equal(ErrorCodes.InvalidState, ex.ErrorCode);
        Assert.Equal(3, db.ItemsOf(record.RecordID).Count);
    }

    // ───────────────────────────── Huỷ (H3-03, gate 4094) ─────────────────────────────────

    /// <summary>★ Gate H3-03: huỷ chỉ định ĐÃ CÓ KẾT QUẢ ⇒ 4094, không phải 4090.</summary>
    [Fact]
    public async Task Huy_chi_dinh_da_co_ket_qua_tra_4094()
    {
        var (db, record, _) = Fixture();
        using var _db = db;

        var order = db.SeedOrder(record, items: new[]
        {
            (100001L, "XN_CTM", ParaclinicalItemState.Done),
            (100002L, "XN_NT", ParaclinicalItemState.Waiting)
        });

        var ex = await Assert.ThrowsAsync<HealthExamException>(() =>
            db.Orders.CancelAsync(order.OrderID, new ParaclinicalCancelRequest { Reason = "Nhầm chỉ định" }));

        Assert.Equal(ErrorCodes.ParaclinicalResultExists, ex.ErrorCode);
        Assert.Equal(409, ErrorCodes.ToHttpStatus(ex.ErrorCode));

        // Và KHÔNG huỷ nửa vời: dòng chưa có KQ cũng phải còn nguyên.
        Assert.All(db.ItemsOf(record.RecordID),
            i => Assert.NotEqual(ParaclinicalItemState.Cancelled, i.State));
    }

    [Fact]
    public async Task Huy_ca_phieu_thi_tat_phieu_va_ghi_ly_do()
    {
        var (db, record, _) = Fixture();
        using var _db = db;

        var order = db.SeedOrder(record, items: new[]
        {
            (100001L, "XN_CTM", ParaclinicalItemState.Ordered),
            (100002L, "XN_NT", ParaclinicalItemState.Waiting)
        });

        var view = await db.Orders.CancelAsync(order.OrderID,
            new ParaclinicalCancelRequest { Reason = "Người bệnh không đến" });

        Assert.False(view.IsActive);
        Assert.All(db.ItemsOf(record.RecordID), i =>
        {
            Assert.Equal(ParaclinicalItemState.Cancelled, i.State);
            Assert.Equal("Người bệnh không đến", i.CancelReason);
            Assert.NotNull(i.CancelledAt);
        });
    }

    /// <summary>Huỷ LẺ một dòng: phiếu vẫn sống, dòng còn lại không đụng.</summary>
    [Fact]
    public async Task Huy_le_mot_dong_khong_dung_dong_con_lai()
    {
        var (db, record, _) = Fixture();
        using var _db = db;

        var order = db.SeedOrder(record, items: new[]
        {
            (100001L, "XN_CTM", ParaclinicalItemState.Ordered),
            (100002L, "XN_NT", ParaclinicalItemState.Waiting)
        });
        var victim = db.ItemsOf(record.RecordID).Single(x => x.ServiceCode == "XN_CTM");

        var view = await db.Orders.CancelAsync(order.OrderID, new ParaclinicalCancelRequest
        {
            OrderItemIDs = new List<Guid> { victim.OrderItemID },
            Reason = "Trùng dịch vụ"
        });

        Assert.True(view.IsActive);
        var items = db.ItemsOf(record.RecordID);
        Assert.Equal(ParaclinicalItemState.Cancelled, items.Single(x => x.ServiceCode == "XN_CTM").State);
        Assert.Equal(ParaclinicalItemState.Waiting, items.Single(x => x.ServiceCode == "XN_NT").State);
    }

    // ───────────────────────────── Đổi trạng thái tay ─────────────────────────────────────

    /// <summary>
    /// ★ Đường NGƯỜI BẤM đưa dịch vụ lên "Đã trả KQ" — bắt buộc phải có cho cơ sở không dùng
    /// scan, vì vendor RIS không biểu đạt nổi trạng thái đó
    /// (docs/handoff/20260826-236-chot-p3-cls.md §5.1).
    /// </summary>
    [Fact]
    public async Task Bam_tay_dua_dich_vu_len_da_tra_KQ_va_dong_dau_ResultAt()
    {
        var (db, record, _) = Fixture();
        using var _db = db;

        var order = db.SeedOrder(record, items: new[]
        {
            (100001L, "XN_CTM", ParaclinicalItemState.Waiting)
        });

        await db.Orders.ChangeStateAsync(order.OrderID, new ParaclinicalStateRequest
        {
            State = (short)ParaclinicalItemState.Done,
            IsAbnormal = true
        });

        var item = db.ItemsOf(record.RecordID).Single();
        Assert.Equal(ParaclinicalItemState.Done, item.State);
        Assert.NotNull(item.ResultAt);
        Assert.Equal(ParaclinicalResultSources.Manual, item.ResultSourceKind);
        Assert.True(item.IsAbnormal);
    }

    /// <summary>Huỷ (4) KHÔNG đi qua PUT /state — nó cần lý do và có chốt chặn 4094 riêng.</summary>
    [Fact]
    public async Task Doi_trang_thai_sang_Huy_bi_tu_choi()
    {
        var (db, record, _) = Fixture();
        using var _db = db;

        var order = db.SeedOrder(record, items: new[] { (100001L, "XN_CTM", ParaclinicalItemState.Waiting) });

        var ex = await Assert.ThrowsAsync<HealthExamException>(() =>
            db.Orders.ChangeStateAsync(order.OrderID,
                new ParaclinicalStateRequest { State = (short)ParaclinicalItemState.Cancelled }));

        Assert.Equal(ErrorCodes.BadRequest, ex.ErrorCode);
    }

    /// <summary>Dịch vụ đã huỷ là trạng thái CUỐI: không lệnh nào kéo nó về lại được.</summary>
    [Fact]
    public async Task Dich_vu_da_huy_khong_quay_lai_duoc()
    {
        var (db, record, _) = Fixture();
        using var _db = db;

        var order = db.SeedOrder(record, items: new[] { (100001L, "XN_CTM", ParaclinicalItemState.Cancelled) });

        var ex = await Assert.ThrowsAsync<HealthExamException>(() =>
            db.Orders.ChangeStateAsync(order.OrderID,
                new ParaclinicalStateRequest { State = (short)ParaclinicalItemState.Waiting }));

        Assert.Equal(ErrorCodes.InvalidState, ex.ErrorCode);
        Assert.Equal(ParaclinicalItemState.Cancelled, db.ItemsOf(record.RecordID).Single().State);
    }

    // ─────────────── F-3 · Lệnh CẢ PHIẾU không xoá trắng dòng đã có KQ ───────────────────

    /// <summary>Dựng đúng phiếu hỗn hợp của review 236 §3.2: 5 dòng, 3 dòng đã có KQ — hai
    /// dòng từ scan (có AttachmentID) và một dòng nhập tay (không có bản sao ở đâu).</summary>
    private static (ParaclinicalOrder Order, Guid ScanItemId, Guid ManualItemId, Guid AttachmentId)
        PhieuHonHop(InMemoryTestDb db, ExamRecord record)
    {
        var order = db.SeedOrder(record, items: new[]
        {
            (100001L, "XN_CTM", ParaclinicalItemState.Done),
            (100002L, "XN_NT", ParaclinicalItemState.Done),
            (200001L, "CDHA_XQNT", ParaclinicalItemState.Done),
            (200002L, "CDHA_SA", ParaclinicalItemState.Ordered),
            (300001L, "TDCN_DTD", ParaclinicalItemState.Waiting)
        });

        var attachmentId = Guid.Parse("bbbbbbbb-0000-0000-0000-0000000000a1");
        // ⚠️ .AsTracking() BẮT BUỘC: InMemoryTestDb dựng context với
        // QueryTrackingBehavior.NoTracking ở cấp toàn cục (InMemoryTestDb.cs:70). Thiếu nó thì
        // vòng gán dưới đây sửa lên thực thể KHÔNG được theo dõi, SaveChanges ghi ra con số 0
        // và fixture lặng lẽ dựng ra phiếu KHÔNG có AttachmentID — tức test vẫn chạy nhưng
        // không còn đo cái nó nói là đang đo.
        var rows = db.Db.ParaclinicalOrderItems.AsTracking().Where(x => x.OrderID == order.OrderID).ToList();
        foreach (var code in new[] { "XN_CTM", "XN_NT" })
        {
            var row = rows.Single(x => x.ServiceCode == code);
            row.ResultSourceKind = ParaclinicalResultSources.Scan;
            row.AttachmentID = code == "XN_CTM" ? attachmentId : Guid.NewGuid();
            row.IsAbnormal = true;
        }
        db.Db.SaveChanges();
        db.Db.ChangeTracker.Clear();

        return (order,
            rows.Single(x => x.ServiceCode == "XN_CTM").OrderItemID,
            rows.Single(x => x.ServiceCode == "CDHA_XQNT").OrderItemID,
            attachmentId);
    }

    /// <summary>
    /// ★ F-3 (review 236 §2.2 · §3.2) — MẤT DỮ LIỆU THẬT nếu không có chốt này.
    ///
    /// Nhân viên muốn đánh dấu 2 dòng chưa làm là "Chờ thực hiện", gửi PUT {"State":1} mà QUÊN
    /// OrderItemIDs. Trước bản vá: cả 5 dòng được chọn ⇒ 3 dòng đã có KQ bị xoá trắng
    /// ResultAt/ResultSourceKind/AttachmentID/IsAbnormal, trả 200 không cảnh báo. KQ nhập tay
    /// mất vĩnh viễn, và AttachmentID vừa bị xoá là con trỏ DUY NHẤT mà đường .removed còn đọc
    /// sau bản vá B-2 ⇒ lượt gỡ thật sau đó rơi vào dead-letter như một sự cố giả.
    /// </summary>
    [Fact]
    public async Task Doi_trang_thai_ca_phieu_khong_dung_toi_dong_da_co_ket_qua()
    {
        var (db, record, _) = Fixture(serviceCount: 5);
        using var _db = db;

        var (order, _, _, attachmentId) = PhieuHonHop(db, record);

        var view = await db.Orders.ChangeStateAsync(order.OrderID,
            new ParaclinicalStateRequest { State = (short)ParaclinicalItemState.Waiting });

        Assert.NotNull(view);

        var items = db.ItemsOf(record.RecordID).ToDictionary(x => x.ServiceCode);

        // Hai dòng chưa làm: đúng thứ nhân viên muốn đổi.
        Assert.Equal(ParaclinicalItemState.Waiting, items["CDHA_SA"].State);
        Assert.Equal(ParaclinicalItemState.Waiting, items["TDCN_DTD"].State);

        // Ba dòng đã có KQ: KHÔNG đụng tới, và mọi con trỏ kết quả còn nguyên.
        foreach (var code in new[] { "XN_CTM", "XN_NT", "CDHA_XQNT" })
        {
            Assert.Equal(ParaclinicalItemState.Done, items[code].State);
            Assert.NotNull(items[code].ResultAt);
            Assert.NotEmpty(items[code].ResultSourceKind);
        }
        Assert.Equal(attachmentId, items["XN_CTM"].AttachmentID);
        Assert.Equal(ParaclinicalResultSources.Manual, items["CDHA_XQNT"].ResultSourceKind);
    }

    /// <summary>
    /// ★ Nửa kia của cùng một chốt — [Huỷ KQ scan] PHẢI đi lọt. Chốt là "chỉ khi lệnh NÊU ĐÍCH
    /// DANH", không phải "cấm rời khỏi Đã trả KQ": siết thành cấm là đóng luôn đường huỷ kết
    /// quả, tức tái lập đúng cái cột cờ IsAllResultReturned đã bị bỏ đi.
    /// </summary>
    [Fact]
    public async Task Neu_dich_danh_thi_van_lui_duoc_dong_da_co_ket_qua()
    {
        var (db, record, _) = Fixture(serviceCount: 5);
        using var _db = db;

        var (order, scanItemId, _, _) = PhieuHonHop(db, record);

        await db.Orders.ChangeStateAsync(order.OrderID, new ParaclinicalStateRequest
        {
            OrderItemIDs = new List<Guid> { scanItemId },
            State = (short)ParaclinicalItemState.Waiting
        });

        var items = db.ItemsOf(record.RecordID).ToDictionary(x => x.ServiceCode);

        Assert.Equal(ParaclinicalItemState.Waiting, items["XN_CTM"].State);
        Assert.Null(items["XN_CTM"].ResultAt);
        Assert.Null(items["XN_CTM"].AttachmentID);
        Assert.Null(items["XN_CTM"].IsAbnormal);

        // Dòng đã có KQ KHÔNG được nêu tên thì vẫn nguyên.
        Assert.Equal(ParaclinicalItemState.Done, items["XN_NT"].State);
        Assert.NotNull(items["XN_NT"].AttachmentID);
    }

    /// <summary>
    /// Mọi dòng đều đã có KQ ⇒ 4094 kèm danh sách dòng đang chặn, KHÔNG phải 4090 "phiếu không
    /// còn dòng nào". Cùng mã lỗi và cùng hình payload với CancelAsync, để FE đọc một thân lỗi
    /// chứ không phải hai.
    /// </summary>
    [Fact]
    public async Task Ca_phieu_da_co_ket_qua_thi_bao_4094_chu_khong_bao_phieu_rong()
    {
        var (db, record, _) = Fixture(serviceCount: 5);
        using var _db = db;

        var order = db.SeedOrder(record, items: new[]
        {
            (100001L, "XN_CTM", ParaclinicalItemState.Done),
            (100002L, "XN_NT", ParaclinicalItemState.Done)
        });

        var ex = await Assert.ThrowsAsync<HealthExamException>(() =>
            db.Orders.ChangeStateAsync(order.OrderID,
                new ParaclinicalStateRequest { State = (short)ParaclinicalItemState.Waiting }));

        Assert.Equal(ErrorCodes.ParaclinicalResultExists, ex.ErrorCode);
        Assert.All(db.ItemsOf(record.RecordID),
            x => Assert.Equal(ParaclinicalItemState.Done, x.State));
    }

    /// <summary>
    /// ★ IsAbnormal và ResultRefID là trường ĐƠN ở cấp thân gói. Bấm "trả KQ cả phiếu" trên
    /// phiếu 4 dòng chỉ 1 dòng bất thường mà dán cho cả 4 thì hồ sơ nói sai về người bệnh, và
    /// ResultRefID trỏ cùng một chuỗi thì mất khả năng đối soát ngược với LIS ở P3b.
    ///
    /// Từ chối chứ không bỏ qua lặng lẽ: bỏ qua là làm mất đúng thứ người dùng vừa gõ.
    /// </summary>
    [Fact]
    public async Task Tra_KQ_ca_phieu_khong_dan_bat_thuong_cho_moi_dong()
    {
        var (db, record, _) = Fixture(serviceCount: 5);
        using var _db = db;

        var order = db.SeedOrder(record, items: new[]
        {
            (100001L, "XN_CTM", ParaclinicalItemState.Waiting),
            (100002L, "XN_NT", ParaclinicalItemState.Waiting)
        });

        var ex = await Assert.ThrowsAsync<HealthExamException>(() =>
            db.Orders.ChangeStateAsync(order.OrderID, new ParaclinicalStateRequest
            {
                State = (short)ParaclinicalItemState.Done,
                IsAbnormal = true
            }));

        Assert.Equal(ErrorCodes.BadRequest, ex.ErrorCode);
        Assert.All(db.ItemsOf(record.RecordID),
            x => Assert.Equal(ParaclinicalItemState.Waiting, x.State));

        // Nêu đích danh một dòng thì đi được — chốt là HÌNH CỦA LỆNH, không phải cấm trường.
        var one = db.ItemsOf(record.RecordID).First(x => x.ServiceCode == "XN_CTM");
        await db.Orders.ChangeStateAsync(order.OrderID, new ParaclinicalStateRequest
        {
            OrderItemIDs = new List<Guid> { one.OrderItemID },
            State = (short)ParaclinicalItemState.Done,
            IsAbnormal = true,
            ResultRefID = "LIS-2026-0001"
        });

        var after = db.ItemsOf(record.RecordID).ToDictionary(x => x.ServiceCode);
        Assert.True(after["XN_CTM"].IsAbnormal);
        Assert.Equal("LIS-2026-0001", after["XN_CTM"].ResultRefID);
        Assert.Null(after["XN_NT"].IsAbnormal);
    }

    // ───────────────────────────── Chốt chặn phạm vi ──────────────────────────────────────

    /// <summary>Đợt đã đóng ⇒ 4091 cho mọi đường ghi thuộc đợt, kể cả chỉ định.</summary>
    [Fact]
    public async Task Dot_da_dong_thi_khong_chi_dinh_them_duoc()
    {
        using var db = new InMemoryTestDb();
        var session = db.SeedSession(ExamSessionState.Closed);
        var record = db.SeedRecord(session.SessionID, ExamRecordState.InProgress);
        db.SeedPackage(services: new (long, string, string, string, string)[]
            { (100001, "XN_CTM", "CTM", "XN", "XN_HUYETHOC") });

        var ex = await Assert.ThrowsAsync<HealthExamException>(() =>
            db.Orders.CreateManualAsync(record.RecordID,
                new ParaclinicalOrderCreateRequest { ServiceIDs = new List<long> { 100001 } }));

        Assert.Equal(ErrorCodes.SessionClosed, ex.ErrorCode);
    }

    /// <summary>Hồ sơ của đơn vị khác ⇒ 4040, không phải trả về dữ liệu của họ.</summary>
    [Fact]
    public async Task Ho_so_cua_don_vi_khac_khong_tra_ve()
    {
        using var db = new InMemoryTestDb();
        var session = db.SeedSession(divisionId: "DON-VI-KHAC");
        var record = db.SeedRecord(session.SessionID, divisionId: "DON-VI-KHAC");

        var ex = await Assert.ThrowsAsync<HealthExamException>(() =>
            db.Orders.ListByRecordAsync(record.RecordID));

        Assert.Equal(ErrorCodes.NotFound, ex.ErrorCode);
    }

    /// <summary>Hồ sơ đã huỷ không được chỉ định thêm.</summary>
    [Theory]
    [InlineData(ExamRecordState.RegistrationCancelled)]
    [InlineData(ExamRecordState.ExamCancelled)]
    public async Task Chi_dinh_tren_ho_so_da_huy_bi_tu_choi(ExamRecordState state)
    {
        using var db = new InMemoryTestDb();
        var session = db.SeedSession();
        var record = db.SeedRecord(session.SessionID, state: state);
        db.SeedPackage(services: new (long, string, string, string, string)[]
            { (100001, "XN_CTM", "CTM", "XN", "XN_HUYETHOC") });

        var ex = await Assert.ThrowsAsync<HealthExamException>(() =>
            db.Orders.CreateManualAsync(record.RecordID,
                new ParaclinicalOrderCreateRequest { ServiceIDs = new List<long> { 100001 } }));

        Assert.Equal(ErrorCodes.InvalidState, ex.ErrorCode);
    }
}
