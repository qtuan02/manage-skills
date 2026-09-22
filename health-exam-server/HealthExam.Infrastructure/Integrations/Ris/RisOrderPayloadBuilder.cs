using HealthExam.Domain.Common;
using HealthExam.Domain.ExamSessions;
using HealthExam.Domain.ExamRecords;
using HealthExam.Domain.Imports;
using HealthExam.Domain.Paraclinical;
using HealthExam.Domain.Webhooks;
using HealthExam.Domain.Integrations;
using HealthExam.Domain.Catalogs;
using HealthExam.Infrastructure.Persistence;

namespace HealthExam.Infrastructure.Integrations.Ris;

/// <summary>Trường bắt buộc còn thiếu — một dòng trong <c>Data</c> của lỗi 4095.</summary>
public record VendorMissingField(string Field, string Reason);

/// <summary>
/// Dựng gói <see cref="RisOrderRequest"/> từ phiếu chỉ định + hồ sơ. HÀM THUẦN: không đụng
/// DB, không gọi mạng — để kiểm được toàn bộ hợp đồng dây bằng test đơn vị, thứ mà đường
/// dựng-gói-bằng-truy-vấn-DB của pacs-connect-server không làm được.
///
/// 🔴 NGUYÊN TẮC: THIẾU THÌ DỪNG, KHÔNG ĐOÁN.
/// Mọi trường vendor khai <c>[Required]</c> mà bên ta không có dữ liệu thì trả về danh sách
/// thiếu (⇒ 4095), chứ không điền chuỗi rỗng/giá trị mặc định. Gửi gói thiếu trường có hai
/// kết cục, cả hai đều tệ hơn một lỗi hiện ra ngay: hoặc vendor từ chối và phiếu vẫn mang
/// dấu "đã gửi", hoặc vendor nhận nhưng worklist hiện sai — phát hiện lúc người bệnh đã
/// đứng ở cửa phòng chụp.
/// </summary>
public static class RisOrderPayloadBuilder
{
    /// <summary>Trần độ dài của <c>Order.Id</c> bên vendor — <c>[StringLength(22)]</c>.</summary>
    public const int MaxLineIdLength = 22;

    /// <summary>Trần độ dài của <c>OrderNumber</c> bên vendor — <c>[StringLength(20)]</c>.</summary>
    public const int MaxOrderNumberLength = 20;

    /// <summary>Trần độ dài của <c>Patient.Name</c> bên vendor — <c>[StringLength(60)]</c>
    /// (pacs-connect-server/PACSConnect.Core/Models/PACSRequest.cs).</summary>
    public const int MaxPatientNameLength = 60;

    /// <summary>
    /// ★ Tiền tố 2 ký tự của KHÔNG GIAN SỐ HIỆU DÒNG — cố ý KHÁC tiền tố số PHIẾU
    /// (<see cref="SequenceOrderNoAllocator.Prefix"/> = "CD").
    ///
    /// Vì sao không dùng chung "CD": hai dãy khác nhau (HEX_ParaclinicalVendorLineNo và
    /// HEX_ParaclinicalOrderNo) mà cùng hình dạng thì hai không gian số TRÙNG NHAU về mặt
    /// chuỗi. Đường về chỉ có đúng một trường <c>OrderID</c> để tra, nên một số hiệu đến
    /// từ nhầm không gian vẫn tra ra MỘT DÒNG CÓ THẬT — của người bệnh khác — rồi áp trạng
    /// thái lên đó, im lặng. Tiền đề "vendor gửi nhầm không gian" không phải giả tưởng: RIS
    /// chỉ cấu hình được MỘT URL gọi về cho mỗi tenant của họ, và KSK với HIS đang dùng
    /// chung tenant 84006 (xem <c>ParaclinicalOrderItem.VendorLineNo</c>).
    ///
    /// Hợp đồng dây chỉ đòi "2 ký tự rồi thuần số" (<c>Orders[].Id</c>, StringLength(22)) —
    /// không đòi đúng "CD". Một hằng số ở đây tách hẳn hai không gian.
    /// </summary>
    public const string LineIdPrefix = "CL";

