using HealthExam.Domain.Common;

namespace HealthExam.Application.Common;

/// <summary>
/// Ngữ cảnh một request. Chép nguyên nguyên tắc của form-server: DivisionID lấy từ HEADER,
/// KHÔNG tin token — hệ thống chạy nhiều DB tenant, pilot dùng chung DB với prod, và cùng
/// một mã đợt khám ở hai tenant có nghĩa hoàn toàn khác nhau.
///
/// Chủ thể không mặc định là nhân viên: UC04 (cổng web người bệnh) sẽ có ActorKind=Patient,
/// nên ActorId luôn đi kèm ActorKind.
/// </summary>
public interface IHealthExamContext
{
    string TraceId { get; }
    string DivisionId { get; }
    string ModuleCode { get; }

    long ActorId { get; }
    ActorKind ActorKind { get; }
    string ActorName { get; }

    /// <summary>Mã nhân viên. sign-server và ssm-server tra chứng thư theo mã, không theo ID.</summary>
    string ActorCode { get; }

    /// <summary>health-exam:employee | form:patient | health-exam:service</summary>
    string Scope { get; }

    /// <summary>Đọc an toàn header từ request đến (vd. Authorization, Token) để chuyển tiếp downstream.</summary>
    string GetRequestHeader(string name);
}
