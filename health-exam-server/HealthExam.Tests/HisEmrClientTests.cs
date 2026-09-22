#nullable enable

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HealthExam.API.Contracts;
using HealthExam.API.Middlewares;
using HealthExam.Application.Common;
using HealthExam.Application.His;
using HealthExam.Application.Integrations;
using HealthExam.Domain.Common;
using HealthExam.Domain.ExamSessions;
using HealthExam.Domain.ExamRecords;
using HealthExam.Domain.Imports;
using HealthExam.Domain.Paraclinical;
using HealthExam.Domain.Webhooks;
using HealthExam.Domain.Integrations;
using HealthExam.Domain.Catalogs;
using HealthExam.Infrastructure.Integrations.HisEmr;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace HealthExam.Tests;

public class HisEmrClientTests
{
    private static readonly HisEmrOptions DefaultOptions = new()
    {
        Enabled = true,
        BaseUrl = "https://his.test",
        Timeout = TimeSpan.FromSeconds(5),
        CredentialHeaderName = "Authorization"
    };

    private static (HisEmrClient client, RecordingHandler handler, FakeHealthExamContext context) CreateClient(
        HisEmrOptions? options = null,
        HttpResponseMessage? response = null)
    {
        var opt = options ?? DefaultOptions;
        var handler = new RecordingHandler(response ?? JsonResponse(new { ErrorCode = 0, Message = "", Data = new JObject() }));
        var context = new FakeHealthExamContext
        {
            TraceId = "TRACE-HIS-1",
            DivisionId = "DEV"
        };
        context.Headers[opt.CredentialHeaderName] = "Bearer test-his-token";

        var client = new HisEmrClient(
            new HttpClient(handler),
            opt,
            context,
            NullLogger<HisEmrClient>.Instance);

        return (client, handler, context);
    }

    private static HttpResponseMessage JsonResponse(object body, HttpStatusCode status = HttpStatusCode.OK)
    {
        return new HttpResponseMessage(status)
        {
            Content = new StringContent(JsonConvert.SerializeObject(body), Encoding.UTF8, "application/json")
        };
    }

    [Fact]
    public async Task GetTemplateAsync_calls_exact_route_and_query()
    {
        var templateId = Guid.NewGuid();
        var (client, handler, _) = CreateClient();

        await client.GetTemplateAsync(templateId, CancellationToken.None);

        var call = Assert.Single(handler.Calls);
        Assert.Equal(HttpMethod.Get, call.Method);
        Assert.Equal($"https://his.test/api/M03F00030/GetTemplateByID?templateID={templateId}", call.Url);
    }

    [Fact]
    public async Task GetTemplateListAsync_calls_exact_route()
    {
        var (client, handler, _) = CreateClient();

        await client.GetTemplateListAsync(CancellationToken.None);

        var call = Assert.Single(handler.Calls);
        Assert.Equal(HttpMethod.Get, call.Method);
        Assert.Equal("https://his.test/api/M03F00030/GetListTemplateByEMR", call.Url);
    }

    [Fact]
    public async Task GetTemplateTreeAsync_without_and_with_admission()
    {
        var templateId = Guid.NewGuid();
        var (client, handler, _) = CreateClient();

        await client.GetTemplateTreeAsync(templateId, null, CancellationToken.None);
        Assert.Equal($"https://his.test/api/M03F00030/GetTreeTemplateByID?templateID={templateId}", handler.Calls[0].Url);

        await client.GetTemplateTreeAsync(templateId, 901, CancellationToken.None);
        Assert.Equal($"https://his.test/api/M03F00030/GetTreeTemplateByID?templateID={templateId}&admissionID=901", handler.Calls[1].Url);
    }

    [Fact]
    public async Task GetTreeByFileDocTypeAsync_calls_exact_route()
    {
        var (client, handler, _) = CreateClient();

        await client.GetTreeByFileDocTypeAsync(15, CancellationToken.None);

        var call = Assert.Single(handler.Calls);
        Assert.Equal(HttpMethod.Get, call.Method);
        Assert.Equal("https://his.test/api/M03F00030/GetTreeByFileDocTypeID?fileDocTypeID=15", call.Url);
    }