    /// <summary>
    /// Ghép số hiệu dòng gửi đi: <see cref="LineIdPrefix"/> + số hiệu dòng thuần số.
    ///
    /// Dùng CHUNG một hằng số với đường đọc gói gọi về (<see cref="ParseLineNo"/>) để hai
    /// chiều không thể lệch quy ước — lệch ở đây là gói gọi về không tra ra dòng nào và cầu
    /// im lặng không áp trạng thái.
    /// </summary>
    public static string ComposeLineId(long vendorLineNo)
        => LineIdPrefix + vendorLineNo.ToString(new string('0', SequenceOrderNoAllocator.Digits));

    /// <summary>
    /// Đọc ngược số hiệu dòng từ chuỗi vendor gửi về: bỏ đúng <see cref="LineIdPrefix"/> rồi
    /// parse số. Giữ phép cắt-2-ký-tự của pacs-connect-server
    /// (<c>M07F99020Commands.cs</c>), nhưng CƯỠNG CHẾ tiền tố thay vì cắt mù.
    ///
    /// 🔴 Cắt mù là chỗ hai không gian số hoà vào nhau: <c>"CD0000000123"</c> (số PHIẾU của
    /// một hệ khác) cắt 2 ký tự cũng ra <c>123</c>, tra ra dòng <c>VendorLineNo = 123</c>
    /// của một hồ sơ hoàn toàn khác. Từ chối ở đây ⇒ 4001, tức gói lạ hiện ra thành lỗi thay
    /// vì thành một lượt ghi sai im lặng.
    /// </summary>
    /// <returns>null nếu sai tiền tố hoặc không parse được số (gói sai hình dạng).</returns>
    public static long? ParseLineNo(string vendorOrderId)
    {
        if (string.IsNullOrWhiteSpace(vendorOrderId)) return null;

        var raw = vendorOrderId.Trim();

        // Hoa/thường bỏ qua: vendor dội lại đúng chuỗi ta gửi, nên khác hoa-thường chỉ có thể
        // đến từ một tầng trung gian chuẩn hoá — không phải từ một không gian số khác.
        if (!raw.StartsWith(LineIdPrefix, StringComparison.OrdinalIgnoreCase)) return null;

        var digits = raw[LineIdPrefix.Length..];

        // 0 bị coi là KHÔNG hợp lệ, giống bản gốc: dãy cấp số bắt đầu từ 1 nên 0 chỉ có thể
        // đến từ một chuỗi rác vừa parse ra 0 (VD "CL" + "" ⇒ TryParse thất bại) — hoặc từ
        // một hệ khác đánh số kiểu khác.
        return long.TryParse(digits, out var value) && value > 0 ? value : null;
    }

