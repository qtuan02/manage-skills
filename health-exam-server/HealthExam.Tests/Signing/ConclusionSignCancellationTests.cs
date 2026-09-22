using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HealthExam.Application.Common;
using HealthExam.Application.Integrations;
using HealthExam.Application.Paraclinical;
using HealthExam.Domain.Common;
using HealthExam.Domain.ExamRecords;
using HealthExam.Infrastructure.Persistence.Repositories;
using Xunit;

namespace HealthExam.Tests.Signing;

/// <summary>
/// Client ngắt kết nối SAU khi PDF đã ký được ghi lên MinIO: lượt lưu + commit cuối phải chạy
/// tới cùng, nếu không file đã ký nằm mồ côi trong bucket còn hồ sơ vẫn New — gọi lại sẽ ký
/// và ghi thêm một file nữa.
/// </summary>
public class ConclusionSignCancellationTests
{
    /// <summary>Hủy token của caller ngay trong lượt upload — mô phỏng client rớt sau khi file đã lên.</summary>
    private sealed class CancelOnUploadStore : IExamFileStore
    {
        private readonly FakeExamFileStore _inner = new();
        private readonly CancellationTokenSource _cts;
        public CancelOnUploadStore(CancellationTokenSource cts) => _cts = cts;
        public Dictionary<string, byte[]> Objects => _inner.Objects;

        public async Task<bool> UploadPdfAsync(string objectPath, byte[] pdf, CancellationToken ct = default)
        {
            var ok = await _inner.UploadPdfAsync(objectPath, pdf, ct);
            _cts.Cancel();
            return ok;
        }

        public Task<byte[]> DownloadAsync(string objectPath, CancellationToken ct = default)
            => _inner.DownloadAsync(objectPath, ct);
    }

    /// <summary>Ghi lại token mà handler đưa vào từng lượt Save/Commit — bọc UnitOfWork thật.</summary>
    private sealed class TokenRecordingUnitOfWork : IUnitOfWork
    {
        private readonly IUnitOfWork _inner;
        public List<bool> SaveTokenCancelled { get; } = new();
        public List<bool> CommitTokenCancelled { get; } = new();

        public TokenRecordingUnitOfWork(IUnitOfWork inner) => _inner = inner;

        public async Task<IApplicationTransaction> BeginAsync(CancellationToken ct = default)
            => new Tx(await _inner.BeginAsync(ct), this);

        public Task<PersistenceSaveResult> SaveChangesAsync(CancellationToken ct = default)
        {
            SaveTokenCancelled.Add(ct.IsCancellationRequested);
            return _inner.SaveChangesAsync(CancellationToken.None);
        }

        public void DiscardPendingChanges() => _inner.DiscardPendingChanges();

        private sealed class Tx : IApplicationTransaction
        {
            private readonly IApplicationTransaction _inner;
            private readonly TokenRecordingUnitOfWork _owner;
            public Tx(IApplicationTransaction inner, TokenRecordingUnitOfWork owner) { _inner = inner; _owner = owner; }
            public Task CommitAsync(CancellationToken ct = default)
            {
                _owner.CommitTokenCancelled.Add(ct.IsCancellationRequested);
                return _inner.CommitAsync(CancellationToken.None);
            }
            public Task RollbackAsync(CancellationToken ct = default) => _inner.RollbackAsync(CancellationToken.None);
            public ValueTask DisposeAsync() => _inner.DisposeAsync();
        }
    }

    /// <summary>
    /// Upload treo tới lúc client rớt (ca dhtesting 18/09: MinIO sai scheme, PutObject bị hủy theo
    /// ct sau 60s) — store trả false và token của caller đã hủy.
    /// </summary>
    private sealed class FailAndCancelStore : IExamFileStore
    {
        private readonly CancellationTokenSource _cts;
        public FailAndCancelStore(CancellationTokenSource cts) => _cts = cts;

        public Task<bool> UploadPdfAsync(string objectPath, byte[] pdf, CancellationToken ct = default)
        {
            _cts.Cancel();
            return Task.FromResult(false);
        }

        public Task<byte[]> DownloadAsync(string objectPath, CancellationToken ct = default)
            => Task.FromResult<byte[]>(null);
    }

    [Fact]
    public async Task Upload_hong_va_client_da_rot_thi_van_luu_duoc_Failed()
    {
        using var f = new ConclusionFixture();
        await f.SignAllSections();
        using var cts = new CancellationTokenSource();
        var uow = new TokenRecordingUnitOfWork(new HealthExam.Infrastructure.Persistence.UnitOfWork(f.Db.Db));
        var handler = new SignConclusionHandler(
            new ParaclinicalRepository(f.Db.Db), new ExamRecordRepository(f.Db.Db), f.Map, f.Cert,
            f.Signer, new FailAndCancelStore(cts), f.His, uow, new HealthExam.Infrastructure.Persistence.AuditRepository(f.Db.Db));

        var res = await handler.HandleAsync(new SignConclusionCommand(
            ConclusionFixture.DivisionId, "1009", "BS KL", ActorKind.Employee, f.Record.RecordID,
            "Bearer t", "TRACE", "NV009", new[] { ConclusionFixture.ConclusionRoleId }, DepartmentId: 458), cts.Token);

        Assert.False(res.IsSuccess);
        Assert.Equal(ApplicationFailureCode.HisBadGateway, res.Failure.Code);
        // Lượt save Failed + commit phải đi với token KHÔNG hủy, nếu không ném OperationCanceledException
        // và mất luôn dấu Failed (hồ sơ trông như chưa từng ký).
        Assert.False(uow.SaveTokenCancelled.Last());
        Assert.Equal(new[] { false }, uow.CommitTokenCancelled);
        Assert.Equal(ExamRecordSignStatus.Failed, f.Db.RecordOf(f.Record.RecordID).SignStatus);
    }

    [Fact]
    public async Task Client_rot_sau_upload_thi_luu_va_commit_cuoi_van_chay_voi_token_khong_huy()
    {
        using var f = new ConclusionFixture();
        await f.SignAllSections();
        using var cts = new CancellationTokenSource();
        var store = new CancelOnUploadStore(cts);
        var uow = new TokenRecordingUnitOfWork(new HealthExam.Infrastructure.Persistence.UnitOfWork(f.Db.Db));
        var handler = new SignConclusionHandler(
            new ParaclinicalRepository(f.Db.Db), new ExamRecordRepository(f.Db.Db), f.Map, f.Cert,
            f.Signer, store, f.His, uow, new HealthExam.Infrastructure.Persistence.AuditRepository(f.Db.Db));

        var res = await handler.HandleAsync(new SignConclusionCommand(
            ConclusionFixture.DivisionId, "1009", "BS KL", ActorKind.Employee, f.Record.RecordID,
            "Bearer t", "TRACE", "NV009", new[] { ConclusionFixture.ConclusionRoleId }, DepartmentId: 458), cts.Token);

        Assert.True(res.IsSuccess);
        Assert.True(cts.IsCancellationRequested);
        Assert.Single(store.Objects);
        // Lượt save cuối (sau upload) và commit phải đi với token KHÔNG hủy — lượt save snapshot
        // trước upload vẫn dùng token của caller.
        Assert.False(uow.SaveTokenCancelled.Last());
        Assert.Equal(new[] { false }, uow.CommitTokenCancelled);
        Assert.Equal(ExamRecordSignStatus.Signed, f.Db.RecordOf(f.Record.RecordID).SignStatus);
    }
}
