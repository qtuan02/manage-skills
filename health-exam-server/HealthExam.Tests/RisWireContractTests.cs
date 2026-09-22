using System.Text.RegularExpressions;
using HealthExam.Domain.Common;
using HealthExam.Domain.ExamSessions;
using HealthExam.Domain.ExamRecords;
using HealthExam.Domain.Imports;
using HealthExam.Domain.Paraclinical;
using HealthExam.Domain.Webhooks;
using HealthExam.Domain.Integrations;
using HealthExam.Domain.Catalogs;
using HealthExam.Domain.Patients;
using HealthExam.Infrastructure.Integrations.Ris;
using HealthExam.API.Contracts;
using HealthExam.API.Middlewares;
using HealthExam.Application.Common;
using HealthExam.Server.Service;
using Newtonsoft.Json.Linq;
using Xunit;

namespace HealthExam.Tests;

/// <summary>
/// G-P3b-1, nửa ĐI RA: hợp đồng dây VietRad được chốt bằng test đơn vị trên hàm thuần.
///
/// Vì sao kiểm được ở tầng này mà pacs-connect-server thì không: nó dựng gói bằng một truy
/// vấn LINQ vào DB của HIS, nên muốn biết gói có đúng hình không thì phải có cả một DB HIS.
/// Tách hàm dựng gói ra khỏi đường ống là lý do duy nhất để những chốt dưới đây tồn tại.
/// </summary>
public class RisWireContractTests
{
    private static (ParaclinicalOrder Order, List<ParaclinicalOrderItem> Lines, ExamRecord Record) Fixture(
        short genderId = 2, string identity = "079201004321", string patientCode = "BN000123",
        string groupCode = "CDHA_XQUANG")
    {
        var patient = new Patient
        {
            PatientRefID = Guid.NewGuid(),
            DivisionID = "DEV",
            PatientCode = patientCode,
            FullName = "Trần Thị B",
            GenderID = genderId,
            IdentityNumber = identity,
            PhoneNumber = "0909000111",
            Address = "12 Nguyễn Huệ",
            Dob = new DateOnly(1988, 4, 2)
        };

        var insurance = new PatientInsurance
        {
            InsuranceRefID = Guid.NewGuid(),
            DivisionID = "DEV",
            PatientRefID = patient.PatientRefID,
            InsuranceNumber = "HS4010112345678"
        };

        var record = new ExamRecord
        {
            RecordID = Guid.NewGuid(),
            DivisionID = "DEV",
            RecordCode = "DK-2026-001-0007",
            PatientRefID = patient.PatientRefID,
            InsuranceRefID = insurance.InsuranceRefID,
            Patient = patient,
            Insurance = insurance
        };

        var order = new ParaclinicalOrder
        {
            OrderID = Guid.NewGuid(),
            DivisionID = "DEV",
            RecordID = record.RecordID,
            OrderNo = "CD0000000042",
            ParaclinicalKind = "CDHA",
            OrderedByName = "BS. Hoà",
            RoomID = 12,
            // 2026-08-20T02:30:00Z = 09:30 giờ Việt Nam.
            OrderedAt = new DateTime(2026, 8, 20, 2, 30, 0, DateTimeKind.Utc)
        };

        var lines = new List<ParaclinicalOrderItem>
        {
            new()
            {
                OrderItemID = Guid.NewGuid(), OrderID = order.OrderID, DivisionID = "DEV",
                ServiceID = 200001, ServiceCode = "CDHA_XQNT", ServiceName = "X-quang ngực thẳng",
                ServiceGroupCode = groupCode, VendorLineNo = 7
            }
        };

        return (order, lines, record);
    }

    // ───────────────────────────── Hình dạng gói ──────────────────────────────────────────

