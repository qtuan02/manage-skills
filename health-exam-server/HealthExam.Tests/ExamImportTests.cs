using HealthExam.Domain.Common;
using HealthExam.Domain.ExamSessions;
using HealthExam.Domain.ExamRecords;
using HealthExam.Domain.Imports;
using HealthExam.Domain.Paraclinical;
using HealthExam.Domain.Webhooks;
using HealthExam.Domain.Integrations;
using HealthExam.Domain.Catalogs;
using HealthExam.Domain.Patients;
using HealthExam.API.Contracts;
using HealthExam.API.Middlewares;
using HealthExam.Application.Common;
using HealthExam.Application.Imports;
using HealthExam.Infrastructure.Excel;
using HealthExam.Server.Service;
using ImportCommitRequest = HealthExam.API.Contracts.ImportCommitRequest;
using ImportRowStatus = HealthExam.API.Contracts.ImportRowStatus;
using OfficeOpenXml;
using Xunit;

namespace HealthExam.Tests;

/// <summary>
/// H-4 — nạp Excel danh sách đăng ký (UC03.5).
///
/// Bộ test chia theo tầng, cố ý:
///  • Luật kiểm tra từng dòng (ValidateRow) là hàm THUẦN → test thẳng, không dựng file.
///  • Đọc file (ExamImportSheet) test bằng workbook dựng trong bộ nhớ, không đọc file trên đĩa.
///  • Hai pha + idempotent: chỉ test được các NHÁNH CHỐT CHẶN ở đây. Đường ghi hồ sơ thật đi
///    qua ExamRecordService.CreateAsync, thứ cấp mã bằng SQL thô trên DbConnection — provider
///    in-memory không có. Phần đó đo bằng curl trên PostgreSQL thật.
/// </summary>
public class ExamImportTests
{
    // ================================================================ bộ cột (Q-HEX-03)

    /// <summary>
    /// ★ Chốt rằng KHÔNG có tên cột tiếng Việt nào được đoán. Q-HEX-03 chưa chốt; ai thêm
    /// "Họ tên"/"Số CCCD" vào Aliases trước khi BA duyệt thì test này đỏ và phải giải thích.
    /// </summary>
    [Fact]
    public void Chua_co_bi_danh_cot_nao_vi_Q_HEX_03_chua_chot()
    {
        Assert.All(ExamImportColumns.All, c => Assert.Empty(c.Aliases));
    }

    /// <summary>
    /// Chỉ ĐÚNG MỘT cột bắt buộc ở mức tiêu đề. Mỗi cột bắt buộc thêm vào là một lý do nữa để
    /// cả file 500 dòng bị từ chối, nên con số này được chốt bằng test chứ không để trôi.
    /// </summary>
    [Fact]
    public void Chi_FullName_la_cot_bat_buoc_o_muc_tieu_de()
    {
        Assert.Equal(new[] { "FullName" },
            ExamImportColumns.HeaderRequiredColumns.Select(c => c.Field).ToArray());
    }

    [Theory]
    [InlineData("FullName", "FullName")]
    [InlineData("  fullname  ", "FullName")]
    [InlineData("PATIENTCODE", "PatientCode")]
    public void Khop_tieu_de_bo_qua_hoa_thuong_va_khoang_trang(string header, string field)
        => Assert.Equal(field, ExamImportColumns.Resolve(header)?.Field);

    [Theory]
    [InlineData("Họ và tên")]
    [InlineData("")]
    [InlineData(null)]
    public void Tieu_de_khong_nhan_ra_thi_tra_null(string header)
        => Assert.Null(ExamImportColumns.Resolve(header));

    // ================================================================ luật từng dòng

    private static ExamSession Session(string variantCode = "DTK_01") => new()
    {
        SessionID = Guid.NewGuid(),
        SessionCode = "DK-2026-001",
        VariantCode = variantCode,
        State = ExamSessionState.Open
    };

    private static ParsedImportRow Row(int rowNo = 2, params (string Field, string Value)[] fields)
    {
        var raw = new Dictionary<string, string>();
        var f = new Dictionary<string, string>();
        foreach (var (field, val) in fields) { f[field] = val; raw[field] = val; }
        return new ParsedImportRow(rowNo, raw, f);
    }

    private static ImportBatchRow Validate(ParsedImportRow row, ExamSession session = null)
        => UploadImportHandler.ValidateRow(row, session ?? Session(),
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            new HashSet<string>(StringComparer.OrdinalIgnoreCase));

    [Fact]
    public void Dong_du_du_lieu_thi_dat()
    {
        var result = Validate(Row(2, ("FullName", "Nguyễn Văn A"), ("PatientCode", "NB001")));

        Assert.True(result.IsValid);
        Assert.Equal("", result.ErrorCode);
        Assert.Equal(2, result.RowNo);
    }

    [Fact]
    public void Thieu_ho_ten_thi_dong_hong_voi_REQUIRED()
    {
        var result = Validate(Row(3, ("PatientCode", "NB001")));

        Assert.False(result.IsValid);
        Assert.Equal(ImportRowErrors.Required, result.ErrorCode);
        Assert.Contains("FullName", result.ErrorMessage);
    }

    /// <summary>
    /// ★ Nhóm khám bỏ trống thì KẾ THỪA của đợt — đúng cách gói khám đã kế thừa ở CreateAsync.
    /// Không kế thừa thì mọi file không có cột Nhóm khám đều hỏng sạch 500 dòng.
    /// </summary>
    [Fact]
    public void Nhom_kham_bo_trong_thi_ke_thua_cua_dot()
    {
        var result = Validate(Row(2, ("FullName", "Nguyễn Văn A")), Session("DTK_03"));

        Assert.True(result.IsValid);
    }

    // ---------------------------------------------------------------- trần độ dài (F2)

