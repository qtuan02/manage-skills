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
using Xunit;

namespace HealthExam.Tests;

public class ExamRecordRegistrationFieldsTests
{
    [Fact]
    public async Task Create_and_update_saves_canonical_names_and_snapshots()
    {
        using var db = new InMemoryTestDb();
        var session = db.SeedSession();
        db.SeedMaster("ETHNICITY", "KINH", "Kinh");
        db.SeedMaster("OCCUPATION", "DEV", "Kỹ sư phần mềm");
        db.SeedMaster("PROVINCE", "92", "Cần Thơ");
        db.SeedMaster("WARD", "26734", "Phường An Cư", parentCode: "92");
        db.SeedMaster("IDENTITY_ISSUER", "CC01", "Cục CS QLHC về TTXH");
        db.SeedMaster("INSURANCE_OBJECT", "DN", "Doanh nghiệp");
        db.SeedMaster("PATIENT_TYPE", "DV", "Dịch vụ");
        db.SeedMaster("PATIENT_SUBJECT", "01", "Người lớn");
        db.SeedMaster("PAYMENT_SOURCE", "OTHER", "Khác");
        db.SeedMaster("EXAM_LOCATION", "PK1", "Phòng khám 1");

        var created = await db.Records.CreateAsync(new ExamRecordSaveRequest
        {
            SessionID = session.SessionID,
            FullName = "Nguyễn Văn A",
            VariantCode = "DTK_01",
            EthnicityCode = "KINH",
            OccupationCode = "DEV",
            BloodAboCode = "AB",
            BloodRhCode = "+",
            ProvinceCode = "92",
            WardCode = "26734",
            IdentityIssuedDate = new DateOnly(2020, 1, 1),
            IdentityIssuerCode = "CC01",
            RelativeRelationshipCode = "SPOUSE",
            RelativeFullName = "Trần Thị B",
            RelativeIdentityNumber = "012345678901",
            RelativePhoneNumber = "0901234567",
            InsuranceObjectCode = "DN",
            InsuranceValidFrom = new DateOnly(2025, 1, 1),
            InsuranceValidTo = new DateOnly(2025, 12, 31),
            ExamReason = "Khám sức khỏe định kỳ",
            PatientTypeCode = "DV",
            PatientSubjectCode = "01",
            PaymentSourceCode = "OTHER",
            PaymentSourceOther = "Công ty tài trợ",
            ExamLocationCode = "PK1"
        });

        Assert.Equal("Kinh", created.EthnicityName);
        Assert.Equal("Kỹ sư phần mềm", created.OccupationName);
        Assert.Equal("AB", created.BloodAboName);
        Assert.Equal("+", created.BloodRhName);
        Assert.Equal("Cần Thơ", created.ProvinceName);
        Assert.Equal("Phường An Cư", created.WardName);
        Assert.Equal("Cục CS QLHC về TTXH", created.IdentityIssuerName);
        Assert.Equal("Vợ-chồng", created.RelativeRelationshipName);
        Assert.Equal("Doanh nghiệp", created.InsuranceObjectName);
        Assert.Equal("Dịch vụ", created.PatientTypeName);
        Assert.Equal("01", created.PatientSubjectCode);
        Assert.Equal("Người lớn", created.PatientSubjectName);
        Assert.Equal("Khác", created.PaymentSourceName);
        Assert.Equal("Công ty tài trợ", created.PaymentSourceOther);
        Assert.Equal("Phòng khám 1", created.ExamLocationName);

        // Rename in master data does NOT alter stored snapshot
        var masterEthnicity = db.Db.MasterDataOptions.First(x => x.Code == "KINH");
        masterEthnicity.Name = "Người Kinh";
        db.Db.SaveChanges();

        var fetched = await db.Records.GetAsync(created.RecordID);
        Assert.Equal("Kinh", fetched.EthnicityName);

        // Clearing code clears name
        var updated = await db.Records.UpdateAsync(created.RecordID, new ExamRecordSaveRequest
        {
            FullName = "Nguyễn Văn A",
            VariantCode = "DTK_01",
            EthnicityCode = ""
        });
        Assert.Equal("", updated.EthnicityCode);
        Assert.Equal("", updated.EthnicityName);
    }

    [Fact]
    public async Task Old_request_without_master_data_fields_succeeds()
    {
        using var db = new InMemoryTestDb();
        var session = db.SeedSession();

        var created = await db.Records.CreateAsync(new ExamRecordSaveRequest
        {
            SessionID = session.SessionID,
            FullName = "Trần Văn C",
            VariantCode = "DTK_01"
        });

        Assert.NotNull(created);
        Assert.Equal("Trần Văn C", created.FullName);
        Assert.Equal("", created.EthnicityCode);
        Assert.Equal("", created.EthnicityName);
    }

    [Fact]
    public async Task Ward_from_another_province_fails()
    {
        using var db = new InMemoryTestDb();
        var session = db.SeedSession();
        db.SeedMaster("PROVINCE", "92", "Cần Thơ");
        db.SeedMaster("PROVINCE", "01", "Hà Nội");
        db.SeedMaster("WARD", "00001", "Phường Phúc Xá", parentCode: "01");

        var ex = await Assert.ThrowsAsync<HealthExamException>(() =>
            db.Records.CreateAsync(new ExamRecordSaveRequest
            {
                SessionID = session.SessionID,
                FullName = "Nguyễn Văn D",
                VariantCode = "DTK_01",
                ProvinceCode = "92",
                WardCode = "00001"
            }));

        Assert.Equal(ErrorCodes.BadRequest, ex.ErrorCode);
    }

