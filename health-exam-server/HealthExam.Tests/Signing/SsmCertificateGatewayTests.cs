using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using HealthExam.Infrastructure.Integrations.Ssm;
using HealthExam.Infrastructure.Integrations.SignServer;
using Xunit;

namespace HealthExam.Tests.Signing;

public class SsmCertificateGatewayTests
{
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly string _body;
        public HttpRequestMessage LastRequest { get; private set; }

        public StubHandler(HttpStatusCode status, string body)
        {
            _status = status;
            _body = body;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(new HttpResponseMessage(_status)
            {
                Content = new StringContent(_body)
            });
        }
    }

    private static SignServerOptions Options => new()
    {
        SsmBaseUrl = "http://ssm.internal",
        SignServerBaseUrl = "http://sign.internal",
        InternalSecret = "s3cr3t",
        TimeoutSeconds = 60
    };

    /// <summary>
    /// ssm-server bọc CertInfo trong {Data: ...}. Gateway phải bóc đúng lớp đó và gắn
    /// Bearer SECRET_INTER, vì ssm-server chỉ chấp nhận secret nội bộ.
    /// </summary>
    [Fact]
    public async Task Doc_duoc_chung_thu_va_gan_secret_noi_bo()
    {
        var stub = new StubHandler(HttpStatusCode.OK, """
        {"Data":{"Name":"BS A","CertName":"cn=bs-a","Pin":"1234","Company":"VIS",
                 "CoPCode":"CCHN01","Valid":"2027-01-01T00:00:00"}}
        """);
        var gateway = new SsmCertificateGateway(new HttpClient(stub), Options);

        var res = await gateway.GetAsync("NV001");

        Assert.True(res.Found);
        Assert.Equal("cn=bs-a", res.Certificate.CertName);
        Assert.Equal("1234", res.Certificate.Pin);
        Assert.Equal("VIS", res.Certificate.Company);
        Assert.Equal("CCHN01", res.Certificate.CoPCode);
        Assert.Contains("userCode=NV001", stub.LastRequest.RequestUri.ToString());
        Assert.Equal("Bearer s3cr3t", stub.LastRequest.Headers.Authorization.ToString());
    }

    /// <summary>
    /// Chứng thư hết hạn phải bị từ chối tại đây, không để lọt xuống sign-server rồi
    /// mới vỡ giữa lượt ký.
    /// </summary>
    [Fact]
    public async Task Tu_choi_chung_thu_het_han()
    {
        var stub = new StubHandler(HttpStatusCode.OK, """
        {"Data":{"CertName":"cn=bs-a","Pin":"1234","Company":"VIS","CoPCode":"C1",
                 "Valid":"2020-01-01T00:00:00"}}
        """);
        var gateway = new SsmCertificateGateway(new HttpClient(stub), Options);

        var res = await gateway.GetAsync("NV001");

        Assert.False(res.Found);
        Assert.Contains("hết hạn", res.Message);
    }

    [Fact]
    public async Task Khong_co_chung_thu_tra_Found_false()
    {
        var stub = new StubHandler(HttpStatusCode.BadRequest, "không tìm thấy");
        var gateway = new SsmCertificateGateway(new HttpClient(stub), Options);

        var res = await gateway.GetAsync("NV404");

        Assert.False(res.Found);
        Assert.Null(res.Certificate);
        Assert.Contains("chưa có chứng thư", res.Message);
    }

    /// <summary>ssm-server 5xx là dịch vụ lỗi, không phải "nhân viên chưa có chứng thư" — báo sai là bác sĩ đi xin cấp lại cert vô ích.</summary>
    [Fact]
    public async Task Ssm_5xx_bao_dich_vu_loi_khong_bao_chua_co_chung_thu()
    {
        var stub = new StubHandler(HttpStatusCode.InternalServerError, "boom");
        var gateway = new SsmCertificateGateway(new HttpClient(stub), Options);

        var res = await gateway.GetAsync("NV001");

        Assert.False(res.Found);
        Assert.Contains("Dịch vụ chứng thư số lỗi", res.Message);
        Assert.DoesNotContain("chưa có chứng thư", res.Message);
    }

    /// <summary>Valid không parse được không được ném exception lên tận controller (500) — trả Found=false có lời.</summary>
    [Fact]
    public async Task Valid_khong_hop_le_thi_tra_Found_false_khong_nem()
    {
        var stub = new StubHandler(HttpStatusCode.OK, """
        {"Data":{"CertName":"cn=bs-a","Pin":"1234","Company":"VIS","CoPCode":"C1","Valid":"khong-phai-ngay"}}
        """);
        var gateway = new SsmCertificateGateway(new HttpClient(stub), Options);

        var res = await gateway.GetAsync("NV001");

        Assert.False(res.Found);
        Assert.Contains("ngày hết hạn không hợp lệ", res.Message);
    }

    /// <summary>EmployeeCertificate chứa PIN — ToString() (log, exception message, debugger) không bao giờ được lộ nó.</summary>
    [Fact]
    public void EmployeeCertificate_ToString_khong_lo_PIN()
    {
        var cert = new HealthExam.Application.Integrations.EmployeeCertificate(
            "NV001", "cn=bs-a", "VIS", "C1", "1234", new DateTime(2027, 1, 1));

        var text = cert.ToString();

        Assert.DoesNotContain("1234", text);
        Assert.Contains("NV001", text);
        Assert.Contains("cn=bs-a", text);
        Assert.Contains("2027-01-01", text);
    }
}
