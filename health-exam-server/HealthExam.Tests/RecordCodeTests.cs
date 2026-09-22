using HealthExam.Server.Service;
using Xunit;

namespace HealthExam.Tests;

/// <summary>
/// Quy ước đặt mã hồ sơ {SessionCode}-{số thứ tự}.
///
/// PHẠM VI: chỉ phần thuần logic. Việc CẤP số (UPDATE ... RETURNING giữ khoá hàng đợt khám)
/// nằm ở PostgreSQL nên không kiểm được ở đây — bộ test của repo chạy không cần DB, và một
/// test tự bỏ qua khi không có DB là màu xanh giả. Phần đó đã kiểm bằng tay trên PostgreSQL
/// thật; xem báo cáo của H-4.
///
/// Cái mà test này chốt lại: số thứ tự đi kèm mã ĐỢT, nên hai đợt khác nhau không bao giờ
/// dùng chung dãy số — chính là lý do không dùng một sequence toàn cục của PostgreSQL.
/// </summary>
public class RecordCodeTests
{
    [Theory]
    [InlineData(1, "KSK2608-0001")]
    [InlineData(2, "KSK2608-0002")]
    [InlineData(42, "KSK2608-0042")]
    [InlineData(999, "KSK2608-0999")]
    [InlineData(9999, "KSK2608-9999")]
    public void Chen_du_4_chu_so(int recordNo, string expected)
        => Assert.Equal(expected, ExamRecordService.ComposeRecordCode("KSK2608", recordNo));

    /// <summary>
    /// Vượt 9999 thì mã dài ra chứ KHÔNG quay vòng về 0000. Cắt cho vừa 4 chữ số sẽ sinh
    /// mã trùng với hồ sơ đầu đợt — đúng thứ mà cả bộ đếm lẫn chỉ mục UNIQUE đang chống.
    /// </summary>
    [Fact]
    public void Khong_quay_vong_khi_vuot_9999()
    {
        Assert.Equal("KSK2608-10000", ExamRecordService.ComposeRecordCode("KSK2608", 10000));
        Assert.NotEqual(
            ExamRecordService.ComposeRecordCode("KSK2608", 1),
            ExamRecordService.ComposeRecordCode("KSK2608", 10001));
    }

    /// <summary>
    /// Hai đợt cùng cấp số 1 vẫn ra hai mã khác nhau. Đây là điều kiện để bộ đếm nằm TRÊN
    /// từng đợt là đúng: đợt B mở sau vẫn được bắt đầu lại từ -0001.
    /// </summary>
    [Fact]
    public void Hai_dot_khong_dung_chung_day_so()
    {
        var a = new[] { 1, 2, 3 }.Select(n => ExamRecordService.ComposeRecordCode("KSK2608A", n)).ToList();
        var b = new[] { 1, 2, 3 }.Select(n => ExamRecordService.ComposeRecordCode("KSK2608B", n)).ToList();

        Assert.Equal(new[] { "KSK2608A-0001", "KSK2608A-0002", "KSK2608A-0003" }, a);
        Assert.Equal(new[] { "KSK2608B-0001", "KSK2608B-0002", "KSK2608B-0003" }, b);
        Assert.Empty(a.Intersect(b));
    }

    /// <summary>
    /// Dãy số cấp liên tiếp trong MỘT đợt phải tăng đều và không lặp — hợp đồng mà
    /// AllocateRecordNoAsync phải giữ (bộ đếm chỉ tăng, không tái sử dụng số của hồ sơ đã xoá).
    /// </summary>
    [Fact]
    public void Day_ma_trong_mot_dot_tang_deu_va_khong_lap()
    {
        var codes = Enumerable.Range(1, 250)
            .Select(n => ExamRecordService.ComposeRecordCode("KSK2608", n))
            .ToList();

        Assert.Equal(250, codes.Distinct().Count());
        Assert.Equal("KSK2608-0001", codes[0]);
        Assert.Equal("KSK2608-0250", codes[^1]);
        // So sánh chuỗi cũng phải ra đúng thứ tự cấp phát — nhờ chèn 0 đủ 4 chữ số. Không
        // chèn 0 thì "KSK2608-10" đứng trước "KSK2608-2" khi sắp xếp, mọi danh sách hồ sơ
        // của đợt (ListAsync sắp theo RecordCode) sẽ hiện lộn xộn.
        Assert.Equal(codes, codes.OrderBy(x => x, StringComparer.Ordinal).ToList());
    }
}
