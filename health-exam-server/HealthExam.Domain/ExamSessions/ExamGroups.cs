namespace HealthExam.Domain.ExamSessions;

/// <summary>
/// 10 Nhóm khám (Đối tượng khám) DTK_01..DTK_10 — nguồn: form-service/03-ksk-mapping §2.2.
///
/// Cố ý KHÔNG dựng bảng <c>HEX_ExamGroup</c>: đây là danh mục do Thông tư quy định, chốt
/// cứng trong tài liệu, mỗi mã ứng với đúng một FormCode bên form-server
/// (<c>KSK-V1-DTK_01</c>…). Đưa vào bảng thì phải seed + đồng bộ hai nơi, mà không ai được
/// phép thêm nhóm thứ 11 từ màn quản trị. Khi Thông tư đổi thì sửa ở đây và seed lại form.
/// </summary>
public static class ExamGroups
{
    /// <summary>Tiền tố FormCode bên form-server; ghép với mã nhóm ra FormCode đầy đủ.</summary>
    public const string FormCodePrefix = "KSK-V1-";

    public sealed class ExamGroup
    {
        /// <summary>DTK_01..DTK_10 — chính là FRM_FormDefinition.VariantCode.</summary>
        public string VariantCode { get; init; } = "";
        public string GroupName { get; init; } = "";
        /// <summary>FormCode đầy đủ để FE tra biểu mẫu bên form-server.</summary>
        public string FormCode { get; init; } = "";
        public int OrderNo { get; init; }
    }

    private static ExamGroup G(int no, string code, string name)
        => new() { VariantCode = code, GroupName = name, FormCode = FormCodePrefix + code, OrderNo = no };

    public static readonly IReadOnlyList<ExamGroup> All = new[]
    {
        G(1,  "DTK_01", "Trẻ dưới 06 tuổi"),
        G(2,  "DTK_02", "Trẻ từ đủ 06 đến 18 tuổi"),
        G(3,  "DTK_03", "Người từ đủ 18 tuổi trở lên"),
        G(4,  "DTK_04", "Người cao tuổi"),
        G(5,  "DTK_05", "Người lái xe, người điều khiển xe máy chuyên dùng"),
        G(6,  "DTK_06", "Người hành nghề lái xe ô tô"),
        G(7,  "DTK_07", "Tuyển dụng giao thông đường sắt"),
        G(8,  "DTK_08", "Người điều khiển phương tiện đường sắt"),
        G(9,  "DTK_09", "Sổ định kỳ thuyền viên tàu biển Việt Nam"),
        G(10, "DTK_10", "Thuyền viên trên tàu biển Việt Nam")
    };

    public static bool IsValid(string variantCode)
        => All.Any(x => x.VariantCode == variantCode);

    public static ExamGroup Find(string variantCode)
        => All.FirstOrDefault(x => string.Equals(x.VariantCode, variantCode, StringComparison.OrdinalIgnoreCase));
}
