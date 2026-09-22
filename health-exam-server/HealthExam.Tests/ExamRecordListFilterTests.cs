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
using HealthExam.API.Controllers;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HealthExam.Tests;

public sealed class ExamRecordListFilterTests
{
    [Fact]
    public async Task Loc_theo_tung_field_khong_dung_keyword_chung()
    {
        using var db = new InMemoryTestDb();
        var session = db.SeedSession();
        db.SeedRecord(session.SessionID, recordCode: "KSK-001", patientCode: "NB-001", fullName: "Nguyễn Văn A");
        db.SeedRecord(session.SessionID, recordCode: "KSK-002", patientCode: "NB-002", fullName: "Trần Thị B");

        var result = await db.Records.ListAsync(new ExamRecordListFilter
        {
            RecordCode = "KSK-001",
            PatientCode = "NB-001",
            FullName = "nguyễn văn a"
        }, 1, 20);

        var item = Assert.Single(result.Items);
        Assert.Equal("KSK-001", item.RecordCode);
    }

    [Fact]
    public async Task Loc_duoc_theo_cccd_va_so_dien_thoai_rieng()
    {
        using var db = new InMemoryTestDb();
        var session = db.SeedSession();
        db.SeedRecord(session.SessionID, recordCode: "KSK-001", identityNumber: "012345678901");
        var target = db.SeedRecord(session.SessionID, recordCode: "KSK-002", identityNumber: "999999999999");
        var tracked = db.Db.ExamRecords.AsTracking().Include(x => x.Patient).Single(x => x.RecordID == target.RecordID);
        tracked.Patient!.PhoneNumber = "0909123456";
        db.Db.SaveChanges();
        db.Db.ChangeTracker.Clear();

        var byIdentity = await db.Records.ListAsync(new ExamRecordListFilter
        {
            IdentityNumber = "999999"
        }, 1, 20);
        var byPhone = await db.Records.ListAsync(new ExamRecordListFilter
        {
            PhoneNumber = "123456"
        }, 1, 20);

        Assert.Equal("KSK-002", Assert.Single(byIdentity.Items).RecordCode);
        Assert.Equal("KSK-002", Assert.Single(byPhone.Items).RecordCode);
    }

    [Fact]
    public async Task Loc_khoang_ngay_tao_ke_ca_ho_so_chua_dang_ky()
    {
        using var db = new InMemoryTestDb();
        var session = db.SeedSession();
        db.SeedRecord(session.SessionID, recordCode: "KSK-CREATED-IN-RANGE",
            createdDate: new DateTime(2026, 8, 24, 17, 0, 0, DateTimeKind.Utc));
        var createdOutsideRange = db.SeedRecord(session.SessionID,
            recordCode: "KSK-REGISTERED-IN-RANGE",
            createdDate: new DateTime(2026, 8, 23, 8, 0, 0, DateTimeKind.Utc));
        db.SeedRecord(session.SessionID, recordCode: "KSK-CREATED-AT-TO-EXCLUSIVE",
            createdDate: new DateTime(2026, 8, 25, 17, 0, 0, DateTimeKind.Utc));
        SetRegisteredAt(db, createdOutsideRange.RecordID,
            new DateTime(2026, 8, 24, 18, 0, 0, DateTimeKind.Utc));

        var result = await db.Records.ListAsync(new ExamRecordListFilter
        {
            From = new DateOnly(2026, 8, 25),
            To = new DateOnly(2026, 8, 25)
        }, 1, 20);

        Assert.Equal("KSK-CREATED-IN-RANGE", Assert.Single(result.Items).RecordCode);
    }

    [Fact]
    public async Task Vietnam_day_includes_early_morning_and_excludes_next_midnight()
    {
        using var db = new InMemoryTestDb();
        var session = db.SeedSession();
        db.SeedRecord(session.SessionID, recordCode: "IN",
            createdDate: new DateTime(2026, 9, 18, 18, 0, 0, DateTimeKind.Utc));
        db.SeedRecord(session.SessionID, recordCode: "OUT",
            createdDate: new DateTime(2026, 9, 19, 17, 0, 0, DateTimeKind.Utc));
        var result = await db.Records.ListAsync(new ExamRecordListFilter
        {
            From = new DateOnly(2026, 9, 19),
            To = new DateOnly(2026, 9, 19)
        }, 1, 20);
        Assert.Equal("IN", Assert.Single(result.Items).RecordCode);
    }

    [Fact]
    public async Task Tu_ngay_lon_hon_den_ngay_bi_tu_choi()
    {
        using var db = new InMemoryTestDb();

        var ex = await Assert.ThrowsAsync<HealthExamException>(() => db.Records.ListAsync(
            new ExamRecordListFilter
            {
                From = new DateOnly(2026, 8, 26),
                To = new DateOnly(2026, 8, 25)
            }, 1, 20));

        Assert.Equal(ErrorCodes.BadRequest, ex.ErrorCode);
    }

    [Fact]
    public async Task Ket_qua_tra_ve_co_ngay_kham_cua_dot()
    {
        using var db = new InMemoryTestDb();
        var session = db.SeedSession(examDate: new DateOnly(2026, 9, 4));
        db.SeedRecord(session.SessionID);

        var result = await db.Records.ListAsync(new ExamRecordListFilter(), 1, 20);

        Assert.Equal(new DateOnly(2026, 9, 4), Assert.Single(result.Items).ExamDate);
    }

    [Fact]
    public async Task Ho_so_tao_moi_nhat_len_dau_ke_ca_khi_chua_dang_ky()
    {
        using var db = new InMemoryTestDb();
        var session = db.SeedSession();
        var older = db.SeedRecord(session.SessionID, recordCode: "KSK-OLD",
            createdDate: new DateTime(2026, 9, 7, 8, 0, 0, DateTimeKind.Utc));
        db.SeedRecord(session.SessionID, recordCode: "KSK-NEW",
            createdDate: new DateTime(2026, 9, 8, 8, 0, 0, DateTimeKind.Utc));
        SetRegisteredAt(db, older.RecordID,
            new DateTime(2026, 9, 8, 9, 0, 0, DateTimeKind.Utc));

        var result = await db.Records.ListAsync(new ExamRecordListFilter(), 1, 20);

        Assert.Equal(new[] { "KSK-NEW", "KSK-OLD" },
            result.Items.Select(x => x.RecordCode));
    }

    [Fact]
    public void Api_exposes_only_fromDate_and_toDate()
    {
        var action = typeof(ExamRecordController).GetMethod(nameof(ExamRecordController.List));
        var parameters = action!.GetParameters();

        Assert.Contains(parameters, x => x.Name == "fromDate");
        Assert.Contains(parameters, x => x.Name == "toDate");
        Assert.DoesNotContain(parameters, x => x.Name == "from");
        Assert.DoesNotContain(parameters, x => x.Name == "to");
    }

    private static void SetRegisteredAt(InMemoryTestDb db, Guid recordId, DateTime value)
    {
        var row = db.Db.ExamRecords.AsTracking().Single(x => x.RecordID == recordId);
        row.RegisteredAt = value;
        db.Db.SaveChanges();
        db.Db.ChangeTracker.Clear();
    }
}
