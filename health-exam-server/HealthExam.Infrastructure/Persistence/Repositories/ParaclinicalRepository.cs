using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HealthExam.Application.Paraclinical;
using HealthExam.Domain.Catalogs;
using HealthExam.Domain.Common;
using HealthExam.Domain.Paraclinical;
using Microsoft.EntityFrameworkCore;

namespace HealthExam.Infrastructure.Persistence.Repositories;

public sealed class ParaclinicalRepository : IParaclinicalRepository
{
    private readonly HealthExamDbContext _db;

    public ParaclinicalRepository(HealthExamDbContext db)
    {
        _db = db;
    }

    public async Task<ParaclinicalOrder> GetAsync(
        string divisionId, Guid orderId, bool includeItems = true, bool forUpdate = false, CancellationToken ct = default)
    {
        IQueryable<ParaclinicalOrder> query = _db.ParaclinicalOrders;
        if (forUpdate)
        {
            query = query.AsTracking();
        }

        if (includeItems)
        {
            query = query.Include(x => x.Items);
        }

        return await query.FirstOrDefaultAsync(
            x => x.OrderID == orderId && x.DivisionID == divisionId, ct);
    }

    public async Task<IReadOnlyList<ParaclinicalOrder>> ListByRecordAsync(
        string divisionId, Guid recordId, bool includeCancelled = true, bool forUpdate = false, CancellationToken ct = default)
    {
        IQueryable<ParaclinicalOrder> query = _db.ParaclinicalOrders;
        if (forUpdate)
        {
            query = query.AsTracking();
        }

        query = query
            .Where(x => x.RecordID == recordId && x.DivisionID == divisionId && x.IsActive)
            .Include(x => x.Items)
            .OrderByDescending(x => x.OrderedAt);

        var orders = await query.ToListAsync(ct);
        return orders;
    }

    public async Task<IReadOnlyList<ExamPackageService>> ListActivePackageServicesAsync(
        string divisionId, Guid packageId, CancellationToken ct = default)
    {
        return await _db.ExamPackageServices
            .Where(x => x.PackageID == packageId && x.IsActive)
            .OrderBy(x => x.OrderNo)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyDictionary<long, ExamPackageService>> SnapshotServicesAsync(
        string divisionId, IReadOnlyCollection<long> serviceIds, CancellationToken ct = default)
    {
        if (serviceIds == null || serviceIds.Count == 0)
            return new Dictionary<long, ExamPackageService>();

        var rows = await _db.ExamPackageServices
            .Where(x => x.IsActive
                     && x.Package.IsActive
                     && x.Package.DivisionID == divisionId
                     && serviceIds.Contains(x.ServiceID))
            .ToListAsync(ct);

        return rows
            .GroupBy(x => x.ServiceID)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.ModifiedDate).First());
    }

    public async Task<ExamPackage> GetPackageAsync(
        string divisionId, Guid packageId, CancellationToken ct = default)
    {
        return await _db.ExamPackages
            .FirstOrDefaultAsync(x => x.PackageID == packageId && x.DivisionID == divisionId, ct);
    }

    public async Task<IReadOnlyList<long>> ListActiveServiceIdsByRecordAsync(
        string divisionId, Guid recordId, CancellationToken ct = default)
    {
        return await _db.ParaclinicalOrderItems
            .Where(x => x.RecordID == recordId
                     && x.DivisionID == divisionId
                     && x.State != ParaclinicalItemState.Cancelled)
            .Select(x => x.ServiceID)
            .ToListAsync(ct);
    }

    public async Task<(bool Satisfied, int Pending, int Total)> EvaluateConditionBAsync(
        string divisionId, Guid recordId, CancellationToken ct = default)
    {
        var counts = await _db.ParaclinicalOrderItems
            .Where(x => x.RecordID == recordId && x.DivisionID == divisionId)
            .GroupBy(x => x.State)
            .Select(g => new { State = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var total = counts.Sum(x => x.Count);
        var pending = counts
            .Where(x => ParaclinicalOrderItem.BlocksConditionB(x.State))
            .Sum(x => x.Count);

        return (pending == 0, pending, total);
    }

    public async Task<ParaclinicalOrderItem> GetItemByVendorLineNoAsync(
        string divisionId, long vendorLineNo, bool includeOrder = true, bool forUpdate = false, CancellationToken ct = default)
    {
        IQueryable<ParaclinicalOrderItem> query = _db.ParaclinicalOrderItems;
        if (forUpdate)
        {
            query = query.AsTracking();
        }
        if (includeOrder)
        {
            query = query.Include(x => x.Order);
        }
        return await query.FirstOrDefaultAsync(
            x => x.DivisionID == divisionId && x.VendorLineNo == vendorLineNo, ct);
    }

    public async Task<IReadOnlyList<ParaclinicalResult>> ListResultsByOrderAsync(
        string divisionId, Guid orderId, CancellationToken ct = default)
    {
        return await _db.ParaclinicalResults
            .Where(x => x.DivisionID == divisionId && x.OrderId == orderId)
            .Include(x => x.Items)
            .OrderByDescending(x => x.ResultDate)
            .ToListAsync(ct);
    }

    public async Task<ParaclinicalResult> GetResultAsync(
        string divisionId, Guid resultId, CancellationToken ct = default)
    {
        return await _db.ParaclinicalResults
            .Where(x => x.DivisionID == divisionId && x.ResultId == resultId)
            .Include(x => x.Items)
            .FirstOrDefaultAsync(ct);
    }

    public void Add(ParaclinicalOrder order)
    {
        _db.ParaclinicalOrders.Add(order);
    }

    public void AddResult(ParaclinicalResult result)
    {
        _db.ParaclinicalResults.Add(result);
    }
}
