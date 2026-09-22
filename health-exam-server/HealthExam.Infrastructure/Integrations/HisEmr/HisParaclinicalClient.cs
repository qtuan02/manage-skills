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
using HealthExam.Application.Integrations;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace HealthExam.Infrastructure.Integrations.HisEmr;

public class HisParaclinicalClient : IHisParaclinicalClient
{
    public const string RouteGetAdmissionInfo = "api/M06F00000/GetAdmissionInfo";
    public const string RouteGetListMedSerType = "api/M02F00710/GetListMedSerType";
    public const string RouteGetListMedicalServiceItem = "api/M02F00710/GetListMedicalServiceItem";
    public const string RouteClinicalRequest = "api/M02F00710/ClinicalRequest";
    public const string RouteCareProcessConnectTP = "api/M02F40000/CareProcessConnectTP";
    public const string RouteCareProcessConnectCancelTP = "api/M02F40000/CareProcessConnectCancelTP";
    public const string RouteRParaClinicalByAdmID = "api/M07F90000/RParaClinicalByAdmID";
    public const string RouteRParaClinicalResultByAdmID = "api/M07F90000/RParaClinicalResultByAdmID";
    public const string RouteRParaClinicalByID = "api/M07F90000/RParaClinicalByID";

    private readonly HttpClient _http;
    private readonly HisEmrOptions _options;
    private readonly IHealthExamContext _context;
    private readonly ILogger<HisParaclinicalClient> _logger;

    public HisParaclinicalClient(
        HttpClient http,
        HisEmrOptions options,
        IHealthExamContext context,
        ILogger<HisParaclinicalClient> logger)
    {
        _http = http;
        _options = options;
        _context = context;
        _logger = logger;
    }

    protected virtual async Task<HisClientResult<JToken>> SendAsync(
        string relativePath,
        HttpMethod method,
        object body = null,
        string credential = null,
        CancellationToken ct = default)
    {
        if (!_options.Enabled)
        {
            return HisClientResult<JToken>.Fail(
                HisClientOutcome.VendorNotConfigured,
                "Tích hợp HIS EMR chưa được kích hoạt");
        }

        if (string.IsNullOrWhiteSpace(_options.BaseUrl) ||
            !Uri.TryCreate(_options.BaseUrl, UriKind.Absolute, out var baseUri) ||
            (baseUri.Scheme != Uri.UriSchemeHttp && baseUri.Scheme != Uri.UriSchemeHttps))
        {
            return HisClientResult<JToken>.Fail(
                HisClientOutcome.VendorNotConfigured,
                "Địa chỉ HIS EMR chưa được cấu hình hợp lệ");
        }

        var cred = credential;
        if (string.IsNullOrWhiteSpace(cred) && _context != null)
        {
            cred = _context.GetRequestHeader(_options.CredentialHeaderName);
        }

        if (string.IsNullOrWhiteSpace(cred))
        {
            return HisClientResult<JToken>.Fail(
                HisClientOutcome.Unauthorized,
                "Thiếu thông tin xác thực HIS trong request");
        }

        var fullUri = new Uri(new Uri(_options.BaseUrl.TrimEnd('/') + "/", UriKind.Absolute), relativePath.TrimStart('/'));
        using var req = new HttpRequestMessage(method, fullUri);
        req.Headers.TryAddWithoutValidation(_options.CredentialHeaderName, cred);

        var traceId = _context?.TraceId;
        if (!string.IsNullOrEmpty(traceId))
            req.Headers.TryAddWithoutValidation("X-Trace-Id", traceId);

        var divisionId = _context?.DivisionId;
        if (!string.IsNullOrEmpty(divisionId))
            req.Headers.TryAddWithoutValidation("X-Division-Id", divisionId);

        if (body != null)
        {
            var json = body is string s ? s : JsonConvert.SerializeObject(body);
            req.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(req, ct);
        }
        catch (TaskCanceledException)
        {
            return HisClientResult<JToken>.Fail(HisClientOutcome.Timeout, "HIS không phản hồi trong thời gian cho phép");
        }
        catch (HttpRequestException)
        {
            return HisClientResult<JToken>.Fail(HisClientOutcome.BadGateway, "Không thể kết nối đến máy chủ HIS");
        }
        catch (Exception ex)
        {
            return HisClientResult<JToken>.Fail(HisClientOutcome.BadGateway, $"Lỗi truyền thông HIS: {ex.Message}");
        }

        var statusCode = response.StatusCode;
        if (statusCode == HttpStatusCode.Unauthorized)
            return HisClientResult<JToken>.Fail(HisClientOutcome.Unauthorized, "HIS từ chối xác thực (401)");

        if (statusCode == HttpStatusCode.Forbidden)
            return HisClientResult<JToken>.Fail(HisClientOutcome.Forbidden, "HIS từ chối phân quyền (403)");

        if (statusCode == HttpStatusCode.NotFound)
            return HisClientResult<JToken>.Fail(HisClientOutcome.NotFound, "Không tìm thấy dữ liệu trên HIS (404)");

        string rawBody;
        try
        {
            rawBody = await response.Content.ReadAsStringAsync(ct);
        }
        catch (Exception)
        {
            return HisClientResult<JToken>.Fail(HisClientOutcome.BadGateway, "Không đọc được nội dung từ HIS");
        }

        HisEnvelope envelope = null;
        if (!string.IsNullOrWhiteSpace(rawBody))
        {
            try
            {
                envelope = JsonConvert.DeserializeObject<HisEnvelope>(rawBody);
            }
            catch
            {
                // Non-envelope JSON or text
            }
        }

        if (statusCode == HttpStatusCode.BadRequest)
        {
            var msg = envelope?.Message;
            if (string.IsNullOrWhiteSpace(msg)) msg = "HIS từ chối thao tác (400)";
            return HisClientResult<JToken>.Fail(HisClientOutcome.SignPrecondition, msg);
        }

        if (envelope == null)
        {
            if (!response.IsSuccessStatusCode)
                return HisClientResult<JToken>.Fail(HisClientOutcome.BadGateway, $"HIS trả về mã lỗi {(int)statusCode}");

            try
            {
                var directToken = JToken.Parse(rawBody);
                return HisClientResult<JToken>.Success(directToken);
            }
            catch
            {
                return HisClientResult<JToken>.Fail(HisClientOutcome.BadGateway, "Nội dung phản hồi từ HIS không hợp lệ");
            }
        }

        if (envelope.ErrorCode != 0)
        {
            var msg = !string.IsNullOrWhiteSpace(envelope.Message) ? envelope.Message : $"HIS lỗi với mã {envelope.ErrorCode}";
            return HisClientResult<JToken>.Fail(HisClientOutcome.SignPrecondition, msg, envelope.Data);
        }

        if (!response.IsSuccessStatusCode)
        {
            return HisClientResult<JToken>.Fail(HisClientOutcome.BadGateway, $"HIS trả về mã lỗi HTTP {(int)statusCode}");
        }

        return HisClientResult<JToken>.Success(envelope.Data ?? JValue.CreateNull());
    }

