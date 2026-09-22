using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using HealthExam.Application.Common;
using HealthExam.Application.ExamRecords;
using HealthExam.Application.His;
using HealthExam.Application.Integrations;
using HealthExam.Application.Patients;
using HealthExam.Application.RegistrationForms;
using HealthExam.Application.Signing;
using HealthExam.Domain.Common;
using HealthExam.Domain.ExamForms;
using HealthExam.Domain.ExamRecords;
using HealthExam.Infrastructure.Persistence.Repositories;
using HealthExam.Tests.Application;
using Xunit;

namespace HealthExam.Tests.Signing;

/// <summary>
/// Spec §2/§5.2/§13: mục khám bị khóa ⇔ tồn tại snapshot ký; hồ sơ <c>Signed</c> là bất biến.
/// Khóa phải nằm ở SERVER — FE tắt nút chỉ là tiện ích, không phải chốt chặn.
///
/// Các fake HIS ở đây NÉM lỗi thay vì trả kết quả: guard phải ngắt TRƯỚC khi handler gọi HIS
/// lấy định nghĩa biểu mẫu hay tạo lượt tiếp nhận, nếu không mục đã khóa vẫn sinh lưu lượng HIS
/// (thậm chí tạo admission) rồi mới bị từ chối.
/// </summary>
public class SectionLockTests
{
    private const string DivisionId = "DIV01";
    private const string VariantCode = "KSK06-18T";
    private const int SignedItemGroupId = 101;
    private const int OtherItemGroupId = 102;

    private static SignStepMap Step(int swStep, int? itemGroupId, long roleId = 45, bool conclusion = false) => new()
    {
        ID = Guid.NewGuid(), DivisionID = DivisionId, VariantCode = VariantCode,
        SWStep = swStep, ItemGroupID = itemGroupId, StepName = $"Bước {swStep}",
        SignTitle = $"Bước {swStep}", SWRoleID = roleId, SignType = 1, SLType = 2,
        SearchPattern = $"##{{S{swStep}}}##", IsConclusionStep = conclusion, IsActive = true
    };

    private static async Task<(InMemoryTestDb Db, ExamRecord Record)> FixtureWithSignedSection()
    {
        var db = new InMemoryTestDb();
        var session = db.SeedSession(divisionId: DivisionId);
        var record = db.SeedRecord(session.SessionID, ExamRecordState.InProgress, divisionId: DivisionId);
        db.SetVariantCode(record.RecordID, VariantCode);

        var map = new FakeSignStepMapRepository();
        map.Steps.Add(Step(1, SignedItemGroupId));
        map.Steps.Add(Step(2, OtherItemGroupId));
        map.Steps.Add(Step(3, null, conclusion: true));
        var cert = new FakeCertificateGateway();
        cert.WithCertificate.Add("NV001");

        var his = new FakeSignRoleEmployeesHisClient().Add(45, 1274, "NV001", "BS A");
        var signed = await db.SignExamSection(map, cert, his).HandleAsync(new SignExamSectionCommand(
            DivisionId, record.RecordID, SignedItemGroupId, EmployeeId: 1274, EmployeeCode: "NV001",
            EmployeeName: "BS A", ActorKind.Employee, Credential: "Bearer t", TraceId: "trace"));
        Assert.True(signed.IsSuccess);
        db.Db.ChangeTracker.Clear();
        return (db, record);
    }

    private static SaveHisFormSectionHandler HisSectionSaver(InMemoryTestDb db, IGetHisFormDefinitionHandler def = null)
        => new(
            new ExamRecordRepository(db.Db),
            new FakeHisEmrClientForEndpoint(HisClientResult<IReadOnlyList<SignRoleEmployee>>.Success(Array.Empty<SignRoleEmployee>())),
            def ?? new ThrowingDefinitionHandler(),
            new ThrowingEnsureHisAdmission(),
            new ThrowingRecordSectionHandler(),
            new HealthExam.Infrastructure.Persistence.UnitOfWork(db.Db));

