using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HealthExam.Application.Common;
using HealthExam.Application.His;
using HealthExam.Application.Integrations;
using HealthExam.Application.RegistrationForms;
using HealthExam.Domain.Common;
using HealthExam.Domain.ExamForms;
using HealthExam.Domain.ExamRecords;
using HealthExam.Domain.ExamSessions;
using HealthExam.Domain.Patients;
using Xunit;

namespace HealthExam.Tests.Application;

public class RegistrationFormHandlerTests
{
    private sealed class FakeRegistrationFormRepository : IRegistrationFormRepository
    {
        public readonly List<ExamGroupFormMapping> Mappings = new();
        public readonly List<ExamRecord> Records = new();

        public Task<ExamGroupFormMapping> GetActiveMappingAsync(string divisionId, string variantCode, CancellationToken ct = default)
        {
            var m = Mappings.FirstOrDefault(x => x.DivisionID == divisionId && x.VariantCode == variantCode && x.IsActive);
            return Task.FromResult(m);
        }

        public Task<IReadOnlyList<string>> ListActiveVariantCodesAsync(string divisionId, CancellationToken ct = default)
        {
            IReadOnlyList<string> list = Mappings.Where(x => x.DivisionID == divisionId && x.IsActive)
                .Select(x => x.VariantCode)
                .Distinct()
                .ToList();
            return Task.FromResult(list);
        }

        public Task<ExamRecord> GetRecordAsync(string divisionId, Guid recordId, bool forUpdate = false, CancellationToken ct = default)
        {
            var r = Records.FirstOrDefault(x => x.DivisionID == divisionId && x.RecordID == recordId);
            return Task.FromResult(r);
        }

        public Task<ExamRecord> GetRecordWithSessionAsync(string divisionId, Guid recordId, bool forUpdate = false, CancellationToken ct = default)
        {
            var r = Records.FirstOrDefault(x => x.DivisionID == divisionId && x.RecordID == recordId);
            return Task.FromResult(r);
        }
    }

    private sealed class FakeHisClient : IHisEmrClient
    {
        public Func<HisOperation, HisRequest, HisClientResult<HisJsonDocument>> SendHandler { get; set; }
        public Func<Guid, HisRequest, HisClientResult<byte[]>> RenderHandler { get; set; }
        public Func<long, HisRequest, HisClientResult<byte[]>> SignedRenderHandler { get; set; }

        public int DraftPreviewCalls { get; private set; }
        public int SignedPreviewCalls { get; private set; }

        public Task<HisClientResult<HisJsonDocument>> SendAsync(HisOperation operation, HisRequest request, CancellationToken ct = default)
        {
            if (SendHandler != null) return Task.FromResult(SendHandler(operation, request));
            return Task.FromResult(HisClientResult<HisJsonDocument>.Success(new HisJsonDocument("{}")));
        }

        public Task<HisClientResult<byte[]>> RenderFormPdfAsync(Guid emrDataId, HisRequest request, CancellationToken ct = default)
        {
            DraftPreviewCalls++;
            if (RenderHandler != null) return Task.FromResult(RenderHandler(emrDataId, request));
            return Task.FromResult(HisClientResult<byte[]>.Success(Encoding.ASCII.GetBytes("%PDF-1.7 draft")));
        }

        public Task<HisClientResult<byte[]>> RenderSignedAdmissionPdfAsync(long admissionId, HisRequest request, CancellationToken ct = default)
        {
            SignedPreviewCalls++;
            if (SignedRenderHandler != null) return Task.FromResult(SignedRenderHandler(admissionId, request));
            return Task.FromResult(HisClientResult<byte[]>.Success(Encoding.ASCII.GetBytes("%PDF-1.7 signed")));
        }

        public Task<HisClientResult<long>> CreateAdmissionAsync(HisAdmissionCreateRequest request, HisCallContext context, CancellationToken ct = default)
        {
            if (SendHandler != null)
            {
                var sendRes = SendHandler(HisOperation.CreateAdmission, new HisRequest("/api/v1/Admission/Add", "POST", context?.Credential, TraceId: context?.TraceId, DivisionId: context?.DivisionId));
                if (sendRes.IsSuccess && long.TryParse(sendRes.Value?.RawJson?.Trim('"', ' '), out var id))
                {
                    return Task.FromResult(HisClientResult<long>.Success(id));
                }
            }
            return Task.FromResult(HisClientResult<long>.Success(1001L));
        }

