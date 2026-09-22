using System.Net;
using System.Text;
using HealthExam.Infrastructure.Persistence;
using HealthExam.Domain.Common;
using HealthExam.Domain.ExamSessions;
using HealthExam.Domain.ExamRecords;
using HealthExam.Domain.Imports;
using HealthExam.Domain.Paraclinical;
using HealthExam.Domain.Webhooks;
using HealthExam.Domain.Integrations;
using HealthExam.Domain.Catalogs;
using HealthExam.Domain.Patients;
using HealthExam.Infrastructure.Persistence.Legacy;
using UnitOfWork = HealthExam.Infrastructure.Persistence.Legacy.UnitOfWork;
using IUnitOfWork = HealthExam.Infrastructure.Persistence.Legacy.IUnitOfWork;
using HealthExam.API.Contracts;
using HealthExam.API.Middlewares;
using HealthExam.Application.Common;
using HealthExam.Server.Service;
using HealthExam.Application.Catalogs;
using HealthExam.Application.ExamRecords;
using HealthExam.Application.ExamSessions;
using HealthExam.Application.Paraclinical;
using HealthExam.Application.Webhooks;
using HealthExam.Infrastructure.Integrations.FormServer;
using HealthExam.Infrastructure.Integrations.Ris;
using HealthExam.Infrastructure.Persistence.Repositories;
using HealthExam.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;

namespace HealthExam.Tests;

/// <summary>
/// Bộ đồ nghề dựng tầng service trên một DB trong bộ nhớ — dùng cho test nghiệp vụ đóng/mở đợt.
///
/// Vì sao không dùng mock cho IUnitOfWork: thứ cần chốt ở đây là DÒNG AUDIT có thật nằm cùng
/// một SaveChanges với thay đổi trạng thái. Mock repository thì "đã ghi audit" chỉ còn là
/// "đã gọi hàm Add", tức là test lại chính cái mock chứ không phải hành vi.
///
/// ⚠️ GIỚI HẠN CỦA PROVIDER IN-MEMORY — đọc trước khi thêm test vào đây:
/// - KHÔNG có chỉ mục UNIQUE (kể cả loại có lọc như UX_HEX_Record_Session_Identity), nên
///   ĐỪNG viết test trông chờ DB chặn trùng. Chốt chặn trùng thật nằm ở PostgreSQL và ở
///   nhánh bắt DbUpdateException trong ExamRecordService — cả hai chỉ kiểm được trên DB thật.
/// - KHÔNG có jsonb: cột Payload ở đây chỉ là chuỗi. Test đọc Payload bằng cách parse JSON,
///   không dùng toán tử jsonb.
/// - KHÔNG có transaction (BeginTransactionAsync bị bỏ qua) và không áp default value của DDL.
/// Mọi test trong bộ này vì thế chỉ chốt NHÁNH LOGIC trong service, không chốt ràng buộc DB.
/// </summary>
public sealed class InMemoryTestDb : IDisposable
{
    public HealthExamDbContext Db { get; }
    public IUnitOfWork Uow { get; }
    public FakeHealthExamContext Ctx { get; }
    public AuditService Audit { get; }
    public ExamSessionService Sessions { get; }
    public ExamRecordService Records { get; }
    public PortalCredentialService PortalCredentials { get; }
    public TestImportService Imports { get; }
    public ExamSessionProgressService SessionProgress { get; }
    public TestWebhookIngestService Webhooks { get; }
    public ProcessWebhookBatchHandler Processor { get; }
    public TestCatalogService Catalog { get; }
    public TestParaclinicalOrderService Orders { get; private set; }
    public MasterDataService MasterData { get; }
    public PatientService Patients { get; }

    /// <summary>Hàng đợi gửi vendor (P3b). Mặc định KHÔNG cấu hình RIS — đúng như mọi môi
    /// trường chưa nối vendor, và là hình mà phần lớn test của P3a cần.</summary>
    public IntegrationOutboxService Outbox { get; private set; }

    /// <summary>Gói vendor nhận được ở lượt gửi gần nhất, do bộ stub ghi lại.</summary>
    public RisStubHandler RisStub { get; private set; }

    /// <summary>Bản cấp số phiếu dùng cho test — bản thật đi thẳng xuống <c>nextval</c> của
    /// PostgreSQL, thứ provider in-memory không có.</summary>
    public CountingOrderNoAllocator OrderNos { get; } = new();

    /// <summary>Cấp số hiệu dòng trên dây vendor — bản thật cũng đi thẳng xuống nextval.</summary>
    public CountingVendorLineNoAllocator VendorLineNos { get; } = new();

    /// <summary>Bộ đếm dùng chung của cả webhook lẫn job đối soát — kiểm gate "metric tăng đúng 1".</summary>
    public InMemoryWebhookMetricsTracker Metrics { get; } = new();

