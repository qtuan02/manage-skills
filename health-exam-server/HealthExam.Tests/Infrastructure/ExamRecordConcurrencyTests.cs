using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HealthExam.Application.Common;
using HealthExam.Application.ExamRecords;
using HealthExam.Application.Patients;
using HealthExam.Domain.Common;
using HealthExam.Domain.ExamRecords;
using HealthExam.Domain.ExamSessions;
using HealthExam.Infrastructure.Persistence;
using HealthExam.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HealthExam.Tests.Infrastructure;

[Collection("PostgresTestDb")]
public class ExamRecordConcurrencyTests
{
    [Fact]
    public async Task Competing_allocators_produce_distinct_sequential_codes()
    {
        if (!PostgresTestDb.Enabled) return;

        using var db = new PostgresTestDb();
        var session = db.SeedSession();
        var concurrency = 8;
        var allocatedCodes = new ConcurrentBag<string>();

        var tasks = Enumerable.Range(0, concurrency).Select(async _ =>
        {
            await using var context = CreateContext(db.ConnectionString);
            var allocator = new RecordCodeAllocator(context);
            var code = await allocator.NextAsync(session.DivisionID, session.SessionID, session.SessionCode);
            allocatedCodes.Add(code);
        });

        await Task.WhenAll(tasks);

        Assert.Equal(concurrency, allocatedCodes.Count);
        Assert.Equal(concurrency, allocatedCodes.Distinct().Count());

        var numbers = allocatedCodes
            .Select(c => int.Parse(c.Split('-').Last()))
            .OrderBy(n => n)
            .ToList();

        Assert.Equal(Enumerable.Range(1, concurrency), numbers);
    }

    [Fact]
    public async Task Sequence_values_remain_consumed_after_rollback()
    {
        if (!PostgresTestDb.Enabled) return;

        using var db = new PostgresTestDb();
        var session = db.SeedSession();

        // Step 1: Pre-occupy code -0001 manually
        await using (var context1 = CreateContext(db.ConnectionString))
        {
            var uow1 = new UnitOfWork(context1);
            var audit1 = new AuditRepository(context1);
            var sessionRepo1 = new ExamSessionRepository(context1);
            var recordRepo1 = new ExamRecordRepository(context1);
            var allocator1 = new RecordCodeAllocator(context1);
            var patientRepo1 = new PatientRepository(context1);
            var writer1 = new PatientRegistrationWriter(patientRepo1, uow1, new SystemClock());

            var handler1 = new CreateExamRecordHandler(recordRepo1, sessionRepo1, allocator1, uow1, audit1, writer1);
            var cmd1 = new CreateExamRecordCommand(
                DivisionId: session.DivisionID,
                ActorId: "tester",
                ActorKind: ActorKind.Employee,
                SessionID: session.SessionID,
                RecordCode: $"{session.SessionCode}-0001",
                FullName: "Người nhập tay",
                VariantCode: "DTK_01");

            var res1 = await handler1.HandleAsync(cmd1);
            Assert.True(res1.IsSuccess);
            Assert.Equal($"{session.SessionCode}-0001", res1.Value.RecordCode);
        }

        // Step 2: Create auto-allocated record. It attempts -0001, encounters collision,
        // rolls back to savepoint (keeping counter increment consumed), and consumes -0002.
        await using (var context2 = CreateContext(db.ConnectionString))
        {
            var uow2 = new UnitOfWork(context2);
            var audit2 = new AuditRepository(context2);
            var sessionRepo2 = new ExamSessionRepository(context2);
            var recordRepo2 = new ExamRecordRepository(context2);
            var allocator2 = new RecordCodeAllocator(context2);
            var patientRepo2 = new PatientRepository(context2);
            var writer2 = new PatientRegistrationWriter(patientRepo2, uow2, new SystemClock());

            var handler2 = new CreateExamRecordHandler(recordRepo2, sessionRepo2, allocator2, uow2, audit2, writer2);
            var cmd2 = new CreateExamRecordCommand(
                DivisionId: session.DivisionID,
                ActorId: "tester",
                ActorKind: ActorKind.Employee,
                SessionID: session.SessionID,
                FullName: "Nguyễn Văn Tự Sinh",
                VariantCode: "DTK_01");

            var res2 = await handler2.HandleAsync(cmd2);
            Assert.True(res2.IsSuccess);
            Assert.Equal($"{session.SessionCode}-0002", res2.Value.RecordCode);
        }

        // Step 3: Counter in database advanced to 2
        await using (var context3 = CreateContext(db.ConnectionString))
        {
            var sess = await context3.ExamSessions.FirstAsync(x => x.SessionID == session.SessionID);
            Assert.Equal(2, sess.LastRecordNo);
        }
    }

