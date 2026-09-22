#nullable enable

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using HealthExam.API.Contracts;
using HealthExam.API.Controllers;
using HealthExam.API.Middlewares;
using HealthExam.Application.Common;
using HealthExam.Application.His;
using HealthExam.Application.Integrations;
using HealthExam.Domain.Common;
using HealthExam.Domain.ExamSessions;
using HealthExam.Domain.ExamRecords;
using HealthExam.Domain.Imports;
using HealthExam.Domain.Paraclinical;
using HealthExam.Domain.Webhooks;
using HealthExam.Domain.Integrations;
using HealthExam.Domain.Catalogs;
using HealthExam.Infrastructure.Integrations.HisEmr;
using HealthExam.Infrastructure.Persistence;
using HealthExam.Infrastructure.Persistence.Repositories;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace HealthExam.Tests;

public class HisExamFormServiceTests
{
    private static readonly Guid KskTemplateId = Guid.NewGuid();
    private const int KskFileDocTypeId = 101;

    private static HisFormDefinition CreateKskDefinition() => new()
    {
        TemplateId = KskTemplateId,
        TemplateCode = "KSK-TREN18TUOI",
        TemplateName = "Khám sức khỏe trên 18 tuổi",
        FileDocTypeId = KskFileDocTypeId,
        VersionCode = "V1",
        IsDraft = false,
        Active = true
    };

    private static (HisExamFormTestService service, FakeHisClient his, InMemoryTestDb db) CreateService(
        HisFormDefinition? definition = null)
    {
        var db = new InMemoryTestDb();
        db.Ctx.Headers["Authorization"] = "Bearer test-token";

        var fakeHis = new FakeHisClient();
        var def = definition ?? CreateKskDefinition();
        var defHandler = new FakeDefinitionHandler(def);

        var recordRepo = new ExamRecordRepository(db.Db);
        var sessionRepo = new ExamSessionRepository(db.Db);
        var audit = new AuditRepository(db.Db);
        var uow = new HealthExam.Infrastructure.Persistence.UnitOfWork(db.Db);

        var validator = new HisProcessValidator(recordRepo, defHandler, fakeHis);

        var getForm = new GetExamRecordHisFormHandler(recordRepo, defHandler);
        var listProcesses = new ListHisProcessesHandler(recordRepo, defHandler, fakeHis);
        var getProcess = new GetHisProcessHandler(validator);

        var service = new HisExamFormTestService(getForm, listProcesses, getProcess, db.Ctx);

        return (service, fakeHis, db);
    }

    [Fact]
    public async Task Unknown_or_cross_division_record_returns_4040()
    {
        var (service, _, db) = CreateService();
        using (db)
        {
            var ex1 = await Assert.ThrowsAsync<HealthExamException>(() =>
                service.GetFormAsync(Guid.NewGuid()));
            Assert.Equal(ErrorCodes.NotFound, ex1.ErrorCode);

            var session = db.SeedSession();
            var recordOther = db.SeedRecord(session.SessionID, divisionId: "OTHER_DIV");

            var ex2 = await Assert.ThrowsAsync<HealthExamException>(() =>
                service.GetFormAsync(recordOther.RecordID));
            Assert.Equal(ErrorCodes.NotFound, ex2.ErrorCode);
        }
    }

    [Fact]
    public async Task GetFormAsync_works_with_null_admission_and_returns_it()
    {
        var (service, _, db) = CreateService();
        using (db)
        {
            var session = db.SeedSession();
            var record = db.SeedRecord(session.SessionID);
            record.AdmissionID = null;
            await db.Db.SaveChangesAsync();

            var form = await service.GetFormAsync(record.RecordID);
            Assert.Equal(record.RecordID, form.RecordId);
            Assert.Null(form.AdmissionId);
            Assert.Equal(KskTemplateId, form.Definition.TemplateId);
        }
    }

    [Fact]
    public async Task Process_endpoints_require_positive_admission_id()
    {
        var (service, _, db) = CreateService();
        using (db)
        {
            var session = db.SeedSession();
            var record = db.SeedRecord(session.SessionID);
            record.AdmissionID = null;
            await db.Db.SaveChangesAsync();

            var processId = Guid.NewGuid();

            var ex1 = await Assert.ThrowsAsync<HealthExamException>(() => service.GetProcessesAsync(record.RecordID));
            Assert.Equal(ErrorCodes.BadRequest, ex1.ErrorCode);

            var ex2 = await Assert.ThrowsAsync<HealthExamException>(() => service.GetProcessAsync(record.RecordID, processId));
            Assert.Equal(ErrorCodes.BadRequest, ex2.ErrorCode);
        }
    }

    [Fact]
    public async Task Process_list_is_filtered_to_definition_template_or_file_doc_type()
    {
        var (service, his, db) = CreateService();
        using (db)
        {
            var session = db.SeedSession();
            var record = db.SeedRecord(session.SessionID, admissionId: 901);

            var proc1 = Guid.NewGuid();
            var proc2 = Guid.NewGuid();
            var procOther = Guid.NewGuid();

            his.Processes = JArray.Parse($@"[
                {{ ""Id"": ""{proc1}"", ""TemplateID"": ""{KskTemplateId}"", ""FileDocTypeID"": 0 }},
                {{ ""Id"": ""{proc2}"", ""TemplateID"": ""{Guid.NewGuid()}"", ""FileDocTypeID"": {KskFileDocTypeId} }},
                {{ ""Id"": ""{procOther}"", ""TemplateID"": ""{Guid.NewGuid()}"", ""FileDocTypeID"": 999 }}
            ]");

            var result = await service.GetProcessesAsync(record.RecordID);
            var arr = Assert.IsType<JArray>(result);
            Assert.Equal(2, arr.Count);
            Assert.Contains(arr, x => x["Id"]!.ToString() == proc1.ToString());
            Assert.Contains(arr, x => x["Id"]!.ToString() == proc2.ToString());
            Assert.DoesNotContain(arr, x => x["Id"]!.ToString() == procOther.ToString());
        }
    }