    [Fact]
    public async Task Inactive_code_fails()
    {
        using var db = new InMemoryTestDb();
        var session = db.SeedSession();
        db.SeedMaster("ETHNICITY", "INACTIVE", "Ẩn", isActive: false);

        var ex = await Assert.ThrowsAsync<HealthExamException>(() =>
            db.Records.CreateAsync(new ExamRecordSaveRequest
            {
                SessionID = session.SessionID,
                FullName = "Lê Văn E",
                VariantCode = "DTK_01",
                EthnicityCode = "INACTIVE"
            }));

        Assert.Equal(ErrorCodes.BadRequest, ex.ErrorCode);
    }

    [Fact]
    public async Task Invalid_abo_rh_relationship_fails()
    {
        using var db = new InMemoryTestDb();
        var session = db.SeedSession();

        var exAbo = await Assert.ThrowsAsync<HealthExamException>(() =>
            db.Records.CreateAsync(new ExamRecordSaveRequest
            {
                SessionID = session.SessionID,
                FullName = "Phạm Văn F",
                VariantCode = "DTK_01",
                BloodAboCode = "INVALID"
            }));
        Assert.Equal(ErrorCodes.BadRequest, exAbo.ErrorCode);

        var exRh = await Assert.ThrowsAsync<HealthExamException>(() =>
            db.Records.CreateAsync(new ExamRecordSaveRequest
            {
                SessionID = session.SessionID,
                FullName = "Phạm Văn F",
                VariantCode = "DTK_01",
                BloodRhCode = "INVALID"
            }));
        Assert.Equal(ErrorCodes.BadRequest, exRh.ErrorCode);

        var exRel = await Assert.ThrowsAsync<HealthExamException>(() =>
            db.Records.CreateAsync(new ExamRecordSaveRequest
            {
                SessionID = session.SessionID,
                FullName = "Phạm Văn F",
                VariantCode = "DTK_01",
                RelativeRelationshipCode = "FRIEND"
            }));
        Assert.Equal(ErrorCodes.BadRequest, exRel.ErrorCode);
    }

    [Fact]
    public async Task Bhyt_end_before_start_fails()
    {
        using var db = new InMemoryTestDb();
        var session = db.SeedSession();

        var ex = await Assert.ThrowsAsync<HealthExamException>(() =>
            db.Records.CreateAsync(new ExamRecordSaveRequest
            {
                SessionID = session.SessionID,
                FullName = "Vũ Văn G",
                VariantCode = "DTK_01",
                InsuranceValidFrom = new DateOnly(2025, 12, 31),
                InsuranceValidTo = new DateOnly(2025, 1, 1)
            }));

        Assert.Equal(ErrorCodes.BadRequest, ex.ErrorCode);
    }

    [Fact]
    public async Task PaymentSourceOther_is_cleared_if_payment_source_is_not_other()
    {
        using var db = new InMemoryTestDb();
        var session = db.SeedSession();
        db.SeedMaster("PAYMENT_SOURCE", "CASH", "Tiền mặt");

        var created = await db.Records.CreateAsync(new ExamRecordSaveRequest
        {
            SessionID = session.SessionID,
            FullName = "Hoàng Văn H",
            VariantCode = "DTK_01",
            PaymentSourceCode = "CASH",
            PaymentSourceOther = "Vẫn truyền chuỗi khác"
        });

        Assert.Equal("CASH", created.PaymentSourceCode);
        Assert.Equal("", created.PaymentSourceOther);
    }

    [Fact]
    public async Task Confirm_without_exam_reason_fails_and_keeps_state()
    {
        using var db = new InMemoryTestDb();
        var session = db.SeedSession();

        var created = await db.Records.CreateAsync(new ExamRecordSaveRequest
        {
            SessionID = session.SessionID,
            FullName = "Đỗ Văn I",
            VariantCode = "DTK_01"
        });

        var ex = await Assert.ThrowsAsync<HealthExamException>(() =>
            db.Records.ConfirmAsync(created.RecordID));

        Assert.Equal(ErrorCodes.BadRequest, ex.ErrorCode);

        var unchanged = await db.Records.GetAsync(created.RecordID);
        Assert.Equal(ExamRecordState.NotRegistered, unchanged.State);
        Assert.Null(unchanged.RegisteredAt);
    }

    [Fact]
    public async Task Confirm_with_other_payment_source_without_text_fails()
    {
        using var db = new InMemoryTestDb();
        var session = db.SeedSession();
        db.SeedMaster("PAYMENT_SOURCE", "OTHER", "Khác");

        var created = await db.Records.CreateAsync(new ExamRecordSaveRequest
        {
            SessionID = session.SessionID,
            FullName = "Ngô Văn K",
            VariantCode = "DTK_01",
            ExamReason = "Khám định kỳ",
            PaymentSourceCode = "OTHER",
            PaymentSourceOther = ""
        });

        var ex = await Assert.ThrowsAsync<HealthExamException>(() =>
            db.Records.ConfirmAsync(created.RecordID));

        Assert.Equal(ErrorCodes.BadRequest, ex.ErrorCode);
    }

    [Fact]
    public async Task Confirm_with_valid_details_succeeds()
    {
        using var db = new InMemoryTestDb();
        var session = db.SeedSession();
        db.SeedMaster("PAYMENT_SOURCE", "OTHER", "Khác");

        var created = await db.Records.CreateAsync(new ExamRecordSaveRequest
        {
            SessionID = session.SessionID,
            FullName = "Dương Văn L",
            VariantCode = "DTK_01",
            ExamReason = "Khám định kỳ",
            PaymentSourceCode = "OTHER",
            PaymentSourceOther = "Công ty trả"
        });

        var confirmed = await db.Records.ConfirmAsync(created.RecordID);

        Assert.Equal(ExamRecordState.Waiting, confirmed.State);
        Assert.NotNull(confirmed.RegisteredAt);
    }
}
