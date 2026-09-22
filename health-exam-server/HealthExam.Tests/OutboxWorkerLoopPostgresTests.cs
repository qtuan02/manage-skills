using System.Net;
using System.Text;
using HealthExam.Infrastructure.Persistence;
using HealthExam.Domain.Common;
using HealthExam.Domain.ExamSessions;
using HealthExam.Domain.ExamRecords;
using HealthExam.Domain.Imports;
using HealthExam.Domain.Paraclinical;
using HealthExam.Domain.Webhooks;
using HealthExam.Domain.Integrations;
using HealthExam.Domain.Catalogs;
using HealthExam.Application.Common;
using HealthExam.Application.Integrations;
using HealthExam.Application.Paraclinical;
using HealthExam.Infrastructure.BackgroundJobs;
using HealthExam.Infrastructure.Integrations.Ris;
using HealthExam.Infrastructure.Persistence.Repositories;
using HealthExam.API.Contracts;
using HealthExam.API.Middlewares;
using HealthExam.Server.Service;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HealthExam.Tests;

/// <summary>
/// P3b — VÒNG LẶP của worker chạy trọn vẹn, trên PostgreSQL thật.
///
/// 🔴 Vì sao bộ này phải có: vá cho mục 4 và 6 của nghiệm thu vòng 3 đã TÁCH một lượt gửi
/// thành ba chặng / hai transaction (ghi sổ lượt thử → gọi mạng → đóng dấu). Đó là thay đổi
/// rủi ro nhất của cả MR, và trước bộ này thì <c>RunOnceAsync</c> KHÔNG có ca nào chạy qua —
/// nó cần transaction thật và SQL thô, hai thứ provider in-memory không có. "Sửa xong một
/// điểm chặn rồi làm hỏng đường chính" là cách tệ nhất để đóng một vòng review.
///
/// Các ca ở đây gọi <see cref="IntegrationOutboxWorker.RunOnceAsync"/> chứ không gọi từng
/// mảnh: thứ đang chốt là ba chặng KHỚP NHAU, không phải từng chặng đúng riêng lẻ.
///
/// ⚠️ Không khai <see cref="PostgresFixture.Env"/> thì các ca này XANH RỖNG — đọc
/// <see cref="PostgresFixture"/> trước khi trích một con số.
/// </summary>
[Collection(PostgresCollection.Name)]
public class OutboxWorkerLoopPostgresTests
{
    private const string Division = "PGW";

    private readonly PostgresFixture _pg;

    public OutboxWorkerLoopPostgresTests(PostgresFixture pg) => _pg = pg;

    private static RisOptions Options() => new()
    {
        BaseUrl = "https://ris.test/hisris/ris/00000",
        Username = "hex",
        Password = "s3cret",
        DivisionId = Division
    };

    /// <summary>Dựng worker trên đúng ống DI thật — <c>AddScoped&lt;IUnitOfWork&gt;</c> +
    /// <c>IntegrationOutboxService</c>, cùng hình với Program.cs.</summary>
    private IntegrationOutboxWorker Worker(RisStubHandler stub)
    {
        var services = new ServiceCollection();

        services.AddDbContext<HealthExamDbContext>(o => o
            .UseNpgsql(_pg.ConnectionString)
            .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking));

        services.AddScoped<IUnitOfWork, HealthExam.Infrastructure.Persistence.UnitOfWork>();
        services.AddScoped<IIntegrationOutboxRepository, IntegrationOutboxRepository>();
        services.AddScoped<IParaclinicalRepository, ParaclinicalRepository>();
        services.AddScoped<IRisPayloadBuilder, RisPayloadBuilder>();
        services.AddScoped<IRisClient>(_ => new RisClient(
            new HttpClient(stub), Options(), NullLogger<RisClient>.Instance));
        services.AddScoped<IAuditRepository, HealthExam.Infrastructure.Persistence.AuditRepository>();
        services.AddScoped<IClock, SystemClock>();
        services.AddScoped<IProcessOutboxBatchHandler, ProcessOutboxBatchHandler>();
        services.AddLogging();

