using System;
using System.Threading.Tasks;
using HealthExam.Application.Integrations;
using Xunit;

namespace HealthExam.Tests.Signing;

public class ExamFileStorePathTests
{
    /// <summary>
    /// Đường dẫn phải phân tầng theo tenant và tháng, nếu không một bucket phẳng sẽ có
    /// hàng trăm nghìn object và liệt kê không nổi.
    /// </summary>
    [Fact]
    public async Task Duong_dan_phan_tang_theo_tenant_va_thang()
    {
        var recordId = Guid.Parse("11111111-2222-3333-4444-555555555555");
        var path = ExamFileStorePaths.ConclusionPdf(
            "DIV01", new DateTime(2026, 9, 18, 0, 0, 0, DateTimeKind.Utc), recordId);

        Assert.Equal("DIV01/2026/09/11111111-2222-3333-4444-555555555555.pdf", path);
        await Task.CompletedTask;
    }
}
