using HealthExam.Application.Common;
using HealthExam.Domain.Common;
using HealthExam.Domain.ExamSessions;
using HealthExam.Domain.ExamRecords;
using HealthExam.Domain.Imports;
using HealthExam.Domain.Paraclinical;
using HealthExam.Domain.Webhooks;
using HealthExam.Domain.Integrations;
using HealthExam.Domain.Catalogs;
using OfficeOpenXml;

namespace HealthExam.Infrastructure.Excel;

/// <summary>
/// Đọc file Excel thành dữ liệu thuần — tách khỏi <see cref="ExamImportService"/> để phần
/// nghiệp vụ (kiểm tra từng dòng, chống trùng, ghi hồ sơ) test được mà không cần dựng file.
///
/// Lớp này CHỈ đọc, không phán xét gì ngoài "file có mở được không" và "có đủ cột bắt buộc
/// không". Mọi luật về nội dung nằm ở ExamImportService.
/// </summary>
public static class ExamImportSheet
{
    static ExamImportSheet()
    {
        // Bắt buộc từ EPPlus 5 trở đi. Đặt đúng một lần ở đây thay vì rải trong từng hàm như
        // his-server đang làm (4 chỗ) — quên một chỗ thì lỗi chỉ nổ ở đúng nhánh ít chạy nhất.
        ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
    }

    /// <summary>Một dòng dữ liệu: số dòng trong file + nội dung theo tiêu đề gốc.</summary>
    public class Row
    {
        public int RowNo { get; init; }

        /// <summary>Khoá = tiêu đề NGUYÊN VĂN trong file. Giữ nguyên để dựng lại file lỗi cho người dùng.</summary>
        public Dictionary<string, string> Raw { get; init; } = new();

        /// <summary>Khoá = tên trường nghiệp vụ đã khớp qua ExamImportColumns. Bỏ cột không nhận ra.</summary>
        public Dictionary<string, string> Fields { get; init; } = new();

        /// <summary>Dòng trắng hoàn toàn — bỏ qua, không tính là dòng hỏng.</summary>
        public bool IsBlank => Raw.Values.All(string.IsNullOrWhiteSpace);
    }

    public class Sheet
    {
        public string Name { get; init; } = "";
        public List<Row> Rows { get; init; } = new();
    }

    /// <summary>
    /// Mở file và đọc sheet ĐẦU TIÊN. Ném 4002 khi cả file không dùng được: không mở được,
    /// không có sheet, không có dòng tiêu đề, hoặc thiếu cột bắt buộc.
    ///
    /// Chỉ sheet đầu tiên: file đơn vị gửi sang hay kèm thêm sheet hướng dẫn/danh mục. Đoán
    /// "sheet nào là sheet dữ liệu" thì đoán sai một lần là nạp nhầm cả lô.
    /// </summary>
    public static Sheet Read(Stream stream)
    {
        ExcelPackage package;
        try
        {
            package = new ExcelPackage(stream);
        }
        catch (Exception ex)
        {
            throw HealthExamException.FileInvalid($"Không đọc được file Excel: {ex.Message}");
        }

        using (package)
        {
            var ws = package.Workbook?.Worksheets.FirstOrDefault()
                     ?? throw HealthExamException.FileInvalid("File không có sheet nào");

            var dim = ws.Dimension
                      ?? throw HealthExamException.FileInvalid($"Sheet \"{ws.Name}\" trống");

            // Dòng tiêu đề là dòng đầu tiên CÓ dữ liệu — file của đơn vị hay có vài dòng
            // tiêu đề trang trí ("DANH SÁCH KHÁM SỨC KHOẺ CÔNG TY ABC") ở trên cùng.
            var headerRow = 0;
            var headers = new Dictionary<int, string>();
            for (var r = dim.Start.Row; r <= dim.End.Row; r++)
            {
                var found = new Dictionary<int, string>();
                for (var c = dim.Start.Column; c <= dim.End.Column; c++)
                {
                    var text = (ws.Cells[r, c].Text ?? "").Trim();
                    if (text.Length > 0 && ExamImportColumns.Resolve(text) != null) found[c] = text;
                }

                if (found.Count > 0)
                {
                    headerRow = r;
                    // Lấy TOÀN BỘ ô của dòng tiêu đề, kể cả cột không nhận ra: RawData phải
                    // giữ nguyên dòng gốc, gồm cả phần ta không hiểu.
                    for (var c = dim.Start.Column; c <= dim.End.Column; c++)
                    {
                        var text = (ws.Cells[r, c].Text ?? "").Trim();
                        if (text.Length > 0) headers[c] = text;
                    }
                    break;
                }
            }

            if (headerRow == 0)
                throw HealthExamException.FileInvalid(
                    "Không tìm thấy dòng tiêu đề. Tải file mẫu tại GET /v1/exam-sessions/{sessionId}/imports/template");

            var missing = ExamImportColumns.HeaderRequiredColumns
                .Where(rc => !headers.Values.Any(h => ExamImportColumns.Resolve(h)?.Field == rc.Field))
                .Select(rc => rc.Field)
                .ToList();

            if (missing.Count > 0)
                throw HealthExamException.FileInvalid(
                    $"Thiếu cột bắt buộc: {string.Join(", ", missing)}",
                    new ValidationErrors
                    {
                        Errors = missing
                            .Select(f => new ValidationError { Field = f, Reason = "Thiếu cột trong file" })
                            .ToList()
                    });

            var sheet = new Sheet { Name = ws.Name };
            for (var r = headerRow + 1; r <= dim.End.Row; r++)
            {
                var row = new Row { RowNo = r };
                foreach (var (col, header) in headers)
                {
                    var text = (ws.Cells[r, col].Text ?? "").Trim();
                    row.Raw[header] = text;

                    var column = ExamImportColumns.Resolve(header);
                    // Cột trùng tên xuất hiện hai lần: giữ giá trị ĐẦU tiên khác rỗng, đừng
                    // để một cột trắng ở cuối file xoá mất giá trị đã đọc được.
                    if (column != null && text.Length > 0 && !row.Fields.ContainsKey(column.Field))
                        row.Fields[column.Field] = text;
                }

                if (!row.IsBlank) sheet.Rows.Add(row);
            }

            return sheet;
        }
    }
}
