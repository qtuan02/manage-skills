using System;
using System.Threading;
using System.Threading.Tasks;
using HealthExam.Application.His;

namespace HealthExam.Application.Integrations;

public enum HisOperation
{
    ListDefinitions,
    GetDefinition,
    GetDefinitionLayout,
    ListProcesses,
    GetProcess,
    CreatePatient,
    CreateAdmission,
    CreateMedicalProcess,
    ReadFormData,
    SaveFormData,
    RenderFormPdf,
    ListSignRoles,
    ListSignRoleEmployees
}

public sealed record HisRequest(
    string RelativePath,
    string Method,
    string Credential,
    string RawJsonBody = null,
    string TraceId = null,
    string DivisionId = null);

public sealed record HisJsonDocument(string RawJson);

public enum HisClientOutcome
{
    Success,
    Unauthorized,
    Forbidden,
    NotFound,
    Timeout,
    BadGateway,
    SignPrecondition,
    VendorNotConfigured
}

public sealed record HisClientResult<T>(
    HisClientOutcome Outcome,
    T Value,
    string Message,
    object Payload = null)
{
    public bool IsSuccess => Outcome == HisClientOutcome.Success;

    public static HisClientResult<T> Success(T value) => new(HisClientOutcome.Success, value, "");
    public static HisClientResult<T> Fail(HisClientOutcome outcome, string message, object payload = null)
        => new(outcome, default, message, payload);
}

public interface IHisEmrClient
{
    Task<HisClientResult<HisJsonDocument>> SendAsync(
        HisOperation operation, HisRequest request, CancellationToken ct = default);

    Task<HisClientResult<byte[]>> RenderFormPdfAsync(
        Guid emrDataId, HisRequest request, CancellationToken ct = default)
        => Task.FromResult(HisClientResult<byte[]>.Fail(HisClientOutcome.NotFound, "Not implemented"));

    Task<HisClientResult<byte[]>> RenderSignedAdmissionPdfAsync(
        long admissionId, HisRequest request, CancellationToken ct = default)
        => Task.FromResult(HisClientResult<byte[]>.Fail(HisClientOutcome.NotFound, "Not implemented"));

    Task<HisClientResult<long>> CreatePatientAsync(
        HisPatientCreateRequest request, HisCallContext context, CancellationToken ct = default)
        => Task.FromResult(HisClientResult<long>.Fail(HisClientOutcome.NotFound, "Not implemented"));

    Task<HisClientResult<long>> CreateAdmissionAsync(
        HisAdmissionCreateRequest request, HisCallContext context, CancellationToken ct = default)
        => Task.FromResult(HisClientResult<long>.Fail(HisClientOutcome.NotFound, "Not implemented"));

    Task<HisClientResult<HisJsonDocument>> CreateMedicalProcessAsync(
        MedicalProcessCreateRequest request, HisCallContext context, CancellationToken ct = default)
        => Task.FromResult(HisClientResult<HisJsonDocument>.Fail(HisClientOutcome.NotFound, "Not implemented"));

    Task<HisClientResult<System.Collections.Generic.IReadOnlyList<Icd10Choice>>> GetIcd10ChoicesAsync(
        string filter, int amount, HisCallContext context, CancellationToken ct = default)
        => Task.FromResult(HisClientResult<System.Collections.Generic.IReadOnlyList<Icd10Choice>>.Fail(HisClientOutcome.NotFound, "Not implemented"));

    Task<HisClientResult<System.Collections.Generic.IReadOnlyList<DepartmentCatalogResult>>> ListEmployeeDepartmentsAsync(
        long employeeId, HisCallContext context, CancellationToken ct = default)
        => Task.FromResult(HisClientResult<System.Collections.Generic.IReadOnlyList<DepartmentCatalogResult>>.Fail(HisClientOutcome.NotFound, "Not implemented"));

    /// <summary>RoleID các nhóm ký mà nhân viên đang thuộc — nguồn: api/M02F30000/GetPermissionGroup, đúng join HIS dùng để kiểm quyền ký.</summary>
    Task<HisClientResult<System.Collections.Generic.IReadOnlyList<long>>> ListEmployeeSignRoleIdsAsync(
        long employeeId, HisCallContext context, CancellationToken ct = default)
        => Task.FromResult(HisClientResult<System.Collections.Generic.IReadOnlyList<long>>.Fail(HisClientOutcome.NotFound, "Not implemented"));

    /// <summary>
    /// Nhân viên đang giữ vai trò ký <paramref name="roleId"/> — nguồn api/GetCodeList?key=EmployeeRole
    /// (<c>DataTypeService.EmployeeRole</c>: join <c>iAM_Employees</c> × <c>sAM_AccountInGroups.EmpID</c>,
    /// lọc <c>Active &amp;&amp; !IsSystem</c>, KHÔNG kiểm <c>IsBlock</c>). Đây là NGUỒN KHÁC với
    /// api/M02F30000/GetPermissionGroup mà <see cref="ListEmployeeSignRoleIdsAsync"/> (dùng cho ký kết
    /// luận) đang tra — hai đường không chắc trả cùng một tập nhân viên cho cùng RoleID trên mọi tenant.
    /// Chưa đối chiếu/khớp hai nguồn này theo từng tenant trước khi rollout. Luôn gửi amount=0.
    /// </summary>
    Task<HisClientResult<System.Collections.Generic.IReadOnlyList<SignRoleEmployee>>> ListSignRoleEmployeesAsync(
        long roleId, HisCallContext context, CancellationToken ct = default)
        => Task.FromResult(HisClientResult<System.Collections.Generic.IReadOnlyList<SignRoleEmployee>>.Fail(HisClientOutcome.NotFound, "Not implemented"));
}
