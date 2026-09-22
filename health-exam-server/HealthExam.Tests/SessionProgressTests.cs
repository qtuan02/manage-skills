using HealthExam.API.Contracts;
using HealthExam.API.Middlewares;
using HealthExam.Application.Common;
using HealthExam.Domain.Common;
using HealthExam.Domain.ExamSessions;
using HealthExam.Domain.ExamRecords;
using HealthExam.Domain.Imports;
using HealthExam.Domain.Paraclinical;
using HealthExam.Domain.Webhooks;
using HealthExam.Domain.Integrations;
using HealthExam.Domain.Catalogs;
using Xunit;

namespace HealthExam.Tests;

/// <summary>
/// H2-07 — GET /v1/exam-sessions/{id}/progress, đọc HOÀN TOÀN từ cache (02-api-spec §3.1).
///
/// Gate của task: đợt vài trăm hồ sơ → trả đủ 5 con số + ConclusionReady, và KHÔNG một lời
/// gọi nào sang form-server. Ở đây điều đó được chốt bằng cấu trúc: ExamSessionProgressService
/// không nhận FormServerClient trong constructor, nên không có đường nào gọi ra ngoài.
/// </summary>
public class SessionProgressTests
{
    [Fact]
    public async Task Dem_du_nam_trang_thai_va_tong()
    {
        using var db = new InMemoryTestDb();
        var session = db.SeedSession();

        Seed(db, session.SessionID, ExamRecordState.NotRegistered, 3);
        Seed(db, session.SessionID, ExamRecordState.Waiting, 4);
        Seed(db, session.SessionID, ExamRecordState.InProgress, 5);
        Seed(db, session.SessionID, ExamRecordState.Completed, 2);
        Seed(db, session.SessionID, ExamRecordState.RegistrationCancelled, 1);
        Seed(db, session.SessionID, ExamRecordState.ExamCancelled, 1);

        var progress = await db.SessionProgress.GetAsync(session.SessionID);

        Assert.Equal(16, progress.Profiles.Total);
        Assert.Equal(3, progress.Profiles.NotRegistered);
        Assert.Equal(4, progress.Profiles.Waiting);
        Assert.Equal(5, progress.Profiles.InProgress);
        Assert.Equal(2, progress.Profiles.Completed);
        Assert.Equal(2, progress.Profiles.Cancelled);
        Assert.Equal(1, progress.Profiles.RegistrationCancelled);
        Assert.Equal(1, progress.Profiles.ExamCancelled);
        Assert.Equal(session.SessionCode, progress.SessionCode);
    }

    /// <summary>
    /// ConclusionReady = hồ sơ ĐANG KHÁM đã đủ số nhóm khám. Hồ sơ chưa nhận sự kiện nào có
    /// 0/0 — thiếu điều kiện ProgressTotal > 0 thì 0 >= 0 đúng và mọi hồ sơ mới toanh đều bị
    /// đếm là đã khám đủ.
    /// </summary>
    [Fact]
    public async Task ConclusionReady_khong_dem_ho_so_chua_nhan_su_kien_nao()
    {
        using var db = new InMemoryTestDb();
        var session = db.SeedSession();

        db.SeedRecord(session.SessionID, ExamRecordState.InProgress, recordCode: "R-1",
            progressDone: 17, progressTotal: 17);
        db.SeedRecord(session.SessionID, ExamRecordState.InProgress, recordCode: "R-2",
            progressDone: 9, progressTotal: 17);
        // Chưa nhận sự kiện nào: 0/0.
        db.SeedRecord(session.SessionID, ExamRecordState.InProgress, recordCode: "R-3");

        var progress = await db.SessionProgress.GetAsync(session.SessionID);

        Assert.Equal(1, progress.ConclusionReady);
    }

    /// <summary>
    /// UpdatedAt lấy mốc mới nhất của CẢ HAI nguồn: hồ sơ chỉ đổi trạng thái (LastEventAt) và
    /// hồ sơ chỉ nhích tiến độ (ProgressSyncedAt) đều làm số ở trên cũ đi. FE hiện mốc này để
    /// người dùng biết mình đang nhìn ảnh chụp lúc nào.
    /// </summary>
    [Fact]
    public async Task UpdatedAt_lay_moc_moi_nhat_cua_ca_hai_nguon()
    {
        using var db = new InMemoryTestDb();
        var session = db.SeedSession();
        var cu = new DateTime(2026, 8, 24, 1, 0, 0, DateTimeKind.Utc);
        var moi = new DateTime(2026, 8, 24, 9, 0, 0, DateTimeKind.Utc);

        db.SeedRecord(session.SessionID, ExamRecordState.Waiting, recordCode: "R-1", lastEventAt: moi);
        var r2 = db.SeedRecord(session.SessionID, ExamRecordState.InProgress, recordCode: "R-2", lastEventAt: cu);
        db.Db.ExamRecords.Attach(r2);
        r2.ProgressSyncedAt = cu;
        db.Db.SaveChanges();
        db.Db.ChangeTracker.Clear();

        var progress = await db.SessionProgress.GetAsync(session.SessionID);

        Assert.Equal(moi, progress.UpdatedAt);
    }

    /// <summary>Đợt chưa nhận sự kiện nào ⇒ UpdatedAt NULL, không phải "bây giờ": FE phải phân
    /// biệt được "chưa có số liệu" với "số liệu vừa cập nhật".</summary>
    [Fact]
    public async Task Dot_chua_nhan_su_kien_nao_thi_UpdatedAt_de_trong()
    {
        using var db = new InMemoryTestDb();
        var session = db.SeedSession();
        db.SeedRecord(session.SessionID);

        var progress = await db.SessionProgress.GetAsync(session.SessionID);

        Assert.Null(progress.UpdatedAt);
    }

    /// <summary>Hồ sơ của đợt khác không lọt vào số liệu của đợt này.</summary>
    [Fact]
    public async Task Khong_dem_lan_ho_so_cua_dot_khac()
    {
        using var db = new InMemoryTestDb();
        var a = db.SeedSession(sessionCode: "DK-A");
        var b = db.SeedSession(sessionCode: "DK-B");
        Seed(db, a.SessionID, ExamRecordState.Waiting, 2);
        Seed(db, b.SessionID, ExamRecordState.Waiting, 7);

        var progress = await db.SessionProgress.GetAsync(a.SessionID);

        Assert.Equal(2, progress.Profiles.Total);
    }

    /// <summary>Đợt của tenant khác ⇒ 4040, không phải một trang số 0 trông như "đợt trống".</summary>
    [Fact]
    public async Task Dot_cua_tenant_khac_tra_4040()
    {
        using var db = new InMemoryTestDb();
        var session = db.SeedSession(divisionId: "BV-KHAC", sessionCode: "DK-KHAC");

        var ex = await Assert.ThrowsAsync<HealthExamException>(
            () => db.SessionProgress.GetAsync(session.SessionID));

        Assert.Equal(ErrorCodes.NotFound, ex.ErrorCode);
    }

    private static void Seed(InMemoryTestDb db, Guid sessionId, ExamRecordState state, int count)
    {
        for (var i = 0; i < count; i++)
            db.SeedRecord(sessionId, state, recordCode: $"{state}-{i:00}");
    }
}
