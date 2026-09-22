using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HealthExam.Application.Common;
using HealthExam.Application.ExamRecords;
using HealthExam.Application.ExamSessions;
using HealthExam.Application.His;
using HealthExam.Application.Integrations;
using HealthExam.Application.Patients;
using HealthExam.Application.RegistrationForms;
using HealthExam.Domain.Catalogs;
using HealthExam.Domain.Common;
using HealthExam.Domain.ExamForms;
using HealthExam.Domain.ExamRecords;
using HealthExam.Domain.ExamSessions;
using HealthExam.Domain.Patients;
using Xunit;

namespace HealthExam.Tests.Application;

public class ExamRecordHandlerTests
{
    private readonly FakeExamRecordRepository _recordRepo = new();
    private readonly FakeSessionRepoForRecordTests _sessionRepo = new();
    private readonly RecordFakeUnitOfWork _uow = new();
    private readonly FakeAuditRepository _audit = new();
    private readonly FakeRecordCodeAllocator _allocator = new();
    private readonly FakePatientRepository _patientRepo = new();
    private readonly FixedClock _clock = new();
    private readonly PatientRegistrationWriter _writer;

    public ExamRecordHandlerTests()
    {
        _writer = new PatientRegistrationWriter(_patientRepo, _uow, _clock);
    }

    // ---------------------------------------------------------------- LIST
    [Fact]
    public async Task ListExamRecords_normalizes_paging_and_forwards_filter()
    {
        var handler = new ListExamRecordsHandler(_recordRepo);
        var filter = new ExamRecordFilter(
            SessionID: Guid.NewGuid(),
            Keyword: "nguyen",
            State: (short)ExamRecordState.Waiting,
            VariantCode: "VAR1",
            RecordCode: "REC01",
            PatientCode: "PAT01",
            FullName: "Nguyen",
            IdentityNumber: "123",
            PhoneNumber: "090",
            From: new DateOnly(2026, 9, 1),
            To: new DateOnly(2026, 9, 10),
            Page: 0,
            Size: 999);

        var result = await handler.HandleAsync(new ListExamRecordsQuery("D01", filter));

        Assert.True(result.IsSuccess);
        Assert.Equal("D01", _recordRepo.LastListDivisionId);
        Assert.NotNull(_recordRepo.LastListFilter);
        Assert.Equal(1, _recordRepo.LastListFilter.Page);
        Assert.Equal(200, _recordRepo.LastListFilter.Size);
        Assert.Equal("nguyen", _recordRepo.LastListFilter.Keyword);
    }

    // ---------------------------------------------------------------- GET
    [Fact]
    public async Task GetExamRecord_returns_mapped_result_when_found()
    {
        var recordId = Guid.NewGuid();
        var patient = new Patient
        {
            PatientRefID = Guid.NewGuid(),
            DivisionID = "D01",
            PatientCode = "PAT-01",
            FullName = "Nguyễn Văn A"
        };
        var record = new ExamRecord
        {
            RecordID = recordId,
            DivisionID = "D01",
            RecordCode = "REC-01",
            PatientRefID = patient.PatientRefID,
            Patient = patient,
            State = ExamRecordState.Waiting
        };
        _recordRepo.Records[recordId] = record;

        var handler = new GetExamRecordHandler(_recordRepo);
        var result = await handler.HandleAsync(new GetExamRecordQuery("D01", recordId));

        Assert.True(result.IsSuccess);
        Assert.Equal("REC-01", result.Value.RecordCode);
        Assert.Equal("Chờ khám", result.Value.StateName);
    }

    [Fact]
    public async Task GetExamRecord_returns_not_found_when_missing()
    {
        var handler = new GetExamRecordHandler(_recordRepo);
        var result = await handler.HandleAsync(new GetExamRecordQuery("D01", Guid.NewGuid()));

        Assert.False(result.IsSuccess);
        Assert.Equal(ApplicationFailureCode.NotFound, result.Failure.Code);
    }

    // ---------------------------------------------------------------- CREATE
    [Fact]
    public async Task CreateExamRecord_validates_required_fields()
    {
        var handler = new CreateExamRecordHandler(_recordRepo, _sessionRepo, _allocator, _uow, _audit, _writer);
        var command = new CreateExamRecordCommand(
            DivisionId: "D01",
            ActorId: "emp-1",
            ActorKind: ActorKind.Employee,
            FullName: "", // Empty name!
            VariantCode: "DTK_01");

        var result = await handler.HandleAsync(command);

        Assert.False(result.IsSuccess);
        Assert.Equal(ApplicationFailureCode.BadRequest, result.Failure.Code);
        Assert.Empty(_recordRepo.Records);
    }

    [Fact]
    public async Task CreateExamRecord_rejects_closed_session()
    {
        var sessionId = Guid.NewGuid();
        _sessionRepo.Sessions[sessionId] = new ExamSession
        {
            SessionID = sessionId,
            DivisionID = "D01",
            State = ExamSessionState.Closed
        };

        var handler = new CreateExamRecordHandler(_recordRepo, _sessionRepo, _allocator, _uow, _audit, _writer);
        var command = new CreateExamRecordCommand(
            DivisionId: "D01",
            ActorId: "emp-1",
            ActorKind: ActorKind.Employee,
            SessionID: sessionId,
            PatientCode: "P01",
            FullName: "Nguyễn Văn A",
            VariantCode: "DTK_01");

        var result = await handler.HandleAsync(command);

        Assert.False(result.IsSuccess);
        Assert.Equal(ApplicationFailureCode.SessionClosed, result.Failure.Code);
        Assert.Contains("đã đóng", result.Failure.Message);
    }

    [Fact]
    public async Task CreateExamRecord_rejects_duplicate_patient_in_session()
    {
        var sessionId = Guid.NewGuid();
        _sessionRepo.Sessions[sessionId] = new ExamSession
        {
            SessionID = sessionId,
            DivisionID = "D01",
            State = ExamSessionState.Open
        };
        _recordRepo.DuplicateKeys.Add(("D01", sessionId, "P01"));

        var handler = new CreateExamRecordHandler(_recordRepo, _sessionRepo, _allocator, _uow, _audit, _writer);
        var command = new CreateExamRecordCommand(
            DivisionId: "D01",
            ActorId: "emp-1",
            ActorKind: ActorKind.Employee,
            SessionID: sessionId,
            PatientCode: "P01",
            FullName: "Nguyễn Văn A",
            VariantCode: "DTK_01");

        var result = await handler.HandleAsync(command);

        Assert.False(result.IsSuccess);
        Assert.Equal(ApplicationFailureCode.DuplicateInSession, result.Failure.Code);
        Assert.Contains("đã có hồ sơ trong đợt", result.Failure.Message);
    }

    [Fact]
    public async Task CreateExamRecord_allocates_code_and_creates_default_session_if_needed()
    {
        _allocator.AllocatedCode = "DK-2026-001-0005";

        var handler = new CreateExamRecordHandler(_recordRepo, _sessionRepo, _allocator, _uow, _audit, _writer);
        var command = new CreateExamRecordCommand(
            DivisionId: "D01",
            ActorId: "emp-1",
            ActorKind: ActorKind.Employee,
            SessionID: null,
            PatientCode: "P01",
            FullName: "Nguyễn Văn A",
            VariantCode: "DTK_01",
            EthnicityCode: "KINH");

        var result = await handler.HandleAsync(command);

        Assert.True(result.IsSuccess);
        Assert.Equal("DK-2026-001-0005", result.Value.RecordCode);
        Assert.Equal("KINH", result.Value.EthnicityCode);
        Assert.Equal(ExamRecordState.Waiting, result.Value.State);
        Assert.NotNull(result.Value.RegisteredAt);
        Assert.Single(_audit.Entries);
        Assert.True(_uow.SaveCount >= 1);
    }

