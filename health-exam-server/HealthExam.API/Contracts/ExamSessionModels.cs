using HealthExam.Domain.Common;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace HealthExam.API.Contracts;

/// <summary>Dòng danh sách đợt khám — GET /v1/exam-sessions.</summary>
public class ExamSessionItem
{
    public Guid SessionID { get; set; }
    public string SessionCode { get; set; } = "";
    public string SessionName { get; set; } = "";
    public Guid? OrganizationID { get; set; }
    public string OrganizationName { get; set; } = "";
    public string ContractNo { get; set; } = "";
    public DateOnly? ContractDate { get; set; }
    public DateOnly ExamDate { get; set; }
    public DateOnly? ExamDateTo { get; set; }
    public string ExamPlace { get; set; } = "";
    public Guid? PackageID { get; set; }
    public string PackageName { get; set; } = "";
    public string VariantCode { get; set; }
    public ExamSessionState State { get; set; }
    /// <summary>Tên trạng thái tiếng Việt — FE hiển thị thẳng, không phải giữ bảng map thứ hai.</summary>
    public string StateName { get; set; } = "";
    public int ExpectedCount { get; set; }
    /// <summary>Số hồ sơ đã đăng ký trong đợt (đếm thật, không phải cache).</summary>
    public int RecordCount { get; set; }
    public string Note { get; set; }
    public bool IsActive { get; set; }
}

/// <summary>Body tạo/sửa đợt khám. Dùng chung cho POST và PUT.</summary>
public class ExamSessionSaveRequest
{
    public string SessionCode { get; set; }
    public string SessionName { get; set; }
    public Guid? OrganizationID { get; set; }
    public string ContractNo { get; set; }
    public DateOnly? ContractDate { get; set; }
    public DateOnly? ExamDate { get; set; }
    public DateOnly? ExamDateTo { get; set; }
    public string ExamPlace { get; set; }
    public int? DepartmentID { get; set; }
    public Guid? PackageID { get; set; }
    public string VariantCode { get; set; }

    public int? ExpectedCount { get; set; }
    public string Note { get; set; }

    /// <summary>
    /// ⚠️ KHÔNG phải trường nghiệp vụ — đây là cái bẫy bắt khoá lạ.
    ///
    /// Trường `State` ĐÃ BỎ khỏi body: đóng/mở đợt nay đi qua POST /close và POST /reopen
    /// (02-api-spec §3.1). Nhưng nếu chỉ xoá thuộc tính, Newtonsoft sẽ LẶNG LẼ bỏ qua khoá
    /// `State` mà FE cũ vẫn gửi — client nhận 200 và tưởng đợt đã đóng, trong khi không có gì
    /// xảy ra. Gom khoá lạ vào đây để ExamSessionService.RejectStateField ném 4001 kèm chỉ
    /// đường sang endpoint đúng.
    ///
    /// Chỉ `State` bị từ chối; khoá lạ khác vẫn được bỏ qua như cũ để FE không vỡ vì một
    /// trường thừa vô hại.
    /// </summary>
    [JsonExtensionData]
    public IDictionary<string, JToken> ExtraFields { get; set; }
}

/// <summary>Body POST /v1/exam-sessions/{sessionId}/close. Lý do là tuỳ chọn — đóng đợt là việc bình thường.</summary>
public class ExamSessionCloseRequest
{
    public string Reason { get; set; }
}

/// <summary>
/// Body POST /v1/exam-sessions/{sessionId}/reopen.
/// Lý do BẮT BUỘC (02-api-spec §3.1) — thiếu thì 4001.
/// </summary>
public class ExamSessionReopenRequest
{
    public string Reason { get; set; }
}
