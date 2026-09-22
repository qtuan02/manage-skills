using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using HealthExam.Application.Common;
using HealthExam.Application.ExamRecords;
using HealthExam.Application.His;
using HealthExam.Application.Integrations;
using HealthExam.Domain.Common;
using HealthExam.Domain.ExamRecords;
using HealthExam.Domain.ExamSessions;
using HealthExam.Domain.Patients;
using Xunit;

namespace HealthExam.Tests.Application;

public class HisHandlerTests
{
    private const string DivisionId = "D01";
    private const string ValidToken = "Bearer test-credential";
    private static readonly Guid ValidTemplateId = Guid.NewGuid();
    private const int ValidDocTypeId = 101;

    [Fact]
    public async Task Missing_credential_returns_unauthorized()
    {
        var client = new FakeHisEmrClient();
        var cache = new FakeHisFormDefinitionCache();
        var handler = new GetHisFormDefinitionHandler(client, cache);

        var result = await handler.HandleAsync(
            new GetHisFormDefinitionQuery("KSK-TREN18TUOI", DivisionId, "", "TRACE-01"));

        Assert.False(result.IsSuccess);
        Assert.Equal(ApplicationFailureCode.Unauthorized, result.Failure.Code);
        Assert.Equal("Thiếu thông tin xác thực HIS trong request", result.Failure.Message);
    }

    [Fact]
    public async Task Non_matching_template_code_returns_not_found()
    {
        var client = new FakeHisEmrClient();
        var cache = new FakeHisFormDefinitionCache();
        var handler = new GetHisFormDefinitionHandler(client, cache);

        var result = await handler.HandleAsync(
            new GetHisFormDefinitionQuery("OTHER-TEMPLATE", DivisionId, ValidToken, "TRACE-01"));

        Assert.False(result.IsSuccess);
        Assert.Equal(ApplicationFailureCode.NotFound, result.Failure.Code);
        Assert.Equal("Chỉ hỗ trợ biểu mẫu KSK-TREN18TUOI", result.Failure.Message);
    }

    [Fact]
    public async Task Record_not_found_returns_not_found()
    {
        var recordRepo = new FakeExamRecordRepository(); // empty
        var defHandler = CreateDefinitionHandler();
        var client = new FakeHisEmrClient();
        var validator = new HisProcessValidator(recordRepo, defHandler, client);
        var handler = new GetHisProcessHandler(validator);

        var result = await handler.HandleAsync(
            new GetHisProcessQuery(DivisionId, Guid.NewGuid(), Guid.NewGuid(), ValidToken, "TRACE-01"));

        Assert.False(result.IsSuccess);
        Assert.Equal(ApplicationFailureCode.NotFound, result.Failure.Code);
        Assert.Equal("Không tìm thấy hồ sơ khám", result.Failure.Message);
    }

    [Fact]
    public async Task Admission_not_linked_returns_bad_request()
    {
        var record = new ExamRecord
        {
            RecordID = Guid.NewGuid(),
            DivisionID = DivisionId,
            RecordCode = "REC-01",
            AdmissionID = null // No admission
        };
        var recordRepo = new FakeExamRecordRepository(record);
        var defHandler = CreateDefinitionHandler();
        var client = new FakeHisEmrClient();
        var handler = new ListHisProcessesHandler(recordRepo, defHandler, client);

        var result = await handler.HandleAsync(
            new ListHisProcessesQuery(DivisionId, record.RecordID, ValidToken, "TRACE-01"));

        Assert.False(result.IsSuccess);
        Assert.Equal(ApplicationFailureCode.BadRequest, result.Failure.Code);
        Assert.Equal("Hồ sơ chưa được liên kết với lượt tiếp nhận HIS", result.Failure.Message);
    }

    [Fact]
    public async Task Unowned_process_returns_not_found()
    {
        var recordId = Guid.NewGuid();
        var record = new ExamRecord
        {
            RecordID = recordId,
            DivisionID = DivisionId,
            RecordCode = "REC-01",
            AdmissionID = 9999
        };
        var recordRepo = new FakeExamRecordRepository(record);
        var defHandler = CreateDefinitionHandler();
        var client = new FakeHisEmrClient();
        // Client returns empty processes list
        client.Responses[HisOperation.ListProcesses] = HisClientResult<HisJsonDocument>.Success(new HisJsonDocument("[]"));

        var validator = new HisProcessValidator(recordRepo, defHandler, client);
        var handler = new GetHisProcessHandler(validator);

        var result = await handler.HandleAsync(
            new GetHisProcessQuery(DivisionId, recordId, Guid.NewGuid(), ValidToken, "TRACE-01"));

        Assert.False(result.IsSuccess);
        Assert.Equal(ApplicationFailureCode.NotFound, result.Failure.Code);
        Assert.Equal("Quy trình khám không thuộc hồ sơ này hoặc không đúng biểu mẫu KSK", result.Failure.Message);
    }