    public async Task<HisClientResult<HisAdmissionInfo>> GetAdmissionInfoAsync(
        long admissionId, string credential = null, CancellationToken ct = default)
    {
        var res = await SendAsync($"{RouteGetAdmissionInfo}?admID={admissionId}", HttpMethod.Get, null, credential, ct);
        if (!res.IsSuccess)
            return HisClientResult<HisAdmissionInfo>.Fail(res.Outcome, res.Message, res.Payload);

        var data = res.Value;
        if (data == null || data.Type == JTokenType.Null)
            return HisClientResult<HisAdmissionInfo>.Fail(HisClientOutcome.NotFound, "Không có thông tin lượt tiếp nhận");

        var info = new HisAdmissionInfo(
            AdmissionId: data["AdmissionID"]?.Value<long>() ?? admissionId,
            AdmissionCode: data["AdmissionCode"]?.Value<string>() ?? "",
            PatientId: data["PatientID"]?.Value<long>() ?? 0,
            PatientCode: data["PatientCode"]?.Value<string>() ?? "",
            FullName: data["FullName"]?.Value<string>() ?? "",
            DepartmentId: data["DepartmentID"]?.Value<int?>(),
            DepartmentName: data["DepartmentName"]?.Value<string>() ?? "",
            AdmissionDate: data["AdmissionDate"]?.Value<DateTime?>());

        return HisClientResult<HisAdmissionInfo>.Success(info);
    }

    public async Task<HisClientResult<IReadOnlyList<HisMedicalServiceType>>> GetListMedSerTypeAsync(
        string credential = null, CancellationToken ct = default)
    {
        var res = await SendAsync(RouteGetListMedSerType, HttpMethod.Get, null, credential, ct);
        if (!res.IsSuccess)
            return HisClientResult<IReadOnlyList<HisMedicalServiceType>>.Fail(res.Outcome, res.Message, res.Payload);

        var list = new List<HisMedicalServiceType>();
        if (res.Value is JArray arr)
        {
            foreach (var item in arr)
            {
                list.Add(new HisMedicalServiceType(
                    ServiceTypeId: item["MedSerTypeID"]?.Value<int>() ?? item["ID"]?.Value<int>() ?? 0,
                    ServiceTypeCode: item["MedSerTypeCode"]?.Value<string>() ?? item["Code"]?.Value<string>() ?? "",
                    ServiceTypeName: item["MedSerTypeName"]?.Value<string>() ?? item["Name"]?.Value<string>() ?? "",
                    ParaclinicalKind: item["ParaclinicalKind"]?.Value<string>() ?? ""));
            }
        }

        return HisClientResult<IReadOnlyList<HisMedicalServiceType>>.Success(list);
    }