    /// <summary>
    /// Dựng gói. <paramref name="lines"/> phải là các dòng ĐÃ ĐƯỢC CẤP
    /// <see cref="ParaclinicalOrderItem.VendorLineNo"/>.
    /// </summary>
    /// <param name="nowUtc">Mốc phát gói (UTC) — truyền vào để test dựng được thời điểm.</param>
    /// <param name="missing">Danh sách trường thiếu; rỗng nghĩa là gói dựng được.</param>
    public static RisOrderRequest Build(
        ParaclinicalOrder order,
        IReadOnlyList<ParaclinicalOrderItem> lines,
        ExamRecord record,
        RisOptions options,
        string status,
        DateTime nowUtc,
        out IReadOnlyList<VendorMissingField> missing)
    {
        var gaps = new List<VendorMissingField>();

        if (lines.Count == 0)
            gaps.Add(new VendorMissingField("Orders", "Phiếu không còn dòng dịch vụ nào để gửi"));

        var genderId = record.Patient?.GenderID ?? 0;
        var sex = SexOf(genderId);
        if (sex == null)
            gaps.Add(new VendorMissingField("Patient.Sex",
                $"Giới tính (GenderID={genderId}) không biểu đạt được trên dây RIS — vendor chỉ nhận 'M' hoặc 'F'"));

        if (string.IsNullOrWhiteSpace(record.Patient?.IdentityNumber))
            gaps.Add(new VendorMissingField("Patient.IdentityCardID", "Hồ sơ chưa có số CCCD/CMND"));

        if (string.IsNullOrWhiteSpace(record.Patient?.FullName))
            gaps.Add(new VendorMissingField("Patient.Name", "Hồ sơ chưa có họ tên"));
        else if (record.Patient.FullName.Length > MaxPatientNameLength)
            // Trần này lấy từ ĐÚNG tệp hợp đồng với hai trần kia. Không kiểm ở đây thì hồ sơ
            // nạp từ Excel có họ tên dài hơn sẽ đi tới RIS, bị trả 400, rồi đốt sạch ngân
            // sách thử và rơi vào DeadLetter — một lỗi dữ liệu sửa được trong 5 giây biến
            // thành một phiếu không bao giờ tới phòng chụp.
            gaps.Add(new VendorMissingField("Patient.Name",
                $"Họ tên dài {record.Patient.FullName.Length} ký tự, vendor chỉ nhận tối đa {MaxPatientNameLength}"));

        if (string.IsNullOrWhiteSpace(record.Patient?.PatientCode))
            gaps.Add(new VendorMissingField("Patient.ID", "Hồ sơ chưa có mã người bệnh"));

        if (order.OrderNo.Length > MaxOrderNumberLength)
            gaps.Add(new VendorMissingField("OrderNumber",
                $"Số phiếu dài {order.OrderNo.Length} ký tự, vendor chỉ nhận tối đa {MaxOrderNumberLength}"));

        foreach (var line in lines)
        {
            var label = string.IsNullOrWhiteSpace(line.ServiceCode) ? line.OrderItemID.ToString() : line.ServiceCode;

            if (!line.VendorLineNo.HasValue)
                gaps.Add(new VendorMissingField($"Orders[{label}].Id", "Dòng dịch vụ chưa được cấp số hiệu vendor"));
            else if (ComposeLineId(line.VendorLineNo.Value).Length > MaxLineIdLength)
                gaps.Add(new VendorMissingField($"Orders[{label}].Id",
                    $"Số hiệu dòng dài quá {MaxLineIdLength} ký tự"));

            if (string.IsNullOrWhiteSpace(line.ServiceCode))
                gaps.Add(new VendorMissingField($"Orders[{label}].Code", "Dòng dịch vụ chưa có mã dịch vụ"));

            if (string.IsNullOrWhiteSpace(line.ServiceName))
                gaps.Add(new VendorMissingField($"Orders[{label}].Name", "Dòng dịch vụ chưa có tên dịch vụ"));

            // ⚠️ CHƯA CHỐT — nhóm dịch vụ theo cách gọi của VENDOR. HIS lấy từ cột ánh xạ
            // MappingConnects của danh mục (M07F99020Queries: Group = s.MappingConnects), tức
            // ở đó có một bảng ánh xạ mã-của-mình → mã-của-vendor. KSK CHƯA có bảng đó, nên
            // tạm gửi mã nhóm của chính mình; vendor nào không nhận ra sẽ từ chối gói, và đó
            // là lý do dòng này phải nằm trong danh sách "chưa chốt" của handoff chứ không
            // im lặng đi qua. Rỗng thì chặn ngay tại đây.
            if (string.IsNullOrWhiteSpace(line.ServiceGroupCode))
                gaps.Add(new VendorMissingField($"Orders[{label}].Group",
                    "Dòng dịch vụ chưa có mã nhóm — vendor khai [Required] và chưa có bảng ánh xạ nhóm KSK → nhóm vendor"));
        }

        missing = gaps;
        if (gaps.Count > 0) return null;

        return new RisOrderRequest
        {
            OrderNumber = order.OrderNo,
            // Mốc CHỈ ĐỊNH (lúc bác sĩ ra y lệnh) chứ không phải lúc gửi: hai thứ lệch nhau
            // đúng bằng thời gian gói nằm trong hàng đợi, và cái phòng chụp cần đọc là mốc
            // y lệnh. MsgDate mới là mốc phát gói.
            OrderDate = VendorClock.ToVietnam(order.OrderedAt),
            MsgDate = VendorClock.ToVietnam(nowUtc),
            Status = status,
            EncounterID = Blank(record.RecordCode),
            VoucherType = options.VoucherType,
            VoucherNo = order.OrderNo,
            StationAE = Blank(options.StationAe),
            Comment = Blank(order.Note),
            Clinical = new RisClinical
            {
                // Bác sĩ RA Y LỆNH, không phải người bấm nút gửi: gói đi từ vòng lặp nền nên
                // "người bấm" ở đó là chính hệ thống.
                Doctor = Blank(order.OrderedByName),
                Room = order.RoomID > 0 ? order.RoomID.ToString() : null
            },
            Patient = new RisPatient
            {
                ID = record.Patient?.PatientCode ?? "",
                Name = record.Patient?.FullName ?? "",
                Sex = sex,
                IdentityCardID = record.Patient?.IdentityNumber ?? "",
                BirthYear = BirthYearOf(record),
                Phone = Blank(record.Patient?.PhoneNumber),
                Address = Blank(record.Patient?.Address),
                HealthInsurance = string.IsNullOrWhiteSpace(record.Insurance?.InsuranceNumber)
                    ? null
                    : new RisHealthInsurance { HealthInsuranceCardID = record.Insurance.InsuranceNumber }
            },
            Orders = lines.Select(line => new RisOrderLine
            {
                Id = ComposeLineId(line.VendorLineNo.Value),
                Code = line.ServiceCode,
                Name = line.ServiceName,
                Group = line.ServiceGroupCode,
                PCReqDltVoucherNo = order.OrderNo
            }).ToList()
        };
    }