    [Fact]
    public async Task GetMedicalProcessesAsync_calls_exact_route_and_query()
    {
        var (client, handler, _) = CreateClient();

        await client.GetMedicalProcessesAsync(901, CancellationToken.None);

        var call = Assert.Single(handler.Calls);
        Assert.Equal(HttpMethod.Get, call.Method);
        Assert.EndsWith("/api/M02F01500/RMedicalProcess?admissionID=901", call.Url);
    }

    [Fact]
    public async Task GetMedicalProcessAsync_calls_exact_route()
    {
        var processId = Guid.NewGuid();
        var (client, handler, _) = CreateClient();

        await client.GetMedicalProcessAsync(processId, CancellationToken.None);

        var call = Assert.Single(handler.Calls);
        Assert.Equal(HttpMethod.Get, call.Method);
        Assert.EndsWith($"/api/M02F01500/RMedicalProcessByID?id={processId}", call.Url);
    }

    [Fact]
    public async Task CreateAdmissionAsync_calls_deployed_post_route_and_exact_payload()
    {
        var (client, handler, _) = CreateClient(response:
            JsonResponse(new { ErrorCode = 0, Message = "", Data = 7001001 }));
        var request = new HisAdmissionWireRequest
        {
            AdmissionCode = "HEX-00000000000000000000000000000001",
            AdmissionDate = new DateTime(2026, 9, 9),
            DepartmentID = 12,
            DepartmentCode = "10",
            IsOutPatient = 2,
            PatientCode = "HEX-PHASE1-DEFAULT-0001",
            FirstName = "A",
            LastName = "Nguyễn Văn",
            FullName = "Nguyễn Văn A",
            I_Gender = 1,
            BirthYear = 1990,
            IDCard = "012345678901"
        };

        var data = await client.CreateAdmissionAsync(request, CancellationToken.None);

        var call = Assert.Single(handler.Calls);
        Assert.Equal(HttpMethod.Post, call.Method);
        Assert.Equal("https://his.test/api/M02F00000/GetAdmissionInfo", call.Url);
        Assert.Equal("Bearer test-his-token", call.Headers["Authorization"]);
        Assert.Equal(7001001L, data.Value<long>());
        var body = JObject.Parse(call.Body);
        Assert.Equal(2, body["IsOutPatient"]!.Value<int>());
        Assert.Equal("10", body["DepartmentCode"]!.Value<string>());
        Assert.Equal("HEX-PHASE1-DEFAULT-0001", body["PatientCode"]!.Value<string>());
        Assert.Equal("Nguyễn Văn", body["LastName"]!.Value<string>());
    }

    [Fact]
    public async Task Typed_CreateAdmissionAsync_calls_deployed_post_route_and_returns_typed_id()
    {
        var (client, handler, _) = CreateClient(response:
            JsonResponse(new { ErrorCode = 0, Message = "", Data = 7001001 }));
        var pat = new HisPatientCreateRequest(
            "PAT-01", "A", "Nguyễn", "Nguyễn A", 1, new DateTime(1990, 1, 1), 1990, "0123", "0901", "a@test.vn", "Hà Nội");
        var adm = new HisAdmissionCreateRequest(
            2, "HEX-ADM-1", new DateTime(2026, 9, 12), 15, "KSK", pat);

        var result = await client.CreateAdmissionAsync(adm, new HisCallContext("test-his-token", "TRACE-1", "D01"));

        Assert.True(result.IsSuccess);
        Assert.Equal(7001001L, result.Value);
        var call = Assert.Single(handler.Calls);
        Assert.Equal(HttpMethod.Post, call.Method);
        Assert.Equal("https://his.test/api/M02F00000/GetAdmissionInfo", call.Url);
    }

