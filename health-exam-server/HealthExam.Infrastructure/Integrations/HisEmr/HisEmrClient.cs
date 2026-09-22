using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HealthExam.Application.Common;
using HealthExam.Application.His;
using HealthExam.Application.Integrations;
using HealthExam.Domain.Common;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace HealthExam.Infrastructure.Integrations.HisEmr;

public class HisEmrClient : IHisEmrClient
{
    public const string RouteGetListTemplate = "api/M03F00030/GetListTemplateByEMR";
    public const string RouteGetTemplate = "api/M03F00030/GetTemplateByID";
    public const string RouteGetTreeTemplate = "api/M03F00030/GetTreeTemplateByID";
    public const string RouteGetTreeByFileDocType = "api/M03F00030/GetTreeByFileDocTypeID";

    public const string RouteRMedicalProcess = "api/M02F01500/RMedicalProcess";
    public const string RouteRMedicalProcessById = "api/M02F01500/RMedicalProcessByID";
    public const string RouteCMedicalProcess = "api/M02F01500/CMedicalProcess";

    public const string RouteREmr = "api/M03F10010/REMR";
    public const string RouteCueEmr = "api/M03F10010/CUEMR";
    public const string RouteVEmr = "api/M03F10010/VEMR";
    public const string RouteVEmrs = "api/M02F01500/VEMRs";
    public const string RouteCUAdmission = "api/M02F00000/GetAdmissionInfo";
    public const string RouteGetCodeList = "api/GetCodeList";
    public const string RouteGetDepartmentsByEmployee = "api/M02F00000/GetDepartmentByEmpID";
    public const string RouteGetPermissionGroup = "api/M02F30000/GetPermissionGroup";

    private readonly HttpClient _http;
    private readonly HisEmrOptions _options;
    private readonly IHealthExamContext _context;
    private readonly ILogger<HisEmrClient> _logger;

    [ActivatorUtilitiesConstructor]
    public HisEmrClient(
        HttpClient http,
        HisEmrOptions options,
        IHealthExamContext context,
        ILogger<HisEmrClient> logger)
    {
        _http = http;
        _options = options;
        _context = context;
        _logger = logger;
    }

    public HisEmrClient(
        HttpClient http,
        HisEmrOptions options,
        ILogger<HisEmrClient> logger)
        : this(http, options, null, logger)
    {
    }

