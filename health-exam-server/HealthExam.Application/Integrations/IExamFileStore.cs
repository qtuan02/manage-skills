using System;
using System.Threading;
using System.Threading.Tasks;

namespace HealthExam.Application.Integrations;

public interface IExamFileStore
{
    Task<bool> UploadPdfAsync(string objectPath, byte[] pdf, CancellationToken ct = default);

    /// <summary>Trả null nếu object không tồn tại.</summary>
    Task<byte[]> DownloadAsync(string objectPath, CancellationToken ct = default);
}

public static class ExamFileStorePaths
{
    public static string ConclusionPdf(string divisionId, DateTime nowUtc, Guid recordId)
        => $"{divisionId}/{nowUtc:yyyy}/{nowUtc:MM}/{recordId}.pdf";
}