    [Fact]
    public async Task Typed_CreateMedicalProcessAsync_calls_deployed_post_route_and_exact_payload()
    {
        var (client, handler, _) = CreateClient(response:
            JsonResponse(new { ErrorCode = 0, Message = "", Data = new JArray { new JObject { ["Id"] = Guid.NewGuid().ToString() } } }));
        var req = new MedicalProcessCreateRequest("KSK03", 7001001L);

        var result = await client.CreateMedicalProcessAsync(req, new HisCallContext("Bearer test-his-token", "TRACE-1", "D01"));

        Assert.True(result.IsSuccess);
        var call = Assert.Single(handler.Calls);
        Assert.Equal(HttpMethod.Post, call.Method);
        Assert.Equal("https://his.test/api/M02F01500/CMedicalProcess", call.Url);
        Assert.Equal("Bearer test-his-token", call.Headers["Authorization"]);
        Assert.Equal("TRACE-1", call.Headers["X-Trace-Id"]);
        Assert.Equal("D01", call.Headers["X-Division-Id"]);
        var body = JObject.Parse(call.Body);
        Assert.Equal("KSK03", body["MedicalTypeCode"]!.Value<string>());
        Assert.Equal(7001001L, body["AdmissionID"]!.Value<long>());
        Assert.Null(body["MedicalTypeCodeOld"]!.Value<string>());
    }

    [Fact]
    public async Task Typed_CreateMedicalProcessAsync_returns_unauthorized_when_credential_missing()
    {
        var (client, handler, context) = CreateClient(response:
            JsonResponse(new { ErrorCode = 0, Message = "", Data = new JArray() }));
        context.Headers.Clear();

        var req = new MedicalProcessCreateRequest("KSK03", 7001001L);
        var result = await client.CreateMedicalProcessAsync(req, new HisCallContext(null, "TRACE-1", "D01"));

        Assert.False(result.IsSuccess);
        Assert.Equal(HisClientOutcome.Unauthorized, result.Outcome);
        Assert.Empty(handler.Calls);
    }

    [Fact]
    public async Task Typed_CreatePatientAsync_calls_configured_route_and_returns_typed_id()
    {
        var options = new HisEmrOptions
        {
            Enabled = true,
            BaseUrl = "https://his.test",
            PatientCreateRoute = "api/M02F00000/CUCommonPatient"
        };
        var (client, handler, _) = CreateClient(options: options, response:
            JsonResponse(new { ErrorCode = 0, Message = "", Data = 8001001 }));
        var pat = new HisPatientCreateRequest(
            "PAT-01", "A", "Nguyễn", "Nguyễn A", 1, new DateTime(1990, 1, 1), 1990, "0123", "0901", "a@test.vn", "Hà Nội");

        var result = await client.CreatePatientAsync(pat, new HisCallContext("test-his-token", "TRACE-1", "D01"));

        Assert.True(result.IsSuccess);
        Assert.Equal(8001001L, result.Value);
        var call = Assert.Single(handler.Calls);
        Assert.Equal("https://his.test/api/M02F00000/CUCommonPatient", call.Url);
    }

    [Fact]
    public async Task CreateAdmissionAsync_does_not_retry_a_connection_failure()
    {
        var handler = new ThrowingHandler(new HttpRequestException("Connection reset"));
        var context = new FakeHealthExamContext();
        context.Headers["Authorization"] = "Bearer token";
        var client = new HisEmrClient(new HttpClient(handler), DefaultOptions, context,
            NullLogger<HisEmrClient>.Instance);

        await Assert.ThrowsAsync<HealthExamException>(() =>
            client.CreateAdmissionAsync(new HisAdmissionWireRequest { AdmissionCode = "HEX-1" }));

        Assert.Equal(1, handler.Attempts);
    }

    [Fact]
    public async Task CreatePatientAsync_calls_configured_route_and_exact_payload()
    {
        var options = new HisEmrOptions
        {
            Enabled = true,
            BaseUrl = "https://his.test",
            Timeout = TimeSpan.FromSeconds(5),
            CredentialHeaderName = "Authorization",
            PatientCreateRoute = "api/M02F00000/CUCommonPatient"
        };
        var (client, handler, _) = CreateClient(options: options, response:
            JsonResponse(new { ErrorCode = 0, Message = "", Data = 8001001 }));
        var request = new HisPatientWireRequest
        {
            PatientCode = "PAT-2026-0001",
            FirstName = "A",
            LastName = "Nguyễn Văn",
            FullName = "Nguyễn Văn A",
            I_Gender = 1,
            BirthDate = new DateTime(1990, 5, 12),
            BirthYear = 1990,
            IDCard = "012345678901",
            MobileNo = "0901234567",
            PersonalEmail = "a@test.vn",
            CurrentAddress = "Hà Nội"
        };

        var data = await client.CreatePatientAsync(request, CancellationToken.None);

        var call = Assert.Single(handler.Calls);
        Assert.Equal(HttpMethod.Post, call.Method);
        Assert.Equal("https://his.test/api/M02F00000/CUCommonPatient", call.Url);
        Assert.Equal("Bearer test-his-token", call.Headers["Authorization"]);
        Assert.Equal("TRACE-HIS-1", call.Headers["X-Trace-Id"]);
        Assert.Equal("DEV", call.Headers["X-Division-Id"]);
        Assert.Equal(8001001L, data.Value<long>());
    }

