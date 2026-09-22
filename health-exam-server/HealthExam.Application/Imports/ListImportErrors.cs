using System.IO;
using System.Threading;
using System.Threading.Tasks;
using HealthExam.Application.Common;

namespace HealthExam.Application.Imports;

public interface IListImportErrorsHandler
{
    Task<ApplicationResult<ImportErrorExportResult>> HandleAsync(
        ListImportErrorsQuery query, CancellationToken ct = default);
}

public sealed class ListImportErrorsHandler : IListImportErrorsHandler
{
    private const string XlsxContentType =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    private readonly IImportRepository _importRepository;
    private readonly IExamWorkbookReader _workbookReader;

    public ListImportErrorsHandler(
        IImportRepository importRepository,
        IExamWorkbookReader workbookReader)
    {
        _importRepository = importRepository;
        _workbookReader = workbookReader;
    }

    public async Task<ApplicationResult<ImportErrorExportResult>> HandleAsync(
        ListImportErrorsQuery query, CancellationToken ct = default)
    {
        var batch = await _importRepository.GetAsync(query.DivisionId, query.ImportId, forUpdate: false, ct);
        if (batch == null)
        {
            return ApplicationResult<ImportErrorExportResult>.Fail(
                ApplicationFailureCode.NotFound, "Không tìm thấy lô nạp");
        }

        var invalidRows = await _importRepository.LoadInvalidRowsAsync(query.DivisionId, query.ImportId, ct);
        var content = await _workbookReader.CreateErrorWorkbookAsync(invalidRows, ct);

        var baseFileName = string.IsNullOrWhiteSpace(batch.FileName) ? "DanhSach" : Path.GetFileNameWithoutExtension(batch.FileName);
        var fileName = $"DongLoi_{baseFileName}.xlsx";

        return ApplicationResult<ImportErrorExportResult>.Success(
            new ImportErrorExportResult(content, XlsxContentType, fileName));
    }
}