    [Fact]
    public async Task CreateExamRecord_resolves_patient_subject_without_using_patient_type()
    {
        _allocator.AllocatedCode = "DK-2026-001-0006";
        var subject = new MasterDataOption
        {
            OptionID = Guid.NewGuid(),
            DivisionID = "D01",
            Category = MasterDataCategories.PatientSubject,
            Code = "01",
            Name = "Người lớn",
            IsActive = true
        };
        _recordRepo.MasterDataOptions[("D01", MasterDataCategories.PatientSubject, "01")] = subject;

        var handler = new CreateExamRecordHandler(_recordRepo, _sessionRepo, _allocator, _uow, _audit, _writer);
        var result = await handler.HandleAsync(new CreateExamRecordCommand(
            DivisionId: "D01",
            ActorId: "emp-1",
            ActorKind: ActorKind.Employee,
            FullName: "Nguyễn Văn Subject",
            VariantCode: "DTK_01",
            PatientSubjectCode: "01"));

        Assert.True(result.IsSuccess);
        var saved = Assert.Single(_recordRepo.Records.Values);
        Assert.Equal(subject.OptionID, saved.PatientSubjectOptionID);
        Assert.Same(subject, saved.PatientSubjectOption);
        Assert.Null(saved.PatientTypeOptionID);
    }

    [Fact]
    public async Task CreateExamRecord_generates_patient_code_for_new_phase_one_record()
    {
        _allocator.AllocatedCode = "PHASE1-DEFAULT-0007";
        var handler = new CreateExamRecordHandler(_recordRepo, _sessionRepo, _allocator, _uow, _audit, _writer);

        var result = await handler.HandleAsync(new CreateExamRecordCommand(
            DivisionId: "D01",
            ActorId: "emp-1",
            ActorKind: ActorKind.Employee,
            FullName: "Nguyễn Văn New",
            VariantCode: "DTK_01"));

        Assert.True(result.IsSuccess);
        var saved = Assert.Single(_recordRepo.Records.Values);
        Assert.Equal("HEX-PHASE1-DEFAULT-0007", saved.Patient?.PatientCode);
    }

    [Fact]
    public async Task CreateExamRecord_ensures_his_admission_when_admission_id_not_provided()
    {
        _allocator.AllocatedCode = "PHASE1-DEFAULT-0008";
        var fakeEnsure = new FakeEnsureHisAdmission { NextAdmissionId = 88001 };
        var handler = new CreateExamRecordHandler(_recordRepo, _sessionRepo, _allocator, _uow, _audit, _writer, fakeEnsure);

        var result = await handler.HandleAsync(new CreateExamRecordCommand(
            DivisionId: "D01",
            ActorId: "123",
            ActorKind: ActorKind.Employee,
            FullName: "Nguyễn Văn HIS",
            VariantCode: "DTK_01",
            Credential: "test-token",
            TraceId: "trace-99"));

        Assert.True(result.IsSuccess);
        Assert.Equal(88001, result.Value.AdmissionID);
        Assert.NotNull(fakeEnsure.LastCommand);
        Assert.Equal("D01", fakeEnsure.LastCommand.DivisionId);
        Assert.Equal(result.Value.RecordID, fakeEnsure.LastCommand.RecordId);
        Assert.Equal("test-token", fakeEnsure.LastCommand.Context.Credential);
        Assert.Equal("trace-99", fakeEnsure.LastCommand.Context.TraceId);
    }

    [Fact]
    public async Task CreateExamRecord_fails_fast_when_his_admission_creation_fails()
    {
        _allocator.AllocatedCode = "PHASE1-DEFAULT-0009";
        var fakeEnsure = new FakeEnsureHisAdmission
        {
            ShouldFail = true,
            FailureCode = ApplicationFailureCode.HisBadGateway,
            FailureMessage = "HIS cổng tiếp nhận lỗi"
        };
        var handler = new CreateExamRecordHandler(_recordRepo, _sessionRepo, _allocator, _uow, _audit, _writer, fakeEnsure);

        var result = await handler.HandleAsync(new CreateExamRecordCommand(
            DivisionId: "D01",
            ActorId: "123",
            ActorKind: ActorKind.Employee,
            FullName: "Nguyễn Văn Lỗi",
            VariantCode: "DTK_01"));

        Assert.False(result.IsSuccess);
        Assert.Equal(ApplicationFailureCode.HisBadGateway, result.Failure.Code);
        Assert.Equal("HIS cổng tiếp nhận lỗi", result.Failure.Message);
    }

    [Fact]
    public async Task CreateExamRecord_skips_ensure_his_admission_when_admission_id_already_provided()
    {
        _allocator.AllocatedCode = "PHASE1-DEFAULT-0010";
        var fakeEnsure = new FakeEnsureHisAdmission();
        var handler = new CreateExamRecordHandler(_recordRepo, _sessionRepo, _allocator, _uow, _audit, _writer, fakeEnsure);

        var result = await handler.HandleAsync(new CreateExamRecordCommand(
            DivisionId: "D01",
            ActorId: "123",
            ActorKind: ActorKind.Employee,
            FullName: "Nguyễn Văn Có Sẵn",
            VariantCode: "DTK_01",
            AdmissionID: 7777));

        Assert.True(result.IsSuccess);
        Assert.Equal(7777, result.Value.AdmissionID);
        Assert.Null(fakeEnsure.LastCommand);
    }

    [Fact]
    public async Task CreateExamRecord_creates_his_medical_process_when_mapped_form_has_medical_type_code()
    {
        _allocator.AllocatedCode = "PHASE1-PROC-0001";
        var fakeEnsure = new FakeEnsureHisAdmission { NextAdmissionId = 88002 };
        var fakeForms = new FakeRegistrationFormRepositoryForRecordTests();
        fakeForms.Mappings.Add(new ExamGroupFormMapping
        {
            DivisionID = "D01",
            VariantCode = "DTK_03",
            TemplateCode = "KSK-TREN18TUOI",
            MedicalTypeCode = "KSK03",
            IsActive = true
        });
        var fakeHis = new FakeHisEmrClientForRecordTests();

        var handler = new CreateExamRecordHandler(
            _recordRepo, _sessionRepo, _allocator, _uow, _audit, _writer, fakeEnsure, fakeForms, fakeHis);

        var result = await handler.HandleAsync(new CreateExamRecordCommand(
            DivisionId: "D01",
            ActorId: "123",
            ActorKind: ActorKind.Employee,
            FullName: "Nguyễn Văn Process",
            VariantCode: "DTK_03",
            Credential: "test-token",
            TraceId: "trace-process"));

        Assert.True(result.IsSuccess);
        Assert.Equal(88002, result.Value.AdmissionID);
        var req = Assert.Single(fakeHis.CreatedRequests);
        Assert.Equal("KSK03", req.MedicalTypeCode);
        Assert.Equal(88002, req.AdmissionID);
        Assert.Null(req.MedicalTypeCodeOld);
    }

    [Fact]
    public async Task CreateExamRecord_creates_medical_process_when_admission_id_already_provided()
    {
        _allocator.AllocatedCode = "PHASE1-PROC-0002";
        var fakeForms = new FakeRegistrationFormRepositoryForRecordTests();
        fakeForms.Mappings.Add(new ExamGroupFormMapping
        {
            DivisionID = "D01",
            VariantCode = "DTK_03",
            TemplateCode = "KSK-TREN18TUOI",
            MedicalTypeCode = "KSK03",
            IsActive = true
        });
        var fakeHis = new FakeHisEmrClientForRecordTests();

        var handler = new CreateExamRecordHandler(
            _recordRepo, _sessionRepo, _allocator, _uow, _audit, _writer, null, fakeForms, fakeHis);

        var result = await handler.HandleAsync(new CreateExamRecordCommand(
            DivisionId: "D01",
            ActorId: "123",
            ActorKind: ActorKind.Employee,
            FullName: "Nguyễn Văn PreAdmission",
            VariantCode: "DTK_03",
            AdmissionID: 654321,
            Credential: "test-token",
            TraceId: "trace-preadm"));

        Assert.True(result.IsSuccess);
        Assert.Equal(654321, result.Value.AdmissionID);
        var req = Assert.Single(fakeHis.CreatedRequests);
        Assert.Equal("KSK03", req.MedicalTypeCode);
        Assert.Equal(654321, req.AdmissionID);
    }