    [Fact]
    public async Task Competing_duplicate_records_return_duplicate_in_session_outcome()
    {
        if (!PostgresTestDb.Enabled) return;

        using var db = new PostgresTestDb();
        var session = db.SeedSession();
        var patientCode = "PAT-CONCURRENT-01";

        Task<ApplicationResult<ExamRecordResult>> RunCreateAsync()
        {
            return Task.Run(async () =>
            {
                await using var context = CreateContext(db.ConnectionString);
                var uow = new UnitOfWork(context);
                var audit = new AuditRepository(context);
                var sessionRepo = new ExamSessionRepository(context);
                var recordRepo = new ExamRecordRepository(context);
                var allocator = new RecordCodeAllocator(context);
                var patientRepo = new PatientRepository(context);
                var writer = new PatientRegistrationWriter(patientRepo, uow, new SystemClock());

                var handler = new CreateExamRecordHandler(recordRepo, sessionRepo, allocator, uow, audit, writer);
                var command = new CreateExamRecordCommand(
                    DivisionId: session.DivisionID,
                    ActorId: "tester",
                    ActorKind: ActorKind.Employee,
                    SessionID: session.SessionID,
                    PatientCode: patientCode,
                    FullName: "Nguyễn Văn Trùng",
                    VariantCode: "DTK_01");

                return await handler.HandleAsync(command);
            });
        }

        var results = await Task.WhenAll(RunCreateAsync(), RunCreateAsync());

        var successes = results.Count(r => r.IsSuccess);
        var duplicates = results.Count(r => !r.IsSuccess && r.Failure.Code == ApplicationFailureCode.DuplicateInSession);

        Assert.Equal(1, successes);
        Assert.Equal(1, duplicates);
    }

    [Fact]
    public async Task Concurrent_default_session_requests_converge_without_duplicate_error()
    {
        if (!PostgresTestDb.Enabled) return;

        using var db = new PostgresTestDb();
        var divisionId = "DIV-DEF-CONCURRENT";

        Task<ExamSession> RunGetDefaultAsync()
        {
            return Task.Run(async () =>
            {
                await using var context = CreateContext(db.ConnectionString);
                var repo = new ExamRecordRepository(context);
                return await repo.GetOrCreateDefaultSessionAsync(divisionId);
            });
        }

        var results = await Task.WhenAll(RunGetDefaultAsync(), RunGetDefaultAsync());

        Assert.Equal(results[0].SessionID, results[1].SessionID);
        Assert.Equal("PHASE1-DEFAULT", results[0].SessionCode);
    }

    [Fact]
    public async Task Concurrent_sign_commands_race_and_only_one_submits_new_transaction()
    {
        if (!PostgresTestDb.Enabled) return;

        using var db = new PostgresTestDb();
        var session = db.SeedSession();
        var recordId = Guid.NewGuid();

        await using (var context = CreateContext(db.ConnectionString))
        {
            var record = new ExamRecord
            {
                RecordID = recordId,
                DivisionID = session.DivisionID,
                SessionID = session.SessionID,
                RecordCode = $"{session.SessionCode}-SIGN-RACE",
                State = ExamRecordState.Completed,
                AdmissionID = 7001
            };
            context.ExamRecords.Add(record);
            await context.SaveChangesAsync();
        }

        // Two racing threads attempting to claim the submission lock
        var submitCount = 0;
        async Task RunSignSubmitRaceAsync()
        {
            await using var context = new HealthExamDbContext(new DbContextOptionsBuilder<HealthExamDbContext>()
                .UseNpgsql(db.ConnectionString)
                .Options);

            await using var tx = await context.Database.BeginTransactionAsync();
            // Row-level lock or atomic update
            var rec = await context.ExamRecords
                .FromSqlInterpolated($"SELECT * FROM \"HEX_ExamRecord\" WHERE \"RecordID\" = {recordId} FOR UPDATE")
                .FirstOrDefaultAsync();

            if (rec != null && rec.SignStatus == ExamRecordSignStatus.New)
            {
                System.Threading.Interlocked.Increment(ref submitCount);
                rec.SignStatus = ExamRecordSignStatus.Signed;
                await context.SaveChangesAsync();
            }

            await tx.CommitAsync();
        }

        await Task.WhenAll(
            Task.Run(RunSignSubmitRaceAsync),
            Task.Run(RunSignSubmitRaceAsync));

        Assert.Equal(1, submitCount);
    }

    private static HealthExamDbContext CreateContext(string connectionString)
    {
        return new HealthExamDbContext(new DbContextOptionsBuilder<HealthExamDbContext>()
            .UseNpgsql(connectionString)
            .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking)
            .Options);
    }
}

