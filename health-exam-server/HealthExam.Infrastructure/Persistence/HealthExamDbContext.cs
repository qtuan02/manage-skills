using HealthExam.Domain.Catalogs;
using HealthExam.Domain.Common;
using HealthExam.Domain.ExamForms;
using HealthExam.Domain.ExamRecords;
using HealthExam.Domain.ExamSessions;
using HealthExam.Domain.Imports;
using HealthExam.Domain.Integrations;
using HealthExam.Domain.Paraclinical;
using HealthExam.Domain.Patients;
using HealthExam.Domain.Webhooks;
using Microsoft.EntityFrameworkCore;

namespace HealthExam.Infrastructure.Persistence;

/// <summary>
/// DbContext của health-exam-server — DB riêng (DEV: DEV_S2_HEALTHEXAM), tiền tố bảng HEX_.
///
/// Giống form-server và KHÁC các *-connect-server: KHÔNG cài converter DateTime toàn cục.
/// Cột là timestamptz lưu UTC, API trả ISO-8601 có offset, FE tự format. Converter toàn cục
/// chính là gốc của lớp lỗi lệch 7h.
///
/// PHASE ĐĂNG KÝ + NỐI FORM-SERVER + CHỈ ĐỊNH CLS + GỬI VENDOR (P3a & P3b): đăng ký, nhật ký,
/// nạp Excel, hộp thư sự kiện đến, khối chỉ định cận lâm sàng, và hàng đợi gửi ra vendor.
/// Bảng còn lại của 01-db-model — HEX_PatientPortal* (cổng người bệnh) — CHƯA khai vì P4 chưa
/// làm; khai sẵn một bảng không có ai ghi vào chỉ làm người đọc sau tưởng đường ống đã có.
/// </summary>
public class HealthExamDbContext : DbContext
{
    public HealthExamDbContext(DbContextOptions<HealthExamDbContext> options) : base(options) { }

    /// <summary>Tên dãy cấp số phiếu chỉ định — dùng chung giữa migration và tầng cấp mã.</summary>
    public const string ParaclinicalOrderNoSequence = "HEX_ParaclinicalOrderNo";

    /// <summary>
    /// Tên dãy cấp SỐ HIỆU DÒNG trên dây vendor (P3b) — ParaclinicalOrderItem.VendorLineNo.
    ///
    /// Dãy RIÊNG chứ không dùng chung với số phiếu: hai thứ được cấp với nhịp hoàn toàn khác
    /// nhau (một phiếu có n dòng), và trộn vào một dãy thì con số của phiếu nhảy theo số dòng
    /// đã gửi — đúng thứ làm mọi phép đối soát "phiếu thứ mấy" bằng mắt trở nên vô nghĩa.
    /// </summary>
    public const string VendorLineNoSequence = "HEX_ParaclinicalVendorLineNo";

    // CATALOG
    public DbSet<Organization> Organizations => Set<Organization>();
    public DbSet<ExamPackage> ExamPackages => Set<ExamPackage>();
    public DbSet<ExamPackageService> ExamPackageServices => Set<ExamPackageService>();

    // ĐỢT KHÁM & HỒ SƠ
    public DbSet<ExamSession> ExamSessions => Set<ExamSession>();
    public DbSet<ExamRecord> ExamRecords => Set<ExamRecord>();
    public DbSet<ExamRecordSignStep> ExamRecordSignSteps => Set<ExamRecordSignStep>();

    // CHỈ ĐỊNH CẬN LÂM SÀNG (UC05.2)
    public DbSet<ParaclinicalOrder> ParaclinicalOrders => Set<ParaclinicalOrder>();
    public DbSet<ParaclinicalOrderItem> ParaclinicalOrderItems => Set<ParaclinicalOrderItem>();
    public DbSet<ParaclinicalResult> ParaclinicalResults => Set<ParaclinicalResult>();
    public DbSet<ParaclinicalResultItem> ParaclinicalResultItems => Set<ParaclinicalResultItem>();

    // NẠP EXCEL (UC03.5)
    public DbSet<ImportBatch> ImportBatches => Set<ImportBatch>();
    public DbSet<ImportBatchRow> ImportBatchRows => Set<ImportBatchRow>();

    // NHẬT KÝ
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    // SỰ KIỆN ĐẾN TỪ FORM-SERVER (02-api-spec §5)
    public DbSet<WebhookInbox> WebhookInboxes => Set<WebhookInbox>();

    // GÓI GỬI RA VENDOR LIS/PACS/RIS (P3b)
    public DbSet<IntegrationOutbox> IntegrationOutboxes => Set<IntegrationOutbox>();

    // MASTER DATA (ĐĂNG KÝ KSK)
    public DbSet<MasterDataOption> MasterDataOptions => Set<MasterDataOption>();

    // CẤU HÌNH BIỂU MẪU THEO NHÓM KHÁM (DYNAMIC STEP 2)
    public DbSet<ExamGroupFormMapping> ExamGroupFormMappings => Set<ExamGroupFormMapping>();
    public DbSet<ExamGroupFormSectionMapping> ExamGroupFormSectionMappings => Set<ExamGroupFormSectionMapping>();

