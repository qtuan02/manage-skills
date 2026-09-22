using HealthExam.Infrastructure.Persistence.Legacy;
using HealthExam.API.Contracts;
using IHealthExamContext = HealthExam.Application.Common.IHealthExamContext;
using HealthExam.Domain.Common;
using HealthExam.Domain.ExamSessions;
using HealthExam.Domain.ExamRecords;
using HealthExam.Domain.Imports;
using HealthExam.Domain.Paraclinical;
using HealthExam.Domain.Webhooks;
using HealthExam.Domain.Integrations;
using HealthExam.Domain.Catalogs;
using Microsoft.EntityFrameworkCore;

namespace HealthExam.Server.Service;

/// <summary>
/// GET /v1/exam-sessions/{id}/progress — 02-api-spec §3.1.
///
/// 🔴 KHÔNG gọi form-server, dù chỉ một lần: số liệu lấy hoàn toàn từ cache trong
/// HEX_ExamRecord do webhook cập nhật (§9.5). Một đợt 320 người mà mỗi lần mở màn hình lại
/// bắn 320 request sang form-server thì cả hai service cùng chết — và màn hình này là màn
/// người dùng để mở suốt buổi khám.
/// </summary>
public class ExamSessionProgressService
{
    private readonly IUnitOfWork _uow;
    private readonly IHealthExamContext _ctx;
    private readonly ExamSessionService _sessions;

    public ExamSessionProgressService(IUnitOfWork uow, IHealthExamContext ctx, ExamSessionService sessions)
    {
        _uow = uow;
        _ctx = ctx;
        _sessions = sessions;
    }

    public async Task<ExamSessionProgressItem> GetAsync(Guid sessionId, CancellationToken ct = default)
    {
        // LoadAsync đã lọc DivisionID và ném 4040 nếu đợt không thuộc tenant này.
        var session = await _sessions.LoadAsync(sessionId, ct);

        var q = _uow.ExamRecords.Query()
            .Where(x => x.SessionID == session.SessionID && x.DivisionID == _ctx.DivisionId);

        // MỘT lần đi DB cho cả 6 con số: gọi CountAsync sáu lần là sáu vòng mạng tới
        // PostgreSQL cho một màn hình mà người dùng bấm làm mới liên tục.
        var counts = await q
            .GroupBy(x => x.State)
            .Select(g => new StateCount { State = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var profiles = new ExamSessionProgressCounts
        {
            Total = counts.Sum(c => c.Count),
            NotRegistered = CountOf(counts, ExamRecordState.NotRegistered),
            Waiting = CountOf(counts, ExamRecordState.Waiting),
            InProgress = CountOf(counts, ExamRecordState.InProgress),
            Completed = CountOf(counts, ExamRecordState.Completed),
            RegistrationCancelled = CountOf(counts, ExamRecordState.RegistrationCancelled),
            ExamCancelled = CountOf(counts, ExamRecordState.ExamCancelled),
            Cancelled = CountOf(counts, ExamRecordState.RegistrationCancelled)
                      + CountOf(counts, ExamRecordState.ExamCancelled)
        };

        // "Đủ điều kiện ký kết luận" theo CACHE: đang khám và đã đủ số nhóm khám.
        // ProgressTotal > 0 là bắt buộc — hồ sơ chưa nhận sự kiện nào có 0/0, mà 0 >= 0 đúng,
        // nên thiếu điều kiện này thì mọi hồ sơ mới toanh đều bị đếm là đã khám đủ.
        var conclusionReady = await q.CountAsync(
            x => x.State == ExamRecordState.InProgress
              && x.ProgressTotal > 0 && x.ProgressDone >= x.ProgressTotal, ct);

        // Mốc mới nhất trong CẢ HAI nguồn: hồ sơ chỉ đổi trạng thái (LastEventAt) và hồ sơ
        // chỉ nhích tiến độ (ProgressSyncedAt) đều làm số ở trên cũ đi.
        var lastEventAt = await q.MaxAsync(x => (DateTime?)x.LastEventAt, ct);
        var lastSyncedAt = await q.MaxAsync(x => (DateTime?)x.ProgressSyncedAt, ct);

        return new ExamSessionProgressItem
        {
            SessionID = session.SessionID,
            SessionCode = session.SessionCode,
            State = StateNames.Of(session.State),
            Profiles = profiles,
            ConclusionReady = conclusionReady,
            UpdatedAt = Latest(lastEventAt, lastSyncedAt)
        };
    }

    private static int CountOf(List<StateCount> counts, ExamRecordState state)
        => counts.FirstOrDefault(c => c.State == state)?.Count ?? 0;

    /// <summary>Kết quả gom nhóm — khai kiểu tường minh để LINQ dịch được sang SQL.</summary>
    private class StateCount
    {
        public ExamRecordState State { get; init; }
        public int Count { get; init; }
    }

    private static DateTime? Latest(DateTime? a, DateTime? b)
    {
        if (!a.HasValue) return b;
        if (!b.HasValue) return a;
        return a.Value >= b.Value ? a : b;
    }
}