    /// <param name="failWriteFor">
    /// Dựng một lỗi ở BƯỚC GHI cho những hồ sơ khớp vị từ này — dùng cho D2 (review lần 3
    /// MR !5). Mọi lỗi khác trong bộ test này đến từ bước ĐỌC (form-server trả 4030), mà hai
    /// bước hỏng theo hai cách khác hẳn nhau: hỏng lúc đọc thì change tracker còn sạch, hỏng
    /// lúc ghi thì nó giữ lại một thực thể bẩn và mọi SaveChanges sau đó gửi lại đúng câu
    /// UPDATE hỏng. Không dựng được nhánh này thì D2 không có test nào bắt.
    /// </param>
    public InMemoryTestDb(FakeHealthExamContext ctx = null, Func<ExamRecord, bool> failWriteFor = null)
    {
        Ctx = ctx ?? new FakeHealthExamContext();

        var options = new DbContextOptionsBuilder<HealthExamDbContext>()
            // Tên DB riêng cho từng thể hiện: các assert kiểu "đúng MỘT dòng audit" sẽ sai
            // ngay khi hai test chạy song song mà dùng chung kho.
            .UseInMemoryDatabase($"health-exam-test-{Guid.NewGuid():N}")
            // Chép đúng cấu hình DI thật (AddHealthExamRepositories). Nếu test chạy ở chế độ
            // tracking mặc định thì một service quên .AsTracking() vẫn lưu được ở đây nhưng
            // âm thầm mất thay đổi trên PostgreSQL — đúng loại lỗi test phải bắt.
            .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .AddInterceptors(new FailWriteInterceptor(failWriteFor))
            .Options;

        Db = new HealthExamDbContext(options);
        Uow = new UnitOfWork(Db);
        Audit = new AuditService(Uow, Ctx);
        Sessions = new ExamSessionService(Uow, Ctx, Audit);
        MasterData = new MasterDataService(Uow, Ctx);
        Patients = new PatientService(Uow, Ctx);
        Records = new ExamRecordService(Uow, Ctx, Sessions, MasterData, Audit, patients: Patients);
        PortalCredentials = new PortalCredentialService(Uow, Ctx);
        Imports = new TestImportService(Db, Ctx);
        SessionProgress = new ExamSessionProgressService(Uow, Ctx, Sessions);
        var webhookInboxRepo = new WebhookInboxRepository(Db);
        var examRecordRepo = new ExamRecordRepository(Db);
        var paraclinicalRepo = new ParaclinicalRepository(Db);
        var uowClean = new HealthExam.Infrastructure.Persistence.UnitOfWork(Db);
        var auditClean = new HealthExam.Infrastructure.Persistence.AuditRepository(Db);
        var clock = new HealthExam.Application.Common.SystemClock();

        var ingestHandler = new IngestWebhookHandler(webhookInboxRepo, uowClean, Metrics);
        var requeueHandler = new RequeueWebhooksHandler(webhookInboxRepo, uowClean);
        var backfillHandler = new BackfillScanResultsHandler(webhookInboxRepo, uowClean, clock);

        Webhooks = new TestWebhookIngestService(ingestHandler, requeueHandler, backfillHandler, Ctx);
        Processor = new ProcessWebhookBatchHandler(
            webhookInboxRepo, examRecordRepo, paraclinicalRepo, uowClean, auditClean, Metrics, clock);
        Catalog = new TestCatalogService(new CatalogRepository(Db), Ctx);
        UseRis(new RisOptions());
    }

    /// <summary>
    /// Đấu (lại) hàng đợi gửi vendor vào một cấu hình RIS và một bộ stub.
    ///
    /// Gọi lại được giữa chừng vì <see cref="TestParaclinicalOrderService"/> giữ tham chiếu tới
    /// hàng đợi (đường huỷ xếp gói CANCELLED), nên đổi cấu hình phải dựng lại cả hai — dựng
    /// lẻ một cái là để hai đường ghi nhìn thấy hai cấu hình khác nhau.
    /// </summary>
    public InMemoryTestDb UseRis(RisOptions options, RisStubHandler stub = null)
    {
        RisStub = stub ?? new RisStubHandler();

        var paraclinicalRepo = new ParaclinicalRepository(Db);
        var recordRepo = new ExamRecordRepository(Db);
        var sessionRepo = new ExamSessionRepository(Db);
        var outboxRepo = new IntegrationOutboxRepository(Db);
        var uow = new HealthExam.Infrastructure.Persistence.UnitOfWork(Db);
        var audit = new HealthExam.Infrastructure.Persistence.AuditRepository(Db);
        var clock = new HealthExam.Application.Common.SystemClock();
        var risPayloadBuilder = new RisPayloadBuilder(options);
        var risClient = new RisClient(new HttpClient(RisStub), options, NullLogger<RisClient>.Instance);
        var outboxDispatcher = new ParaclinicalOutboxDispatcher(outboxRepo, risPayloadBuilder, audit, NullLogger<ParaclinicalOutboxDispatcher>.Instance);

        var dispatchHandler = new DispatchOrderHandler(
            paraclinicalRepo, recordRepo, outboxRepo, VendorLineNos, risPayloadBuilder, uow, audit, clock);

        Outbox = new IntegrationOutboxService(
            dispatchHandler, outboxDispatcher, risClient, paraclinicalRepo, audit, Db, Ctx);

        var getRecordOrdersHandler = new GetRecordOrdersHandler(paraclinicalRepo, recordRepo);
        var createOrdersHandler = new CreateOrdersHandler(paraclinicalRepo, recordRepo, sessionRepo, OrderNos, uow, audit, clock);
        var createOrdersFromPackageHandler = new CreateOrdersFromPackageHandler(paraclinicalRepo, recordRepo, sessionRepo, OrderNos, uow, audit, clock);
        var getOrderHandler = new GetOrderHandler(paraclinicalRepo);
        var cancelOrderHandler = new CancelOrderHandler(paraclinicalRepo, recordRepo, sessionRepo, outboxDispatcher, uow, audit, clock);
        var changeOrderStateHandler = new ChangeOrderStateHandler(paraclinicalRepo, recordRepo, sessionRepo, uow, audit, clock);

        Orders = new TestParaclinicalOrderService(
            getRecordOrdersHandler,
            createOrdersHandler,
            createOrdersFromPackageHandler,
            getOrderHandler,
            cancelOrderHandler,
            changeOrderStateHandler,
            Ctx);
        return this;
    }

