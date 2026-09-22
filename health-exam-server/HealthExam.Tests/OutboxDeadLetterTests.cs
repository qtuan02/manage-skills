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
using HealthExam.API.Contracts;
using HealthExam.API.Middlewares;
using HealthExam.Application.Common;
using HealthExam.Server.Service;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HealthExam.Tests;

/// <summary>
/// P3b — ĐIỂM CHẶN 2 của nghiệm thu vòng 3: DeadLetter là ngõ cụt và nút [Gửi lại] nói dối.
///
/// Hình dạng của lỗi: RIS sập lâu hơn ngân sách thử (30+60+120+240 ≈ 7,5 phút) ⇒ gói vào
/// DeadLetter. RIS sống lại. Bác sĩ thấy phòng chụp chưa có phiếu nên bấm [Gửi lại]. Phép
/// dedup không lọc State nên hàng DeadLetter VẪN KHỚP ⇒ HTTP 200 kèm AlreadyQueued=true,
/// KHÔNG có gì được xếp hàng, phiếu KHÔNG BAO GIỜ tới RIS — và màn hình báo thành công.
///
/// Một dòng LogError không đóng được lỗ này: người vận hành không đọc log khi màn hình
/// báo 200 OK.
/// </summary>
public class OutboxDeadLetterTests
{
    private static RisOptions Configured() => new()
    {
        BaseUrl = "https://ris.test/hisris/ris/00000",
        Username = "hex",
        Password = "s3cret",
        DivisionId = FakeHealthExamContext.DefaultDivisionId
    };

