namespace HealthExam.Infrastructure.Integrations.Ris;

/// <summary>
/// Đổi mốc UTC sang GIỜ VIỆT NAM để đặt vào gói gửi vendor.
///
/// 🔴 Vì sao KHÔNG dùng <c>DateTime.Now</c> / <c>ToLocalTime()</c>: hợp đồng dây VietRad
/// định dạng <c>"yyyy-MM-dd HH:mm:ss"</c> — KHÔNG mang offset, không mang 'Z'. Bên nhận đọc
/// chuỗi đó là giờ Việt Nam. Nếu ta lấy giờ máy thì kết quả phụ thuộc TZ của POD: cùng một
/// bản build, chạy ở máy dev (TZ=Asia/Ho_Chi_Minh) ra một giờ, chạy trong container
/// (TZ mặc định UTC) ra giờ khác LỆCH 7 TIẾNG — và không có gì đỏ lên, chỉ là phiếu chụp
/// hiện sai giờ trên worklist.
///
/// Đây đúng họ hàng của lớp lỗi đã vấp thật: converter/ToTimeLocal cộng nhầm 7h ở các
/// *-connect-server, và mốc backfill lệch 7h vừa phải vá ở chính MR !10. Khác biệt duy nhất
/// giữ cho nó không tái diễn: múi giờ ở đây là HẰNG SỐ CỦA HỢP ĐỒNG, không phải môi trường.
/// </summary>
public static class VendorClock
{
    /// <summary>IANA (Linux/container) và Windows — cùng một múi, hai hệ tên.</summary>
    private const string IanaId = "Asia/Ho_Chi_Minh";
    private const string WindowsId = "SE Asia Standard Time";

    /// <summary>
    /// +07:00 cố định, dùng khi ảnh chạy KHÔNG có cơ sở dữ liệu múi giờ (ảnh
    /// runtime-deps/alpine gọn thường thiếu tzdata).
    ///
    /// Việt Nam không có giờ mùa hè từ 1975, nên với mọi mốc hệ thống này sinh ra thì offset
    /// cố định và bảng múi giờ cho CÙNG một kết quả. Rơi về hằng số vì thế KHÔNG phải "đoán
    /// cho qua" — nhưng vẫn phải là nhánh dự phòng chứ không phải nhánh chính, để mốc lịch sử
    /// (trước 1975) và mọi thay đổi luật tương lai vẫn đi theo bảng.
    /// </summary>
    private static readonly TimeSpan FallbackOffset = TimeSpan.FromHours(7);

    private static readonly TimeZoneInfo Zone = Resolve();

