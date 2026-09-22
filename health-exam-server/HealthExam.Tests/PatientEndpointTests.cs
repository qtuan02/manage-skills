using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading.Tasks;
using HealthExam.API.Contracts;
using HealthExam.Domain.Catalogs;
using HealthExam.Domain.Common;
using HealthExam.Domain.ExamRecords;
using HealthExam.Domain.ExamSessions;
using HealthExam.Domain.Patients;
using HealthExam.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json.Linq;
using Xunit;

namespace HealthExam.Tests;

public class PatientEndpointTests : IClassFixture<AuthTestHost>
{
    private readonly WebApplicationFactory<Program> _factory;

    public PatientEndpointTests(AuthTestHost host)
    {
        var databaseName = $"patient-endpoint-{Guid.NewGuid():N}";
        _factory = host.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                var descriptors = services.Where(d =>
                    d.ServiceType == typeof(DbContextOptions<HealthExamDbContext>) ||
                    d.ServiceType == typeof(DbContextOptions) ||
                    d.ServiceType == typeof(HealthExamDbContext)).ToList();
                foreach (var d in descriptors)
                    services.Remove(d);

                services.AddDbContext<HealthExamDbContext>(options =>
                {
                    options.UseInMemoryDatabase(databaseName);
                    options.ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning));
                });
            });
        });
    }

    private HttpClient CreateEmployeeClient(string divisionId = "DEV")
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestHeaders.Division, divisionId);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", AuthTestHost.EmployeeToken());
        return client;
    }

    private HttpClient CreateAnonymousClient(string divisionId = "DEV")
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestHeaders.Division, divisionId);
        return client;
    }

    [Fact]
    public async Task Anonymous_search_is_rejected()
    {
        var client = CreateAnonymousClient();
        var response = await client.GetAsync("/v1/patients/search?keyword=079123456789");
        var body = JObject.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(ErrorCodes.Unauthorized, body["ErrorCode"]!.Value<int>());
        Assert.False(string.IsNullOrWhiteSpace(body["TraceID"]!.Value<string>()));
    }

    private async Task<Guid> SeedPatientAsync(
        string divisionId = "DEV",
        string fullName = "Nguyễn Văn An",
        string identityNumber = "079123456789",
        string phoneNumber = "0901234567",
        string patientCode = "BN000125001",
        bool isActive = true)
    {
        var id = Guid.NewGuid();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HealthExamDbContext>();
        db.Patients.Add(new Patient
        {
            PatientRefID = id,
            DivisionID = divisionId,
            FullName = fullName,
            IdentityNumber = identityNumber,
            Dob = new DateOnly(1990, 1, 1),
            BirthYear = 1990,
            GenderID = 1,
            PhoneNumber = phoneNumber,
            PatientCode = patientCode,
            Address = "Địa chỉ cũ",
            IsActive = isActive
        });
        await db.SaveChangesAsync();
        return id;
    }

    [Fact]
    public async Task Search_auto_returns_summary_items_in_envelope()
    {
        var id = await SeedPatientAsync();

        var client = CreateEmployeeClient("DEV");
        var response = await client.GetAsync("/v1/patients/search?keyword=079123456789");
        var body = JObject.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(0, body["ErrorCode"]!.Value<int>());
        var array = Assert.IsAssignableFrom<JArray>(body["Data"]);
        var item = Assert.Single(array);
        Assert.Equal(id, Guid.Parse(item["PatientRefID"]!.Value<string>()!));
        Assert.Equal("BN000125001", item["PatientCode"]!.Value<string>());
        Assert.Equal("Nguyễn Văn An", item["FullName"]!.Value<string>());
        Assert.Equal(1990, item["BirthYear"]!.Value<int>());
        Assert.Equal(1, item["GenderID"]!.Value<int>());
        Assert.Equal("079123456789", item["IdentityNumber"]!.Value<string>());
        // Chỉ trả tóm tắt: không lộ các trường chi tiết
        Assert.Null(item["Address"]);
        Assert.Null(item["PhoneNumber"]);
    }

    [Theory]
    [InlineData("name", "văn an")]
    [InlineData("phone", "0901")]
    [InlineData("code", "bn0001")]
    [InlineData("identity", "0791")]
    public async Task Search_explicit_types_find_seeded_patient(string type, string keyword)
    {
        var id = await SeedPatientAsync();

        var client = CreateEmployeeClient("DEV");
        var response = await client.GetAsync($"/v1/patients/search?type={type}&keyword={Uri.EscapeDataString(keyword)}");
        var body = JObject.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var array = Assert.IsAssignableFrom<JArray>(body["Data"]);
        Assert.Contains(array, x => Guid.Parse(x["PatientRefID"]!.Value<string>()!) == id);
    }

    [Fact]
    public async Task Search_without_keyword_returns_latest_active_patients()
    {
        var id = await SeedPatientAsync();

        var client = CreateEmployeeClient("DEV");
        var response = await client.GetAsync("/v1/patients/search");
        var body = JObject.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(0, body["ErrorCode"]!.Value<int>());
        var array = Assert.IsAssignableFrom<JArray>(body["Data"]);
        Assert.Contains(array, x => Guid.Parse(x["PatientRefID"]!.Value<string>()!) == id);
    }

    [Fact]
    public async Task Search_unknown_type_returns_bad_request()
    {
        var client = CreateEmployeeClient("DEV");
        var response = await client.GetAsync("/v1/patients/search?type=email&keyword=an");
        var body = JObject.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(ErrorCodes.BadRequest, body["ErrorCode"]!.Value<int>());
    }

    [Fact]
    public async Task Get_patient_returns_full_profile()
    {
        var id = await SeedPatientAsync();

        var client = CreateEmployeeClient("DEV");
        var response = await client.GetAsync($"/v1/patients/{id}");
        var body = JObject.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(0, body["ErrorCode"]!.Value<int>());
        var data = body["Data"]!;
        Assert.Equal(id, Guid.Parse(data["PatientRefID"]!.Value<string>()!));
        Assert.Equal("Nguyễn Văn An", data["FullName"]!.Value<string>());
        Assert.Equal("0901234567", data["PhoneNumber"]!.Value<string>());
        Assert.Equal("Địa chỉ cũ", data["Address"]!.Value<string>());
        Assert.True(data["IsActive"]!.Value<bool>());
    }

    [Fact]
    public async Task Get_patient_returns_insurance_employment_and_relative_of_last_registration()
    {
        var id = await SeedPatientAsync();
        Guid insuranceRefId, relativeRefId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HealthExamDbContext>();
            var insuranceObject = new MasterDataOption
            {
                OptionID = Guid.NewGuid(), DivisionID = "DEV", Category = MasterDataCategories.InsuranceObject,
                Code = "HT", Name = "Hưu trí", IsActive = true
            };
            db.MasterDataOptions.Add(insuranceObject);
            var insurance = new PatientInsurance
            {
                InsuranceRefID = Guid.NewGuid(), DivisionID = "DEV", PatientRefID = id,
                InsuranceNumber = "DN4790000001", InsuranceObjectOptionID = insuranceObject.OptionID,
                ValidFrom = new DateOnly(2026, 1, 1), ValidTo = new DateOnly(2026, 12, 31), IsActive = true
            };
            db.PatientInsurances.Add(insurance);
            var relative = new PatientRelative
            {
                RelativeRefID = Guid.NewGuid(), DivisionID = "DEV", PatientRefID = id,
                RelationshipCode = "SPOUSE", FullName = "Trần Thị B", PhoneNumber = "0912345678", IsActive = true
            };
            db.PatientRelatives.Add(relative);
            await db.SaveChangesAsync();
            insuranceRefId = insurance.InsuranceRefID;
            relativeRefId = relative.RelativeRefID;
        }

        var client = CreateEmployeeClient("DEV");
        var response = await client.GetAsync($"/v1/patients/{id}");
        var body = JObject.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var data = body["Data"]!;
        Assert.Equal(insuranceRefId, Guid.Parse(data["InsuranceRefID"]!.Value<string>()!));
        Assert.Equal("DN4790000001", data["InsuranceNumber"]!.Value<string>());
        Assert.Equal("HT", data["InsuranceObjectCode"]!.Value<string>());
        Assert.Equal("Hưu trí", data["InsuranceObjectName"]!.Value<string>());
        Assert.Equal("2026-01-01", data["InsuranceValidFrom"]!.Value<string>());
        Assert.Equal(JTokenType.Null, data["EmploymentRefID"]!.Type);
        Assert.Equal("", data["OccupationCode"]!.Value<string>());
        Assert.Equal(relativeRefId, Guid.Parse(data["RelativeRefID"]!.Value<string>()!));
        Assert.Equal("SPOUSE", data["RelativeRelationshipCode"]!.Value<string>());
        Assert.Equal("Vợ-chồng", data["RelativeRelationshipName"]!.Value<string>());
        Assert.Equal("Trần Thị B", data["RelativeFullName"]!.Value<string>());
        Assert.Equal("", data["IdentityIssuerCode"]!.Value<string>());
    }

    [Fact]
    public async Task Get_patient_from_other_division_is_not_found()
    {
        var id = await SeedPatientAsync(divisionId: "OTHER");

        var client = CreateEmployeeClient("DEV");
        var response = await client.GetAsync($"/v1/patients/{id}");
        var body = JObject.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(ErrorCodes.NotFound, body["ErrorCode"]!.Value<int>());
    }

    [Fact]
    public async Task Anonymous_get_patient_is_rejected()
    {
        var client = CreateAnonymousClient();
        var response = await client.GetAsync($"/v1/patients/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Match_patient_returns_standard_envelope_and_changed_fields()
    {
        var patientRefId = Guid.NewGuid();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HealthExamDbContext>();
            db.Patients.Add(new Patient
            {
                PatientRefID = patientRefId,
                DivisionID = "DEV",
                FullName = "Nguyễn Văn An",
                IdentityNumber = "079123456789",
                Dob = new DateOnly(1990, 1, 1),
                BirthYear = 1990,
                GenderID = 1,
                PhoneNumber = "0901234567",
                Address = "Địa chỉ cũ",
                IsActive = true
            });
            await db.SaveChangesAsync();
        }

        var client = CreateEmployeeClient("DEV");
        var response = await client.PostAsJsonAsync("/v1/patients/match", new
        {
            FullName = "Nguyễn Văn An",
            Dob = "1990-01-01",
            BirthYear = 1990,
            GenderID = 1,
            IdentityNumber = "079123456789",
            IdentityIssuedDate = (string)null,
            IdentityIssuerOptionID = (Guid?)null,
            PhoneNumber = "0901234567",
            Email = "",
            Address = "Địa chỉ mới",
            EthnicityOptionID = (Guid?)null,
            BloodAboCode = "",
            BloodRhCode = ""
        });
        var body = JObject.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(0, body["ErrorCode"]!.Value<int>());
        Assert.Equal(patientRefId, Guid.Parse(body["Data"]!["PatientRefID"]!.Value<string>()!));
        Assert.False(body["Data"]!["Matched"]!.Value<bool>());
        Assert.Equal(new[] { "Address" }, body["Data"]!["ChangedFields"]!.Values<string>());
    }

    [Fact]
    public async Task Post_match_so_khop_dung_province_va_ward_code()
    {
        var patientRefId = Guid.NewGuid();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HealthExamDbContext>();
            db.Patients.Add(new Patient
            {
                PatientRefID = patientRefId,
                DivisionID = "DEV",
                FullName = "Nguyễn Hoàng Hải",
                IdentityNumber = "003939393939",
                Dob = new DateOnly(2000, 2, 15),
                GenderID = 1,
                PhoneNumber = "0303449493",
                Address = "505/1 Lê Văn Sỹ",
                ProvinceCode = "79",
                WardCode = "26830",
                IsActive = true
            });
            await db.SaveChangesAsync();
        }

        var client = CreateEmployeeClient("DEV");
        var response = await client.PostAsJsonAsync("/v1/patients/match", new
        {
            FullName = "Nguyễn Hoàng Hải",
            Dob = "2000-02-15",
            BirthYear = (short?)null,
            GenderID = 1,
            IdentityNumber = "003939393939",
            IdentityIssuedDate = (string)null,
            IdentityIssuerOptionID = (Guid?)null,
            PhoneNumber = "0303449493",
            Email = (string)null,
            Address = "505/1 Lê Văn Sỹ",
            EthnicityOptionID = (Guid?)null,
            BloodAboCode = "",
            BloodRhCode = "",
            ProvinceCode = "79",
            WardCode = "26830"
        });
        var body = JObject.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(0, body["ErrorCode"]!.Value<int>());
        Assert.Equal(patientRefId, Guid.Parse(body["Data"]!["PatientRefID"]!.Value<string>()!));
        Assert.True(body["Data"]!["Matched"]!.Value<bool>());
        Assert.Empty(body["Data"]!["ChangedFields"]!.Values<string>());
    }

    private async Task<Guid> SeedSignedHistoryAsync(
        string divisionId = "DEV",
        string recordCode = "KSK-2026-0001",
        string sessionCode = "DK-2026",
        DateOnly? examDate = null,
        Guid? lineageID = null,
        string hisSignStatus = "Signed")
    {
        var lineage = lineageID ?? Guid.NewGuid();
        var patientRefID = Guid.NewGuid();
        var sessionID = Guid.NewGuid();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HealthExamDbContext>();
        db.Patients.Add(new Patient
        {
            PatientRefID = patientRefID,
            DivisionID = divisionId,
            FullName = "Nguyễn Văn An",
            IdentityNumber = "079123456789",
            PatientCode = "BN000125001",
            ProfileLineageID = lineage,
            IsActive = true
        });
        db.ExamSessions.Add(new ExamSession
        {
            SessionID = sessionID,
            DivisionID = divisionId,
            SessionCode = sessionCode,
            SessionName = "Đợt khám công ty A",
            ExamDate = examDate ?? new DateOnly(2026, 3, 14)
        });
        db.ExamRecords.Add(new ExamRecord
        {
            RecordID = Guid.NewGuid(),
            DivisionID = divisionId,
            SessionID = sessionID,
            RecordCode = recordCode,
            PatientRefID = patientRefID,
            VariantCode = "DTK_01",
            HealthClassCode = "II",
            State = ExamRecordState.Completed,
            SignStatus = hisSignStatus ?? ExamRecordSignStatus.New,
            HisSignedAt = new DateTime(2026, 3, 14, 9, 12, 0, DateTimeKind.Utc)
        });
        await db.SaveChangesAsync();
        return patientRefID;
    }

    [Fact]
    public async Task Exam_history_tra_envelope_phan_trang_size_mac_dinh_5()
    {
        var patientRefID = await SeedSignedHistoryAsync();

        var client = CreateEmployeeClient("DEV");
        var response = await client.GetAsync($"/v1/patients/{patientRefID}/exam-history");
        var body = JObject.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(0, body["ErrorCode"]!.Value<int>());
        Assert.Equal(1, body["Data"]!["Page"]!.Value<int>());
        Assert.Equal(5, body["Data"]!["Size"]!.Value<int>());
        Assert.Equal(1, body["Data"]!["Total"]!.Value<int>());

        var items = Assert.IsAssignableFrom<JArray>(body["Data"]!["Items"]);
        var item = Assert.Single(items);
        Assert.Equal("KSK-2026-0001", item["RecordCode"]!.Value<string>());
        Assert.Equal("DK-2026", item["SessionCode"]!.Value<string>());
        Assert.Equal("II", item["HealthClassCode"]!.Value<string>());
        Assert.Equal("Signed", item["HisSignStatus"]!.Value<string>());
        Assert.NotNull(item["HisSignedAt"]);
    }

    [Fact]
    public async Task Exam_history_bo_qua_ho_so_chua_ky_ket_luan()
    {
        var patientRefID = await SeedSignedHistoryAsync(
            recordCode: "KSK-CHUA-KY", hisSignStatus: "InProcessing");

        var client = CreateEmployeeClient("DEV");
        var response = await client.GetAsync($"/v1/patients/{patientRefID}/exam-history");
        var body = JObject.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(0, body["Data"]!["Total"]!.Value<int>());
        Assert.Empty(Assert.IsAssignableFrom<JArray>(body["Data"]!["Items"]));
    }

    [Fact]
    public async Task Exam_history_cua_patientRefId_la_tra_404()
    {
        var client = CreateEmployeeClient("DEV");
        var response = await client.GetAsync($"/v1/patients/{Guid.NewGuid()}/exam-history");
        var body = JObject.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(ErrorCodes.NotFound, body["ErrorCode"]!.Value<int>());
    }

    [Fact]
    public async Task Exam_history_khong_cho_khach_vang_lai()
    {
        var patientRefID = await SeedSignedHistoryAsync();

        var client = CreateAnonymousClient("DEV");
        var response = await client.GetAsync($"/v1/patients/{patientRefID}/exam-history");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
