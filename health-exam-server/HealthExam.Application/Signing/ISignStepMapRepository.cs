using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using HealthExam.Domain.ExamForms;

namespace HealthExam.Application.Signing;

public interface ISignStepMapRepository
{
    /// <summary>Các bước ký đang bật của một bộ biểu mẫu, sắp theo SWStep tăng dần.</summary>
    Task<IReadOnlyList<SignStepMap>> ListAsync(string divisionId, string variantCode, CancellationToken ct = default);
}
