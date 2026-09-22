namespace HealthExam.Domain.Common;

/// <summary>
/// Mã hệ thống đã chốt của module Khám sức khoẻ. Gom về một chỗ vì cùng lý do form-server
/// gom <c>HostRefTypes</c>: chuỗi rải rác trong code/script thì mỗi chỗ tự đặt một biến thể
/// ("KSK", "HEALTH_CHECK"…), mà không có bảng tham chiếu nào chặn — sai chính tả chỉ lộ ra
/// khi phiếu bên form-server không tra ngược được về hồ sơ nào.
/// </summary>
/// <remarks>
/// "KSK" chỉ là cách gọi tắt trong văn tiếng Việt. MÃ TRONG HỆ THỐNG luôn tiếng Anh.
/// </remarks>
public static class ModuleCodes
{
    /// <summary>Giá trị của header X-Module-Code và của FRM_Submission.ModuleCode.</summary>
    public const string HealthExam = "HEALTH_EXAM";

    /// <summary>
    /// FRM_Submission.HostRefType — phiếu KSK neo vào ĐỢT/HỒ SƠ khám sức khoẻ, không phải
    /// lượt nhập viện. Phải khớp đúng chuỗi với Form.Core/Models/HostRefTypes.cs.
    /// </summary>
    public const string HostRefType = "HEALTH_EXAM_SESSION";
}
