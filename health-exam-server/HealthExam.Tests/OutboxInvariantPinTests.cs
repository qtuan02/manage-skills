using System.Net;
using System.Text;
using HealthExam.API.Middlewares;
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
using HealthExam.Application.Common;
using HealthExam.Server.Service;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json.Linq;
using Xunit;

namespace HealthExam.Tests;

/// <summary>
/// P3b — GHIM những khẳng định mà nghiệm thu vòng 3 chỉ ra là "đúng nhưng không có gì giữ
/// cho nó tiếp tục đúng" (mục 7 và 8 của phần NÊN SỬA).
///
/// Mỗi ca ở đây được chọn vì nó ĐỎ LÊN với một đột biến cụ thể, và đột biến đó đã được chạy
/// thật để xác nhận — chứ không phải vì nó "phủ thêm một hàm". Đột biến tương ứng ghi trong
/// chú thích của từng ca, để lần sau ai đó sửa mã còn biết ca này canh cái gì.
/// </summary>
public class OutboxInvariantPinTests
{
    private static RisOptions Configured(string divisionId = FakeHealthExamContext.DefaultDivisionId) => new()
    {
        BaseUrl = "https://ris.test/hisris/ris/00000",
        Username = "hex",
        Password = "s3cret",
        DivisionId = divisionId
    };

    private static (InMemoryTestDb Db, ExamRecord Record, ParaclinicalOrder Order) Fixture(
        RisOptions options = null, RisStubHandler stub = null)
    {
        var db = new InMemoryTestDb();
        db.UseRis(options ?? Configured(), stub);

        var session = db.SeedSession();
        var record = db.SeedRecord(
            session.SessionID, ExamRecordState.InProgress,
            patientCode: "BN000123", identityNumber: "079201004321", genderId: 1);
        var order = db.SeedOrder(record, kind: "CDHA",
            items: new[] { (200001L, "CDHA_XQNT", ParaclinicalItemState.Ordered) });

        return (db, record, order);
    }

    // ───────────────────── 7. Múi giờ trên dây là HẰNG SỐ HỢP ĐỒNG ────────────────────────

    /// <summary>
    /// Mốc KHÔNG GẮN NHÃN (<c>Kind=Unspecified</c>) vẫn phải ra giờ Việt Nam trên dây.
    ///
    /// Cột của service này là <c>timestamp with time zone</c> nên EF trả về <c>Kind=Utc</c> —
    /// Unspecified KHÔNG phải hình dạng mốc đọc từ DB, và ca này không giả vờ là thế. Nó
    /// canh đường KHÁC: mốc đi vào từ ngoài DB (nạp Excel, DTO, một migration đổi kiểu cột
    /// sau này) mất nhãn Kind, và lúc đó "coi Unspecified là giờ máy" lệch đúng 7 tiếng.
    ///
    /// ⚠️ KHAI THẲNG GIỚI HẠN: ca này KHÔNG giết được đột biến
    /// <c>VendorClock.ToVietnam(x)</c> → <c>x.ToLocalTime()</c> mà nghiệm thu vòng 3 chỉ ra.
    /// Đã đo: đột biến đó SỐNG SÓT cả bộ 421 test. Lý do không phải test viết ẩu — .NET coi
    /// <c>Kind=Unspecified</c> là UTC trong <c>ToLocalTime()</c>, nên dưới
    /// <c>TZ=Asia/Ho_Chi_Minh</c> (múi giờ duy nhất bộ test được phép chạy,
    /// <see cref="TzGuard"/>) hai phép cho kết quả Y HỆT nhau với MỌI Kind. Không test nào
    /// trong một tiến trình một múi giờ phân biệt nổi chúng.
    ///
    /// Chốt chặn thật vì thế nằm ở kiểu <see cref="VendorClock.Stamp"/> — đột biến kia nay
    /// KHÔNG BIÊN DỊCH ĐƯỢC. Xem
    /// <see cref="Moc_bi_quen_thi_keu_len_chu_khong_lang_le_thanh_nam_0001"/>.
    ///
    /// Hỏng thật trông thế nào: phiếu chụp hiện sai 7 tiếng trên worklist, không có gì đỏ lên.
    /// </summary>
    [Fact]
    public void Moc_doc_len_tu_DB_khong_gan_nhan_van_duoc_cong_dung_bay_tieng()
    {
        var db = new InMemoryTestDb();
        using var _db = db;
        db.UseRis(Configured());

        var session = db.SeedSession();
        var record = db.SeedRecord(
            session.SessionID, ExamRecordState.InProgress,
            patientCode: "BN000123", identityNumber: "079201004321", genderId: 1);
        var order = db.SeedOrder(record, kind: "CDHA",
            items: new[] { (200001L, "CDHA_XQNT", ParaclinicalItemState.Ordered) });

        // Mốc mất nhãn Kind — xem chú thích của ca này về việc nó tới từ đâu.
        order.OrderedAt = new DateTime(2026, 8, 20, 2, 30, 0, DateTimeKind.Unspecified);
        var lines = db.ItemsOf(record.RecordID).ToList();
        lines[0].VendorLineNo = 7;

        var payload = RisOrderPayloadBuilder.Build(
            order, lines, record, Configured(), RisOrderStatuses.New,
            new DateTime(2026, 8, 20, 3, 0, 0, DateTimeKind.Unspecified), out var missing);

        Assert.Empty(missing);

        var json = JObject.Parse(RisClient.Serialize(payload));

        // 02:30 (UTC chưa gắn nhãn) ⇒ 09:30 giờ Việt Nam. ToLocalTime() sẽ trả "02:30:00".
        Assert.Equal("2026-08-20 09:30:00", json["orderDate"]!.Value<string>());
        Assert.Equal("2026-08-20 10:00:00", json["msgDate"]!.Value<string>());
    }