        public Task<HisClientResult<long>> CreatePatientAsync(HisPatientCreateRequest request, HisCallContext context, CancellationToken ct = default)
        {
            return Task.FromResult(HisClientResult<long>.Success(2002L));
        }
    }

    private sealed class FakeHisFormDefinitionCache : IHisFormDefinitionCache
    {
        private readonly Dictionary<string, HisFormDefinitionResult> _cache = new();
        public async Task<HisFormDefinitionResult> GetOrCreateAsync(
            string divisionId, string templateCode, Func<CancellationToken, Task<HisFormDefinitionResult>> factory, CancellationToken ct = default)
        {
            var key = $"{divisionId}:{templateCode}";
            if (_cache.TryGetValue(key, out var val)) return val;
            val = await factory(ct);
            _cache[key] = val;
            return val;
        }
    }

    private sealed class FakeUnitOfWork : IUnitOfWork
    {
        public Task<IApplicationTransaction> BeginAsync(CancellationToken ct = default) => Task.FromResult<IApplicationTransaction>(new FakeTransaction());
        public Task<PersistenceSaveResult> SaveChangesAsync(CancellationToken ct = default) => Task.FromResult(new PersistenceSaveResult(PersistenceSaveOutcome.Saved));
        public void DiscardPendingChanges() { }
        private sealed class FakeTransaction : IApplicationTransaction
        {
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
            public Task CommitAsync(CancellationToken ct = default) => Task.CompletedTask;
            public Task RollbackAsync(CancellationToken ct = default) => Task.CompletedTask;
        }
    }

    private sealed class FakeHisCredentialOptions : IHisCredentialOptions
    {
        public string CredentialHeaderName => "Authorization";
        public int? KskDepartmentId => 10;
        public string KskDepartmentCode => "K01";
    }

    private readonly FakeRegistrationFormRepository _repo = new();
    private readonly FakeHisClient _his = new();
    private readonly FakeHisFormDefinitionCache _cache = new();
    private readonly FakeUnitOfWork _uow = new();
    private readonly FakeHisCredentialOptions _options = new();

    private readonly Guid _templateId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private readonly Guid _field1Id = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private readonly Guid _field2Id = Guid.Parse("33333333-3333-3333-3333-333333333333");

    public RegistrationFormHandlerTests()
    {
        var mapping = new ExamGroupFormMapping
        {
            DivisionID = "DEV",
            VariantCode = "DTK_03",
            TemplateCode = "KSK-TREN18TUOI",
            IsActive = true,
            Sections = new List<ExamGroupFormSectionMapping>
            {
                new() { SectionKind = "HISTORY", ItemGroupID = 64 },
                new() { SectionKind = "EXTRA_INFO", ItemGroupID = 63 }
            }
        };
        _repo.Mappings.Add(mapping);

        _his.SendHandler = (op, req) =>
        {
            if (op == HisOperation.ListDefinitions)
            {
                var json = $"[{{\"TemplateCode\": \"KSK-TREN18TUOI\", \"Active\": true, \"Id\": \"{_templateId}\"}}]";
                return HisClientResult<HisJsonDocument>.Success(new HisJsonDocument(json));
            }
            if (op == HisOperation.GetDefinition)
            {
                var json = "{\"TemplateCode\": \"KSK-TREN18TUOI\", \"TemplateName\": \"Khám sức khỏe trên 18 tuổi\", \"Active\": true, \"Details\": [{\"ItemGroupID\": 64, \"ItemGroupName\": \"Tiền sử bệnh\"}, {\"ItemGroupID\": 63, \"ItemGroupName\": \"Thông tin bổ sung\"}]}";
                return HisClientResult<HisJsonDocument>.Success(new HisJsonDocument(json));
            }
            if (op == HisOperation.GetDefinitionLayout)
            {
                var json = $"[{{\"ItemGroupID\": 64, \"ItemID\": \"{_field1Id}\", \"Label\": \"Bệnh tim mạch\", \"ControlType\": \"TXT\", \"DataType\": \"S\", \"ReadOnly\": false, \"OrderNo\": 1}}, {{\"ItemGroupID\": 63, \"ItemID\": \"{_field2Id}\", \"Label\": \"Tiền sử gia đình\", \"ControlType\": \"TXT\", \"DataType\": \"S\", \"ReadOnly\": false, \"OrderNo\": 1}}]";
                return HisClientResult<HisJsonDocument>.Success(new HisJsonDocument(json));
            }
            if (op == HisOperation.ReadFormData)
            {
                var json = $"{{\"EMRDataID\": \"44444444-4444-4444-4444-444444444444\", \"Details\": [{{\"ItemID\": \"{_field1Id}\", \"Value\": \"Không\", \"Text\": \"Không có bệnh\"}}]}}";
                return HisClientResult<HisJsonDocument>.Success(new HisJsonDocument(json));
            }
            if (op == HisOperation.CreateAdmission)
            {
                return HisClientResult<HisJsonDocument>.Success(new HisJsonDocument("1001"));
            }
            if (op == HisOperation.SaveFormData)
            {
                return HisClientResult<HisJsonDocument>.Success(new HisJsonDocument("\"55555555-5555-5555-5555-555555555555\""));
            }
            return HisClientResult<HisJsonDocument>.Success(new HisJsonDocument("{}"));
        };
    }

