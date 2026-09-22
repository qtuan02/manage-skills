using HealthExam.Infrastructure.Persistence;
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
using Npgsql;
using Xunit;

namespace HealthExam.Tests;

/// <summary>
/// P3b — chốt chặn của HÀNG ĐỢI, kiểm trên PostgreSQL THẬT.
///
/// Mọi ca ở đây đi qua <see cref="IntegrationOutboxWorker.NextRowAsync"/>, tức qua chính câu
/// SQL thô mà provider in-memory không chạy nổi. Đây là chỗ nghiệm thu vòng 3 nói thẳng là
/// "388 xanh KHÔNG nói gì" — và điểm chặn 1 nằm trọn trong câu đó.
///
/// ⚠️ Không khai <see cref="PostgresFixture.Env"/> thì 13 ca ở đây XANH RỖNG — thoát ở dòng
/// đầu, và tổng số test không đổi. Đọc <see cref="PostgresFixture"/> trước khi trích một con
/// số từ bộ test này.
/// </summary>
[Collection(PostgresCollection.Name)]
public class OutboxQueuePostgresTests
{
    private const string DivisionA = "PGA";
    private const string DivisionB = "PGB";

    private readonly PostgresFixture _pg;

    public OutboxQueuePostgresTests(PostgresFixture pg) => _pg = pg;

    // ─────────────────────────────── đồ nghề ──────────────────────────────────────────────

    /// <summary>Một hàng outbox thô. Mọi ca dựng hàng thẳng vào bảng: thứ đang kiểm là CÂU
    /// TRUY VẤN, nên đi vòng qua tầng nghiệp vụ chỉ thêm biến số.</summary>
    private static IntegrationOutbox Row(
        Guid orderId, string operation, OutboxState state, DateTime createdAt,
        DateTime? nextAttemptAt = null, short retryCount = 0, string division = DivisionA,
        string dedupKey = null)
        => new()
        {
            DivisionID = division,
            OrderID = orderId,
            Vendor = ParaclinicalTargets.Ris,
            Operation = operation,
            DedupKey = dedupKey ?? $"{ParaclinicalTargets.Ris}:{operation}:{Guid.NewGuid():N}",
            Payload = "{}",
            State = state,
            RetryCount = retryCount,
            NextAttemptAt = nextAttemptAt,
            CreatedAt = createdAt
        };

    private async Task<Guid> SeedAsync(params IntegrationOutbox[] rows)
    {
        await using var db = _pg.NewContext();
        db.IntegrationOutboxes.AddRange(rows);
        await db.SaveChangesAsync();
        return rows.Length > 0 ? rows[0].OrderID : Guid.NewGuid();
    }

    /// <summary>Dọn sạch bảng — các ca chia nhau một database.</summary>
    private async Task ResetAsync()
    {
        await using var db = _pg.NewContext();
        await db.Database.ExecuteSqlRawAsync("TRUNCATE TABLE \"HEX_IntegrationOutbox\"");
    }

    private async Task<IntegrationOutbox> NextAsync(string division = DivisionA)
    {
        var uow = _pg.NewUow(out var db);
        await using (db)
            return await IntegrationOutboxWorker.NextRowAsync(uow, division);
    }

    // ───────────────────────────── ĐIỂM CHẶN 1 ────────────────────────────────────────────