    [Fact]
    public async Task CreateExamRecord_reuses_existing_medical_process_if_already_present()
    {
        _allocator.AllocatedCode = "PHASE1-PROC-0003";
        var fakeEnsure = new FakeEnsureHisAdmission { NextAdmissionId = 88003 };
        var fakeForms = new FakeRegistrationFormRepositoryForRecordTests();
        fakeForms.Mappings.Add(new ExamGroupFormMapping
        {
            DivisionID = "D01",
            VariantCode = "DTK_03",
            TemplateCode = "KSK-TREN18TUOI",
            MedicalTypeCode = "KSK03",
            IsActive = true
        });
        var fakeHis = new FakeHisEmrClientForRecordTests
        {
            ExistingProcessesJson = "[{\"Id\":\"11111111-1111-1111-1111-111111111111\",\"MedicalTypeCode\":\"KSK03\"}]"
        };

        var handler = new CreateExamRecordHandler(
            _recordRepo, _sessionRepo, _allocator, _uow, _audit, _writer, fakeEnsure, fakeForms, fakeHis);

        var result = await handler.HandleAsync(new CreateExamRecordCommand(
            DivisionId: "D01",
            ActorId: "123",
            ActorKind: ActorKind.Employee,
            FullName: "Nguyễn Văn Existing",
            VariantCode: "DTK_03",
            Credential: "test-token",
            TraceId: "trace-existing"));

        Assert.True(result.IsSuccess);
        Assert.Empty(fakeHis.CreatedRequests);
    }

    [Fact]
    public async Task CreateExamRecord_skips_medical_process_when_mapping_has_no_medical_type_code()
    {
        _allocator.AllocatedCode = "PHASE1-PROC-0004";
        var fakeEnsure = new FakeEnsureHisAdmission { NextAdmissionId = 88004 };
        var fakeForms = new FakeRegistrationFormRepositoryForRecordTests();
        fakeForms.Mappings.Add(new ExamGroupFormMapping
        {
            DivisionID = "D01",
            VariantCode = "DTK_01",
            TemplateCode = "FORM-DTK01",
            MedicalTypeCode = "",
            IsActive = true
        });
        var fakeHis = new FakeHisEmrClientForRecordTests();

        var handler = new CreateExamRecordHandler(
            _recordRepo, _sessionRepo, _allocator, _uow, _audit, _writer, fakeEnsure, fakeForms, fakeHis);

        var result = await handler.HandleAsync(new CreateExamRecordCommand(
            DivisionId: "D01",
            ActorId: "123",
            ActorKind: ActorKind.Employee,
            FullName: "Nguyễn Văn NoCode",
            VariantCode: "DTK_01",
            Credential: "test-token",
            TraceId: "trace-nocode"));

        Assert.True(result.IsSuccess);
        Assert.Empty(fakeHis.CreatedRequests);
    }

    [Fact]
    public async Task CreateExamRecord_returns_dependency_error_when_his_medical_process_creation_fails()
    {
        _allocator.AllocatedCode = "PHASE1-PROC-0005";
        var fakeEnsure = new FakeEnsureHisAdmission { NextAdmissionId = 88005 };
        var fakeForms = new FakeRegistrationFormRepositoryForRecordTests();
        fakeForms.Mappings.Add(new ExamGroupFormMapping
        {
            DivisionID = "D01",
            VariantCode = "DTK_03",
            TemplateCode = "KSK-TREN18TUOI",
            MedicalTypeCode = "KSK03",
            IsActive = true
        });
        var fakeHis = new FakeHisEmrClientForRecordTests
        {
            FailCreateProcess = true,
            FailureOutcome = HisClientOutcome.BadGateway,
            FailureMessage = "Lỗi tạo quy trình bệnh án trên HIS"
        };

        var handler = new CreateExamRecordHandler(
            _recordRepo, _sessionRepo, _allocator, _uow, _audit, _writer, fakeEnsure, fakeForms, fakeHis);

        var result = await handler.HandleAsync(new CreateExamRecordCommand(
            DivisionId: "D01",
            ActorId: "123",
            ActorKind: ActorKind.Employee,
            FullName: "Nguyễn Văn RemoteFail",
            VariantCode: "DTK_03",
            Credential: "test-token",
            TraceId: "trace-fail"));

        Assert.False(result.IsSuccess);
        Assert.Equal(ApplicationFailureCode.HisBadGateway, result.Failure.Code);
        Assert.Equal("Lỗi tạo quy trình bệnh án trên HIS", result.Failure.Message);
        Assert.NotEmpty(_recordRepo.Records);
    }

    [Fact]
    public async Task CreateExamRecord_discards_changes_on_save_failure()
    {
        _allocator.AllocatedCode = "DK-2026-001-0005";
        _uow.FailOnSave = true;

        var handler = new CreateExamRecordHandler(_recordRepo, _sessionRepo, _allocator, _uow, _audit, _writer);
        var command = new CreateExamRecordCommand(
            DivisionId: "D01",
            ActorId: "emp-1",
            ActorKind: ActorKind.Employee,
            SessionID: null,
            PatientCode: "P01",
            FullName: "Nguyễn Văn A",
            VariantCode: "DTK_01");

        await Assert.ThrowsAsync<InvalidOperationException>(() => handler.HandleAsync(command));
        Assert.True(_uow.Discarded);
    }

    [Fact]
    public async Task Create_writes_patient_and_child_fact_references()
    {
        _allocator.AllocatedCode = "DK-2026-0001";
        var handler = new CreateExamRecordHandler(_recordRepo, _sessionRepo, _allocator, _uow, _audit, _writer);

        var command = new CreateExamRecordCommand(
            DivisionId: "D01",
            ActorId: "emp-1",
            ActorKind: ActorKind.Employee,
            FullName: "Nguyễn Văn Test",
            PatientCode: "P-TEST",
            Dob: new DateOnly(1995, 5, 20),
            BirthYear: 1995,
            GenderID: 1,
            IdentityNumber: "001234567890",
            InsuranceNumber: "DN4010123456789",
            StaffCode: "EMP01",
            OrgDeptName: "Khoa CNTT",
            JobTitle: "Kỹ sư",
            RelativeFullName: "Nguyễn Mẹ",
            RelativeRelationshipCode: "ME",
            RelativePhoneNumber: "0901234567",
            VariantCode: "DTK_01");

        var result = await handler.HandleAsync(command);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value.PatientRefID);
        Assert.NotEqual(Guid.Empty, result.Value.PatientRefID.Value);
        Assert.NotNull(result.Value.InsuranceRefID);
        Assert.NotNull(result.Value.EmploymentRefID);
        Assert.NotNull(result.Value.RelativeRefID);

        var savedPatient = _patientRepo.Patients.FirstOrDefault(p => p.PatientRefID == result.Value.PatientRefID.Value);
        Assert.NotNull(savedPatient);
        Assert.Equal("Nguyễn Văn Test", savedPatient.FullName);
        Assert.Equal("P-TEST", savedPatient.PatientCode);

        var savedInsurance = _patientRepo.Insurances.FirstOrDefault(i => i.InsuranceRefID == result.Value.InsuranceRefID.Value);
        Assert.NotNull(savedInsurance);
        Assert.Equal("DN4010123456789", savedInsurance.InsuranceNumber);

        var savedEmployment = _patientRepo.Employments.FirstOrDefault(e => e.EmploymentRefID == result.Value.EmploymentRefID.Value);
        Assert.NotNull(savedEmployment);
        Assert.Equal("EMP01", savedEmployment.StaffCode);