    private static SaveHisFormSectionCommand SaveHisCommand(Guid recordId, int itemGroupId) => new(
        DivisionId, recordId, itemGroupId, new HisFormSectionSaveRequest(Array.Empty<HisFormSectionFieldValue>()), "Bearer t", "trace",
        actorId: "1274", actorKind: ActorKind.Employee);

    [Fact]
    public async Task Muc_da_ky_thi_khong_luu_duoc_noi_dung()
    {
        var (db, record) = await FixtureWithSignedSection();

        var res = await HisSectionSaver(db).HandleAsync(SaveHisCommand(record.RecordID, SignedItemGroupId));

        Assert.False(res.IsSuccess);
        Assert.Equal(ApplicationFailureCode.InvalidState, res.Failure.Code);
        Assert.Contains("hủy ký", res.Failure.Message);
    }

    /// <summary>
    /// Khóa theo TỪNG mục: ký mục 101 không được khóa lây sang mục 102. Guard cho qua thì handler
    /// đi tiếp tới bước lấy định nghĩa biểu mẫu — fake ở đó trả lỗi đánh dấu để chốt "đã đi qua guard".
    /// </summary>
    [Fact]
    public async Task Muc_khac_chua_ky_van_luu_duoc()
    {
        var (db, record) = await FixtureWithSignedSection();
        var sentinel = new SentinelDefinitionHandler();

        var res = await HisSectionSaver(db, sentinel).HandleAsync(SaveHisCommand(record.RecordID, OtherItemGroupId));

        Assert.False(res.IsSuccess);
        Assert.Equal(ApplicationFailureCode.HisBadGateway, res.Failure.Code);
        Assert.Equal(SentinelDefinitionHandler.Message, res.Failure.Message);
        Assert.Equal(1, sentinel.Calls);
    }

    [Fact]
    public async Task Ho_so_da_ky_ket_luan_thi_khong_luu_duoc_bat_ky_muc_nao()
    {
        var (db, record) = await FixtureWithSignedSection();
        db.SetSignStatus(record.RecordID, ExamRecordSignStatus.Signed);

        var res = await HisSectionSaver(db).HandleAsync(SaveHisCommand(record.RecordID, OtherItemGroupId));

        Assert.False(res.IsSuccess);
        Assert.Equal(ApplicationFailureCode.InvalidState, res.Failure.Code);
        Assert.Contains("ký kết luận", res.Failure.Message);
    }

    [Fact]
    public async Task Ho_so_da_ky_ket_luan_thi_khong_luu_duoc_phieu_dang_ky()
    {
        var (db, record) = await FixtureWithSignedSection();
        db.SetSignStatus(record.RecordID, ExamRecordSignStatus.Signed);

        var handler = new SaveRegistrationFormSectionHandler(
            new RegistrationFormRepository(db.Db),
            new FakeHisEmrClientForEndpoint(HisClientResult<IReadOnlyList<SignRoleEmployee>>.Success(Array.Empty<SignRoleEmployee>())),
            new ThrowingDefinitionCache(),
            new PlainCredentialOptions(),
            new HealthExam.Infrastructure.Persistence.UnitOfWork(db.Db),
            new ThrowingGetRegistrationFormHandler(),
            new ThrowingEnsureHisAdmission());

        var res = await handler.HandleAsync(new SaveRegistrationFormSectionCommand(
            DivisionId, record.RecordID, "HISTORY", new RegistrationSectionSaveRequest(), "1274", ActorKind.Employee));

        Assert.False(res.IsSuccess);
        Assert.Equal(ApplicationFailureCode.InvalidState, res.Failure.Code);
        Assert.Contains("ký kết luận", res.Failure.Message);
    }

