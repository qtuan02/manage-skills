using HealthExam.API.Contracts;
using HealthExam.API.Middlewares;
using HealthExam.Application.Common;
using HealthExam.Domain.Common;
using HealthExam.Domain.ExamSessions;
using HealthExam.Domain.ExamRecords;
using HealthExam.Domain.Imports;
using HealthExam.Domain.Paraclinical;
using HealthExam.Domain.Webhooks;
using HealthExam.Domain.Integrations;
using HealthExam.Domain.Catalogs;
using HealthExam.Server.Service;
using Xunit;

namespace HealthExam.Tests;

/// <summary>
/// H-3b — đối chiếu dữ kiện đăng nhập cổng người bệnh cho iam-server.
///
/// Bộ test này chốt hai thứ mà mắt người review khó bắt:
///  1. RỖNG KHÔNG KHỚP RỖNG. Cột IdentityNumber/InsuranceNumber mặc định là "", và hồ sơ nạp
///     từ Excel của đơn vị thường không có CCCD. So thẳng hai chuỗi thì một request gửi
///     IdentityNumber rỗng sẽ khớp mọi hồ sơ trống CCCD, tức đăng nhập được chỉ bằng Mã NB —
///     mà Mã NB in trên giấy hẹn và đánh số tuần tự, đoán được.
///  2. TRẢ MÃ HỒ SƠ, KHÔNG PHẢI MÃ ĐỢT. Giá trị này thành claim sub của token cổng NB và
///     form-server so sub với FRM_Submission.SubjectID. Trả mã đợt thì cả trăm người trong
///     đợt dùng chung một sub — người bệnh A mở được phiếu người bệnh B.
/// </summary>
public class PortalCredentialTests
{
    private const string PatientCode = "NB0001";
    private const string Cccd = "079201000123";
    private const string Bhyt = "DN4797912345678";

    private static VerifyPortalCredentialsRequest Req(
        string patientCode = PatientCode, string identityNumber = Cccd, string insuranceNumber = "")
        => new()
        {
            PatientCode = patientCode,
            IdentityNumber = identityNumber,
            InsuranceNumber = insuranceNumber
        };

    // ------------------------------------------------------------------ đường đúng

    [Fact]
    public async Task Dung_du_kien_tra_ve_du_bon_truong()
    {
        using var db = new InMemoryTestDb();
        var session = db.SeedSession(sessionCode: "DK-2026-009");
        db.SeedRecord(session.SessionID, recordCode: "DK-2026-009-0007",
            patientCode: PatientCode, identityNumber: Cccd, fullName: "Trần Thị B");

        var result = await db.PortalCredentials.VerifyAsync(Req());

        Assert.True(result.IsValid);
        Assert.Equal("DK-2026-009-0007", result.RecordCode);
        Assert.Equal("DK-2026-009", result.SessionCode);
        Assert.Equal("Trần Thị B", result.FullName);
    }

    /// <summary>
    /// RecordCode trả về phải là mã HỒ SƠ. Test cố tình để hai mã khác hẳn nhau chứ không
    /// dùng tiền tố chung, để một lần lẫn RecordCode ↔ SessionCode là đỏ ngay.
    /// </summary>
    [Fact]
    public async Task RecordCode_la_ma_ho_so_chu_khong_phai_ma_dot()
    {
        using var db = new InMemoryTestDb();
        var session = db.SeedSession(sessionCode: "DOT-CONG-TY-ABC");
        db.SeedRecord(session.SessionID, recordCode: "HS-RIENG-0042",
            patientCode: PatientCode, identityNumber: Cccd);

        var result = await db.PortalCredentials.VerifyAsync(Req());

        Assert.Equal("HS-RIENG-0042", result.RecordCode);
        Assert.NotEqual(result.SessionCode, result.RecordCode);
    }

    /// <summary>Số thẻ BHYT là dữ kiện thay thế khi người bệnh không mang CCCD.</summary>
    [Fact]
    public async Task Dang_nhap_duoc_bang_so_the_BHYT()
    {
        using var db = new InMemoryTestDb();
        var session = db.SeedSession();
        db.SeedRecord(session.SessionID, patientCode: PatientCode, insuranceNumber: Bhyt);

        var result = await db.PortalCredentials.VerifyAsync(
            Req(identityNumber: "", insuranceNumber: Bhyt));

        Assert.True(result.IsValid);
    }

