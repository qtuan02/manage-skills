using System;
using System.Threading;
using System.Threading.Tasks;

namespace HealthExam.Application.Integrations;

/// <summary>
/// Chứng thư số của một nhân viên. CHỨA PIN — không bao giờ ghi object này ra log.
/// </summary>
public sealed record EmployeeCertificate(
    string EmployeeCode,
    string CertName,
    string Company,
    string CoPCode,
    string Pin,
    DateTime ValidUntil)
{
    /// <summary>Ghi đè ToString mặc định của record — bản sinh tự động in cả Pin.</summary>
    public override string ToString()
        => $"EmployeeCertificate({EmployeeCode}, {CertName}, valid→{ValidUntil:yyyy-MM-dd})";
}

public sealed record CertificateResult(bool Found, EmployeeCertificate Certificate, string Message);

public interface ICertificateGateway
{
    Task<CertificateResult> GetAsync(string employeeCode, CancellationToken ct = default);
}