    /// <summary>
    /// Một lượt gửi trọn vẹn: gọi vendor rồi áp hệ quả — hai chặng mà worker cố ý tách ra hai
    /// transaction (xem <see cref="IntegrationOutboxWorker.RunOneAsync"/>).
    ///
    /// Gộp lại ở ĐÂY là đúng, vì thứ những test này chốt là HỆ QUẢ NGHIỆP VỤ của một lượt
    /// gửi. Thứ tự transaction và chốt thứ tự hàng đợi thì không kiểm được bằng provider
    /// in-memory (nó ném ở FromSqlRaw) — chúng nằm ở <c>OutboxQueuePostgresTests</c>.
    /// </summary>
    public async Task<OutboxSendResult> SendOnceAsync(
        IntegrationOutbox row, CancellationToken ct = default)
        => await Outbox.ApplyAsync(row, await Outbox.CallVendorAsync(row, ct), ct);

    /// <summary>Đường về của vendor — dựng riêng vì nó không dùng HttpClient nào.</summary>
    public VendorStatusService Vendor()
    {
        var paraclinicalRepo = new ParaclinicalRepository(Db);
        var uow = new HealthExam.Infrastructure.Persistence.UnitOfWork(Db);
        var audit = new HealthExam.Infrastructure.Persistence.AuditRepository(Db);
        var clock = new HealthExam.Application.Common.SystemClock();
        var handler = new UpdateVendorStatusHandler(paraclinicalRepo, uow, audit, clock);
        return new VendorStatusService(handler, Ctx);
    }

    /// <summary>
    /// Job đối soát kéo, đấu vào một form-server giả.
    ///
    /// Dựng bằng hàm chứ không phải thuộc tính vì mỗi test cần một phản hồi khác nhau từ
    /// form-server, mà FormServerClient nhận HttpClient lúc khởi tạo.
    /// </summary>
    public TestReconciliationService Reconciliation(HttpMessageHandler formServer)
    {
        var examRecordRepo = new ExamRecordRepository(Db);
        var uowClean = new HealthExam.Infrastructure.Persistence.UnitOfWork(Db);
        var auditClean = new HealthExam.Infrastructure.Persistence.AuditRepository(Db);
        var clock = new HealthExam.Application.Common.SystemClock();
        var client = new FormServerClient(
            new HttpClient(formServer),
            new FormServerOptions { BaseUrl = "https://form.test", DocTypeId = 990001 },
            Ctx,
            NullLogger<FormServerClient>.Instance);

        var handler = new ReconcileProgressHandler(
            examRecordRepo, client, uowClean, auditClean, Metrics, clock);

        return new TestReconciliationService(handler);
    }

    /// <summary>
    /// Nạp sẵn một đợt khám ở trạng thái bất kỳ.
    ///
    /// Ghi thẳng qua DbContext chứ KHÔNG qua CreateAsync: service luôn tạo đợt ở trạng thái
    /// Nháp, nên không có đường nào dựng được đợt "Đã đóng"/"Hủy" để test nhánh từ chối.
    /// </summary>
    public ExamSession SeedSession(
        ExamSessionState state = ExamSessionState.Open,
        string sessionCode = "DK-2026-001",
        string divisionId = null,
        DateOnly? examDate = null)
    {
        var session = new ExamSession
        {
            SessionID = Guid.NewGuid(),
            DivisionID = divisionId ?? Ctx.DivisionId,
            SessionCode = sessionCode,
            SessionName = "Đợt khám công ty A",
            ExamDate = examDate ?? new DateOnly(2026, 8, 26),
            ExamPlace = "Hội trường tầng 3",
            VariantCode = "DTK_01",
            State = state
        };

        Db.ExamSessions.Add(session);
        Db.SaveChanges();
        // Xoá vết theo dõi để mỗi test bắt đầu như một request mới: còn bám thì service đọc
        // lại được thực thể cũ trong bộ nhớ và nhánh "không tìm thấy" không bao giờ chạy.
        Db.ChangeTracker.Clear();
        return session;
    }

