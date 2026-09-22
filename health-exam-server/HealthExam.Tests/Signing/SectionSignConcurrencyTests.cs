using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using HealthExam.Application.Common;
using HealthExam.Application.Signing;
using HealthExam.Domain.Common;
using HealthExam.Domain.ExamForms;
using HealthExam.Domain.ExamRecords;
using HealthExam.Tests.Application;
using Xunit;

namespace HealthExam.Tests.Signing;

/// <summary>
/// Ký/hủy ký mục khám phải chạy trong cùng khuôn với ký kết luận (T9 ở SignConclusion): mở
/// giao dịch, khóa dòng HEX_ExamRecord, rồi mới đọc để sửa; và phải nhìn vào kết quả
/// SaveChangesAsync — unique (hồ sơ, biến thể, bước) vỡ ở DB thật thì đó là lượt ký đồng thời,
/// không phải "thành công".
/// </summary>
public class SectionSignConcurrencyTests
{
    private const string DivisionId = "DIV01";
    private const string VariantCode = "KSK06-18T";
    private const int ItemGroupId = 101;

    private sealed class RecordingUnitOfWork : IUnitOfWork
    {
        public PersistenceSaveOutcome NextOutcome { get; set; } = PersistenceSaveOutcome.Saved;
        public int Begun { get; private set; }
        public int Committed { get; private set; }
        public int Saves { get; private set; }
        public bool Discarded { get; private set; }
        public bool TransactionOpen { get; private set; }
        public bool SavedInsideTransaction { get; private set; } = true;

        public Task<IApplicationTransaction> BeginAsync(CancellationToken ct = default)
        {
            Begun++;
            TransactionOpen = true;
            return Task.FromResult<IApplicationTransaction>(new Tx(this));
        }

        public Task<PersistenceSaveResult> SaveChangesAsync(CancellationToken ct = default)
        {
            Saves++;
            SavedInsideTransaction &= TransactionOpen;
            return Task.FromResult(new PersistenceSaveResult(NextOutcome));
        }

        public void DiscardPendingChanges() => Discarded = true;

        private sealed class Tx : IApplicationTransaction
        {
            private readonly RecordingUnitOfWork _owner;
            public Tx(RecordingUnitOfWork owner) => _owner = owner;
            public Task CommitAsync(CancellationToken ct = default) { _owner.Committed++; return Task.CompletedTask; }
            public Task RollbackAsync(CancellationToken ct = default) => Task.CompletedTask;
            public ValueTask DisposeAsync() { _owner.TransactionOpen = false; return ValueTask.CompletedTask; }
        }
    }

    private static (FakeExamRecordRepository Repo, RecordingUnitOfWork Uow, ExamRecord Record) Fixture(bool withSnapshot = false)
    {
        var record = new ExamRecord
        {
            RecordID = Guid.NewGuid(),
            DivisionID = DivisionId,
            SessionID = Guid.NewGuid(),
            RecordCode = "REC-CONC-01",
            VariantCode = VariantCode,
            State = ExamRecordState.InProgress,
            SignSteps = new List<ExamRecordSignStep>()
        };
        if (withSnapshot)
        {
            record.SignSteps.Add(new ExamRecordSignStep
            {
                ID = Guid.NewGuid(), DivisionID = DivisionId, RecordID = record.RecordID,
                VariantCode = VariantCode, SWStep = 1, ItemGroupID = ItemGroupId,
                Status = ExamRecordSignStepStatus.Snapshot, SignedByEmployeeID = 1274
            });
        }
        var repo = new FakeExamRecordRepository(record);
        return (repo, new RecordingUnitOfWork(), record);
    }

    private static SignExamSectionHandler Signer(FakeExamRecordRepository repo, RecordingUnitOfWork uow)
    {
        var map = new FakeSignStepMapRepository();
        map.Steps.Add(new SignStepMap
        {
            ID = Guid.NewGuid(), DivisionID = DivisionId, VariantCode = VariantCode,
            SWStep = 1, ItemGroupID = ItemGroupId, StepName = "Bước 1", SignTitle = "Bước 1",
            SWRoleID = 45, SignType = 1, SLType = 2, SearchPattern = "##{S1}##", IsActive = true
        });
        var cert = new FakeCertificateGateway();
        cert.WithCertificate.Add("NV001");
        var his = new FakeSignRoleEmployeesHisClient().Add(45, 1274, "NV001", "BS A");
        return new SignExamSectionHandler(repo, map, cert, his, uow, new FakeAuditRepository());
    }

