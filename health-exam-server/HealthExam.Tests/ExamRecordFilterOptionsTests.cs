using System.Net;
using Newtonsoft.Json.Linq;
using Xunit;

namespace HealthExam.Tests;

public class ExamRecordFilterOptionsTests : IClassFixture<AuthTestHost>
{
    private readonly AuthTestHost _host;

    public ExamRecordFilterOptionsTests(AuthTestHost host) => _host = host;

    [Fact]
    public async Task Filter_options_tra_danh_sach_trang_thai_va_doi_tuong_cho_FE()
    {
        var response = await _host.CreateEmployeeClient()
            .GetAsync("/v1/exam-records/filter-options");
        var body = JObject.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var statuses = body["Data"]!["Statuses"]!;
        Assert.Equal(7, statuses.Count());
        Assert.Equal("Tất cả", statuses[0]!["Label"]!.Value<string>());
        Assert.Equal("Chưa đăng ký", statuses[1]!["Label"]!.Value<string>());
        Assert.Equal("Hủy đăng ký", statuses[5]!["Label"]!.Value<string>());
        Assert.Equal("Hủy khám", statuses[6]!["Label"]!.Value<string>());
        Assert.Equal(4, statuses[5]!["State"]!.Value<short>());
        Assert.Equal(4, statuses[6]!["State"]!.Value<short>());

        var examTypes = body["Data"]!["ExamTypes"]!;
        Assert.Equal(11, examTypes.Count());
        Assert.Equal("Tất cả", examTypes[0]!["Label"]!.Value<string>());
        Assert.Equal("DTK_02", examTypes[2]!["VariantCode"]!.Value<string>());
        Assert.Equal("Trẻ đủ 06–18 tuổi", examTypes[2]!["Label"]!.Value<string>());
        Assert.Equal("DTK_10", examTypes[10]!["VariantCode"]!.Value<string>());
        Assert.Equal("Thuyền viên tàu biển Việt Nam", examTypes[10]!["Label"]!.Value<string>());
    }
}
