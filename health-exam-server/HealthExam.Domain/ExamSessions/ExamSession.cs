using HealthExam.Domain.Common;
using HealthExam.Domain.Catalogs;

namespace HealthExam.Domain.ExamSessions;

/// <summary>HEX_ExamSession — đợt khám (01-db-model §4.1).</summary>
public class ExamSession : AuditableEntity
{
    public Guid SessionID { get; set; }
    public string DivisionID { get; set; } = "";

    /// <summary>Mã đợt khám — đẩy sang form-server làm ContextValues (SessionCode).</summary>
    public string SessionCode { get; set; } = "";
    public string SessionName { get; set; } = "";

    public Guid? OrganizationID { get; set; }

    /// <summary>
    /// ★ Ảnh chụp tên đơn vị tại thời điểm mở đợt. Bốn giá trị đẩy vào ContextValues của
    /// form-server phải BẤT BIẾN theo đợt: đơn vị đổi tên sau ba tháng thì phiếu của đợt cũ
    /// vẫn phải in ra tên cũ. Đọc live từ HEX_Organization thì mọi phiếu cũ đổi theo — sai
    /// nghiệp vụ và không sửa được sau khi đã ký số.
    /// </summary>
    public string OrganizationName { get; set; } = "";

    public string ContractNo { get; set; } = "";
    public DateOnly? ContractDate { get; set; }

    /// <summary>
    /// Ngày khám. Kiểu DateOnly → cột `date`, KHÔNG phải timestamptz: ngày khám là ngày
    /// lịch, không có giờ. Lưu timestamptz thì đợt mở lúc 23:30 sẽ hiển thị lệch một ngày ở
    /// nơi khác — đúng lớp lỗi lệch 7h đã gặp ở các *-connect-server.
    /// </summary>
    public DateOnly ExamDate { get; set; }
    public DateOnly? ExamDateTo { get; set; }

    public string ExamPlace { get; set; } = "";
    public int DepartmentID { get; set; }

    public Guid? PackageID { get; set; }
    /// <summary>★ Ảnh chụp — cùng lý do với OrganizationName.</summary>
    public string PackageName { get; set; } = "";

    /// <summary>Nhóm khám mặc định của đợt (DTK_01..DTK_10).</summary>
    public string VariantCode { get; set; }

    public ExamSessionState State { get; set; } = ExamSessionState.Draft;

    /// <summary>Số người dự kiến theo hợp đồng.</summary>
    public int ExpectedCount { get; set; }

    /// <summary>
    /// ★ Bộ đếm số thứ tự hồ sơ ĐÃ CẤP của riêng đợt này — nguồn sinh {SessionCode}-0001,
    /// -0002… Cấp số bằng một câu UPDATE ... RETURNING trên chính hàng này
    /// (ExamRecordService.AllocateRecordNoAsync), KHÔNG phải đếm hồ sơ rồi cộng một: hai
    /// request song song đếm cùng lúc thì ra cùng một số, nạp Excel hàng trăm dòng là hỏng
    /// cả lô.
    ///
    /// Vì sao là cột trên đợt chứ không phải một sequence của PostgreSQL: mã hồ sơ đánh số
    /// LẠI TỪ 1 cho mỗi đợt. Sequence dùng chung thì đợt B vừa mở đã bắt đầu từ -0005.
    ///
    /// Bộ đếm chỉ tăng, không tái sử dụng số của hồ sơ đã xoá/hủy — hai người mang cùng một
    /// mã hồ sơ ở hai thời điểm là thứ không truy vết nổi khi phiếu đã in và đã ký số.
    /// </summary>
    public int LastRecordNo { get; set; }

    public string Note { get; set; }
    public bool IsActive { get; set; } = true;

    public Organization Organization { get; set; }
    public ExamPackage Package { get; set; }

    public DomainResult Close()
    {
        if (State == ExamSessionState.Closed)
            return DomainResult.Reject(DomainFailureCode.InvalidTransition, $"Đợt khám {SessionCode} đã đóng từ trước");
        if (State == ExamSessionState.Cancelled)
            return DomainResult.Reject(DomainFailureCode.InvalidTransition, $"Đợt khám {SessionCode} đã hủy, không đóng được");

        State = ExamSessionState.Closed;
        return DomainResult.Apply();
    }

    public DomainResult Reopen(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
            return DomainResult.Reject(DomainFailureCode.RequiredValue, "Mở lại đợt khám phải có lý do");
        if (State != ExamSessionState.Closed)
            return DomainResult.Reject(DomainFailureCode.InvalidTransition, $"Đợt khám đang ở trạng thái \"{StateNames.Of(State)}\", chỉ đợt \"Đã đóng\" mới mở lại được");

        State = ExamSessionState.Open;
        return DomainResult.Apply();
    }
}

file static class StateNames
{
    public static string Of(ExamSessionState state) => state switch
    {
        ExamSessionState.Draft => "Nháp",
        ExamSessionState.Open => "Đang mở",
        ExamSessionState.InProgress => "Đang khám",
        ExamSessionState.Closed => "Đã đóng",
        ExamSessionState.Cancelled => "Hủy",
        _ => ""
    };
}