    public async Task<HisClientResult<IReadOnlyList<HisMedicalServiceItem>>> GetListMedicalServiceItemAsync(
        int? serviceTypeId = null, string credential = null, CancellationToken ct = default)
    {
        var route = serviceTypeId.HasValue
            ? $"{RouteGetListMedicalServiceItem}?medSerTypeID={serviceTypeId.Value}"
            : RouteGetListMedicalServiceItem;

        var res = await SendAsync(route, HttpMethod.Get, null, credential, ct);
        if (!res.IsSuccess)
            return HisClientResult<IReadOnlyList<HisMedicalServiceItem>>.Fail(res.Outcome, res.Message, res.Payload);

        var list = new List<HisMedicalServiceItem>();
        if (res.Value is JArray arr)
        {
            foreach (var item in arr)
            {
                list.Add(new HisMedicalServiceItem(
                    MedSerId: item["MedSerID"]?.Value<long>() ?? item["ID"]?.Value<long>() ?? 0,
                    MedSerCode: item["MedSerCode"]?.Value<string>() ?? item["Code"]?.Value<string>() ?? "",
                    MedSerName: item["MedSerName"]?.Value<string>() ?? item["Name"]?.Value<string>() ?? "",
                    ServiceTypeId: item["MedSerTypeID"]?.Value<int>() ?? 0,
                    ServiceTypeName: item["MedSerTypeName"]?.Value<string>() ?? "",
                    Price: item["Price"]?.Value<decimal>() ?? 0m,
                    Unit: item["Unit"]?.Value<string>() ?? ""));
            }
        }

        return HisClientResult<IReadOnlyList<HisMedicalServiceItem>>.Success(list);
    }

    public async Task<HisClientResult<HisClinicalRequestSummary>> CreateClinicalRequestAsync(
        HisCreateClinicalRequest request, string credential = null, CancellationToken ct = default)
    {
        var payload = new
        {
            PtID = request.PtId,
            PtCode = request.PtCode,
            AdmissionID = request.AdmissionId,
            AdmissionCode = request.AdmissionCode,
            TPID = request.TreatmentProcessId,
            PCReqDoctorID = request.DoctorId,
            ReqDeptID = request.DepartmentId,
            Note = request.Note,
            Items = request.Items.Select(x => new
            {
                MedSerID = x.MedSerId,
                Quantity = x.Quantity,
                Note = x.Note
            })
        };

        var res = await SendAsync(RouteClinicalRequest, HttpMethod.Post, payload, credential, ct);
        if (!res.IsSuccess)
            return HisClientResult<HisClinicalRequestSummary>.Fail(res.Outcome, res.Message, res.Payload);

        var data = res.Value;
        var summary = ParseClinicalRequestSummary(data);
        return HisClientResult<HisClinicalRequestSummary>.Success(summary);
    }

    public async Task<HisClientResult<HisClinicalRequestSummary>> GetClinicalRequestAsync(
        long paraClinReqId, string credential = null, CancellationToken ct = default)
    {
        var res = await SendAsync($"{RouteClinicalRequest}?reqID={paraClinReqId}", HttpMethod.Get, null, credential, ct);
        if (!res.IsSuccess)
            return HisClientResult<HisClinicalRequestSummary>.Fail(res.Outcome, res.Message, res.Payload);

        var summary = ParseClinicalRequestSummary(res.Value);
        return HisClientResult<HisClinicalRequestSummary>.Success(summary);
    }

    public async Task<HisClientResult<IReadOnlyList<HisClinicalRequestSummary>>> GetClinicalRequestsByAdmissionAsync(
        long admissionId, string credential = null, CancellationToken ct = default)
    {
        var res = await SendAsync($"{RouteClinicalRequest}/{admissionId}", HttpMethod.Get, null, credential, ct);
        if (!res.IsSuccess)
            return HisClientResult<IReadOnlyList<HisClinicalRequestSummary>>.Fail(res.Outcome, res.Message, res.Payload);

        var list = new List<HisClinicalRequestSummary>();
        if (res.Value is JArray arr)
        {
            foreach (var item in arr)
                list.Add(ParseClinicalRequestSummary(item));
        }
        else if (res.Value != null && res.Value.HasValues)
        {
            list.Add(ParseClinicalRequestSummary(res.Value));
        }

        return HisClientResult<IReadOnlyList<HisClinicalRequestSummary>>.Success(list);
    }