        var savedRelative = _patientRepo.Relatives.FirstOrDefault(r => r.RelativeRefID == result.Value.RelativeRefID.Value);
        Assert.NotNull(savedRelative);
        Assert.Equal("Nguyễn Mẹ", savedRelative.FullName);
    }

    // ---------------------------------------------------------------- UPDATE
    [Fact]
    public async Task UpdateExamRecord_returns_not_found_when_missing()
    {
        var handler = new UpdateExamRecordHandler(_recordRepo, _sessionRepo, _uow, _audit, _writer);
        var command = new UpdateExamRecordCommand(
            DivisionId: "D01",
            RecordId: Guid.NewGuid(),
            ActorId: "emp-1",
            ActorKind: ActorKind.Employee,
            RecordCode: "REC01",
            PatientCode: "P01",
            FullName: "Nguyễn Văn B",
            VariantCode: "DTK_01");

        var result = await handler.HandleAsync(command);

        Assert.False(result.IsSuccess);
        Assert.Equal(ApplicationFailureCode.NotFound, result.Failure.Code);
    }

    [Fact]
    public async Task Create_changed_patient_without_decision_returns_profile_changed()
    {
        var active = new Patient
        {
            PatientRefID = Guid.NewGuid(),
            DivisionID = "D01",
            PatientCode = "P01",
            FullName = "Nguyễn Văn A",
            PhoneNumber = "0901000000",
            IsActive = true
        };
        _patientRepo.Add(active);

        var handler = new CreateExamRecordHandler(_recordRepo, _sessionRepo, _allocator, _uow, _audit, _writer);
        var command = new CreateExamRecordCommand(
            DivisionId: "D01",
            ActorId: "emp-1",
            ActorKind: ActorKind.Employee,
            PatientRefID: active.PatientRefID,
            PatientCode: "P01",
            FullName: "Nguyễn Văn A",
            PhoneNumber: "0902000000",
            VariantCode: "DTK_01",
            SetAsActiveProfile: null);

        var result = await handler.HandleAsync(command);

        Assert.False(result.IsSuccess);
        Assert.Equal(ApplicationFailureCode.PatientProfileChanged, result.Failure.Code);
        Assert.Empty(_recordRepo.Records);
    }

    [Fact]
    public async Task Create_allocates_patient_version_inside_record_transaction()
    {
        var active = new Patient
        {
            PatientRefID = Guid.NewGuid(),
            DivisionID = "D01",
            FullName = "Nguyễn Văn An",
            IdentityNumber = "079123456789",
            PhoneNumber = "0901000000",
            IsActive = true,
            VersionNumber = 1
        };
        active.ProfileLineageID = active.PatientRefID;
        _patientRepo.Add(active);
        _patientRepo.BeforeAllocateVersion = () => Assert.True(_uow.TransactionActive);

        var result = await new CreateExamRecordHandler(
            _recordRepo, _sessionRepo, _allocator, _uow, _audit, _writer)
            .HandleAsync(new CreateExamRecordCommand(
                DivisionId: "D01",
                ActorId: "emp-1",
                ActorKind: ActorKind.Employee,
                RecordCode: "REC-ATOMIC-01",
                FullName: active.FullName,
                IdentityNumber: active.IdentityNumber,
                PhoneNumber: "0902000000",
                VariantCode: "DTK_01",
                PatientRefID: active.PatientRefID,
                SetAsActiveProfile: false));

        Assert.True(result.IsSuccess);
        Assert.NotEqual(active.PatientRefID, result.Value.PatientRefID);
        Assert.Equal(2, _patientRepo.Patients.Single(x =>
            x.PatientRefID == result.Value.PatientRefID).VersionNumber);
    }

    [Fact]
    public async Task Update_changed_patient_forks_without_changing_another_record_history()
    {
        var active = new Patient
        {
            PatientRefID = Guid.NewGuid(),
            DivisionID = "D01",
            PatientCode = "P01",
            FullName = "Nguyễn Văn Shared",
            PhoneNumber = "0901000000",
            IsActive = true
        };
        _patientRepo.Add(active);

        var sessionId = Guid.NewGuid();
        _sessionRepo.Sessions[sessionId] = new ExamSession
        {
            SessionID = sessionId,
            DivisionID = "D01",
            State = ExamSessionState.Open
        };

        var firstRecordId = Guid.NewGuid();
        var secondRecordId = Guid.NewGuid();

        var firstRecord = new ExamRecord
        {
            RecordID = firstRecordId,
            DivisionID = "D01",
            SessionID = sessionId,
            RecordCode = "REC-01",
            PatientRefID = active.PatientRefID,
            Patient = active,
            VariantCode = "DTK_01",
            State = ExamRecordState.Waiting
        };
        var secondRecord = new ExamRecord
        {
            RecordID = secondRecordId,
            DivisionID = "D01",
            SessionID = sessionId,
            RecordCode = "REC-02",
            PatientRefID = active.PatientRefID,
            Patient = active,
            VariantCode = "DTK_01",
            State = ExamRecordState.Waiting
        };

        _recordRepo.Records[firstRecordId] = firstRecord;
        _recordRepo.Records[secondRecordId] = secondRecord;

        var handler = new UpdateExamRecordHandler(_recordRepo, _sessionRepo, _uow, _audit, _writer);
        var updateCmd = new UpdateExamRecordCommand(
            DivisionId: "D01",
            RecordId: firstRecordId,
            ActorId: "emp-1",
            ActorKind: ActorKind.Employee,
            PatientRefID: active.PatientRefID,
            PatientCode: "P01",
            FullName: "Nguyễn Văn Shared",
            PhoneNumber: "0902000000",
            VariantCode: "DTK_01",
            SetAsActiveProfile: false);

        var result = await handler.HandleAsync(updateCmd);

        Assert.True(result.IsSuccess);
        Assert.NotEqual(active.PatientRefID, result.Value.PatientRefID);
        Assert.Equal(active.PatientRefID, _recordRepo.Records[secondRecordId].PatientRefID);
        Assert.Equal("0901000000", _recordRepo.Records[secondRecordId].Patient.PhoneNumber);
    }

    [Fact]
    public async Task UpdateExamRecord_updates_fields_and_audits()
    {
        var recordId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var patient = new Patient
        {
            PatientRefID = Guid.NewGuid(),
            DivisionID = "D01",
            PatientCode = "P01",
            FullName = "Nguyễn Văn A",
            GenderID = 1,
            IsActive = true
        };
        _patientRepo.Add(patient);

        var record = new ExamRecord
        {
            RecordID = recordId,
            DivisionID = "D01",
            SessionID = sessionId,
            RecordCode = "REC01",
            PatientRefID = patient.PatientRefID,
            Patient = patient,
            VariantCode = "DTK_01",
            State = ExamRecordState.NotRegistered
        };
        _recordRepo.Records[recordId] = record;
        _sessionRepo.Sessions[sessionId] = new ExamSession
        {
            SessionID = sessionId,
            DivisionID = "D01",
            State = ExamSessionState.Open
        };

        var handler = new UpdateExamRecordHandler(_recordRepo, _sessionRepo, _uow, _audit, _writer);
        var command = new UpdateExamRecordCommand(
            DivisionId: "D01",
            RecordId: recordId,
            ActorId: "emp-1",
            ActorKind: ActorKind.Employee,
            RecordCode: "REC01",
            PatientCode: "P01",
            FullName: "Nguyễn Văn B",
            Dob: new DateOnly(1990, 1, 1),
            BirthYear: 1990,
            GenderID: 2,
            JobTitle: "Developer",
            VariantCode: "DTK_01",
            EthnicityCode: "TAY",
            SetAsActiveProfile: true);

        var result = await handler.HandleAsync(command);

        Assert.True(result.IsSuccess);
        Assert.Equal("Nguyễn Văn B", record.Patient?.FullName);
        Assert.Equal((short)2, record.Patient?.GenderID);
        Assert.Equal("Developer", record.Employment?.JobTitle);
        Assert.Equal("TAY", result.Value.EthnicityCode);
        Assert.Equal("Tên TAY", result.Value.EthnicityName);
        Assert.Single(_audit.Entries);
    }

    [Fact]
    public async Task Update_reuses_patient_by_positive_his_patient_id()
    {
        var existingPatientRefId = Guid.NewGuid();
        var existingPatient = new Patient
        {
            PatientRefID = existingPatientRefId,
            DivisionID = "D01",
            HisPatientID = 12345L,
            PatientCode = "P-123",
            FullName = "Lê Văn Cũ",
            IsActive = true
        };
        _patientRepo.Add(existingPatient);

        var recordId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var record = new ExamRecord
        {
            RecordID = recordId,
            DivisionID = "D01",
            SessionID = sessionId,
            RecordCode = "REC-123",
            VariantCode = "DTK_01",
            State = ExamRecordState.NotRegistered
        };
        _recordRepo.Records[recordId] = record;
        _sessionRepo.Sessions[sessionId] = new ExamSession
        {
            SessionID = sessionId,
            DivisionID = "D01",
            State = ExamSessionState.Open
        };

        var handler = new UpdateExamRecordHandler(_recordRepo, _sessionRepo, _uow, _audit, _writer);
        var updateCmd = new UpdateExamRecordCommand(
            DivisionId: "D01",
            RecordId: recordId,
            ActorId: "emp-1",
            ActorKind: ActorKind.Employee,
            PatientID: 12345L,
            PatientCode: "P-123",
            FullName: "Lê Văn Cũ",
            VariantCode: "DTK_01");

        var result = await handler.HandleAsync(updateCmd);

        Assert.True(result.IsSuccess);
        Assert.Equal(existingPatientRefId, result.Value.PatientRefID);
        Assert.Single(_patientRepo.Patients);
        Assert.Equal("Lê Văn Cũ", _patientRepo.Patients[0].FullName);
    }

    // ---------------------------------------------------------------- CONFIRM
    [Fact]
    public async Task ConfirmExamRecord_transitions_not_registered_to_waiting()
    {
        var recordId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var record = new ExamRecord
        {
            RecordID = recordId,
            DivisionID = "D01",
            SessionID = sessionId,
            RecordCode = "REC01",
            State = ExamRecordState.NotRegistered,
            ExamReason = "Khám sức khỏe định kỳ"
        };
        _recordRepo.Records[recordId] = record;
        _sessionRepo.Sessions[sessionId] = new ExamSession
        {
            SessionID = sessionId,
            DivisionID = "D01",
            State = ExamSessionState.Open
        };

        var handler = new ConfirmExamRecordHandler(_recordRepo, _sessionRepo, _uow, _audit);
        var command = new ConfirmExamRecordCommand("D01", recordId, "emp-1", ActorKind.Employee);

        var result = await handler.HandleAsync(command);

        Assert.True(result.IsSuccess);
        Assert.Equal(ExamRecordState.Waiting, record.State);
        Assert.Single(_audit.Entries);
        Assert.Equal(1, _uow.SaveCount);
    }

    [Fact]
    public async Task ConfirmExamRecord_rejects_non_not_registered_state()
    {
        var recordId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var record = new ExamRecord
        {
            RecordID = recordId,
            DivisionID = "D01",
            SessionID = sessionId,
            RecordCode = "REC01",
            State = ExamRecordState.Waiting,
            ExamReason = "Khám sức khỏe định kỳ"
        };
        _recordRepo.Records[recordId] = record;
        _sessionRepo.Sessions[sessionId] = new ExamSession
        {
            SessionID = sessionId,
            DivisionID = "D01",
            State = ExamSessionState.Open
        };

        var handler = new ConfirmExamRecordHandler(_recordRepo, _sessionRepo, _uow, _audit);
        var command = new ConfirmExamRecordCommand("D01", recordId, "emp-1", ActorKind.Employee);

        var result = await handler.HandleAsync(command);

        Assert.False(result.IsSuccess);
        Assert.Equal(ApplicationFailureCode.InvalidState, result.Failure.Code);
        Assert.Contains("chỉ hồ sơ", result.Failure.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ---------------------------------------------------------------- CANCEL
    [Fact]
    public async Task CancelExamRecord_requires_reason()
    {
        var recordId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var record = new ExamRecord
        {
            RecordID = recordId,
            DivisionID = "D01",
            SessionID = sessionId,
            RecordCode = "REC01",
            State = ExamRecordState.Waiting
        };
        _recordRepo.Records[recordId] = record;
        _sessionRepo.Sessions[sessionId] = new ExamSession
        {
            SessionID = sessionId,
            DivisionID = "D01",
            State = ExamSessionState.Open
        };

        var handler = new CancelExamRecordHandler(_recordRepo, _sessionRepo, _uow, _audit);
        var command = new CancelExamRecordCommand("D01", recordId, "emp-1", ActorKind.Employee, Reason: "  ");

        var result = await handler.HandleAsync(command);

        Assert.False(result.IsSuccess);
        Assert.Equal(ApplicationFailureCode.BadRequest, result.Failure.Code);
        Assert.Contains("Lý do hủy", result.Failure.Message);
    }

    [Theory]
    [InlineData(ExamRecordState.NotRegistered, ExamRecordState.RegistrationCancelled)]
    [InlineData(ExamRecordState.Waiting, ExamRecordState.ExamCancelled)]
    [InlineData(ExamRecordState.InProgress, ExamRecordState.ExamCancelled)]
    public async Task CancelExamRecord_applies_target_state_and_audits(
        ExamRecordState source, ExamRecordState target)
    {
        var recordId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var record = new ExamRecord
        {
            RecordID = recordId,
            DivisionID = "D01",
            SessionID = sessionId,
            RecordCode = "REC01",
            State = source
        };
        _recordRepo.Records[recordId] = record;
        _sessionRepo.Sessions[sessionId] = new ExamSession
        {
            SessionID = sessionId,
            DivisionID = "D01",
            State = ExamSessionState.Open
        };

        var handler = new CancelExamRecordHandler(_recordRepo, _sessionRepo, _uow, _audit);
        var command = new CancelExamRecordCommand("D01", recordId, "emp-1", ActorKind.Employee, Reason: "Không đến khám");

        var result = await handler.HandleAsync(command);

        Assert.True(result.IsSuccess);
        Assert.Equal(target, record.State);
        Assert.Equal("Không đến khám", record.CancelReason);
        Assert.Single(_audit.Entries);
    }

    // ---------------------------------------------------------------- VERIFY PORTAL CREDENTIALS
    [Fact]
    public async Task VerifyPortalCredentials_validates_required_fields()
    {
        var handler = new VerifyPortalCredentialsHandler(_recordRepo);

        var resultNoPatient = await handler.HandleAsync(new VerifyPortalCredentialsCommand("D01", "", "123", ""));
        Assert.False(resultNoPatient.IsSuccess);
        Assert.Equal(ApplicationFailureCode.BadRequest, resultNoPatient.Failure.Code);

        var resultNoId = await handler.HandleAsync(new VerifyPortalCredentialsCommand("D01", "P01", "", ""));
        Assert.False(resultNoId.IsSuccess);
        Assert.Equal(ApplicationFailureCode.BadRequest, resultNoId.Failure.Code);
    }

    [Fact]
    public async Task VerifyPortalCredentials_returns_invalid_when_no_match()
    {
        var handler = new VerifyPortalCredentialsHandler(_recordRepo);
        var result = await handler.HandleAsync(new VerifyPortalCredentialsCommand("D01", "P01", "123", ""));

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.IsValid);
    }

    [Fact]
    public async Task VerifyPortalCredentials_returns_valid_when_credentials_match()
    {
        _recordRepo.PortalCandidates["P01"] = CreateDummyResult("REC-001", "DK-01", "Nguyễn Văn A", "123456", "BHYT123");

        var handler = new VerifyPortalCredentialsHandler(_recordRepo);
        var result = await handler.HandleAsync(new VerifyPortalCredentialsCommand("D01", "P01", "123456", ""));

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.IsValid);
        Assert.Equal("REC-001", result.Value.RecordCode);
        Assert.Equal("Nguyễn Văn A", result.Value.FullName);
    }

    // ---------------------------------------------------------------- PROGRESS
    [Fact]
    public async Task GetExamSessionProgress_returns_not_found_when_session_missing()
    {
        var handler = new GetExamSessionProgressHandler(_recordRepo, _sessionRepo);
        var result = await handler.HandleAsync(new GetExamSessionProgressQuery("D01", Guid.NewGuid()));

        Assert.False(result.IsSuccess);
        Assert.Equal(ApplicationFailureCode.NotFound, result.Failure.Code);
    }

    [Fact]
    public async Task GetExamSessionProgress_returns_progress_when_found()
    {
        var sessionId = Guid.NewGuid();
        _sessionRepo.Sessions[sessionId] = new ExamSession
        {
            SessionID = sessionId,
            DivisionID = "D01",
            SessionCode = "DK-01",
            State = ExamSessionState.Open
        };

        _recordRepo.SessionProgress[sessionId] = new ExamSessionProgressResult(
            sessionId,
            "DK-01",
            "Open",
            new ExamSessionProgressCounts(10, 2, 3, 3, 2, 0, 0, 0),
            1,
            DateTime.UtcNow);

        var handler = new GetExamSessionProgressHandler(_recordRepo, _sessionRepo);
        var result = await handler.HandleAsync(new GetExamSessionProgressQuery("D01", sessionId));

        Assert.True(result.IsSuccess);
        Assert.Equal(10, result.Value.Profiles.Total);
        Assert.Equal(1, result.Value.ConclusionReady);
    }

    private static ExamRecordResult CreateDummyResult(
        string recordCode, string sessionCode, string fullName, string identityNumber, string insuranceNumber)
    {
        return new ExamRecordResult(
            RecordID: Guid.NewGuid(),
            SessionID: Guid.NewGuid(),
            SessionCode: sessionCode,
            ExamDate: new DateOnly(2026, 9, 10),
            RecordCode: recordCode,
            PatientID: 1,
            AdmissionID: null,
            PatientCode: "P01",
            FullName: fullName,
            Dob: null,
            BirthYear: null,
            GenderID: 1,
            IdentityNumber: identityNumber,
            InsuranceNumber: insuranceNumber,
            PhoneNumber: "",
            Email: "",
            Address: "",
            StaffCode: "",
            OrgDeptName: "",
            JobTitle: "",
            VariantCode: "DTK_01",
            VariantName: "DTK 01",
            PackageID: null,
            PackageName: "",
            FormID: null,
            FormCode: "",
            SubmissionID: null,
            State: ExamRecordState.Waiting,
            StateName: "Chờ khám",
            RegisteredAt: null,
            ExamStartedAt: null,
            ExamFinishedAt: null,
            CancelledAt: null,
            CancelReason: null,
            ProgressDone: 0,
            ProgressTotal: 0,
            HealthClassCode: "",
            Note: null,
            EthnicityCode: "",
            EthnicityName: "",
            OccupationCode: "",
            OccupationName: "",
            BloodAboCode: "",
            BloodAboName: "",
            BloodRhCode: "",
            BloodRhName: "",
            ProvinceCode: "",
            ProvinceName: "",
            WardCode: "",
            WardName: "",
            IdentityIssuedDate: null,
            IdentityIssuerCode: "",
            IdentityIssuerName: "",
            RelativeRelationshipCode: "",
            RelativeRelationshipName: "",
            RelativeFullName: "",
            RelativeIdentityNumber: "",
            RelativePhoneNumber: "",
            InsuranceObjectCode: "",
            InsuranceObjectName: "",
            InsuranceValidFrom: null,
            InsuranceValidTo: null,
            ExamReason: "",
            PatientTypeCode: "",
            PatientTypeName: "",
            PaymentSourceCode: "",
            PaymentSourceName: "",
            PaymentSourceOther: "",
            ExamLocationCode: "",
            ExamLocationName: "",
            CreatedDate: DateTime.UtcNow,
            ModifiedDate: DateTime.UtcNow);
    }
}