    private static SignExamSectionCommand SignCommand(Guid recordId) => new(
        DivisionId, recordId, ItemGroupId, EmployeeId: 1274, EmployeeCode: "NV001",
        EmployeeName: "BS A", ActorKind.Employee, Credential: "Bearer t", TraceId: "trace");

    private static CancelExamSectionSignCommand CancelCommand(Guid recordId)
        => new(DivisionId, recordId, ItemGroupId, EmployeeId: 1274, ActorKind.Employee);

    [Fact]
    public async Task Ky_muc_kham_khoa_dong_truoc_khi_doc_va_commit_trong_giao_dich()
    {
        var (repo, uow, record) = Fixture();

        var res = await Signer(repo, uow).HandleAsync(SignCommand(record.RecordID));

        Assert.True(res.IsSuccess);
        Assert.Equal(1, uow.Begun);
        Assert.Equal(new[] { "Lock", "GetForUpdate" }, repo.Calls);
        Assert.True(uow.SavedInsideTransaction);
        Assert.Equal(1, uow.Committed);
    }

    [Fact]
    public async Task Ky_muc_kham_UniqueConflict_la_luot_ky_dong_thoi_khong_phai_thanh_cong()
    {
        var (repo, uow, record) = Fixture();
        uow.NextOutcome = PersistenceSaveOutcome.UniqueConflict;

        var res = await Signer(repo, uow).HandleAsync(SignCommand(record.RecordID));

        Assert.False(res.IsSuccess);
        Assert.Equal(ApplicationFailureCode.InvalidState, res.Failure.Code);
        Assert.Contains("lượt khác", res.Failure.Message);
        Assert.Equal(0, uow.Committed);
        Assert.True(uow.Discarded);
    }

    /// <summary>LockRecordAsync khóa theo RecordID không lọc tenant — handler phải tự đối chiếu DivisionID.</summary>
    [Fact]
    public async Task Ky_muc_kham_ho_so_cua_tenant_khac_thi_NotFound_khong_mo_snapshot()
    {
        var (repo, uow, record) = Fixture();

        var res = await Signer(repo, uow).HandleAsync(SignCommand(record.RecordID) with { DivisionId = "TENANT-KHAC" });

        Assert.False(res.IsSuccess);
        Assert.Equal(ApplicationFailureCode.NotFound, res.Failure.Code);
        Assert.Equal(0, uow.Saves);
        Assert.Empty(record.SignSteps);
    }

    [Fact]
    public async Task Huy_ky_khoa_dong_truoc_khi_doc_va_commit_trong_giao_dich()
    {
        var (repo, uow, record) = Fixture(withSnapshot: true);

        var res = await new CancelExamSectionSignHandler(repo, uow, new FakeAuditRepository())
            .HandleAsync(CancelCommand(record.RecordID));

        Assert.True(res.IsSuccess);
        Assert.Equal(1, uow.Begun);
        Assert.Equal(new[] { "Lock", "GetForUpdate" }, repo.Calls);
        Assert.True(uow.SavedInsideTransaction);
        Assert.Equal(1, uow.Committed);
    }

    [Fact]
    public async Task Huy_ky_UniqueConflict_thi_tra_InvalidState_khong_commit()
    {
        var (repo, uow, record) = Fixture(withSnapshot: true);
        uow.NextOutcome = PersistenceSaveOutcome.UniqueConflict;

        var res = await new CancelExamSectionSignHandler(repo, uow, new FakeAuditRepository())
            .HandleAsync(CancelCommand(record.RecordID));

        Assert.False(res.IsSuccess);
        Assert.Equal(ApplicationFailureCode.InvalidState, res.Failure.Code);
        Assert.Equal(0, uow.Committed);
        Assert.True(uow.Discarded);
    }

    [Fact]
    public async Task Huy_ky_ho_so_cua_tenant_khac_thi_NotFound()
    {
        var (repo, uow, record) = Fixture(withSnapshot: true);

        var res = await new CancelExamSectionSignHandler(repo, uow, new FakeAuditRepository())
            .HandleAsync(CancelCommand(record.RecordID) with { DivisionId = "TENANT-KHAC" });

        Assert.False(res.IsSuccess);
        Assert.Equal(ApplicationFailureCode.NotFound, res.Failure.Code);
        Assert.Single(record.SignSteps);
    }
}
