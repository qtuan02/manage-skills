using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HealthExam.Application.Integrations;
using HealthExam.Infrastructure.Integrations.HisEmr;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HealthExam.Tests.Infrastructure;

public class HisEmrClientPdfTests
{
    private sealed class DelegatingHandlerStub : DelegatingHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;
        public DelegatingHandlerStub(Func<HttpRequestMessage, HttpResponseMessage> handler) => _handler = handler;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(_handler(request));
    }

    private static HisEmrClient CreateClient(Func<HttpRequestMessage, HttpResponseMessage> handler)
    {
        var stub = new DelegatingHandlerStub(handler);
        var options = new HisEmrOptions
        {
            Enabled = true,
            BaseUrl = "http://his-mock.local",
            CredentialHeaderName = "Authorization"
        };
        return new HisEmrClient(new HttpClient(stub), options, NullLogger<HisEmrClient>.Instance);
    }

    [Fact]
    public async Task RenderFormPdfAsync_returns_bytes_when_his_responds_pdf()
    {
        var emrDataId = Guid.NewGuid();
        var expectedBytes = Encoding.ASCII.GetBytes("%PDF-1.7 mock content");

        var client = CreateClient(req =>
        {
            Assert.Equal(HttpMethod.Get, req.Method);
            Assert.Contains($"api/M03F10010/VEMR?EMRDataID={emrDataId}&IsJson=false", req.RequestUri!.ToString());
            Assert.Equal("Bearer token123", req.Headers.Authorization?.ToString());

            var resp = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(expectedBytes)
            };
            resp.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/pdf");
            return resp;
        });

        var res = await client.RenderFormPdfAsync(
            emrDataId,
            new HisRequest("", "GET", "Bearer token123", null, "trace-1", "DEV"),
            CancellationToken.None);

        Assert.True(res.IsSuccess);
        Assert.Equal(expectedBytes, res.Value);
    }

    [Fact]
    public async Task RenderSignedAdmissionPdfAsync_calls_VEMRs_and_returns_pdf()
    {
        const long admissionId = 7001;
        var bytes = Encoding.ASCII.GetBytes("%PDF-1.7 signed");
        var client = CreateClient(req =>
        {
            Assert.Contains($"api/M02F01500/VEMRs?admissionID={admissionId}", req.RequestUri!.ToString());
            Assert.Equal("Bearer token123", req.Headers.Authorization?.ToString());
            Assert.Equal("trace-1", System.Linq.Enumerable.First(req.Headers.GetValues("X-Trace-Id")));
            Assert.Equal("DEV", System.Linq.Enumerable.First(req.Headers.GetValues("X-Division-Id")));

            var resp = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(bytes)
            };
            resp.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/pdf");
            return resp;
        });

        var result = await client.RenderSignedAdmissionPdfAsync(
            admissionId,
            new HisRequest("", "GET", "Bearer token123", TraceId: "trace-1", DivisionId: "DEV"));

        Assert.True(result.IsSuccess);
        Assert.Equal(bytes, result.Value);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task RenderSignedAdmissionPdfAsync_returns_SignPrecondition_when_admissionId_invalid(long invalidAdmissionId)
    {
        var client = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.OK));

        var result = await client.RenderSignedAdmissionPdfAsync(
            invalidAdmissionId,
            new HisRequest("", "GET", "Bearer token123"));

        Assert.False(result.IsSuccess);
        Assert.Equal(HisClientOutcome.SignPrecondition, result.Outcome);
    }

    [Fact]
    public async Task RenderSignedAdmissionPdfAsync_maps_specific_not_found_message_to_NotFound()
    {
        const long admissionId = 7001;
        var json = "{\"ErrorCode\":1,\"Message\":\"Không tìm thấy dữ liệu hồ sơ bệnh án\",\"Data\":null}";
        var client = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        });

        var result = await client.RenderSignedAdmissionPdfAsync(
            admissionId,
            new HisRequest("", "GET", "Bearer token123"));

        Assert.False(result.IsSuccess);
        Assert.Equal(HisClientOutcome.NotFound, result.Outcome);
        Assert.Equal("Không tìm thấy dữ liệu hồ sơ bệnh án", result.Message);
    }

    [Fact]
    public async Task RenderSignedAdmissionPdfAsync_maps_other_bad_request_to_SignPrecondition()
    {
        const long admissionId = 7001;
        var json = "{\"ErrorCode\":1,\"Message\":\"Hồ sơ chưa hoàn tất quy trình\",\"Data\":null}";
        var client = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        });

        var result = await client.RenderSignedAdmissionPdfAsync(
            admissionId,
            new HisRequest("", "GET", "Bearer token123"));

        Assert.False(result.IsSuccess);
        Assert.Equal(HisClientOutcome.SignPrecondition, result.Outcome);
        Assert.Equal("Hồ sơ chưa hoàn tất quy trình", result.Message);
    }

    [Fact]
    public async Task RenderSignedAdmissionPdfAsync_maps_empty_or_non_pdf_body_to_BadGateway()
    {
        const long admissionId = 7001;
        var client = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(Encoding.ASCII.GetBytes("NOT-A-PDF-CONTENT"))
        });

        var result = await client.RenderSignedAdmissionPdfAsync(
            admissionId,
            new HisRequest("", "GET", "Bearer token123"));

        Assert.False(result.IsSuccess);
        Assert.Equal(HisClientOutcome.BadGateway, result.Outcome);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, HisClientOutcome.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden, HisClientOutcome.Forbidden)]
    [InlineData(HttpStatusCode.NotFound, HisClientOutcome.NotFound)]
    public async Task RenderSignedAdmissionPdfAsync_maps_standard_error_codes(HttpStatusCode status, HisClientOutcome expectedOutcome)
    {
        const long admissionId = 7001;
        var client = CreateClient(_ => new HttpResponseMessage(status));

        var result = await client.RenderSignedAdmissionPdfAsync(
            admissionId,
            new HisRequest("", "GET", "Bearer token123"));

        Assert.False(result.IsSuccess);
        Assert.Equal(expectedOutcome, result.Outcome);
    }
}