    private static TimeZoneInfo Resolve()
    {
        foreach (var id in new[] { IanaId, WindowsId })
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch (TimeZoneNotFoundException) { }
            catch (InvalidTimeZoneException) { }
        }
        return TimeZoneInfo.CreateCustomTimeZone("HEX-VN-Fixed", FallbackOffset, "Vietnam (fixed +07)", "ICT");
    }

    /// <summary>Định dạng mốc trên dây VietRad — KHÔNG offset, KHÔNG 'Z'.</summary>
    public const string WireFormat = "yyyy-MM-dd HH:mm:ss";

    /// <summary>
    /// ★ MỘT MỐC ĐÃ ĐỔI SANG GIỜ VIỆT NAM, và là kiểu DUY NHẤT mà gói gửi vendor nhận.
    ///
    /// 🔴 Vì sao phải là một KIỂU RIÊNG chứ không phải <c>DateTime</c>: nghiệm thu vòng 3 đo
    /// được rằng thay <c>VendorClock.ToVietnam(x)</c> bằng <c>x.ToLocalTime()</c> ở chỗ dựng
    /// gói thì TOÀN BỘ bộ test vẫn xanh — và đó không phải vì test viết ẩu. Dưới
    /// <c>TZ=Asia/Ho_Chi_Minh</c>, vốn là múi giờ DUY NHẤT bộ test được phép chạy
    /// (<c>TzGuard</c> ở bộ test), hai phép cho KẾT QUẢ Y HỆT NHAU với mọi
    /// <c>DateTimeKind</c> — kể cả Unspecified, thứ mà <c>ToLocalTime()</c> cũng coi là UTC.
    /// Không có test nào trong một tiến trình một múi giờ phân biệt nổi hai phép đó.
    ///
    /// Nên chốt chặn phải nằm ở TRÌNH BIÊN DỊCH, không ở bộ test: hàm dựng là <c>private</c>
    /// và chỉ <see cref="VendorClock"/> — kiểu bao ngoài — gọi được. Mọi lối viết
    /// <c>OrderDate = x.ToLocalTime()</c> hay <c>= DateTime.Now</c> nay KHÔNG BIÊN DỊCH ĐƯỢC.
    /// Sai lầm ở đây tốn 7 tiếng trên worklist phòng chụp mà không có gì đỏ lên, nên nó đáng
    /// một kiểu riêng.
    /// </summary>
    public readonly struct Stamp
    {
        private readonly DateTime _vietnam;
        private readonly bool _set;

        private Stamp(DateTime vietnam)
        {
            _vietnam = vietnam;
            _set = true;
        }

        /// <summary>
        /// Lối DUY NHẤT dựng được một mốc trên dây — và nó TỰ đổi múi giờ, không nhận một
        /// <c>DateTime</c> "ai đó đã đổi rồi".
        ///
        /// Đó là điểm mấu chốt: một hàm dựng nhận sẵn giờ Việt Nam thì vẫn viết được
        /// <c>Stamp.Of(x.ToLocalTime())</c>, tức khoá lại đúng chỗ vừa mở ra. Nhận mốc UTC và
        /// tự quy đổi thì KHÔNG CÒN chỗ nào cho giờ máy chen vào.
        ///
        /// (Kiểu lồng đọc được thành viên private của kiểu bao ngoài — nên <c>Zone</c> ở đây
        /// vẫn là bảng múi giờ đã giải quyết một lần của <see cref="VendorClock"/>. Chiều
        /// ngược lại thì KHÔNG: C# không cho kiểu bao ngoài gọi hàm dựng private của kiểu
        /// lồng, nên phép dựng phải nằm ở đây chứ không nằm ở ToVietnam.)
        /// </summary>
        internal static Stamp FromUtc(DateTime utc)
        {
            var asUtc = utc.Kind switch
            {
                DateTimeKind.Utc => utc,
                DateTimeKind.Local => utc.ToUniversalTime(),
                // Mọi mốc trong DB của service này là UTC (không có converter toàn cục), nên
                // Unspecified ở đây nghĩa là "UTC chưa gắn nhãn" chứ không phải giờ máy.
                _ => DateTime.SpecifyKind(utc, DateTimeKind.Utc)
            };

            return new Stamp(
                DateTime.SpecifyKind(TimeZoneInfo.ConvertTimeFromUtc(asUtc, Zone), DateTimeKind.Unspecified));
        }

        /// <summary>Giá trị giờ Việt Nam, <c>Kind=Unspecified</c>. Đọc được thoải mái — chốt
        /// chặn nằm ở chỗ DỰNG, không ở chỗ đọc.</summary>
        public DateTime Value => Require();

        public override string ToString()
            => Require().ToString(WireFormat, System.Globalization.CultureInfo.InvariantCulture);

        /// <summary>
        /// <c>default(Stamp)</c> là trường BỊ QUÊN, không phải mốc năm 0001. Ném ở đây để nó
        /// hiện ra thành lỗi lúc dựng gói, thay vì thành "0001-01-01 00:00:00" trên dây —
        /// vendor nhận chuỗi đó rồi trả 400, và triệu chứng phía ta chỉ là một lỗi 4xx không
        /// rõ nguyên nhân (đúng cái đã xảy ra với MsgDate ở bản khảo sát hợp đồng của P3).
        /// </summary>
        private DateTime Require()
            => _set ? _vietnam
                : throw new InvalidOperationException(
                    "Mốc gửi vendor chưa được đặt — dựng nó bằng VendorClock.ToVietnam(...).");
    }

    /// <summary>
    /// Mốc UTC → giờ Việt Nam, <c>Kind=Unspecified</c>.
    ///
    /// Unspecified là ĐÚNG cho chuỗi không mang offset: gán <c>Local</c> sẽ mời
    /// <c>ToUniversalTime()</c> ở tầng dưới trừ đi offset của MÁY một lần nữa, tức lệch hai
    /// lần. Gán <c>Utc</c> thì Newtonsoft có thể phụ thêm hậu tố 'Z' tuỳ cấu hình.
    /// </summary>
    public static Stamp ToVietnam(DateTime utc) => Stamp.FromUtc(utc);
}

/// <summary>
/// Ghi <see cref="VendorClock.Stamp"/> lên dây thành chuỗi <c>"yyyy-MM-dd HH:mm:ss"</c>.
///
/// Cần bộ chuyển riêng vì <c>DateFormatString</c> của Newtonsoft chỉ áp cho <c>DateTime</c>;
/// một struct không có converter sẽ ra một OBJECT (<c>{"value":…}</c>) và bên kia đọc rỗng
/// mà vẫn trả 200 — hỏng im lặng, đúng lớp lỗi đang chống.
/// </summary>
public class VendorStampJsonConverter : Newtonsoft.Json.JsonConverter<VendorClock.Stamp>
{
    public override void WriteJson(
        Newtonsoft.Json.JsonWriter writer, VendorClock.Stamp value, Newtonsoft.Json.JsonSerializer serializer)
        => writer.WriteValue(value.ToString());

    /// <summary>Gói chỉ đi MỘT CHIỀU. Đường về của vendor có đúng ba trường và không có mốc nào.</summary>
    public override VendorClock.Stamp ReadJson(
        Newtonsoft.Json.JsonReader reader, Type objectType, VendorClock.Stamp existingValue,
        bool hasExistingValue, Newtonsoft.Json.JsonSerializer serializer)
        => throw new NotSupportedException("Mốc vendor chỉ dựng được bằng VendorClock.ToVietnam.");
}