    /// <summary>Người bệnh gõ tay trên điện thoại, dán thừa dấu cách là chuyện thường.</summary>
    [Fact]
    public async Task Khoang_trang_thua_khong_lam_hong_doi_chieu()
    {
        using var db = new InMemoryTestDb();
        var session = db.SeedSession();
        db.SeedRecord(session.SessionID, patientCode: PatientCode, identityNumber: Cccd);

        var result = await db.PortalCredentials.VerifyAsync(
            Req(patientCode: "  " + PatientCode + " ", identityNumber: " " + Cccd));

        Assert.True(result.IsValid);
    }

    // ------------------------------------------------------------------ luật đối chiếu

    // Bộ dưới đây gọi THẲNG CredentialsMatch. Đi qua VerifyAsync thì tầng validate chặn mất
    // phần lớn trường hợp cần kiểm, nên test sẽ xanh kể cả khi luật đối chiếu bị gỡ — đã đo
    // bằng đột biến: bỏ điều kiện "giá trị lưu phải khác rỗng" mà toàn bộ 18 test đi qua
    // VerifyAsync vẫn xanh. Đó là lý do luật được tách thành hàm thuần.

    /// <summary>
    /// ★ Khoá thứ hai: phải có ÍT NHẤT MỘT dữ kiện thật sự được đối chiếu. Không có nó thì
    /// một request chỉ mang Mã NB khớp mọi hồ sơ — Mã NB in trên giấy hẹn và đánh số tuần tự.
    /// Tầng validate cũng chặn bằng 4001; đây là chốt chặn thứ hai, nằm ở chỗ khác.
    /// </summary>
    [Fact]
    public void Khong_du_kien_nao_duoc_doi_chieu_thi_khong_khop()
    {
        Assert.False(PortalCredentialService.CredentialsMatch(Cccd, Bhyt, "", ""));
    }

    /// <summary>
    /// ★ Hồ sơ trống CCCD thì gửi CCCD nào cũng không khớp — kể cả chuỗi chỉ có khoảng trắng.
    /// Đây là lỗ hổng chính của H-3b: danh sách Excel của đơn vị hay thiếu CCCD, và nếu rỗng
    /// khớp rỗng thì Mã NB (in trên giấy hẹn, đánh số tuần tự) thành dữ kiện đăng nhập duy nhất.
    /// Test chốt HÀNH VI chứ không chốt nhánh code — trong hiện thực hiện tại có hai chốt chặn
    /// chồng nhau nên gỡ một cái vẫn xanh; xem ghi chú ở EqualsStored.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("079201000123")]
    public void Ho_so_trong_CCCD_khong_khop_voi_bat_ky_CCCD_nao(string guiCccd)
    {
        Assert.False(PortalCredentialService.CredentialsMatch(
            storedIdentity: "", storedInsurance: "", identityNumber: guiCccd, insuranceNumber: ""));
    }

    /// <summary>Gửi cả hai thì phải khớp CẢ HAI — và, không phải hoặc.</summary>
    [Fact]
    public void Gui_ca_hai_du_kien_thi_mot_cai_sai_la_hong()
    {
        Assert.True(PortalCredentialService.CredentialsMatch(Cccd, Bhyt, Cccd, Bhyt));
        Assert.False(PortalCredentialService.CredentialsMatch(Cccd, Bhyt, Cccd, "DN4797900000000"));
        Assert.False(PortalCredentialService.CredentialsMatch(Cccd, Bhyt, "000000000000", Bhyt));
    }

    /// <summary>
    /// Gửi BHYT trong khi hồ sơ trống BHYT là KHÔNG khớp, dù CCCD đúng. Hệ quả của luật "và":
    /// dữ kiện đã gửi thì phải đối chiếu được, không im lặng bỏ qua.
    /// ⚠️ Kéo theo một hệ quả cho iam-server: chỉ gửi sang những ô người bệnh THẬT SỰ điền.
    /// </summary>
    [Fact]
    public void Gui_BHYT_ma_ho_so_trong_BHYT_thi_khong_khop()
    {
        Assert.False(PortalCredentialService.CredentialsMatch(
            storedIdentity: Cccd, storedInsurance: "", identityNumber: Cccd, insuranceNumber: Bhyt));
    }

