using HealthExam.API.Contracts;
using Xunit;

namespace HealthExam.Tests;

/// <summary>
/// Ghim các biểu thức filter trên provider thật. Khi chưa cấu hình PostgreSQL, nhóm này bỏ qua
/// giống các test allocation hiện hữu; InMemory vẫn chốt toàn bộ nghiệp vụ và boundary.
/// </summary>
[Collection("PostgresTestDb")]
public sealed class ExamRecordListFilterPostgresTests
{
    [Fact]
    public async Task Filter_va_khoang_ngay_tao_dich_duoc_tren_PostgreSQL()
    {
        if (!PostgresTestDb.Enabled) return;

        using var db = new PostgresTestDb();
        var session = db.SeedSession();
        await db.Records.CreateAsync(new ExamRecordSaveRequest
        {
            SessionID = session.SessionID,
            RecordCode = "KSK-FILTER-001",
            FullName = "Nguyễn Văn A",
            IdentityNumber = "012345678901",
            PhoneNumber = "0909123456",
            VariantCode = "DTK_01"
        });
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var result = await db.Records.ListAsync(new ExamRecordListFilter
        {
            FullName = "nguyễn văn",
            IdentityNumber = "345678",
            PhoneNumber = "123456",
            From = today,
            To = today
        }, 1, 20);

        Assert.Equal("KSK-FILTER-001", Assert.Single(result.Items).RecordCode);
    }
}