    // NGƯỜI BỆNH (3NF NORMALIZATION)
    public DbSet<Patient> Patients => Set<Patient>();
    public DbSet<PatientInsurance> PatientInsurances => Set<PatientInsurance>();
    public DbSet<PatientEmployment> PatientEmployments => Set<PatientEmployment>();
    public DbSet<PatientRelative> PatientRelatives => Set<PatientRelative>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);
        b.HasPostgresExtension("pgcrypto");   // gen_random_uuid()

        // Dãy cấp SỐ PHIẾU chỉ định. Sequence của PostgreSQL chứ không phải COUNT(*)+1:
        // hai người cùng bấm Lưu ở pop-up thì COUNT đọc cùng một con số và cùng sinh ra một
        // mã — đúng lớp lỗi mà HEX_ExamSession.LastRecordNo đã phải bỏ cách đếm đó để tránh.
        //
        // Sequence KHÔNG lùi khi transaction rollback, nên dãy số phiếu có lỗ. Chấp nhận:
        // số phiếu là mã đối soát với vendor, không phải sổ liên tục — lỗ số rẻ hơn trùng số.
        // Dãy dùng CHUNG cho mọi tenant, còn tính duy nhất thì do UNIQUE(DivisionID, OrderNo)
        // giữ; tách dãy theo tenant sẽ phải sinh sequence động lúc mở đơn vị mới.
        b.HasSequence<long>(ParaclinicalOrderNoSequence).StartsAt(1).IncrementsBy(1);

        // Dãy cấp số hiệu DÒNG trên dây vendor. Cùng lý do sequence như trên; xem
        // ParaclinicalOrderItem.VendorLineNo về việc vì sao dòng cần một số thuần số.
        b.HasSequence<long>(VendorLineNoSequence).StartsAt(1).IncrementsBy(1);

        ConfigureCatalog(b);
        ConfigureSessionAndRecord(b);
        ConfigureParaclinical(b);
        ConfigureImport(b);
        ConfigureAuditLog(b);
        ConfigureWebhookInbox(b);
        ConfigureIntegrationOutbox(b);
        ConfigureMasterData(b);
        ConfigurePatientRegistration(b);
        ConfigureExamGroupFormMapping(b);

        // Enum lưu smallint để khớp DDL trong 01-db-model và để SQL seed/migrate đọc được
        // bằng số nguyên trần, không phụ thuộc tên C#. (Kiểu enum của PostgreSQL thì thêm
        // giá trị phải ALTER TYPE và rollback rất phiền.)
        foreach (var entity in b.Model.GetEntityTypes())
            foreach (var prop in entity.GetProperties())
                if (prop.ClrType.IsEnum || (Nullable.GetUnderlyingType(prop.ClrType)?.IsEnum ?? false))
                    prop.SetColumnType("smallint");
    }

    private static void ConfigureCatalog(ModelBuilder b)
    {
        b.Entity<Organization>(e =>
        {
            e.ToTable("HEX_Organization");
            e.HasKey(x => x.OrganizationID);
            e.Property(x => x.OrganizationID).HasDefaultValueSql("gen_random_uuid()");
            e.Property(x => x.DivisionID).HasMaxLength(20).HasDefaultValue("");
            e.Property(x => x.OrgCode).HasMaxLength(50).IsRequired();
            e.Property(x => x.OrgName).HasMaxLength(500).IsRequired();
            e.Property(x => x.ShortName).HasMaxLength(255).HasDefaultValue("");
            e.Property(x => x.TaxCode).HasMaxLength(20).HasDefaultValue("");
            e.Property(x => x.Address).HasMaxLength(500).HasDefaultValue("");
            e.Property(x => x.ContactName).HasMaxLength(255).HasDefaultValue("");
            e.Property(x => x.ContactPhone).HasMaxLength(20).HasDefaultValue("");
            e.Property(x => x.ContactEmail).HasMaxLength(100).HasDefaultValue("");
            e.Property(x => x.IsActive).HasDefaultValue(true);
            e.HasIndex(x => new { x.DivisionID, x.OrgCode }).IsUnique();
            e.HasIndex(x => new { x.DivisionID, x.OrgName })
             .HasDatabaseName("IX_HEX_Org_Name")
             .HasFilter("\"IsActive\"");
        });

        b.Entity<ExamPackage>(e =>
        {
            e.ToTable("HEX_ExamPackage");
            e.HasKey(x => x.PackageID);
            e.Property(x => x.PackageID).HasDefaultValueSql("gen_random_uuid()");
            e.Property(x => x.DivisionID).HasMaxLength(20).HasDefaultValue("");
            e.Property(x => x.PackageCode).HasMaxLength(50).IsRequired();
            e.Property(x => x.PackageName).HasMaxLength(500).IsRequired();
            e.Property(x => x.VariantCode).HasMaxLength(50);
            e.Property(x => x.IsActive).HasDefaultValue(true);
            e.HasIndex(x => new { x.DivisionID, x.PackageCode }).IsUnique();
        });

        b.Entity<ExamPackageService>(e =>
        {
            e.ToTable("HEX_ExamPackageService");
            e.HasKey(x => x.PackageServiceID);
            e.Property(x => x.PackageServiceID).HasDefaultValueSql("gen_random_uuid()");
            e.Property(x => x.ServiceCode).HasMaxLength(50).HasDefaultValue("");
            e.Property(x => x.ServiceName).HasMaxLength(500).HasDefaultValue("");
            e.Property(x => x.ParaclinicalKind).HasMaxLength(10).HasDefaultValue("");
            e.Property(x => x.ServiceGroupCode).HasMaxLength(50).HasDefaultValue("");
            e.Property(x => x.Quantity).HasDefaultValue((short)1);
            e.Property(x => x.IsActive).HasDefaultValue(true);
            e.HasIndex(x => new { x.PackageID, x.ServiceID }).IsUnique();
            e.HasIndex(x => new { x.PackageID, x.OrderNo })
             .HasDatabaseName("IX_HEX_PkgService_Pkg")
             .HasFilter("\"IsActive\"");
            e.HasOne(x => x.Package).WithMany(p => p.Services)
             .HasForeignKey(x => x.PackageID).OnDelete(DeleteBehavior.Cascade);
        });
    }

    private static void ConfigureSessionAndRecord(ModelBuilder b)
    {
        b.Entity<ExamSession>(e =>
        {
            e.ToTable("HEX_ExamSession");
            e.HasKey(x => x.SessionID);
            e.Property(x => x.SessionID).HasDefaultValueSql("gen_random_uuid()");
            e.Property(x => x.DivisionID).HasMaxLength(20).HasDefaultValue("");
            e.Property(x => x.SessionCode).HasMaxLength(50).IsRequired();
            e.Property(x => x.SessionName).HasMaxLength(500).HasDefaultValue("");
            e.Property(x => x.OrganizationName).HasMaxLength(500).HasDefaultValue("");
            e.Property(x => x.ContractNo).HasMaxLength(100).HasDefaultValue("");
            e.Property(x => x.ExamPlace).HasMaxLength(500).HasDefaultValue("");
            e.Property(x => x.PackageName).HasMaxLength(500).HasDefaultValue("");
            e.Property(x => x.VariantCode).HasMaxLength(50);
            // Bộ đếm cấp mã hồ sơ. NOT NULL DEFAULT 0 chứ không cho NULL: câu cấp số là
            // "LastRecordNo" + 1, gặp NULL thì kết quả là NULL và RETURNING trả về rỗng —
            // hỏng im lặng thay vì báo lỗi. Mọi đường ghi khác (INSERT đợt mới, migrate hàng
            // cũ) đều không nêu cột này nên DEFAULT phải nằm ở DB, không chỉ ở C#.
            e.Property(x => x.LastRecordNo).HasDefaultValue(0);
            e.Property(x => x.IsActive).HasDefaultValue(true);
            e.HasIndex(x => new { x.DivisionID, x.SessionCode }).IsUnique();
            e.HasIndex(x => new { x.DivisionID, x.ExamDate })
             .HasDatabaseName("IX_HEX_Session_Date")
             .IsDescending(false, true)
             .HasFilter("\"IsActive\"");
            e.HasIndex(x => new { x.DivisionID, x.OrganizationID, x.ExamDate })
             .HasDatabaseName("IX_HEX_Session_Org")
             .IsDescending(false, false, true);
            e.HasOne(x => x.Organization).WithMany()
             .HasForeignKey(x => x.OrganizationID).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Package).WithMany()
             .HasForeignKey(x => x.PackageID).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<ExamRecord>(e =>
        {
            e.ToTable("HEX_ExamRecord");
            e.HasKey(x => x.RecordID);
            e.Property(x => x.RecordID).HasDefaultValueSql("gen_random_uuid()");
            e.Property(x => x.DivisionID).HasMaxLength(20).HasDefaultValue("");
            // Trần độ dài lấy từ RecordFieldLengths, KHÔNG viết số ở đây: pha 1 của nạp Excel
            // và tầng chép dữ liệu webhook cùng đo theo bộ hằng số đó. Viết số ở hai chỗ là
            // cách F2 (MR !4) và BLOCKER 1 (MR !5) đã xảy ra — ô dài hơn cột, 500 giữa lô.
            e.Property(x => x.RecordCode).HasMaxLength(RecordFieldLengths.RecordCode).IsRequired();
            e.Property(x => x.VariantCode).HasMaxLength(RecordFieldLengths.VariantCode).IsRequired();
            e.Property(x => x.PackageName).HasMaxLength(RecordFieldLengths.PackageName).HasDefaultValue("");
            e.Property(x => x.FormCode).HasMaxLength(RecordFieldLengths.FormCode).HasDefaultValue("");
            e.Property(x => x.CancelReason).HasMaxLength(RecordFieldLengths.CancelReason).HasDefaultValue("");
            e.Property(x => x.HealthClassCode).HasMaxLength(RecordFieldLengths.HealthClassCode).HasDefaultValue("");

            e.Property(x => x.ProvinceCode).HasMaxLength(RecordFieldLengths.ProvinceCode).HasDefaultValue("");
            e.Property(x => x.ProvinceName).HasMaxLength(RecordFieldLengths.ProvinceName).HasDefaultValue("");
            e.Property(x => x.WardCode).HasMaxLength(RecordFieldLengths.WardCode).HasDefaultValue("");
            e.Property(x => x.WardName).HasMaxLength(RecordFieldLengths.WardName).HasDefaultValue("");
            e.Property(x => x.ExamReason).HasMaxLength(RecordFieldLengths.ExamReason).HasDefaultValue("");
            e.Property(x => x.PaymentSourceOther).HasMaxLength(RecordFieldLengths.PaymentSourceOther).HasDefaultValue("");
            e.Property(x => x.HisFormSyncStatus).HasMaxLength(50).HasDefaultValue("Pending");
            e.Property(x => x.HisFormSyncError).HasMaxLength(1000).HasDefaultValue("");
            e.Property(x => x.SignedFilePath).HasMaxLength(RecordFieldLengths.HisSignedFilePath);
            e.Property(x => x.SignStatus).HasMaxLength(RecordFieldLengths.HisSignStatus).HasDefaultValue(ExamRecordSignStatus.New).IsRequired();

            e.HasIndex(x => new { x.DivisionID, x.RecordCode }).IsUnique();
            e.HasIndex(x => new { x.SessionID, x.State })
             .HasDatabaseName("IX_HEX_Record_Session");


            // Chống trùng người trong cùng một đợt theo PatientRefID (3NF):
            // loại State=4, 5 (Hủy) để một người bị hủy rồi đăng ký lại trong cùng đợt vẫn được.
            e.HasIndex(x => new { x.SessionID, x.PatientRefID })
             .HasDatabaseName("UX_HEX_Record_Session_PatientRef")
             .IsUnique()
             .HasFilter("\"PatientRefID\" IS NOT NULL AND \"State\" NOT IN (4, 5)");

            e.HasOne(x => x.Session).WithMany()
              .HasForeignKey(x => x.SessionID).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Package).WithMany()
              .HasForeignKey(x => x.PackageID).OnDelete(DeleteBehavior.Restrict);
            // FormID/SubmissionID cố ý KHÔNG có FK: chúng trỏ sang DB của form-server.

            // 3NF Normalized References
            e.HasOne(x => x.Patient).WithMany()
              .HasForeignKey(x => x.PatientRefID).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Insurance).WithMany()
              .HasForeignKey(x => x.InsuranceRefID).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Employment).WithMany()
              .HasForeignKey(x => x.EmploymentRefID).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Relative).WithMany()
              .HasForeignKey(x => x.RelativeRefID).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.PatientTypeOption).WithMany()
              .HasForeignKey(x => x.PatientTypeOptionID).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.PatientSubjectOption).WithMany()
              .HasForeignKey(x => x.PatientSubjectOptionID).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.PaymentSourceOption).WithMany()
              .HasForeignKey(x => x.PaymentSourceOptionID).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.ExamLocationOption).WithMany()
              .HasForeignKey(x => x.ExamLocationOptionID).OnDelete(DeleteBehavior.Restrict);

            e.HasIndex(x => x.PatientRefID).HasDatabaseName("IX_HEX_Record_PatientRefID");
            e.HasIndex(x => x.InsuranceRefID).HasDatabaseName("IX_HEX_Record_InsuranceRefID");
            e.HasIndex(x => x.EmploymentRefID).HasDatabaseName("IX_HEX_Record_EmploymentRefID");
            e.HasIndex(x => x.RelativeRefID).HasDatabaseName("IX_HEX_Record_RelativeRefID");
        });

        b.Entity<ExamRecordSignStep>(e =>
        {
            e.ToTable("HEX_ExamRecordSignStep");
            e.HasKey(x => x.ID);
            e.Property(x => x.ID).HasDefaultValueSql("gen_random_uuid()");
            e.Property(x => x.DivisionID).HasMaxLength(20).HasDefaultValue("").IsRequired();
            e.Property(x => x.VariantCode).HasMaxLength(50).HasDefaultValue("").IsRequired();
            e.Property(x => x.StepName).HasMaxLength(255).HasDefaultValue("").IsRequired();
            e.Property(x => x.SignedByEmployeeCode).HasMaxLength(50).HasDefaultValue("").IsRequired();
            e.Property(x => x.SignedByEmployeeName).HasMaxLength(255).IsRequired().HasDefaultValue("");
            e.Property(x => x.PerformedByEmployeeName).HasMaxLength(255).IsRequired().HasDefaultValue("");
            e.Property(x => x.Status).HasMaxLength(30).HasDefaultValue(ExamRecordSignStepStatus.Snapshot).IsRequired();

            e.HasIndex(x => new { x.DivisionID, x.RecordID, x.VariantCode, x.SWStep })
                .IsUnique()
                .HasDatabaseName("UX_HEX_RecordSignStep_Step");

            e.HasIndex(x => new { x.DivisionID, x.RecordID, x.Status })
                .HasDatabaseName("IX_HEX_RecordSignStep_Status");

            e.HasOne(x => x.Record)
                .WithMany(r => r.SignSteps)
                .HasForeignKey(x => x.RecordID)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }

    private static void ConfigureParaclinical(ModelBuilder b)
    {
        b.Entity<ParaclinicalOrder>(e =>
        {
            e.ToTable("HEX_ParaclinicalOrder");
            e.HasKey(x => x.OrderID);
            e.Property(x => x.OrderID).HasDefaultValueSql("gen_random_uuid()");
            e.Property(x => x.DivisionID).HasMaxLength(WebhookFieldLengths.DivisionID).HasDefaultValue("");
            e.Property(x => x.OrderNo).HasMaxLength(ParaclinicalFieldLengths.OrderNo).IsRequired();
            e.Property(x => x.ParaclinicalKind).HasMaxLength(ParaclinicalFieldLengths.ParaclinicalKind).HasDefaultValue("");
            e.Property(x => x.OrderedByName).HasMaxLength(RecordFieldLengths.FullName).HasDefaultValue("");
            e.Property(x => x.OrderedAt).HasDefaultValueSql("now()");
            e.Property(x => x.TargetSystem).HasMaxLength(ParaclinicalFieldLengths.TargetSystem).HasDefaultValue(ParaclinicalTargets.None);
            e.Property(x => x.ExternalOrderID).HasMaxLength(ParaclinicalFieldLengths.ExternalOrderID).HasDefaultValue("");
            e.Property(x => x.Note).HasMaxLength(ParaclinicalFieldLengths.Note).HasDefaultValue("");
            e.Property(x => x.IsActive).HasDefaultValue(true);
            e.Property(x => x.HisPtCode).HasMaxLength(ParaclinicalFieldLengths.HisPtCode).HasDefaultValue("");
            e.Property(x => x.HisAdmissionCode).HasMaxLength(ParaclinicalFieldLengths.HisAdmissionCode).HasDefaultValue("");
            e.Property(x => x.LastSyncError).HasMaxLength(ParaclinicalFieldLengths.LastSyncError).HasDefaultValue("");
            e.Property(x => x.Version).HasDefaultValue(1);

            // Số phiếu duy nhất TRONG một tenant. Không toàn cục: mỗi đơn vị đánh số riêng,
            // và khoá toàn cục sẽ biến việc hai bệnh viện cùng cấp số "CD…0001" thành lỗi ghi.
            e.HasIndex(x => new { x.DivisionID, x.OrderNo })
             .HasDatabaseName("UX_HEX_Order_No").IsUnique();

            e.HasIndex(x => new { x.DivisionID, x.HisParaClinReqId })
             .HasDatabaseName("IX_HEX_Order_HisParaClinReqId")
             .HasFilter("\"HisParaClinReqId\" IS NOT NULL");

            e.HasIndex(x => new { x.RecordID, x.OrderedAt })
             .HasDatabaseName("IX_HEX_Order_Record")
             .IsDescending(false, true)
             .HasFilter("\"IsActive\"");

            // Lọc theo đợt (màn theo dõi tiến độ đợt) — đi thẳng cột denormalize.
            e.HasIndex(x => new { x.SessionID, x.OrderedAt })
             .HasDatabaseName("IX_HEX_Order_Session")
             .IsDescending(false, true);

            e.HasOne(x => x.Record).WithMany()
             .HasForeignKey(x => x.RecordID).OnDelete(DeleteBehavior.Restrict);
            // SourcePackageID cố ý KHÔNG có FK: gói bị soft-delete (hoặc một ngày bị xoá
            // thật) vẫn phải truy được từ phiếu đã chỉ định — xem ParaclinicalOrder.
        });

        b.Entity<ParaclinicalOrderItem>(e =>
        {
            e.ToTable("HEX_ParaclinicalOrderItem");
            e.HasKey(x => x.OrderItemID);
            e.Property(x => x.OrderItemID).HasDefaultValueSql("gen_random_uuid()");
            e.Property(x => x.DivisionID).HasMaxLength(WebhookFieldLengths.DivisionID).HasDefaultValue("");
            e.Property(x => x.ServiceCode).HasMaxLength(ParaclinicalFieldLengths.ServiceCode).HasDefaultValue("");
            e.Property(x => x.ServiceName).HasMaxLength(ParaclinicalFieldLengths.ServiceName).HasDefaultValue("");
            e.Property(x => x.ServiceGroupCode).HasMaxLength(ParaclinicalFieldLengths.ServiceGroupCode).HasDefaultValue("");
            e.Property(x => x.Quantity).HasDefaultValue((short)1);
            e.Property(x => x.Priority).HasDefaultValue(0);
            e.Property(x => x.ResultRequired).HasDefaultValue(true);
            e.Property(x => x.ResultSourceKind).HasMaxLength(ParaclinicalFieldLengths.ResultSourceKind).HasDefaultValue("");
            e.Property(x => x.ResultRefID).HasMaxLength(ParaclinicalFieldLengths.ResultRefID);
            e.Property(x => x.MessageID).HasMaxLength(ParaclinicalFieldLengths.MessageID);
            e.Property(x => x.CancelReason).HasMaxLength(ParaclinicalFieldLengths.CancelReason).HasDefaultValue("");

            // Một dịch vụ chỉ được đứng MỘT lần trong một phiếu — bấm hai lần cùng dịch vụ ở
            // pop-up là thao tác thừa, không phải hai chỉ định.
            e.HasIndex(x => new { x.OrderID, x.ServiceID })
             .HasDatabaseName("UX_HEX_OrderItem_Service").IsUnique();

            // ★ Đúng câu truy vấn của điều kiện (B): đếm dòng chưa xong của MỘT hồ sơ.
            e.HasIndex(x => new { x.RecordID, x.State })
             .HasDatabaseName("IX_HEX_OrderItem_Record");

            // Chống trùng đường vendor — cùng khuôn với hộp thư sự kiện, xem MessageID.
            e.HasIndex(x => new { x.DivisionID, x.MessageID })
             .HasDatabaseName("UX_HEX_OrderItem_MessageID").IsUnique()
             .HasFilter("\"MessageID\" IS NOT NULL");

            // ★ Đúng câu truy vấn của ĐƯỜNG VỀ vendor: gói gọi về chỉ mang một số hiệu dòng
            // (OrderID sau khi cắt 2 ký tự), và tra sai tenant là áp trạng thái lên dịch vụ
            // của người khác. UNIQUE MỘT PHẦN: NULL trên mọi dòng chưa từng được gửi.
            e.HasIndex(x => new { x.DivisionID, x.VendorLineNo })
             .HasDatabaseName("UX_HEX_OrderItem_VendorLineNo").IsUnique()
             .HasFilter("\"VendorLineNo\" IS NOT NULL");

            e.HasIndex(x => new { x.DivisionID, x.HisParaClinProcessId })
             .HasDatabaseName("IX_HEX_OrderItem_HisParaClinProcessId")
             .HasFilter("\"HisParaClinProcessId\" IS NOT NULL");

            // CASCADE: dòng dịch vụ không có nghĩa ngoài phiếu của nó. Khác HEX_AuditLog —
            // ở đó cascade sẽ xoá mất nhật ký, còn ở đây để lại dòng mồ côi thì điều kiện (B)
            // đếm phải một dịch vụ không thuộc phiếu nào và không ai ký được kết luận.
            e.HasOne(x => x.Order).WithMany(o => o.Items)
             .HasForeignKey(x => x.OrderID).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<ParaclinicalResult>(e =>
        {
            e.ToTable("HEX_ParaclinicalResult");
            e.HasKey(x => x.ResultId);
            e.Property(x => x.ResultId).HasDefaultValueSql("gen_random_uuid()");
            e.Property(x => x.DivisionID).HasMaxLength(WebhookFieldLengths.DivisionID).HasDefaultValue("");
            e.Property(x => x.HisResultId).HasMaxLength(ParaclinicalFieldLengths.HisResultId).IsRequired();
            e.Property(x => x.Status).HasMaxLength(50).HasDefaultValue("");
            e.Property(x => x.ImportedAt).HasDefaultValueSql("now()");

            e.HasIndex(x => new { x.DivisionID, x.HisResultId })
             .HasDatabaseName("UX_HEX_Result_HisResultId").IsUnique();

            e.HasIndex(x => new { x.OrderId, x.ResultDate })
             .HasDatabaseName("IX_HEX_Result_Order");

            e.HasOne(x => x.Order).WithMany()
             .HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<ParaclinicalResultItem>(e =>
        {
            e.ToTable("HEX_ParaclinicalResultItem");
            e.HasKey(x => x.ResultItemId);
            e.Property(x => x.ResultItemId).HasDefaultValueSql("gen_random_uuid()");
            e.Property(x => x.DivisionID).HasMaxLength(WebhookFieldLengths.DivisionID).HasDefaultValue("");
            e.Property(x => x.HisDetailId).HasMaxLength(ParaclinicalFieldLengths.HisDetailId).HasDefaultValue("");
            e.Property(x => x.Value).HasMaxLength(ParaclinicalFieldLengths.ResultValue).HasDefaultValue("");
            e.Property(x => x.Text).HasMaxLength(ParaclinicalFieldLengths.ResultText).HasDefaultValue("");
            e.Property(x => x.Unit).HasMaxLength(ParaclinicalFieldLengths.ResultUnit).HasDefaultValue("");
            e.Property(x => x.ReferenceRange).HasMaxLength(ParaclinicalFieldLengths.ReferenceRange).HasDefaultValue("");
            e.Property(x => x.AbnormalFlag).HasMaxLength(ParaclinicalFieldLengths.AbnormalFlag).HasDefaultValue("");

            e.HasIndex(x => new { x.ResultId, x.HisDetailId })
             .HasDatabaseName("IX_HEX_ResultItem_Detail");

            e.HasOne(x => x.Result).WithMany(r => r.Items)
             .HasForeignKey(x => x.ResultId).OnDelete(DeleteBehavior.Cascade);

            e.HasOne(x => x.OrderItem).WithMany()
             .HasForeignKey(x => x.OrderItemId).OnDelete(DeleteBehavior.SetNull);
        });
    }

    private static void ConfigureImport(ModelBuilder b)
    {
        b.Entity<ImportBatch>(e =>
        {
            e.ToTable("HEX_ImportBatch");
            e.HasKey(x => x.BatchID);
            e.Property(x => x.BatchID).HasDefaultValueSql("gen_random_uuid()");
            e.Property(x => x.DivisionID).HasMaxLength(20).HasDefaultValue("");
            e.Property(x => x.FileName).HasMaxLength(500).IsRequired();
            e.Property(x => x.StoragePath).HasDefaultValue("");
            e.Property(x => x.SheetName).HasMaxLength(100).HasDefaultValue("");
            e.Property(x => x.ErrorSummary).HasMaxLength(1000).HasDefaultValue("");
            e.Property(x => x.StartedAt).HasDefaultValueSql("now()");

            e.HasOne(x => x.Session).WithMany()
             .HasForeignKey(x => x.SessionID).OnDelete(DeleteBehavior.Restrict);

            e.HasIndex(x => new { x.SessionID, x.StartedAt })
             .HasDatabaseName("IX_HEX_Import_Session")
             .IsDescending(false, true);
        });

        b.Entity<ImportBatchRow>(e =>
        {
            e.ToTable("HEX_ImportBatchRow");
            e.HasKey(x => x.ImportRowID);
            e.Property(x => x.ImportRowID).UseIdentityAlwaysColumn();   // bigserial
            e.Property(x => x.RawData).HasColumnType("jsonb").IsRequired();
            e.Property(x => x.ErrorCode).HasMaxLength(50).HasDefaultValue("");
            e.Property(x => x.ErrorMessage).HasMaxLength(1000).HasDefaultValue("");

            // CASCADE ở đây (ngược với HEX_AuditLog) là có chủ ý: dòng Excel không phải nhật
            // ký, nó chỉ có nghĩa bên trong lô của mình. Bỏ lô mà để lại dòng mồ côi thì job
            // dọn lô quá hạn sẽ làm bảng phình mãi.
            e.HasOne(x => x.Batch).WithMany(x => x.Rows)
             .HasForeignKey(x => x.BatchID).OnDelete(DeleteBehavior.Cascade);

            // ⚠️ HAI chỉ mục trên CÙNG bộ cột, nên PHẢI dùng quá tải có TÊN. EF gộp các
            // HasIndex cùng bộ thuộc tính làm một; đã vấp thật: bản không tên sinh ra đúng
            // MỘT chỉ mục vừa UNIQUE vừa mang bộ lọc "WHERE NOT IsValid" — tức ràng buộc duy
            // nhất chỉ còn áp cho dòng HỎNG, và hai dòng ĐẠT trùng RowNo lọt qua. Sai lặng lẽ,
            // migration vẫn chạy, test EF in-memory không thấy (in-memory không có UNIQUE).
            e.HasIndex(x => new { x.BatchID, x.RowNo }, "UX_HEX_ImportRow_Batch_RowNo").IsUnique();

            // Chỉ mục CÓ LỌC: màn preview gần như luôn hỏi "cho tôi xem các dòng hỏng".
            e.HasIndex(x => new { x.BatchID, x.RowNo }, "IX_HEX_ImportRow_Error")
             .HasFilter("NOT \"IsValid\"");
        });
    }

    private static void ConfigureAuditLog(ModelBuilder b)
    {
        b.Entity<AuditLog>(e =>
        {
            e.ToTable("HEX_AuditLog");
            e.HasKey(x => x.AuditID);
            e.Property(x => x.AuditID).UseIdentityAlwaysColumn();   // bigserial
            e.Property(x => x.DivisionID).HasMaxLength(20).HasDefaultValue("");
            e.Property(x => x.EntityType).HasMaxLength(30).IsRequired();
            e.Property(x => x.Action).HasMaxLength(30).IsRequired();
            e.Property(x => x.ActorName).HasMaxLength(255).HasDefaultValue("");
            e.Property(x => x.TraceID).HasMaxLength(50).HasDefaultValue("");
            e.Property(x => x.Payload).HasColumnType("jsonb");
            e.Property(x => x.OccurredAt).HasDefaultValueSql("now()");

            // EntityID KHÔNG có FK sang HEX_ExamSession/HEX_ExamRecord: một dòng nhật ký phải
            // sống lâu hơn bản ghi nó mô tả — xoá hồ sơ mà kéo theo nhật ký thì đúng lúc cần
            // tra "ai xoá" là không còn gì để tra.
            e.HasIndex(x => new { x.EntityType, x.EntityID, x.OccurredAt })
             .HasDatabaseName("IX_HEX_Audit_Entity")
             .IsDescending(false, false, true);
            e.HasIndex(x => new { x.DivisionID, x.OccurredAt })
             .HasDatabaseName("IX_HEX_Audit_Division")
             .IsDescending(false, true);
        });
    }

    private static void ConfigureWebhookInbox(ModelBuilder b)
    {
        b.Entity<WebhookInbox>(e =>
        {
            e.ToTable("HEX_WebhookInbox");
            e.HasKey(x => x.InboxID);
            e.Property(x => x.InboxID).UseIdentityAlwaysColumn();   // bigserial
            e.Property(x => x.EventID).HasMaxLength(WebhookFieldLengths.EventID).IsRequired();
            e.Property(x => x.EventType).HasMaxLength(WebhookFieldLengths.EventType).IsRequired();
            e.Property(x => x.DivisionID).HasMaxLength(WebhookFieldLengths.DivisionID).HasDefaultValue("");
            e.Property(x => x.Payload).HasColumnType("jsonb").IsRequired();
            e.Property(x => x.LastError).HasMaxLength(WebhookFieldLengths.LastError).HasDefaultValue("");
            e.Property(x => x.TraceID).HasMaxLength(WebhookFieldLengths.TraceID).HasDefaultValue("");
            e.Property(x => x.ReceivedAt).HasDefaultValueSql("now()");
            e.Property(x => x.MessageID).HasMaxLength(ParaclinicalFieldLengths.MessageID);

            // ★ Chốt chặn trùng. Đặt Ở DB chứ không ở service: hai gói cùng EventID hoàn toàn
            // có thể tới song song (bên phát retry trong lúc gói đầu còn đang ghi), và mọi
            // phép "kiểm rồi mới ghi" ở tầng code đều lọt qua khe đó.
            //
            // THEO TENANT, không toàn cục (đổi sau review MR !5): UNIQUE toàn cục biến "một
            // EventID đã tiêu thụ ở tenant sai" thành "gói thật của tenant đúng vĩnh viễn là
            // trùng" — tức mất hẳn một sự kiện khám, không có lỗi nào ném ra. Bên phát vẫn
            // sinh EventID duy nhất toàn cục nên khoá rộng ra theo tenant không nới lỏng gì.
            e.HasIndex(x => new { x.DivisionID, x.EventID })
             .HasDatabaseName("UX_HEX_Inbox_Event").IsUnique();

            // Khoá thứ hai, cho đường VENDOR. UNIQUE MỘT PHẦN vì cột NULL trên toàn bộ gói
            // của form-server: unique thường coi nhiều NULL là khác nhau ở PostgreSQL nên
            // vẫn chạy, nhưng chỉ mục sẽ ôm cả triệu hàng NULL vô ích. Bộ lọc giữ chỉ mục
            // đúng bằng số hàng thật sự có khoá.
            e.HasIndex(x => new { x.DivisionID, x.MessageID })
             .HasDatabaseName("UX_HEX_Inbox_MessageID").IsUnique()
             .HasFilter("\"MessageID\" IS NOT NULL");

            // Đúng câu truy vấn của worker: hàng tới hạn thử, cũ nhất trước. Chỉ mục CÓ LỌC
            // để nó không phình theo toàn bộ lịch sử sự kiện đã xử lý — bảng này chỉ có thêm,
            // không bao giờ bớt.
            e.HasIndex(x => new { x.ProcessState, x.NextAttemptAt, x.ReceivedAt })
             .HasDatabaseName("IX_HEX_Inbox_Pending")
             .HasFilter("\"ProcessState\" IN (0, 2)");
        });
    }

    private static void ConfigureIntegrationOutbox(ModelBuilder b)
    {
        b.Entity<IntegrationOutbox>(e =>
        {
            e.ToTable("HEX_IntegrationOutbox");
            e.HasKey(x => x.OutboxID);
            e.Property(x => x.OutboxID).UseIdentityAlwaysColumn();   // bigserial
            e.Property(x => x.DivisionID).HasMaxLength(WebhookFieldLengths.DivisionID).HasDefaultValue("");
            e.Property(x => x.Vendor).HasMaxLength(ParaclinicalFieldLengths.TargetSystem).IsRequired();
            e.Property(x => x.Operation).HasMaxLength(OutboxFieldLengths.Operation).IsRequired();
            e.Property(x => x.DedupKey).HasMaxLength(OutboxFieldLengths.DedupKey).IsRequired();
            e.Property(x => x.Payload).HasColumnType("jsonb").IsRequired();
            e.Property(x => x.LastError).HasMaxLength(WebhookFieldLengths.LastError).HasDefaultValue("");
            e.Property(x => x.ResponseSnippet).HasMaxLength(OutboxFieldLengths.ResponseSnippet).HasDefaultValue("");
            e.Property(x => x.TraceID).HasMaxLength(WebhookFieldLengths.TraceID).HasDefaultValue("");
            e.Property(x => x.CreatedAt).HasDefaultValueSql("now()");

            // ★ Chống xếp hàng trùng, ở TẦNG DB. Bấm Gửi hai lần liên tiếp là thao tác thường
            // gặp nhất của màn này, và hai request song song thì phép kiểm ở tầng service lọt
            // qua đúng khe giữa SELECT và INSERT — cùng bài học với UNIQUE(EventID).
            e.HasIndex(x => new { x.DivisionID, x.DedupKey })
             .HasDatabaseName("UX_HEX_Outbox_Dedup").IsUnique();

            // Đúng câu truy vấn của worker: gói tới hạn gửi, cũ nhất trước. CÓ LỌC để chỉ mục
            // không phình theo toàn bộ lịch sử đã gửi — bảng này chỉ có thêm, không bớt.
            e.HasIndex(x => new { x.State, x.NextAttemptAt, x.CreatedAt })
             .HasDatabaseName("IX_HEX_Outbox_Pending")
             .HasFilter("\"State\" IN (0, 2)");

            // Tra theo phiếu: "phiếu này đã gửi được chưa, lỗi gì" là câu hỏi đầu tiên của
            // người trực khi bác sĩ báo phòng chụp không thấy chỉ định.
            e.HasIndex(x => x.OrderID).HasDatabaseName("IX_HEX_Outbox_Order");
        });
    }

    private static void ConfigureMasterData(ModelBuilder b)
    {
        b.Entity<MasterDataOption>(e =>
        {
            e.ToTable("HEX_MasterDataOption");
            e.HasKey(x => x.OptionID);
            e.Property(x => x.OptionID).HasDefaultValueSql("gen_random_uuid()");
            e.Property(x => x.DivisionID).HasMaxLength(20).HasDefaultValue("");
            e.Property(x => x.Category).HasMaxLength(50).IsRequired();
            e.Property(x => x.Code).HasMaxLength(50).IsRequired();
            e.Property(x => x.Name).HasMaxLength(500).IsRequired();
            e.Property(x => x.ParentCode).HasMaxLength(50).HasDefaultValue("");
            e.Property(x => x.IsActive).HasDefaultValue(true);
            e.HasIndex(x => new { x.DivisionID, x.Category, x.Code }).IsUnique();
            e.HasIndex(x => new { x.DivisionID, x.Category, x.IsActive, x.OrderNo });
            e.HasIndex(x => new { x.DivisionID, x.Category, x.ParentCode, x.IsActive });
        });
    }

    public static readonly Guid Dtk03MappingId = Guid.Parse("f0000001-0000-0000-0000-000000000001");
    public static readonly Guid Dtk03HistorySectionId = Guid.Parse("f0000001-0000-0000-0000-000000000002");
    public static readonly Guid Dtk03ExtraInfoSectionId = Guid.Parse("f0000001-0000-0000-0000-000000000003");

    public static readonly Guid Dtk03DhTestingMappingId = Guid.Parse("f0000001-0000-0000-0000-000000000004");
    public static readonly Guid Dtk03DhTestingHistorySectionId = Guid.Parse("f0000001-0000-0000-0000-000000000005");
    public static readonly Guid Dtk03DhTestingExtraInfoSectionId = Guid.Parse("f0000001-0000-0000-0000-000000000006");

    private static void ConfigureExamGroupFormMapping(ModelBuilder b)
    {
        b.Entity<ExamGroupFormMapping>(e =>
        {
            e.ToTable("HEX_ExamGroupFormMapping");
            e.HasKey(x => x.MappingID);
            e.Property(x => x.MappingID).HasDefaultValueSql("gen_random_uuid()");
            e.Property(x => x.DivisionID).HasMaxLength(20).HasDefaultValue("");
            e.Property(x => x.VariantCode).HasMaxLength(50).IsRequired();
            e.Property(x => x.TemplateCode).HasMaxLength(100).IsRequired();
            e.Property(x => x.MedicalTypeCode).HasMaxLength(50).HasDefaultValue("");
            e.Property(x => x.IsActive).HasDefaultValue(true);
            e.HasIndex(x => new { x.DivisionID, x.VariantCode }).IsUnique();

            e.HasMany(x => x.Sections)
             .WithOne(x => x.Mapping)
             .HasForeignKey(x => x.MappingID)
             .OnDelete(DeleteBehavior.Cascade);

            e.HasData(
                new
                {
                    MappingID = Dtk03MappingId,
                    DivisionID = "DEV",
                    VariantCode = "DTK_03",
                    TemplateCode = "KSK-TREN18TUOI",
                    MedicalTypeCode = "KSK03",
                    IsActive = true,
                    CreatedDate = new DateTime(2026, 9, 9, 0, 0, 0, DateTimeKind.Utc),
                    CreatedBy = 0L,
                    CreatedActorKind = ActorKind.Employee,
                    ModifiedDate = new DateTime(2026, 9, 9, 0, 0, 0, DateTimeKind.Utc),
                    ModifiedBy = 0L,
                    ModifiedActorKind = ActorKind.Employee
                },
                new
                {
                    MappingID = Dtk03DhTestingMappingId,
                    DivisionID = "DHTESTING",
                    VariantCode = "DTK_03",
                    TemplateCode = "KSK-TREN18TUOI",
                    MedicalTypeCode = "KSK03",
                    IsActive = true,
                    CreatedDate = new DateTime(2026, 9, 9, 0, 0, 0, DateTimeKind.Utc),
                    CreatedBy = 0L,
                    CreatedActorKind = ActorKind.Employee,
                    ModifiedDate = new DateTime(2026, 9, 9, 0, 0, 0, DateTimeKind.Utc),
                    ModifiedBy = 0L,
                    ModifiedActorKind = ActorKind.Employee
                }
            );
        });

        b.Entity<ExamGroupFormSectionMapping>(e =>
        {
            e.ToTable("HEX_ExamGroupFormSectionMapping");
            e.HasKey(x => x.SectionMappingID);
            e.Property(x => x.SectionMappingID).HasDefaultValueSql("gen_random_uuid()");
            e.Property(x => x.SectionKind).HasMaxLength(50).IsRequired();
            e.Property(x => x.ItemGroupID).IsRequired();
            e.HasIndex(x => new { x.MappingID, x.SectionKind }).IsUnique();

            e.HasData(
                new
                {
                    SectionMappingID = Dtk03HistorySectionId,
                    MappingID = Dtk03MappingId,
                    SectionKind = "HISTORY",
                    ItemGroupID = 64
                },
                new
                {
                    SectionMappingID = Dtk03ExtraInfoSectionId,
                    MappingID = Dtk03MappingId,
                    SectionKind = "EXTRA_INFO",
                    ItemGroupID = 63
                },
                new
                {
                    SectionMappingID = Dtk03DhTestingHistorySectionId,
                    MappingID = Dtk03DhTestingMappingId,
                    SectionKind = "HISTORY",
                    ItemGroupID = 64
                },
                new
                {
                    SectionMappingID = Dtk03DhTestingExtraInfoSectionId,
                    MappingID = Dtk03DhTestingMappingId,
                    SectionKind = "EXTRA_INFO",
                    ItemGroupID = 63
                }
            );
        });

        b.Entity<SignStepMap>(e =>
        {
            e.ToTable("HEX_SignStepMap");
            e.HasKey(x => x.ID);
            e.Property(x => x.ID).HasDefaultValueSql("gen_random_uuid()");
            e.Property(x => x.DivisionID).HasMaxLength(20).HasDefaultValue("").IsRequired();
            e.Property(x => x.VariantCode).HasMaxLength(50).IsRequired();
            e.Property(x => x.StepName).HasMaxLength(255).HasDefaultValue("");
            e.Property(x => x.SignTitle).HasMaxLength(255).HasDefaultValue("");
            e.Property(x => x.SearchPattern).HasMaxLength(50).HasDefaultValue("");
            e.Property(x => x.SignType).HasDefaultValue(1);
            e.Property(x => x.SLType).HasDefaultValue(2);
            e.Property(x => x.IsActive).HasDefaultValue(true);
            e.HasIndex(x => new { x.DivisionID, x.VariantCode, x.SWStep }).IsUnique();
            e.HasIndex(x => new { x.DivisionID, x.VariantCode, x.ItemGroupID })
                .IsUnique()
                .HasFilter("\"ItemGroupID\" IS NOT NULL");
        });
    }

    private static void ConfigurePatientRegistration(ModelBuilder b)
    {
        b.Entity<Patient>(e =>
        {
            e.ToTable("HEX_Patient");
            e.HasKey(x => x.PatientRefID);
            e.Property(x => x.PatientRefID).HasDefaultValueSql("gen_random_uuid()");
            e.Property(x => x.DivisionID).HasMaxLength(20).HasDefaultValue("");
            e.Property(x => x.PatientCode).HasMaxLength(RecordFieldLengths.PatientCode).HasDefaultValue("");
            e.Property(x => x.FullName).HasMaxLength(RecordFieldLengths.FullName).IsRequired();
            e.Property(x => x.IdentityNumber).HasMaxLength(RecordFieldLengths.IdentityNumber).HasDefaultValue("");
            e.Property(x => x.PhoneNumber).HasMaxLength(RecordFieldLengths.PhoneNumber).HasDefaultValue("");
            e.Property(x => x.Email).HasMaxLength(RecordFieldLengths.Email).HasDefaultValue("");
            e.Property(x => x.Address).HasMaxLength(RecordFieldLengths.Address).HasDefaultValue("");
            e.Property(x => x.ProvinceCode).HasMaxLength(RecordFieldLengths.ProvinceCode).HasDefaultValue("");
            e.Property(x => x.WardCode).HasMaxLength(RecordFieldLengths.WardCode).HasDefaultValue("");
            e.Property(x => x.BloodAboCode).HasMaxLength(RecordFieldLengths.BloodAboCode).HasDefaultValue("");
            e.Property(x => x.BloodRhCode).HasMaxLength(RecordFieldLengths.BloodRhCode).HasDefaultValue("");
            e.Property(x => x.HisSyncStatus).HasMaxLength(RecordFieldLengths.HisSyncStatus).HasDefaultValue("Pending").IsRequired();
            e.Property(x => x.HisSyncError).HasMaxLength(RecordFieldLengths.HisSyncError).HasDefaultValue("").IsRequired();
            e.Property(x => x.IsActive).HasDefaultValue(true);
            e.Property(x => x.ProfileLineageID).HasDefaultValueSql("gen_random_uuid()");
            e.Property(x => x.VersionNumber).HasDefaultValue(1);

            e.HasOne(x => x.PreviousPatient).WithMany()
             .HasForeignKey(x => x.PreviousPatientRefID)
             .OnDelete(DeleteBehavior.Restrict);

            e.HasIndex(x => new { x.DivisionID, x.IdentityNumber })
             .HasDatabaseName("UX_HEX_Patient_Division_ActiveIdentity")
             .IsUnique()
             .HasFilter("\"IsActive\" AND \"IdentityNumber\" <> ''");

            e.HasIndex(x => new { x.DivisionID, x.HisPatientID })
             .HasDatabaseName("UX_HEX_Patient_Division_HisPatientID")
             .IsUnique()
             .HasFilter("\"IsActive\" AND \"HisPatientID\" IS NOT NULL AND \"HisPatientID\" > 0");
            e.HasIndex(x => new { x.DivisionID, x.ProfileLineageID, x.VersionNumber })
             .HasDatabaseName("UX_HEX_Patient_Division_Lineage_Version")
             .IsUnique();
            e.HasIndex(x => new { x.DivisionID, x.PatientCode })
             .HasDatabaseName("IX_HEX_Patient_Division_Code");

            e.HasOne(x => x.IdentityIssuerOption).WithMany()
             .HasForeignKey(x => x.IdentityIssuerOptionID).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.EthnicityOption).WithMany()
             .HasForeignKey(x => x.EthnicityOptionID).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<PatientInsurance>(e =>
        {
            e.ToTable("HEX_PatientInsurance");
            e.HasKey(x => x.InsuranceRefID);
            e.Property(x => x.InsuranceRefID).HasDefaultValueSql("gen_random_uuid()");
            e.Property(x => x.DivisionID).HasMaxLength(20).HasDefaultValue("");
            e.Property(x => x.InsuranceNumber).HasMaxLength(RecordFieldLengths.InsuranceNumber).HasDefaultValue("");
            e.Property(x => x.IsActive).HasDefaultValue(true);

            e.HasIndex(x => x.PatientRefID).HasDatabaseName("IX_HEX_PatientInsurance_Patient");
            e.HasIndex(x => new { x.DivisionID, x.InsuranceNumber }).HasDatabaseName("IX_HEX_PatientInsurance_Number");

            e.HasOne(x => x.Patient).WithMany(x => x.Insurances)
             .HasForeignKey(x => x.PatientRefID).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.InsuranceObjectOption).WithMany()
             .HasForeignKey(x => x.InsuranceObjectOptionID).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.RegistrationPlaceOption).WithMany()
             .HasForeignKey(x => x.RegistrationPlaceOptionID).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<PatientEmployment>(e =>
        {
            e.ToTable("HEX_PatientEmployment");
            e.HasKey(x => x.EmploymentRefID);
            e.Property(x => x.EmploymentRefID).HasDefaultValueSql("gen_random_uuid()");
            e.Property(x => x.DivisionID).HasMaxLength(20).HasDefaultValue("");
            e.Property(x => x.StaffCode).HasMaxLength(RecordFieldLengths.StaffCode).HasDefaultValue("");
            e.Property(x => x.OrgDeptName).HasMaxLength(RecordFieldLengths.OrgDeptName).HasDefaultValue("");
            e.Property(x => x.JobTitle).HasMaxLength(RecordFieldLengths.JobTitle).HasDefaultValue("");
            e.Property(x => x.IsActive).HasDefaultValue(true);

            e.HasIndex(x => x.PatientRefID).HasDatabaseName("IX_HEX_PatientEmployment_Patient");

            e.HasOne(x => x.Patient).WithMany(x => x.Employments)
             .HasForeignKey(x => x.PatientRefID).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.OccupationOption).WithMany()
             .HasForeignKey(x => x.OccupationOptionID).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<PatientRelative>(e =>
        {
            e.ToTable("HEX_PatientRelative");
            e.HasKey(x => x.RelativeRefID);
            e.Property(x => x.RelativeRefID).HasDefaultValueSql("gen_random_uuid()");
            e.Property(x => x.DivisionID).HasMaxLength(20).HasDefaultValue("");
            e.Property(x => x.RelationshipCode).HasMaxLength(RecordFieldLengths.RelativeRelationshipCode).HasDefaultValue("");
            e.Property(x => x.FullName).HasMaxLength(RecordFieldLengths.RelativeFullName).HasDefaultValue("");
            e.Property(x => x.IdentityNumber).HasMaxLength(RecordFieldLengths.RelativeIdentityNumber).HasDefaultValue("");
            e.Property(x => x.PhoneNumber).HasMaxLength(RecordFieldLengths.RelativePhoneNumber).HasDefaultValue("");
            e.Property(x => x.IsActive).HasDefaultValue(true);

            e.HasIndex(x => x.PatientRefID).HasDatabaseName("IX_HEX_PatientRelative_Patient");

            e.HasOne(x => x.Patient).WithMany(x => x.Relatives)
             .HasForeignKey(x => x.PatientRefID).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