    private static RisStubHandler Broken() => new(_ =>
        new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent("RIS đang bảo trì", Encoding.UTF8, "text/plain")
        });

    private static (InMemoryTestDb Db, ParaclinicalOrder Order) Fixture(RisStubHandler stub = null)
    {
        var db = new InMemoryTestDb();
        db.UseRis(Configured(), stub);

        var session = db.SeedSession();
        var record = db.SeedRecord(
            session.SessionID, ExamRecordState.InProgress,
            patientCode: "BN000123", identityNumber: "079201004321", genderId: 1);
        var order = db.SeedOrder(record, kind: "CDHA",
            items: new[] { (200001L, "CDHA_XQNT", ParaclinicalItemState.Ordered) });

        return (db, order);
    }

    /// <summary>Đốt sạch ngân sách thử của gói đang nằm trong hàng đợi ⇒ nó vào DeadLetter.</summary>
    private static async Task BurnBudgetAsync(InMemoryTestDb db)
    {
        var row = db.Db.IntegrationOutboxes.AsTracking().Single();

        for (var i = 1; i <= IntegrationOutboxService.MaxRetry; i++)
        {
            Assert.True(IntegrationOutboxWorker.Claim(row, DateTime.UtcNow));
            var sent = await db.SendOnceAsync(row);
            IntegrationOutboxWorker.Stamp(row, sent.Success, sent.Detail, sent.ResponseSnippet);
        }

        Assert.False(IntegrationOutboxWorker.Claim(row, DateTime.UtcNow));
        await db.Uow.SaveChangesAsync();
        db.Db.ChangeTracker.Clear();

        Assert.Equal(OutboxState.DeadLetter,
            db.Db.IntegrationOutboxes.AsNoTracking().Single().State);
    }

    // ─────────────────────────────── ĐIỂM CHẶN 2 ──────────────────────────────────────────

    /// <summary>
    /// 🔴 GATE: bấm [Gửi lại] sau khi gói đã DeadLetter phải XẾP MỘT GÓI MỚI, không được trả
    /// "đã xếp hàng rồi".
    ///
    /// Đây là ca đỏ với bản trước: <c>existing != null</c> khớp cả hàng State=3, nên nó trả
    /// AlreadyQueued=true cho một hàng mà worker KHÔNG BAO GIỜ nhặt lại nữa.
    /// </summary>
    [Fact]
    public async Task Gui_lai_sau_khi_DeadLetter_thi_xep_goi_MOI_chu_khong_bao_da_xep()
    {
        var (db, order) = Fixture(Broken());
        using var _db = db;

        await db.Outbox.EnqueueOrderAsync(order.OrderID);
        await BurnBudgetAsync(db);

        var again = await db.Outbox.EnqueueOrderAsync(order.OrderID);

        Assert.False(again.AlreadyQueued);

        var rows = db.Db.IntegrationOutboxes.AsNoTracking().OrderBy(x => x.OutboxID).ToList();
        Assert.Equal(2, rows.Count);

        // Hàng cũ được GIỮ LẠI làm bằng chứng đối soát (đã thử 5 lượt, hỏng thế nào), chỉ
        // nhường khoá chống trùng cho gói mới.
        var dead = rows[0];
        Assert.Equal(OutboxState.DeadLetter, dead.State);
        Assert.Contains("#dead", dead.DedupKey);
        Assert.Contains("RIS đang bảo trì", dead.ResponseSnippet);

        // Gói mới CHỜ GỬI thật, ngân sách thử mới nguyên.
        var fresh = rows[1];
        Assert.Equal(OutboxState.Pending, fresh.State);
        Assert.Equal(0, fresh.RetryCount);
        Assert.Equal(fresh.OutboxID, again.OutboxID);
        Assert.Equal(
            IntegrationOutboxService.DedupKeyFor(ParaclinicalTargets.Ris, RisOrderStatuses.New, order.OrderNo),
            fresh.DedupKey);
    }

    /// <summary>
    /// Gói nạp lại phải được DỰNG LẠI từ dữ liệu hôm nay, không hồi sinh gói đã đóng băng.
    ///
    /// Payload đóng băng lúc xếp hàng là đúng CHO MỘT LƯỢT xếp hàng — nó giữ cho vendor nhận
    /// đúng thứ người dùng đã bấm. Nhưng nạp lại là một lượt xếp hàng MỚI, và giữa hai lượt
    /// có thể đã có dòng bị huỷ: gửi lại gói cũ là báo cho phòng chụp một dịch vụ vừa bị bỏ.
    /// </summary>
    [Fact]
    public async Task Goi_nap_lai_dung_du_lieu_hom_nay_chu_khong_hoi_sinh_goi_da_dong_bang()
    {
        var db = new InMemoryTestDb();
        using var _db = db;
        db.UseRis(Configured(), Broken());

        var session = db.SeedSession();
        var record = db.SeedRecord(
            session.SessionID, ExamRecordState.InProgress,
            patientCode: "BN000123", identityNumber: "079201004321", genderId: 1);
        var order = db.SeedOrder(record, kind: "CDHA", items: new[]
        {
            (200001L, "CDHA_XQNT", ParaclinicalItemState.Ordered),
            (200002L, "CDHA_SANO", ParaclinicalItemState.Ordered)
        });

        await db.Outbox.EnqueueOrderAsync(order.OrderID);
        await BurnBudgetAsync(db);

        // Giữa hai lượt, một dòng bị huỷ.
        var dropped = db.Db.ParaclinicalOrderItems.AsTracking().Single(x => x.ServiceCode == "CDHA_SANO");
        dropped.State = ParaclinicalItemState.Cancelled;
        await db.Uow.SaveChangesAsync();
        db.Db.ChangeTracker.Clear();

        await db.Outbox.EnqueueOrderAsync(order.OrderID);

        var fresh = db.Db.IntegrationOutboxes.AsNoTracking().OrderBy(x => x.OutboxID).Last();
        Assert.DoesNotContain("CDHA_SANO", fresh.Payload);
        Assert.Contains("CDHA_XQNT", fresh.Payload);
    }

    /// <summary>
    /// 🔴 Nạp lại KHÔNG được mở lại đúng cái hại mà chốt thứ tự sinh ra để chặn.
    ///
    /// Chốt thứ tự trong câu truy vấn của worker chỉ nhìn hàng CŨ HƠN, mà gói nạp lại mang
    /// <c>CreatedAt = bây giờ</c> — nên nó KHÔNG đỡ được đường này. Nếu phiếu đã báo huỷ sang
    /// vendor rồi mà [Gửi lại] vẫn xếp được gói NEW, thì RIS nhận một chỉ định cho phiếu vừa
    /// được báo huỷ: y hệt hậu quả lâm sàng của ĐIỂM CHẶN 1, chỉ khác cửa vào.
    ///
    /// Một mã lỗi bác sĩ nhìn thấy rẻ hơn nhiều một worklist sai: chỉ định lại một phiếu mới
    /// là xong, còn phòng chụp gọi nhầm người bệnh thì không.
    /// </summary>
    [Fact]
    public async Task Da_bao_huy_sang_vendor_roi_thi_KHONG_nap_lai_duoc()
    {
        var (db, order) = Fixture(Broken());
        using var _db = db;

        await db.Outbox.EnqueueOrderAsync(order.OrderID);
        await BurnBudgetAsync(db);

        // Dựng một gói CANCELLED ĐÃ TỚI vendor cho chính phiếu này.
        db.Db.IntegrationOutboxes.Add(new IntegrationOutbox
        {
            DivisionID = order.DivisionID,
            OrderID = order.OrderID,
            Vendor = ParaclinicalTargets.Ris,
            Operation = RisOrderStatuses.Cancelled,
            DedupKey = IntegrationOutboxService.DedupKeyFor(
                ParaclinicalTargets.Ris, RisOrderStatuses.Cancelled, order.OrderNo, new[] { 1L }),
            Payload = "{}",
            State = OutboxState.Sent,
            SentAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        });
        await db.Uow.SaveChangesAsync();
        db.Db.ChangeTracker.Clear();

        var ex = await Assert.ThrowsAsync<HealthExamException>(
            () => db.Outbox.EnqueueOrderAsync(order.OrderID));

        Assert.Equal(ErrorCodes.InvalidState, ex.ErrorCode);

        // Và KHÔNG có gói NEW nào được xếp thêm.
        Assert.Equal(1, db.Db.IntegrationOutboxes.AsNoTracking()
            .Count(x => x.Operation == RisOrderStatuses.New));
    }

    /// <summary>
    /// Đối xứng: hàng CHƯA chết thì [Gửi lại] vẫn phải là no-op báo "đã xếp rồi", không đẻ
    /// thêm gói. Bấm hai lần là thao tác bình thường của người đang sốt ruột.
    ///
    /// Đi kèm ba ca trên như một CẶP: một mình chúng thì ai đó "sửa" bằng cách bỏ hẳn phép
    /// dedup vẫn xanh — mà như thế là mỗi lần bấm một gói gửi sang RIS.
    /// </summary>
    [Fact]
    public async Task Hang_chua_chet_thi_bam_lai_van_la_mot_goi()
    {
        var (db, order) = Fixture();
        using var _db = db;

        var first = await db.Outbox.EnqueueOrderAsync(order.OrderID);
        var second = await db.Outbox.EnqueueOrderAsync(order.OrderID);

        Assert.False(first.AlreadyQueued);
        Assert.True(second.AlreadyQueued);
        Assert.Equal(first.OutboxID, second.OutboxID);
        Assert.Single(db.Db.IntegrationOutboxes.AsNoTracking().ToList());
    }

    /// <summary>
    /// Khoá khép lại phải VỪA cột (<see cref="OutboxFieldLengths.DedupKey"/> = 120) ở CẢ HAI
    /// nhánh — nếu không thì lượt nạp lại chết ở tầng DB với lỗi độ dài, đúng lúc người dùng
    /// đang cố gỡ một phiếu kẹt.
    ///
    /// Ghim thẳng hàm vì nhánh cắt bớt chỉ chạy khi khoá gốc đã dài kịch trần: không ca
    /// nghiệp vụ nào đi qua nó, nên đi vòng qua nghiệp vụ là để nhánh đó không có test nào.
    /// </summary>
    [Theory]
    [InlineData("RIS:NEW:CD0000000042")]                       // khoá thường
    [InlineData("RIS:CANCELLED:CD0000000042:1,2,3,4,5,6,7,8")]  // khoá có tập dòng
    public void Khoa_khep_lai_luon_vua_cot_va_luon_duy_nhat(string original)
    {
        foreach (var key in new[] { original, new string('K', OutboxFieldLengths.DedupKey) })
        {
            var dead = new IntegrationOutbox { OutboxID = 987654321, DedupKey = key };

            IntegrationOutboxService.Retire(dead);

            Assert.EndsWith("#dead987654321", dead.DedupKey);
            Assert.True(dead.DedupKey.Length <= OutboxFieldLengths.DedupKey,
                $"Khoá khép lại dài {dead.DedupKey.Length} ký tự, tràn cột {OutboxFieldLengths.DedupKey}");
            Assert.NotEqual(key, dead.DedupKey);
        }
    }
}
