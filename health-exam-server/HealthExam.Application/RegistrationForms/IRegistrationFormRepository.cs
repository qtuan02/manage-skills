using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using HealthExam.Domain.ExamForms;
using HealthExam.Domain.ExamRecords;

namespace HealthExam.Application.RegistrationForms;

public interface IRegistrationFormRepository
{
    Task<ExamGroupFormMapping> GetActiveMappingAsync(string divisionId, string variantCode, CancellationToken ct = default);
    Task<IReadOnlyList<string>> ListActiveVariantCodesAsync(string divisionId, CancellationToken ct = default);
    Task<ExamRecord> GetRecordAsync(string divisionId, Guid recordId, bool forUpdate = false, CancellationToken ct = default);
    Task<ExamRecord> GetRecordWithSessionAsync(string divisionId, Guid recordId, bool forUpdate = false, CancellationToken ct = default);
}
