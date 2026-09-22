using HealthExam.Infrastructure.Persistence;
using HealthExam.Domain.Common;
using HealthExam.Domain.ExamSessions;
using HealthExam.Domain.ExamRecords;
using HealthExam.Domain.Imports;
using HealthExam.Domain.Paraclinical;
using HealthExam.Domain.Webhooks;
using HealthExam.Domain.Integrations;
using HealthExam.Domain.Catalogs;
using HealthExam.Infrastructure.Persistence.Legacy;
using UnitOfWork = HealthExam.Infrastructure.Persistence.Legacy.UnitOfWork;
using IUnitOfWork = HealthExam.Infrastructure.Persistence.Legacy.IUnitOfWork;
using HealthExam.API.Contracts;
using HealthExam.API.Middlewares;
using HealthExam.Application.Common;
using HealthExam.Server.Service;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Xunit;

namespace HealthExam.Tests;

/// <summary>
/// Bộ đồ nghề chạy tầng service trên PostgreSQL THẬT.
///
/// VÌ SAO CẦN, dù đã có <see cref="InMemoryTestDb"/>: nhánh cấp mã hồ sơ tự động
/// (<c>ExamRecordService.CreateAsync</c> khi <c>RecordCode</c> rỗng) đi qua transaction,
/// SAVEPOINT và một câu <c>UPDATE ... RETURNING</c> viết tay. Provider in-memory không có
/// thứ nào trong ba thứ đó, nên MỌI test nạp Excel trong bộ in-memory buộc phải truyền sẵn
/// <c>RecordCode</c> — tức là chúng ghim nhánh <c>else</c>, còn dòng Excel thật thì
/// KHÔNG BAO GIỜ đi vào nhánh đó (<c>ExamImportService.ToSaveRequest</c> không đặt
/// <c>RecordCode</c>). Bỏ nguyên nhánh cấp mã đi mà cả bộ in-memory vẫn xanh.
///
/// CÁCH BẬT:
///     export HEALTHEXAM_TEST_DB='Host=...;Port=5432;Database=...;User ID=...;Password=...'
///     TZ=Asia/Ho_Chi_Minh dotnet test
/// Không đặt biến thì các test dùng lớp này TỰ BỎ QUA (xunit 2.x không có Assert.Skip động).
/// Bỏ qua là im lặng, nên có <c>PostgresFixtureGuardTests</c> canh: biến ĐÃ đặt mà không nối
/// được thì test đó đỏ, thay vì cả nhóm lặng lẽ không chạy.
///
/// CÁCH LY: mỗi thể hiện dựng một schema riêng <c>hextest_xxxxxxxx</c> và ghim
/// <c>Search Path</c> vào đó, nên chạy song song hay chạy trên DB có sẵn dữ liệu đều không
[CollectionDefinition("PostgresTestDb", DisableParallelization = true)]
public class PostgresTestDbCollectionDefinition { }

public sealed class PostgresTestDb : IDisposable
{
    private static readonly object _migrateLock = new();
    public const string ConnectionEnv = "HEALTHEXAM_TEST_DB";

    /// <summary>Chuỗi kết nối gốc, rỗng nghĩa là chưa bật nhóm test này.</summary>
    public static string BaseConnectionString
    {
        get
        {
            var conn = Environment.GetEnvironmentVariable(ConnectionEnv) ?? "";
            if (string.IsNullOrWhiteSpace(conn))
            {
                conn = Environment.GetEnvironmentVariable("HEX_PG_TESTS") ?? "";
            }
            return conn.Trim();
        }
    }

    public static bool Enabled => BaseConnectionString.Length > 0;

    public HealthExamDbContext Db { get; }
    public string ConnectionString { get; }
    public IUnitOfWork Uow { get; }
    public FakeHealthExamContext Ctx { get; }
    public AuditService Audit { get; }
    public ExamSessionService Sessions { get; }
    public ExamRecordService Records { get; }
    public TestImportService Imports { get; }

    private readonly string _schema;

    public PostgresTestDb(FakeHealthExamContext ctx = null)
    {
        if (string.IsNullOrWhiteSpace(BaseConnectionString))
        {
            throw new InvalidOperationException("HEALTHEXAM_TEST_DB is required for Infrastructure integration tests");
        }

        Ctx = ctx ?? new FakeHealthExamContext();
        _schema = "hextest_" + Guid.NewGuid().ToString("N")[..8];

        using (var admin = new NpgsqlConnection(BaseConnectionString))
        {
            admin.Open();
            using var cmd = admin.CreateCommand();
            cmd.CommandText = $"CREATE SCHEMA \"{_schema}\";";
            cmd.ExecuteNonQuery();
        }

        // Search Path phải trỏ vào schema vừa tạo TRƯỚC khi Migrate: script migration dùng
        // tên bảng không định danh schema, và câu UPDATE ... RETURNING trong
        // AllocateRecordNoAsync cũng vậy. Lệch chỗ này thì test ghi vào public của DB thật.
        ConnectionString = new NpgsqlConnectionStringBuilder(BaseConnectionString)
        {
            SearchPath = _schema,
            // Pool riêng cho từng schema, nếu không thì kết nối tái dùng mang theo search_path cũ.
            Pooling = false
        }.ToString();

        Db = new HealthExamDbContext(new DbContextOptionsBuilder<HealthExamDbContext>()
            .UseNpgsql(ConnectionString)
            // Giống hệt cấu hình DI thật (AddHealthExamRepositories) — xem chú thích cùng chỗ
            // ở InMemoryTestDb: chạy tracking mặc định sẽ che mất lỗi quên .AsTracking().
            .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking)
            .Options);

        lock (_migrateLock)
        {
            Db.Database.Migrate();
        }

        Uow = new UnitOfWork(Db);
        Audit = new AuditService(Uow, Ctx);
        Sessions = new ExamSessionService(Uow, Ctx, Audit);
        var masterData = new MasterDataService(Uow, Ctx);
        Records = new ExamRecordService(Uow, Ctx, Sessions, masterData, Audit);
        Imports = new TestImportService(Db, Ctx);
    }

    /// <summary>Đợt khám mở sẵn. Ghi thẳng qua DbContext, cùng lý do như InMemoryTestDb.</summary>
    public ExamSession SeedSession(string sessionCode = "DK-2026-001")
    {
        var session = new ExamSession
        {
            SessionID = Guid.NewGuid(),
            DivisionID = Ctx.DivisionId,
            SessionCode = sessionCode,
            SessionName = "Đợt khám công ty A",
            ExamDate = new DateOnly(2026, 8, 26),
            ExamPlace = "Hội trường tầng 3",
            VariantCode = "DTK_01",
            State = ExamSessionState.Open
        };
        Db.ExamSessions.Add(session);
        Db.SaveChanges();
        Db.ChangeTracker.Clear();
        return session;
    }

    public void Dispose()
    {
        try
        {
            Db.Dispose();
            using var admin = new NpgsqlConnection(BaseConnectionString);
            admin.Open();
            using var cmd = admin.CreateCommand();
            cmd.CommandText = $"DROP SCHEMA IF EXISTS \"{_schema}\" CASCADE;";
            cmd.ExecuteNonQuery();
        }
        catch
        {
            // Dọn dẹp hỏng không được phép làm đỏ một test đã chạy xong: kết quả nghiệp vụ
            // vẫn đúng, chỉ còn lại một schema rác mang tiền tố hextest_ để xoá tay.
        }
    }
}