    /// <summary>
    /// 🔴🔴 GATE của điểm chặn 1 — gói CANCELLED KHÔNG được vượt mặt gói NEW đang backoff.
    ///
    /// Dựng lại đúng kịch bản MỘT POD, không cần đua:
    ///   T0      bác sĩ bấm Gửi          → A = NEW, State=0
    ///   T1      RIS chớp lỗi            → A: State=2, NextAttemptAt = T1+30s (CHƯA tới hạn)
    ///   T1+10s  bác sĩ huỷ chỉ định     → B = CANCELLED, State=0 (TỚI HẠN NGAY)
    ///
    /// Với câu cũ (<c>ORDER BY "CreatedAt"</c> mà không có vế NOT EXISTS), worker nhặt B —
    /// vì A không tới hạn nên nó KHÔNG CÓ MẶT để mà được sắp trước. RIS nhận lệnh huỷ cho một
    /// phiếu chưa từng biết (no-op), rồi 30s sau nhận NEW và GIỮ MỘT CHỈ ĐỊNH ĐÃ HUỶ. Cả hai
    /// gói đều Sent thành công, không một cảnh báo nào; phòng chụp gọi người bệnh vào chụp
    /// một phim bác sĩ vừa huỷ.
    ///
    /// Nay: worker KHÔNG nhặt gì cả cho tới khi A xong.
    /// </summary>
    [Fact]
    public async Task Goi_CANCELLED_khong_vuot_mat_goi_NEW_dang_backoff()
    {
        if (!_pg.Enabled) return;
        await ResetAsync();

        var order = Guid.NewGuid();
        var t0 = DateTime.UtcNow.AddMinutes(-5);

        var neu = Row(order, RisOrderStatuses.New, OutboxState.Failed, t0,
            nextAttemptAt: DateTime.UtcNow.AddSeconds(30), retryCount: 1);
        var huy = Row(order, RisOrderStatuses.Cancelled, OutboxState.Pending, t0.AddSeconds(10));

        await SeedAsync(neu, huy);

        var picked = await NextAsync();

        Assert.True(picked == null,
            $"Worker nhặt gói {picked?.Operation} trong khi gói NEW của cùng phiếu còn đang backoff — "
            + "đó là đường RIS giữ một chỉ định đã huỷ.");
    }

    /// <summary>Và gói NEW xong thì gói CANCELLED được đi ngay — chốt thứ tự KHÔNG được biến
    /// thành một cái phanh dính.</summary>
    [Fact]
    public async Task Goi_NEW_gui_xong_thi_goi_CANCELLED_di_ngay()
    {
        if (!_pg.Enabled) return;
        await ResetAsync();

        var order = Guid.NewGuid();
        var t0 = DateTime.UtcNow.AddMinutes(-5);

        await SeedAsync(
            Row(order, RisOrderStatuses.New, OutboxState.Sent, t0),
            Row(order, RisOrderStatuses.Cancelled, OutboxState.Pending, t0.AddSeconds(10)));

        var picked = await NextAsync();

        Assert.NotNull(picked);
        Assert.Equal(RisOrderStatuses.Cancelled, picked.Operation);
    }

    /// <summary>
    /// 🔴 Gói NEW đã DeadLetter thì KHÔNG được chặn gói CANCELLED — nếu không, chốt thứ tự vừa
    /// dựng thêm một NGÕ CỤT VĨNH VIỄN thứ hai: hàng sau nằm Pending mãi mãi, không log,
    /// không cảnh báo, đúng lớp lỗi của chính điểm chặn 2.
    ///
    /// Đây là lý do vế lọc là <c>p."State" IN (0,2)</c> chứ KHÔNG phải <c>p."State" &lt;&gt; 1</c>
    /// như bản đề nghị. NEW đã chết nghĩa là RIS chưa từng thấy phiếu, nên CANCELLED đi sau
    /// chỉ là một no-op vô hại bên họ.
    /// </summary>
    [Fact]
    public async Task Goi_NEW_da_DeadLetter_thi_khong_chan_goi_CANCELLED_vinh_vien()
    {
        if (!_pg.Enabled) return;
        await ResetAsync();

        var order = Guid.NewGuid();
        var t0 = DateTime.UtcNow.AddMinutes(-30);

        await SeedAsync(
            Row(order, RisOrderStatuses.New, OutboxState.DeadLetter, t0,
                retryCount: IntegrationOutboxService.MaxRetry),
            Row(order, RisOrderStatuses.Cancelled, OutboxState.Pending, t0.AddMinutes(1)));

        var picked = await NextAsync();

        Assert.NotNull(picked);
        Assert.Equal(RisOrderStatuses.Cancelled, picked.Operation);
    }

