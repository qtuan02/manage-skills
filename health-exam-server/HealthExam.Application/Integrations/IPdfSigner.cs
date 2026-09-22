using System;
using System.Threading;
using System.Threading.Tasks;

namespace HealthExam.Application.Integrations;

/// <param name="DisplayDate">Ngày in cạnh chữ ký — thời điểm bác sĩ bấm ký, không phải lúc chạy ký.</param>
public sealed record PdfSignRequest(
    byte[] Pdf,
    EmployeeCertificate Certificate,
    string EmployeeName,
    DateTime DisplayDate,
    string SignTitle,
    int SignType,
    int SignLocationType,
    string SearchPattern,
    int Page,
    float PositionX,
    float PositionY);

public sealed record PdfSignOutcome(bool Succeeded, byte[] Pdf, string Message);

public interface IPdfSigner
{
    Task<PdfSignOutcome> SignAsync(PdfSignRequest request, CancellationToken ct = default);
}