    [Fact]
    public async Task GetExamGroupRegistrationForms_returns_normalized_sections()
    {
        var handler = new GetExamGroupRegistrationFormsHandler(_repo, _his, _cache);
        var res = await handler.HandleAsync(new GetExamGroupRegistrationFormsQuery("DEV", "DTK_03"));

        Assert.True(res.IsSuccess);
        Assert.Equal("DTK_03", res.Value.VariantCode);
        Assert.Equal("KSK-TREN18TUOI", res.Value.TemplateCode);
        Assert.Equal(2, res.Value.Sections.Count);

        var hist = res.Value.Sections.First(s => s.Kind == "HISTORY");
        Assert.Equal(64, hist.ItemGroupID);
        Assert.Single(hist.Nodes);
        Assert.Equal(_field1Id, hist.Nodes[0].ItemID);
        Assert.Equal("Bệnh tim mạch", hist.Nodes[0].Label);
    }

    [Fact]
    public async Task GetExamGroupRegistrationForms_returns_NotFound_for_invalid_variant()
    {
        var handler = new GetExamGroupRegistrationFormsHandler(_repo, _his, _cache);
        var res = await handler.HandleAsync(new GetExamGroupRegistrationFormsQuery("DEV", "UNKNOWN"));

        Assert.False(res.IsSuccess);
        Assert.Equal(ApplicationFailureCode.NotFound, res.Failure.Code);
    }

    [Fact]
    public async Task ListAvailableExamGroups_returns_active_ordered_groups()
    {
        var handler = new ListAvailableExamGroupsHandler(_repo);
        var res = await handler.HandleAsync(new ListAvailableExamGroupsQuery("DEV"));

        Assert.True(res.IsSuccess);
        Assert.Contains(res.Value, x => x.VariantCode == "DTK_03");
    }

    [Fact]
    public async Task GetRegistrationForm_merges_remr_values()
    {
        var recordId = Guid.NewGuid();
        var record = new ExamRecord
        {
            DivisionID = "DEV",
            RecordID = recordId,
            VariantCode = "DTK_03",
            AdmissionID = 999,
            HisEmrDataID = Guid.Parse("44444444-4444-4444-4444-444444444444")
        };
        _repo.Records.Add(record);

        var handler = new GetRegistrationFormHandler(_repo, _his, _cache, _uow);
        var res = await handler.HandleAsync(new GetRegistrationFormQuery("DEV", recordId));

        Assert.True(res.IsSuccess);
        Assert.Equal(recordId, res.Value.RecordID);
        var hist = res.Value.Sections.First(s => s.Kind == "HISTORY");
        Assert.Equal("Không", hist.Nodes[0].Value);
        Assert.Equal("Không có bệnh", hist.Nodes[0].Text);
    }

    [Fact]
    public async Task SaveRegistrationFormSection_rejects_invalid_section_kind()
    {
        var recordId = Guid.NewGuid();
        var record = new ExamRecord
        {
            DivisionID = "DEV",
            RecordID = recordId,
            VariantCode = "DTK_03"
        };
        _repo.Records.Add(record);

        var getHandler = new GetRegistrationFormHandler(_repo, _his, _cache, _uow);
        var handler = new SaveRegistrationFormSectionHandler(_repo, _his, _cache, _options, _uow, getHandler);

        var cmd = new SaveRegistrationFormSectionCommand(
            "DEV", recordId, "INVALID", new RegistrationSectionSaveRequest(), "1", ActorKind.Employee);

        var res = await handler.HandleAsync(cmd);
        Assert.False(res.IsSuccess);
        Assert.Equal(ApplicationFailureCode.BadRequest, res.Failure.Code);
    }