    /// <summary>Nạp sẵn một hồ sơ trong đợt — dùng cho các nhánh sửa/chốt đăng ký.</summary>
    public ExamRecord SeedRecord(
        Guid sessionId,
        ExamRecordState state = ExamRecordState.NotRegistered,
        string recordCode = "DK-2026-001-0001",
        string patientCode = "",
        string identityNumber = "",
        string insuranceNumber = "",
        string fullName = "Nguyễn Văn A",
        short genderId = 0,
        string divisionId = null,
        DateTime? createdDate = null,
        Guid? submissionId = null,
        DateTime? lastEventAt = null,
        short progressDone = 0,
        short progressTotal = 0,
        long? admissionId = null,
        Guid? hisEmrDataId = null,
        Guid? hisFormTemplateId = null,
        string hisSignStatus = null,
        DateTime? hisSignedAt = null,
        string hisSignedFilePath = null,
        Guid? profileLineageID = null)
    {
        var divId = divisionId ?? Ctx.DivisionId;
        // ProfileLineageID phải khác Guid.Empty y như dữ liệu thật: migration
        // 20260916092419_AddPatientProfileVersionNumbers backfill nó bằng COALESCE(root, chính nó),
        // và PatientRegistrationWriter luôn đặt. Fixture để trống thì mọi Patient dùng chung
        // lineage Guid.Empty và test gộp-theo-dòng sẽ xanh giả.
        var patientRefID = Guid.NewGuid();
        var patient = new Patient
        {
            PatientRefID = patientRefID,
            DivisionID = divId,
            FullName = fullName,
            PatientCode = patientCode,
            IdentityNumber = identityNumber,
            GenderID = genderId,
            ProfileLineageID = profileLineageID ?? patientRefID,
            CreatedDate = createdDate ?? new DateTime(2026, 8, 24, 0, 0, 0, DateTimeKind.Utc)
        };
        Db.Patients.Add(patient);

        PatientInsurance ins = null;
        if (!string.IsNullOrWhiteSpace(insuranceNumber))
        {
            ins = new PatientInsurance
            {
                InsuranceRefID = Guid.NewGuid(),
                DivisionID = divId,
                PatientRefID = patient.PatientRefID,
                InsuranceNumber = insuranceNumber,
                CreatedDate = createdDate ?? new DateTime(2026, 8, 24, 0, 0, 0, DateTimeKind.Utc)
            };
            Db.PatientInsurances.Add(ins);
        }

        var record = new ExamRecord
        {
            RecordID = Guid.NewGuid(),
            DivisionID = divId,
            SessionID = sessionId,
            AdmissionID = admissionId,
            RecordCode = recordCode,
            PatientRefID = patient.PatientRefID,
            InsuranceRefID = ins?.InsuranceRefID,
            VariantCode = "DTK_01",
            FormCode = ExamGroups.FormCodePrefix + "DTK_01",
            CreatedDate = createdDate ?? new DateTime(2026, 8, 24, 0, 0, 0, DateTimeKind.Utc),
            State = state,
            SubmissionID = submissionId,
            LastEventAt = lastEventAt,
            ProgressDone = progressDone,
            ProgressTotal = progressTotal,
            HisEmrDataID = hisEmrDataId,
            HisFormTemplateID = hisFormTemplateId,
            SignStatus = hisSignStatus ?? ExamRecordSignStatus.New,
            HisSignedAt = hisSignedAt,
            SignedFilePath = hisSignedFilePath
        };

        Db.ExamRecords.Add(record);
        Db.SaveChanges();
        Db.ChangeTracker.Clear();

        record.Patient = patient;
        record.Insurance = ins;
        return record;
    }

    /// <summary>
    /// Nạp sẵn một lô nạp Excel kèm các dòng — dùng cho nhánh chốt chặn của commit.
    ///
    /// ⚠️ KHÔNG dùng để test đường ghi hồ sơ của commit: ExamRecordService.CreateAsync cấp mã
    /// bằng một câu SQL thô trên DbConnection thật (UPDATE … RETURNING), thứ provider
    /// in-memory không có. Phần đó chốt bằng curl trên PostgreSQL thật.
    /// </summary>
    public ImportBatch SeedImportBatch(
        Guid sessionId,
        ImportBatchState state = ImportBatchState.Pending,
        DateTime? startedAt = null,
        params (int RowNo, bool IsValid, string Raw)[] rows)
    {
        var batch = new ImportBatch
        {
            BatchID = Guid.NewGuid(),
            DivisionID = Ctx.DivisionId,
            SessionID = sessionId,
            FileName = "DSKSK.xlsx",
            SheetName = "Sheet1",
            State = state,
            StartedAt = startedAt ?? DateTime.UtcNow,
            TotalRow = rows.Length,
            SuccessRow = rows.Count(r => r.IsValid),
            ErrorRow = rows.Count(r => !r.IsValid)
        };
        Db.ImportBatches.Add(batch);

        foreach (var (rowNo, isValid, raw) in rows)
            Db.ImportBatchRows.Add(new ImportBatchRow
            {
                BatchID = batch.BatchID,
                RowNo = rowNo,
                RawData = raw ?? "{}",
                IsValid = isValid,
                ErrorCode = isValid ? "" : ImportRowErrors.Required,
                ErrorMessage = isValid ? "" : "FullName: Bỏ trống (bắt buộc)"
            });

        Db.SaveChanges();
        Db.ChangeTracker.Clear();
        return batch;
    }

    /// <summary>
    /// Đặt mốc ModifiedDate của một lô — dùng để dựng "lô đang được ghi" và "lô mồ côi".
    /// Ngưỡng StaleCommitAfter đo theo mốc này chứ không theo StartedAt, đúng như câu SQL
    /// giành lô, nên test phải đặt được nó.
    /// </summary>
    public void TouchImportBatch(Guid batchId, DateTime modifiedDate)
    {
        var batch = Db.ImportBatches.AsTracking().Single(x => x.BatchID == batchId);
        batch.ModifiedDate = modifiedDate;
        Db.SaveChanges();
        Db.ChangeTracker.Clear();
    }

