using System.Data.Common;
using HealthExam.Infrastructure.Persistence;
using HealthExam.Infrastructure.Persistence.Legacy;
using HealthExam.Server.Service;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using UnitOfWork = HealthExam.Infrastructure.Persistence.Legacy.UnitOfWork;
using Xunit;

namespace HealthExam.Tests;

[Collection("PostgresTestDb")]
public class ExamDefaultSessionPostgresTests
{
    [Fact]
    public async Task Concurrent_first_requests_converge_on_one_default_session()
    {
        if (!PostgresTestDb.Enabled) return;

        using var owner = new PostgresTestDb();
        var lookupBarrier = new InitialLookupBarrier();

        await using var firstUow = NewUnitOfWork(owner.ConnectionString, lookupBarrier);
        await using var secondUow = NewUnitOfWork(owner.ConnectionString, lookupBarrier);
        var firstContext = new FakeHealthExamContext();
        var secondContext = new FakeHealthExamContext();
        var first = new ExamSessionService(firstUow, firstContext, new AuditService(firstUow, firstContext));
        var second = new ExamSessionService(secondUow, secondContext, new AuditService(secondUow, secondContext));

        var sessions = await Task.WhenAll(
            first.GetOrCreatePhaseOneDefaultAsync(),
            second.GetOrCreatePhaseOneDefaultAsync());

        Assert.Equal(sessions[0].SessionID, sessions[1].SessionID);
        Assert.Single(await owner.Db.ExamSessions
            .Where(x => x.SessionCode == ExamSessionService.PhaseOneDefaultSessionCode)
            .ToListAsync());
    }

    private static UnitOfWork NewUnitOfWork(string connectionString, DbCommandInterceptor interceptor)
    {
        var db = new HealthExamDbContext(new DbContextOptionsBuilder<HealthExamDbContext>()
            .UseNpgsql(connectionString)
            .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking)
            .AddInterceptors(interceptor)
            .Options);
        return new UnitOfWork(db);
    }

    /// <summary>
    /// Cho cả hai SELECT đầu cùng hoàn tất trước khi request nào được INSERT. Nếu implementation
    /// là check-then-insert thuần tuý, một request sẽ đụng unique constraint; ON CONFLICT phải
    /// làm cả hai lời gọi thành công và trả về cùng một hàng.
    /// </summary>
    private sealed class InitialLookupBarrier : DbCommandInterceptor
    {
        private int _remaining = 2;
        private readonly TaskCompletionSource _ready =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("HEX_ExamSession", StringComparison.Ordinal)
                && Volatile.Read(ref _remaining) > 0)
            {
                if (Interlocked.Decrement(ref _remaining) == 0) _ready.TrySetResult();
                await _ready.Task.WaitAsync(cancellationToken);
            }

            return await base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }
}