    [Fact]
    public async Task SaveRegistrationFormSection_ensures_admission_and_saves_data()
    {
        var recordId = Guid.NewGuid();
        var session = new ExamSession
        {
            SessionID = Guid.NewGuid(),
            DivisionID = "DEV",
            ExamDate = DateOnly.FromDateTime(DateTime.UtcNow),
            DepartmentID = 10
        };
        var patient = new Patient
        {
            PatientRefID = Guid.NewGuid(),
            DivisionID = "DEV",
            PatientCode = "BN001",
            FullName = "Nguyen Van Test",
            GenderID = 1
        };
        var record = new ExamRecord
        {
            DivisionID = "DEV",
            RecordID = recordId,
            SessionID = session.SessionID,
            Session = session,
            VariantCode = "DTK_03",
            PatientRefID = patient.PatientRefID,
            Patient = patient,
            AdmissionID = null
        };
        _repo.Records.Add(record);

        var getHandler = new GetRegistrationFormHandler(_repo, _his, _cache, _uow);
        var handler = new SaveRegistrationFormSectionHandler(_repo, _his, _cache, _options, _uow, getHandler);

        var fields = new List<RegistrationFormFieldValue>
        {
            new(_field1Id, "Bình thường", "Không phát hiện bất thường")
        };
        var cmd = new SaveRegistrationFormSectionCommand(
            "DEV", recordId, "HISTORY", new RegistrationSectionSaveRequest(fields, isDraft: false), "1", ActorKind.Employee);

        var res = await handler.HandleAsync(cmd);

        Assert.True(res.IsSuccess);
        Assert.Equal(1001, record.AdmissionID);
        Assert.Equal(Guid.Parse("55555555-5555-5555-5555-555555555555"), record.HisEmrDataID);
        Assert.Equal("Synced", record.HisFormSyncStatus);
    }

    /// <summary>
    /// Preview không phải HIS-era nữa: hồ sơ chưa Signed luôn render bản nháp từ HIS, bất kể
    /// còn sót SignedFilePath cũ hay không — chỉ SignStatus mới quyết định nhánh.
    /// </summary>
    [Fact]
    public async Task Preview_WithoutSignedFilePath_falls_back_to_draft()
    {
        var recordId = Guid.NewGuid();
        var record = new ExamRecord
        {
            DivisionID = "DEV",
            RecordID = recordId,
            AdmissionID = 12345L,
            HisEmrDataID = Guid.NewGuid(),
            SignedFilePath = null
        };
        _repo.Records.Add(record);

        var handler = new PreviewRegistrationFormPdfHandler(_repo, _his, new HealthExam.Tests.Signing.FakeExamFileStore());

        var res = await handler.HandleAsync(new PreviewRegistrationFormPdfQuery("DEV", recordId));

        Assert.True(res.IsSuccess);
        Assert.Equal("%PDF-1.7 draft", Encoding.ASCII.GetString(res.Value));
        Assert.Equal(0, _his.SignedPreviewCalls);
        Assert.Equal(1, _his.DraftPreviewCalls);
    }

    [Fact]
    public async Task Preview_WithoutSignedFilePath_fails_if_emrDataId_missing()
    {
        var recordId = Guid.NewGuid();
        var record = new ExamRecord
        {
            DivisionID = "DEV",
            RecordID = recordId,
            AdmissionID = 12345L,
            HisEmrDataID = null,
            SignedFilePath = null
        };
        _repo.Records.Add(record);

        var handler = new PreviewRegistrationFormPdfHandler(_repo, _his, new HealthExam.Tests.Signing.FakeExamFileStore());

        var res = await handler.HandleAsync(new PreviewRegistrationFormPdfQuery("DEV", recordId));

        Assert.False(res.IsSuccess);
        Assert.Equal(ApplicationFailureCode.InvalidState, res.Failure.Code);
        Assert.Equal(0, _his.SignedPreviewCalls);
        Assert.Equal(0, _his.DraftPreviewCalls);
    }
}
