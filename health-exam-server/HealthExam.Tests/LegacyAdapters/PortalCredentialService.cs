using HealthExam.Domain.Common;
using HealthExam.Domain.ExamSessions;
using HealthExam.Domain.ExamRecords;
using HealthExam.Domain.Imports;
using HealthExam.Domain.Paraclinical;
using HealthExam.Domain.Webhooks;
using HealthExam.Domain.Integrations;
using HealthExam.Domain.Catalogs;
using HealthExam.Infrastructure.Persistence.Legacy;
using HealthExam.API.Contracts;
using HealthExam.API.Middlewares;
using IHealthExamContext = HealthExam.Application.Common.IHealthExamContext;
using ValidationErrors = HealthExam.Application.Common.ValidationErrors;
using ValidationError = HealthExam.Application.Common.ValidationError;
using Microsoft.EntityFrameworkCore;

namespace HealthExam.Server.Service;

/// <summary>
/// Đối chiếu dữ kiện đăng nhập cổng người bệnh (UC04) cho iam-server — H-3b.
///
/// Service này CỐ Ý không đúc token, không ghi audit trạng thái, không sửa gì. Nó chỉ trả lời
/// đúng một câu: "ba dữ kiện này có khớp một hồ sơ khám nào trong tenant không, và nếu có thì
/// là hồ sơ nào". Mọi chính sách phiên (hạn token, khoá tài khoản sau N lần sai, nhật ký đăng
/// nhập) nằm ở iam-server — gom cả hai vào đây là đúng thứ đã tách ra hồi 24/08.
/// </summary>
public class PortalCredentialService
{
    private readonly IUnitOfWork _uow;
    private readonly IHealthExamContext _ctx;

    public PortalCredentialService(IUnitOfWork uow, IHealthExamContext ctx)
    {
        _uow = uow;
        _ctx = ctx;
    }

    public async Task<VerifyPortalCredentialsResult> VerifyAsync(
        VerifyPortalCredentialsRequest req, CancellationToken ct = default)
    {
        req ??= new VerifyPortalCredentialsRequest();

        var patientCode = Normalize(req.PatientCode);
        var identityNumber = Normalize(req.IdentityNumber);
        var insuranceNumber = Normalize(req.InsuranceNumber);

        // 4001 chứ không phải IsValid=false: đây là lỗi HÌNH DẠNG của lời gọi, không phải kết
        // quả đối chiếu. Người gọi là iam-server chứ không phải người bệnh, nên nói thẳng
        // "thiếu trường" không lộ gì — mà giấu đi thì lỗi tích hợp trông y hệt "sai mật khẩu".
        var errors = new ValidationErrors();
        if (patientCode.Length == 0)
            errors.Errors.Add(new ValidationError { Field = nameof(req.PatientCode), Reason = "Bỏ trống (bắt buộc)" });
        if (identityNumber.Length == 0 && insuranceNumber.Length == 0)
            errors.Errors.Add(new ValidationError
            {
                Field = nameof(req.IdentityNumber),
                Reason = "Bỏ trống (bắt buộc ít nhất một trong IdentityNumber / InsuranceNumber)"
            });
        if (errors.Errors.Count > 0) throw HealthExamException.BadRequest(payload: errors);

        var candidates = await _uow.ExamRecords.Query()
            .Where(x => x.DivisionID == _ctx.DivisionId
                     && x.Patient.PatientCode == patientCode
                     // Hồ sơ đã huỷ không còn là đường vào cổng: phiếu của nó không dùng nữa,
                     // mà mã hồ sơ thì vẫn nằm trên giấy hẹn đã phát ra.
                     && x.State != ExamRecordState.RegistrationCancelled
                     && x.State != ExamRecordState.ExamCancelled
                     && x.Session.State != ExamSessionState.Cancelled)
            .Select(x => new Candidate
            {
                RecordCode = x.RecordCode,
                FullName = x.Patient.FullName,
                IdentityNumber = x.Patient.IdentityNumber,
                InsuranceNumber = x.Insurance != null ? x.Insurance.InsuranceNumber : "",
                SessionCode = x.Session.SessionCode,
                ExamDate = x.Session.ExamDate,
                CreatedDate = x.CreatedDate
            })
            .ToListAsync(ct);

        var matched = candidates
            .Where(c => CredentialsMatch(c.IdentityNumber, c.InsuranceNumber, identityNumber, insuranceNumber))
            // Một người khám nhiều đợt (định kỳ hằng năm, đổi hạng GPLX…) nên cùng bộ dữ kiện
            // có thể khớp nhiều hồ sơ. Chọn đợt khám GẦN NHẤT: người bệnh mở cổng là để xem
            // kết quả lần khám vừa rồi, không phải lần năm ngoái.
            // ⚠️ Quy tắc này do 235 chốt (24/08), CHƯA có BA duyệt — xem test
            // PortalCredentialTests.Nhieu_dot_thi_lay_ho_so_cua_dot_gan_nhat.
            .OrderByDescending(c => c.ExamDate)
            .ThenByDescending(c => c.CreatedDate)
            .FirstOrDefault();

        if (matched == null) return VerifyPortalCredentialsResult.Invalid();

        return new VerifyPortalCredentialsResult
        {
            IsValid = true,
            // Mã HỒ SƠ, không phải mã đợt — sẽ thành claim sub của token cổng NB.
            RecordCode = matched.RecordCode,
            SessionCode = matched.SessionCode,
            FullName = matched.FullName
        };
    }