    /// <summary>Đặt kết quả PHA 2 lên một dòng: đã tạo hồ sơ, hoặc hỏng lúc ghi.</summary>
    public void MarkImportRow(Guid batchId, int rowNo, Guid? recordId = null, string errorCode = null)
    {
        var row = Db.ImportBatchRows.AsTracking().Single(x => x.BatchID == batchId && x.RowNo == rowNo);
        if (recordId.HasValue) row.RecordID = recordId;
        if (errorCode != null)
        {
            row.IsValid = false;
            row.ErrorCode = errorCode;
            row.ErrorMessage = $"{errorCode}: dựng cho test";
        }
        Db.SaveChanges();
        Db.ChangeTracker.Clear();
    }

    /// <summary>
    /// Đặt sẵn mốc giờ khám xong kèm cờ "do job ước lượng" — dựng nhánh §4.7 mà không phải
    /// chạy cả job đối soát.
    /// </summary>
    public void SetExamFinished(Guid recordId, DateTime value, bool estimated)
    {
        var record = Db.ExamRecords.AsTracking().Single(x => x.RecordID == recordId);
        record.ExamFinishedAt = value;
        record.ExamFinishedAtEstimated = estimated;
        Db.SaveChanges();
        Db.ChangeTracker.Clear();
    }

    /// <inheritdoc cref="SetExamFinished"/>
    public void SetExamStarted(Guid recordId, DateTime value, bool estimated)
    {
        var record = Db.ExamRecords.AsTracking().Single(x => x.RecordID == recordId);
        record.ExamStartedAt = value;
        record.ExamStartedAtEstimated = estimated;
        Db.SaveChanges();
        Db.ChangeTracker.Clear();
    }

    /// <summary>Đặt Loại sức khoẻ sẵn cho một hồ sơ — dựng nhánh "đã có giá trị, không được xoá".</summary>
    public void SetHealthClass(Guid recordId, string code)
    {
        var record = Db.ExamRecords.AsTracking().Single(x => x.RecordID == recordId);
        record.HealthClassCode = code;
        Db.SaveChanges();
        Db.ChangeTracker.Clear();
    }

    public void SetVariantCode(Guid recordId, string code)
    {
        var record = Db.ExamRecords.AsTracking().Single(x => x.RecordID == recordId);
        record.VariantCode = code;
        Db.SaveChanges();
        Db.ChangeTracker.Clear();
    }

    public void SetSignStatus(Guid recordId, string status)
    {
        var record = Db.ExamRecords.AsTracking().Single(x => x.RecordID == recordId);
        record.SignStatus = status;
        Db.SaveChanges();
        Db.ChangeTracker.Clear();
    }

    public void SetSignedFilePath(Guid recordId, string path)
    {
        var record = Db.ExamRecords.AsTracking().Single(x => x.RecordID == recordId);
        record.SignedFilePath = path;
        Db.SaveChanges();
        Db.ChangeTracker.Clear();
    }

    public HealthExam.Application.Signing.SignExamSectionHandler SignExamSection(
        HealthExam.Application.Signing.ISignStepMapRepository map,
        HealthExam.Application.Integrations.ICertificateGateway cert,
        HealthExam.Application.Integrations.IHisEmrClient his)
        => new(new ExamRecordRepository(Db), map, cert, his,
            new HealthExam.Infrastructure.Persistence.UnitOfWork(Db),
            new HealthExam.Infrastructure.Persistence.AuditRepository(Db));

    public HealthExam.Application.Signing.CancelExamSectionSignHandler CancelExamSectionSign()
        => new(new ExamRecordRepository(Db),
            new HealthExam.Infrastructure.Persistence.UnitOfWork(Db),
            new HealthExam.Infrastructure.Persistence.AuditRepository(Db));

    public HealthExam.Application.RegistrationForms.PreviewRegistrationFormPdfHandler PreviewConclusionPdf(
        HealthExam.Application.Integrations.IExamFileStore store,
        HealthExam.Application.Integrations.IHisEmrClient his)
        => new(new HealthExam.Infrastructure.Persistence.Repositories.RegistrationFormRepository(Db), his, store);

    public HealthExam.Application.Paraclinical.GetConclusionEligibilityHandler ConclusionEligibility(
        HealthExam.Application.Signing.ISignStepMapRepository map)
        => new(new ParaclinicalRepository(Db), new ExamRecordRepository(Db), map);

    public HealthExam.Application.Paraclinical.SignConclusionHandler SignConclusion(
        HealthExam.Application.Signing.ISignStepMapRepository map,
        HealthExam.Application.Integrations.ICertificateGateway cert,
        HealthExam.Application.Integrations.IPdfSigner signer,
        HealthExam.Application.Integrations.IExamFileStore store,
        HealthExam.Application.Integrations.IHisEmrClient his)
        => new(new ParaclinicalRepository(Db), new ExamRecordRepository(Db), map, cert,
            signer, store, his,
            new HealthExam.Infrastructure.Persistence.UnitOfWork(Db),
            new HealthExam.Infrastructure.Persistence.AuditRepository(Db));

    public HealthExam.Application.Paraclinical.CancelConclusionSignHandler CancelConclusionSign(
        HealthExam.Application.Signing.ISignStepMapRepository map)
        => new(new ExamRecordRepository(Db), map,
            new HealthExam.Infrastructure.Persistence.UnitOfWork(Db),
            new HealthExam.Infrastructure.Persistence.AuditRepository(Db));