    /// <summary>
    /// Và ghim thẳng chính <see cref="VendorClock"/>: offset phải đúng +07:00, so với HẰNG SỐ
    /// chứ không so với giờ máy. Ca này đỏ kể cả khi ai đó chạy bộ test ở múi giờ khác — cố ý,
    /// vì đó chính là điều đang được chốt (múi giờ ở đây thuộc HỢP ĐỒNG DÂY, không thuộc môi
    /// trường chạy).
    /// </summary>
    [Theory]
    [InlineData(DateTimeKind.Utc)]
    [InlineData(DateTimeKind.Unspecified)]
    [InlineData(DateTimeKind.Local)]
    public void VendorClock_luon_cong_dung_bay_tieng_du_nhan_Kind_nao(DateTimeKind kind)
    {
        var moc = DateTime.SpecifyKind(new DateTime(2026, 8, 20, 2, 30, 0), kind);
        var utc = kind == DateTimeKind.Local ? moc.ToUniversalTime() : moc;

        var stamp = VendorClock.ToVietnam(moc);

        Assert.Equal(utc.AddHours(7), DateTime.SpecifyKind(stamp.Value, utc.Kind));

        // Kind trả về phải là Unspecified: chuỗi trên dây không mang offset, và gắn Local sẽ
        // mời một phép trừ offset MÁY nữa ở tầng dưới — lệch hai lần.
        Assert.Equal(DateTimeKind.Unspecified, stamp.Value.Kind);
    }

    /// <summary>
    /// 🔴 CHỐT CHẶN THẬT của lớp lỗi này nằm ở TRÌNH BIÊN DỊCH, không ở đây.
    ///
    /// Nghiệm thu vòng 3 đo được: thay <c>VendorClock.ToVietnam(x)</c> bằng
    /// <c>x.ToLocalTime()</c> ở chỗ dựng gói thì toàn bộ bộ test vẫn xanh. Đó KHÔNG phải lỗi
    /// của bộ test — dưới <c>TZ=Asia/Ho_Chi_Minh</c> (múi giờ duy nhất bộ test được phép chạy)
    /// hai phép cho kết quả y hệt nhau với MỌI <c>DateTimeKind</c>, kể cả Unspecified mà
    /// <c>ToLocalTime()</c> cũng coi là UTC. Không test nào trong một tiến trình một múi giờ
    /// phân biệt nổi chúng.
    ///
    /// Nên mốc trên dây nay là <see cref="VendorClock.Stamp"/> — một kiểu chỉ dựng được bằng
    /// <see cref="VendorClock.ToVietnam"/>, và phép dựng đó TỰ đổi múi giờ. Đột biến kia bây
    /// giờ KHÔNG BIÊN DỊCH ĐƯỢC.
    ///
    /// Ca này ghim hai tính chất còn kiểm được bằng test: mốc bị QUÊN phải KÊU chứ không lặng
    /// lẽ thành "0001-01-01", và mốc lên dây phải là CHUỖI đúng khuôn chứ không phải object.
    /// </summary>
    [Fact]
    public void Moc_bi_quen_thi_keu_len_chu_khong_lang_le_thanh_nam_0001()
    {
        var quen = default(VendorClock.Stamp);

        Assert.Throws<InvalidOperationException>(() => quen.ToString());
        Assert.Throws<InvalidOperationException>(() => quen.Value);

        Assert.Equal("2026-08-20 09:30:00",
            VendorClock.ToVietnam(new DateTime(2026, 8, 20, 2, 30, 0, DateTimeKind.Utc)).ToString());
    }

