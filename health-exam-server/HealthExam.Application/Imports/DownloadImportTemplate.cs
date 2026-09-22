using System.Threading;
using System.Threading.Tasks;
using HealthExam.Application.Common;
using HealthExam.Application.ExamSessions;

namespace HealthExam.Application.Imports;

public interface IDownloadImportTemplateHandler
{
    Task<ApplicationResult<ImportTemplateResult>> HandleAsync(
        DownloadImportTemplateQuery query, CancellationToken ct = default);
}

public sealed class DownloadImportTemplateHandler : IDownloadImportTemplateHandler
{
    private const string XlsxContentType =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    private const string DefaultFileName = "MauDanhSachKSK.xlsx";

    private readonly IExamSessionRepository _sessionRepository;
    private readonly IExamWorkbookReader _workbookReader;

    public DownloadImportTemplateHandler(
        IExamSessionRepository sessionRepository,
        IExamWorkbookReader workbookReader)
    {
        _sessionRepository = sessionRepository;
        _workbookReader = workbookReader;
    }

    public async Task<ApplicationResult<ImportTemplateResult>> HandleAsync(
        DownloadImportTemplateQuery query, CancellationToken ct = default)
    {
        var session = await _sessionRepository.GetAsync(query.DivisionId, query.SessionId, forUpdate: false, ct);
        if (session == null)
        {
            return ApplicationResult<ImportTemplateResult>.Fail(
                ApplicationFailureCode.NotFound, "Không tìm thấy đợt khám");
        }

        var content = await _workbookReader.CreateTemplateAsync(ct);
        return ApplicationResult<ImportTemplateResult>.Success(
            new ImportTemplateResult(content, XlsxContentType, DefaultFileName));
    }
}
