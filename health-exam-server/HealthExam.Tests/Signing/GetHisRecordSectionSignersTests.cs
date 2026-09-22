using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HealthExam.Application.Common;
using HealthExam.Application.His;
using HealthExam.Domain.Common;
using HealthExam.Domain.ExamForms;
using HealthExam.Domain.ExamRecords;
using HealthExam.Infrastructure.Persistence.Repositories;
using Xunit;

namespace HealthExam.Tests.Signing;

/// <summary>
/// GET section mang sẵn danh sách người ký của bước (PROJ-2374) để FE vẽ dropdown "Người xác
/// nhận" mà không gọi thêm — chỉ khi bước chưa ký, vì đã ký thì hộp ký không mở và GetCodeList
/// amount=0 trả cả bệnh viện. Kèm tên người đã ký để dòng meta không phải in mã.
/// </summary>
public class GetHisRecordSectionSignersTests
{
    private const string DivisionId = "DIV01";
    private const string VariantCode = "KSK06-18T";
    private const int ItemGroupId = 101;

    [Fact]
    public async Task Buoc_chua_ky_thi_tra_danh_sach_nguoi_ky_theo_SWRoleID()
    {
        var (db, record, map) = Seed();
        var his = new FakeSignRoleEmployeesHisClient()
            .Add(45, 40, "40", "BS. Nguyễn Văn An")
            .Add(45, 41, "41", "BS. Trần Thị Bình")
            .Add(99, 77, "77", "Khác vai trò");
        SeedSnapshot(db, record, ExamRecordSignStepStatus.InProgress);

        var res = await Handler(db, map, his).HandleAsync(Query(record.RecordID));

        Assert.True(res.IsSuccess);
        Assert.Equal(new[] { 45L }, his.RequestedRoleIds);
        Assert.Equal(new[] { 40L, 41L }, res.Value.Signers.Select(s => s.EmployeeID));
        Assert.Null(res.Value.CurrentStepSignedByEmployeeName);
    }

    [Fact]
    public async Task Buoc_da_ky_thi_khong_goi_HIS_va_tra_ten_nguoi_ky()
    {
        var (db, record, map) = Seed();
        var his = new FakeSignRoleEmployeesHisClient().Add(45, 41, "41", "BS. Trần Thị Bình");
        SeedSnapshot(db, record, ExamRecordSignStepStatus.Signed);

        var res = await Handler(db, map, his).HandleAsync(Query(record.RecordID));

        Assert.True(res.IsSuccess);
        Assert.Empty(his.RequestedRoleIds);
        Assert.Empty(res.Value.Signers);
        Assert.Equal("BS. Trần Thị Bình", res.Value.CurrentStepSignedByEmployeeName);
        Assert.Equal(41, res.Value.CurrentStepSignedByEmployeeID);
    }

    /// <summary>HIS lỗi không được làm hỏng màn khám: danh sách rỗng, request vẫn thành công.</summary>
    [Fact]
    public async Task HIS_loi_thi_Signers_rong_va_van_thanh_cong()
    {
        var (db, record, map) = Seed();
        var his = new FakeSignRoleEmployeesHisClient { Fail = true };

        var res = await Handler(db, map, his).HandleAsync(Query(record.RecordID));

        Assert.True(res.IsSuccess);
        Assert.Empty(res.Value.Signers);
    }

    [Fact]
    public async Task Muc_khong_co_buoc_ky_thi_khong_goi_HIS_va_Signers_rong()
    {
        var (db, record, _) = Seed();
        var emptyMap = new FakeSignStepMapRepository();
        var his = new FakeSignRoleEmployeesHisClient().Add(45, 40, "40", "BS. Nguyễn Văn An");

        var res = await Handler(db, emptyMap, his).HandleAsync(Query(record.RecordID));

        Assert.True(res.IsSuccess);
        Assert.Empty(res.Value.Signers);
        Assert.Empty(his.RequestedRoleIds);
    }

    private static GetHisRecordSectionQuery Query(Guid recordId)
        => new(DivisionId, recordId, ItemGroupId, "Bearer t", "trace");

    private static void SeedSnapshot(InMemoryTestDb db, ExamRecord record, string status)
    {
        var signed = status == ExamRecordSignStepStatus.Signed;
        db.Db.ExamRecordSignSteps.Add(new ExamRecordSignStep
        {
            ID = Guid.NewGuid(),
            DivisionID = DivisionId, RecordID = record.RecordID, VariantCode = VariantCode,
            ItemGroupID = ItemGroupId, SWStep = 1, StepName = "Khám thể lực", SWRoleID = 45,
            Status = status,
            SignedByEmployeeID = signed ? 41 : null,
            SignedByEmployeeCode = signed ? "41" : "",
            SignedByEmployeeName = signed ? "BS. Trần Thị Bình" : "",
            SignedAt = signed ? DateTime.UtcNow : null
        });
        db.Db.SaveChanges();
        db.Db.ChangeTracker.Clear();
    }

    private static (InMemoryTestDb Db, ExamRecord Record, FakeSignStepMapRepository Map) Seed()
    {
        var db = new InMemoryTestDb();
        var session = db.SeedSession(divisionId: DivisionId);
        var record = db.SeedRecord(session.SessionID, ExamRecordState.InProgress, divisionId: DivisionId);
        db.SetVariantCode(record.RecordID, VariantCode);

        var map = new FakeSignStepMapRepository();
        map.Steps.Add(new SignStepMap
        {
            ID = Guid.NewGuid(), DivisionID = DivisionId, VariantCode = VariantCode,
            SWStep = 1, ItemGroupID = ItemGroupId, StepName = "Khám thể lực", SignTitle = "Khám thể lực",
            SWRoleID = 45, SignType = 1, SLType = 2, SearchPattern = "##{S1}##", IsActive = true
        });
        return (db, record, map);
    }

    private static GetHisRecordSectionHandler Handler(
        InMemoryTestDb db, FakeSignStepMapRepository map, FakeSignRoleEmployeesHisClient his)
        => new(new ExamRecordRepository(db.Db), new FakeDefinitionHandler(), his,
            new HealthExam.Infrastructure.Persistence.UnitOfWork(db.Db), icd10Handler: null, signStepMap: map);

    private sealed class FakeDefinitionHandler : IGetHisFormDefinitionHandler
    {
        public Task<ApplicationResult<HisFormDefinitionResult>> HandleAsync(
            GetHisFormDefinitionQuery query, CancellationToken ct = default)
            => Task.FromResult(ApplicationResult<HisFormDefinitionResult>.Success(new HisFormDefinitionResult(
                Guid.NewGuid(), "KSK-TREN18TUOI", "Mẫu", 1, "V1", false, true,
                $"[{{\"ItemGroupID\":{ItemGroupId}}}]", "[]")));
    }
}