    /// <summary>
    /// Điều kiện + lệnh ký kết luận, đọc toàn bộ từ bảng map và snapshot cục bộ. Mọi cổng ra
    /// ngoài (chứng thư, sign-server, MinIO, HIS render) đều là fake — truyền vào khi test cần
    /// quan sát hoặc lái chúng, bỏ trống thì lấy fake mặc định.
    /// </summary>
    public TestConclusionService Conclusion(
        HealthExam.Application.Signing.ISignStepMapRepository map = null,
        HealthExam.Application.Integrations.ICertificateGateway cert = null,
        HealthExam.Application.Integrations.IPdfSigner signer = null,
        HealthExam.Application.Integrations.IExamFileStore store = null,
        HealthExam.Application.Integrations.IHisEmrClient hisEmrClient = null)
    {
        map ??= new HealthExam.Tests.Signing.FakeSignStepMapRepository();
        var paraclinicalRepo = new ParaclinicalRepository(Db);
        var getEligibilityHandler = ConclusionEligibility(map);
        var signConclusionHandler = SignConclusion(
            map,
            cert ?? new HealthExam.Tests.Signing.FakeCertificateGateway(),
            signer ?? new HealthExam.Tests.Signing.FakePdfSigner(),
            store ?? new HealthExam.Tests.Signing.FakeExamFileStore(),
            hisEmrClient ?? new HealthExam.Tests.Signing.FakeRenderingHisClient());
        return new TestConclusionService(paraclinicalRepo, getEligibilityHandler, signConclusionHandler, Ctx);
    }

    /// <summary>
    /// Nạp một gói khám kèm dịch vụ — nguồn của danh mục 3 tầng VÀ của lệnh bung theo gói.
    /// </summary>
    public ExamPackage SeedPackage(
        string packageCode = "GOI-CB",
        bool isActive = true,
        string divisionId = null,
        params (long ServiceID, string Code, string Name, string Kind, string Group)[] services)
    {
        var package = new ExamPackage
        {
            PackageID = Guid.NewGuid(),
            DivisionID = divisionId ?? Ctx.DivisionId,
            PackageCode = packageCode,
            PackageName = "Gói khám cơ bản",
            IsActive = isActive
        };
        Db.ExamPackages.Add(package);

        var orderNo = 0;
        foreach (var (serviceId, code, name, kind, group) in services)
            Db.ExamPackageServices.Add(new ExamPackageService
            {
                PackageServiceID = Guid.NewGuid(),
                PackageID = package.PackageID,
                ServiceID = serviceId,
                ServiceCode = code,
                ServiceName = name,
                ParaclinicalKind = kind,
                ServiceGroupCode = group,
                Quantity = 1,
                OrderNo = ++orderNo,
                IsActive = true
            });

        Db.SaveChanges();
        Db.ChangeTracker.Clear();
        return package;
    }

    /// <summary>
    /// Nạp thẳng một phiếu chỉ định kèm dòng dịch vụ ở trạng thái bất kỳ.
    ///
    /// Ghi thẳng qua DbContext chứ KHÔNG qua service: service luôn tạo dòng ở "Chờ chỉ định",
    /// nên không có đường nào dựng được một hồ sơ "còn 2 dịch vụ chưa trả KQ" để test điều
    /// kiện (B).
    /// </summary>
    public ParaclinicalOrder SeedOrder(
        ExamRecord record,
        string orderNo = "CD0000000001",
        string kind = "XN",
        Guid? sourcePackageId = null,
        params (long ServiceID, string Code, ParaclinicalItemState State)[] items)
    {
        var order = new ParaclinicalOrder
        {
            OrderID = Guid.NewGuid(),
            DivisionID = record.DivisionID,
            RecordID = record.RecordID,
            SessionID = record.SessionID,
            OrderNo = orderNo,
            ParaclinicalKind = kind,
            SourcePackageID = sourcePackageId,
            OrderedAt = DateTime.UtcNow,
            TargetSystem = ParaclinicalTargets.None,
            IsActive = true
        };
        Db.ParaclinicalOrders.Add(order);

        foreach (var (serviceId, code, state) in items)
            Db.ParaclinicalOrderItems.Add(new ParaclinicalOrderItem
            {
                OrderItemID = Guid.NewGuid(),
                OrderID = order.OrderID,
                RecordID = record.RecordID,
                DivisionID = record.DivisionID,
                ServiceID = serviceId,
                ServiceCode = code,
                ServiceName = code,
                ServiceGroupCode = "XN_HUYETHOC",
                Quantity = 1,
                State = state,
                SourcePackageID = sourcePackageId,
                // Dòng đã có kết quả thì phải có mốc, nếu không test không phân biệt được
                // "đã trả KQ" với "vừa dựng sai".
                ResultAt = state == ParaclinicalItemState.Done ? DateTime.UtcNow : null,
                ResultSourceKind = state == ParaclinicalItemState.Done ? ParaclinicalResultSources.Manual : ""
            });

        Db.SaveChanges();
        Db.ChangeTracker.Clear();
        return order;
    }

    public MasterDataOption SeedMaster(
        string category,
        string code,
        string name,
        string parentCode = "",
        int orderNo = 1,
        bool isActive = true,
        string divisionId = null)
    {
        var entity = new MasterDataOption
        {
            OptionID = Guid.NewGuid(),
            DivisionID = divisionId ?? Ctx.DivisionId,
            Category = category,
            Code = code,
            Name = name,
            ParentCode = parentCode,
            OrderNo = orderNo,
            IsActive = isActive,
            CreatedDate = DateTime.UtcNow,
            CreatedBy = Ctx.ActorId,
            CreatedActorKind = Ctx.ActorKind,
            ModifiedDate = DateTime.UtcNow,
            ModifiedBy = Ctx.ActorId,
            ModifiedActorKind = Ctx.ActorKind
        };
        Db.MasterDataOptions.Add(entity);
        Db.SaveChanges();
        Db.ChangeTracker.Clear();
        return entity;
    }

