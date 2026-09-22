using HealthExam.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace HealthExam.Tests;

/// <summary>
/// Một PostgreSQL THẬT cho những chốt chặn mà provider in-memory không diễn đạt nổi.
///
/// 🔴 VÌ SAO PHẢI CÓ, đọc trước khi định bỏ: câu lấy gói của
/// <see cref="HealthExam.Server.Service.IntegrationOutboxWorker"/> là SQL thô
/// (<c>FROM …WHERE …FOR UPDATE SKIP LOCKED</c>). Provider in-memory NÉM ngay ở
/// <c>FromSqlRaw</c>, nên mọi test đi qua tầng service đều KHÔNG chạm được vào nó. Ba thứ
/// nằm trọn trong câu đó và không có nơi nào khác để kiểm:
///   · chốt THỨ TỰ (điểm chặn 1 — gói CANCELLED không được vượt mặt gói NEW đang backoff),
///   · <c>SKIP LOCKED</c> (hai pod không cùng gửi một phiếu),
///   · lọc <c>DivisionID</c> (không gửi gói của đơn vị này tới RIS của đơn vị khác).
/// Cộng thêm ràng buộc UNIQUE ở tầng DB, thứ in-memory cũng không có.
///
/// ⚠️ LƯỢT CHẠY MẶC ĐỊNH KHÔNG CHẠY BỘ NÀY. Phải khai chuỗi kết nối:
/// <code>
/// HEX_PG_TESTS='Host=localhost;Port=15433;Username=hex;Password=…;Database=postgres' \
///   TZ=Asia/Ho_Chi_Minh dotnet test HealthExam.Tests/HealthExam.Tests.csproj
/// </code>
///
/// 🔴 ĐỌC KỸ CHỖ NÀY TRƯỚC KHI TIN MỘT CON SỐ. Không khai biến thì các ca của bộ này
/// XANH RỖNG — chúng thoát ở dòng đầu. KHÔNG phải "bỏ qua" mà là "xanh mà chưa kiểm gì",
/// và tổng số test KHÔNG ĐỔI giữa hai lượt. Lý do: xunit v2 (2.9.2 ở repo này) KHÔNG có
/// <c>Assert.Skip</c>, và <c>[Theory]</c> không có dòng dữ liệu nào thì bị báo LỖI chứ
/// không phải bỏ qua — nên không có cách nào làm cho lượt mặc định hiện ra là đã bỏ qua.
///
/// Hệ quả phải nói thẳng mỗi lần báo cáo: con số của lượt mặc định KHÔNG chứng minh gì về
/// chốt thứ tự, SKIP LOCKED, lọc tenant hay ràng buộc UNIQUE. Bằng chứng những ca này CÓ
/// chạy thật là các phép ĐỘT BIẾN (bỏ vế NOT EXISTS / đổi nó thành <c>&lt;&gt; 1</c> / bỏ vế
/// lọc DivisionID) — chúng chỉ đỏ được nếu câu SQL đã thực sự chạy trên PostgreSQL.
///
/// Nhưng KHÔNG có nhánh xanh giả ở phía CÒN LẠI: khai biến mà không nối được thì bộ này ĐỎ,
/// không lặng lẽ bỏ qua. Nếu không thì "đã bật test PostgreSQL" và "test PostgreSQL đang
/// chạy" là hai chuyện khác nhau mà không ai phân biệt được.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    /// <summary>Chuỗi kết nối tới DB QUẢN TRỊ (thường là <c>postgres</c>) — fixture tự tạo một
    /// DB riêng cho lượt chạy rồi xoá đi.</summary>
    public const string Env = "HEX_PG_TESTS";

    private string _adminConnection;
    private string _database;

    /// <summary>Có chạy được không. Kiểm ở đầu mỗi ca — xem chú thích của lớp.</summary>
    public bool Enabled => _adminConnection != null;

    public string ConnectionString { get; private set; }

    public async Task InitializeAsync()
    {
        _adminConnection = (Environment.GetEnvironmentVariable("HEALTHEXAM_TEST_DB")
            ?? Environment.GetEnvironmentVariable(Env)
            ?? "").Trim();
        if (_adminConnection.Length == 0)
        {
            _adminConnection = null;
            throw new InvalidOperationException("HEALTHEXAM_TEST_DB is required for Infrastructure integration tests");
        }

        // DB riêng cho mỗi lượt: các assert kiểu "đúng MỘT hàng tới hạn" sai ngay khi hai lượt
        // chạy chồng lên nhau, và một lượt bỏ dở để lại rác thì lượt sau đỏ vì lý do khác hẳn.
        _database = $"hex_wf_{Guid.NewGuid():N}";

        var builder = new NpgsqlConnectionStringBuilder(_adminConnection);
        await using (var admin = new NpgsqlConnection(builder.ConnectionString))
        {
            // KHÔNG bắt lỗi: khai biến mà không nối được thì phải ĐỎ ở đây, không xanh giả.
            await admin.OpenAsync();
            await using var cmd = admin.CreateCommand();
            cmd.CommandText = $"CREATE DATABASE \"{_database}\"";
            await cmd.ExecuteNonQueryAsync();
        }

        builder.Database = _database;
        ConnectionString = builder.ConnectionString;

        // Migrate THẬT chứ không EnsureCreated: thứ đang kiểm gồm cả chỉ mục riêng phần
        // IX_HEX_Outbox_Pending và ràng buộc UX_HEX_Outbox_Dedup, mà EnsureCreated dựng schema
        // từ model chứ không từ migration — tức kiểm một schema KHÁC schema sẽ deploy.
        await using var db = NewContext();
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        if (!Enabled) return;

        var builder = new NpgsqlConnectionStringBuilder(_adminConnection);
        await using var admin = new NpgsqlConnection(builder.ConnectionString);
        await admin.OpenAsync();

        await using var cmd = admin.CreateCommand();
        cmd.CommandText = $"DROP DATABASE IF EXISTS \"{_database}\" WITH (FORCE)";
        await cmd.ExecuteNonQueryAsync();
    }

    public HealthExamDbContext NewContext()
        => new(new DbContextOptionsBuilder<HealthExamDbContext>()
            .UseNpgsql(ConnectionString)
            // Chép đúng cấu hình DI thật (AddHealthExamRepositories): chạy test ở chế độ
            // tracking mặc định thì một service quên .AsTracking() vẫn lưu được ở đây nhưng
            // âm thầm mất thay đổi lúc chạy thật.
            .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking)
            .Options);

    public UnitOfWork NewUow(out HealthExamDbContext db)
    {
        db = NewContext();
        return new UnitOfWork(db);
    }
}

/// <summary>Gom mọi bộ test dùng PostgreSQL vào một collection: chúng chia nhau MỘT database,
/// nên chạy song song là để một ca thấy hàng của ca khác.</summary>
[CollectionDefinition(Name)]
public class PostgresCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "postgres";
}
