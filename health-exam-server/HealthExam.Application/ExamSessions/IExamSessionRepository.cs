using System;
using System.Threading;
using System.Threading.Tasks;
using HealthExam.Application.Common;
using HealthExam.Domain.Catalogs;
using HealthExam.Domain.ExamSessions;

namespace HealthExam.Application.ExamSessions;

public interface IExamSessionRepository
{
    Task<PageResult<ExamSessionResult>> ListAsync(
        string divisionId, ExamSessionFilter filter, CancellationToken ct = default);

    Task<ExamSession> GetAsync(
        string divisionId, Guid sessionId, bool forUpdate = false, CancellationToken ct = default);

    Task<bool> ExistsByCodeAsync(
        string divisionId, string sessionCode, Guid? excludingSessionId = null, CancellationToken ct = default);

    void Add(ExamSession session);

    Task<Organization> GetOrganizationAsync(
        string divisionId, Guid organizationId, CancellationToken ct = default);

    Task<ExamPackage> GetPackageAsync(
        string divisionId, Guid packageId, CancellationToken ct = default);

    Task<int> GetRecordCountAsync(
        string divisionId, Guid sessionId, CancellationToken ct = default);
}