    /// <summary>
    /// Gate: gói NEW mang đủ 6 trường [Required] của PACSRequest, camelCase, và MsgDate —
    /// trường mà bản khảo sát hợp đồng của P3 BỎ SÓT (G-P3b-1 nêu đích danh).
    /// </summary>
    [Fact]
    public void Goi_NEW_du_sau_truong_bat_buoc_va_dung_camelCase()
    {
        var (order, lines, record) = Fixture();

        var payload = RisOrderPayloadBuilder.Build(
            order, lines, record, new RisOptions(), RisOrderStatuses.New,
            new DateTime(2026, 8, 20, 3, 0, 0, DateTimeKind.Utc), out var missing);

        Assert.Empty(missing);

        var json = JObject.Parse(RisClient.Serialize(payload));

        // camelCase: khoá "patient", KHÔNG phải "Patient". Sai vế này là bên kia đọc ra rỗng
        // mà vẫn trả 200 — hỏng im lặng đúng lớp đang chống.
        Assert.NotNull(json["patient"]);
        Assert.Null(json["Patient"]);

        Assert.Equal("CD0000000042", json["orderNumber"]!.Value<string>());
        Assert.Equal("NEW", json["status"]!.Value<string>());
        Assert.NotNull(json["msgDate"]);
        Assert.NotNull(json["orderDate"]);
        Assert.Equal("BN000123", json["patient"]!["id"]!.Value<string>());
        Assert.Equal("F", json["patient"]!["sex"]!.Value<string>());
        Assert.Equal("079201004321", json["patient"]!["identityCardID"]!.Value<string>());

        var line = json["orders"]![0]!;
        Assert.Equal("CL0000000007", line["id"]!.Value<string>());
        Assert.Equal("CDHA_XQNT", line["code"]!.Value<string>());
        Assert.Equal("X-quang ngực thẳng", line["name"]!.Value<string>());
        Assert.Equal("CDHA_XQUANG", line["group"]!.Value<string>());
        Assert.False(string.IsNullOrEmpty(line["pcReqDltVoucherNo"]!.Value<string>()));
    }

    /// <summary>
    /// ★ Mốc thời gian trên dây là GIỜ VIỆT NAM, và KHÔNG phụ thuộc TZ của máy chạy.
    ///
    /// Định dạng "yyyy-MM-dd HH:mm:ss" không mang offset nên bên nhận đọc nó là giờ địa
    /// phương của họ. Lấy giờ máy thì cùng một bản build cho hai kết quả lệch 7 tiếng giữa
    /// máy dev và container — không có gì đỏ lên, chỉ là phiếu chụp hiện sai giờ.
    ///
    /// Test này KHÔNG cần TzGuard: nó chốt rằng kết quả BẤT BIẾN theo TZ, nên nó phải cắn
    /// dưới mọi múi giờ. Đó cũng chính là điều đang được chốt.
    /// </summary>
    [Fact]
    public void Moc_tren_day_luon_la_gio_Viet_Nam_du_may_chay_o_mui_nao()
    {
        var (order, lines, record) = Fixture();

        var payload = RisOrderPayloadBuilder.Build(
            order, lines, record, new RisOptions(), RisOrderStatuses.New,
            new DateTime(2026, 8, 20, 3, 0, 0, DateTimeKind.Utc), out _);

        var json = JObject.Parse(RisClient.Serialize(payload));

        // 02:30Z ⇒ 09:30 (+7) · 03:00Z ⇒ 10:00 (+7)
        Assert.Equal("2026-08-20 09:30:00", json["orderDate"]!.Value<string>());
        Assert.Equal("2026-08-20 10:00:00", json["msgDate"]!.Value<string>());

        // Và không có 'Z' hay offset nào lọt vào — thêm hậu tố là đổi hình dạng chuỗi bên kia parse.
        Assert.Matches(new Regex(@"^\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}$"), json["msgDate"]!.Value<string>());
    }