        var provider = services.BuildServiceProvider();

        return new IntegrationOutboxWorker(
            provider.GetRequiredService<IServiceScopeFactory>(),
            Options(),
            NullLogger<IntegrationOutboxWorker>.Instance);
    }

    private async Task<IntegrationOutbox> SeedAsync(string operation = RisOrderStatuses.New)
    {
        await using var db = _pg.NewContext();
        await db.Database.ExecuteSqlRawAsync("TRUNCATE TABLE \"HEX_IntegrationOutbox\"");

        var row = new IntegrationOutbox
        {
            DivisionID = Division,
            OrderID = Guid.NewGuid(),
            Vendor = ParaclinicalTargets.Ris,
            Operation = operation,
            DedupKey = $"{ParaclinicalTargets.Ris}:{operation}:{Guid.NewGuid():N}",
            Payload = "{}",
            State = OutboxState.Pending,
            CreatedAt = DateTime.UtcNow
        };

        db.IntegrationOutboxes.Add(row);
        await db.SaveChangesAsync();
        return row;
    }

    private async Task<IntegrationOutbox> ReadAsync(long outboxId)
    {
        await using var db = _pg.NewContext();
        return await db.IntegrationOutboxes.SingleAsync(x => x.OutboxID == outboxId);
    }

    // ─────────────────────────────── Đường chính ──────────────────────────────────────────

    /// <summary>
    /// Vendor nhận ⇒ hàng đóng dấu <c>Sent</c>, có <c>SentAt</c>, hết hẹn giờ. Và lượt thử ĐÃ
    /// TIÊU (<c>RetryCount = 1</c>): ngân sách tiêu lúc BẮT ĐẦU lượt, không lúc biết kết quả.
    /// </summary>
    [Fact]
    public async Task Gui_thanh_cong_thi_dong_dau_Sent_va_da_tieu_dung_mot_luot()
    {
        if (!_pg.Enabled) return;

        var row = await SeedAsync();
        var stub = new RisStubHandler();

        var processed = await Worker(stub).RunOnceAsync(CancellationToken.None);

        Assert.Equal(1, processed);
        Assert.Single(stub.Received);

        var saved = await ReadAsync(row.OutboxID);
        Assert.Equal(OutboxState.Sent, saved.State);
        Assert.NotNull(saved.SentAt);
        Assert.Null(saved.NextAttemptAt);
        Assert.Equal((short)1, saved.RetryCount);
        Assert.Equal("", saved.LastError);
    }

    /// <summary>
    /// Vendor trả 500 ⇒ KHÔNG Sent, có <c>LastError</c>, và hẹn giờ thử lại đúng 30 giây của
    /// lượt đầu. Đóng dấu Sent cho một gói bên kia không nhận là để phiếu trông như đã tới nơi.
    /// </summary>
    [Fact]
    public async Task Vendor_tra_loi_thi_hen_gio_thu_lai_chu_khong_dong_dau_Sent()
    {
        if (!_pg.Enabled) return;

        var row = await SeedAsync();
        var stub = new RisStubHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent("boom", Encoding.UTF8, "text/plain")
        });

        var before = DateTime.UtcNow;
        await Worker(stub).RunOnceAsync(CancellationToken.None);

        var saved = await ReadAsync(row.OutboxID);
        Assert.Equal(OutboxState.Failed, saved.State);
        Assert.Null(saved.SentAt);
        Assert.Equal((short)1, saved.RetryCount);
        Assert.Contains("500", saved.LastError);
        Assert.Contains("boom", saved.ResponseSnippet);

        Assert.NotNull(saved.NextAttemptAt);
        Assert.InRange(saved.NextAttemptAt!.Value, before.AddSeconds(29), before.AddSeconds(45));
    }

    /// <summary>
    /// 🔴 NGÂN SÁCH THỬ CÓ ĐÁY, đo qua ĐÚNG vòng lặp thật.
    ///
    /// RIS hỏng liên tục: sau <see cref="IntegrationOutboxService.MaxRetry"/> lượt, hàng phải
    /// vào DeadLetter và worker THÔI gọi mạng. Ca này là chốt cho "bắn lặp một phiếu sang RIS
    /// không giới hạn" — thứ mà mục 4 của review nêu.
    ///
    /// Đếm số lời gọi thật tới stub, không đếm số vòng: đó mới là con số vendor nhìn thấy.
    /// </summary>
    [Fact]
    public async Task RIS_hong_lien_tuc_thi_dung_han_sau_dung_ngan_sach_luot()
    {
        if (!_pg.Enabled) return;

        var row = await SeedAsync();
        var stub = new RisStubHandler(_ => new HttpResponseMessage(HttpStatusCode.BadGateway));
        var worker = Worker(stub);

        // Mỗi vòng: đẩy hàng tới hạn rồi cho worker quét. Vòng cuối là vòng đóng dấu DeadLetter.
        for (var i = 0; i <= IntegrationOutboxService.MaxRetry; i++)
        {
            await using (var db = _pg.NewContext())
                await db.Database.ExecuteSqlRawAsync(
                    "UPDATE \"HEX_IntegrationOutbox\" SET \"NextAttemptAt\" = now() WHERE \"State\" = 2");

            await worker.RunOnceAsync(CancellationToken.None);
        }

        var saved = await ReadAsync(row.OutboxID);
        Assert.Equal(OutboxState.DeadLetter, saved.State);
        Assert.Equal((short)IntegrationOutboxService.MaxRetry, saved.RetryCount);
        Assert.Null(saved.NextAttemptAt);

        Assert.Equal(IntegrationOutboxService.MaxRetry, stub.Received.Count);

        // Và quét thêm nữa KHÔNG gọi vendor lần nào: DeadLetter là ngõ cụt có chủ ý, gỡ bằng
        // nút [Gửi lại] (xem OutboxDeadLetterTests), không phải bằng vòng lặp.
        await worker.RunOnceAsync(CancellationToken.None);
        Assert.Equal(IntegrationOutboxService.MaxRetry, stub.Received.Count);
    }

    /// <summary>
    /// Hàng đang backoff thì worker KHÔNG gọi mạng — chưa tới hạn là chưa tới hạn.
    /// Thiếu vế này thì "giãn cách nhân đôi" chỉ là một con số ghi trong DB.
    /// </summary>
    [Fact]
    public async Task Hang_chua_toi_han_thi_khong_goi_vendor()
    {
        if (!_pg.Enabled) return;

        var row = await SeedAsync();
        await using (var db = _pg.NewContext())
            await db.Database.ExecuteSqlRawAsync(
                "UPDATE \"HEX_IntegrationOutbox\" SET \"State\" = 2, \"RetryCount\" = 1, "
                + "\"NextAttemptAt\" = now() + interval '10 minutes'");

        var stub = new RisStubHandler();
        var processed = await Worker(stub).RunOnceAsync(CancellationToken.None);

        Assert.Equal(0, processed);
        Assert.Empty(stub.Received);
        Assert.Equal((short)1, (await ReadAsync(row.OutboxID)).RetryCount);
    }

    /// <summary>Gói của đơn vị KHÁC thì worker không đụng tới — cùng chốt với ca truy vấn,
    /// nhưng đo ở tầng vòng lặp, nơi lời gọi mạng thật sự xảy ra.</summary>
    [Fact]
    public async Task Khong_goi_vendor_cho_goi_cua_don_vi_khac()
    {
        if (!_pg.Enabled) return;

        await SeedAsync();
        await using (var db = _pg.NewContext())
            await db.Database.ExecuteSqlRawAsync("UPDATE \"HEX_IntegrationOutbox\" SET \"DivisionID\" = 'PGZ'");

        var stub = new RisStubHandler();

        Assert.Equal(0, await Worker(stub).RunOnceAsync(CancellationToken.None));
        Assert.Empty(stub.Received);
    }
}