    /// <summary>
    /// Hai gói cùng phiếu TRÙNG <c>CreatedAt</c> tới từng micro giây (xếp trong cùng một
    /// transaction) thì vẫn phải có đúng một cái đi trước.
    ///
    /// Đây là lý do vế chặn so bằng BỘ ĐÔI <c>("CreatedAt","OutboxID")</c> — khớp đúng
    /// <c>ORDER BY</c>. Chỉ so <c>CreatedAt</c> thì không vế nào chặn vế nào và cả hai cùng
    /// tới hạn: thứ tự trở lại thành may rủi.
    /// </summary>
    [Fact]
    public async Task Hai_goi_trung_moc_tao_van_di_dung_mot_cai_truoc()
    {
        if (!_pg.Enabled) return;
        await ResetAsync();

        var order = Guid.NewGuid();
        var t0 = DateTime.UtcNow.AddMinutes(-5);

        var neu = Row(order, RisOrderStatuses.New, OutboxState.Pending, t0);
        var huy = Row(order, RisOrderStatuses.Cancelled, OutboxState.Pending, t0);
        await SeedAsync(neu, huy);

        var picked = await NextAsync();

        Assert.NotNull(picked);
        Assert.Equal(neu.OutboxID, picked.OutboxID);
    }

    /// <summary>Phiếu KHÁC không chặn nhau — chốt thứ tự chỉ trong phạm vi MỘT phiếu, chứ
    /// không biến hàng đợi thành một hàng nối đuôi toàn cục.</summary>
    [Fact]
    public async Task Goi_cua_phieu_khac_khong_chan_nhau()
    {
        if (!_pg.Enabled) return;
        await ResetAsync();

        var t0 = DateTime.UtcNow.AddMinutes(-5);

        await SeedAsync(
            Row(Guid.NewGuid(), RisOrderStatuses.New, OutboxState.Failed, t0,
                nextAttemptAt: DateTime.UtcNow.AddSeconds(30), retryCount: 1),
            Row(Guid.NewGuid(), RisOrderStatuses.New, OutboxState.Pending, t0.AddSeconds(10)));

        Assert.NotNull(await NextAsync());
    }

    // ───────────────────────────── SKIP LOCKED, nhiều pod ─────────────────────────────────

    /// <summary>
    /// 🔴 Hai "pod" quét cùng lúc phải nhận HAI hàng KHÁC NHAU — không pod nào chờ pod kia, và
    /// tuyệt đối không có chuyện hai pod cùng gửi một phiếu sang vendor.
    ///
    /// Nghiệm thu vòng 3 khai thẳng: không có bằng chứng nhiều pod, mọi phát hiện về đua đều
    /// SUY TỪ MÃ. Ca này là bằng chứng — hai transaction thật, chạy chồng lên nhau.
    /// </summary>
    [Fact]
    public async Task Hai_pod_quet_cung_luc_nhan_hai_hang_khac_nhau()
    {
        if (!_pg.Enabled) return;
        await ResetAsync();

        var t0 = DateTime.UtcNow.AddMinutes(-5);
        await SeedAsync(
            Row(Guid.NewGuid(), RisOrderStatuses.New, OutboxState.Pending, t0),
            Row(Guid.NewGuid(), RisOrderStatuses.New, OutboxState.Pending, t0.AddSeconds(1)));

        var uowA = _pg.NewUow(out var dbA);
        var uowB = _pg.NewUow(out var dbB);

        await using (dbA)
        await using (dbB)
        {
            await using var txA = await uowA.BeginTransactionAsync();
            var a = await IntegrationOutboxWorker.NextRowAsync(uowA, DivisionA);

            // Pod B quét TRONG KHI pod A còn giữ khoá.
            await using var txB = await uowB.BeginTransactionAsync();
            var b = await IntegrationOutboxWorker.NextRowAsync(uowB, DivisionA);

            Assert.NotNull(a);
            Assert.NotNull(b);
            Assert.NotEqual(a.OutboxID, b.OutboxID);

            await txA.CommitAsync();
            await txB.CommitAsync();
        }
    }

