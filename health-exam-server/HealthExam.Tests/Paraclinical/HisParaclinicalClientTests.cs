using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HealthExam.Application.Common;
using HealthExam.Application.Integrations;
using HealthExam.Infrastructure.Integrations.HisEmr;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json;
using Xunit;

namespace HealthExam.Tests.Paraclinical;

public class HisParaclinicalClientTests
{
    private sealed class FakeHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public HttpRequestMessage LastRequest { get; private set; }

        public FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        {
            _responder = responder;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(_responder(request));
        }
    }

    private static HttpResponseMessage JsonResponse(object obj, HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        var json = JsonConvert.SerializeObject(obj);
        return new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

    private (HisParaclinicalClient Client, FakeHttpMessageHandler Handler) CreateClient(
        Func<HttpRequestMessage, HttpResponseMessage> responder,
        bool enabled = true)
    {
        var handler = new FakeHttpMessageHandler(responder);
        var http = new HttpClient(handler);
        var options = new HisEmrOptions
        {
            Enabled = enabled,
            BaseUrl = "https://his.test-hospital.vn/his-api",
            CredentialHeaderName = "Authorization"
        };
        var context = new FakeHealthExamContext
        {
            TraceId = "TRACE-123",
            DivisionId = "DIV1"
        };
        context.Headers["Authorization"] = "Bearer test-his-token";

        var client = new HisParaclinicalClient(
            http,
            options,
            context,
            NullLogger<HisParaclinicalClient>.Instance);

        return (client, handler);
    }

    [Fact]
    public async Task GetAdmissionInfoAsync_returns_success_and_parses_admission()
    {
        var (client, handler) = CreateClient(req =>
        {
            Assert.Contains("/api/M06F00000/GetAdmissionInfo?admID=123", req.RequestUri.PathAndQuery);
            return JsonResponse(new
            {
                ErrorCode = 0,
                Message = "",
                Data = new
                {
                    AdmissionID = 123,
                    AdmissionCode = "ADM123",
                    PatientID = 456,
                    PatientCode = "BN456",
                    FullName = "Nguyễn Văn A",
                    DepartmentID = 10,
                    DepartmentName = "Khoa KSK",
                    AdmissionDate = new DateTime(2026, 9, 18)
                }
            });
        });

        var result = await client.GetAdmissionInfoAsync(123);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal(123, result.Value.AdmissionId);
        Assert.Equal("ADM123", result.Value.AdmissionCode);
        Assert.Equal("BN456", result.Value.PatientCode);
    }

    [Fact]
    public async Task CreateClinicalRequestAsync_sends_post_and_returns_summary()
    {
        var (client, handler) = CreateClient(req =>
        {
            Assert.Equal(HttpMethod.Post, req.Method);
            Assert.Contains("/api/M02F00710/ClinicalRequest", req.RequestUri.PathAndQuery);
            return JsonResponse(new
            {
                ErrorCode = 0,
                Message = "",
                Data = new
                {
                    ParaClinReqID = 8888,
                    ParaClinReqCode = "YCK001",
                    AdmissionID = 123,
                    PtID = 456,
                    Status = 1,
                    Details = new[]
                    {
                        new { ParaClinReqDtlID = 9001, MedSerID = 101, MedSerCode = "XN01", MedSerName = "Công thức máu", Quantity = 1, Status = 1 }
                    }
                }
            });
        });

        var req = new HisCreateClinicalRequest(
            PtId: 456,
            PtCode: "BN456",
            AdmissionId: 123,
            AdmissionCode: "ADM123",
            TreatmentProcessId: 789,
            DoctorId: 55,
            DepartmentId: 10,
            Note: "Chỉ định KSK",
            Items: new[] { new HisClinicalRequestItem(MedSerId: 101, Quantity: 1) });

        var result = await client.CreateClinicalRequestAsync(req);

        Assert.True(result.IsSuccess);
        Assert.Equal(8888, result.Value.ParaClinReqId);
        Assert.Equal("YCK001", result.Value.ParaClinReqCode);
        Assert.Single(result.Value.Details);
        Assert.Equal(9001, result.Value.Details[0].ParaClinReqDtlId);
    }

    [Fact]
    public async Task CareProcessConnectTPAsync_returns_process_id()
    {
        var (client, handler) = CreateClient(req =>
        {
            Assert.Equal(HttpMethod.Get, req.Method);
            Assert.Contains("/api/M02F40000/CareProcessConnectTP?reqID=8888&tpid=789", req.RequestUri.PathAndQuery);
            return JsonResponse(new
            {
                ErrorCode = 0,
                Message = "Thành công",
                Data = new
                {
                    ParaClinProcessID = 99999
                }
            });
        });

        var result = await client.CareProcessConnectTPAsync(new HisConnectTreatmentProcessRequest(8888, 789));

        Assert.True(result.IsSuccess);
        Assert.Equal(99999, result.Value.ParaClinProcessId);
    }

    [Fact]
    public async Task CareProcessConnectCancelTPAsync_calls_cancel_endpoint()
    {
        var (client, handler) = CreateClient(req =>
        {
            Assert.Equal(HttpMethod.Get, req.Method);
            Assert.Contains("/api/M02F40000/CareProcessConnectCancelTP?processID=99999", req.RequestUri.PathAndQuery);
            return JsonResponse(new
            {
                ErrorCode = 0,
                Message = "Cancelled"
            });
        });

        var result = await client.CareProcessConnectCancelTPAsync(new HisCancelConnectTreatmentProcessRequest(99999, "Bệnh nhân hủy"));

        Assert.True(result.IsSuccess);
        Assert.True(result.Value);
    }

    [Fact]
    public async Task GetParaClinicalByIdAsync_parses_report_and_details()
    {
        var (client, handler) = CreateClient(req =>
        {
            Assert.Contains("/api/M07F90000/RParaClinicalByID?resultID=RES-101", req.RequestUri.PathAndQuery);
            return JsonResponse(new
            {
                ErrorCode = 0,
                Data = new
                {
                    ResultID = "RES-101",
                    ParaClinReqID = 8888,
                    Status = "Completed",
                    Conclusion = "Bình thường",
                    DoctorName = "BS. Nam",
                    Details = new[]
                    {
                        new
                        {
                            DetailID = "DTL-1",
                            ServiceCode = "WBC",
                            ServiceName = "Bạch cầu",
                            Value = "7.5",
                            Unit = "G/L",
                            ReferenceRange = "4.0 - 10.0",
                            AbnormalFlag = "NORMAL"
                        }
                    }
                }
            });
        });

        var result = await client.GetParaClinicalByIdAsync("RES-101");

        Assert.True(result.IsSuccess);
        Assert.Equal("RES-101", result.Value.HisResultId);
        Assert.Equal("Bình thường", result.Value.Conclusion);
        Assert.Single(result.Value.Details);
        Assert.Equal("WBC", result.Value.Details[0].ServiceCode);
        Assert.Equal("7.5", result.Value.Details[0].Value);
    }

    [Fact]
    public async Task SendAsync_converts_non_zero_ErrorCode_to_SignPrecondition_failure()
    {
        var (client, handler) = CreateClient(req =>
        {
            return JsonResponse(new
            {
                ErrorCode = 1002,
                Message = "Bệnh nhân không tồn tại trên HIS",
                Data = (object)null
            });
        });

        var result = await client.GetAdmissionInfoAsync(999);

        Assert.False(result.IsSuccess);
        Assert.Equal(HisClientOutcome.SignPrecondition, result.Outcome);
        Assert.Contains("Bệnh nhân không tồn tại trên HIS", result.Message);
    }

    [Fact]
    public async Task SendAsync_handles_401_Unauthorized()
    {
        var (client, handler) = CreateClient(req =>
        {
            return new HttpResponseMessage(HttpStatusCode.Unauthorized);
        });

        var result = await client.GetAdmissionInfoAsync(123);

        Assert.False(result.IsSuccess);
        Assert.Equal(HisClientOutcome.Unauthorized, result.Outcome);
    }

    [Fact]
    public async Task SendAsync_handles_404_NotFound()
    {
        var (client, handler) = CreateClient(req =>
        {
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var result = await client.GetAdmissionInfoAsync(123);

        Assert.False(result.IsSuccess);
        Assert.Equal(HisClientOutcome.NotFound, result.Outcome);
    }

    [Fact]
    public async Task SendAsync_returns_VendorNotConfigured_when_disabled()
    {
        var (client, handler) = CreateClient(req => new HttpResponseMessage(HttpStatusCode.OK), enabled: false);

        var result = await client.GetAdmissionInfoAsync(123);

        Assert.False(result.IsSuccess);
        Assert.Equal(HisClientOutcome.VendorNotConfigured, result.Outcome);
    }
}
