using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HealthExam.Application.Imports;
using HealthExam.Domain.Imports;
using Newtonsoft.Json;
using OfficeOpenXml;
using OfficeOpenXml.Style;

namespace HealthExam.Infrastructure.Excel;

public class ExamWorkbookReader : IExamWorkbookReader
{
    private const string ReasonHeader = "Lý do";

    static ExamWorkbookReader()
    {
        ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
    }

    public Task<ParsedImportSheet> ReadAsync(Stream file, string fileName = "", CancellationToken ct = default)
    {
        var sheet = ExamImportSheet.Read(file);
        var parsedRows = sheet.Rows.Select(r => new ParsedImportRow(
            r.RowNo,
            r.Raw,
            r.Fields,
            r.IsBlank)).ToList();

        var result = new ParsedImportSheet(sheet.Name, parsedRows);
        return Task.FromResult(result);
    }

    public Task<byte[]> CreateTemplateAsync(CancellationToken ct = default)
        => Task.FromResult(BuildTemplate());

    public Task<byte[]> CreateErrorWorkbookAsync(IReadOnlyList<ImportRowData> invalidRows, CancellationToken ct = default)
        => Task.FromResult(BuildErrorFile(invalidRows));

    /// <summary>File mẫu: một dòng tiêu đề + một dòng ghi chú giải thích từng cột.</summary>
    public static byte[] BuildTemplate()
    {
        using var package = new ExcelPackage();
        var ws = package.Workbook.Worksheets.Add("DanhSachDangKy");

        for (var i = 0; i < ExamImportColumns.All.Length; i++)
        {
            var col = ExamImportColumns.All[i];
            ws.Cells[1, i + 1].Value = col.Field;
            ws.Cells[1, i + 1].Style.Font.Bold = true;
            // Cột bắt buộc tô khác để người điền thấy ngay, không phải đọc dòng ghi chú.
            if (col.HeaderRequired)
            {
                ws.Cells[1, i + 1].Style.Fill.PatternType = ExcelFillStyle.Solid;
                ws.Cells[1, i + 1].Style.Fill.BackgroundColor.SetColor(Color.LightYellow);
            }

            ws.Cells[2, i + 1].Value = col.Note;
            ws.Cells[2, i + 1].Style.Font.Italic = true;
        }

        // Dòng 2 là ghi chú, KHÔNG phải dữ liệu. Người dùng xoá đi rồi điền từ dòng 2 cũng
        // được: bộ đọc bỏ qua dòng trắng và bắt lỗi từng dòng, nên dòng ghi chú sót lại chỉ
        // thành một dòng hỏng có lý do rõ ràng chứ không làm hỏng cả file.
        ws.Cells[ws.Dimension.Address].AutoFitColumns(12, 40);
        return package.GetAsByteArray();
    }

    /// <summary>
    /// File các dòng lỗi: nguyên dòng gốc + cột <c>Lý do</c> ở cuối.
    ///
    /// Giữ đúng thứ tự cột của file người dùng gửi lên (đọc từ RawData) chứ không sắp lại theo
    /// bộ cột chuẩn — họ sẽ sửa trên chính file này rồi nạp lại, đổi thứ tự cột là bắt họ dò lại.
    /// </summary>
    public static byte[] BuildErrorFile(IEnumerable<ImportRowData> rows)
    {
        using var package = new ExcelPackage();
        var ws = package.Workbook.Worksheets.Add("DongLoi");

        var parsed = rows.Select(r => new
        {
            r.RowNo,
            r.ErrorMessage,
            Raw = JsonConvert.DeserializeObject<Dictionary<string, string>>(r.RawData ?? "{}")
                  ?? new Dictionary<string, string>()
        }).ToList();

        // Hợp các tiêu đề đã gặp, giữ thứ tự xuất hiện lần đầu.
        var headers = new List<string>();
        foreach (var p in parsed)
            foreach (var h in p.Raw.Keys)
                if (!headers.Contains(h)) headers.Add(h);

        ws.Cells[1, 1].Value = "Dòng";
        for (var i = 0; i < headers.Count; i++) ws.Cells[1, i + 2].Value = headers[i];
        ws.Cells[1, headers.Count + 2].Value = ReasonHeader;
        ws.Cells[1, 1, 1, headers.Count + 2].Style.Font.Bold = true;

        for (var r = 0; r < parsed.Count; r++)
        {
            var p = parsed[r];
            ws.Cells[r + 2, 1].Value = p.RowNo;
            for (var i = 0; i < headers.Count; i++)
                ws.Cells[r + 2, i + 2].Value = p.Raw.GetValueOrDefault(headers[i], "");
            ws.Cells[r + 2, headers.Count + 2].Value = p.ErrorMessage;
        }

        if (ws.Dimension != null) ws.Cells[ws.Dimension.Address].AutoFitColumns(10, 60);
        return package.GetAsByteArray();
    }

    public static byte[] BuildErrorFile(IEnumerable<ImportBatchRow> rows)
    {
        return BuildErrorFile(rows.Select(r => new ImportRowData(
            r.ImportRowID,
            r.BatchID,
            r.RowNo,
            r.RawData,
            r.IsValid,
            r.ErrorCode,
            r.ErrorMessage,
            r.RecordID)));
    }
}

public static class ExamImportWorkbook
{
    public static byte[] BuildTemplate() => ExamWorkbookReader.BuildTemplate();
    public static byte[] BuildErrorFile(IEnumerable<ImportBatchRow> rows) => ExamWorkbookReader.BuildErrorFile(rows);
    public static byte[] BuildErrorFile(IEnumerable<ImportRowData> rows) => ExamWorkbookReader.BuildErrorFile(rows);
}
