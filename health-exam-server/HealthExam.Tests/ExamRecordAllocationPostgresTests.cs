using HealthExam.Domain.Common;
using HealthExam.Domain.ExamSessions;
using HealthExam.Domain.ExamRecords;
using HealthExam.Domain.Imports;
using HealthExam.Domain.Paraclinical;
using HealthExam.Domain.Webhooks;
using HealthExam.Domain.Integrations;
using HealthExam.Domain.Catalogs;
using HealthExam.API.Contracts;
using HealthExam.API.Middlewares;
using HealthExam.Application.Common;
using HealthExam.Server.Service;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using OfficeOpenXml;
using Xunit;
using ImportCommitRequest = HealthExam.API.Contracts.ImportCommitRequest;

namespace HealthExam.Tests;

/// <summary>
/// D6 — ghim ĐÚNG nhánh mà dòng Excel thật đi qua.
///
/// Ba test D6 trong <see cref="ExamImportTests"/> đều truyền sẵn <c>RecordCode</c>, nên chúng
/// ghim nhánh "client tự truyền mã" (<c>ExamRecordService.CreateAsync</c> nhánh <c>else</c>).
/// Nhưng <c>ExamImportService.ToSaveRequest</c> KHÔNG BAO GIỜ đặt <c>RecordCode</c> — grep cả
/// file được 0 lần — nên mọi dòng Excel đều rơi vào nhánh còn lại: cấp mã bằng
/// <c>UPDATE ... RETURNING</c> trong transaction có SAVEPOINT.
///
/// Hệ quả trước bộ test này: gỡ hẳn lời gọi <c>FillGeneratedPatientCode</c> ở nhánh cấp mã thì
/// toàn bộ test vẫn xanh, mà cổng người bệnh thì không ai đăng nhập được.
///
/// Nhánh đó không dựng được trên provider in-memory (không transaction, không SAVEPOINT,
/// không SQL thô), nên nhóm này chạy trên PostgreSQL thật — xem <see cref="PostgresTestDb"/>
/// để biết cách bật.
/// </summary>
[Collection("PostgresTestDb")]
public class ExamRecordAllocationPostgresTests
{
    /// <summary>
    /// ❗ Đường đi THẬT của một dòng Excel, từ file tới hồ sơ: Upload → Commit → cấp mã.
    ///
    /// Không gọi thẳng CreateAsync mà đi qua ExamImportService là cố ý — chính chỗ nối
    /// import ↔ cấp mã là chỗ vòng review 3 gỡ mất mà không test nào bắt.
    /// </summary>
    [Fact]
    public async Task Dong_Excel_that_di_qua_nhanh_cap_ma_va_duoc_cap_ma_NB_tu_sinh()
    {
        if (!PostgresTestDb.Enabled) return;

        using var db = new PostgresTestDb();
        var session = db.SeedSession();

        using var file = Workbook(new[] { "FullName" }, new[] { "Nguyễn Văn A" }, new[] { "Trần Thị B" });
        var batch = await db.Imports.UploadAsync(session.SessionID, file, "DS.xlsx", 1, 50);
        var committed = await db.Imports.CommitAsync(batch.ImportID, new ImportCommitRequest(), 1, 50);

        Assert.Equal(2, committed.CreatedRecordCount);

        var records = await db.Db.ExamRecords
            .Include(x => x.Patient)
            .Where(x => x.SessionID == session.SessionID)
            .OrderBy(x => x.RecordCode)
            .ToListAsync();

        // Mã hồ sơ do BỘ ĐẾM CỦA ĐỢT cấp, không phải do file mang sang.
        Assert.Equal(new[] { "DK-2026-001-0001", "DK-2026-001-0002" },
            records.Select(x => x.RecordCode).ToArray());

        // F4: mã NB tự sinh bám theo mã hồ sơ cuối cùng — đây là thứ ba test D6 cũ tưởng là
        // đang chốt, nhưng chốt ở nhánh không ai đi.
        Assert.Equal(new[] { "HEX-DK-2026-001-0001", "HEX-DK-2026-001-0002" },
            records.Select(x => x.Patient?.PatientCode).ToArray());

        // Bộ đếm phải nhích đúng bằng số hồ sơ đã cấp, không nhiều hơn.
        var lastNo = await db.Db.ExamSessions
            .Where(x => x.SessionID == session.SessionID).Select(x => x.LastRecordNo).SingleAsync();
        Assert.Equal(2, lastNo);
    }