    /// <summary>Chỉ còn MỘT hàng thì pod thứ hai KHÔNG chờ — nó bỏ qua và đi làm việc khác.
    /// <c>SKIP LOCKED</c> mà thiếu thì pod thứ hai treo tới khi pod đầu commit, tức lời gọi
    /// RIS 30 giây của pod đầu trở thành 30 giây chết của cả hàng đợi.</summary>
    [Fact]
    public async Task Chi_con_mot_hang_thi_pod_thu_hai_bo_qua_chu_khong_cho()
    {
        if (!_pg.Enabled) return;
        await ResetAsync();

        await SeedAsync(Row(Guid.NewGuid(), RisOrderStatuses.New, OutboxState.Pending, DateTime.UtcNow));

        var uowA = _pg.NewUow(out var dbA);
        var uowB = _pg.NewUow(out var dbB);

        await using (dbA)
        await using (dbB)
        {
            await using var txA = await uowA.BeginTransactionAsync();
            Assert.NotNull(await IntegrationOutboxWorker.NextRowAsync(uowA, DivisionA));

            await using var txB = await uowB.BeginTransactionAsync();
            var b = await IntegrationOutboxWorker.NextRowAsync(uowB, DivisionA)
                .WaitAsync(TimeSpan.FromSeconds(10));

            Assert.Null(b);

            await txA.CommitAsync();
            await txB.CommitAsync();
        }
    }

    // ───────────────────────────── Lọc tenant ─────────────────────────────────────────────

    /// <summary>
    /// 🔴 Cầu RIS của đơn vị A KHÔNG được nhặt gói của đơn vị B.
    ///
    /// Service đa tenant theo request nhưng cấu hình RIS là MỘT URL cho cả tiến trình: thiếu
    /// vế lọc này thì gói của đơn vị B đi tới RIS của đơn vị A — phiếu chụp của một người bệnh
    /// hiện trên worklist của bệnh viện khác, mà lời gọi vẫn 200 nên không có gì đỏ lên.
    /// </summary>
    [Fact]
    public async Task Khong_nhat_goi_cua_don_vi_khac()
    {
        if (!_pg.Enabled) return;
        await ResetAsync();

        await SeedAsync(
            Row(Guid.NewGuid(), RisOrderStatuses.New, OutboxState.Pending, DateTime.UtcNow, division: DivisionB));

        Assert.Null(await NextAsync(DivisionA));
        Assert.NotNull(await NextAsync(DivisionB));
    }

    // ───────────────────────────── Ràng buộc ở TẦNG DB ────────────────────────────────────

    /// <summary>
    /// 🔴 <c>UX_HEX_Outbox_Dedup</c> là UNIQUE THẬT ở tầng DB, và nó nổ 23505.
    ///
    /// Test in-memory <c>Bam_gui_nhieu_lan_chi_ra_dung_mot_goi</c> XANH GIẢ ở vế này: provider
    /// in-memory không có ràng buộc duy nhất, nên nó chỉ chốt nhánh kiểm ở tầng service —
    /// đúng nhánh mà hai request song song đều lọt qua.
    /// </summary>
    [Fact]
    public async Task Trung_khoa_chong_trung_thi_DB_no_23505()
    {
        if (!_pg.Enabled) return;
        await ResetAsync();

        var key = $"{ParaclinicalTargets.Ris}:{RisOrderStatuses.New}:CD0000000042";
        await SeedAsync(Row(Guid.NewGuid(), RisOrderStatuses.New, OutboxState.Pending, DateTime.UtcNow, dedupKey: key));

        var ex = await Assert.ThrowsAsync<DbUpdateException>(() =>
            SeedAsync(Row(Guid.NewGuid(), RisOrderStatuses.New, OutboxState.Pending, DateTime.UtcNow, dedupKey: key)));

        Assert.Equal("23505", Assert.IsType<PostgresException>(ex.InnerException).SqlState);
    }

    /// <summary>Cùng khoá nhưng KHÁC đơn vị thì KHÔNG trùng — ràng buộc là
    /// <c>(DivisionID, DedupKey)</c>, và bỏ vế đầu là hai bệnh viện chặn gói của nhau.</summary>
    [Fact]
    public async Task Cung_khoa_khac_don_vi_thi_khong_trung()
    {
        if (!_pg.Enabled) return;
        await ResetAsync();

        var key = $"{ParaclinicalTargets.Ris}:{RisOrderStatuses.New}:CD0000000042";

        await SeedAsync(Row(Guid.NewGuid(), RisOrderStatuses.New, OutboxState.Pending, DateTime.UtcNow,
            division: DivisionA, dedupKey: key));
        await SeedAsync(Row(Guid.NewGuid(), RisOrderStatuses.New, OutboxState.Pending, DateTime.UtcNow,
            division: DivisionB, dedupKey: key));

        await using var db = _pg.NewContext();
        Assert.Equal(2, await db.IntegrationOutboxes.CountAsync(x => x.DedupKey == key));
    }

