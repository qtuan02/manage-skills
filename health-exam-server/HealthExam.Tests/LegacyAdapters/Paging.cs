namespace HealthExam.Server.Service;

/// <summary>Chuẩn hoá tham số phân trang — chặn size=0 và size khổng lồ làm sập truy vấn.</summary>
public static class Paging
{
    public const int DefaultSize = 20;
    public const int MaxSize = 200;

    public static (int Page, int Size) Normalize(int page, int size)
        => (page < 1 ? 1 : page, size < 1 ? DefaultSize : (size > MaxSize ? MaxSize : size));
}