// ---------------------------------------------------------------- FAKES
public class FakeExamRecordRepository : IExamRecordRepository
{
    public Dictionary<Guid, ExamRecord> Records { get; } = new();
    public HashSet<(string DivisionId, Guid SessionId, string PatientCode)> DuplicateKeys { get; } = new();
    public Dictionary<string, ExamRecordResult> PortalCandidates { get; } = new();
    public Dictionary<Guid, ExamSessionProgressResult> SessionProgress { get; } = new();
    public string LastListDivisionId { get; private set; }
    public ExamRecordFilter LastListFilter { get; private set; }
    public string LastHistoryDivisionId { get; private set; }
    public Guid? LastHistoryLineageID { get; private set; }
    public int LastHistoryPage { get; private set; }
    public int LastHistorySize { get; private set; }
    public PageResult<ExamRecordResult> HistoryPage { get; set; }

    /// <summary>Thứ tự gọi Lock/Get — cho test chốt "khóa dòng TRƯỚC khi đọc để sửa".</summary>
    public List<string> Calls { get; } = new();

    public FakeExamRecordRepository() { }

    public FakeExamRecordRepository(ExamRecord initialRecord)
    {
        if (initialRecord != null)
        {
            Records[initialRecord.RecordID] = initialRecord;
        }
    }

