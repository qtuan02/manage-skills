using HealthExam.Domain.Catalogs;
using HealthExam.Domain.Common;
using HealthExam.Domain.ExamRecords;
using HealthExam.Domain.ExamSessions;
using HealthExam.Domain.Imports;
using HealthExam.Domain.Integrations;
using HealthExam.Domain.Paraclinical;
using HealthExam.Domain.Webhooks;
using HealthExam.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Storage;

namespace HealthExam.Infrastructure.Persistence.Legacy;

/// <summary>
/// Chỉ mở đúng những repository đang có người dùng. Repository KHÔNG tự SaveChanges — một thao
/// tác nghiệp vụ (VD tạo hồ sơ + cập nhật đếm của đợt) đụng nhiều bảng và phải nằm trong một
/// transaction, nên việc chốt nằm ở đây.
/// </summary>
public interface IUnitOfWork : IAsyncDisposable
{
    IRepositoryBase<Organization> Organizations { get; }
    IRepositoryBase<ExamPackage> ExamPackages { get; }
    IRepositoryBase<ExamPackageService> ExamPackageServices { get; }
    IRepositoryBase<ExamSession> ExamSessions { get; }
    IRepositoryBase<ExamRecord> ExamRecords { get; }
    IRepositoryBase<ParaclinicalOrder> ParaclinicalOrders { get; }
    IRepositoryBase<ParaclinicalOrderItem> ParaclinicalOrderItems { get; }
    IRepositoryBase<ImportBatch> ImportBatches { get; }
    IRepositoryBase<ImportBatchRow> ImportBatchRows { get; }
    IRepositoryBase<AuditLog> AuditLogs { get; }
    IRepositoryBase<WebhookInbox> WebhookInboxes { get; }
    IRepositoryBase<IntegrationOutbox> IntegrationOutboxes { get; }
    IRepositoryBase<MasterDataOption> MasterDataOptions { get; }
    IRepositoryBase<HealthExam.Domain.Patients.Patient> Patients { get; }
    IRepositoryBase<HealthExam.Domain.Patients.PatientInsurance> PatientInsurances { get; }
    IRepositoryBase<HealthExam.Domain.Patients.PatientEmployment> PatientEmployments { get; }
    IRepositoryBase<HealthExam.Domain.Patients.PatientRelative> PatientRelatives { get; }

    HealthExamDbContext Context { get; }
    Task<int> SaveChangesAsync(CancellationToken ct = default);
    Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken ct = default);
}

public class UnitOfWork : IUnitOfWork
{
    private readonly HealthExamDbContext _db;
    private readonly Dictionary<Type, object> _repos = new();

    public UnitOfWork(HealthExamDbContext db) => _db = db;

    private IRepositoryBase<T> Repo<T>() where T : class
    {
        if (!_repos.TryGetValue(typeof(T), out var repo))
        {
            repo = new RepositoryBase<T>(_db);
            _repos[typeof(T)] = repo;
        }
        return (IRepositoryBase<T>)repo;
    }

    public IRepositoryBase<Organization> Organizations => Repo<Organization>();
    public IRepositoryBase<ExamPackage> ExamPackages => Repo<ExamPackage>();
    public IRepositoryBase<ExamPackageService> ExamPackageServices => Repo<ExamPackageService>();
    public IRepositoryBase<ExamSession> ExamSessions => Repo<ExamSession>();
    public IRepositoryBase<ExamRecord> ExamRecords => Repo<ExamRecord>();
    public IRepositoryBase<ParaclinicalOrder> ParaclinicalOrders => Repo<ParaclinicalOrder>();
    public IRepositoryBase<ParaclinicalOrderItem> ParaclinicalOrderItems => Repo<ParaclinicalOrderItem>();
    public IRepositoryBase<ImportBatch> ImportBatches => Repo<ImportBatch>();
    public IRepositoryBase<ImportBatchRow> ImportBatchRows => Repo<ImportBatchRow>();
    public IRepositoryBase<AuditLog> AuditLogs => Repo<AuditLog>();
    public IRepositoryBase<WebhookInbox> WebhookInboxes => Repo<WebhookInbox>();
    public IRepositoryBase<IntegrationOutbox> IntegrationOutboxes => Repo<IntegrationOutbox>();
    public IRepositoryBase<MasterDataOption> MasterDataOptions => Repo<MasterDataOption>();
    public IRepositoryBase<HealthExam.Domain.Patients.Patient> Patients => Repo<HealthExam.Domain.Patients.Patient>();
    public IRepositoryBase<HealthExam.Domain.Patients.PatientInsurance> PatientInsurances => Repo<HealthExam.Domain.Patients.PatientInsurance>();
    public IRepositoryBase<HealthExam.Domain.Patients.PatientEmployment> PatientEmployments => Repo<HealthExam.Domain.Patients.PatientEmployment>();
    public IRepositoryBase<HealthExam.Domain.Patients.PatientRelative> PatientRelatives => Repo<HealthExam.Domain.Patients.PatientRelative>();

    public HealthExamDbContext Context => _db;

    public Task<int> SaveChangesAsync(CancellationToken ct = default) => _db.SaveChangesAsync(ct);

    public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken ct = default)
        => _db.Database.BeginTransactionAsync(ct);

    public ValueTask DisposeAsync() => _db.DisposeAsync();
}
