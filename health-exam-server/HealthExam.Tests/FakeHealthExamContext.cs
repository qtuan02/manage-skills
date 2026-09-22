using HealthExam.API.Contracts;
using HealthExam.API.Middlewares;
using HealthExam.Application.Common;
using HealthExam.Domain.Common;
using HealthExam.Domain.ExamSessions;
using HealthExam.Domain.ExamRecords;
using HealthExam.Domain.Imports;
using HealthExam.Domain.Paraclinical;
using HealthExam.Domain.Webhooks;
using HealthExam.Domain.Integrations;
using HealthExam.Domain.Catalogs;

namespace HealthExam.Tests;

/// <summary>
/// Ngữ cảnh request giả, dùng cho test gọi THẲNG tầng service (không qua HTTP).
///
/// Vì sao cần: AuditService và mọi service đều đọc danh tính + tenant qua IHealthExamContext.
/// Bản thật (HealthExamRequestContext) bám vào HttpContext, nên muốn chốt "dòng audit ghi
/// đúng ai, đúng TraceID nào" mà không phải dựng cả web host thì phải có bản giả tự đặt giá trị.
///
/// ActorId cố ý lấy đúng AuthTestHost.EmployeeId — cùng một nhân viên với token của test HTTP,
/// nên đọc chéo hai bộ test không phải nhớ hai con số khác nhau.
///
/// ⚠️ Bản giả này KHÔNG chứng minh được "ActorId đọc đúng claim EmployeeID của token": đoạn đó
/// do AuthGuardTests (H-1) chốt qua HTTP. Ở đây chốt nửa còn lại của chuỗi — service/audit chép
/// đúng ActorId của ngữ cảnh chứ không ghi 0.
/// </summary>
public class FakeHealthExamContext : IHealthExamContext
{
    public const string DefaultDivisionId = "DEV";
    public const long DefaultActorId = AuthTestHost.EmployeeId;
    public const string DefaultTraceId = "TRACE-TEST-0001";

    public string TraceId { get; set; } = DefaultTraceId;
    public string DivisionId { get; set; } = DefaultDivisionId;
    public string ModuleCode { get; set; } = ModuleCodes.HealthExam;

    public long ActorId { get; set; } = DefaultActorId;
    public ActorKind ActorKind { get; set; } = ActorKind.Employee;
    public string ActorName { get; set; } = AuthTestHost.UserName;
    public string ActorCode { get; set; } = "NV001";

    public string Scope { get; set; } = "health-exam:employee";

    public Dictionary<string, string> Headers { get; } = new(StringComparer.OrdinalIgnoreCase);

    public string GetRequestHeader(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "";
        return Headers.TryGetValue(name, out var val) ? val : "";
    }
}