    // ───────────────── 8a. Giãn cách thử lại ghim ở CHỖ GỌI, không chỉ ở hàm ──────────────

    /// <summary>
    /// 🔴 ĐỘT BIẾN CANH: đổi <c>BackoffFor(attempt)</c> ở chỗ gọi trong
    /// <see cref="IntegrationOutboxWorker.Claim"/> thành <c>TimeSpan.FromSeconds(1)</c>.
    /// Bộ test cũ vẫn xanh cả bộ, vì chỉ có ca ghim BẢN THÂN HÀM <c>BackoffFor</c>.
    ///
    /// Ca này so bằng SỐ GIÂY THẬT (30 · 60 · 120 · 240 · 480), không gọi lại
    /// <c>BackoffFor</c> để tính kỳ vọng — gọi lại là ghim hàm với chính nó, và một đột biến
    /// ở chỗ gọi vẫn đi lọt.
    ///
    /// Vì sao đáng canh: thử lại dày đặc thì cả ngân sách 5 lượt cháy hết trong vài giây,
    /// trước khi một sự cố thoáng qua của RIS kịp qua — phiếu vào DeadLetter vô cớ.
    /// </summary>
    [Theory]
    [InlineData(0, 30)]
    [InlineData(1, 60)]
    [InlineData(2, 120)]
    [InlineData(3, 240)]
    [InlineData(4, 480)]
    public void Cho_goi_hen_gio_thu_lai_dung_so_giay_cua_duong_cong(short alreadySpent, int expectedSeconds)
    {
        var now = new DateTime(2026, 8, 26, 10, 0, 0, DateTimeKind.Utc);
        var row = new IntegrationOutbox
        {
            Operation = RisOrderStatuses.New,
            Vendor = ParaclinicalTargets.Ris,
            RetryCount = alreadySpent
        };

        Assert.True(IntegrationOutboxWorker.Claim(row, now));

        Assert.Equal(now.AddSeconds(expectedSeconds), row.NextAttemptAt);
        Assert.Equal((short)(alreadySpent + 1), row.RetryCount);
    }

    /// <summary>
    /// Lượt CUỐI hỏng thì hàng phải tới hạn NGAY, để lượt quét kế tiếp đóng dấu DeadLetter —
    /// chứ không bắt người trực chờ hết một nhịp backoff 8 phút mới thấy cảnh báo.
    /// </summary>
    [Fact]
    public void Luot_cuoi_hong_thi_toi_han_ngay_de_lan_quet_sau_dong_dau_DeadLetter()
    {
        var row = new IntegrationOutbox
        {
            Operation = RisOrderStatuses.New,
            Vendor = ParaclinicalTargets.Ris,
            RetryCount = IntegrationOutboxService.MaxRetry
        };

        IntegrationOutboxWorker.Stamp(row, success: false, "vendor vẫn 500", snippet: "");

        Assert.Equal(OutboxState.Failed, row.State);
        Assert.True(row.NextAttemptAt <= DateTime.UtcNow,
            "Lượt cuối hỏng mà còn hẹn giờ ra tương lai thì cảnh báo DeadLetter tới muộn cả nhịp backoff.");
        Assert.False(IntegrationOutboxWorker.Claim(row, DateTime.UtcNow));
        Assert.Equal(OutboxState.DeadLetter, row.State);
    }

    // ─────────────── 8b. Hai nhánh BỎ GÓI CANCELLED TRONG IM LẶNG ────────────────────────