    /// <summary>Chỉ một trong hai dữ kiện là đủ, miễn nó khớp một giá trị lưu khác rỗng.</summary>
    [Fact]
    public void Mot_du_kien_dung_la_du()
    {
        Assert.True(PortalCredentialService.CredentialsMatch(Cccd, Bhyt, Cccd, ""));
        Assert.True(PortalCredentialService.CredentialsMatch(Cccd, Bhyt, "", Bhyt));
    }

    /// <summary>Luật trên có được nối vào đường thật hay không — chốt qua VerifyAsync.</summary>
    [Fact]
    public async Task Ho_so_trong_ca_hai_du_kien_khong_dang_nhap_duoc()
    {
        using var db = new InMemoryTestDb();
        var session = db.SeedSession();
        db.SeedRecord(session.SessionID, patientCode: PatientCode,
            identityNumber: "", insuranceNumber: "");

        var result = await db.PortalCredentials.VerifyAsync(Req());

        Assert.False(result.IsValid);
    }

    // ------------------------------------------------------------------ không nói sai vế nào

    /// <summary>
    /// Sai Mã NB và sai CCCD phải cho ra phản hồi GIỐNG HỆT nhau. Lệch một chi tiết là người
    /// dò biết được "mã NB này có tồn tại" mà không cần biết CCCD — Mã NB đánh số tuần tự.
    /// </summary>
    [Fact]
    public async Task Sai_ma_NB_va_sai_CCCD_cho_ra_phan_hoi_giong_het_nhau()
    {
        using var db = new InMemoryTestDb();
        var session = db.SeedSession();
        db.SeedRecord(session.SessionID, patientCode: PatientCode, identityNumber: Cccd);

        var saiMaNb = await db.PortalCredentials.VerifyAsync(Req(patientCode: "NB9999"));
        var saiCccd = await db.PortalCredentials.VerifyAsync(Req(identityNumber: "000000000000"));

        Assert.False(saiMaNb.IsValid);
        Assert.False(saiCccd.IsValid);
        Assert.Equal(saiMaNb.RecordCode, saiCccd.RecordCode);
        Assert.Equal(saiMaNb.SessionCode, saiCccd.SessionCode);
        Assert.Equal(saiMaNb.FullName, saiCccd.FullName);
    }

    /// <summary>Đối chiếu hỏng thì không rò tên, mã hồ sơ hay mã đợt.</summary>
    [Fact]
    public async Task Doi_chieu_hong_khong_ro_truong_nao()
    {
        using var db = new InMemoryTestDb();
        var session = db.SeedSession();
        db.SeedRecord(session.SessionID, patientCode: PatientCode, identityNumber: Cccd,
            fullName: "Lê Văn C");

        var result = await db.PortalCredentials.VerifyAsync(Req(identityNumber: "000000000000"));

        Assert.False(result.IsValid);
        Assert.Equal("", result.RecordCode);
        Assert.Equal("", result.SessionCode);
        Assert.Equal("", result.FullName);
    }

    // ------------------------------------------------------------------ phạm vi tra cứu

    /// <summary>
    /// Chốt chặn tenant. Pilot dùng chung DB với prod và cùng một Mã NB ở hai đơn vị là hai
    /// người khác nhau — thiếu bộ lọc DivisionID thì người bệnh đơn vị này đọc phiếu đơn vị kia.
    /// </summary>
    [Fact]
    public async Task Ho_so_cua_tenant_khac_khong_doi_chieu_duoc()
    {
        using var db = new InMemoryTestDb();
        var session = db.SeedSession(divisionId: "TENANT-KHAC");
        db.SeedRecord(session.SessionID, patientCode: PatientCode, identityNumber: Cccd,
            divisionId: "TENANT-KHAC");

        var result = await db.PortalCredentials.VerifyAsync(Req());

        Assert.False(result.IsValid);
    }

