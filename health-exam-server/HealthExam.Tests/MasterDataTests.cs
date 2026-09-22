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
using HealthExam.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using HealthExam.Server.Service;
using Xunit;

namespace HealthExam.Tests;

public class MasterDataTests
{
    [Fact]
    public async Task Resolve_master_data_option_reuses_the_tracked_registration_option()
    {
        using var db = new InMemoryTestDb();
        var subject = db.SeedMaster("PATIENT_SUBJECT", "01", "Người lớn");
        var session = db.SeedSession();
        var record = db.SeedRecord(session.SessionID);

        var stored = db.Db.ExamRecords.AsTracking().Single(x => x.RecordID == record.RecordID);
        stored.PatientSubjectOptionID = subject.OptionID;
        db.Db.SaveChanges();
        db.Db.ChangeTracker.Clear();

        var tracked = await db.Db.ExamRecords.AsTracking()
            .Include(x => x.PatientSubjectOption)
            .SingleAsync(x => x.RecordID == record.RecordID);
        var repository = new ExamRecordRepository(db.Db);

        var resolved = await repository.ResolveMasterDataOptionAsync("DEV", "PATIENT_SUBJECT", "01");

        Assert.Same(tracked.PatientSubjectOption, resolved);
    }

    [Fact]
    public async Task Registration_options_contain_static_and_tenant_values()
    {
        using var db = new InMemoryTestDb();
        db.SeedMaster("ETHNICITY", "KINH", "Kinh");
        db.SeedMaster("ETHNICITY", "HIDDEN", "Ẩn", isActive: false);
        db.SeedMaster("ETHNICITY", "OTHER", "Tenant khác", divisionId: "OTHER");
        db.SeedMaster("OCCUPATION", "DEV", "Lập trình viên");

        var result = await db.MasterData.GetRegistrationOptionsAsync();

        Assert.Contains(result.BloodAbos, x => x.Code == "AB" && x.Name == "AB");
        Assert.Contains(result.BloodRhs, x => x.Code == "+" && x.Name == "+");
        Assert.Contains(result.Genders, x => x.Code == "1" && x.Name == "Nam");
        Assert.Contains(result.Relationships, x => x.Code == "SPOUSE");
        Assert.Contains(result.ExamRecordStates, x => x.Code == "4" && x.Name == "Hủy đăng ký");
        Assert.Contains(result.ExamRecordStates, x => x.Code == "5" && x.Name == "Hủy khám");

        Assert.Single(result.Ethnicities);
        Assert.Equal("KINH", result.Ethnicities[0].Code);
        Assert.Equal("Kinh", result.Ethnicities[0].Name);

        Assert.Single(result.Occupations);
        Assert.Equal("DEV", result.Occupations[0].Code);
    }

    [Fact]
    public async Task Registration_options_return_registration_places_patient_types_and_patient_subjects()
    {
        using var db = new InMemoryTestDb();
        db.SeedMaster("REGISTRATION_PLACE", "BV01", "Bệnh viện Bạch Mai");
        db.SeedMaster("PATIENT_SUBJECT", "01", "Người lớn");
        db.SeedMaster("PATIENT_SUBJECT", "02", "Người cao tuổi");
        db.SeedMaster("PATIENT_TYPE", "OUTPATIENT", "Ngoại trú");

        var result = await db.MasterData.GetRegistrationOptionsAsync();

        Assert.Contains(result.RegistrationPlaces,
            x => x.Code == "BV01" && x.Name == "Bệnh viện Bạch Mai");
        Assert.Contains(result.PatientSubjects,
            x => x.Code == "01" && x.Name == "Người lớn");
        Assert.Contains(result.PatientTypes,
            x => x.Code == "OUTPATIENT" && x.Name == "Ngoại trú");
    }

    [Fact]
    public async Task ListProvinces_searches_case_insensitive_and_orders()
    {
        using var db = new InMemoryTestDb();
        db.SeedMaster("PROVINCE", "01", "Hà Nội", orderNo: 2);
        db.SeedMaster("PROVINCE", "79", "Hồ Chí Minh", orderNo: 1);
        db.SeedMaster("PROVINCE", "92", "Cần Thơ", orderNo: 3);
        db.SeedMaster("PROVINCE", "99", "Ẩn", isActive: false);

        var all = await db.MasterData.ListProvincesAsync("");
        Assert.Equal(3, all.Count);
        Assert.Equal("79", all[0].Code);
        Assert.Equal("01", all[1].Code);
        Assert.Equal("92", all[2].Code);

        var search = await db.MasterData.ListProvincesAsync("hà");
        Assert.Single(search);
        Assert.Equal("01", search[0].Code);

        var searchCode = await db.MasterData.ListProvincesAsync("79");
        Assert.Single(searchCode);
        Assert.Equal("79", searchCode[0].Code);
    }

    [Fact]
    public async Task ListWards_requires_valid_province_and_matches_parent()
    {
        using var db = new InMemoryTestDb();
        db.SeedMaster("PROVINCE", "92", "Cần Thơ");
        db.SeedMaster("WARD", "26734", "Phường An Cư", parentCode: "92", orderNo: 1);
        db.SeedMaster("WARD", "26737", "Phường An Nghiệp", parentCode: "92", orderNo: 2);
        db.SeedMaster("WARD", "00001", "Phường Phúc Xá", parentCode: "01", orderNo: 1);

        // Missing province throws BadRequest
        var exMissing = await Assert.ThrowsAsync<HealthExamException>(() =>
            db.MasterData.ListWardsAsync("", ""));
        Assert.Equal(ErrorCodes.BadRequest, exMissing.ErrorCode);

        // Non-existent province throws BadRequest
        var exInvalid = await Assert.ThrowsAsync<HealthExamException>(() =>
            db.MasterData.ListWardsAsync("99", ""));
        Assert.Equal(ErrorCodes.BadRequest, exInvalid.ErrorCode);

        // Valid province lists only wards of that parent
        var wards = await db.MasterData.ListWardsAsync("92", "");
        Assert.Equal(2, wards.Count);
        Assert.Equal("26734", wards[0].Code);
        Assert.Equal("26737", wards[1].Code);

        // Search ward by keyword
        var searchWard = await db.MasterData.ListWardsAsync("92", "nghiệp");
        Assert.Single(searchWard);
        Assert.Equal("26737", searchWard[0].Code);
    }