    /// <summary>Trường rỗng bị BỎ HẲN khỏi gói, không gửi "" — một số hệ coi chuỗi rỗng là
    /// "xoá giá trị đang có".</summary>
    [Fact]
    public void Truong_rong_thi_bo_khoi_goi_chu_khong_gui_chuoi_rong()
    {
        var (order, lines, record) = Fixture();
        record.Insurance!.InsuranceNumber = "";
        record.Patient!.PhoneNumber = "";
        order.Note = "";

        var json = JObject.Parse(RisClient.Serialize(
            RisOrderPayloadBuilder.Build(order, lines, record, new RisOptions(), RisOrderStatuses.New,
                DateTime.UtcNow, out _)));

        Assert.Null(json["patient"]!["healthInsurance"]);
        Assert.Null(json["patient"]!["phone"]);
        Assert.Null(json["comment"]);
    }

    // ───────────────────────────── Thiếu thì DỪNG ─────────────────────────────────────────

    /// <summary>
    /// Gate: thiếu trường [Required] của vendor ⇒ KHÔNG dựng gói, và nói rõ thiếu gì.
    /// Gửi gói thiếu rồi để vendor từ chối thì phiếu đã mang dấu "đã gửi" trong khi worklist
    /// phòng chụp trống.
    /// </summary>
    [Fact]
    public void Thieu_CCCD_thi_khong_dung_goi_va_neu_dich_danh_truong_thieu()
    {
        var (order, lines, record) = Fixture(identity: "");

        var payload = RisOrderPayloadBuilder.Build(
            order, lines, record, new RisOptions(), RisOrderStatuses.New, DateTime.UtcNow, out var missing);

        Assert.Null(payload);
        Assert.Contains(missing, x => x.Field == "Patient.IdentityCardID");
    }

    /// <summary>
    /// ★ Giới tính "Khác" (3) KHÔNG được đoán thành M hay O.
    ///
    /// Vendor khai đúng một ký tự và chỉ chú thích F/M. Đoán bừa nghĩa là worklist phòng chụp
    /// mang giới tính SAI của một người thật — thứ ảnh hưởng tới cả quy trình chụp lẫn cách
    /// đọc kết quả. Đây là chỗ phải để người quyết định.
    /// </summary>
    [Theory]
    [InlineData((short)0)]
    [InlineData((short)3)]
    [InlineData((short)9)]
    public void Gioi_tinh_khong_bieu_dat_duoc_thi_dung_chu_khong_doan(short genderId)
    {
        var (order, lines, record) = Fixture(genderId: genderId);

        var payload = RisOrderPayloadBuilder.Build(
            order, lines, record, new RisOptions(), RisOrderStatuses.New, DateTime.UtcNow, out var missing);

        Assert.Null(payload);
        Assert.Contains(missing, x => x.Field == "Patient.Sex");
        Assert.Null(RisOrderPayloadBuilder.SexOf(genderId));
    }

    [Fact]
    public void Nam_ra_M_va_Nu_ra_F()
    {
        Assert.Equal("M", RisOrderPayloadBuilder.SexOf(1));
        Assert.Equal("F", RisOrderPayloadBuilder.SexOf(2));
    }

    /// <summary>Chưa có bảng ánh xạ nhóm KSK → nhóm vendor thì mã nhóm rỗng phải CHẶN, không
    /// gửi gói thiếu nhóm rồi chờ vendor từ chối.</summary>
    [Fact]
    public void Thieu_ma_nhom_dich_vu_thi_chan_tai_cho()
    {
        var (order, lines, record) = Fixture(groupCode: "");

        Assert.Null(RisOrderPayloadBuilder.Build(
            order, lines, record, new RisOptions(), RisOrderStatuses.New, DateTime.UtcNow, out var missing));
        Assert.Contains(missing, x => x.Field.EndsWith(".Group"));
    }

    /// <summary>Dòng chưa được cấp số hiệu vendor thì KHÔNG có gì để gửi — và cũng không có
    /// đường về, vì gói gọi về chỉ mang đúng số hiệu đó.</summary>
    [Fact]
    public void Dong_chua_cap_so_hieu_vendor_thi_chan()
    {
        var (order, lines, record) = Fixture();
        lines[0].VendorLineNo = null;

        Assert.Null(RisOrderPayloadBuilder.Build(
            order, lines, record, new RisOptions(), RisOrderStatuses.New, DateTime.UtcNow, out var missing));
        Assert.Contains(missing, x => x.Field.EndsWith(".Id"));
    }