    /// <summary>
    /// ★ Ô dài hơn trần của cột phải hỏng Ở PHA 1.
    ///
    /// Đây là ca đã làm cả lô 500 giữa chừng: pha 1 duyệt một CCCD 28 ký tự, tới lệnh INSERT
    /// thì PostgreSQL ném 22001, mà 22001 không phải unique-violation nên không tầng nào bắt.
    /// Kết quả đo được: HTTP 500, 2 hồ sơ đã vào DB, lô kẹt Pending, bấm lại đẻ thêm hồ sơ
    /// trùng (F2, review MR !4). Test này chốt rằng nó dừng ở MỘT DÒNG.
    /// </summary>
    [Theory]
    [InlineData("IdentityNumber", 21)]
    [InlineData("PatientCode", 51)]
    [InlineData("FullName", 256)]
    [InlineData("Email", 101)]
    [InlineData("PhoneNumber", 21)]
    public void O_dai_hon_tran_cua_cot_thi_hong_o_pha_1(string field, int length)
    {
        var result = Validate(Row(2, ("FullName", "Nguyễn Văn A"), (field, new string('x', length))));

        Assert.False(result.IsValid);
        Assert.Equal(ImportRowErrors.TooLong, result.ErrorCode);
        Assert.Contains(field, result.ErrorMessage);
        Assert.Contains($"{length} ký tự", result.ErrorMessage);
    }

    /// <summary>Đúng bằng trần thì ĐẠT — lỗi lệch một đơn vị ở đây làm hỏng những dòng hợp lệ.</summary>
    [Fact]
    public void O_dai_dung_bang_tran_thi_van_dat()
    {
        var result = Validate(Row(2,
            ("FullName", new string('x', RecordFieldLengths.FullName)),
            ("IdentityNumber", new string('9', RecordFieldLengths.IdentityNumber))));

        Assert.True(result.IsValid);
    }

    /// <summary>
    /// ★ Trần dùng để kiểm PHẢI là chính trần khai trong DDL. Hai con số viết tay ở hai chỗ
    /// là cách F2 xảy ra, nên chốt bằng test: đổi HasMaxLength mà quên RecordFieldLengths
    /// (hoặc ngược lại) thì test này đỏ.
    /// </summary>
    [Fact]
    public void Tran_do_dai_cua_pha_1_khop_voi_DDL()
    {
        using var db = new InMemoryTestDb();
        var recordEntity = db.Db.Model.FindEntityType(typeof(ExamRecord));
        var patientEntity = db.Db.Model.FindEntityType(typeof(Patient));
        var insEntity = db.Db.Model.FindEntityType(typeof(PatientInsurance));
        var empEntity = db.Db.Model.FindEntityType(typeof(PatientEmployment));

        Assert.Equal(recordEntity.FindProperty(nameof(ExamRecord.RecordCode)).GetMaxLength(), RecordFieldLengths.RecordCode);
        Assert.Equal(recordEntity.FindProperty(nameof(ExamRecord.VariantCode)).GetMaxLength(), RecordFieldLengths.VariantCode);
        Assert.Equal(recordEntity.FindProperty(nameof(ExamRecord.HealthClassCode)).GetMaxLength(), RecordFieldLengths.HealthClassCode);

        Assert.Equal(patientEntity.FindProperty(nameof(Patient.PatientCode)).GetMaxLength(), RecordFieldLengths.PatientCode);
        Assert.Equal(patientEntity.FindProperty(nameof(Patient.FullName)).GetMaxLength(), RecordFieldLengths.FullName);
        Assert.Equal(patientEntity.FindProperty(nameof(Patient.IdentityNumber)).GetMaxLength(), RecordFieldLengths.IdentityNumber);
        Assert.Equal(patientEntity.FindProperty(nameof(Patient.PhoneNumber)).GetMaxLength(), RecordFieldLengths.PhoneNumber);
        Assert.Equal(patientEntity.FindProperty(nameof(Patient.Email)).GetMaxLength(), RecordFieldLengths.Email);
        Assert.Equal(patientEntity.FindProperty(nameof(Patient.Address)).GetMaxLength(), RecordFieldLengths.Address);

        Assert.Equal(insEntity.FindProperty(nameof(PatientInsurance.InsuranceNumber)).GetMaxLength(), RecordFieldLengths.InsuranceNumber);

        Assert.Equal(empEntity.FindProperty(nameof(PatientEmployment.StaffCode)).GetMaxLength(), RecordFieldLengths.StaffCode);
        Assert.Equal(empEntity.FindProperty(nameof(PatientEmployment.OrgDeptName)).GetMaxLength(), RecordFieldLengths.OrgDeptName);
        Assert.Equal(empEntity.FindProperty(nameof(PatientEmployment.JobTitle)).GetMaxLength(), RecordFieldLengths.JobTitle);
    }

    /// <summary>Đợt cũng không khai Nhóm khám thì mới hỏng — hỏng ở mức DÒNG, không phải cả file.</summary>
    [Fact]
    public void Dot_khong_khai_nhom_kham_thi_dong_hong_chu_khong_phai_ca_file()
    {
        var result = Validate(Row(2, ("FullName", "Nguyễn Văn A")), Session(""));

        Assert.False(result.IsValid);
        Assert.Equal(ImportRowErrors.Required, result.ErrorCode);
    }

    [Fact]
    public void Nhom_kham_la_ma_khong_co_that_thi_UNKNOWN_VARIANT()
    {
        var result = Validate(Row(2, ("FullName", "A"), ("VariantCode", "DTK_99")));

        Assert.False(result.IsValid);
        Assert.Equal(ImportRowErrors.UnknownVariant, result.ErrorCode);
    }

    [Theory]
    [InlineData("Dob", "32/13/2020")]
    [InlineData("BirthYear", "19")]
    [InlineData("BirthYear", "2O25")]
    [InlineData("GenderID", "Nam")]
    public void O_sai_dinh_dang_thi_BAD_FORMAT(string field, string value)
    {
        var result = Validate(Row(2, ("FullName", "A"), (field, value)));

        Assert.False(result.IsValid);
        Assert.Equal(ImportRowErrors.BadFormat, result.ErrorCode);
    }