    /// <summary>
    /// Mặt kia: mã NB người dùng KHAI trong file phải giữ nguyên, kể cả khi đi nhánh cấp mã.
    /// Thiếu vế này thì một bản vá "cứ sinh cho chắc" vẫn xanh mà nó ghi đè mã thật của
    /// bệnh viện — mã đó là thứ sau này phải đối chiếu với danh mục người bệnh của HIS.
    /// </summary>
    [Fact]
    public async Task Dong_Excel_co_khai_ma_NB_thi_giu_nguyen()
    {
        if (!PostgresTestDb.Enabled) return;

        using var db = new PostgresTestDb();
        var session = db.SeedSession();

        using var file = Workbook(new[] { "FullName", "PatientCode" }, new[] { "Nguyễn Văn A", "NB100" });
        var batch = await db.Imports.UploadAsync(session.SessionID, file, "DS.xlsx", 1, 50);
        await db.Imports.CommitAsync(batch.ImportID, new ImportCommitRequest(), 1, 50);

        var record = await db.Db.ExamRecords.Include(x => x.Patient).SingleAsync(x => x.SessionID == session.SessionID);
        Assert.Equal("NB100", record.Patient?.PatientCode);
        Assert.Equal("DK-2026-001-0001", record.RecordCode);
    }

    /// <summary>
    /// Hồ sơ đăng ký TAY (không có lô nạp) đi CÙNG nhánh cấp mã nhưng KHÔNG được cấp mã NB
    /// bịa: "HEX-…" là mã không có trong danh mục người bệnh của HIS.
    ///
    /// Vế này bộ in-memory có, nhưng ở nhánh else. Ghim lại ở đây để hai vế nằm trên cùng
    /// một nhánh, nếu không thì "có lô thì sinh, không lô thì không" vẫn có thể đúng ở nhánh
    /// này và sai ở nhánh kia.
    /// </summary>
    [Fact]
    public async Task Dang_ky_tay_di_nhanh_cap_ma_thi_khong_bi_cap_ma_NB_tu_sinh()
    {
        if (!PostgresTestDb.Enabled) return;

        using var db = new PostgresTestDb();
        var session = db.SeedSession();

        var item = await db.Records.CreateAsync(new ExamRecordSaveRequest
        {
            SessionID = session.SessionID,
            FullName = "Nguyễn Văn C",
            VariantCode = "DTK_01"
        });

        Assert.Equal("DK-2026-001-0001", item.RecordCode);
        Assert.True(string.IsNullOrEmpty(item.PatientCode),
            $"Hồ sơ đăng ký tay bị cấp mã NB tự sinh: \"{item.PatientCode}\"");
    }

    /// <summary>
    /// ❗ Mã do client tự truyền CHIẾM CHỖ của dãy tự sinh, và nhánh cấp mã phải nhảy qua chứ
    /// không được hỏng. Đây là lý do tồn tại của vòng lặp SAVEPOINT — thứ duy nhất không mô
    /// phỏng được ở in-memory (không có chỉ mục UNIQUE thì không có gì để đụng).
    /// </summary>
    [Fact]
    public async Task Ma_bi_chiem_thi_cap_lai_so_moi_chu_khong_hong()
    {
        if (!PostgresTestDb.Enabled) return;

        using var db = new PostgresTestDb();
        var session = db.SeedSession();

        // Người nhập tay chiếm sẵn đúng mã mà dãy tự sinh sắp cấp.
        await db.Records.CreateAsync(new ExamRecordSaveRequest
        {
            SessionID = session.SessionID,
            RecordCode = "DK-2026-001-0001",
            FullName = "Người nhập tay",
            VariantCode = "DTK_01"
        });

        var auto = await db.Records.CreateAsync(new ExamRecordSaveRequest
        {
            SessionID = session.SessionID,
            FullName = "Nguyễn Văn A",
            VariantCode = "DTK_01"
        });

        // Không phải -0001 (đã bị chiếm), và cũng không được ném lỗi.
        Assert.NotEqual("DK-2026-001-0001", auto.RecordCode);
        Assert.StartsWith("DK-2026-001-", auto.RecordCode);
    }

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
}

/// <summary>
/// Canh chính bộ test trên. Bỏ qua là im lặng: đặt sai chuỗi kết nối trong CI thì cả nhóm
/// PostgreSQL lặng lẽ không chạy và bảng kết quả vẫn "toàn xanh" — đúng loại lỗi mà D6 là
/// nạn nhân. Test này luôn chạy và nói to khi cấu hình hỏng.
/// </summary>
public class PostgresFixtureGuardTests
{
    [Fact]
    public void Bien_moi_truong_da_dat_thi_phai_noi_duoc_toi_DB()
    {
        if (!PostgresTestDb.Enabled) return;   // chưa bật thì không có gì để canh

        var ex = Record.Exception(() =>
        {
            using var conn = new NpgsqlConnection(PostgresTestDb.BaseConnectionString);
            conn.Open();
        });

        Assert.True(ex is null,
            $"{PostgresTestDb.ConnectionEnv} đã đặt nhưng không nối được — nhóm test PostgreSQL "
            + $"đang bị bỏ qua trong im lặng. Lỗi: {ex?.Message}");
    }
}