    [Fact]
    public async Task Ho_so_da_ky_ket_luan_thi_khong_sua_duoc_hanh_chinh()
    {
        var recordRepo = new FakeExamRecordRepository();
        var uow = new RecordFakeUnitOfWork();
        var recordId = Guid.NewGuid();
        recordRepo.Records[recordId] = new ExamRecord
        {
            RecordID = recordId,
            DivisionID = DivisionId,
            SessionID = Guid.NewGuid(),
            RecordCode = "REC-LOCK-01",
            VariantCode = "DTK_01",
            State = ExamRecordState.InProgress,
            SignStatus = ExamRecordSignStatus.Signed
        };
        var handler = new UpdateExamRecordHandler(
            recordRepo, new FakeSessionRepoForRecordTests(), uow, new FakeAuditRepository(),
            new PatientRegistrationWriter(new FakePatientRepository(), uow, new FixedClock()));

        var res = await handler.HandleAsync(new UpdateExamRecordCommand(
            DivisionId: DivisionId, RecordId: recordId, ActorId: "1274", ActorKind: ActorKind.Employee,
            FullName: "Tên mới", VariantCode: "DTK_01"));

        Assert.False(res.IsSuccess);
        Assert.Equal(ApplicationFailureCode.InvalidState, res.Failure.Code);
        Assert.Contains("ký kết luận", res.Failure.Message);
    }

    // ───── fakes: ném lỗi = "handler không được đi tới đây" ─────

    private sealed class ThrowingDefinitionHandler : IGetHisFormDefinitionHandler
    {
        public Task<ApplicationResult<HisFormDefinitionResult>> HandleAsync(
            GetHisFormDefinitionQuery query, CancellationToken ct = default)
            => throw new InvalidOperationException("Guard phải ngắt trước khi gọi HIS lấy định nghĩa biểu mẫu");
    }

    private sealed class SentinelDefinitionHandler : IGetHisFormDefinitionHandler
    {
        public const string Message = "sentinel: đã đi qua guard khóa mục";
        public int Calls { get; private set; }

        public Task<ApplicationResult<HisFormDefinitionResult>> HandleAsync(
            GetHisFormDefinitionQuery query, CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult(ApplicationResult<HisFormDefinitionResult>.Fail(
                ApplicationFailureCode.HisBadGateway, Message));
        }
    }

    private sealed class ThrowingEnsureHisAdmission : IEnsureHisAdmission
    {
        public Task<ApplicationResult<long>> HandleAsync(EnsureHisAdmissionCommand command, CancellationToken ct = default)
            => throw new InvalidOperationException("Guard phải ngắt trước khi tạo lượt tiếp nhận HIS");
    }

    private sealed class ThrowingRecordSectionHandler : IGetHisRecordSectionHandler
    {
        public Task<ApplicationResult<HisRecordSectionResult>> HandleAsync(
            GetHisRecordSectionQuery query, CancellationToken ct = default)
            => throw new InvalidOperationException("Guard phải ngắt trước khi đọc lại mục khám");
    }

    private sealed class ThrowingDefinitionCache : IHisFormDefinitionCache
    {
        public Task<HisFormDefinitionResult> GetOrCreateAsync(
            string divisionId, string templateCode,
            Func<CancellationToken, Task<HisFormDefinitionResult>> factory, CancellationToken ct = default)
            => throw new InvalidOperationException("Guard phải ngắt trước khi đọc định nghĩa biểu mẫu");
    }

    private sealed class ThrowingGetRegistrationFormHandler : IGetRegistrationFormHandler
    {
        public Task<ApplicationResult<RegistrationRecordForm>> HandleAsync(
            GetRegistrationFormQuery query, CancellationToken ct = default)
            => throw new InvalidOperationException("Guard phải ngắt trước khi đọc lại phiếu đăng ký");
    }

    private sealed class PlainCredentialOptions : IHisCredentialOptions
    {
        public string CredentialHeaderName => "Authorization";
    }
}