    // ───────────────────────────── Vòng ID đi–về ──────────────────────────────────────────

    /// <summary>
    /// ★ Chốt VÒNG, không chốt từng chiều: ghép rồi parse phải trả lại đúng con số đã gửi.
    /// Kiểm riêng từng chiều thì hai quy ước lệch nhau vẫn xanh cả hai bên.
    /// </summary>
    [Theory]
    [InlineData(1L)]
    [InlineData(7L)]
    [InlineData(999999L)]
    [InlineData(9999999999L)]
    public void Vong_so_hieu_dong_di_ve_khop_nhau(long lineNo)
    {
        var wire = RisOrderPayloadBuilder.ComposeLineId(lineNo);

        Assert.StartsWith(RisOrderPayloadBuilder.LineIdPrefix, wire);
        Assert.True(wire.Length <= RisOrderPayloadBuilder.MaxLineIdLength);
        Assert.Equal(lineNo, RisOrderPayloadBuilder.ParseLineNo(wire));
    }

    /// <summary>
    /// 🔴 HAI KHÔNG GIAN SỐ PHẢI TÁCH HẲN NHAU.
    ///
    /// Số hiệu DÒNG và số PHIẾU đến từ hai dãy khác nhau (HEX_ParaclinicalVendorLineNo và
    /// HEX_ParaclinicalOrderNo), nên chúng đếm ĐỘC LẬP: dòng số 7 và phiếu số 7 cùng tồn tại
    /// là chuyện bình thường. Nếu cả hai lên dây cùng hình dạng "CD"+10 chữ số thì đường về
    /// — vốn chỉ có ĐÚNG MỘT trường OrderID để tra — không phân biệt được, và một số hiệu đến
    /// từ nhầm không gian vẫn tra ra một dòng CÓ THẬT của người bệnh khác rồi áp trạng thái
    /// lên đó, im lặng.
    ///
    /// Tiền đề không phải giả tưởng: RIS chỉ cấu hình được MỘT URL gọi về cho mỗi tenant của
    /// họ, và KSK với HIS đang dùng chung tenant 84006 (chưa giải quyết — T-6 của 236).
    ///
    /// Test này là thứ ĐỎ LÊN nếu ai đó đưa ComposeLineId về dùng chung tiền tố số phiếu.
    /// </summary>
    [Fact]
    public void So_hieu_dong_KHONG_dung_chung_khong_gian_voi_so_phieu()
    {
        Assert.NotEqual(SequenceOrderNoAllocator.Prefix, RisOrderPayloadBuilder.LineIdPrefix);

        // Cùng con số, hai không gian — chuỗi trên dây phải KHÁC nhau.
        Assert.NotEqual(SequenceOrderNoAllocator.Compose(7L), RisOrderPayloadBuilder.ComposeLineId(7L));

        // Và số PHIẾU đi vào đường về của DÒNG thì bị từ chối, chứ không cắt bừa 2 ký tự rồi
        // ra 7. Đây là vế đắt nhất: thiếu nó thì tách tiền tố chỉ là trang trí.
        Assert.Null(RisOrderPayloadBuilder.ParseLineNo(SequenceOrderNoAllocator.Compose(7L)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("CL")]
    [InlineData("CLxxxx")]
    [InlineData("CL0000000000")]
    // Sai KHÔNG GIAN, không phải sai hình dạng: "CD"+10 chữ số là số PHIẾU hợp lệ của một hệ
    // khác. Cắt mù 2 ký tự thì nó ra 7 và tra trúng dòng của người bệnh khác.
    [InlineData("CD0000000007")]
    [InlineData("XX0000000007")]
    public void Chuoi_khong_dung_quy_uoc_thi_tra_null_chu_khong_ra_so_bua(string wire)
        => Assert.Null(RisOrderPayloadBuilder.ParseLineNo(wire));
}
