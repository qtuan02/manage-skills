using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HealthExam.Application.His;
using HealthExam.Application.Integrations;
using HealthExam.Application.Signing;
using HealthExam.Domain.ExamForms;

namespace HealthExam.Tests.Signing;

public sealed class FakeSignStepMapRepository : ISignStepMapRepository
{
    public List<SignStepMap> Steps { get; } = new();

    public Task<IReadOnlyList<SignStepMap>> ListAsync(
        string divisionId, string variantCode, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<SignStepMap>>(
            Steps.Where(s => s.DivisionID == divisionId && s.VariantCode == variantCode && s.IsActive)
                 .OrderBy(s => s.SWStep).ToList());
}

public sealed class FakeCertificateGateway : ICertificateGateway
{
    public HashSet<string> WithCertificate { get; } = new();
    public int CallCount { get; private set; }

    public Task<CertificateResult> GetAsync(string employeeCode, CancellationToken ct = default)
    {
        CallCount++;
        return Task.FromResult(WithCertificate.Contains(employeeCode)
            ? new CertificateResult(true, new EmployeeCertificate(
                employeeCode, $"cn={employeeCode}", "VIS", "C1", "1234", DateTime.MaxValue), "")
            : new CertificateResult(false, null, "Nhân viên chưa có chứng thư số"));
    }
}

public sealed class FakePdfSigner : IPdfSigner
{
    public List<PdfSignRequest> Requests { get; } = new();
    public HashSet<string> FailForPattern { get; } = new();

    public Task<PdfSignOutcome> SignAsync(PdfSignRequest request, CancellationToken ct = default)
    {
        Requests.Add(request);
        if (FailForPattern.Contains(request.SearchPattern))
            return Task.FromResult(new PdfSignOutcome(false, null, $"không tìm thấy {request.SearchPattern}"));

        var next = new byte[request.Pdf.Length + 1];
        Array.Copy(request.Pdf, next, request.Pdf.Length);
        next[^1] = (byte)Requests.Count;
        return Task.FromResult(new PdfSignOutcome(true, next, ""));
    }
}

public sealed class FakeExamFileStore : IExamFileStore
{
    public Dictionary<string, byte[]> Objects { get; } = new();
    public bool FailUpload { get; set; }

    public Task<bool> UploadPdfAsync(string objectPath, byte[] pdf, CancellationToken ct = default)
    {
        if (FailUpload) return Task.FromResult(false);
        Objects[objectPath] = pdf;
        return Task.FromResult(true);
    }

    public Task<byte[]> DownloadAsync(string objectPath, CancellationToken ct = default)
        => Task.FromResult(Objects.TryGetValue(objectPath, out var v) ? v : null);
}

/// <summary>HIS giả chỉ biết render PDF nháp — đếm số lần render để chốt "render đúng một lần".</summary>
public sealed class FakeRenderingHisClient : IHisEmrClient
{
    public int RenderCount { get; private set; }

    public Task<HisClientResult<HisJsonDocument>> SendAsync(
        HisOperation operation, HisRequest request, CancellationToken ct = default)
        => Task.FromResult(HisClientResult<HisJsonDocument>.Fail(
            HisClientOutcome.NotFound, "FakeRenderingHisClient chỉ hỗ trợ RenderFormPdfAsync"));

    public Task<HisClientResult<byte[]>> RenderFormPdfAsync(
        Guid emrDataId, HisRequest request, CancellationToken ct = default)
    {
        RenderCount++;
        return Task.FromResult(HisClientResult<byte[]>.Success(
            System.Text.Encoding.ASCII.GetBytes("%PDF-1.7 draft")));
    }
}

/// <summary>
/// HIS giả cho danh sách người ký theo vai trò. Mọi method khác dùng default interface (NotFound).
/// </summary>
public sealed class FakeSignRoleEmployeesHisClient : IHisEmrClient
{
    private readonly List<(long RoleId, SignRoleEmployee Employee)> _rows = new();
    public bool Fail { get; set; }
    public List<long> RequestedRoleIds { get; } = new();

    public FakeSignRoleEmployeesHisClient Add(long roleId, long employeeId, string code, string name)
    {
        _rows.Add((roleId, new SignRoleEmployee(employeeId, code, name)));
        return this;
    }

    public Task<HisClientResult<HisJsonDocument>> SendAsync(
        HisOperation operation, HisRequest request, CancellationToken ct = default)
        => Task.FromResult(HisClientResult<HisJsonDocument>.Fail(
            HisClientOutcome.NotFound, "FakeSignRoleEmployeesHisClient chỉ hỗ trợ ListSignRoleEmployeesAsync"));

    public Task<HisClientResult<IReadOnlyList<SignRoleEmployee>>> ListSignRoleEmployeesAsync(
        long roleId, HisCallContext context, CancellationToken ct = default)
    {
        RequestedRoleIds.Add(roleId);
        if (Fail)
            return Task.FromResult(HisClientResult<IReadOnlyList<SignRoleEmployee>>.Fail(
                HisClientOutcome.BadGateway, "HIS lỗi"));
        return Task.FromResult(HisClientResult<IReadOnlyList<SignRoleEmployee>>.Success(
            _rows.Where(r => r.RoleId == roleId).Select(r => r.Employee).ToList()));
    }
}
