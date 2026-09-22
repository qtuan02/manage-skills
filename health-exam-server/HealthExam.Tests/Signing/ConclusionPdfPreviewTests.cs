using System.Linq;
using System.Text;
using System.Threading.Tasks;
using HealthExam.Application.Common;
using HealthExam.Application.RegistrationForms;
using HealthExam.Domain.ExamRecords;
using Xunit;

namespace HealthExam.Tests.Signing;

public class ConclusionPdfPreviewTests
{
    /// <summary>
    /// Hồ sơ đã ký thì luôn trả bản đã ký. Trả bản nháp render lại là phát ra một tài liệu
    /// KHÔNG có chữ ký nào mà người đọc tưởng là bản chính thức.
    /// </summary>
    [Fact]
    public async Task Da_ky_thi_tra_file_tu_MinIO_chu_khong_render_lai()
    {
        var f = new ConclusionFixture();
        await f.SignAllSections();
        await f.SignConclusion();
        f.Db.Db.ChangeTracker.Clear();
        var renderCountAfterSign = f.His.RenderCount;

        var pdf = await f.Db.PreviewConclusionPdf(f.Store, f.His).HandleAsync(
            new PreviewRegistrationFormPdfQuery(ConclusionFixture.DivisionId, f.Record.RecordID, "Bearer t"));

        Assert.True(pdf.IsSuccess);
        Assert.Equal(f.Store.Objects.Values.Single(), pdf.Value);
        Assert.Equal(renderCountAfterSign, f.His.RenderCount);
    }

    /// <summary>
    /// Đã Signed mà file biến mất khỏi MinIO là sự cố dữ liệu — phải báo lỗi, tuyệt đối
    /// không âm thầm thay bằng bản nháp. Chốt cả hai vế: đúng mã lỗi NotFound VÀ chưa từng
    /// đụng tới HIS render — không chỉ "có lỗi xảy ra".
    /// </summary>
    [Fact]
    public async Task Da_ky_nhung_mat_file_thi_bao_loi_chu_khong_tra_ban_nhap()
    {
        var f = new ConclusionFixture();
        await f.SignAllSections();
        await f.SignConclusion();
        f.Db.Db.ChangeTracker.Clear();
        var renderCountAfterSign = f.His.RenderCount;
        f.Store.Objects.Clear();

        var pdf = await f.Db.PreviewConclusionPdf(f.Store, f.His).HandleAsync(
            new PreviewRegistrationFormPdfQuery(ConclusionFixture.DivisionId, f.Record.RecordID, "Bearer t"));

        Assert.False(pdf.IsSuccess);
        Assert.Equal(ApplicationFailureCode.NotFound, pdf.Failure.Code);
        Assert.Equal(renderCountAfterSign, f.His.RenderCount);
    }

    /// <summary>
    /// SignStatus=Signed nhưng chưa từng ghi được SignedFilePath (sự cố dữ liệu khác) — vẫn phải
    /// báo lỗi ngay, không thử tải MinIO bằng path rỗng rồi càng không render bản nháp.
    /// </summary>
    [Fact]
    public async Task Da_ky_nhung_chua_co_duong_dan_file_thi_bao_loi()
    {
        var f = new ConclusionFixture();
        f.Db.SetSignStatus(f.Record.RecordID, ExamRecordSignStatus.Signed);

        var pdf = await f.Db.PreviewConclusionPdf(f.Store, f.His).HandleAsync(
            new PreviewRegistrationFormPdfQuery(ConclusionFixture.DivisionId, f.Record.RecordID, "Bearer t"));

        Assert.False(pdf.IsSuccess);
        Assert.Equal(ApplicationFailureCode.NotFound, pdf.Failure.Code);
        Assert.Equal(0, f.His.RenderCount);
    }

    /// <summary>
    /// Hàng kiểu R6 (migration): SignStatus đã collapse về New/Failed nhưng SignedFilePath vẫn
    /// còn trỏ vào file cũ trên MinIO/his-server. Chỉ SignStatus mới quyết định nhánh — còn sót
    /// SignedFilePath không được coi là "đã ký", tuyệt đối không trả nhầm file cũ đó.
    /// </summary>
    [Theory]
    [InlineData(ExamRecordSignStatus.New)]
    [InlineData(ExamRecordSignStatus.Failed)]
    public async Task Chua_ky_nhung_con_duong_dan_file_cu_thi_van_tra_ban_nhap(string signStatus)
    {
        var f = new ConclusionFixture();
        var legacyPath = $"{ConclusionFixture.DivisionId}/2026/01/legacy.pdf";
        f.Db.SetSignStatus(f.Record.RecordID, signStatus);
        f.Db.SetSignedFilePath(f.Record.RecordID, legacyPath);
        f.Store.Objects[legacyPath] = Encoding.ASCII.GetBytes("%PDF-1.7 legacy");

        var pdf = await f.Db.PreviewConclusionPdf(f.Store, f.His).HandleAsync(
            new PreviewRegistrationFormPdfQuery(ConclusionFixture.DivisionId, f.Record.RecordID, "Bearer t"));

        Assert.True(pdf.IsSuccess);
        Assert.Equal(Encoding.ASCII.GetBytes("%PDF-1.7 draft"), pdf.Value);
        Assert.Equal(1, f.His.RenderCount);
        Assert.NotEqual(Encoding.ASCII.GetBytes("%PDF-1.7 legacy"), pdf.Value);
    }

    [Fact]
    public async Task Chua_ky_thi_tra_ban_nhap_render_tu_HIS()
    {
        var f = new ConclusionFixture();

        var pdf = await f.Db.PreviewConclusionPdf(f.Store, f.His).HandleAsync(
            new PreviewRegistrationFormPdfQuery(ConclusionFixture.DivisionId, f.Record.RecordID, "Bearer t"));

        Assert.True(pdf.IsSuccess);
        Assert.Equal(Encoding.ASCII.GetBytes("%PDF-1.7 draft"), pdf.Value);
    }
}
