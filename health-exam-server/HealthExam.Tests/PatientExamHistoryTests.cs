using System;
using System.Linq;
using System.Threading.Tasks;
using HealthExam.Application.ExamRecords;
using HealthExam.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HealthExam.Tests;

/// <summary>
/// Danh sách "Đợt khám trước": trạng thái ký kết luận trên kết quả, phân giải dòng hồ sơ
/// người bệnh, và truy vấn hồ sơ đã ký. Chạy trên InMemory provider — đủ để chốt NHÁNH LINQ
/// (lineage, lọc Signed, thứ tự, phân trang); ràng buộc DB thật không thuộc phạm vi ở đây.
/// </summary>
public sealed class PatientExamHistoryTests
{
    [Fact]
    public async Task Ket_qua_ho_so_mang_theo_trang_thai_gio_ky_va_duong_dan_file_ky()
    {
        using var db = new InMemoryTestDb();
        var session = db.SeedSession();
        var signedAt = new DateTime(2026, 3, 14, 9, 12, 0, DateTimeKind.Utc);
        db.SeedRecord(session.SessionID, recordCode: "KSK-001",
            hisSignStatus: "Signed", hisSignedAt: signedAt,
            hisSignedFilePath: "/Signed/2026/03/KSK-001.pdf");

        var repo = new ExamRecordRepository(db.Db);
        var page = await repo.ListAsync(FakeHealthExamContext.DefaultDivisionId, new ExamRecordFilter());

        var item = Assert.Single(page.Items);
        Assert.Equal("Signed", item.HisSignStatus);
        Assert.Equal(signedAt, item.HisSignedAt);
        Assert.Equal("/Signed/2026/03/KSK-001.pdf", item.HisSignedFilePath);
    }

    [Fact]
    public async Task Ho_so_chua_ky_tra_chuoi_rong_va_gio_ky_null()
    {
        using var db = new InMemoryTestDb();
        var session = db.SeedSession();
        db.SeedRecord(session.SessionID, recordCode: "KSK-002");

        var repo = new ExamRecordRepository(db.Db);
        var page = await repo.ListAsync(FakeHealthExamContext.DefaultDivisionId, new ExamRecordFilter());

        var item = Assert.Single(page.Items);
        Assert.Equal("", item.HisSignStatus);
        Assert.Null(item.HisSignedAt);
        Assert.Equal("", item.HisSignedFilePath);
    }

    [Fact]
    public async Task Lineage_tra_ve_ca_khi_phien_ban_da_bi_thay_the()
    {
        using var db = new InMemoryTestDb();
        var session = db.SeedSession();
        var lineageID = Guid.NewGuid();
        var oldVersion = db.SeedRecord(session.SessionID, recordCode: "KSK-001",
            profileLineageID: lineageID);

        // Phiên bản cũ: đúng tình huống hồ sơ khám năm ngoái trỏ vào bản đã bị thay thế.
        var tracked = db.Db.Patients.AsTracking()
            .Single(p => p.PatientRefID == oldVersion.PatientRefID.Value);
        tracked.IsActive = false;
        db.Db.SaveChanges();
        db.Db.ChangeTracker.Clear();

        var repo = new PatientRepository(db.Db);
        var found = await repo.FindProfileLineageIdAsync(
            FakeHealthExamContext.DefaultDivisionId, oldVersion.PatientRefID.Value);

        Assert.Equal(lineageID, found);
    }

    [Fact]
    public async Task Lineage_khong_vuot_don_vi_va_tra_null_khi_khong_co()
    {
        using var db = new InMemoryTestDb();
        var session = db.SeedSession(divisionId: "D_OTHER");
        var other = db.SeedRecord(session.SessionID, recordCode: "KSK-001", divisionId: "D_OTHER");

        var repo = new PatientRepository(db.Db);

        Assert.Null(await repo.FindProfileLineageIdAsync(
            FakeHealthExamContext.DefaultDivisionId, other.PatientRefID.Value));
        Assert.Null(await repo.FindProfileLineageIdAsync(
            FakeHealthExamContext.DefaultDivisionId, Guid.NewGuid()));
        Assert.Null(await repo.FindProfileLineageIdAsync(
            FakeHealthExamContext.DefaultDivisionId, Guid.Empty));
    }