    public Task<PageResult<ExamRecordResult>> ListAsync(
        string divisionId, ExamRecordFilter filter, CancellationToken ct = default)
    {
        LastListDivisionId = divisionId;
        LastListFilter = filter;
        return Task.FromResult(new PageResult<ExamRecordResult>(
            Array.Empty<ExamRecordResult>(), filter.Page, filter.Size, 0));
    }

    public Task<PageResult<ExamRecordResult>> ListSignedByPatientLineageAsync(
        string divisionId, Guid profileLineageID, int page, int size, CancellationToken ct = default)
    {
        LastHistoryDivisionId = divisionId;
        LastHistoryLineageID = profileLineageID;
        LastHistoryPage = page;
        LastHistorySize = size;
        return Task.FromResult(HistoryPage ?? new PageResult<ExamRecordResult>(
            Array.Empty<ExamRecordResult>(), page, size, 0));
    }

    public Task<ExamRecord> GetAsync(
        string divisionId, Guid recordId, bool forUpdate = false, CancellationToken ct = default)
    {
        Calls.Add(forUpdate ? "GetForUpdate" : "Get");
        Records.TryGetValue(recordId, out var record);
        if (record != null && record.DivisionID == divisionId) return Task.FromResult(record);
        return Task.FromResult<ExamRecord>(null);
    }

    public Dictionary<(string DivisionId, string Category, string Code), MasterDataOption> MasterDataOptions { get; } = new();

    public Task<ExamRecord> GetWithRegistrationAsync(
        string divisionId, Guid recordId, bool forUpdate = false, CancellationToken ct = default)
    {
        return GetAsync(divisionId, recordId, forUpdate, ct);
    }

    public Task<MasterDataOption> ResolveMasterDataOptionAsync(
        string divisionId, string category, string code, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(code)) return Task.FromResult<MasterDataOption>(null);
        var trimmed = code.Trim();
        if (MasterDataOptions.TryGetValue((divisionId, category, trimmed), out var opt))
            return Task.FromResult(opt);