    public async Task<HisClientResult<HisJsonDocument>> SendAsync(
        HisOperation operation, HisRequest request, CancellationToken ct = default)
    {
        if (!_options.Enabled)
        {
            return HisClientResult<HisJsonDocument>.Fail(
                HisClientOutcome.VendorNotConfigured,
                "Tích hợp HIS EMR chưa được kích hoạt");
        }

        if (string.IsNullOrWhiteSpace(_options.BaseUrl) ||
            !Uri.TryCreate(_options.BaseUrl, UriKind.Absolute, out var baseUri) ||
            (baseUri.Scheme != Uri.UriSchemeHttp && baseUri.Scheme != Uri.UriSchemeHttps))
        {
            return HisClientResult<HisJsonDocument>.Fail(
                HisClientOutcome.VendorNotConfigured,
                "Địa chỉ HIS EMR chưa được cấu hình hợp lệ");
        }

        var credential = request.Credential;
        if (string.IsNullOrWhiteSpace(credential) && _context != null)
        {
            credential = _context.GetRequestHeader(_options.CredentialHeaderName);
        }

        if (string.IsNullOrWhiteSpace(credential))
        {
            return HisClientResult<HisJsonDocument>.Fail(
                HisClientOutcome.Unauthorized,
                "Thiếu thông tin xác thực HIS trong request");
        }

        var traceId = request.TraceId ?? _context?.TraceId;
        var divisionId = request.DivisionId ?? _context?.DivisionId;

        var fullUri = new Uri(new Uri(_options.BaseUrl.TrimEnd('/') + "/", UriKind.Absolute), request.RelativePath.TrimStart('/'));
        var retryConnectionOnce = operation is HisOperation.ListDefinitions or HisOperation.GetDefinition
            or HisOperation.GetDefinitionLayout or HisOperation.ListSignRoles or HisOperation.ListSignRoleEmployees;

        HttpRequestMessage CreateRequest()
        {
            var msg = new HttpRequestMessage(new HttpMethod(request.Method), fullUri);
            msg.Headers.TryAddWithoutValidation(_options.CredentialHeaderName, credential);
            if (!string.IsNullOrEmpty(traceId))
            {
                msg.Headers.TryAddWithoutValidation("X-Trace-Id", traceId);
            }
            if (!string.IsNullOrEmpty(divisionId))
            {
                msg.Headers.TryAddWithoutValidation("X-Division-Id", divisionId);
            }
            if (!string.IsNullOrEmpty(request.RawJsonBody))
            {
                var bodyToSend = operation == HisOperation.SaveFormData
                    ? NormalizeSaveFormDataBody(request.RawJsonBody)
                    : request.RawJsonBody;
                msg.Content = new StringContent(bodyToSend, Encoding.UTF8, "application/json");
            }
            return msg;
        }

        var sw = Stopwatch.StartNew();
        HttpResponseMessage response;

        try
        {
            if (retryConnectionOnce)
            {
                try
                {
                    using var req1 = CreateRequest();
                    response = await _http.SendAsync(req1, ct);
                }
                catch (HttpRequestException ex)
                {
                    _logger.LogWarning("Kết nối tới HIS lỗi ({Operation}), thử lại 1 lần: {Message}", operation, ex.Message);
                    using var req2 = CreateRequest();
                    response = await _http.SendAsync(req2, ct);
                }
            }
            else
            {
                using var req = CreateRequest();
                response = await _http.SendAsync(req, ct);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is TaskCanceledException or TimeoutException)
        {
            _logger.LogError("Gọi HIS quá thời gian ({Operation}) sau {ElapsedMs}ms", operation, sw.ElapsedMilliseconds);
            return HisClientResult<HisJsonDocument>.Fail(
                HisClientOutcome.Timeout,
                "Dịch vụ HIS xử lý quá thời gian, vui lòng thử lại sau");
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError("Lỗi kết nối tới HIS ({Operation}) sau {ElapsedMs}ms: {Message}", operation, sw.ElapsedMilliseconds, ex.Message);
            return HisClientResult<HisJsonDocument>.Fail(
                HisClientOutcome.BadGateway,
                "Không thể kết nối đến dịch vụ HIS");
        }

        var statusCode = response.StatusCode;
        var elapsedMs = sw.ElapsedMilliseconds;

        _logger.LogInformation("HIS {Operation} hoàn thành với HTTP {StatusCode} sau {ElapsedMs}ms (TraceId: {TraceId})",
            operation, (int)statusCode, elapsedMs, traceId);

        if (statusCode == HttpStatusCode.Unauthorized)
            return HisClientResult<HisJsonDocument>.Fail(HisClientOutcome.Unauthorized, "HIS từ chối xác thực (401)");
        if (statusCode == HttpStatusCode.Forbidden)
            return HisClientResult<HisJsonDocument>.Fail(HisClientOutcome.Forbidden, "Không đủ quyền truy cập HIS (403)");
        if (statusCode == HttpStatusCode.NotFound)
            return HisClientResult<HisJsonDocument>.Fail(HisClientOutcome.NotFound, "Không tìm thấy dữ liệu trên HIS (404)");

        string body;
        try
        {
            body = await response.Content.ReadAsStringAsync(ct);
        }
        catch (Exception)
        {
            return HisClientResult<HisJsonDocument>.Fail(HisClientOutcome.BadGateway, $"Không đọc được nội dung từ HIS ({operation})");
        }

        HisEnvelope envelope = null;
        if (!string.IsNullOrWhiteSpace(body))
        {
            try
            {
                envelope = JsonConvert.DeserializeObject<HisEnvelope>(body);
            }
            catch
            {
                // Non-JSON / HTML response
            }
        }

        if (statusCode == HttpStatusCode.BadRequest)
        {
            var msg = ExtractErrorMessage(body, envelope)
                ?? "HIS từ chối thao tác (400)";
            return HisClientResult<HisJsonDocument>.Fail(HisClientOutcome.SignPrecondition, msg);
        }

        if (envelope == null)
        {
            return HisClientResult<HisJsonDocument>.Fail(
                HisClientOutcome.BadGateway,
                $"HIS trả về nội dung không hợp lệ hoặc rỗng ({operation})");
        }

        if (envelope.ErrorCode != 0)
        {
            var msg = !string.IsNullOrWhiteSpace(envelope.Message)
                ? envelope.Message
                : "HIS từ chối thao tác";
            return HisClientResult<HisJsonDocument>.Fail(HisClientOutcome.SignPrecondition, msg);
        }

        if (!response.IsSuccessStatusCode)
        {
            return HisClientResult<HisJsonDocument>.Fail(
                HisClientOutcome.BadGateway,
                $"HIS trả về mã lỗi HTTP {(int)statusCode} ({operation})");
        }

        if (envelope.Data == null || envelope.Data.Type == JTokenType.Null)
        {
            return HisClientResult<HisJsonDocument>.Fail(
                HisClientOutcome.BadGateway,
                $"HIS báo thành công nhưng không kèm dữ liệu ({operation})");
        }

        var rawJson = envelope.Data.ToString(Formatting.None);
        return HisClientResult<HisJsonDocument>.Success(new HisJsonDocument(rawJson));
    }

    public virtual async Task<HisClientResult<byte[]>> RenderFormPdfAsync(
        Guid emrDataId, HisRequest request, CancellationToken ct = default)
    {
        if (!_options.Enabled)
        {
            return HisClientResult<byte[]>.Fail(
                HisClientOutcome.VendorNotConfigured,
                "Tích hợp HIS EMR chưa được kích hoạt");
        }

        if (string.IsNullOrWhiteSpace(_options.BaseUrl) ||
            !Uri.TryCreate(_options.BaseUrl, UriKind.Absolute, out var baseUri) ||
            (baseUri.Scheme != Uri.UriSchemeHttp && baseUri.Scheme != Uri.UriSchemeHttps))
        {
            return HisClientResult<byte[]>.Fail(
                HisClientOutcome.VendorNotConfigured,
                "Địa chỉ HIS EMR chưa được cấu hình hợp lệ");
        }

        var credential = request.Credential;
        if (string.IsNullOrWhiteSpace(credential) && _context != null)
        {
            credential = _context.GetRequestHeader(_options.CredentialHeaderName);
        }

        if (string.IsNullOrWhiteSpace(credential))
        {
            return HisClientResult<byte[]>.Fail(
                HisClientOutcome.Unauthorized,
                "Thiếu thông tin xác thực HIS trong request");
        }

        var traceId = request.TraceId ?? _context?.TraceId;
        var divisionId = request.DivisionId ?? _context?.DivisionId;

        var path = $"{RouteVEmr}?EMRDataID={emrDataId}&IsJson=false";
        var fullUri = new Uri(new Uri(_options.BaseUrl.TrimEnd('/') + "/", UriKind.Absolute), path);

        using var req = new HttpRequestMessage(HttpMethod.Get, fullUri);
        req.Headers.TryAddWithoutValidation(_options.CredentialHeaderName, credential);
        if (!string.IsNullOrEmpty(traceId))
        {
            req.Headers.TryAddWithoutValidation("X-Trace-Id", traceId);
        }
        if (!string.IsNullOrEmpty(divisionId))
        {
            req.Headers.TryAddWithoutValidation("X-Division-Id", divisionId);
        }

        var sw = Stopwatch.StartNew();
        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(req, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is TaskCanceledException or TimeoutException)
        {
            _logger.LogError("Gọi HIS quá thời gian (VEMR) sau {ElapsedMs}ms", sw.ElapsedMilliseconds);
            return HisClientResult<byte[]>.Fail(
                HisClientOutcome.Timeout,
                "Dịch vụ HIS xử lý quá thời gian, vui lòng thử lại sau");
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError("Lỗi kết nối tới HIS (VEMR) sau {ElapsedMs}ms: {Message}", sw.ElapsedMilliseconds, ex.Message);
            return HisClientResult<byte[]>.Fail(
                HisClientOutcome.BadGateway,
                "Không thể kết nối đến dịch vụ HIS");
        }

        var statusCode = response.StatusCode;
        var elapsedMs = sw.ElapsedMilliseconds;

        _logger.LogInformation("HIS VEMR hoàn thành với HTTP {StatusCode} sau {ElapsedMs}ms (TraceId: {TraceId})",
            (int)statusCode, elapsedMs, traceId);

        if (statusCode == HttpStatusCode.Unauthorized)
            return HisClientResult<byte[]>.Fail(HisClientOutcome.Unauthorized, "HIS từ chối xác thực (401)");
        if (statusCode == HttpStatusCode.Forbidden)
            return HisClientResult<byte[]>.Fail(HisClientOutcome.Forbidden, "Không đủ quyền truy cập HIS (403)");
        if (statusCode == HttpStatusCode.NotFound)
            return HisClientResult<byte[]>.Fail(HisClientOutcome.NotFound, "Không tìm thấy dữ liệu trên HIS (404)");

        if (!response.IsSuccessStatusCode)
        {
            string body;
            try
            {
                body = await response.Content.ReadAsStringAsync(ct);
            }
            catch
            {
                return HisClientResult<byte[]>.Fail(HisClientOutcome.BadGateway, "Không đọc được nội dung từ HIS (VEMR)");
            }

            HisEnvelope envelope = null;
            if (!string.IsNullOrWhiteSpace(body))
            {
                try
                {
                    envelope = JsonConvert.DeserializeObject<HisEnvelope>(body);
                }
                catch
                {
                }
            }

            if (statusCode == HttpStatusCode.BadRequest)
            {
                var msg = !string.IsNullOrWhiteSpace(envelope?.Message)
                    ? envelope.Message
                    : "HIS từ chối thao tác (400)";
                return HisClientResult<byte[]>.Fail(HisClientOutcome.SignPrecondition, msg);
            }

            if (envelope != null && envelope.ErrorCode != 0)
            {
                var msg = !string.IsNullOrWhiteSpace(envelope.Message)
                    ? envelope.Message
                    : "HIS từ chối thao tác";
                return HisClientResult<byte[]>.Fail(HisClientOutcome.SignPrecondition, msg);
            }

            return HisClientResult<byte[]>.Fail(HisClientOutcome.BadGateway, $"HIS trả về mã lỗi HTTP {(int)statusCode} (VEMR)");
        }

        var mediaType = response.Content.Headers.ContentType?.MediaType;
        if (mediaType != null && mediaType.Contains("json", StringComparison.OrdinalIgnoreCase))
        {
            string body;
            try
            {
                body = await response.Content.ReadAsStringAsync(ct);
            }
            catch
            {
                return HisClientResult<byte[]>.Fail(HisClientOutcome.BadGateway, "Không đọc được nội dung từ HIS (VEMR)");
            }

            HisEnvelope envelope = null;
            if (!string.IsNullOrWhiteSpace(body))
            {
                try
                {
                    envelope = JsonConvert.DeserializeObject<HisEnvelope>(body);
                }
                catch
                {
                }
            }

            if (envelope != null && envelope.ErrorCode != 0)
            {
                var msg = !string.IsNullOrWhiteSpace(envelope.Message)
                    ? envelope.Message
                    : "HIS từ chối thao tác";
                return HisClientResult<byte[]>.Fail(HisClientOutcome.SignPrecondition, msg);
            }

            return HisClientResult<byte[]>.Fail(HisClientOutcome.BadGateway, "HIS trả về JSON thay vì nội dung file PDF (VEMR)");
        }

        var bytes = await response.Content.ReadAsByteArrayAsync(ct);
        return HisClientResult<byte[]>.Success(bytes);
    }

    public virtual async Task<HisClientResult<byte[]>> RenderSignedAdmissionPdfAsync(
        long admissionId, HisRequest request, CancellationToken ct = default)
    {
        if (admissionId <= 0)
        {
            return HisClientResult<byte[]>.Fail(
                HisClientOutcome.SignPrecondition,
                "Mã lượt khám HIS (admissionID) không hợp lệ");
        }

        if (!_options.Enabled)
        {
            return HisClientResult<byte[]>.Fail(
                HisClientOutcome.VendorNotConfigured,
                "Tích hợp HIS EMR chưa được kích hoạt");
        }

        if (string.IsNullOrWhiteSpace(_options.BaseUrl) ||
            !Uri.TryCreate(_options.BaseUrl, UriKind.Absolute, out var baseUri) ||
            (baseUri.Scheme != Uri.UriSchemeHttp && baseUri.Scheme != Uri.UriSchemeHttps))
        {
            return HisClientResult<byte[]>.Fail(
                HisClientOutcome.VendorNotConfigured,
                "Địa chỉ HIS EMR chưa được cấu hình hợp lệ");
        }

        var credential = request.Credential;
        if (string.IsNullOrWhiteSpace(credential) && _context != null)
        {
            credential = _context.GetRequestHeader(_options.CredentialHeaderName);
        }

        if (string.IsNullOrWhiteSpace(credential))
        {
            return HisClientResult<byte[]>.Fail(
                HisClientOutcome.Unauthorized,
                "Thiếu thông tin xác thực HIS trong request");
        }

        var traceId = request.TraceId ?? _context?.TraceId;
        var divisionId = request.DivisionId ?? _context?.DivisionId;

        var path = $"{RouteVEmrs}?admissionID={admissionId}";
        var fullUri = new Uri(new Uri(_options.BaseUrl.TrimEnd('/') + "/", UriKind.Absolute), path);

        using var req = new HttpRequestMessage(HttpMethod.Get, fullUri);
        req.Headers.TryAddWithoutValidation(_options.CredentialHeaderName, credential);
        if (!string.IsNullOrEmpty(traceId))
        {
            req.Headers.TryAddWithoutValidation("X-Trace-Id", traceId);
        }
        if (!string.IsNullOrEmpty(divisionId))
        {
            req.Headers.TryAddWithoutValidation("X-Division-Id", divisionId);
        }

        var sw = Stopwatch.StartNew();
        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(req, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is TaskCanceledException or TimeoutException)
        {
            _logger.LogError("Gọi HIS quá thời gian (VEMRs) sau {ElapsedMs}ms", sw.ElapsedMilliseconds);
            return HisClientResult<byte[]>.Fail(
                HisClientOutcome.Timeout,
                "Dịch vụ HIS xử lý quá thời gian, vui lòng thử lại sau");
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError("Lỗi kết nối tới HIS (VEMRs) sau {ElapsedMs}ms: {Message}", sw.ElapsedMilliseconds, ex.Message);
            return HisClientResult<byte[]>.Fail(
                HisClientOutcome.BadGateway,
                "Không thể kết nối đến dịch vụ HIS");
        }

        var statusCode = response.StatusCode;
        var elapsedMs = sw.ElapsedMilliseconds;

        _logger.LogInformation("HIS VEMRs hoàn thành với HTTP {StatusCode} sau {ElapsedMs}ms (TraceId: {TraceId})",
            (int)statusCode, elapsedMs, traceId);

        if (statusCode == HttpStatusCode.Unauthorized)
            return HisClientResult<byte[]>.Fail(HisClientOutcome.Unauthorized, "HIS từ chối xác thực (401)");
        if (statusCode == HttpStatusCode.Forbidden)
            return HisClientResult<byte[]>.Fail(HisClientOutcome.Forbidden, "Không đủ quyền truy cập HIS (403)");
        if (statusCode == HttpStatusCode.NotFound)
            return HisClientResult<byte[]>.Fail(HisClientOutcome.NotFound, "Không tìm thấy dữ liệu trên HIS (404)");

        if (!response.IsSuccessStatusCode)
        {
            string body;
            try
            {
                body = await response.Content.ReadAsStringAsync(ct);
            }
            catch
            {
                return HisClientResult<byte[]>.Fail(HisClientOutcome.BadGateway, "Không đọc được nội dung từ HIS (VEMRs)");
            }

            HisEnvelope envelope = null;
            if (!string.IsNullOrWhiteSpace(body))
            {
                try
                {
                    envelope = JsonConvert.DeserializeObject<HisEnvelope>(body);
                }
                catch
                {
                }
            }

            if (statusCode == HttpStatusCode.BadRequest)
            {
                if (envelope != null && string.Equals(envelope.Message?.Trim(), "Không tìm thấy dữ liệu hồ sơ bệnh án", StringComparison.OrdinalIgnoreCase))
                {
                    return HisClientResult<byte[]>.Fail(HisClientOutcome.NotFound, envelope.Message);
                }

                var msg = !string.IsNullOrWhiteSpace(envelope?.Message)
                    ? envelope.Message
                    : "HIS từ chối thao tác (400)";
                return HisClientResult<byte[]>.Fail(HisClientOutcome.SignPrecondition, msg);
            }

            if (envelope != null && envelope.ErrorCode != 0)
            {
                var msg = !string.IsNullOrWhiteSpace(envelope.Message)
                    ? envelope.Message
                    : "HIS từ chối thao tác";
                return HisClientResult<byte[]>.Fail(HisClientOutcome.SignPrecondition, msg);
            }

            return HisClientResult<byte[]>.Fail(HisClientOutcome.BadGateway, $"HIS trả về mã lỗi HTTP {(int)statusCode} (VEMRs)");
        }

        var mediaType = response.Content.Headers.ContentType?.MediaType;
        if (mediaType != null && mediaType.Contains("json", StringComparison.OrdinalIgnoreCase))
        {
            string body;
            try
            {
                body = await response.Content.ReadAsStringAsync(ct);
            }
            catch
            {
                return HisClientResult<byte[]>.Fail(HisClientOutcome.BadGateway, "Không đọc được nội dung từ HIS (VEMRs)");
            }

            HisEnvelope envelope = null;
            if (!string.IsNullOrWhiteSpace(body))
            {
                try
                {
                    envelope = JsonConvert.DeserializeObject<HisEnvelope>(body);
                }
                catch
                {
                }
            }

            if (envelope != null && envelope.ErrorCode != 0)
            {
                var msg = !string.IsNullOrWhiteSpace(envelope.Message)
                    ? envelope.Message
                    : "HIS từ chối thao tác";
                return HisClientResult<byte[]>.Fail(HisClientOutcome.SignPrecondition, msg);
            }

            return HisClientResult<byte[]>.Fail(HisClientOutcome.BadGateway, "HIS trả về JSON thay vì nội dung file PDF (VEMRs)");
        }

        var bytes = await response.Content.ReadAsByteArrayAsync(ct);
        if (bytes == null || bytes.Length < 5 ||
            bytes[0] != (byte)'%' || bytes[1] != (byte)'P' || bytes[2] != (byte)'D' || bytes[3] != (byte)'F' || bytes[4] != (byte)'-')
        {
            return HisClientResult<byte[]>.Fail(HisClientOutcome.BadGateway, "Dữ liệu trả về không phải định dạng PDF hợp lệ (VEMRs)");
        }

        return HisClientResult<byte[]>.Success(bytes);
    }

    // Legacy direct methods for backwards compatibility with existing test suite
    public virtual Task<JToken> GetTemplateListAsync(CancellationToken ct = default)
        => SendDirectAsync(HisOperation.ListDefinitions, RouteGetListTemplate, "GET", null, ct);

    public virtual Task<JToken> GetTemplateAsync(Guid templateId, CancellationToken ct = default)
        => SendDirectAsync(HisOperation.GetDefinition, $"{RouteGetTemplate}?templateID={templateId}", "GET", null, ct);

    public virtual Task<JToken> GetTemplateTreeAsync(Guid templateId, long? admissionId = null, CancellationToken ct = default)
    {
        var path = admissionId.HasValue
            ? $"{RouteGetTreeTemplate}?templateID={templateId}&admissionID={admissionId.Value}"
            : $"{RouteGetTreeTemplate}?templateID={templateId}";
        return SendDirectAsync(HisOperation.GetDefinitionLayout, path, "GET", null, ct);
    }

    public virtual Task<JToken> GetTreeByFileDocTypeAsync(int fileDocTypeId, CancellationToken ct = default)
        => SendDirectAsync(HisOperation.GetDefinitionLayout, $"{RouteGetTreeByFileDocType}?fileDocTypeID={fileDocTypeId}", "GET", null, ct);

    public virtual Task<JToken> GetMedicalProcessesAsync(long admissionId, CancellationToken ct = default)
        => SendDirectAsync(HisOperation.ListProcesses, $"{RouteRMedicalProcess}?admissionID={admissionId}", "GET", null, ct);

    public virtual Task<JToken> GetMedicalProcessAsync(Guid processId, CancellationToken ct = default)
        => SendDirectAsync(HisOperation.GetProcess, $"{RouteRMedicalProcessById}?id={processId}", "GET", null, ct);

    public virtual async Task<HisClientResult<long>> CreatePatientAsync(
        HisPatientCreateRequest request, HisCallContext context, CancellationToken ct = default)
    {
        if (!_options.Enabled)
        {
            return HisClientResult<long>.Fail(
                HisClientOutcome.VendorNotConfigured,
                "Tích hợp HIS EMR chưa được kích hoạt");
        }

        if (string.IsNullOrWhiteSpace(_options.PatientCreateRoute))
        {
            return HisClientResult<long>.Fail(
                HisClientOutcome.VendorNotConfigured,
                "Chưa cấu hình route tạo người bệnh HIS");
        }

        var wireReq = new HisPatientWireRequest
        {
            PatientCode = request.PatientCode,
            FirstName = request.FirstName,
            LastName = request.LastName,
            FullName = request.FullName,
            I_Gender = request.Gender,
            BirthDate = request.BirthDate,
            BirthYear = request.BirthYear,
            IDCard = request.IdentityNumber,
            MobileNo = request.PhoneNumber,
            PersonalEmail = request.Email,
            CurrentAddress = request.Address
        };

        var hisReq = new HisRequest(
            _options.PatientCreateRoute,
            "POST",
            context?.Credential,
            JsonConvert.SerializeObject(wireReq),
            TraceId: context?.TraceId,
            DivisionId: context?.DivisionId);

        var res = await SendAsync(HisOperation.CreatePatient, hisReq, ct);
        if (!res.IsSuccess)
        {
            return HisClientResult<long>.Fail(res.Outcome, res.Message, res.Payload);
        }

        var id = ParsePositiveId(res.Value.RawJson);
        if (id <= 0)
        {
            return HisClientResult<long>.Fail(HisClientOutcome.BadGateway, "HIS trả về PatientID không hợp lệ");
        }

        return HisClientResult<long>.Success(id);
    }

    public virtual async Task<HisClientResult<long>> CreateAdmissionAsync(
        HisAdmissionCreateRequest request, HisCallContext context, CancellationToken ct = default)
    {
        if (!_options.Enabled)
        {
            return HisClientResult<long>.Fail(
                HisClientOutcome.VendorNotConfigured,
                "Tích hợp HIS EMR chưa được kích hoạt");
        }

        var wireReq = new HisAdmissionWireRequest
        {
            IsOutPatient = request.IsOutPatient,
            AdmissionCode = request.AdmissionCode,
            AdmissionDate = request.AdmissionDate,
            DepartmentID = request.DepartmentID,
            DepartmentCode = request.DepartmentCode,
            PatientCode = request.Patient.PatientCode,
            FirstName = request.Patient.FirstName,
            LastName = request.Patient.LastName,
            FullName = request.Patient.FullName,
            I_Gender = request.Patient.Gender,
            BirthDate = request.Patient.BirthDate,
            BirthYear = request.Patient.BirthYear,
            IDCard = request.Patient.IdentityNumber,
            MobileNo = request.Patient.PhoneNumber,
            PersonalEmail = request.Patient.Email,
            CurrentAddress = request.Patient.Address
        };

        var hisReq = new HisRequest(
            RouteCUAdmission,
            "POST",
            context?.Credential,
            JsonConvert.SerializeObject(wireReq),
            TraceId: context?.TraceId,
            DivisionId: context?.DivisionId);

        var res = await SendAsync(HisOperation.CreateAdmission, hisReq, ct);
        if (!res.IsSuccess)
        {
            return HisClientResult<long>.Fail(res.Outcome, res.Message, res.Payload);
        }

        var id = ParsePositiveId(res.Value.RawJson);
        if (id <= 0)
        {
            return HisClientResult<long>.Fail(HisClientOutcome.BadGateway, "HIS trả về AdmissionID không hợp lệ");
        }

        return HisClientResult<long>.Success(id);
    }

    public virtual async Task<HisClientResult<HisJsonDocument>> CreateMedicalProcessAsync(
        MedicalProcessCreateRequest request, HisCallContext context, CancellationToken ct = default)
    {
        if (!_options.Enabled)
        {
            return HisClientResult<HisJsonDocument>.Fail(
                HisClientOutcome.VendorNotConfigured,
                "Tích hợp HIS EMR chưa được kích hoạt");
        }

        var wireReq = new HisMedicalProcessWireRequest
        {
            MedicalTypeCode = request.MedicalTypeCode,
            MedicalTypeCodeOld = request.MedicalTypeCodeOld,
            AdmissionID = request.AdmissionID
        };

        var hisReq = new HisRequest(
            RouteCMedicalProcess,
            "POST",
            context?.Credential,
            JsonConvert.SerializeObject(wireReq),
            TraceId: context?.TraceId,
            DivisionId: context?.DivisionId);

        return await SendAsync(HisOperation.CreateMedicalProcess, hisReq, ct);
    }

    public virtual async Task<HisClientResult<System.Collections.Generic.IReadOnlyList<Icd10Choice>>> GetIcd10ChoicesAsync(
        string filter, int amount, HisCallContext context, CancellationToken ct = default)
    {
        if (!_options.Enabled)
        {
            return HisClientResult<System.Collections.Generic.IReadOnlyList<Icd10Choice>>.Fail(
                HisClientOutcome.VendorNotConfigured, "Tích hợp HIS EMR chưa được kích hoạt");
        }

        var path = $"{RouteGetCodeList}?key=ICD10";
        if (!string.IsNullOrWhiteSpace(filter))
        {
            path += $"&filter={Uri.EscapeDataString(filter.Trim())}";
        }
        if (amount > 0)
        {
            path += $"&amount={amount}";
        }

        var hisReq = new HisRequest(path, "GET", context?.Credential, TraceId: context?.TraceId, DivisionId: context?.DivisionId);
        var res = await SendAsync(HisOperation.ListDefinitions, hisReq, ct);
        if (!res.IsSuccess)
        {
            return HisClientResult<System.Collections.Generic.IReadOnlyList<Icd10Choice>>.Fail(res.Outcome, res.Message, res.Payload);
        }

        var choices = ParseIcd10Choices(res.Value.RawJson, amount > 0 ? amount : 0);
        return HisClientResult<System.Collections.Generic.IReadOnlyList<Icd10Choice>>.Success(choices);
    }

    public virtual async Task<HisClientResult<System.Collections.Generic.IReadOnlyList<DepartmentCatalogResult>>> ListEmployeeDepartmentsAsync(
        long employeeId, HisCallContext context, CancellationToken ct = default)
    {
        if (!_options.Enabled)
        {
            return HisClientResult<System.Collections.Generic.IReadOnlyList<DepartmentCatalogResult>>.Fail(
                HisClientOutcome.VendorNotConfigured, "Tích hợp HIS EMR chưa được kích hoạt");
        }

        var path = $"{RouteGetDepartmentsByEmployee}?EmployeeID={employeeId}";
        var hisReq = new HisRequest(path, "GET", context?.Credential, TraceId: context?.TraceId, DivisionId: context?.DivisionId);
        var res = await SendAsync(HisOperation.ListDefinitions, hisReq, ct);
        if (!res.IsSuccess)
        {
            return HisClientResult<System.Collections.Generic.IReadOnlyList<DepartmentCatalogResult>>.Fail(res.Outcome, res.Message, res.Payload);
        }

        var items = ParseDepartments(res.Value.RawJson);
        return HisClientResult<System.Collections.Generic.IReadOnlyList<DepartmentCatalogResult>>.Success(items);
    }

    public virtual async Task<HisClientResult<System.Collections.Generic.IReadOnlyList<long>>> ListEmployeeSignRoleIdsAsync(
        long employeeId, HisCallContext context, CancellationToken ct = default)
    {
        if (!_options.Enabled)
        {
            return HisClientResult<System.Collections.Generic.IReadOnlyList<long>>.Fail(
                HisClientOutcome.VendorNotConfigured, "Tích hợp HIS EMR chưa được kích hoạt");
        }

        var path = $"{RouteGetPermissionGroup}?empID={employeeId}";
        var hisReq = new HisRequest(path, "GET", context?.Credential, TraceId: context?.TraceId, DivisionId: context?.DivisionId);
        var res = await SendAsync(HisOperation.ListSignRoles, hisReq, ct);
        if (!res.IsSuccess)
        {
            return HisClientResult<System.Collections.Generic.IReadOnlyList<long>>.Fail(res.Outcome, res.Message, res.Payload);
        }

        var items = ParseSignRoleIds(res.Value.RawJson);
        return HisClientResult<System.Collections.Generic.IReadOnlyList<long>>.Success(items);
    }

    public virtual async Task<HisClientResult<System.Collections.Generic.IReadOnlyList<SignRoleEmployee>>> ListSignRoleEmployeesAsync(
        long roleId, HisCallContext context, CancellationToken ct = default)
    {
        if (!_options.Enabled)
        {
            return HisClientResult<System.Collections.Generic.IReadOnlyList<SignRoleEmployee>>.Fail(
                HisClientOutcome.VendorNotConfigured, "Tích hợp HIS EMR chưa được kích hoạt");
        }

        // amount=0 → HIS trả toàn bộ; bỏ trống thì mặc định 20 dòng, thiếu bác sĩ mà không báo.
        var path = $"{RouteGetCodeList}?key=EmployeeRole&filterCode={roleId}&amount=0";
        var hisReq = new HisRequest(path, "GET", context?.Credential, TraceId: context?.TraceId, DivisionId: context?.DivisionId);
        var res = await SendAsync(HisOperation.ListSignRoleEmployees, hisReq, ct);
        if (!res.IsSuccess)
        {
            return HisClientResult<System.Collections.Generic.IReadOnlyList<SignRoleEmployee>>.Fail(res.Outcome, res.Message, res.Payload);
        }

        return HisClientResult<System.Collections.Generic.IReadOnlyList<SignRoleEmployee>>.Success(
            ParseSignRoleEmployees(res.Value.RawJson));
    }

    public virtual async Task<JToken> CreatePatientAsync(HisPatientWireRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_options.PatientCreateRoute))
        {
            throw new HealthExamException(5021, "Chưa cấu hình route tạo người bệnh HIS");
        }

