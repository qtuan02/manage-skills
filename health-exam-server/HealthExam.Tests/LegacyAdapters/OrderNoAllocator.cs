using System.Data.Common;
using HealthExam.Application.Paraclinical;
using HealthExam.Infrastructure.Persistence;
using HealthExam.Infrastructure.Persistence.Legacy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace HealthExam.Server.Service;

/// <summary>
/// Cấp SỐ PHIẾU chỉ định. Tách thành cổng riêng vì bản thật đi thẳng xuống PostgreSQL
/// (<c>nextval</c>), thứ provider in-memory của bộ test không có — và một nhánh
/// "nếu đang test thì làm khác" nằm trong code chạy thật là nhánh không ai kiểm.
/// </summary>
public interface IOrderNoAllocator
{
    Task<string> NextAsync(CancellationToken ct = default);
}

/// <summary>
/// Bản thật: <c>nextval</c> trên dãy HEX_ParaclinicalOrderNo.
///
/// ★ Hình dạng mã: 2 KÝ TỰ ĐẦU rồi THUẦN SỐ — <c>CD0000000123</c>. Không phải trang trí:
/// đường về của RIS cắt <c>OrderID.Substring(2)</c> rồi mới parse số
/// (pacs-connect-server/M07F99020Commands.cs). Đặt mã tự do bây giờ thì P3b hoặc phải đổi mã
/// hàng loạt trên dữ liệu thật, hoặc phải dựng một bảng ánh xạ chỉ để nói chuyện với vendor.
///
/// Vì sao sequence chứ không COUNT(*)+1: hai người cùng bấm Lưu thì COUNT đọc cùng một con số
/// và cùng sinh một mã — đúng lớp lỗi mà bộ đếm mã hồ sơ đã phải bỏ cách đếm đó để tránh.
/// Sequence không lùi khi rollback nên dãy có lỗ; số phiếu là mã đối soát, không phải sổ liên
/// tục, nên lỗ rẻ hơn trùng.
/// </summary>
public class SequenceOrderNoAllocator : IOrderNoAllocator
{
    /// <summary>Tiền tố 2 ký tự theo nếp HIS — xem chú thích của lớp.</summary>
    public const string Prefix = "CD";

    /// <summary>Đệm 10 chữ số: đủ cho mọi vòng đời của một cơ sở, và độ dài cố định giúp mã
    /// sắp xếp theo chuỗi trùng với thứ tự cấp.</summary>
    public const int Digits = 10;

    private readonly IUnitOfWork _uow;

    public SequenceOrderNoAllocator(IUnitOfWork uow) => _uow = uow;

    public async Task<string> NextAsync(CancellationToken ct = default)
    {
        var db = _uow.Context.Database;

        // Gán CurrentTransaction để lệnh nằm cùng transaction với INSERT phiếu. (nextval
        // không rollback theo transaction — đó là hành vi của PostgreSQL, không phải thiếu
        // sót ở đây — nhưng lệnh vẫn phải chạy trên đúng connection đang mở transaction, nếu
        // không Npgsql sẽ mở connection thứ hai và treo chờ chính khoá mình đang giữ.)
        await db.OpenConnectionAsync(ct);
        try
        {
            await using var cmd = db.GetDbConnection().CreateCommand();
            cmd.CommandText = $"SELECT nextval('\"{HealthExamDbContext.ParaclinicalOrderNoSequence}\"')";
            cmd.Transaction = db.CurrentTransaction?.GetDbTransaction();

            var next = await cmd.ExecuteScalarAsync(ct);
            return Compose(Convert.ToInt64(next));
        }
        finally
        {
            await db.CloseConnectionAsync();
        }
    }

    /// <summary>Ghép mã từ số đã cấp. Hàm thuần để kiểm được quy ước đặt mã mà không cần DB.</summary>
    public static string Compose(long value) => Prefix + value.ToString(new string('0', Digits));
}