    /// <summary>
    /// 🔴 Cầu RIS không phục vụ đơn vị này ⇒ gói CANCELLED KHÔNG được xếp, nhưng lượt huỷ của
    /// người dùng vẫn phải THÀNH CÔNG.
    ///
    /// Hai vế đều là chốt chặn: ném ở đây là để một lỗi cấu hình tích hợp chặn bác sĩ huỷ một
    /// chỉ định sai; im lặng xếp gói là gửi lệnh huỷ tới RIS của ĐƠN VỊ KHÁC.
    /// </summary>
    [Fact]
    public async Task Cau_RIS_khong_phuc_vu_don_vi_nay_thi_bo_goi_CANCELLED_nhung_huy_van_chay()
    {
        var (db, record, order) = Fixture();
        using var _db = db;

        await db.Outbox.EnqueueOrderAsync(order.OrderID);
        var row = db.Db.IntegrationOutboxes.AsTracking().Single();
        var sent = await db.SendOnceAsync(row);
        IntegrationOutboxWorker.Stamp(row, sent.Success, sent.Detail, sent.ResponseSnippet);
        await db.Uow.SaveChangesAsync();
        db.Db.ChangeTracker.Clear();

        // Đơn vị của phiếu là DEV, còn cầu RIS đang khai cho một đơn vị khác.
        db.UseRis(Configured(divisionId: "BV-KHAC"));

        await db.Orders.CancelAsync(order.OrderID, new ParaclinicalCancelRequest { Reason = "Nhầm chỉ định" });

        Assert.Equal(ParaclinicalItemState.Cancelled, db.ItemsOf(record.RecordID).Single().State);
        Assert.Empty(db.Db.IntegrationOutboxes.AsNoTracking()
            .Where(x => x.Operation == RisOrderStatuses.Cancelled).ToList());
    }

    /// <summary>
    /// 🔴 Hồ sơ bị sửa mất một trường bắt buộc SAU khi gửi ⇒ không dựng được gói CANCELLED.
    /// Vẫn KHÔNG ném (lượt huỷ phải chạy), nhưng cũng KHÔNG được lặng lẽ coi như đã báo huỷ.
    ///
    /// Đây là nhánh tệ nhất trong ba nhánh bỏ gói: vendor ĐANG GIỮ một chỉ định đã huỷ và
    /// không có gì trên màn hình nói ra điều đó — chỉ có một dòng LogError. Ca này ít nhất
    /// ghim rằng lượt huỷ không bị hỏng và không có gói rác nào được xếp.
    /// </summary>
    [Fact]
    public async Task Ho_so_mat_truong_bat_buoc_thi_bo_goi_CANCELLED_nhung_huy_van_chay()
    {
        var (db, record, order) = Fixture();
        using var _db = db;

        await db.Outbox.EnqueueOrderAsync(order.OrderID);
        var row = db.Db.IntegrationOutboxes.AsTracking().Single();
        var sent = await db.SendOnceAsync(row);
        IntegrationOutboxWorker.Stamp(row, sent.Success, sent.Detail, sent.ResponseSnippet);
        await db.Uow.SaveChangesAsync();
        db.Db.ChangeTracker.Clear();

        var stored = db.Db.ExamRecords.AsTracking().Include(x => x.Patient).Single(x => x.RecordID == record.RecordID);
        stored.Patient!.IdentityNumber = "";              // vendor khai [Required]
        await db.Uow.SaveChangesAsync();
        db.Db.ChangeTracker.Clear();

        await db.Orders.CancelAsync(order.OrderID, new ParaclinicalCancelRequest { Reason = "Nhầm chỉ định" });

        Assert.Equal(ParaclinicalItemState.Cancelled, db.ItemsOf(record.RecordID).Single().State);
        Assert.Empty(db.Db.IntegrationOutboxes.AsNoTracking()
            .Where(x => x.Operation == RisOrderStatuses.Cancelled).ToList());
    }

    // ─────────────── 8c. TENANT lấy TỪ ĐƯỜNG DẪN — ghim GIÁ TRỊ, không phủ định ───────────

    /// <summary>
    /// 🔴 Chốt GIÁ TRỊ mã đơn vị tách ra từ đường dẫn, không chỉ chốt "gói không bị chặn".
    ///
    /// Một phép tách sai vẫn đi qua đủ mọi chốt chặn rồi ghi vào ĐƠN VỊ KHÁC — nhiều DB tenant
    /// dùng chung mã, nên kết quả trông hoàn toàn hợp lý và sẽ không ai phát hiện. Ca phủ định
    /// ở <see cref="VendorCallbackEndpointTests"/> chỉ nói "không kẹt ở 4001"; nó xanh y hệt
    /// nếu phép tách trả về chuỗi sai.
    /// </summary>
    [Theory]
    [InlineData("/v1/integration/vendor/DEV/paraclinical-status", "DEV")]
    [InlineData("/v1/integration/vendor/84006/paraclinical-status", "84006")]
    // Mã đơn vị có dấu cách được mã hoá trên URL — không tách đúng thì tra nhầm tenant.
    [InlineData("/v1/integration/vendor/BV%20A/paraclinical-status", "BV A")]
    // Không có đoạn sau: vẫn phải ra mã đơn vị chứ không ra rỗng.
    [InlineData("/v1/integration/vendor/DEV", "DEV")]
    // Dấu / thừa ở đầu đoạn — biến thể đường dẫn mà chốt chặn xác thực đã được bắn thử.
    [InlineData("/v1/integration/vendor//DEV/paraclinical-status", "DEV")]
    // Không có đơn vị nào ⇒ rỗng ⇒ middleware trả 4001, KHÔNG được đoán.
    [InlineData("/v1/integration/vendor/", "")]
    public void Ma_don_vi_tach_ra_tu_duong_dan_dung_gia_tri(string path, string expected)
        => Assert.Equal(expected, VendorCallbackAuthMiddleware.DivisionFrom(path));