    [Fact]
    public async Task Multiple_active_definitions_returns_invalid_state()
    {
        var client = new FakeHisEmrClient();
        client.Responses[HisOperation.ListDefinitions] = HisClientResult<HisJsonDocument>.Success(
            new HisJsonDocument($@"
            [
                {{ ""Id"": ""{Guid.NewGuid()}"", ""TemplateCode"": ""KSK-TREN18TUOI"", ""Active"": true }},
                {{ ""Id"": ""{Guid.NewGuid()}"", ""TemplateCode"": ""ksk-tren18tuoi"", ""Active"": true }}
            ]"));
        var cache = new FakeHisFormDefinitionCache();
        var handler = new GetHisFormDefinitionHandler(client, cache);

        var result = await handler.HandleAsync(
            new GetHisFormDefinitionQuery("KSK-TREN18TUOI", DivisionId, ValidToken, "TRACE-01"));

        Assert.False(result.IsSuccess);
        Assert.Equal(ApplicationFailureCode.InvalidState, result.Failure.Code);
        Assert.Equal("Tìm thấy nhiều hơn một biểu mẫu KSK-TREN18TUOI đang hoạt động trên HIS", result.Failure.Message);
    }

    [Fact]
    public async Task No_active_definitions_returns_not_found()
    {
        var client = new FakeHisEmrClient();
        client.Responses[HisOperation.ListDefinitions] = HisClientResult<HisJsonDocument>.Success(
            new HisJsonDocument(@"
            [
                { ""Id"": ""1"", ""TemplateCode"": ""OTHER"", ""Active"": true },
                { ""Id"": ""2"", ""TemplateCode"": ""KSK-TREN18TUOI"", ""Active"": false }
            ]"));
        var cache = new FakeHisFormDefinitionCache();
        var handler = new GetHisFormDefinitionHandler(client, cache);

        var result = await handler.HandleAsync(
            new GetHisFormDefinitionQuery("KSK-TREN18TUOI", DivisionId, ValidToken, "TRACE-01"));

        Assert.False(result.IsSuccess);
        Assert.Equal(ApplicationFailureCode.NotFound, result.Failure.Code);
        Assert.Equal("Không tìm thấy biểu mẫu KSK-TREN18TUOI đang hoạt động trên HIS", result.Failure.Message);
    }

    [Fact]
    public async Task Get_section_record_not_found_returns_not_found()
    {
        var recordRepo = new FakeExamRecordRepository();
        var defHandler = CreateDefinitionHandler();
        var handler = new GetHisRecordSectionHandler(recordRepo, defHandler);

        var result = await handler.HandleAsync(
            new GetHisRecordSectionQuery(DivisionId, Guid.NewGuid(), 73, ValidToken, "TRACE-01"));

        Assert.False(result.IsSuccess);
        Assert.Equal(ApplicationFailureCode.NotFound, result.Failure.Code);
        Assert.Equal("Không tìm thấy hồ sơ khám", result.Failure.Message);
    }

    [Fact]
    public async Task Get_section_not_in_form_details_returns_not_found()
    {
        var recordId = Guid.NewGuid();
        var record = new ExamRecord
        {
            RecordID = recordId,
            DivisionID = DivisionId,
            RecordCode = "REC-01",
            AdmissionID = 9999
        };
        var recordRepo = new FakeExamRecordRepository(record);
        var defHandler = CreateDefinitionHandler();
        var handler = new GetHisRecordSectionHandler(recordRepo, defHandler);

        var result = await handler.HandleAsync(
            new GetHisRecordSectionQuery(DivisionId, recordId, 73, ValidToken, "TRACE-01"));

        Assert.False(result.IsSuccess);
        Assert.Equal(ApplicationFailureCode.NotFound, result.Failure.Code);
        Assert.Equal("Không tìm thấy phần khám '73' trong biểu mẫu KSK", result.Failure.Message);
    }

    [Fact]
    public async Task Get_section_filters_layout_matching_item_group_id()
    {
        var recordId = Guid.NewGuid();
        var record = new ExamRecord
        {
            RecordID = recordId,
            DivisionID = DivisionId,
            RecordCode = "REC-01",
            AdmissionID = 9999
        };
        var recordRepo = new FakeExamRecordRepository(record);
        var defResult = new HisFormDefinitionResult(
            ValidTemplateId,
            "KSK-TREN18TUOI",
            "Phiếu khám KSK trên 18 tuổi",
            ValidDocTypeId,
            "V1",
            false,
            true,
            @"[ { ""ItemGroupID"": 73, ""Title"": ""Khám Mắt"" }, { ""ItemGroupID"": 74, ""Title"": ""Khám Tai Mũi Họng"" } ]",
            @"[ { ""ItemGroupID"": 73, ""Label"": ""Thị lực mắt phải"" }, { ""ItemGroupID"": 74, ""Label"": ""Tai trái"" }, { ""ItemGroupID"": 73, ""Label"": ""Thị lực mắt trái"" } ]");
        var defHandler = new FakeDefinitionHandler(defResult);
        var handler = new GetHisRecordSectionHandler(recordRepo, defHandler);

        var result = await handler.HandleAsync(
            new GetHisRecordSectionQuery(DivisionId, recordId, 73, ValidToken, "TRACE-01"));

        Assert.True(result.IsSuccess);
        Assert.Equal(recordId, result.Value.RecordId);
        Assert.Equal(9999, result.Value.AdmissionId);
        Assert.Equal(73, result.Value.ItemGroupId);

        var parsedLayout = System.Text.Json.Nodes.JsonNode.Parse(result.Value.LayoutJson) as System.Text.Json.Nodes.JsonArray;
        Assert.NotNull(parsedLayout);
        Assert.Equal(2, parsedLayout.Count);
        Assert.Equal("Thị lực mắt phải", parsedLayout[0]?["Label"]?.ToString());
        Assert.Equal("Thị lực mắt trái", parsedLayout[1]?["Label"]?.ToString());
    }

    [Fact]
    public async Task Get_section_hydrates_values_and_text_from_his_emr_data()
    {
        var recordId = Guid.NewGuid();
        var itemEyeRight = Guid.NewGuid();
        var itemEyeLeft = Guid.NewGuid();
        var record = new ExamRecord
        {
            RecordID = recordId,
            DivisionID = DivisionId,
            RecordCode = "REC-01",
            AdmissionID = 9999,
            HisEmrDataID = Guid.NewGuid()
        };
        var recordRepo = new FakeExamRecordRepository(record);
        var defResult = new HisFormDefinitionResult(
            ValidTemplateId,
            "KSK-TREN18TUOI",
            "Phiếu khám KSK trên 18 tuổi",
            ValidDocTypeId,
            "V1",
            false,
            true,
            @"[ { ""ItemGroupID"": 73, ""Title"": ""Khám Mắt"" } ]",
            $@"[
                {{ ""ItemGroupID"": 73, ""ItemID"": ""{itemEyeRight}"", ""Label"": ""Thị lực mắt phải"", ""Value"": null }},
                {{ ""ItemGroupID"": 73, ""ItemID"": ""{itemEyeLeft}"", ""Label"": ""Thị lực mắt trái"", ""Value"": null }}
            ]");
        var defHandler = new FakeDefinitionHandler(defResult);

        var hisClient = new FakeHisEmrClient();
        var remrJson = $@"{{
            ""EMRDataID"": ""{record.HisEmrDataID}"",
            ""Details"": [
                {{ ""ItemID"": ""{itemEyeRight}"", ""Value"": ""10/10"", ""Text"": ""Mười phần mười"" }},
                {{ ""ItemID"": ""{itemEyeLeft}"", ""Value"": ""9/10"", ""Text"": ""Chín phần mười"" }}
            ]
        }}";
        hisClient.Responses[HisOperation.ReadFormData] =
            HisClientResult<HisJsonDocument>.Success(new HisJsonDocument(remrJson));

        var handler = new GetHisRecordSectionHandler(recordRepo, defHandler, hisClient);

        var result = await handler.HandleAsync(
            new GetHisRecordSectionQuery(DivisionId, recordId, 73, ValidToken, "TRACE-01"));

        Assert.True(result.IsSuccess);
        var parsedLayout = System.Text.Json.Nodes.JsonNode.Parse(result.Value.LayoutJson) as System.Text.Json.Nodes.JsonArray;
        Assert.NotNull(parsedLayout);
        Assert.Equal("10/10", parsedLayout[0]?["Value"]?.ToString());
        Assert.Equal("Mười phần mười", parsedLayout[0]?["Text"]?.ToString());
        Assert.Equal("9/10", parsedLayout[1]?["Value"]?.ToString());
        Assert.Equal("Chín phần mười", parsedLayout[1]?["Text"]?.ToString());
    }

    [Fact]
    public async Task Get_section_populates_icd10_choices_for_cmb_node()
    {
        var recordId = Guid.NewGuid();
        var itemDiag = Guid.NewGuid();
        var record = new ExamRecord
        {
            RecordID = recordId,
            DivisionID = DivisionId,
            RecordCode = "REC-01",
            AdmissionID = 9999
        };
        var recordRepo = new FakeExamRecordRepository(record);
        var defResult = new HisFormDefinitionResult(
            ValidTemplateId,
            "KSK-TREN18TUOI",
            "Phiếu khám KSK trên 18 tuổi",
            ValidDocTypeId,
            "V1",
            false,
            true,
            @"[ { ""ItemGroupID"": 73, ""Title"": ""Khám Mắt"" } ]",
            $@"[
                {{ ""ItemGroupID"": 73, ""ItemID"": ""{itemDiag}"", ""ControlType"": ""CMB"", ""Choices"": [] }}
            ]");
        var defHandler = new FakeDefinitionHandler(defResult);

        var icdHandler = new FakeIcd10Handler(new List<Icd10Choice>
        {
            new("Z01.0", "Z01.0 - Khám mắt", "Khám mắt", "Z01.0 - Khám mắt")
        });

        var handler = new GetHisRecordSectionHandler(recordRepo, defHandler, icd10Handler: icdHandler);

        var result = await handler.HandleAsync(
            new GetHisRecordSectionQuery(DivisionId, recordId, 73, ValidToken, "TRACE-01"));

        Assert.True(result.IsSuccess);
        var parsedLayout = System.Text.Json.Nodes.JsonNode.Parse(result.Value.LayoutJson) as System.Text.Json.Nodes.JsonArray;
        Assert.NotNull(parsedLayout);
        var choices = parsedLayout[0]?["Choices"] as System.Text.Json.Nodes.JsonArray;
        Assert.NotNull(choices);
        Assert.Single(choices);
        Assert.Equal("Z01.0", choices[0]?["Code"]?.ToString());
        Assert.Equal("Z01.0 - Khám mắt", choices[0]?["Label"]?.ToString());
    }

    private sealed class FakeIcd10Handler : IGetIcd10ChoicesHandler
    {
        private readonly IReadOnlyList<Icd10Choice> _choices;
        public FakeIcd10Handler(IReadOnlyList<Icd10Choice> choices) => _choices = choices;
        public Task<ApplicationResult<IReadOnlyList<Icd10Choice>>> HandleAsync(
            GetIcd10ChoicesQuery query, CancellationToken ct = default)
            => Task.FromResult(ApplicationResult<IReadOnlyList<Icd10Choice>>.Success(_choices));
    }

    [Fact]
    public async Task Missing_patient_his_id_creates_and_links_patient()
    {
        var patient = new Patient
        {
            PatientRefID = Guid.NewGuid(), DivisionID = "D01", PatientCode = "P01",
            FullName = "Nguyễn Văn A", GenderID = 1
        };
        var repo = new FakePatientRepository(patient);
        var his = new FakeHisEmrClient { PatientId = 9001 };
        var operation = new EnsureHisPatient(repo, his, new FixedClock());

        var result = await operation.HandleAsync(new EnsureHisPatientCommand(
            "D01", patient.PatientRefID,
            new HisCallContext("credential", "trace-1", "D01"), 7, ActorKind.Employee));

        Assert.True(result.IsSuccess);
        Assert.Equal(9001, patient.HisPatientID);
        Assert.Equal("Linked", patient.HisSyncStatus);
    }

    [Fact]
    public async Task Missing_admission_creates_and_links_admission_from_normalized_patient()
    {
        var record = new ExamRecord
        {
            RecordID = Guid.NewGuid(), DivisionID = "D01", PatientRefID = Guid.NewGuid(),
            Patient = new Patient
            {
                PatientRefID = Guid.NewGuid(), DivisionID = "D01", PatientCode = "P01",
                FullName = "Nguyễn Văn A", GenderID = 1
            },
            Session = new ExamSession { ExamDate = new DateOnly(2026, 9, 12), DepartmentID = 15 }
        };
        var repo = new FakeExamRecordRepository();
        repo.Records[record.RecordID] = record;
        var his = new FakeHisEmrClient { AdmissionId = 7001 };
        var operation = new EnsureHisAdmission(
            repo, his, new FakeHisCredentialOptions { KskDepartmentId = 15, KskDepartmentCode = "KSK" }, new FixedClock());

        var result = await operation.HandleAsync(new EnsureHisAdmissionCommand(
            "D01", record.RecordID,
            new HisCallContext("credential", "trace-1", "D01"), 7, ActorKind.Employee));

        Assert.True(result.IsSuccess);
        Assert.Equal(7001, record.AdmissionID);
        Assert.Equal("P01", his.LastAdmissionRequest.Patient.PatientCode);
    }

    private static IGetHisFormDefinitionHandler CreateDefinitionHandler()
    {
        return new FakeDefinitionHandler(new HisFormDefinitionResult(
            ValidTemplateId,
            "KSK-TREN18TUOI",
            "Phiếu khám KSK trên 18 tuổi",
            ValidDocTypeId,
            "V1",
            false,
            true,
            "[]",
            "[]"));
    }
}

// ---------------------------------------------------------------- FAKES
public class FakeDefinitionHandler : IGetHisFormDefinitionHandler
{
    private readonly HisFormDefinitionResult _result;

    public FakeDefinitionHandler(HisFormDefinitionResult result) => _result = result;

    public Task<ApplicationResult<HisFormDefinitionResult>> HandleAsync(
        GetHisFormDefinitionQuery query, CancellationToken ct = default)
    {
        if (query.TemplateCode != "KSK-TREN18TUOI")
            return Task.FromResult(ApplicationResult<HisFormDefinitionResult>.Fail(ApplicationFailureCode.NotFound, "Chỉ hỗ trợ biểu mẫu KSK-TREN18TUOI"));
        return Task.FromResult(ApplicationResult<HisFormDefinitionResult>.Success(_result));
    }
}

public class FakeHisFormDefinitionCache : IHisFormDefinitionCache
{
    public Dictionary<string, HisFormDefinitionResult> Store { get; } = new();

    public async Task<HisFormDefinitionResult> GetOrCreateAsync(
        string divisionId,
        string templateCode,
        Func<CancellationToken, Task<HisFormDefinitionResult>> factory,
        CancellationToken ct = default)
    {
        var key = $"{divisionId}|{templateCode}";
        if (Store.TryGetValue(key, out var cached)) return cached;

        var created = await factory(ct);
        Store[key] = created;
        return created;
    }
}

public class FakeHisEmrClient : IHisEmrClient
{
    public Dictionary<HisOperation, HisClientResult<HisJsonDocument>> Responses { get; } = new();
    public Dictionary<HisOperation, Queue<HisClientResult<HisJsonDocument>>> QueuedResponses { get; } = new();
    public List<HisRequest> SentRequests { get; } = new();
    public long PatientId { get; set; } = 9001;
    public long AdmissionId { get; set; } = 7001;
    public HisPatientCreateRequest LastPatientRequest { get; set; }
    public HisAdmissionCreateRequest LastAdmissionRequest { get; set; }
    public HisCallContext LastContext { get; set; }

    public Task<HisClientResult<HisJsonDocument>> SendAsync(
        HisOperation operation, HisRequest request, CancellationToken ct = default)
    {
        SentRequests.Add(request);
        if (QueuedResponses.TryGetValue(operation, out var queue) && queue.Count > 0)
            return Task.FromResult(queue.Dequeue());

        if (Responses.TryGetValue(operation, out var res))
            return Task.FromResult(res);

        return Task.FromResult(HisClientResult<HisJsonDocument>.Success(new HisJsonDocument("{}")));
    }

    public Task<HisClientResult<long>> CreatePatientAsync(
        HisPatientCreateRequest request, HisCallContext context, CancellationToken ct = default)
    {
        LastPatientRequest = request;
        LastContext = context;
        if (PatientId <= 0)
            return Task.FromResult(HisClientResult<long>.Fail(HisClientOutcome.BadGateway, "HIS trả về PatientID không hợp lệ"));
        return Task.FromResult(HisClientResult<long>.Success(PatientId));
    }

    public Task<HisClientResult<long>> CreateAdmissionAsync(
        HisAdmissionCreateRequest request, HisCallContext context, CancellationToken ct = default)
    {
        LastAdmissionRequest = request;
        LastContext = context;
        if (AdmissionId <= 0)
            return Task.FromResult(HisClientResult<long>.Fail(HisClientOutcome.BadGateway, "HIS trả về AdmissionID không hợp lệ"));
        return Task.FromResult(HisClientResult<long>.Success(AdmissionId));
    }
}