    [Fact]
    public async Task ResolveAsync_handles_blank_and_valid_and_invalid()
    {
        using var db = new InMemoryTestDb();
        db.SeedMaster("ETHNICITY", "KINH", "Kinh");
        db.SeedMaster("ETHNICITY", "INACTIVE", "Ẩn", isActive: false);

        // Blank returns null
        var blank = await db.MasterData.ResolveAsync("ETHNICITY", "", "EthnicityCode");
        Assert.Null(blank);

        // Valid returns item
        var valid = await db.MasterData.ResolveAsync("ETHNICITY", "KINH", "EthnicityCode");
        Assert.NotNull(valid);
        Assert.Equal("KINH", valid!.Code);
        Assert.Equal("Kinh", valid.Name);

        // Inactive throws BadRequest
        var exInactive = await Assert.ThrowsAsync<HealthExamException>(() =>
            db.MasterData.ResolveAsync("ETHNICITY", "INACTIVE", "EthnicityCode"));
        Assert.Equal(ErrorCodes.BadRequest, exInactive.ErrorCode);

        // Non-existent throws BadRequest
        var exNotFound = await Assert.ThrowsAsync<HealthExamException>(() =>
            db.MasterData.ResolveAsync("ETHNICITY", "NONEXISTENT", "EthnicityCode"));
        Assert.Equal(ErrorCodes.BadRequest, exNotFound.ErrorCode);

        // Static category resolution
        var abo = await db.MasterData.ResolveAsync("BLOOD_ABO", "AB", "BloodAboCode");
        Assert.NotNull(abo);
        Assert.Equal("AB", abo!.Code);

        var exAbo = await Assert.ThrowsAsync<HealthExamException>(() =>
            db.MasterData.ResolveAsync("BLOOD_ABO", "XYZ", "BloodAboCode"));
        Assert.Equal(ErrorCodes.BadRequest, exAbo.ErrorCode);
    }

    [Fact]
    public async Task ResolveWardAsync_validates_parent_relationship()
    {
        using var db = new InMemoryTestDb();
        db.SeedMaster("PROVINCE", "92", "Cần Thơ");
        db.SeedMaster("WARD", "26734", "Phường An Cư", parentCode: "92");

        // Blank ward returns null
        var blank = await db.MasterData.ResolveWardAsync("92", "", "WardCode");
        Assert.Null(blank);

        // Valid parent and ward returns item
        var valid = await db.MasterData.ResolveWardAsync("92", "26734", "WardCode");
        Assert.NotNull(valid);
        Assert.Equal("26734", valid!.Code);
        Assert.Equal("Phường An Cư", valid.Name);

        // Wrong parent throws BadRequest
        var exWrongParent = await Assert.ThrowsAsync<HealthExamException>(() =>
            db.MasterData.ResolveWardAsync("01", "26734", "WardCode"));
        Assert.Equal(ErrorCodes.BadRequest, exWrongParent.ErrorCode);
    }

    [Fact]
    public async Task ResolveOptionAsync_resolves_option_id_and_validates_active_tenant()
    {
        using var db = new InMemoryTestDb();
        var ptype = db.SeedMaster("PATIENT_TYPE", "OUTPATIENT", "Ngoại trú");
        var location = db.SeedMaster("EXAM_LOCATION", "CLINIC_3_F2", "Phòng khám 3 Tầng 2");
        db.SeedMaster("PATIENT_TYPE", "INACTIVE", "Không dùng", isActive: false);
        db.SeedMaster("PATIENT_TYPE", "OTHER_TENANT", "Khác tenant", divisionId: "OTHER_TENANT");

        var resolvedPType = await db.MasterData.ResolveOptionAsync("PATIENT_TYPE", "OUTPATIENT", "PatientTypeCode");
        Assert.NotNull(resolvedPType);
        Assert.Equal(ptype.OptionID, resolvedPType!.OptionID);
        Assert.Equal("OUTPATIENT", resolvedPType.Code);
        Assert.Equal("Ngoại trú", resolvedPType.Name);

        var resolvedLocation = await db.MasterData.ResolveOptionAsync("EXAM_LOCATION", "CLINIC_3_F2", "ExamLocationCode");
        Assert.NotNull(resolvedLocation);
        Assert.Equal(location.OptionID, resolvedLocation!.OptionID);
        Assert.Equal("CLINIC_3_F2", resolvedLocation.Code);
        Assert.Equal("Phòng khám 3 Tầng 2", resolvedLocation.Name);

        // Inactive throws BadRequest
        var exInactive = await Assert.ThrowsAsync<HealthExamException>(() =>
            db.MasterData.ResolveOptionAsync("PATIENT_TYPE", "INACTIVE", "PatientTypeCode"));
        Assert.Equal(ErrorCodes.BadRequest, exInactive.ErrorCode);

        // Cross-tenant throws BadRequest
        var exCrossTenant = await Assert.ThrowsAsync<HealthExamException>(() =>
            db.MasterData.ResolveOptionAsync("PATIENT_TYPE", "OTHER_TENANT", "PatientTypeCode"));
        Assert.Equal(ErrorCodes.BadRequest, exCrossTenant.ErrorCode);
    }
}