    // ─────────────── 5. Cầu một-URL phục vụ đúng MỘT đơn vị ──────────────────────────────

    /// <summary>
    /// 🔴 Đơn vị khác đơn vị đã khai thì KHÔNG gửi được, và KHÔNG có gì được xếp hàng.
    ///
    /// Service đa tenant theo request (<c>X-Division-Id</c>) nhưng cấu hình RIS là MỘT URL cho
    /// cả tiến trình. Không có chốt này thì gói của đơn vị A đi tới RIS của đơn vị B: phiếu
    /// chụp của một người bệnh hiện trên worklist của bệnh viện khác, mà lời gọi vẫn 200 nên
    /// không có gì đỏ lên.
    /// </summary>
    [Fact]
    public async Task Don_vi_khong_phai_don_vi_da_khai_thi_5021_va_khong_xep_hang()
    {
        var (db, _, order) = Fixture(options: Configured(divisionId: "BV-KHAC"));
        using var _db = db;

        var ex = await Assert.ThrowsAsync<HealthExamException>(
            () => db.Outbox.EnqueueOrderAsync(order.OrderID));

        Assert.Equal(ErrorCodes.VendorNotConfigured, ex.ErrorCode);
        Assert.Equal(503, ErrorCodes.ToHttpStatus(ex.ErrorCode));
        Assert.Empty(db.Db.IntegrationOutboxes.AsNoTracking().ToList());
    }

    /// <summary>Thiếu hẳn <c>RIS_DIVISION_ID</c> cũng là CHƯA CẤU HÌNH — không có nhánh "đoán
    /// là đơn vị đang gọi", vì đoán chính là cách gói đi nhầm đích.</summary>
    [Fact]
    public void Thieu_RIS_DIVISION_ID_thi_coi_nhu_chua_cau_hinh()
    {
        var options = new RisOptions { BaseUrl = "https://ris.test", Username = "u", Password = "p" };

        Assert.False(options.IsDispatchConfigured);
        Assert.False(options.ServesDivision("DEV"));
        Assert.True(Configured().ServesDivision(FakeHealthExamContext.DefaultDivisionId));
    }

    /// <summary>
    /// 🔴 DANH SÁCH BIẾN TRONG THÔNG BÁO phải khớp ĐÚNG danh sách biến thật sự bắt buộc.
    ///
    /// Đây là ca canh một lớp lỗi vừa vấp thật khi thêm <c>RIS_DIVISION_ID</c>: nó được thêm
    /// vào <see cref="RisOptions.IsDispatchConfigured"/> nhưng BA chỗ khác vẫn liệt kê tay
    /// đúng ba biến cũ, và chỉ một chỗ được sửa theo. Hậu quả không phải một dòng log xấu —
    /// người triển khai khai đủ URL/user/pass, đọc dòng log kể tên đúng ba biến họ VỪA KHAI
    /// XONG, rồi đi tìm nguyên nhân ở chỗ khác. Worker còn thoát ngay sau dòng đó, nên đấy là
    /// manh mối DUY NHẤT họ có.
    ///
    /// Cách chốt: bỏ TỪNG biến một khỏi một cấu hình đầy đủ; mỗi lần phải làm
    /// <c>IsDispatchConfigured</c> hoá false. Tức mọi tên trong thông báo đều THẬT SỰ bắt
    /// buộc, và mọi thứ bắt buộc đều CÓ TÊN trong thông báo.
    /// </summary>
    [Fact]
    public void Danh_sach_bien_trong_thong_bao_khop_dung_thu_thuc_su_bat_buoc()
    {
        var full = Configured();
        Assert.True(full.IsDispatchConfigured);

        var bo = new Dictionary<string, RisOptions>
        {
            [RisOptions.BaseUrlEnv] = new() { Username = full.Username, Password = full.Password, DivisionId = full.DivisionId },
            [RisOptions.UsernameEnv] = new() { BaseUrl = full.BaseUrl, Password = full.Password, DivisionId = full.DivisionId },
            [RisOptions.PasswordEnv] = new() { BaseUrl = full.BaseUrl, Username = full.Username, DivisionId = full.DivisionId },
            [RisOptions.DivisionEnv] = new() { BaseUrl = full.BaseUrl, Username = full.Username, Password = full.Password }
        };

        foreach (var (envName, thieu) in bo)
        {
            Assert.False(thieu.IsDispatchConfigured, $"Thiếu {envName} mà vẫn coi là đã cấu hình");
            Assert.Contains(envName, RisOptions.DispatchEnvNames);
        }

        // Và không có tên THỪA: mỗi tên trong thông báo phải là một trong bốn ca ở trên.
        foreach (var name in RisOptions.DispatchEnvNames.Split(", "))
            Assert.Contains(name, bo.Keys);
    }