    /// <summary>Đọc lại các dòng dịch vụ của một hồ sơ, theo mã dịch vụ.</summary>
    public List<ParaclinicalOrderItem> ItemsOf(Guid recordId)
    {
        Db.ChangeTracker.Clear();
        return Db.ParaclinicalOrderItems.AsNoTracking()
                 .Where(x => x.RecordID == recordId)
                 .OrderBy(x => x.ServiceCode)
                 .ToList();
    }

    /// <summary>Đặt mốc sự kiện gần nhất của hồ sơ — dựng nhánh chống-đến-muộn mà không phải
    /// chạy trước một sự kiện thật.</summary>
    public void SetLastEventAt(Guid recordId, DateTime value)
    {
        var record = Db.ExamRecords.AsTracking().Single(x => x.RecordID == recordId);
        record.LastEventAt = value;
        Db.SaveChanges();
        Db.ChangeTracker.Clear();
    }

    /// <summary>Toàn bộ nhật ký của một thực thể, theo đúng thứ tự ghi.</summary>
    public List<AuditLog> AuditRowsOf(Guid entityId)
        => Db.AuditLogs.AsNoTracking()
             .Where(x => x.EntityID == entityId)
             .OrderBy(x => x.AuditID)
             .ToList();

    /// <summary>Đọc lại hồ sơ từ kho, không lấy thực thể đang bám trong bộ nhớ.</summary>
    public ExamRecord RecordOf(Guid recordId)
    {
        Db.ChangeTracker.Clear();
        return Db.ExamRecords.AsNoTracking()
            .Include(x => x.Patient)
            .Include(x => x.Insurance)
            .Include(x => x.Employment)
            .Include(x => x.Relative)
            .Include(x => x.PatientTypeOption)
            .Include(x => x.PaymentSourceOption)
            .Include(x => x.ExamLocationOption)
            .Include(x => x.SignSteps)
            .First(x => x.RecordID == recordId);
    }

    /// <summary>Hàng hộp thư đọc lại từ kho, theo thứ tự nhận.</summary>
    public List<WebhookInbox> InboxRows()
    {
        Db.ChangeTracker.Clear();
        return Db.WebhookInboxes.AsNoTracking().OrderBy(x => x.InboxID).ToList();
    }

    /// <summary>Đọc lại trạng thái đợt từ kho, không lấy thực thể đang bám trong bộ nhớ.</summary>
    public ExamSessionState StateOf(Guid sessionId)
        => Db.ExamSessions.AsNoTracking().First(x => x.SessionID == sessionId).State;

    public static HealthExamDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<HealthExamDbContext>()
            .UseInMemoryDatabase($"health-exam-test-{Guid.NewGuid():N}")
            .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new HealthExamDbContext(options);
    }

    public void Dispose() => Db.Dispose();
}

/// <summary>
/// Chặn đúng BƯỚC GHI của một số hồ sơ — bản dựng trong bộ nhớ của cái trigger PostgreSQL mà
/// review lần 3 dùng để đo D2.
///
/// Ném từ <c>SavingChanges</c> chứ không phải từ service: chỗ hỏng thật nằm ở tầng DB (ràng
/// buộc, trigger, mất kết nối giữa chừng), và điều quan trọng cần dựng lại là hệ quả của nó
/// lên CHANGE TRACKER — thực thể bẩn ở lại sau một lượt lưu hỏng. Ném từ service thì thực thể
/// chưa kịp bẩn và test sẽ xanh cho một bản vá không chữa gì.
/// </summary>
internal sealed class FailWriteInterceptor : SaveChangesInterceptor
{
    private readonly Func<ExamRecord, bool> _shouldFail;

    public FailWriteInterceptor(Func<ExamRecord, bool> shouldFail) => _shouldFail = shouldFail;

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken ct = default)
    {
        if (_shouldFail != null && eventData.Context != null)
        {
            var poisoned = eventData.Context.ChangeTracker.Entries<ExamRecord>()
                .Any(e => e.State is EntityState.Modified or EntityState.Added && _shouldFail(e.Entity));

            if (poisoned)
                throw new DbUpdateException("Tầng DB từ chối lệnh ghi (dựng cho test D2)");
        }

        return base.SavingChangesAsync(eventData, result, ct);
    }
}

/// <summary>
/// Cấp số phiếu bằng bộ đếm trong bộ nhớ — bản dùng cho test.
///
/// Vẫn ghép mã qua <see cref="SequenceOrderNoAllocator.Compose"/> để quy ước "2 ký tự đầu rồi
/// thuần số" được kiểm bằng chính hàm mà bản thật dùng; nếu quy ước đổi mà test vẫn xanh nhờ
/// một hàm ghép riêng thì test đang canh sai thứ.
/// </summary>
/// <summary>
/// Bản đếm trong bộ nhớ của <see cref="IVendorLineNoAllocator"/>. Vẫn cấp số TĂNG DẦN và
/// liên tiếp để test kiểm được đúng thứ dây vendor nhìn thấy.
/// </summary>
public sealed class CountingVendorLineNoAllocator : IVendorLineNoAllocator
{
    private long _next;

