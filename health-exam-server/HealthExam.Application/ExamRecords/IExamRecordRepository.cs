using System;
using System.Threading;
using System.Threading.Tasks;
using HealthExam.Application.Common;
using HealthExam.Domain.Catalogs;
using HealthExam.Domain.ExamRecords;
using HealthExam.Domain.ExamSessions;

namespace HealthExam.Application.ExamRecords;

public interface IExamRecordRepository
{
    Task<PageResult<ExamRecordResult>> ListAsync(
        string divisionId, ExamRecordFilter filter, CancellationToken ct = default);

    /// <summary>
    /// Các hồ sơ KSK ĐÃ KÝ KẾT LUẬN của một dòng hồ sơ người bệnh (mọi phiên bản cùng
    /// ProfileLineageID), mới nhất lên đầu. page/size phải đã chuẩn hoá (>= 1) từ handler.
    /// </summary>
    Task<PageResult<ExamRecordResult>> ListSignedByPatientLineageAsync(
        string divisionId, Guid profileLineageID, int page, int size, CancellationToken ct = default);

    Task<ExamRecord> GetAsync(
        string divisionId, Guid recordId, bool forUpdate = false, CancellationToken ct = default);

    Task<ExamRecord> GetWithRegistrationAsync(
        string divisionId, Guid recordId, bool forUpdate = false, CancellationToken ct = default);

    Task<ExamRecordResult> GetResultAsync(
        string divisionId, Guid recordId, CancellationToken ct = default);

    Task<bool> ExistsInSessionAsync(
        string divisionId, Guid sessionId, string patientCode, Guid? excludingRecordId = null, CancellationToken ct = default);

    Task<bool> ExistsDuplicateAsync(
        string divisionId, Guid sessionId, string identityNumber, string patientCode, Guid? excludingRecordId = null, CancellationToken ct = default);

    Task<ExamSession> GetOrCreateDefaultSessionAsync(
        string divisionId, CancellationToken ct = default);

    Task<ExamSessionProgressResult> GetProgressAsync(
        string divisionId, Guid sessionId, CancellationToken ct = default);

    Task<ExamRecordResult> VerifyPortalCredentialsAsync(
        string divisionId, string patientCode, string identifier, CancellationToken ct = default);

    Task<ExamRecordResult> VerifyPortalCredentialsAsync(
        string divisionId, string patientCode, string identityNumber, string insuranceNumber, CancellationToken ct = default);

    Task<ExamPackage> GetPackageAsync(
        string divisionId, Guid packageId, CancellationToken ct = default);

    Task<(string Code, string Name)?> ResolveMasterDataAsync(
        string divisionId, string category, string code, CancellationToken ct = default);

    Task<MasterDataOption> ResolveMasterDataOptionAsync(
        string divisionId, string category, string code, CancellationToken ct = default);

    Task<(string Code, string Name)?> ResolveWardAsync(
        string divisionId, string provinceCode, string wardCode, CancellationToken ct = default);

    Task<ExamRecord> ResolveForWebhookAsync(
        string divisionId,
        Guid? submissionId,
        string subjectId,
        string hostRefId,
        bool forUpdate = true,
        CancellationToken ct = default);

    Task<ExamRecord> LockRecordAsync(Guid recordId, CancellationToken ct = default);

    Task<IReadOnlyList<ReconcileCandidate>> FindReconcileCandidatesAsync(
        string divisionId = null,
        Guid? sessionId = null,
        int batchSize = 100,
        CancellationToken ct = default);

    void Add(ExamRecord record);
}

public sealed record ReconcileCandidate(
    Guid RecordID,
    Guid SubmissionID,
    string DivisionID,
    string RecordCode);