    [Fact]
    public async Task Unowned_or_mismatched_process_returns_4040()
    {
        var (service, his, db) = CreateService();
        using (db)
        {
            var session = db.SeedSession();
            var record = db.SeedRecord(session.SessionID, admissionId: 901);

            var unownedProcessId = Guid.NewGuid();
            his.Processes = JArray.Parse(@"[]"); // Empty list

            var ex = await Assert.ThrowsAsync<HealthExamException>(() =>
                service.GetProcessAsync(record.RecordID, unownedProcessId));
            Assert.Equal(ErrorCodes.NotFound, ex.ErrorCode);
            Assert.Equal(0, his.ProcessDetailCalls);
        }
    }

    private sealed class HisExamFormTestService
    {
        private readonly IGetExamRecordHisFormHandler _getForm;
        private readonly IListHisProcessesHandler _listProcesses;
        private readonly IGetHisProcessHandler _getProcess;
        private readonly FakeHealthExamContext _context;

        public HisExamFormTestService(
            IGetExamRecordHisFormHandler getForm,
            IListHisProcessesHandler listProcesses,
            IGetHisProcessHandler getProcess,
            FakeHealthExamContext context)
        {
            _getForm = getForm;
            _listProcesses = listProcesses;
            _getProcess = getProcess;
            _context = context;
        }

        private string Credential =>
            _context.Headers.TryGetValue("Authorization", out var c) ? c : "Bearer test-token";

        public async Task<ExamRecordHisForm> GetFormAsync(Guid recordId, CancellationToken ct = default)
        {
            var res = await _getForm.HandleAsync(new GetExamRecordHisFormQuery(_context.DivisionId, recordId, Credential, _context.TraceId), ct);
            if (!res.IsSuccess)
                throw new HealthExamException(ApplicationResultMapper.ToErrorCode(res.Failure.Code), res.Failure.Message);

            var def = HisFormController.MapToDto(res.Value.Definition);
            return new ExamRecordHisForm(res.Value.RecordId, res.Value.AdmissionId, def);
        }

        public async Task<JArray> GetProcessesAsync(Guid recordId, CancellationToken ct = default)
        {
            var res = await _listProcesses.HandleAsync(new ListHisProcessesQuery(_context.DivisionId, recordId, Credential, _context.TraceId), ct);
            if (!res.IsSuccess)
                throw new HealthExamException(ApplicationResultMapper.ToErrorCode(res.Failure.Code), res.Failure.Message);

            return JArray.Parse(res.Value.RawJson ?? "[]");
        }

        public async Task<JObject> GetProcessAsync(Guid recordId, Guid processId, CancellationToken ct = default)
        {
            var res = await _getProcess.HandleAsync(new GetHisProcessQuery(_context.DivisionId, recordId, processId, Credential, _context.TraceId), ct);
            if (!res.IsSuccess)
                throw new HealthExamException(ApplicationResultMapper.ToErrorCode(res.Failure.Code), res.Failure.Message);

            return JObject.Parse(res.Value.RawJson ?? "{}");
        }
    }

    private sealed class FakeDefinitionHandler : IGetHisFormDefinitionHandler
    {
        private readonly HisFormDefinition _def;
        public FakeDefinitionHandler(HisFormDefinition def) => _def = def;

        public Task<ApplicationResult<HisFormDefinitionResult>> HandleAsync(
            GetHisFormDefinitionQuery query, CancellationToken ct = default)
        {
            var res = new HisFormDefinitionResult(
                _def.TemplateId,
                _def.TemplateCode,
                _def.TemplateName,
                _def.FileDocTypeId,
                _def.VersionCode,
                _def.IsDraft,
                _def.Active,
                _def.Details?.ToString(Formatting.None) ?? "[]",
                _def.Layout?.ToString(Formatting.None) ?? "[]");
            return Task.FromResult(ApplicationResult<HisFormDefinitionResult>.Success(res));
        }
    }

    private sealed class FakeHisClient : IHisEmrClient
    {
        public JArray Processes { get; set; } = new();
        public JObject ProcessDetail { get; set; } = new();
        public Dictionary<HisOperation, Queue<HisClientResult<HisJsonDocument>>> QueuedResponses { get; } = new();
        public int ProcessDetailCalls { get; private set; }

        public Task<HisClientResult<HisJsonDocument>> SendAsync(
            HisOperation operation, HisRequest request, CancellationToken ct = default)
        {
            if (QueuedResponses.TryGetValue(operation, out var queue) && queue.Count > 0)
            {
                if (operation == HisOperation.GetProcess) ProcessDetailCalls++;
                return Task.FromResult(queue.Dequeue());
            }

            switch (operation)
            {
                case HisOperation.ListProcesses:
                    return Task.FromResult(HisClientResult<HisJsonDocument>.Success(new HisJsonDocument(Processes.ToString(Formatting.None))));

                case HisOperation.GetProcess:
                    ProcessDetailCalls++;
                    return Task.FromResult(HisClientResult<HisJsonDocument>.Success(new HisJsonDocument(ProcessDetail.ToString(Formatting.None))));

                default:
                    return Task.FromResult(HisClientResult<HisJsonDocument>.Fail(HisClientOutcome.BadGateway, "Unsupported operation"));
            }
        }
    }
}
