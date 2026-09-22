using Xunit;

namespace HealthExam.Tests;

/// <summary>
/// Chốt chặn cho những test mà TOÀN BỘ giá trị của chúng nằm ở chỗ giờ địa phương LỆCH UTC.
///
/// Vì sao cần: <c>ToUniversalTime()</c> trên <c>Kind=Unspecified</c> và
/// <c>ToLocalTime()</c> đều là NO-OP khi máy chạy ở UTC. Một test dựng đúng ba nhánh Kind
/// vẫn xanh với mã hỏng nếu <c>TZ=UTC</c> — hỏng im lặng, không một dòng cảnh báo, đúng lớp
/// lỗi mà chính những test đó sinh ra để chặn. Repo KHÔNG có .runsettings, không có
/// EnvironmentVariables trong .csproj, không có CI khai TZ ⇒ không có gì bảo đảm múi giờ khi
/// test chạy ở máy khác.
///
/// ⚠️ ĐỪNG đặt TZ bằng <c>Environment.SetEnvironmentVariable</c> trong test:
/// .NET nạp <c>TimeZoneInfo.Local</c> ĐÚNG MỘT LẦN rồi cache, đặt sau lần chạm đầu tiên là
/// không ăn — thành ra thêm một đường hỏng im lặng nữa thay vì gỡ nó.
///
/// Cách chạy đúng: <c>TZ=Asia/Ho_Chi_Minh dotnet test HealthExam.Tests/HealthExam.Tests.csproj</c>
/// </summary>
internal static class TzGuard
{
    /// <summary>
    /// Đỏ ồn ào nếu máy đang chạy ở UTC. Gọi ở DÒNG ĐẦU của test phụ thuộc múi giờ.
    /// </summary>
    /// <param name="moc">Thời điểm dùng để tra offset — offset phụ thuộc mùa ở một số múi.</param>
    internal static void RequireLocalOffsetKhacUtc(DateTime moc)
    {
        Assert.True(
            TimeZoneInfo.Local.GetUtcOffset(moc) != TimeSpan.Zero,
            $"Test này chỉ có nghĩa khi giờ địa phương LỆCH UTC, nhưng TimeZoneInfo.Local = "
          + $"'{TimeZoneInfo.Local.Id}' đang cho offset 0 tại {moc:O}. Dưới UTC thì "
          + "ToUniversalTime()/ToLocalTime() là no-op nên test xanh KỂ CẢ VỚI MÃ HỎNG. "
          + "Chạy lại với TZ=Asia/Ho_Chi_Minh.");
    }
}