        var fallback = new MasterDataOption
        {
            OptionID = Guid.NewGuid(),
            DivisionID = divisionId,
            Category = category,
            Code = trimmed,
            Name = $"Tên {trimmed}",
            IsActive = true
        };
        return Task.FromResult(fallback);
    }

    public Task<ExamRecordResult> GetResultAsync(
        string divisionId, Guid recordId, CancellationToken ct = default)
    {
        Records.TryGetValue(recordId, out var record);
        if (record != null && record.DivisionID == divisionId)
        {
            return Task.FromResult(new ExamRecordResult(
                record.RecordID,
                record.SessionID,
                "DK-01",
                new DateOnly(2026, 9, 10),
                record.RecordCode,
                record.Patient?.HisPatientID ?? 0,
                record.AdmissionID,
                record.Patient?.PatientCode ?? "",
                record.Patient?.FullName ?? "",
                record.Patient?.Dob,
                record.Patient?.BirthYear,
                record.Patient?.GenderID ?? 0,
                record.Patient?.IdentityNumber ?? "",
                record.Insurance?.InsuranceNumber ?? "",
                record.Patient?.PhoneNumber ?? "",
                record.Patient?.Email ?? "",
                record.Patient?.Address ?? "",
                record.Employment?.StaffCode ?? "",
                record.Employment?.OrgDeptName ?? "",
                record.Employment?.JobTitle ?? "",
                record.VariantCode,
                "",
                record.PackageID,
                "",
                record.FormID,
                record.FormCode,
                record.SubmissionID,
                record.State,
                record.State == ExamRecordState.Waiting ? "Chờ khám" : "Chưa đăng ký",
                record.RegisteredAt,
                record.ExamStartedAt,
                record.ExamFinishedAt,
                record.CancelledAt,
                record.CancelReason,
                record.ProgressDone,
                record.ProgressTotal,
                record.HealthClassCode,
                record.Note,
                record.Patient?.EthnicityOption?.Code ?? "",
                record.Patient?.EthnicityOption?.Name ?? "",
                record.Employment?.OccupationOption?.Code ?? "",
                record.Employment?.OccupationOption?.Name ?? "",
                record.Patient?.BloodAboCode ?? "",
                record.Patient?.BloodAboCode ?? "",
                record.Patient?.BloodRhCode ?? "",
                record.Patient?.BloodRhCode ?? "",
                record.ProvinceCode,
                record.ProvinceName,
                record.WardCode,
                record.WardName,
                record.Patient?.IdentityIssuedDate,
                record.Patient?.IdentityIssuerOption?.Code ?? "",
                record.Patient?.IdentityIssuerOption?.Name ?? "",
                record.Relative?.RelationshipCode ?? "",
                record.Relative?.RelationshipCode ?? "",
                record.Relative?.FullName ?? "",
                record.Relative?.IdentityNumber ?? "",
                record.Relative?.PhoneNumber ?? "",
                record.Insurance?.InsuranceObjectOption?.Code ?? "",
                record.Insurance?.InsuranceObjectOption?.Name ?? "",
                record.Insurance?.ValidFrom,
                record.Insurance?.ValidTo,
                record.ExamReason,
                record.PatientTypeOption?.Code ?? "",
                record.PatientTypeOption?.Name ?? "",
                record.PaymentSourceOption?.Code ?? "",
                record.PaymentSourceOption?.Name ?? "",
                record.PaymentSourceOther,
                record.ExamLocationOption?.Code ?? "",
                record.ExamLocationOption?.Name ?? "",
                DateTime.UtcNow,
                DateTime.UtcNow,
                PatientRefID: record.PatientRefID,
                InsuranceRefID: record.InsuranceRefID,
                EmploymentRefID: record.EmploymentRefID,
                RelativeRefID: record.RelativeRefID,
                PatientTypeOptionID: record.PatientTypeOptionID,
                PaymentSourceOptionID: record.PaymentSourceOptionID,
                ExamLocationOptionID: record.ExamLocationOptionID,
                RegistrationPlaceCode: record.Insurance?.RegistrationPlaceOption?.Code ?? "",
                RegistrationPlaceName: record.Insurance?.RegistrationPlaceOption?.Name ?? ""));
        }
        return Task.FromResult<ExamRecordResult>(null);
    }

    public Task<bool> ExistsInSessionAsync(
        string divisionId, Guid sessionId, string patientCode, Guid? excludingRecordId = null, CancellationToken ct = default)
    {
        var exists = DuplicateKeys.Contains((divisionId, sessionId, patientCode));
        return Task.FromResult(exists);
    }

    public Task<bool> ExistsDuplicateAsync(
        string divisionId, Guid sessionId, string identityNumber, string patientCode, Guid? excludingRecordId = null, CancellationToken ct = default)
    {
        var exists = DuplicateKeys.Contains((divisionId, sessionId, patientCode));
        return Task.FromResult(exists);
    }

    public Task<ExamSession> GetOrCreateDefaultSessionAsync(
        string divisionId, CancellationToken ct = default)
    {
        var session = new ExamSession
        {
            SessionID = Guid.NewGuid(),
            DivisionID = divisionId,
            SessionCode = "PHASE1-DEFAULT",
            State = ExamSessionState.Open
        };
        return Task.FromResult(session);
    }

    public void Add(ExamRecord record)
    {
        Records[record.RecordID] = record;
    }

    public Task<ExamSessionProgressResult> GetProgressAsync(
        string divisionId, Guid sessionId, CancellationToken ct = default)
    {
        SessionProgress.TryGetValue(sessionId, out var res);
        return Task.FromResult(res);
    }

    public Task<ExamRecordResult> VerifyPortalCredentialsAsync(
        string divisionId, string patientCode, string identifier, CancellationToken ct = default)
    {
        return VerifyPortalCredentialsAsync(divisionId, patientCode, identifier, identifier, ct);
    }

    public Task<ExamRecordResult> VerifyPortalCredentialsAsync(
        string divisionId, string patientCode, string identityNumber, string insuranceNumber, CancellationToken ct = default)
    {
        PortalCandidates.TryGetValue(patientCode, out var candidate);
        if (candidate == null) return Task.FromResult<ExamRecordResult>(null);

        var matchIdentity = string.IsNullOrEmpty(identityNumber) || candidate.IdentityNumber == identityNumber;
        var matchInsurance = string.IsNullOrEmpty(insuranceNumber) || candidate.InsuranceNumber == insuranceNumber;

        if (matchIdentity && matchInsurance) return Task.FromResult(candidate);
        return Task.FromResult<ExamRecordResult>(null);
    }

    public Task<ExamPackage> GetPackageAsync(
        string divisionId, Guid packageId, CancellationToken ct = default)
    {
        return Task.FromResult<ExamPackage>(null);
    }

    public Task<ExamRecord> ResolveForWebhookAsync(
        string divisionId, Guid? submissionId, string subjectId, string hostRefId, bool forUpdate = false, CancellationToken ct = default)
    {
        var record = Records.Values.FirstOrDefault(r =>
            r.DivisionID == divisionId &&
            ((submissionId.HasValue && r.SubmissionID == submissionId.Value) ||
             (!string.IsNullOrEmpty(subjectId) && r.RecordCode == subjectId) ||
             (!string.IsNullOrEmpty(hostRefId) && r.RecordCode == hostRefId)));
        return Task.FromResult(record);
    }

    public Task<ExamRecord> LockRecordAsync(Guid recordId, CancellationToken ct = default)
    {
        Calls.Add("Lock");
        Records.TryGetValue(recordId, out var record);
        return Task.FromResult(record);
    }

    public Task<IReadOnlyList<ReconcileCandidate>> FindReconcileCandidatesAsync(
        string divisionId, Guid? sessionId, int max = 100, CancellationToken ct = default)
    {
        var query = Records.Values.Where(r => r.SubmissionID != null);
        if (!string.IsNullOrEmpty(divisionId))
            query = query.Where(r => r.DivisionID == divisionId);
        if (sessionId.HasValue)
            query = query.Where(r => r.SessionID == sessionId.Value);
        return Task.FromResult<IReadOnlyList<ReconcileCandidate>>(
            query.Take(max).Select(r => new ReconcileCandidate(r.RecordID, r.SubmissionID.Value, r.DivisionID, r.RecordCode)).ToList());
    }

    public Task<(string Code, string Name)?> ResolveMasterDataAsync(
        string divisionId, string category, string code, CancellationToken ct = default)
    {
        return Task.FromResult<(string Code, string Name)?>((code, $"Tên {code}"));
    }

    public Task<(string Code, string Name)?> ResolveWardAsync(
        string divisionId, string provinceCode, string wardCode, CancellationToken ct = default)
    {
        return Task.FromResult<(string Code, string Name)?>((wardCode, $"Tên {wardCode}"));
    }
}