    /// <summary>Ô rỗng KHÔNG phải sai định dạng — cột không bắt buộc bỏ trống là bình thường.</summary>
    [Fact]
    public void O_rong_o_cot_khong_bat_buoc_van_dat()
    {
        var result = Validate(Row(2, ("FullName", "A"), ("Dob", ""), ("GenderID", "")));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Trung_nguoi_da_co_ho_so_trong_dot_thi_DUPLICATE()
    {
        var result = UploadImportHandler.ValidateRow(
            Row(2, ("FullName", "A"), ("PatientCode", "NB001")), Session(),
            new HashSet<string>(new[] { "NB001" }, StringComparer.OrdinalIgnoreCase),
            new HashSet<string>(StringComparer.OrdinalIgnoreCase));

        Assert.False(result.IsValid);
        Assert.Equal(ImportRowErrors.Duplicate, result.ErrorCode);
    }

    /// <summary>
    /// ★ RawData giữ NGUYÊN dòng gốc, kể cả cột server không hiểu. Đây là thứ duy nhất cho
    /// phép trả lại đúng 12 dòng hỏng trong file 500 dòng để người dùng sửa rồi nạp lại.
    /// </summary>
    [Fact]
    public void RawData_giu_nguyen_ca_cot_server_khong_hieu()
    {
        var row = Row(2, ("FullName", "Nguyễn Văn A"), ("Ghi chú của phòng nhân sự", "ca chiều"));

        var result = Validate(row);

        Assert.Contains("Ghi chú của phòng nhân sự", result.RawData);
        Assert.Contains("ca chiều", result.RawData);
    }

    // ================================================================ đọc ngày tháng

    [Theory]
    [InlineData("1990-04-03", 1990, 4, 3)]
    [InlineData("03/04/1990", 1990, 4, 3)]   // gõ kiểu Việt Nam: ngày trước tháng
    [InlineData("3/4/1990", 1990, 4, 3)]
    [InlineData("03-04-1990", 1990, 4, 3)]
    public void Doc_duoc_ngay_sinh_theo_ca_hai_thoi_quen(string raw, int y, int m, int d)
    {
        Assert.True(UploadImportHandler.TryParseDate(raw, out var value));
        Assert.Equal(new DateOnly(y, m, d), value);
    }

    /// <summary>
    /// ★ "03/04/1990" phải đọc là 3 THÁNG 4, không phải 4 tháng 3. Cả hai đều hợp lệ về hình
    /// dạng nên không có lỗi nào báo ra — sai ở đây là sai lặng lẽ trên toàn bộ file.
    /// </summary>
    [Fact]
    public void Ngay_mo_ho_uu_tien_kieu_Viet_Nam_chu_khong_phai_kieu_My()
    {
        UploadImportHandler.TryParseDate("03/04/1990", out var value);
        Assert.Equal(4, value.Month);
    }

    [Theory]
    [InlineData("")]
    [InlineData("hôm qua")]
    [InlineData("32/13/2020")]
    public void Ngay_khong_doc_duoc_thi_bao_that_bai(string raw)
        => Assert.False(UploadImportHandler.TryParseDate(raw, out _));

    [Theory]
    [InlineData("1990", true)]
    [InlineData("1899", false)]
    [InlineData("19", false)]
    [InlineData("3000", false)]
    public void Nam_sinh_phai_nam_trong_khoang_nguoi_that(string raw, bool ok)
        => Assert.Equal(ok, UploadImportHandler.TryParseBirthYear(raw, out _));

    // ================================================================ đọc file

    /// <summary>Dựng một workbook trong bộ nhớ — test không đụng đĩa, không phụ thuộc file mẫu.</summary>
    private static MemoryStream Workbook(string[] headers, params string[][] rows)
    {
        ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
        using var package = new ExcelPackage();
        var ws = package.Workbook.Worksheets.Add("DanhSach");
        for (var c = 0; c < headers.Length; c++) ws.Cells[1, c + 1].Value = headers[c];
        for (var r = 0; r < rows.Length; r++)
            for (var c = 0; c < rows[r].Length; c++) ws.Cells[r + 2, c + 1].Value = rows[r][c];
        return new MemoryStream(package.GetAsByteArray());
    }

    [Fact]
    public void Doc_duoc_file_dung_bo_cot()
    {
        using var stream = Workbook(
            new[] { "FullName", "PatientCode" },
            new[] { "Nguyễn Văn A", "NB001" },
            new[] { "Trần Thị B", "NB002" });

        var sheet = ExamImportSheet.Read(stream);

        Assert.Equal("DanhSach", sheet.Name);
        Assert.Equal(2, sheet.Rows.Count);
        Assert.Equal("Nguyễn Văn A", sheet.Rows[0].Fields["FullName"]);
        Assert.Equal(2, sheet.Rows[0].RowNo);   // số dòng TRONG FILE, tính cả tiêu đề
    }

    /// <summary>Thiếu cột bắt buộc ⇒ 4002 cho CẢ FILE, và không có lô nào được tạo.</summary>
    [Fact]
    public void Thieu_cot_bat_buoc_tra_4002()
    {
        using var stream = Workbook(new[] { "PatientCode" }, new[] { "NB001" });

        var ex = Assert.Throws<HealthExamException>(() => ExamImportSheet.Read(stream));

        Assert.Equal(ErrorCodes.FileInvalid, ex.ErrorCode);
        Assert.Contains("FullName", ex.Message);
    }

    [Fact]
    public void File_khong_phai_Excel_tra_4002()
    {
        using var stream = new MemoryStream("đây không phải file excel"u8.ToArray());

        var ex = Assert.Throws<HealthExamException>(() => ExamImportSheet.Read(stream));

        Assert.Equal(ErrorCodes.FileInvalid, ex.ErrorCode);
    }

    /// <summary>
    /// File của đơn vị hay có dòng tiêu đề trang trí ở trên cùng. Bộ đọc phải tìm đúng dòng
    /// tiêu đề THẬT, nếu không thì mọi file thực tế đều báo thiếu cột bắt buộc.
    /// </summary>
    [Fact]
    public void Bo_qua_dong_trang_tri_o_tren_dong_tieu_de()
    {
        ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
        using var package = new ExcelPackage();
        var ws = package.Workbook.Worksheets.Add("DanhSach");
        ws.Cells[1, 1].Value = "DANH SÁCH KHÁM SỨC KHOẺ CÔNG TY ABC";
        ws.Cells[2, 1].Value = "Năm 2026";
        ws.Cells[3, 1].Value = "FullName";
        ws.Cells[3, 2].Value = "PatientCode";
        ws.Cells[4, 1].Value = "Nguyễn Văn A";
        ws.Cells[4, 2].Value = "NB001";
        using var stream = new MemoryStream(package.GetAsByteArray());

        var sheet = ExamImportSheet.Read(stream);

        Assert.Single(sheet.Rows);
        Assert.Equal(4, sheet.Rows[0].RowNo);
    }

    /// <summary>Dòng trắng giữa file là chuyện thường — không được tính thành dòng hỏng.</summary>
    [Fact]
    public void Dong_trang_khong_tinh_la_dong_hong()
    {
        using var stream = Workbook(
            new[] { "FullName" },
            new[] { "Nguyễn Văn A" },
            new[] { "" },
            new[] { "Trần Thị B" });

        var sheet = ExamImportSheet.Read(stream);

        Assert.Equal(2, sheet.Rows.Count);
    }

    /// <summary>File mẫu phải tự nạp lại được — nếu không thì hướng dẫn "tải mẫu về mà điền" là sai.</summary>
    [Fact]
    public void File_mau_tu_no_doc_lai_duoc()
    {
        using var stream = new MemoryStream(ExamImportWorkbook.BuildTemplate());

        var sheet = ExamImportSheet.Read(stream);

        // Dòng duy nhất là dòng ghi chú — đọc được, không nổ.
        Assert.All(ExamImportColumns.All,
            c => Assert.Contains(c.Field, sheet.Rows[0].Raw.Keys));
    }

    [Fact]
    public void File_dong_loi_co_cot_Ly_do()
    {
        var rows = new[]
        {
            new ImportBatchRow
            {
                RowNo = 5, IsValid = false,
                RawData = """{"FullName":"","PatientCode":"NB007"}""",
                ErrorMessage = "FullName: Bỏ trống (bắt buộc)"
            }
        };

        using var stream = new MemoryStream(ExamImportWorkbook.BuildErrorFile(rows));
        ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
        using var package = new ExcelPackage(stream);
        var ws = package.Workbook.Worksheets[0];

        Assert.Equal("Dòng", ws.Cells[1, 1].Text);
        Assert.Equal("Lý do", ws.Cells[1, 4].Text);
        Assert.Equal("5", ws.Cells[2, 1].Text);
        Assert.Equal("NB007", ws.Cells[2, 3].Text);
        Assert.Contains("Bỏ trống", ws.Cells[2, 4].Text);
    }

    // ================================================================ hai pha & idempotent

    [Fact]
    public async Task Upload_chua_ghi_ho_so_nao()
    {
        using var db = new InMemoryTestDb();
        var session = db.SeedSession();
        using var stream = Workbook(new[] { "FullName" }, new[] { "Nguyễn Văn A" }, new[] { "Trần Thị B" });

        var result = await db.Imports.UploadAsync(session.SessionID, stream, "DS.xlsx", 1, 50);

        Assert.Equal(2, result.TotalRows);
        Assert.Equal(2, result.ValidRows);
        Assert.Equal(ImportBatchState.Pending, result.State);
        Assert.Equal(0, result.CreatedRecordCount);
        // ★ Cốt lõi của hai pha: chưa có hồ sơ nào trong DB.
        Assert.Empty(db.Db.ExamRecords);
    }

    /// <summary>Trùng TRONG CHÍNH FILE phải bắt ngay ở pha 1, không đợi tới lúc người dùng bấm xác nhận.</summary>
    [Fact]
    public async Task Upload_bat_duoc_trung_trong_chinh_file()
    {
        using var db = new InMemoryTestDb();
        var session = db.SeedSession();
        using var stream = Workbook(
            new[] { "FullName", "PatientCode" },
            new[] { "Nguyễn Văn A", "NB001" },
            new[] { "Nguyễn Văn A", "NB001" });

        var result = await db.Imports.UploadAsync(session.SessionID, stream, "DS.xlsx", 1, 50);

        Assert.Equal(1, result.ValidRows);
        Assert.Equal(1, result.InvalidRows);
        Assert.Equal(ImportRowStatus.Duplicate, result.Rows.Items[1].Status);
    }

    [Fact]
    public async Task Upload_bat_duoc_trung_voi_ho_so_da_co_trong_dot()
    {
        using var db = new InMemoryTestDb();
        var session = db.SeedSession();
        db.SeedRecord(session.SessionID, patientCode: "NB001");
        using var stream = Workbook(new[] { "FullName", "PatientCode" }, new[] { "Nguyễn Văn A", "NB001" });

        var result = await db.Imports.UploadAsync(session.SessionID, stream, "DS.xlsx", 1, 50);

        Assert.Equal(0, result.ValidRows);
        Assert.Equal(ImportRowStatus.Duplicate, result.Rows.Items[0].Status);
    }

    [Fact]
    public async Task Upload_vao_dot_da_dong_bi_chan_4091()
    {
        using var db = new InMemoryTestDb();
        var session = db.SeedSession(state: ExamSessionState.Closed);
        using var stream = Workbook(new[] { "FullName" }, new[] { "Nguyễn Văn A" });

        var ex = await Assert.ThrowsAsync<HealthExamException>(
            () => db.Imports.UploadAsync(session.SessionID, stream, "DS.xlsx", 1, 50));

        Assert.Equal(ErrorCodes.SessionClosed, ex.ErrorCode);
    }

    /// <summary>★ Gọi commit lại lô ĐÃ nạp: trả kết quả lần đầu, KHÔNG ghi thêm hồ sơ nào.</summary>
    [Fact]
    public async Task Commit_lai_lo_da_nap_tra_AlreadyCommitted_va_khong_ghi_them()
    {
        using var db = new InMemoryTestDb();
        var session = db.SeedSession();
        var batch = db.SeedImportBatch(session.SessionID, ImportBatchState.Completed,
            rows: new[] { (2, true, """{"FullName":"A"}""") });

        var result = await db.Imports.CommitAsync(batch.BatchID, new ImportCommitRequest(), 1, 50);

        Assert.True(result.AlreadyCommitted);
        Assert.Empty(db.Db.ExamRecords);
    }

    /// <summary>
    /// Mặc định KHÔNG bỏ qua dòng hỏng. Người dùng phải tự chọn — mặc định bỏ qua thì bấm
    /// nhầm nút xác nhận là 40 người biến mất khỏi danh sách mà không ai nhận ra.
    /// </summary>
    [Fact]
    public async Task Con_dong_hong_ma_khong_chon_bo_qua_thi_tu_choi_4001()
    {
        using var db = new InMemoryTestDb();
        var session = db.SeedSession();
        var batch = db.SeedImportBatch(session.SessionID,
            rows: new[] { (2, true, """{"FullName":"A"}"""), (3, false, """{"FullName":""}""") });

        var ex = await Assert.ThrowsAsync<HealthExamException>(
            () => db.Imports.CommitAsync(batch.BatchID, new ImportCommitRequest(), 1, 50));

        Assert.Equal(ErrorCodes.BadRequest, ex.ErrorCode);
        Assert.Empty(db.Db.ExamRecords);
    }

    [Fact]
    public async Task Lo_qua_han_thi_tu_choi_commit()
    {
        using var db = new InMemoryTestDb();
        var session = db.SeedSession();
        var batch = db.SeedImportBatch(session.SessionID,
            startedAt: DateTime.UtcNow - ImportBatch.BatchLifetime - TimeSpan.FromMinutes(1),
            rows: new[] { (2, true, """{"FullName":"A"}""") });

        var ex = await Assert.ThrowsAsync<HealthExamException>(
            () => db.Imports.CommitAsync(batch.BatchID, new ImportCommitRequest(), 1, 50));

        Assert.Equal(ErrorCodes.InvalidState, ex.ErrorCode);
    }

    [Fact]
    public async Task Lo_da_bo_thi_khong_nap_lai_duoc()
    {
        using var db = new InMemoryTestDb();
        var session = db.SeedSession();
        var batch = db.SeedImportBatch(session.SessionID, ImportBatchState.Discarded,
            rows: new[] { (2, true, """{"FullName":"A"}""") });

        var ex = await Assert.ThrowsAsync<HealthExamException>(
            () => db.Imports.CommitAsync(batch.BatchID, new ImportCommitRequest(), 1, 50));

        Assert.Equal(ErrorCodes.InvalidState, ex.ErrorCode);
    }

    [Fact]
    public async Task Lo_da_nap_xong_thi_khong_bo_duoc()
    {
        using var db = new InMemoryTestDb();
        var session = db.SeedSession();
        var batch = db.SeedImportBatch(session.SessionID, ImportBatchState.Completed);

        var ex = await Assert.ThrowsAsync<HealthExamException>(() => db.Imports.DiscardAsync(batch.BatchID));

        Assert.Equal(ErrorCodes.InvalidState, ex.ErrorCode);
    }

    /// <summary>
    /// ★ Chốt chặn tenant. ImportID là Guid đoán không ra, nhưng đoán-không-ra chưa bao giờ là
    /// chốt chặn: pilot dùng chung DB với prod, một Guid rò ra là đọc được nguyên danh sách
    /// người khám của đơn vị khác.
    /// </summary>
    [Fact]
    public async Task Lo_cua_tenant_khac_khong_doc_duoc()
    {
        using var db = new InMemoryTestDb();
        var session = db.SeedSession();
        var batch = db.SeedImportBatch(session.SessionID);
        db.Ctx.DivisionId = "TENANT-KHAC";

        var ex = await Assert.ThrowsAsync<HealthExamException>(
            () => db.Imports.GetAsync(batch.BatchID, 1, 50, false));

        Assert.Equal(ErrorCodes.NotFound, ex.ErrorCode);
    }

    /// <summary>Màn preview gần như luôn hỏi "chỉ cho tôi xem dòng hỏng".</summary>
    [Fact]
    public async Task Loc_duoc_chi_dong_hong()
    {
        using var db = new InMemoryTestDb();
        var session = db.SeedSession();
        var batch = db.SeedImportBatch(session.SessionID,
            rows: new[] { (2, true, """{"FullName":"A"}"""), (3, false, """{"FullName":""}""") });

        var all = await db.Imports.GetAsync(batch.BatchID, 1, 50, onlyInvalid: false);
        var bad = await db.Imports.GetAsync(batch.BatchID, 1, 50, onlyInvalid: true);

        Assert.Equal(2, all.Rows.Total);
        Assert.Equal(1, bad.Rows.Total);
        Assert.Equal(3, bad.Rows.Items[0].Row);
    }
    // ================================================================ F4 — cổng NB nhìn thấy được

    /// <summary>
    /// F4 ý (3): dòng thiếu CẢ CCCD lẫn BHYT VẪN ĐẠT — hồ sơ chỉ đến khám tại quầy là nghiệp
    /// vụ hợp lệ — nhưng phải mang cảnh báo. Im lặng thì đơn vị tưởng cả 500 người tra cứu
    /// được, tới lúc người bệnh gọi tổng đài mới vỡ ra.
    /// </summary>
    [Fact]
    public async Task Dong_thieu_ca_CCCD_lan_BHYT_van_dat_nhung_mang_canh_bao()
    {
        using var db = new InMemoryTestDb();
        var session = db.SeedSession();
        var batch = db.SeedImportBatch(session.SessionID,
            rows: new[] { (2, true, """{"FullName":"Khong co gi"}""") });

        var result = await db.Imports.GetAsync(batch.BatchID, 1, 50, onlyInvalid: false);
        var row = result.Rows.Items[0];

        Assert.Equal(ImportRowStatus.Valid, row.Status);
        Assert.Empty(row.Errors);
        Assert.Equal(new[] { ImportRowWarnings.NoPortalCredential }, row.Warnings);
        Assert.Equal(1, result.NoPortalCredentialRows);
    }

    /// <summary>Một trong hai dữ kiện là đủ để đăng nhập — không được kêu oan.</summary>
    [Theory]
    [InlineData("""{"FullName":"Co CCCD","IdentityNumber":"079..."}""")]
    [InlineData("""{"FullName":"Co BHYT","InsuranceNumber":"HC4..."}""")]
    public async Task Co_mot_trong_hai_du_kien_thi_khong_canh_bao(string raw)
    {
        using var db = new InMemoryTestDb();
        var session = db.SeedSession();
        var batch = db.SeedImportBatch(session.SessionID, rows: new[] { (2, true, raw) });

        var result = await db.Imports.GetAsync(batch.BatchID, 1, 50, onlyInvalid: false);

        Assert.Empty(result.Rows.Items[0].Warnings);
        Assert.Equal(0, result.NoPortalCredentialRows);
    }

    /// <summary>
    /// Dòng HỎNG không sinh hồ sơ, nên "không đăng nhập được cổng NB" chưa phải chuyện của
    /// nó. Đếm cả dòng hỏng là báo một con số lớn hơn số người thật sự bị ảnh hưởng.
    /// </summary>
    [Fact]
    public async Task Dong_hong_khong_bi_dem_vao_so_khong_dang_nhap_duoc()
    {
        using var db = new InMemoryTestDb();
        var session = db.SeedSession();
        var batch = db.SeedImportBatch(session.SessionID,
            rows: new[] { (2, false, """{"FullName":""}"""), (3, true, """{"FullName":"B"}""") });

        var result = await db.Imports.GetAsync(batch.BatchID, 1, 50, onlyInvalid: false);

        Assert.Equal(1, result.NoPortalCredentialRows);
        Assert.Empty(result.Rows.Items[0].Warnings);
        Assert.Equal(new[] { ImportRowWarnings.NoPortalCredential }, result.Rows.Items[1].Warnings);
    }

    /// <summary>Đếm trên TOÀN lô, không trên trang đang xem — màn xác nhận chỉ hiện trang đầu.</summary>
    [Fact]
    public async Task So_khong_dang_nhap_duoc_dem_tren_toan_lo_khong_theo_trang()
    {
        using var db = new InMemoryTestDb();
        var session = db.SeedSession();
        var batch = db.SeedImportBatch(session.SessionID,
            rows: Enumerable.Range(2, 5).Select(i => (i, true, $$"""{"FullName":"Nguoi {{i}}"}""")).ToArray());

        var page1 = await db.Imports.GetAsync(batch.BatchID, 1, 2, onlyInvalid: false);

        Assert.Equal(2, page1.Rows.Items.Count);
        Assert.Equal(5, page1.NoPortalCredentialRows);
    }

    // ================================================================ §3b review lần 2

    /// <summary>
    /// §3b(i): lô ĐANG GHI thì không bỏ được. Không chặn thì DELETE trả 200, đặt Discarded,
    /// rồi lượt commit đang chạy ghi đè Completed ở cuối — người dùng thấy "đã bỏ" mà hồ sơ
    /// vẫn được tạo đủ.
    /// </summary>
    [Fact]
    public async Task Lo_dang_ghi_thi_khong_bo_duoc()
    {
        using var db = new InMemoryTestDb();
        var session = db.SeedSession();
        var batch = db.SeedImportBatch(session.SessionID, ImportBatchState.Committing);
        db.TouchImportBatch(batch.BatchID, DateTime.UtcNow);

        var ex = await Assert.ThrowsAsync<HealthExamException>(() => db.Imports.DiscardAsync(batch.BatchID));

        Assert.Equal(ErrorCodes.InvalidState, ex.ErrorCode);
        Assert.Contains("đang được ghi", ex.Message);
    }

    /// <summary>
    /// …nhưng lô MỒ CÔI (pod chết, quá ngưỡng StaleCommitAfter) thì phải bỏ được, nếu không
    /// nó nằm kẹt tới hết hạn 60 phút mà không ai gỡ ra nổi.
    /// </summary>
    [Fact]
    public async Task Lo_mo_coi_qua_nguong_thi_van_bo_duoc()
    {
        using var db = new InMemoryTestDb();
        var session = db.SeedSession();
        var batch = db.SeedImportBatch(session.SessionID, ImportBatchState.Committing);
        db.TouchImportBatch(batch.BatchID,
            DateTime.UtcNow - ImportBatch.StaleCommitAfter - TimeSpan.FromMinutes(1));

        await db.Imports.DiscardAsync(batch.BatchID);

        Assert.Equal(ImportBatchState.Discarded, db.Db.ImportBatches.Single().State);
        // Dòng nhật ký phải ghi trạng thái NGUỒN THẬT, không ghi cứng Pending — nếu không thì
        // tra lại cũng không thấy được chuyện gì đã xảy ra.
        var audit = db.AuditRowsOf(batch.BatchID).Last();
        Assert.Equal((short)ImportBatchState.Committing, audit.FromState);
        Assert.Equal((short)ImportBatchState.Discarded, audit.ToState);
    }

    /// <summary>
    /// §3b(ii): dòng hỏng Ở PHA 2 KHÔNG được chặn lượt commit chạy lại. Cờ SkipInvalidRows là
    /// câu trả lời cho "file của anh có dòng hỏng" — một câu hỏi về nội dung file, mà người
    /// dùng đã trả lời ở lượt commit đầu tiên.
    /// </summary>
    [Theory]
    [InlineData("WRITE_FAILED", true)]
    [InlineData("DUPLICATE_AT_COMMIT", true)]
    [InlineData("REQUIRED", false)]
    [InlineData("TOO_LONG", false)]
    [InlineData("DUPLICATE", false)]
    public void Chi_ma_loi_cua_pha_2_moi_khong_chan_commit_chay_lai(string code, bool commitPhase)
        => Assert.Equal(commitPhase, ImportRowErrors.IsCommitPhase(code));

    /// <summary>
    /// Lô đã có dòng mang RecordID = một lượt commit ĐÃ chạy và tạo hồ sơ thật ⇒ không chặn
    /// nữa. Đây là lối thoát thứ hai, bịt lỗ của lối thứ nhất khi lượt trước chết trước cả
    /// khi kịp đánh dấu dòng nào.
    /// </summary>
    [Fact]
    public async Task Lo_dang_chay_lai_khong_bi_chan_boi_dong_hong_cua_luot_truoc()
    {
        using var db = new InMemoryTestDb();
        var session = db.SeedSession();
        var batch = db.SeedImportBatch(session.SessionID,
            rows: new[] { (2, true, """{"FullName":"A"}"""), (3, false, """{"FullName":"B"}""") });

        // Dòng 2 đã tạo hồ sơ ở lượt trước; dòng 3 hỏng ngay tại pha 2.
        db.MarkImportRow(batch.BatchID, 2, recordId: Guid.NewGuid());
        db.MarkImportRow(batch.BatchID, 3, errorCode: ImportRowErrors.WriteFailed);

        // Provider in-memory không chạy được câu SQL giành lô, nên phép kiểm nằm TRƯỚC nó là
        // thứ duy nhất chốt được ở đây: lỗi ném ra KHÔNG được là 4001 "còn dòng không đạt".
        var ex = await Assert.ThrowsAnyAsync<Exception>(
            () => db.Imports.CommitAsync(batch.BatchID, new ImportCommitRequest(), 1, 50));

        Assert.False(ex is HealthExamException { ErrorCode: ErrorCodes.BadRequest },
            "Lượt commit chạy lại bị chặn bởi dòng hỏng của chính lượt trước");
    }

    // ================================================================ F4 — mã NB tự sinh

    /// <summary>
    /// F4 ý (1): mã NB tự sinh suy từ RecordCode — mã duy nhất trong tenant, có sẵn ngay lúc
    /// chèn, và trong cùng một đợt chỉ khác nhau ở số thứ tự nên lưới UNIQUE không bị đụng.
    /// </summary>
    [Fact]
    public void Ma_NB_tu_sinh_mang_tien_to_phan_biet_duoc()
        => Assert.Equal("HEX-KSK2608-0001-0007",
            ExamRecordService.ComposeGeneratedPatientCode("KSK2608-0001-0007"));

    /// <summary>
    /// ❗ Vượt trần thì KHÔNG sinh, và tuyệt đối không cắt bớt: cắt là mở đường cho hai người
    /// khác nhau nhận cùng một mã, tức người bệnh A đăng nhập ra hồ sơ người bệnh B.
    /// </summary>
    [Fact]
    public void Ma_NB_tu_sinh_vuot_tran_thi_tra_null_chu_khong_cat_bot()
    {
        var tooLong = new string('S', RecordFieldLengths.PatientCode);
        Assert.Null(ExamRecordService.ComposeGeneratedPatientCode(tooLong));
        // Sát trần thì vẫn phải sinh được — đừng chặn nhầm cả trường hợp vừa đủ.
        var justFits = new string('S', RecordFieldLengths.PatientCode
                                       - ExamRecordService.GeneratedPatientCodePrefix.Length);
        Assert.Equal(RecordFieldLengths.PatientCode,
            ExamRecordService.ComposeGeneratedPatientCode(justFits).Length);
    }

    /// <summary>
    /// ❗ D6 — CHỖ NỐI DÂY của F4, không phải hàm thuần.
    ///
    /// Vòng review 3 gỡ CẢ HAI lời gọi <c>FillGeneratedPatientCode</c> mà 197/197 vẫn xanh:
    /// F4 có thể lặng lẽ biến mất khỏi sản phẩm và CI không nói gì, triệu chứng chỉ lộ ra khi
    /// người bệnh gọi tổng đài vì không đăng nhập được cổng NB. Test này giữ đường đi qua
    /// <see cref="ExamRecordService.CreateAsync"/> có <c>importBatchId</c>.
    ///
    /// ⚠️ ĐỌC KỸ TRƯỚC KHI TIN TEST NÀY: nó đi nhánh CLIENT TỰ TRUYỀN RecordCode
    /// (<c>CreateAsync</c> nhánh <c>else</c>), mà **dòng Excel thật KHÔNG đi nhánh đó** —
    /// <c>ExamImportService.ToSaveRequest</c> không bao giờ đặt <c>RecordCode</c>, nên mọi
    /// dòng nạp từ file rơi vào nhánh cấp mã tự động. Ở đây phải truyền mã vì nhánh kia dùng
    /// transaction + SAVEPOINT + SQL thô, provider in-memory không có (xem chú thích giới hạn
    /// ở InMemoryTestDb).
    ///
    /// Vì thế test này KHÔNG đủ để chốt F4: gỡ lời gọi FillGeneratedPatientCode ở nhánh cấp
    /// mã thì nó vẫn xanh. Nhánh thật được ghim ở
    /// <see cref="ExamRecordAllocationPostgresTests"/> — chạy trên PostgreSQL, bật bằng biến
    /// môi trường <c>HEALTHEXAM_TEST_DB</c>. Đã đo: gỡ lời gọi đó thì bộ Postgres đỏ đúng 1 test.
    /// </summary>
    [Fact]
    public async Task Ho_so_tu_lo_Excel_khong_khai_ma_NB_thi_duoc_cap_ma_tu_sinh()
    {
        using var db = new InMemoryTestDb();
        var session = db.SeedSession();
        var batch = db.SeedImportBatch(session.SessionID);

        var item = await db.Records.CreateAsync(
            new ExamRecordSaveRequest
            {
                SessionID = session.SessionID,
                RecordCode = "DK-2026-001-0007",
                FullName = "Nguyễn Văn A",
                VariantCode = "DTK_01"
            },
            default,
            batch.BatchID);

        Assert.Equal("HEX-DK-2026-001-0007", item.PatientCode);
    }

    /// <summary>
    /// Mặt kia của cùng chỗ nối: mã NB người dùng KHAI trong file phải giữ nguyên. Thiếu vế
    /// này thì một bản vá "cứ sinh cho chắc" vẫn xanh, mà nó ghi đè mã thật của bệnh viện.
    /// </summary>
    [Fact]
    public async Task Ho_so_tu_lo_Excel_co_khai_ma_NB_thi_giu_nguyen()
    {
        using var db = new InMemoryTestDb();
        var session = db.SeedSession();
        var batch = db.SeedImportBatch(session.SessionID);

        var item = await db.Records.CreateAsync(
            new ExamRecordSaveRequest
            {
                SessionID = session.SessionID,
                RecordCode = "DK-2026-001-0008",
                PatientCode = "NB100",
                FullName = "Nguyễn Văn B",
                VariantCode = "DTK_01"
            },
            default,
            batch.BatchID);

        Assert.Equal("NB100", item.PatientCode);
    }

    /// <summary>
    /// Vế thứ ba: hồ sơ đăng ký TAY (không có lô nạp) KHÔNG được cấp mã bịa. Mã "HEX-…" là
    /// mã không có trong danh mục người bệnh của HIS; sinh nó cho một người đến quầy đăng ký
    /// là bơm dữ liệu rác vào đúng chỗ sau này phải đối chiếu với danh mục thật.
    /// </summary>
    [Fact]
    public async Task Ho_so_dang_ky_tay_khong_bi_cap_ma_NB_tu_sinh()
    {
        using var db = new InMemoryTestDb();
        var session = db.SeedSession();

        var item = await db.Records.CreateAsync(
            new ExamRecordSaveRequest
            {
                SessionID = session.SessionID,
                RecordCode = "DK-2026-001-0009",
                FullName = "Nguyễn Văn C",
                VariantCode = "DTK_01"
            });

        Assert.True(string.IsNullOrEmpty(item.PatientCode),
            $"Hồ sơ đăng ký tay bị cấp mã NB tự sinh: \"{item.PatientCode}\"");
    }

    // ================================================================ D4 — nhãn dòng trùng pha 2

    /// <summary>
    /// ❗ D4 (review lần 3 MR !4): dòng trùng phát hiện ở PHA 2 phải hiện là "Duplicate".
    ///
    /// Bản vá §3b(ii) tách <c>DUPLICATE_AT_COMMIT</c> khỏi <c>DUPLICATE</c> ở tầng dữ liệu
    /// (để <c>IsCommitPhase</c> phân biệt được pha 1 với pha 2), nhưng bộ ánh xạ sang DTO
    /// không đổi theo — dòng rơi vào "Invalid". Người vận hành mất đúng cái nhãn nói ra lý do
    /// thật: "người này đã có hồ sơ trong đợt", không phải "anh gõ sai một ô".
    /// </summary>
    [Fact]
    public async Task Dong_trung_o_pha_2_hien_la_Duplicate_chu_khong_phai_Invalid()
    {
        using var db = new InMemoryTestDb();
        var session = db.SeedSession();
        var batch = db.SeedImportBatch(session.SessionID, ImportBatchState.Failed,
            rows: new[] { (2, true, "{\"FullName\":\"Nguyễn Văn A\",\"PatientCode\":\"NB001\"}") });
        db.MarkImportRow(batch.BatchID, 2, errorCode: ImportRowErrors.DuplicateAtCommit);

        var result = await db.Imports.GetAsync(batch.BatchID, 1, 50, onlyInvalid: false);

        Assert.Equal(ImportRowStatus.Duplicate, result.Rows.Items[0].Status);
    }

    /// <summary>
    /// Ranh giới của bản vá trên: <c>WRITE_FAILED</c> phải Ở LẠI "Invalid". Cùng nằm trong
    /// <see cref="ImportRowErrors.IsCommitPhase"/> nên rất dễ bị gộp chung một phép kiểm,
    /// nhưng nó là "pha 1 còn thiếu một luật" — việc của người viết code. Dán nhãn Duplicate
    /// lên đó là đẩy một lỗi của hệ thống sang cho người gửi file đi tìm.
    /// </summary>
    [Fact]
    public async Task Dong_hong_luc_ghi_van_hien_la_Invalid()
    {
        using var db = new InMemoryTestDb();
        var session = db.SeedSession();
        var batch = db.SeedImportBatch(session.SessionID, ImportBatchState.Failed,
            rows: new[] { (2, true, "{\"FullName\":\"Nguyễn Văn A\"}") });
        db.MarkImportRow(batch.BatchID, 2, errorCode: ImportRowErrors.WriteFailed);

        var result = await db.Imports.GetAsync(batch.BatchID, 1, 50, onlyInvalid: false);

        Assert.Equal(ImportRowStatus.Invalid, result.Rows.Items[0].Status);
    }
}