    public Task<IReadOnlyList<long>> NextAsync(int count, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<long>>(
            Enumerable.Range(0, Math.Max(0, count)).Select(_ => Interlocked.Increment(ref _next)).ToList());
}

public sealed class CountingOrderNoAllocator : HealthExam.Application.Paraclinical.IOrderNoAllocator
{
    private long _next;

    public Task<string> NextAsync(CancellationToken ct = default)
        => Task.FromResult(HealthExam.Infrastructure.Persistence.SequenceOrderNoAllocator.Compose(Interlocked.Increment(ref _next)));
}

/// <summary>
/// RIS giả — PHÁT LẠI đúng hợp đồng dây VietRad, và GIỮ LẠI nguyên văn gói nhận được.
///
/// Đây là nửa "đi ra" của gate G-P3b-1: vòng đi-về ghi hình đầy đủ với stub cục bộ, đạt được
/// ngay mà không cần ai cấp quyền. Lời gọi THẬT (G-P3b-2) vẫn hoãn — không có tenant sandbox
/// nào trong cấu hình, và bắn phiếu vào RIS đang chạy của một bệnh viện là ghi dữ liệu thật
/// vào hệ production của bên thứ ba (docs/handoff/20260826-236-chot-p3-cls.md §4).
/// </summary>
public sealed class RisStubHandler : HttpMessageHandler
{
    private readonly Func<string, HttpResponseMessage> _reply;

    /// <summary>Mọi gói đã nhận, theo thứ tự — nguyên văn JSON trên dây.</summary>
    public List<string> Received { get; } = new();

    /// <summary>Đường dẫn của gói cuối — để chốt rằng ta gọi đúng /his/json/request.</summary>
    public string LastPath { get; private set; } = "";

    /// <summary>Header Authorization của gói cuối — để chốt Basic có thật.</summary>
    public string LastAuthorization { get; private set; } = "";

    public RisStubHandler(Func<string, HttpResponseMessage> reply = null)
        => _reply = reply ?? (_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"result\":\"OK\"}", Encoding.UTF8, "application/json")
        });

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var body = request.Content == null ? "" : await request.Content.ReadAsStringAsync(ct);
        Received.Add(body);
        LastPath = request.RequestUri?.AbsolutePath ?? "";
        LastAuthorization = request.Headers.Authorization?.ToString() ?? "";
        return _reply(body);
    }
}

public sealed class TestWebhookIngestService
{
    public const int DefaultRequeue = RequeueWebhooksHandler.DefaultRequeue;
    public const int MaxRequeue = RequeueWebhooksHandler.MaxRequeue;

    private readonly IIngestWebhookHandler _ingest;
    private readonly IRequeueWebhooksHandler _requeue;
    private readonly IBackfillScanResultsHandler _backfill;
    private readonly FakeHealthExamContext _ctx;

    public TestWebhookIngestService(
        IIngestWebhookHandler ingest,
        IRequeueWebhooksHandler requeue,
        IBackfillScanResultsHandler backfill,
        FakeHealthExamContext ctx)
    {
        _ingest = ingest;
        _requeue = requeue;
        _backfill = backfill;
        _ctx = ctx;
    }

    public async Task<WebhookAckResult> ReceiveAsync(
        FormWebhookEvent evt, string rawPayload, CancellationToken ct = default)
    {
        var cmd = new IngestWebhookCommand(
            evt?.EventID,
            evt?.Event,
            evt?.DivisionID,
            evt?.SubmissionID,
            evt?.HostRefType,
            evt?.HostRefID,
            evt?.SubjectID,
            evt?.OccurredAt,
            rawPayload,
            _ctx.TraceId,
            _ctx.DivisionId);

        var result = await _ingest.HandleAsync(cmd, ct);
        if (!result.IsSuccess)
        {
            throw HealthExamException.BadRequest(
                result.Failure.Message,
                payload: result.Failure.Payload);
        }

        return result.Value;
    }

    public async Task<WebhookRequeueResult> RequeueDeadLettersAsync(
        string eventId = null, int max = 100, CancellationToken ct = default)
    {
        var cmd = new RequeueWebhooksCommand(_ctx.DivisionId, eventId, max);
        var result = await _requeue.HandleAsync(cmd, ct);
        if (!result.IsSuccess)
            throw new InvalidOperationException(result.Failure.Message);

        return result.Value;
    }

    public async Task<WebhookRequeueResult> BackfillScanResultsAsync(
        int max = 100, DateTime? before = null, CancellationToken ct = default)
    {
        var cmd = new BackfillScanResultsCommand(_ctx.DivisionId, max, before);
        var result = await _backfill.HandleAsync(cmd, ct);
        if (!result.IsSuccess)
            throw new InvalidOperationException(result.Failure.Message);

        return result.Value;
    }
}

public sealed class TestReconciliationService
{
    private readonly IReconcileProgressHandler _handler;

    public TestReconciliationService(IReconcileProgressHandler handler)
    {
        _handler = handler;
    }

    public async Task<ReconcileResult> RunAsync(
        string divisionId = null, Guid? sessionId = null, int batchSize = 100, CancellationToken ct = default)
    {
        var cmd = new ReconcileProgressCommand(divisionId, sessionId, batchSize);
        var result = await _handler.HandleAsync(cmd, ct);
        if (!result.IsSuccess)
            throw new InvalidOperationException(result.Failure.Message);

        return result.Value;
    }
}