    [Fact]
    public async Task CreatePatientAsync_throws_VendorNotConfigured_when_route_is_empty()
    {
        var options = new HisEmrOptions
        {
            Enabled = true,
            BaseUrl = "https://his.test",
            PatientCreateRoute = ""
        };
        var (client, _, _) = CreateClient(options: options);

        var ex = await Assert.ThrowsAsync<HealthExamException>(() =>
            client.CreatePatientAsync(new HisPatientWireRequest { PatientCode = "PAT-1" }));

        Assert.Equal(ErrorCodes.VendorNotConfigured, ex.ErrorCode);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("\"string-id\"")]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("true")]
    public async Task CreatePatientAsync_throws_HisBadGateway_on_invalid_data(string dataJson)
    {
        var options = new HisEmrOptions
        {
            Enabled = true,
            BaseUrl = "https://his.test",
            PatientCreateRoute = "api/M02F00000/CUCommonPatient"
        };
        var (client, _, _) = CreateClient(options: options, response:
            JsonResponse(new { ErrorCode = 0, Message = "", Data = JToken.Parse(dataJson) }));

        var ex = await Assert.ThrowsAsync<HealthExamException>(() =>
            client.CreatePatientAsync(new HisPatientWireRequest { PatientCode = "PAT-1" }));

        Assert.Equal(ErrorCodes.HisBadGateway, ex.ErrorCode);
    }

    [Fact]
    public async Task CreatePatientAsync_does_not_retry_a_connection_failure()
    {
        var options = new HisEmrOptions
        {
            Enabled = true,
            BaseUrl = "https://his.test",
            PatientCreateRoute = "api/M02F00000/CUCommonPatient"
        };
        var handler = new ThrowingHandler(new HttpRequestException("Connection reset"));
        var context = new FakeHealthExamContext();
        context.Headers["Authorization"] = "Bearer token";
        var client = new HisEmrClient(new HttpClient(handler), options, context,
            NullLogger<HisEmrClient>.Instance);

        await Assert.ThrowsAsync<HealthExamException>(() =>
            client.CreatePatientAsync(new HisPatientWireRequest { PatientCode = "PAT-1" }));

        Assert.Equal(1, handler.Attempts);
    }

    [Fact]
    public async Task Admission_400_problem_details_preserves_field_validation_error()
    {
        var response = JsonResponse(new
        {
            title = "One or more validation errors occurred.",
            errors = new Dictionary<string, string[]>
            {
                ["DepartmentID"] = ["The DepartmentID field is required."]
            }
        }, HttpStatusCode.BadRequest);
        var (client, _, _) = CreateClient(response: response);

        var ex = await Assert.ThrowsAsync<HealthExamException>(() =>
            client.CreateAdmissionAsync(new HisAdmissionWireRequest
            {
                AdmissionCode = "HEX-1",
                PatientCode = "BN001"
            }));

        Assert.Equal(ErrorCodes.SignPrecondition, ex.ErrorCode);
        Assert.Contains("DepartmentID", ex.Message);
        Assert.Contains("The DepartmentID field is required.", ex.Message);
    }

    [Fact]
    public async Task Forwards_configured_credential_and_context_headers()
    {
        var (client, handler, _) = CreateClient();

        await client.GetTemplateListAsync(CancellationToken.None);

        var call = Assert.Single(handler.Calls);
        Assert.Equal("Bearer test-his-token", call.Headers["Authorization"]);
        Assert.Equal("TRACE-HIS-1", call.Headers["X-Trace-Id"]);
        Assert.Equal("DEV", call.Headers["X-Division-Id"]);

        // Credential must NOT be in URI or body
        Assert.DoesNotContain("test-his-token", call.Url);
        Assert.DoesNotContain("test-his-token", call.Body);
    }

    [Fact]
    public async Task Missing_or_disabled_configuration_fails_before_handler()
    {
        var handler = new RecordingHandler(JsonResponse(new { }));
        var context = new FakeHealthExamContext();
        context.Headers["Authorization"] = "Bearer token";

        var disabledClient = new HisEmrClient(
            new HttpClient(handler),
            new HisEmrOptions { Enabled = false, BaseUrl = "https://his.test" },
            context,
            NullLogger<HisEmrClient>.Instance);

        var ex = await Assert.ThrowsAsync<HealthExamException>(() => disabledClient.GetTemplateListAsync());
        Assert.Equal(ErrorCodes.VendorNotConfigured, ex.ErrorCode);
        Assert.Empty(handler.Calls);

        var emptyUrlClient = new HisEmrClient(
            new HttpClient(handler),
            new HisEmrOptions { Enabled = true, BaseUrl = "" },
            context,
            NullLogger<HisEmrClient>.Instance);

        var ex2 = await Assert.ThrowsAsync<HealthExamException>(() => emptyUrlClient.GetTemplateListAsync());
        Assert.Equal(ErrorCodes.VendorNotConfigured, ex2.ErrorCode);
        Assert.Empty(handler.Calls);
    }

    [Fact]
    public async Task Missing_inbound_credential_raises_4010_before_handler()
    {
        var handler = new RecordingHandler(JsonResponse(new { }));
        var context = new FakeHealthExamContext(); // no credential header set

        var client = new HisEmrClient(
            new HttpClient(handler),
            DefaultOptions,
            context,
            NullLogger<HisEmrClient>.Instance);

        var ex = await Assert.ThrowsAsync<HealthExamException>(() => client.GetTemplateListAsync());
        Assert.Equal(ErrorCodes.Unauthorized, ex.ErrorCode);
        Assert.Empty(handler.Calls);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, ErrorCodes.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden, ErrorCodes.Forbidden)]
    [InlineData(HttpStatusCode.NotFound, ErrorCodes.NotFound)]
    public async Task Downstream_401_403_404_map_to_health_exam_error_codes(HttpStatusCode status, int expectedCode)
    {
        var response = new HttpResponseMessage(status) { Content = new StringContent("error") };
        var (client, _, _) = CreateClient(response: response);

        var ex = await Assert.ThrowsAsync<HealthExamException>(() => client.GetTemplateListAsync());
        Assert.Equal(expectedCode, ex.ErrorCode);
    }

    [Fact]
    public async Task Downstream_400_maps_to_4221_and_preserves_message()
    {
        var response = JsonResponse(new { ErrorCode = 1, Message = "Chưa đủ điều kiện ký" }, HttpStatusCode.BadRequest);
        var (client, _, _) = CreateClient(response: response);

        var ex = await Assert.ThrowsAsync<HealthExamException>(() => client.GetTemplateListAsync());
        Assert.Equal(ErrorCodes.SignPrecondition, ex.ErrorCode);
        Assert.Contains("Chưa đủ điều kiện ký", ex.Message);
    }

    [Fact]
    public async Task Nonzero_business_envelope_maps_to_4221_and_preserves_message()
    {
        var response = JsonResponse(new { ErrorCode = 99, Message = "Quy trình đã kết thúc" }, HttpStatusCode.OK);
        var (client, _, _) = CreateClient(response: response);

        var ex = await Assert.ThrowsAsync<HealthExamException>(() => client.GetTemplateListAsync());
        Assert.Equal(ErrorCodes.SignPrecondition, ex.ErrorCode);
        Assert.Contains("Quy trình đã kết thúc", ex.Message);
    }

    [Fact]
    public async Task Html_or_malformed_json_maps_to_5022()
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("<html><body>502 Bad Gateway from Nginx</body></html>", Encoding.UTF8, "text/html")
        };
        var (client, _, _) = CreateClient(response: response);

        var ex = await Assert.ThrowsAsync<HealthExamException>(() => client.GetTemplateListAsync());
        Assert.Equal(ErrorCodes.HisBadGateway, ex.ErrorCode);
    }

    [Fact]
    public async Task Success_envelope_with_null_data_maps_to_5022()
    {
        var response = JsonResponse(new { ErrorCode = 0, Message = "OK", Data = (object?)null });
        var (client, _, _) = CreateClient(response: response);

        var ex = await Assert.ThrowsAsync<HealthExamException>(() => client.GetTemplateListAsync());
        Assert.Equal(ErrorCodes.HisBadGateway, ex.ErrorCode);
    }

    [Fact]
    public async Task TaskCanceledException_maps_to_5040()
    {
        var handler = new ThrowingHandler(new TaskCanceledException("HttpClient timeout"));
        var context = new FakeHealthExamContext();
        context.Headers["Authorization"] = "Bearer token";

        var client = new HisEmrClient(
            new HttpClient(handler),
            DefaultOptions,
            context,
            NullLogger<HisEmrClient>.Instance);

        var ex = await Assert.ThrowsAsync<HealthExamException>(() => client.GetTemplateListAsync(CancellationToken.None));
        Assert.Equal(ErrorCodes.HisTimeout, ex.ErrorCode);
    }

    [Fact]
    public async Task Caller_cancellation_is_not_converted_to_5040()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var handler = new ThrowingHandler(new OperationCanceledException(cts.Token));
        var context = new FakeHealthExamContext();
        context.Headers["Authorization"] = "Bearer token";

        var client = new HisEmrClient(
            new HttpClient(handler),
            DefaultOptions,
            context,
            NullLogger<HisEmrClient>.Instance);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.GetTemplateListAsync(cts.Token));
    }

    [Fact]
    public async Task Definition_get_retries_once_on_connection_failure()
    {
        var handler = new ThrowingHandler(new HttpRequestException("Connection reset"));
        var context = new FakeHealthExamContext();
        context.Headers["Authorization"] = "Bearer token";

        var client = new HisEmrClient(
            new HttpClient(handler),
            DefaultOptions,
            context,
            NullLogger<HisEmrClient>.Instance);

        var ex = await Assert.ThrowsAsync<HealthExamException>(() => client.GetTemplateListAsync());
        Assert.Equal(ErrorCodes.HisBadGateway, ex.ErrorCode);
        Assert.Equal(2, handler.Attempts);
    }

    [Fact]
    public async Task Definition_get_does_not_retry_when_http_response_received()
    {
        var handler = new RecordingHandler(new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent("Server Error")
        });
        var context = new FakeHealthExamContext();
        context.Headers["Authorization"] = "Bearer token";

        var client = new HisEmrClient(
            new HttpClient(handler),
            DefaultOptions,
            context,
            NullLogger<HisEmrClient>.Instance);

        var ex = await Assert.ThrowsAsync<HealthExamException>(() => client.GetTemplateListAsync());
        Assert.Equal(ErrorCodes.HisBadGateway, ex.ErrorCode);
        Assert.Single(handler.Calls);
    }

    [Fact]
    public async Task ListEmployeeSignRoleIdsAsync_parses_distinct_positive_role_ids()
    {
        var (client, handler, _) = CreateClient(response: JsonResponse(new
        {
            ErrorCode = 0,
            Data = new[]
            {
                new { GroupID = 1, RoleID = 45, RoleName = "Bác sĩ khám", DepartmentID = 10 },
                new { GroupID = 2, RoleID = 45, RoleName = "Bác sĩ khám", DepartmentID = 11 },
                new { GroupID = 3, RoleID = 60, RoleName = "Kết luận", DepartmentID = 10 }
            }
        }));

        var res = await client.ListEmployeeSignRoleIdsAsync(1274, new HisCallContext("Bearer explicit-token", "trace-99", "DIV-1"));

        Assert.True(res.IsSuccess);
        Assert.Equal(new long[] { 45, 60 }, res.Value.OrderBy(x => x));
        var call = Assert.Single(handler.Calls);
        Assert.Equal("https://his.test/api/M02F30000/GetPermissionGroup?empID=1274", call.Url);
        Assert.Equal("Bearer explicit-token", call.Headers["Authorization"]);
        Assert.Equal("trace-99", call.Headers["X-Trace-Id"]);
        Assert.Equal("DIV-1", call.Headers["X-Division-Id"]);
    }

    /// <summary>
    /// Lượt tra vai trò ký phải ghi log dưới tên thao tác của chính nó. Trước đây mượn
    /// ListDefinitions nên log/metric HIS gộp tra quyền vào đọc định nghĩa biểu mẫu.
    /// </summary>
    [Fact]
    public async Task ListEmployeeSignRoleIdsAsync_logs_under_ListSignRoles_operation()
    {
        var handler = new RecordingHandler(JsonResponse(new { ErrorCode = 0, Data = new[] { new { RoleID = 45 } } }));
        var context = new FakeHealthExamContext { TraceId = "TRACE-HIS-1", DivisionId = "DEV" };
        var logger = new CapturingLogger<HisEmrClient>();
        var client = new HisEmrClient(new HttpClient(handler), DefaultOptions, context, logger);

        var res = await client.ListEmployeeSignRoleIdsAsync(1274, new HisCallContext("Bearer t", "trace-1", "DEV"));

        Assert.True(res.IsSuccess);
        Assert.Contains(logger.Messages, m => m.Contains("HIS ListSignRoles hoàn thành"));
        Assert.DoesNotContain(logger.Messages, m => m.Contains("ListDefinitions"));
    }

    /// <summary>
    /// Danh sách người ký của một bước = HIS GetCodeList key=EmployeeRole. Phải gửi amount=0:
    /// mặc định HIS trả 20 dòng đầu — thiếu bác sĩ mà không hề báo lỗi.
    /// </summary>
    [Fact]
    public async Task ListSignRoleEmployeesAsync_goi_GetCodeList_EmployeeRole_amount_0_va_parse_Data_Data()
    {
        var (client, handler, _) = CreateClient(response: JsonResponse(new
        {
            ErrorCode = 0,
            Data = new
            {
                Filter = "", Amount = 4, Page = 0, Total = 4, Sort = "",
                Data = new[]
                {
                    new { Code = "40", CodeID = 40, CodeName = "BS. Nguyễn Văn An", DisplayName = "BS. Nguyễn Văn An" },
                    new { Code = "40", CodeID = 40, CodeName = "BS. Nguyễn Văn An", DisplayName = "BS. Nguyễn Văn An" },
                    new { Code = "41", CodeID = 41, CodeName = "BS. Trần Thị Bình", DisplayName = "" },
                    new { Code = "", CodeID = 0, CodeName = "rác", DisplayName = "" }
                }
            }
        }));

        var res = await client.ListSignRoleEmployeesAsync(45, new HisCallContext("Bearer explicit-token", "trace-99", "DIV-1"));

        Assert.True(res.IsSuccess);
        Assert.Equal(new[]
        {
            new SignRoleEmployee(40, "40", "BS. Nguyễn Văn An"),
            new SignRoleEmployee(41, "41", "BS. Trần Thị Bình")
        }, res.Value);
        var call = Assert.Single(handler.Calls);
        Assert.Equal("https://his.test/api/GetCodeList?key=EmployeeRole&filterCode=45&amount=0", call.Url);
        Assert.Equal("Bearer explicit-token", call.Headers["Authorization"]);
        Assert.Equal("trace-99", call.Headers["X-Trace-Id"]);
    }

    [Fact]
    public async Task ListSignRoleEmployeesAsync_tra_Fail_khi_HIS_loi()
    {
        var (client, _, _) = CreateClient(response: new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent("Server Error")
        });

        var res = await client.ListSignRoleEmployeesAsync(45, new HisCallContext("Bearer t", "trace", "DIV-1"));

        Assert.False(res.IsSuccess);
        Assert.Null(res.Value);
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Messages.Add(formatter(state, exception));
    }

    private sealed record HttpCall(HttpMethod Method, string Url, string Body, Dictionary<string, string> Headers);

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly HttpResponseMessage _response;
        public List<HttpCall> Calls { get; } = new();

        public RecordingHandler(HttpResponseMessage response) => _response = response;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content != null ? await request.Content.ReadAsStringAsync(cancellationToken) : "";
            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var h in request.Headers)
            {
                headers[h.Key] = string.Join(",", h.Value);
            }

            Calls.Add(new HttpCall(request.Method, request.RequestUri!.ToString(), body, headers));
            return _response;
        }
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        private readonly Exception _exception;
        public int Attempts { get; private set; }

        public ThrowingHandler(Exception exception) => _exception = exception;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Attempts++;
            throw _exception;
        }
    }
}