    /// <summary>
    /// Khoá khép lại của hàng DeadLetter phải NHƯỜNG được khoá cho gói mới — trên DB thật, nơi
    /// UNIQUE có hiệu lực. Đây là vế mà điểm chặn 2 dựa vào và in-memory không kiểm nổi.
    /// </summary>
    [Fact]
    public async Task Khoa_khep_lai_nhuong_duoc_cho_goi_moi_tren_DB_that()
    {
        if (!_pg.Enabled) return;
        await ResetAsync();

        var order = Guid.NewGuid();
        var key = $"{ParaclinicalTargets.Ris}:{RisOrderStatuses.New}:CD0000000077";

        var dead = Row(order, RisOrderStatuses.New, OutboxState.DeadLetter, DateTime.UtcNow.AddHours(-1),
            retryCount: IntegrationOutboxService.MaxRetry, dedupKey: key);
        await SeedAsync(dead);

        await using (var db = _pg.NewContext())
        {
            var stored = await db.IntegrationOutboxes.AsTracking().SingleAsync(x => x.OutboxID == dead.OutboxID);
            IntegrationOutboxService.Retire(stored);
            db.IntegrationOutboxes.Add(
                Row(order, RisOrderStatuses.New, OutboxState.Pending, DateTime.UtcNow, dedupKey: key));

            // Cùng MỘT SaveChanges: khép hàng cũ và xếp hàng mới phải cùng sống hoặc cùng chết.
            await db.SaveChangesAsync();
        }

        await using var check = _pg.NewContext();
        Assert.Equal(1, await check.IntegrationOutboxes.CountAsync(x => x.DedupKey == key));
        Assert.Equal(1, await check.IntegrationOutboxes.CountAsync(x => x.DedupKey.EndsWith("#dead" + dead.OutboxID)));

        // Và gói mới ĐI ĐƯỢC: hàng DeadLetter không chặn nó (xem ca ngõ-cụt-thứ-hai ở trên).
        var picked = await NextAsync();
        Assert.NotNull(picked);
        Assert.Equal(OutboxState.Pending, picked.State);
    }

    // ───────────────────────────── Câu SQL chạy được thật ─────────────────────────────────

    /// <summary>
    /// Hàng đợi rỗng ⇒ trả null, KHÔNG ném. Ca rẻ nhất nhưng bắt được lớp lỗi đắt nhất của
    /// SQL thô: sai tên bảng/cột thì tick đầu tiên của worker nổ <c>42703</c>/<c>42P01</c> lúc
    /// chạy thật, và không test nào ở tầng service chạm tới câu đó để mà đỏ.
    /// </summary>
    [Fact]
    public async Task Hang_doi_rong_thi_tra_null_chu_khong_nem()
    {
        if (!_pg.Enabled) return;
        await ResetAsync();

        Assert.Null(await NextAsync());
    }

    /// <summary>Câu khoá lại một hàng đã biết số hiệu (chặng 3 của worker) cũng phải chạy được
    /// trên DB thật — nó là SQL thô thứ hai, và cũng không có đường nào khác chạm tới.</summary>
    [Fact]
    public async Task Khoa_lai_dung_mot_hang_da_biet_so_hieu()
    {
        if (!_pg.Enabled) return;
        await ResetAsync();

        var row = Row(Guid.NewGuid(), RisOrderStatuses.New, OutboxState.Failed, DateTime.UtcNow, retryCount: 2);
        await SeedAsync(row);

        var uow = _pg.NewUow(out var db);
        await using (db)
        {
            await using var tx = await uow.BeginTransactionAsync();
            var locked = await IntegrationOutboxWorker.LockRowAsync(uow, row.OutboxID);

            Assert.NotNull(locked);
            Assert.Equal(row.OutboxID, locked.OutboxID);
            Assert.Equal((short)2, locked.RetryCount);

            Assert.Null(await IntegrationOutboxWorker.LockRowAsync(uow, row.OutboxID + 99999));
            await tx.CommitAsync();
        }
    }
}