    /// <summary>
    /// Luật đối chiếu, tách ra thành hàm thuần để kiểm được thẳng bằng test — phần dễ sai
    /// nhất của H-3b nằm ở đây chứ không ở câu truy vấn.
    ///
    /// HAI KHOÁ ĐỘC LẬP, cố ý chồng nhau:
    ///  • Dữ kiện nào ĐƯỢC GỬI thì phải khớp; gửi cả hai thì phải khớp CẢ HAI (và, không phải hoặc).
    ///  • Phải có ÍT NHẤT MỘT dữ kiện thật sự được đối chiếu thành công. Không có khoá này thì
    ///    một request chỉ mang Mã NB sẽ khớp mọi hồ sơ — mà Mã NB in trên giấy hẹn và đánh số
    ///    tuần tự, tức đoán được. Tầng validate cũng chặn tình huống đó bằng 4001, nhưng hai
    ///    chốt chặn nằm ở hai chỗ thì gỡ nhầm một cái vẫn chưa mở được cổng.
    ///
    /// Hệ quả cần thiết của cả hai: hồ sơ trống CCCD (rất thường gặp — danh sách Excel của đơn
    /// vị hay thiếu) KHÔNG đăng nhập được bằng CCCD rỗng. Đó chính là lỗ hổng mà một cách viết
    /// tự nhiên hơn ("so từng cột, cột nào bằng nhau thì khớp") mở ra: rỗng bằng rỗng là true,
    /// và Mã NB trở thành dữ kiện duy nhất.
    /// </summary>
    public static bool CredentialsMatch(
        string storedIdentity, string storedInsurance, string identityNumber, string insuranceNumber)
    {
        identityNumber = Normalize(identityNumber);
        insuranceNumber = Normalize(insuranceNumber);

        var matchedAny = false;

        if (identityNumber.Length > 0)
        {
            if (!EqualsStored(storedIdentity, identityNumber)) return false;
            matchedAny = true;
        }

        if (insuranceNumber.Length > 0)
        {
            if (!EqualsStored(storedInsurance, insuranceNumber)) return false;
            matchedAny = true;
        }

        return matchedAny;
    }

    /// <summary>
    /// <paramref name="provided"/> luôn khác rỗng khi vào tới đây (người gọi đã lọc), nên điều
    /// kiện <c>stored.Length > 0</c> là DƯ THỪA CÓ CHỦ Ý — gỡ nó đi không có test nào đỏ, đã đo
    /// bằng đột biến. Giữ lại vì nó là chốt chặn duy nhất còn đứng nếu một ngày ai đó bỏ nhánh
    /// lọc ở CredentialsMatch: lúc đó "" == "" lập tức thành một đường đăng nhập.
    /// </summary>
    private static bool EqualsStored(string stored, string provided)
    {
        stored = Normalize(stored);
        return stored.Length > 0 && string.Equals(stored, provided, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Cắt khoảng trắng — người bệnh gõ tay trên điện thoại, dán thừa dấu cách là thường.</summary>
    private static string Normalize(string value) => (value ?? "").Trim();

    /// <summary>
    /// Chỉ những cột cần cho việc đối chiếu. Không nạp cả thực thể ExamRecord: hàng đó mang
    /// địa chỉ, điện thoại, email — dữ liệu không lời gọi này nào cần, kéo về chỉ tăng chỗ rò.
    /// </summary>
    private class Candidate
    {
        public string RecordCode { get; init; } = "";
        public string FullName { get; init; } = "";
        public string IdentityNumber { get; init; } = "";
        public string InsuranceNumber { get; init; } = "";
        public string SessionCode { get; init; } = "";
        public DateOnly ExamDate { get; init; }
        public DateTime CreatedDate { get; init; }
    }
}
