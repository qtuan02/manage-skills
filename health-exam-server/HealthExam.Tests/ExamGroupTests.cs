using HealthExam.API.Contracts;
using HealthExam.API.Middlewares;
using HealthExam.Application.Common;
using HealthExam.Domain.Common;
using HealthExam.Domain.ExamSessions;
using HealthExam.Domain.ExamRecords;
using HealthExam.Domain.Imports;
using HealthExam.Domain.Paraclinical;
using HealthExam.Domain.Webhooks;
using HealthExam.Domain.Integrations;
using HealthExam.Domain.Catalogs;
using Xunit;

namespace HealthExam.Tests;

/// <summary>
/// 10 Nhóm khám là danh mục đóng theo Thông tư (03-ksk-mapping §2.2). Test này chốt cứng
/// bộ mã để một lần "dọn dẹp" nào đó sau này không âm thầm đổi mã mà form-server đang dùng
/// làm FormCode.
/// </summary>
public class ExamGroupTests
{
    [Fact]
    public void Du_10_nhom_va_dung_thu_tu()
    {
        Assert.Equal(10, ExamGroups.All.Count);
        Assert.Equal("DTK_01", ExamGroups.All[0].VariantCode);
        Assert.Equal("Trẻ dưới 06 tuổi", ExamGroups.All[0].GroupName);
        Assert.Equal("Thuyền viên trên tàu biển Việt Nam", ExamGroups.All[9].GroupName);
    }

    [Fact]
    public void FormCode_ghep_dung_quy_uoc_KSK_V1()
        => Assert.Equal("KSK-V1-DTK_06", ExamGroups.All[5].FormCode);

    [Theory]
    [InlineData("DTK_01", true)]
    [InlineData("DTK_10", true)]
    [InlineData("DTK_11", false)]
    [InlineData("dtk_01", false)]
    [InlineData("", false)]
    public void IsValid_chan_ma_ngoai_danh_muc(string code, bool expected)
        => Assert.Equal(expected, ExamGroups.IsValid(code));
}