    [Fact]
    public async Task Chi_tra_ho_so_da_ky_ket_luan()
    {
        using var db = new InMemoryTestDb();
        var session = db.SeedSession();
        var lineageID = Guid.NewGuid();
        db.SeedRecord(session.SessionID, recordCode: "KSK-SIGNED",
            profileLineageID: lineageID, hisSignStatus: "Signed",
            hisSignedAt: new DateTime(2026, 3, 14, 9, 0, 0, DateTimeKind.Utc));
        db.SeedRecord(session.SessionID, recordCode: "KSK-INPROGRESS",
            profileLineageID: lineageID, hisSignStatus: "InProcessing");
        db.SeedRecord(session.SessionID, recordCode: "KSK-UNSIGNED",
            profileLineageID: lineageID);

        var repo = new ExamRecordRepository(db.Db);
        var page = await repo.ListSignedByPatientLineageAsync(
            FakeHealthExamContext.DefaultDivisionId, lineageID, 1, 5);

        Assert.Equal(1, page.Total);
        Assert.Equal("KSK-SIGNED", Assert.Single(page.Items).RecordCode);
    }

    [Fact]
    public async Task Gop_ho_so_kham_cua_moi_phien_ban_cung_dong()
    {
        using var db = new InMemoryTestDb();
        var lineageID = Guid.NewGuid();
        var older = db.SeedSession(sessionCode: "DK-2025", examDate: new DateOnly(2025, 4, 1));
        var newer = db.SeedSession(sessionCode: "DK-2026", examDate: new DateOnly(2026, 4, 1));

        // Hai ĐỢT khác nhau, hai PHIÊN BẢN hồ sơ khác nhau, cùng một dòng — cùng một người.
        db.SeedRecord(older.SessionID, recordCode: "KSK-2025-0001",
            profileLineageID: lineageID, hisSignStatus: "Signed",
            hisSignedAt: new DateTime(2025, 4, 1, 10, 0, 0, DateTimeKind.Utc));
        db.SeedRecord(newer.SessionID, recordCode: "KSK-2026-0001",
            profileLineageID: lineageID, hisSignStatus: "Signed",
            hisSignedAt: new DateTime(2026, 4, 1, 10, 0, 0, DateTimeKind.Utc));

        // Người khác, dòng khác: không được lọt vào.
        db.SeedRecord(newer.SessionID, recordCode: "KSK-2026-0002",
            profileLineageID: Guid.NewGuid(), hisSignStatus: "Signed",
            hisSignedAt: new DateTime(2026, 4, 1, 11, 0, 0, DateTimeKind.Utc));

        var repo = new ExamRecordRepository(db.Db);
        var page = await repo.ListSignedByPatientLineageAsync(
            FakeHealthExamContext.DefaultDivisionId, lineageID, 1, 5);

        Assert.Equal(2, page.Total);
        // Mới nhất lên đầu, theo ngày khám của đợt.
        Assert.Equal(new[] { "KSK-2026-0001", "KSK-2025-0001" },
            page.Items.Select(x => x.RecordCode).ToArray());
    }

    [Fact]
    public async Task Khong_tra_ho_so_cua_don_vi_khac()
    {
        using var db = new InMemoryTestDb();
        var lineageID = Guid.NewGuid();
        var session = db.SeedSession(divisionId: "D_OTHER");
        db.SeedRecord(session.SessionID, recordCode: "KSK-OTHER", divisionId: "D_OTHER",
            profileLineageID: lineageID, hisSignStatus: "Signed",
            hisSignedAt: new DateTime(2026, 4, 1, 10, 0, 0, DateTimeKind.Utc));

        var repo = new ExamRecordRepository(db.Db);
        var page = await repo.ListSignedByPatientLineageAsync(
            FakeHealthExamContext.DefaultDivisionId, lineageID, 1, 5);

        Assert.Equal(0, page.Total);
        Assert.Empty(page.Items);
    }

    [Fact]
    public async Task Phan_trang_giu_nguyen_tong_va_cat_dung_trang()
    {
        using var db = new InMemoryTestDb();
        var lineageID = Guid.NewGuid();
        for (var year = 2021; year <= 2023; year++)
        {
            var session = db.SeedSession(
                sessionCode: $"DK-{year}", examDate: new DateOnly(year, 4, 1));
            db.SeedRecord(session.SessionID, recordCode: $"KSK-{year}",
                profileLineageID: lineageID, hisSignStatus: "Signed",
                hisSignedAt: new DateTime(year, 4, 1, 10, 0, 0, DateTimeKind.Utc));
        }

        var repo = new ExamRecordRepository(db.Db);
        var first = await repo.ListSignedByPatientLineageAsync(
            FakeHealthExamContext.DefaultDivisionId, lineageID, 1, 2);
        var second = await repo.ListSignedByPatientLineageAsync(
            FakeHealthExamContext.DefaultDivisionId, lineageID, 2, 2);

        Assert.Equal(3, first.Total);
        Assert.Equal(new[] { "KSK-2023", "KSK-2022" }, first.Items.Select(x => x.RecordCode).ToArray());
        Assert.Equal(3, second.Total);
        Assert.Equal(2, second.Size);
        Assert.Equal("KSK-2021", Assert.Single(second.Items).RecordCode);
    }
}
