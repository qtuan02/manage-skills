namespace HealthExam.API.Contracts;

/// <summary>
/// Danh sách option tĩnh để FE dựng bộ lọc màn danh sách hồ sơ KSK.
/// Đây là metadata mock cho UI, không đọc database.
/// </summary>
public sealed class ExamRecordFilterOptions
{
    public IReadOnlyList<ExamRecordStatusOption> Statuses { get; init; } = Array.Empty<ExamRecordStatusOption>();
    public IReadOnlyList<ExamTypeOption> ExamTypes { get; init; } = Array.Empty<ExamTypeOption>();

    public static ExamRecordFilterOptions Create() => new()
    {
        Statuses = new[]
        {
            new ExamRecordStatusOption("ALL", "Tất cả", null),
            new ExamRecordStatusOption("NOT_REGISTERED", "Chưa đăng ký", 0),
            new ExamRecordStatusOption("WAITING", "Chờ khám", 1),
            new ExamRecordStatusOption("IN_PROGRESS", "Đang khám", 2),
            new ExamRecordStatusOption("COMPLETED", "Đã khám", 3),
            // Hai option UI cùng trỏ vào Cancelled vì domain hiện chưa phân biệt loại hủy.
            // Không để null: FE chọn một option vẫn phải lọc được hồ sơ đã hủy, không phải
            // vô tình bỏ điều kiện state và trả toàn bộ danh sách.
            new ExamRecordStatusOption("REGISTRATION_CANCELLED", "Hủy đăng ký", 4),
            new ExamRecordStatusOption("EXAM_CANCELLED", "Hủy khám", 4)
        },
        ExamTypes = new[]
        {
            new ExamTypeOption("ALL", "Tất cả", null),
            new ExamTypeOption("DTK_01", "Trẻ dưới 06 tuổi", "DTK_01"),
            new ExamTypeOption("DTK_02", "Trẻ đủ 06–18 tuổi", "DTK_02"),
            new ExamTypeOption("DTK_03", "Người từ đủ 18 tuổi", "DTK_03"),
            new ExamTypeOption("DTK_04", "Người cao tuổi", "DTK_04"),
            new ExamTypeOption("DTK_05", "Người lái xe, máy chuyên dùng", "DTK_05"),
            new ExamTypeOption("DTK_06", "Người hành nghề lái xe ô tô", "DTK_06"),
            new ExamTypeOption("DTK_07", "Tuyển dụng giao thông đường sắt", "DTK_07"),
            new ExamTypeOption("DTK_08", "Người điều khiển phương tiện đường sắt", "DTK_08"),
            new ExamTypeOption("DTK_09", "Sổ định kỳ thuyền viên", "DTK_09"),
            new ExamTypeOption("DTK_10", "Thuyền viên tàu biển Việt Nam", "DTK_10")
        }
    };
}

public sealed record ExamRecordStatusOption(string Code, string Label, short? State);

public sealed record ExamTypeOption(string Code, string Label, string VariantCode);