    public async Task<HisClientResult<bool>> DeleteClinicalRequestAsync(
        long paraClinReqId, string reason = "", string credential = null, CancellationToken ct = default)
    {
        var query = $"{RouteClinicalRequest}?reqID={paraClinReqId}";
        if (!string.IsNullOrWhiteSpace(reason))
            query += $"&reason={Uri.EscapeDataString(reason)}";

        var res = await SendAsync(query, HttpMethod.Delete, null, credential, ct);
        if (!res.IsSuccess)
            return HisClientResult<bool>.Fail(res.Outcome, res.Message, res.Payload);

        return HisClientResult<bool>.Success(true);
    }

    public async Task<HisClientResult<HisConnectTreatmentProcessResult>> CareProcessConnectTPAsync(
        HisConnectTreatmentProcessRequest request, string credential = null, CancellationToken ct = default)
    {
        var query = $"{RouteCareProcessConnectTP}?reqID={request.ParaClinReqId}&tpid={request.TreatmentProcessId}";
        if (request.ParaClinReqDtlIds != null && request.ParaClinReqDtlIds.Count > 0)
        {
            query += $"&dtlIDs={string.Join(",", request.ParaClinReqDtlIds)}";
        }

        var res = await SendAsync(query, HttpMethod.Get, null, credential, ct);
        if (!res.IsSuccess)
            return HisClientResult<HisConnectTreatmentProcessResult>.Fail(res.Outcome, res.Message, res.Payload);

        var data = res.Value;
        long processId = 0;
        string message = "";

        if (data != null)
        {
            if (data.Type == JTokenType.Integer)
                processId = data.Value<long>();
            else
            {
                processId = data["ParaClinProcessID"]?.Value<long>() ?? data["ProcessID"]?.Value<long>() ?? data["ID"]?.Value<long>() ?? 0;
                message = data["Message"]?.Value<string>() ?? "";
            }
        }

        return HisClientResult<HisConnectTreatmentProcessResult>.Success(
            new HisConnectTreatmentProcessResult(processId, message));
    }

    public async Task<HisClientResult<bool>> CareProcessConnectCancelTPAsync(
        HisCancelConnectTreatmentProcessRequest request, string credential = null, CancellationToken ct = default)
    {
        var query = $"{RouteCareProcessConnectCancelTP}?processID={request.ParaClinProcessId}";
        if (!string.IsNullOrWhiteSpace(request.Reason))
            query += $"&reason={Uri.EscapeDataString(request.Reason)}";

        var res = await SendAsync(query, HttpMethod.Get, null, credential, ct);
        if (!res.IsSuccess)
            return HisClientResult<bool>.Fail(res.Outcome, res.Message, res.Payload);

        return HisClientResult<bool>.Success(true);
    }

    public async Task<HisClientResult<IReadOnlyList<HisParaclinicalReport>>> GetParaClinicalByAdmissionAsync(
        long admissionId, string credential = null, CancellationToken ct = default)
    {
        var res = await SendAsync($"{RouteRParaClinicalByAdmID}?admID={admissionId}", HttpMethod.Get, null, credential, ct);
        if (!res.IsSuccess)
            return HisClientResult<IReadOnlyList<HisParaclinicalReport>>.Fail(res.Outcome, res.Message, res.Payload);

        return HisClientResult<IReadOnlyList<HisParaclinicalReport>>.Success(ParseReports(res.Value));
    }

    public async Task<HisClientResult<IReadOnlyList<HisParaclinicalReport>>> GetParaClinicalResultByAdmissionAsync(
        long admissionId, string credential = null, CancellationToken ct = default)
    {
        var res = await SendAsync($"{RouteRParaClinicalResultByAdmID}?admID={admissionId}", HttpMethod.Get, null, credential, ct);
        if (!res.IsSuccess)
            return HisClientResult<IReadOnlyList<HisParaclinicalReport>>.Fail(res.Outcome, res.Message, res.Payload);

        return HisClientResult<IReadOnlyList<HisParaclinicalReport>>.Success(ParseReports(res.Value));
    }