public class FakeSessionRepoForRecordTests : IExamSessionRepository
{
    public Dictionary<Guid, ExamSession> Sessions { get; } = new();

    public Task<PageResult<ExamSessionResult>> ListAsync(
        string divisionId, ExamSessionFilter filter, CancellationToken ct = default)
        => Task.FromResult(new PageResult<ExamSessionResult>(Array.Empty<ExamSessionResult>(), 1, 20, 0));

    public Task<ExamSession> GetAsync(
        string divisionId, Guid sessionId, bool forUpdate = false, CancellationToken ct = default)
    {
        Sessions.TryGetValue(sessionId, out var s);
        if (s != null && s.DivisionID == divisionId) return Task.FromResult(s);
        return Task.FromResult<ExamSession>(null);
    }

    public Task<bool> ExistsByCodeAsync(
        string divisionId, string sessionCode, Guid? excludingSessionId = null, CancellationToken ct = default)
        => Task.FromResult(false);

    public void Add(ExamSession session) => Sessions[session.SessionID] = session;

    public Task<Organization> GetOrganizationAsync(
        string divisionId, Guid organizationId, CancellationToken ct = default)
        => Task.FromResult<Organization>(null);

    public Task<ExamPackage> GetPackageAsync(
        string divisionId, Guid packageId, CancellationToken ct = default)
        => Task.FromResult<ExamPackage>(null);

    public Task<int> GetRecordCountAsync(
        string divisionId, Guid sessionId, CancellationToken ct = default)
        => Task.FromResult(0);
}

public class FakeRecordCodeAllocator : IRecordCodeAllocator
{
    public string AllocatedCode { get; set; } = "REC-DEFAULT-0001";

    public Task<string> NextAsync(string divisionId, Guid sessionId, string sessionCode, CancellationToken ct = default)
    {
        return Task.FromResult(AllocatedCode);
    }
}

public class RecordFakeUnitOfWork : IUnitOfWork
{
    public int SaveCount { get; private set; }
    public bool FailOnSave { get; set; }
    public bool Discarded { get; private set; }
    public bool TransactionActive { get; private set; }

    public Task<IApplicationTransaction> BeginAsync(CancellationToken ct = default)
    {
        TransactionActive = true;
        return Task.FromResult<IApplicationTransaction>(new TrackingTransaction(() => TransactionActive = false));
    }

    public Task<PersistenceSaveResult> SaveChangesAsync(CancellationToken ct = default)
    {
        if (FailOnSave)
            throw new InvalidOperationException("Simulated save failure");
        SaveCount++;
        return Task.FromResult(new PersistenceSaveResult(PersistenceSaveOutcome.Saved));
    }

    public void DiscardPendingChanges()
    {
        Discarded = true;
    }

    private sealed class TrackingTransaction : IApplicationTransaction
    {
        private readonly Action _close;
        public TrackingTransaction(Action close) => _close = close;
        public Task CommitAsync(CancellationToken ct = default) { _close(); return Task.CompletedTask; }
        public Task RollbackAsync(CancellationToken ct = default) { _close(); return Task.CompletedTask; }
        public ValueTask DisposeAsync() { _close(); return ValueTask.CompletedTask; }
    }
}

public class FakeEnsureHisAdmission : IEnsureHisAdmission
{
    public long NextAdmissionId { get; set; } = 7001;
    public bool ShouldFail { get; set; }
    public ApplicationFailureCode FailureCode { get; set; } = ApplicationFailureCode.HisBadGateway;
    public string FailureMessage { get; set; } = "Lỗi kết nối HIS";
    public EnsureHisAdmissionCommand LastCommand { get; private set; }

    public Task<ApplicationResult<long>> HandleAsync(
        EnsureHisAdmissionCommand command, CancellationToken ct = default)
    {
        LastCommand = command;
        if (ShouldFail)
        {
            return Task.FromResult(ApplicationResult<long>.Fail(FailureCode, FailureMessage));
        }

        return Task.FromResult(ApplicationResult<long>.Success(NextAdmissionId));
    }
}

public class FakeRegistrationFormRepositoryForRecordTests : IRegistrationFormRepository
{
    public readonly List<ExamGroupFormMapping> Mappings = new();

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
        => Task.FromResult<ExamRecord>(null);

    public Task<ExamRecord> GetRecordWithSessionAsync(string divisionId, Guid recordId, bool forUpdate = false, CancellationToken ct = default)
        => Task.FromResult<ExamRecord>(null);
}

public class FakeHisEmrClientForRecordTests : IHisEmrClient
{
    public List<MedicalProcessCreateRequest> CreatedRequests { get; } = new();
    public List<HisRequest> SentRequests { get; } = new();
    public string ExistingProcessesJson { get; set; } = "[]";
    public bool FailCreateProcess { get; set; }
    public HisClientOutcome FailureOutcome { get; set; } = HisClientOutcome.BadGateway;
    public string FailureMessage { get; set; } = "HIS error";

    public Task<HisClientResult<HisJsonDocument>> SendAsync(HisOperation operation, HisRequest request, CancellationToken ct = default)
    {
        SentRequests.Add(request);
        if (operation == HisOperation.ListProcesses)
        {
            return Task.FromResult(HisClientResult<HisJsonDocument>.Success(new HisJsonDocument(ExistingProcessesJson)));
        }
        return Task.FromResult(HisClientResult<HisJsonDocument>.Success(new HisJsonDocument("[]")));
    }

    public Task<HisClientResult<HisJsonDocument>> CreateMedicalProcessAsync(MedicalProcessCreateRequest request, HisCallContext context, CancellationToken ct = default)
    {
        CreatedRequests.Add(request);
        if (FailCreateProcess)
        {
            return Task.FromResult(HisClientResult<HisJsonDocument>.Fail(FailureOutcome, FailureMessage));
        }
        return Task.FromResult(HisClientResult<HisJsonDocument>.Success(new HisJsonDocument("[{\"Id\":\"00000000-0000-0000-0000-000000000001\"}]")));
    }

    public Task<HisClientResult<long>> CreateAdmissionAsync(HisAdmissionCreateRequest request, HisCallContext context, CancellationToken ct = default)
        => Task.FromResult(HisClientResult<long>.Success(9001L));

    public Task<HisClientResult<long>> CreatePatientAsync(HisPatientCreateRequest request, HisCallContext context, CancellationToken ct = default)
        => Task.FromResult(HisClientResult<long>.Success(9002L));

    public Task<HisClientResult<byte[]>> RenderFormPdfAsync(Guid emrDataId, HisRequest request, CancellationToken ct = default)
        => Task.FromResult(HisClientResult<byte[]>.Success(Array.Empty<byte>()));

    public Task<HisClientResult<IReadOnlyList<Icd10Choice>>> GetIcd10ChoicesAsync(string filter, int amount, HisCallContext context, CancellationToken ct = default)
        => Task.FromResult(HisClientResult<IReadOnlyList<Icd10Choice>>.Success(Array.Empty<Icd10Choice>()));

    public Task<HisClientResult<IReadOnlyList<DepartmentCatalogResult>>> ListEmployeeDepartmentsAsync(long employeeId, HisCallContext context, CancellationToken ct = default)
        => Task.FromResult(HisClientResult<IReadOnlyList<DepartmentCatalogResult>>.Success(Array.Empty<DepartmentCatalogResult>()));
}