        var data = await SendDirectAsync(HisOperation.CreatePatient, _options.PatientCreateRoute, "POST", JsonConvert.SerializeObject(request), ct);
        if (data == null || data.Type != JTokenType.Integer)
        {
            throw new HealthExamException(5022, "HIS trả về PatientID không hợp lệ");
        }

        try
        {
            var id = data.Value<long>();
            if (id <= 0)
            {
                throw new HealthExamException(5022, "HIS trả về PatientID không hợp lệ");
            }
        }
        catch (Exception ex) when (ex is InvalidCastException or FormatException or OverflowException)
        {
            throw new HealthExamException(5022, "HIS trả về PatientID không hợp lệ", ex);
        }

        return data;
    }

    public virtual async Task<JToken> CreateAdmissionAsync(HisAdmissionWireRequest request, CancellationToken ct = default)
    {
        var data = await SendDirectAsync(HisOperation.CreateAdmission, RouteCUAdmission, "POST", JsonConvert.SerializeObject(request), ct);
        return data;
    }

    private static long ParsePositiveId(string rawJson)
    {
        if (string.IsNullOrWhiteSpace(rawJson)) return 0;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(rawJson);
            var root = doc.RootElement;
            if (root.ValueKind == System.Text.Json.JsonValueKind.Number && root.TryGetInt64(out var idNum) && idNum > 0)
                return idNum;
            if (root.ValueKind == System.Text.Json.JsonValueKind.String && long.TryParse(root.GetString(), out var idParsed) && idParsed > 0)
                return idParsed;
            if (root.ValueKind == System.Text.Json.JsonValueKind.Object)
            {
                if (root.TryGetProperty("PatientID", out var pPat) && pPat.TryGetInt64(out var patId) && patId > 0)
                    return patId;
                if (root.TryGetProperty("AdmissionID", out var pAdm) && pAdm.TryGetInt64(out var aId) && aId > 0)
                    return aId;
                if (root.TryGetProperty("Id", out var pId) && pId.TryGetInt64(out var pIdNum) && pIdNum > 0)
                    return pIdNum;
                if (root.TryGetProperty("Data", out var pData))
                {
                    if (pData.ValueKind == System.Text.Json.JsonValueKind.Number && pData.TryGetInt64(out var dId) && dId > 0)
                        return dId;
                    if (pData.ValueKind == System.Text.Json.JsonValueKind.String && long.TryParse(pData.GetString(), out var dsId) && dsId > 0)
                        return dsId;
                }
            }
        }
        catch
        {
        }
        return 0;
    }

    private static string ExtractErrorMessage(string body, HisEnvelope envelope)
    {
        var envelopeMessage = NormalizeMessage(envelope?.Message);
        if (envelopeMessage != null)
            return envelopeMessage;

        if (string.IsNullOrWhiteSpace(body))
            return null;

        try
        {
            var token = JToken.Parse(body);
            if (token.Type == JTokenType.String)
                return NormalizeMessage(token.Value<string>());

            if (token is not JObject problem)
                return null;

            var errors = problem.GetValue("errors", StringComparison.OrdinalIgnoreCase) as JObject;
            if (errors != null)
            {
                var parts = new System.Collections.Generic.List<string>();
                foreach (var property in errors.Properties())
                {
                    if (property.Value is JArray messages)
                    {
                        foreach (var message in messages.Values<string>())
                        {
                            var normalized = NormalizeMessage(message);
                            if (normalized != null)
                                parts.Add($"{property.Name}: {normalized}");
                        }
                    }
                    else
                    {
                        var normalized = NormalizeMessage(property.Value.ToString());
                        if (normalized != null)
                            parts.Add($"{property.Name}: {normalized}");
                    }
                }

                var validationMessage = NormalizeMessage(string.Join("; ", parts));
                if (validationMessage != null)
                    return validationMessage;
            }

            foreach (var propertyName in new[] { "detail", "title", "message" })
            {
                var value = problem.GetValue(propertyName, StringComparison.OrdinalIgnoreCase);
                var normalized = NormalizeMessage(value?.Type == JTokenType.String ? value.Value<string>() : value?.ToString());
                if (normalized != null)
                    return normalized;
            }
        }
        catch
        {
        }

        return null;
    }

    private static string NormalizeMessage(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return null;
        var trimmed = message.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    private static string NormalizeSaveFormDataBody(string rawJsonBody)
    {
        if (string.IsNullOrWhiteSpace(rawJsonBody)) return rawJsonBody;
        try
        {
            var jobj = JObject.Parse(rawJsonBody);
            if (jobj["Details"] is JArray details)
            {
                foreach (var item in details)
                {
                    if (item is JObject dObj)
                    {
                        var text = dObj["Text"]?.ToString();
                        if (string.IsNullOrWhiteSpace(text))
                        {
                            dObj["Text"] = dObj["Value"]?.ToString() ?? "";
                        }
                    }
                }
                return jobj.ToString(Formatting.None);
            }
        }
        catch
        {
        }
        return rawJsonBody;
    }

    private static IReadOnlyList<Icd10Choice> ParseIcd10Choices(string rawJson, int maxChoices = 300)
    {
        if (string.IsNullOrWhiteSpace(rawJson)) return Array.Empty<Icd10Choice>();
        try
        {
            var root = JToken.Parse(rawJson);
            JArray list = null;
            if (root is JArray rArr) list = rArr;
            else if (root is JObject rObj)
            {
                var dataToken = rObj["Data"] ?? rObj["data"];
                if (dataToken is JArray dArr) list = dArr;
                else if (dataToken is JObject dObj)
                {
                    var subData = dObj["Data"] ?? dObj["data"] ?? dObj["Items"] ?? dObj["items"];
                    if (subData is JArray subArr) list = subArr;
                }
                if (list == null)
                {
                    var items = rObj["Items"] ?? rObj["items"] ?? rObj["List"] ?? rObj["list"];
                    if (items is JArray itemsArr) list = itemsArr;
                }
            }
            if (list == null || list.Count == 0) return Array.Empty<Icd10Choice>();

            var choices = new List<Icd10Choice>();
            var seenCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var itemsByCode = new Dictionary<string, (string Code, string Label, string CodeName, string DisplayName)>(StringComparer.OrdinalIgnoreCase);

            foreach (var item in list)
            {
                var code = item["Code"]?.ToString()?.Trim();
                if (string.IsNullOrEmpty(code)) continue;

                var displayName = item["DisplayName"]?.ToString()?.Trim();
                var codeName = item["CodeName"]?.ToString()?.Trim();
                var shortName = item["ShortName"]?.ToString()?.Trim();

                var label = !string.IsNullOrWhiteSpace(displayName)
                    ? displayName
                    : (!string.IsNullOrWhiteSpace(codeName)
                        ? $"{code} - {codeName}"
                        : (!string.IsNullOrWhiteSpace(shortName) ? $"{code} - {shortName}" : code));

                itemsByCode[code] = (code, label, codeName, displayName);
            }

            foreach (var prioCode in PrioritizedIcd10Codes)
            {
                if (itemsByCode.TryGetValue(prioCode, out var match))
                {
                    choices.Add(new Icd10Choice(match.Code, match.Label, match.CodeName, match.DisplayName));
                    seenCodes.Add(match.Code);
                }
            }

            foreach (var kvp in itemsByCode)
            {
                if (!seenCodes.Contains(kvp.Key))
                {
                    choices.Add(new Icd10Choice(kvp.Value.Code, kvp.Value.Label, kvp.Value.CodeName, kvp.Value.DisplayName));
                    seenCodes.Add(kvp.Key);
                    if (maxChoices > 0 && choices.Count >= maxChoices)
                    {
                        break;
                    }
                }
            }

            return choices;
        }
        catch
        {
            return Array.Empty<Icd10Choice>();
        }
    }

    private static readonly string[] PrioritizedIcd10Codes = new[]
    {
        "Z00.0", "Z10.0", "Z01.0", "Z01.1", "Z01.2",
        "I10", "I11", "I20", "I49.9", "R00.0",
        "J00", "J02", "J06.9", "J20.9", "J45",
        "K21.0", "K29.5", "K29.7", "K25", "K58", "K76.0",
        "M54.5", "M50", "M51", "M17", "M19",
        "G43", "G44.2", "G47.0", "R51",
        "H52.1", "H52.2", "H52.4", "H10",
        "H66", "J30", "J31", "J35.0",
        "K02", "K05", "K07",
        "L20", "L70", "L50", "B35",
        "E11", "E78.0", "E78.5", "E04", "E66",
        "N20", "N39.0", "N76", "N72"
    };

    private static IReadOnlyList<DepartmentCatalogResult> ParseDepartments(string rawJson)
    {
        if (string.IsNullOrWhiteSpace(rawJson)) return Array.Empty<DepartmentCatalogResult>();
        try
        {
            var root = JToken.Parse(rawJson);
            JArray list = null;
            if (root is JArray rArr) list = rArr;
            else if (root is JObject rObj)
            {
                var dataToken = rObj["Data"] ?? rObj["data"];
                if (dataToken is JArray dArr) list = dArr;
            }
            if (list == null) return Array.Empty<DepartmentCatalogResult>();

            var items = new List<DepartmentCatalogResult>();
            foreach (var row in list)
            {
                var deptId = row.Value<long>("CodeID");
                var code = row.Value<string>("Code") ?? "";
                var name = row.Value<string>("CodeName") ?? "";
                var parentId = row.Value<long?>("ParentCodeID") ?? 0;
                var isTraditional = row.Value<bool?>("IsTraditional") ?? false;
                items.Add(new DepartmentCatalogResult(deptId, code, name, parentId, isTraditional));
            }
            return items;
        }
        catch
        {
            return Array.Empty<DepartmentCatalogResult>();
        }
    }

    private static IReadOnlyList<long> ParseSignRoleIds(string rawJson)
    {
        if (string.IsNullOrWhiteSpace(rawJson)) return Array.Empty<long>();
        try
        {
            var root = JToken.Parse(rawJson);
            JArray list = null;
            if (root is JArray rArr) list = rArr;
            else if (root is JObject rObj)
            {
                var dataToken = rObj["Data"] ?? rObj["data"];
                if (dataToken is JArray dArr) list = dArr;
            }
            if (list == null) return Array.Empty<long>();

            var roleIds = new HashSet<long>();
            foreach (var row in list)
            {
                var roleId = row.Value<long?>("RoleID") ?? 0;
                if (roleId > 0) roleIds.Add(roleId);
            }
            return roleIds.ToList();
        }
        catch
        {
            return Array.Empty<long>();
        }
    }

    /// <summary>GetCodeList bọc ResponseFilterData: Data.Data[] {Code, CodeID, CodeName, DisplayName}.</summary>
    private static IReadOnlyList<SignRoleEmployee> ParseSignRoleEmployees(string rawJson)
    {
        if (string.IsNullOrWhiteSpace(rawJson)) return Array.Empty<SignRoleEmployee>();
        try
        {
            var root = JToken.Parse(rawJson);
            JArray list = null;
            if (root is JArray rArr) list = rArr;
            else if (root is JObject rObj)
            {
                var dataToken = rObj["Data"] ?? rObj["data"];
                if (dataToken is JArray dArr) list = dArr;
                else if (dataToken is JObject dObj && (dObj["Data"] ?? dObj["data"]) is JArray subArr) list = subArr;
            }
            if (list == null) return Array.Empty<SignRoleEmployee>();

            var byId = new Dictionary<long, SignRoleEmployee>();
            foreach (var row in list)
            {
                var id = row.Value<long?>("CodeID") ?? 0;
                if (id <= 0 || byId.ContainsKey(id)) continue;
                var code = row.Value<string>("Code")?.Trim() ?? "";
                var name = row.Value<string>("DisplayName")?.Trim();
                if (string.IsNullOrWhiteSpace(name)) name = row.Value<string>("CodeName")?.Trim() ?? "";
                byId[id] = new SignRoleEmployee(id, code, name);
            }
            return byId.Values.ToList();
        }
        catch
        {
            return Array.Empty<SignRoleEmployee>();
        }
    }

    private async Task<JToken> SendDirectAsync(
        HisOperation op, string path, string method, string rawBody, CancellationToken ct)
    {
        var res = await SendAsync(op, new HisRequest(path, method, null, rawBody), ct);
        if (res.IsSuccess)
        {
            return JToken.Parse(res.Value.RawJson);
        }

        var errorCode = res.Outcome switch
        {
            HisClientOutcome.Unauthorized => 4010,
            HisClientOutcome.Forbidden => 4030,
            HisClientOutcome.NotFound => 4040,
            HisClientOutcome.SignPrecondition => 4221,
            HisClientOutcome.Timeout => 5040,
            HisClientOutcome.BadGateway => 5022,
            HisClientOutcome.VendorNotConfigured => 5021,
            _ => 5022
        };

        throw new HealthExamException(errorCode, res.Message);
    }
}