    // ─────────────── 2. Thân gói KHÔNG được chảy ra log ──────────────────────────────────

    /// <summary>
    /// 🔴 Gói mang họ tên, CCCD, số thẻ BHYT, điện thoại, địa chỉ, chẩn đoán — và log chảy
    /// thẳng vào Elasticsearch, nơi quyền truy cập khác hẳn quyền đọc bệnh án.
    ///
    /// Bản đối soát ĐÃ CÓ SẴN ở cột <c>Payload</c>, nên log thân gói không thêm khả năng chẩn
    /// đoán, chỉ thêm một nơi đang giữ dữ liệu y tế. Ca này bắt được cả lượt XANH (chỗ dễ quên
    /// nhất, vì người ta chỉ nghĩ tới log lỗi).
    /// </summary>
    [Theory]
    [InlineData(HttpStatusCode.OK)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task Khong_dinh_danh_nguoi_benh_nao_lot_ra_log(HttpStatusCode status)
    {
        var log = new CapturingLogger();
        var options = Configured();
        var stub = new RisStubHandler(_ => new HttpResponseMessage(status)
        {
            Content = new StringContent("{\"ok\":true}", Encoding.UTF8, "application/json")
        });

        var client = new RisClient(new HttpClient(stub), options, log);

        var payload = RisClient.Serialize(new RisOrderRequest
        {
            OrderNumber = "CD0000000042",
            Status = RisOrderStatuses.New,
            OrderDate = VendorClock.ToVietnam(new DateTime(2026, 8, 20, 2, 30, 0, DateTimeKind.Utc)),
            MsgDate = VendorClock.ToVietnam(new DateTime(2026, 8, 20, 3, 0, 0, DateTimeKind.Utc)),
            Patient = new RisPatient
            {
                ID = "BN000123",
                Name = "Nguyễn Thị Bảo Châu",
                Sex = "F",
                IdentityCardID = "079201004321",
                Phone = "0909123456",
                Address = "12 Lý Thường Kiệt, Quận 10",
                HealthInsurance = new RisHealthInsurance { HealthInsuranceCardID = "DN4797912345678" }
            }
        });

        await client.SendAsync(payload, outboxId: 4242);

        var all = log.All();
        foreach (var secret in new[]
                 {
                     "Nguyễn Thị Bảo Châu", "079201004321", "0909123456",
                     "Lý Thường Kiệt", "DN4797912345678"
                 })
            Assert.DoesNotContain(secret, all);

        // …nhưng CON TRỎ tới bản đối soát thì phải có, kẻo gate G-P3b-1 mất chỗ neo.
        Assert.Contains("4242", all);
    }
}

/// <summary>Bộ ghi log gom mọi dòng thành một chuỗi — đủ cho câu hỏi "chuỗi này có lọt ra không".</summary>
internal sealed class CapturingLogger : Microsoft.Extensions.Logging.ILogger<RisClient>
{
    private readonly List<string> _lines = new();

    public IDisposable BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;

    public void Log<TState>(
        Microsoft.Extensions.Logging.LogLevel logLevel,
        Microsoft.Extensions.Logging.EventId eventId,
        TState state, Exception exception,
        Func<TState, Exception, string> formatter)
        => _lines.Add(formatter(state, exception));

    public string All() => string.Join("\n", _lines);
}