    /// <summary>Hồ sơ đã huỷ không còn là đường vào cổng — giấy hẹn đã phát thì vẫn còn mã.</summary>
    [Theory]
    [InlineData(ExamRecordState.RegistrationCancelled)]
    [InlineData(ExamRecordState.ExamCancelled)]
    public async Task Ho_so_da_huy_khong_dang_nhap_duoc(ExamRecordState state)
    {
        using var db = new InMemoryTestDb();
        var session = db.SeedSession();
        db.SeedRecord(session.SessionID, state: state,
            patientCode: PatientCode, identityNumber: Cccd);

        var result = await db.PortalCredentials.VerifyAsync(Req());

        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task Dot_kham_da_huy_khong_dang_nhap_duoc()
    {
        using var db = new InMemoryTestDb();
        var session = db.SeedSession(state: ExamSessionState.Cancelled);
        db.SeedRecord(session.SessionID, patientCode: PatientCode, identityNumber: Cccd);

        var result = await db.PortalCredentials.VerifyAsync(Req());

        Assert.False(result.IsValid);
    }

    /// <summary>Đợt ĐÃ ĐÓNG thì vẫn vào được: đóng sổ đợt là lúc người bệnh cần xem kết quả nhất.</summary>
    [Fact]
    public async Task Dot_da_dong_van_dang_nhap_duoc()
    {
        using var db = new InMemoryTestDb();
        var session = db.SeedSession(state: ExamSessionState.Closed);
        db.SeedRecord(session.SessionID, state: ExamRecordState.Completed,
            patientCode: PatientCode, identityNumber: Cccd);

        var result = await db.PortalCredentials.VerifyAsync(Req());

        Assert.True(result.IsValid);
    }

    // ------------------------------------------------------------------ một người, nhiều đợt

    /// <summary>
    /// ★ Quy tắc do 235 chốt ngày 24/08, CHƯA có BA duyệt — ghi thành test để nó là quy tắc
    /// chạy được chứ không phải câu văn trong tài liệu.
    ///
    /// Một người khám nhiều đợt (định kỳ hằng năm, đổi hạng GPLX) nên cùng bộ dữ kiện khớp
    /// nhiều hồ sơ. Chọn đợt khám GẦN NHẤT: người bệnh mở cổng để xem lần khám vừa rồi.
    /// </summary>
    [Fact]
    public async Task Nhieu_dot_thi_lay_ho_so_cua_dot_gan_nhat()
    {
        using var db = new InMemoryTestDb();
        var cu = db.SeedSession(sessionCode: "DK-2025-001", examDate: new DateOnly(2025, 3, 10));
        var moi = db.SeedSession(sessionCode: "DK-2026-001", examDate: new DateOnly(2026, 8, 26));

        db.SeedRecord(cu.SessionID, recordCode: "DK-2025-001-0005",
            patientCode: PatientCode, identityNumber: Cccd);
        db.SeedRecord(moi.SessionID, recordCode: "DK-2026-001-0011",
            patientCode: PatientCode, identityNumber: Cccd);

        var result = await db.PortalCredentials.VerifyAsync(Req());

        Assert.True(result.IsValid);
        Assert.Equal("DK-2026-001-0011", result.RecordCode);
        Assert.Equal("DK-2026-001", result.SessionCode);
    }

    /// <summary>Đợt gần nhất bị huỷ thì rơi về đợt còn hiệu lực, không phải trả hỏng.</summary>
    [Fact]
    public async Task Dot_gan_nhat_bi_huy_thi_lay_dot_con_hieu_luc()
    {
        using var db = new InMemoryTestDb();
        var cu = db.SeedSession(sessionCode: "DK-2025-001", examDate: new DateOnly(2025, 3, 10));
        var moiHuy = db.SeedSession(sessionCode: "DK-2026-001",
            state: ExamSessionState.Cancelled, examDate: new DateOnly(2026, 8, 26));

        db.SeedRecord(cu.SessionID, recordCode: "DK-2025-001-0005",
            patientCode: PatientCode, identityNumber: Cccd);
        db.SeedRecord(moiHuy.SessionID, recordCode: "DK-2026-001-0011",
            patientCode: PatientCode, identityNumber: Cccd);

        var result = await db.PortalCredentials.VerifyAsync(Req());

        Assert.Equal("DK-2025-001-0005", result.RecordCode);
    }

    // ------------------------------------------------------------------ hình dạng lời gọi

    /// <summary>
    /// Thiếu trường là lỗi TÍCH HỢP của iam-server, không phải "sai mật khẩu" — nên 4001 chứ
    /// không phải IsValid=false. Giấu đi thì lỗi nối dây trông y hệt người bệnh gõ sai.
    /// </summary>
    [Fact]
    public async Task Thieu_ma_NB_tra_4001()
    {
        using var db = new InMemoryTestDb();

        var ex = await Assert.ThrowsAsync<HealthExamException>(
            () => db.PortalCredentials.VerifyAsync(Req(patientCode: "")));

        Assert.Equal(ErrorCodes.BadRequest, ex.ErrorCode);
    }

    [Fact]
    public async Task Khong_co_du_kien_nao_ngoai_ma_NB_tra_4001()
    {
        using var db = new InMemoryTestDb();

        var ex = await Assert.ThrowsAsync<HealthExamException>(
            () => db.PortalCredentials.VerifyAsync(Req(identityNumber: "", insuranceNumber: "")));

        Assert.Equal(ErrorCodes.BadRequest, ex.ErrorCode);
    }

    [Fact]
    public async Task Body_rong_tra_4001_chu_khong_no()
    {
        using var db = new InMemoryTestDb();

        var ex = await Assert.ThrowsAsync<HealthExamException>(
            () => db.PortalCredentials.VerifyAsync(null));

        Assert.Equal(ErrorCodes.BadRequest, ex.ErrorCode);
    }
    /// <summary>
    /// ★ F4 — HAI HỢP ĐỒNG PHẢI GẶP NHAU. Mẫu Excel chỉ bắt buộc FullName, còn cổng người
    /// bệnh bắt buộc PatientCode. Chốt của 236: hồ sơ nạp bằng Excel được cấp mã NB tự sinh.
    ///
    /// Test này chốt đúng chỗ nối: mã do <see cref="ExamRecordService.ComposeGeneratedPatientCode"/>
    /// sinh ra phải ĐĂNG NHẬP ĐƯỢC thật, chứ không chỉ nằm trong cột cho đẹp. Không có nó thì
    /// hai bên vẫn xanh riêng lẻ mà người bệnh vẫn không vào được cổng.
    /// </summary>
    [Fact]
    public async Task Ma_NB_tu_sinh_cua_lo_Excel_dang_nhap_duoc_cong_nguoi_benh()
    {
        using var db = new InMemoryTestDb();
        var session = db.SeedSession(sessionCode: "KSK2608-0001");
        var recordCode = ExamRecordService.ComposeRecordCode(session.SessionCode, 7);
        var generated = ExamRecordService.ComposeGeneratedPatientCode(recordCode);
        db.SeedRecord(session.SessionID, recordCode: recordCode,
            patientCode: generated, identityNumber: "079203001234", fullName: "Lê Thị C");

        var result = await db.PortalCredentials.VerifyAsync(new VerifyPortalCredentialsRequest
        {
            PatientCode = generated,
            IdentityNumber = "079203001234"
        });

        Assert.True(result.IsValid);
        // RecordCode chứ không phải SessionCode: giá trị này thành claim sub của token và
        // form-server so nó với FRM_Submission.SubjectID — trả nhầm mã đợt là người bệnh A
        // đọc được phiếu người bệnh B.
        Assert.Equal(recordCode, result.RecordCode);
        Assert.Equal(session.SessionCode, result.SessionCode);
    }

    /// <summary>
    /// …và mặt trái: dòng Excel CHỈ CÓ họ tên thì mã tự sinh cũng vô ích — phép so từ chối
    /// mọi giá trị lưu rỗng. Đây chính là lý do pha 1 phải cảnh báo (F4 ý 3), vì không có gì
    /// ở tầng này cứu được nữa.
    /// </summary>
    [Fact]
    public async Task Ho_so_thieu_ca_CCCD_lan_BHYT_thi_ma_tu_sinh_cung_khong_dang_nhap_duoc()
    {
        using var db = new InMemoryTestDb();
        var session = db.SeedSession(sessionCode: "KSK2608-0001");
        var recordCode = ExamRecordService.ComposeRecordCode(session.SessionCode, 8);
        var generated = ExamRecordService.ComposeGeneratedPatientCode(recordCode);
        db.SeedRecord(session.SessionID, recordCode: recordCode, patientCode: generated,
            identityNumber: "", insuranceNumber: "", fullName: "Phạm Văn D");

        var result = await db.PortalCredentials.VerifyAsync(new VerifyPortalCredentialsRequest
        {
            PatientCode = generated,
            IdentityNumber = "079203009999"
        });

        Assert.False(result.IsValid);
    }
}