    /// <summary>
    /// 1 Nam → "M" · 2 Nữ → "F" (nếp danh mục KSK, xem ExamImportColumns).
    ///
    /// 3 "Khác" và mọi giá trị lạ trả null ⇒ DỪNG. Không đoán "M", không gửi "O": vendor khai
    /// đúng một ký tự và chỉ chú thích F/M, nên "O" là chữ họ không hứa sẽ hiểu — còn đoán
    /// bừa thì worklist phòng chụp mang giới tính SAI của một người thật, thứ ảnh hưởng tới
    /// cả quy trình chụp lẫn cách đọc kết quả. Đây là chỗ phải để người quyết định, không
    /// phải chỗ để phần mềm chọn hộ.
    /// </summary>
    public static string SexOf(short genderId) => genderId switch
    {
        1 => "M",
        2 => "F",
        _ => null
    };

    private static string BirthYearOf(ExamRecord record)
    {
        if (record.Patient?.Dob.HasValue == true) return record.Patient.Dob.Value.Year.ToString();
        return record.Patient?.BirthYear.HasValue == true && record.Patient.BirthYear.Value > 0
            ? record.Patient.BirthYear.Value.ToString()
            : null;
    }

    /// <summary>Rỗng → null để <c>NullValueHandling.Ignore</c> bỏ hẳn khoá khỏi gói. Gửi
    /// <c>""</c> và không gửi gì là hai chuyện khác nhau với bên nhận: một số hệ coi chuỗi
    /// rỗng là "xoá giá trị đang có".</summary>
    private static string Blank(string value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
