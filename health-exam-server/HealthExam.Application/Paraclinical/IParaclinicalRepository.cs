using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using HealthExam.Domain.Catalogs;
using HealthExam.Domain.Paraclinical;

namespace HealthExam.Application.Paraclinical;

public interface IParaclinicalRepository
{
    Task<ParaclinicalOrder> GetAsync(string divisionId, Guid orderId, bool includeItems = true, bool forUpdate = false, CancellationToken ct = default);
    Task<IReadOnlyList<ParaclinicalOrder>> ListByRecordAsync(string divisionId, Guid recordId, bool includeCancelled = true, bool forUpdate = false, CancellationToken ct = default);
    Task<IReadOnlyList<ExamPackageService>> ListActivePackageServicesAsync(string divisionId, Guid packageId, CancellationToken ct = default);
    Task<IReadOnlyDictionary<long, ExamPackageService>> SnapshotServicesAsync(string divisionId, IReadOnlyCollection<long> serviceIds, CancellationToken ct = default);
    Task<ExamPackage> GetPackageAsync(string divisionId, Guid packageId, CancellationToken ct = default);
    Task<IReadOnlyList<long>> ListActiveServiceIdsByRecordAsync(string divisionId, Guid recordId, CancellationToken ct = default);
    Task<(bool Satisfied, int Pending, int Total)> EvaluateConditionBAsync(string divisionId, Guid recordId, CancellationToken ct = default);
    Task<ParaclinicalOrderItem> GetItemByVendorLineNoAsync(string divisionId, long vendorLineNo, bool includeOrder = true, bool forUpdate = false, CancellationToken ct = default);
    Task<IReadOnlyList<ParaclinicalResult>> ListResultsByOrderAsync(string divisionId, Guid orderId, CancellationToken ct = default);
    Task<ParaclinicalResult> GetResultAsync(string divisionId, Guid resultId, CancellationToken ct = default);
    void Add(ParaclinicalOrder order);
    void AddResult(ParaclinicalResult result);
}
