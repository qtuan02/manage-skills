using System.Linq.Expressions;
using HealthExam.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HealthExam.Infrastructure.Persistence.Legacy;

public interface IRepositoryBase<T> where T : class
{
    IQueryable<T> Query();
    Task<T> GetAsync(Expression<Func<T, bool>> predicate, CancellationToken ct = default);
    Task<List<T>> ListAsync(Expression<Func<T, bool>> predicate, CancellationToken ct = default);
    Task<bool> AnyAsync(Expression<Func<T, bool>> predicate, CancellationToken ct = default);
    Task<int> CountAsync(Expression<Func<T, bool>> predicate, CancellationToken ct = default);
    void Add(T entity);
    void AddRange(IEnumerable<T> entities);
    void Update(T entity);
    void Remove(T entity);
    void RemoveRange(IEnumerable<T> entities);
}

/// <summary>
/// Truy vấn mặc định NoTracking; muốn ghi thì gọi Query().AsTracking() hoặc Update().
/// Repository KHÔNG tự SaveChanges — một thao tác nghiệp vụ của KSK đụng nhiều bảng
/// (Submission + SubmissionSection + SubmissionValue + Audit) và phải nằm trong một
/// transaction, nên việc chốt nằm ở UnitOfWork.
/// </summary>
public class RepositoryBase<T> : IRepositoryBase<T> where T : class
{
    protected readonly HealthExamDbContext Db;
    protected readonly DbSet<T> Set;

    public RepositoryBase(HealthExamDbContext db)
    {
        Db = db;
        Set = db.Set<T>();
    }

    public IQueryable<T> Query() => Set.AsQueryable();

    public Task<T> GetAsync(Expression<Func<T, bool>> predicate, CancellationToken ct = default)
        => Set.FirstOrDefaultAsync(predicate, ct);

    public Task<List<T>> ListAsync(Expression<Func<T, bool>> predicate, CancellationToken ct = default)
        => Set.Where(predicate).ToListAsync(ct);

    public Task<bool> AnyAsync(Expression<Func<T, bool>> predicate, CancellationToken ct = default)
        => Set.AnyAsync(predicate, ct);

    public Task<int> CountAsync(Expression<Func<T, bool>> predicate, CancellationToken ct = default)
        => Set.CountAsync(predicate, ct);

    public void Add(T entity) => Set.Add(entity);
    public void AddRange(IEnumerable<T> entities) => Set.AddRange(entities);
    public void Update(T entity) => Set.Update(entity);
    public void Remove(T entity) => Set.Remove(entity);
    public void RemoveRange(IEnumerable<T> entities) => Set.RemoveRange(entities);
}