    public async Task<HisClientResult<HisParaclinicalReport>> GetParaClinicalByIdAsync(
        string hisResultId, string credential = null, CancellationToken ct = default)
    {
        var res = await SendAsync($"{RouteRParaClinicalByID}?resultID={hisResultId}", HttpMethod.Get, null, credential, ct);
        if (!res.IsSuccess)
            return HisClientResult<HisParaclinicalReport>.Fail(res.Outcome, res.Message, res.Payload);

        var report = ParseSingleReport(res.Value);
        return HisClientResult<HisParaclinicalReport>.Success(report);
    }

    private static HisClinicalRequestSummary ParseClinicalRequestSummary(JToken data)
    {
        if (data == null || data.Type == JTokenType.Null)
            return new HisClinicalRequestSummary(0, "", 0, "", 0, "", 0, null, Array.Empty<HisClinicalRequestDetailItem>());

        var details = new List<HisClinicalRequestDetailItem>();
        if (data["Details"] is JArray arr)
        {
            foreach (var d in arr)
            {
                details.Add(new HisClinicalRequestDetailItem(
                    ParaClinReqDtlId: d["ParaClinReqDtlID"]?.Value<long>() ?? d["ID"]?.Value<long>() ?? 0,
                    MedSerId: d["MedSerID"]?.Value<long>() ?? 0,
                    MedSerCode: d["MedSerCode"]?.Value<string>() ?? "",
                    MedSerName: d["MedSerName"]?.Value<string>() ?? "",
                    Quantity: d["Quantity"]?.Value<int>() ?? 1,
                    Status: d["Status"]?.Value<int>() ?? 0,
                    ParaClinProcessId: d["ParaClinProcessID"]?.Value<long?>()));
            }
        }

        return new HisClinicalRequestSummary(
            ParaClinReqId: data["ParaClinReqID"]?.Value<long>() ?? data["ID"]?.Value<long>() ?? 0,
            ParaClinReqCode: data["ParaClinReqCode"]?.Value<string>() ?? data["Code"]?.Value<string>() ?? "",
            AdmissionId: data["AdmissionID"]?.Value<long>() ?? 0,
            AdmissionCode: data["AdmissionCode"]?.Value<string>() ?? "",
            PtId: data["PtID"]?.Value<long>() ?? 0,
            PtCode: data["PtCode"]?.Value<string>() ?? "",
            Status: data["Status"]?.Value<int>() ?? 0,
            RequestDate: data["RequestDate"]?.Value<DateTime?>(),
            Details: details);
    }

    private static IReadOnlyList<HisParaclinicalReport> ParseReports(JToken token)
    {
        var list = new List<HisParaclinicalReport>();
        if (token is JArray arr)
        {
            foreach (var item in arr)
                list.Add(ParseSingleReport(item));
        }
        else if (token != null && token.HasValues)
        {
            list.Add(ParseSingleReport(token));
        }
        return list;
    }

    private static HisParaclinicalReport ParseSingleReport(JToken item)
    {
        if (item == null || item.Type == JTokenType.Null)
            return new HisParaclinicalReport("", null, null, null, "", DateTime.UtcNow, "", "", Array.Empty<HisParaclinicalResultDetail>());

        var details = new List<HisParaclinicalResultDetail>();
        if (item["Details"] is JArray dArr)
        {
            foreach (var d in dArr)
            {
                details.Add(new HisParaclinicalResultDetail(
                    HisDetailId: d["DetailID"]?.Value<string>() ?? d["ID"]?.Value<string>() ?? "",
                    ServiceCode: d["ServiceCode"]?.Value<string>() ?? "",
                    ServiceName: d["ServiceName"]?.Value<string>() ?? "",
                    Value: d["Value"]?.Value<string>() ?? "",
                    Text: d["Text"]?.Value<string>() ?? "",
                    Unit: d["Unit"]?.Value<string>() ?? "",
                    ReferenceRange: d["ReferenceRange"]?.Value<string>() ?? "",
                    AbnormalFlag: d["AbnormalFlag"]?.Value<string>() ?? ""));
            }
        }

        return new HisParaclinicalReport(
            HisResultId: item["ResultID"]?.Value<string>() ?? item["ID"]?.Value<string>() ?? "",
            ParaClinReqId: item["ParaClinReqID"]?.Value<long?>(),
            ParaClinProcessId: item["ParaClinProcessID"]?.Value<long?>(),
            AdmissionId: item["AdmissionID"]?.Value<long?>(),
            Status: item["Status"]?.Value<string>() ?? "Completed",
            ResultDate: item["ResultDate"]?.Value<DateTime?>() ?? DateTime.UtcNow,
            Conclusion: item["Conclusion"]?.Value<string>() ?? "",
            DoctorName: item["DoctorName"]?.Value<string>() ?? "",
            Details: details);
    }
}
