using System;
using System.Collections.Generic;

namespace HealthExam.API.Contracts;

/// <summary>Kết quả phân trang dùng chung cho các API danh sách catalog.</summary>
public class PaginationData<T>
{
    public IReadOnlyList<T> Items { get; set; } = Array.Empty<T>();
    public int Page { get; set; }
    public int Size { get; set; }
    public long Total { get; set; }

    public PaginationData() { }

    public PaginationData(IReadOnlyList<T> items, int page, int size, long total)
    {
        Items = items;
        Page = page;
        Size = size;
        Total = total;
    }
}
