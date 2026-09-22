namespace HealthExam.Infrastructure.Excel;

/// <summary>
/// Bộ cột của file Excel danh sách đăng ký (UC03.5) — CHỖ DUY NHẤT phát biểu chuẩn này.
///
/// ⚠️ Q-HEX-03 CHƯA CHỐT. Tài liệu nguồn (FRD) không liệt kê tên cột tiếng Việt của file mà
/// đơn vị gửi sang, nên ở đây CỐ Ý KHÔNG ĐOÁN. Cách giải quyết:
///
///   • Tên cột chuẩn = đúng tên trường nghiệp vụ đã có (PatientCode, FullName…). Đây không
///     phải phỏng đoán — nó là hợp đồng do chính service này phát biểu, và
///     <c>GET /imports/template</c> phát ra file mẫu mang đúng bộ tên đó. Người dùng tải mẫu
///     về, điền, nạp lên: chạy được ngay hôm nay, không chờ BA.
///   • Khi BA chốt bộ cột tiếng Việt, chỉ việc thêm bí danh vào <see cref="Aliases"/> của
///     từng cột. Không phải sửa parser, không phải sửa validate, không phải migrate.
///
/// Vì sao không nhận đại mọi tên cột rồi đoán theo thứ tự: file của các đơn vị khác nhau có
/// thứ tự cột khác nhau, và đoán sai thì số CCCD chui vào ô số điện thoại — hỏng lặng lẽ, chỉ
/// phát hiện ra khi người bệnh không đăng nhập được cổng.
/// </summary>
public static class ExamImportColumns
{
    /// <summary>
    /// Một cột logic. <c>Field</c> trùng tên thuộc tính của ExamRecordSaveRequest, nên đọc
    /// bảng này là biết ngay dòng Excel đổ vào đâu.
    /// </summary>
    public class Column
    {
        public string Field { get; init; } = "";

        /// <summary>Thiếu cột này trong FILE thì cả file không dùng được → 4002.</summary>
        public bool HeaderRequired { get; init; }

        /// <summary>Ô rỗng trong một DÒNG thì dòng đó hỏng, các dòng khác vẫn nạp được.</summary>
        public bool ValueRequired { get; init; }

        /// <summary>
        /// Bí danh tiêu đề được chấp nhận, ngoài chính <c>Field</c>.
        /// RỖNG cho tới khi Q-HEX-03 chốt — xem chú thích của lớp.
        /// </summary>
        public string[] Aliases { get; init; } = Array.Empty<string>();

        /// <summary>Mô tả đưa vào dòng ghi chú của file mẫu.</summary>
        public string Note { get; init; } = "";
    }

    /// <summary>
    /// Bắt buộc DUY NHẤT ở mức tiêu đề là <c>FullName</c> — vì đó cũng là trường duy nhất
    /// <c>ExamRecordService.CreateAsync</c> bắt buộc mà file phải cung cấp. Không tự thêm cột
    /// bắt buộc nào khác: mỗi cột bắt buộc là một lý do để cả file 500 dòng bị từ chối.
    ///
    /// <c>VariantCode</c> CÓ THỂ vắng mặt — khi đó mọi dòng lấy Nhóm khám của ĐỢT, đúng cách
    /// mà gói khám đã kế thừa từ đợt trong CreateAsync. Đợt cũng không khai Nhóm khám thì
    /// từng dòng mới hỏng (REQUIRED), chứ không phải cả file.
    /// </summary>
    public static readonly Column[] All =
    {
        new() { Field = "FullName",        HeaderRequired = true, ValueRequired = true, Note = "Họ tên — bắt buộc" },
        new() { Field = "PatientCode",     Note = "Mã người bệnh (nếu đã có mã ở HIS) — bỏ trống thì hệ thống tự cấp" },
        new() { Field = "IdentityNumber",  Note = "Số CCCD/CMND — cần CCCD HOẶC BHYT để đăng nhập cổng người bệnh" },
        new() { Field = "InsuranceNumber", Note = "Số thẻ BHYT — cần CCCD HOẶC BHYT để đăng nhập cổng người bệnh" },
        new() { Field = "Dob",             Note = "Ngày sinh (yyyy-MM-dd hoặc dd/MM/yyyy)" },
        new() { Field = "BirthYear",       Note = "Năm sinh — dùng khi chỉ có năm" },
        new() { Field = "GenderID",        Note = "Giới tính: 1 Nam, 2 Nữ, 3 Khác" },
        new() { Field = "PhoneNumber",     Note = "Số điện thoại" },
        new() { Field = "Email",           Note = "Email" },
        new() { Field = "Address",         Note = "Địa chỉ" },
        new() { Field = "StaffCode",       Note = "Mã nhân viên bên đơn vị" },
        new() { Field = "OrgDeptName",     Note = "Phòng/ban bên đơn vị" },
        new() { Field = "JobTitle",        Note = "Chức danh" },
        new() { Field = "VariantCode",     Note = "Nhóm khám DTK_01..DTK_10 — bỏ trống thì lấy theo đợt" }
    };

    /// <summary>Cột buộc phải có mặt trong dòng tiêu đề, nếu không thì 4002.</summary>
    public static IEnumerable<Column> HeaderRequiredColumns => All.Where(c => c.HeaderRequired);

    /// <summary>
    /// Khớp một tiêu đề trong file với cột logic. So sau khi cắt trắng và bỏ phân biệt hoa
    /// thường: file do người gõ tay, "Fullname " và "FullName" là cùng một cột.
    /// </summary>
    public static Column Resolve(string header)
    {
        header = (header ?? "").Trim();
        if (header.Length == 0) return null;

        return All.FirstOrDefault(c =>
            string.Equals(c.Field, header, StringComparison.OrdinalIgnoreCase) ||
            c.Aliases.Any(a => string.Equals(a, header, StringComparison.OrdinalIgnoreCase)));
    }
}
