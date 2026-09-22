using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using HealthExam.Application.Common;
using HealthExam.Application.ExamSessions;
using HealthExam.Application.Patients;
using HealthExam.Application.His;
using HealthExam.Application.Integrations;
using HealthExam.Application.RegistrationForms;
using HealthExam.Domain.Catalogs;
using HealthExam.Domain.Common;
using HealthExam.Domain.ExamRecords;
using HealthExam.Domain.ExamSessions;

namespace HealthExam.Application.ExamRecords;

public interface ICreateExamRecordHandler
{
    Task<ApplicationResult<ExamRecordResult>> HandleAsync(
        CreateExamRecordCommand command, CancellationToken ct = default);
}

public sealed class CreateExamRecordHandler : ICreateExamRecordHandler
{
    private const int MaxCodeAttempts = 5;
    private const string RecordCodeIndex = "IX_HEX_ExamRecord_DivisionID_RecordCode";
    private const string ActiveIdentityIndex = "UX_HEX_Patient_Division_ActiveIdentity";
    private const string ActiveHisPatientIdIndex = "UX_HEX_Patient_Division_HisPatientID";
    private const string PatientVersionIndex = "UX_HEX_Patient_Division_Lineage_Version";
    public const string GeneratedPatientCodePrefix = "HEX-";

    private readonly IExamRecordRepository _recordRepository;
    private readonly IExamSessionRepository _sessionRepository;
    private readonly IRecordCodeAllocator _allocator;
    private readonly IUnitOfWork _uow;
    private readonly IAuditRepository _audit;
    private readonly PatientRegistrationWriter _writer;
    private readonly IEnsureHisAdmission _ensureAdmission;
    private readonly IRegistrationFormRepository _registrationForms;
    private readonly IHisEmrClient _hisEmrClient;

    public CreateExamRecordHandler(
        IExamRecordRepository recordRepository,
        IExamSessionRepository sessionRepository,
        IRecordCodeAllocator allocator,
        IUnitOfWork uow,
        IAuditRepository audit,
        PatientRegistrationWriter writer,
        IEnsureHisAdmission ensureAdmission = null,
        IRegistrationFormRepository registrationForms = null,
        IHisEmrClient hisEmrClient = null)
    {
        _recordRepository = recordRepository;
        _sessionRepository = sessionRepository;
        _allocator = allocator;
        _uow = uow;
        _audit = audit;
        _writer = writer;
        _ensureAdmission = ensureAdmission;
        _registrationForms = registrationForms;
        _hisEmrClient = hisEmrClient;
    }

    public async Task<ApplicationResult<ExamRecordResult>> HandleAsync(
        CreateExamRecordCommand command, CancellationToken ct = default)
    {
        var errors = new List<ApplicationValidationError>();
        if (string.IsNullOrWhiteSpace(command.FullName))
            errors.Add(new ApplicationValidationError(nameof(command.FullName), "Bỏ trống (bắt buộc)"));
        if (string.IsNullOrWhiteSpace(command.VariantCode))
            errors.Add(new ApplicationValidationError(nameof(command.VariantCode), "Bỏ trống (bắt buộc)"));
        else if (!ExamGroups.IsValid(command.VariantCode))
            errors.Add(new ApplicationValidationError(nameof(command.VariantCode), "Nhóm khám không hợp lệ (DTK_01..DTK_10)"));

        if (command.AdmissionID.HasValue && command.AdmissionID.Value <= 0)
            errors.Add(new ApplicationValidationError(nameof(command.AdmissionID), "Phải lớn hơn 0"));

        if (errors.Count > 0)
        {
            return ApplicationResult<ExamRecordResult>.Fail(
                ApplicationFailureCode.BadRequest, "Dữ liệu không hợp lệ", new ApplicationValidationErrors(errors));
        }

        ExamSession session;
        if (command.SessionID.HasValue)
        {
            session = await _sessionRepository.GetAsync(command.DivisionId, command.SessionID.Value, forUpdate: false, ct);
            if (session == null)
            {
                return ApplicationResult<ExamRecordResult>.Fail(
                    ApplicationFailureCode.NotFound, "Không tìm thấy đợt khám");
            }
        }
        else
        {
            session = await _recordRepository.GetOrCreateDefaultSessionAsync(command.DivisionId, ct);
        }

        if (session.State == ExamSessionState.Closed || session.State == ExamSessionState.Cancelled)
        {
            return ApplicationResult<ExamRecordResult>.Fail(
                ApplicationFailureCode.SessionClosed,
                $"Đợt khám {session.SessionCode} đã đóng, cần mở lại đợt trước khi ghi hồ sơ");
        }

        if (!string.IsNullOrWhiteSpace(command.IdentityNumber))
        {
            var id = command.IdentityNumber.Trim();
            if (await _recordRepository.ExistsDuplicateAsync(command.DivisionId, session.SessionID, id, null, null, ct))
            {
                return ApplicationResult<ExamRecordResult>.Fail(
                    ApplicationFailureCode.DuplicateInSession,
                    $"CCCD {id} đã có hồ sơ trong đợt khám này",
                    ApplicationValidationErrors.Of("IdentityNumber", "Đã có hồ sơ trong đợt"));
            }
        }

        if (!string.IsNullOrWhiteSpace(command.PatientCode))
        {
            var pc = command.PatientCode.Trim();
            if (await _recordRepository.ExistsDuplicateAsync(command.DivisionId, session.SessionID, null, pc, null, ct))
            {
                return ApplicationResult<ExamRecordResult>.Fail(
                    ApplicationFailureCode.DuplicateInSession,
                    $"Mã người bệnh {pc} đã có hồ sơ trong đợt khám này",
                    ApplicationValidationErrors.Of("PatientCode", "Đã có hồ sơ trong đợt"));
            }
        }

        ExamPackage pkg = null;
        if (command.PackageID.HasValue)
        {
            pkg = await _recordRepository.GetPackageAsync(command.DivisionId, command.PackageID.Value, ct);
            if (pkg == null)
            {
                return ApplicationResult<ExamRecordResult>.Fail(
                    ApplicationFailureCode.BadRequest,
                    "Không tìm thấy gói khám",
                    ApplicationValidationErrors.Of(nameof(command.PackageID), "Không tồn tại"));
            }
        }

        if (command.InsuranceValidFrom.HasValue && command.InsuranceValidTo.HasValue &&
            command.InsuranceValidTo.Value < command.InsuranceValidFrom.Value)
        {
            return ApplicationResult<ExamRecordResult>.Fail(
                ApplicationFailureCode.BadRequest,
                "Hạn thẻ bảo hiểm y tế không hợp lệ",
                ApplicationValidationErrors.Of(nameof(command.InsuranceValidTo), "Ngày hết hạn phải lớn hơn hoặc bằng ngày bắt đầu"));
        }

        var (masterOk, masterData, masterResult) = await ResolveMasterDataAsync(command, ct);
        if (!masterOk) return masterResult;

        var now = DateTime.UtcNow;
        var actorId = long.TryParse(command.ActorId, out var parsedActorId) ? parsedActorId : 0L;

        var patientRequest = new PatientWriteRequest(
            DivisionId: command.DivisionId,
            ActorId: actorId,
            ActorKind: command.ActorKind,
            PatientRefID: command.PatientRefID,
            HisPatientID: command.PatientID,
            PatientCode: command.PatientCode?.Trim() ?? "",
            FullName: command.FullName?.Trim() ?? "",
            Dob: command.Dob,
            BirthYear: command.BirthYear,
            GenderID: command.GenderID ?? 0,
            IdentityNumber: command.IdentityNumber?.Trim() ?? "",
            IdentityIssuedDate: command.IdentityIssuedDate,
            IdentityIssuerOptionID: masterData.IdentityIssuer?.OptionID,
            PhoneNumber: command.PhoneNumber?.Trim() ?? "",
            Email: command.Email?.Trim() ?? "",
            Address: command.Address?.Trim() ?? "",
            ProvinceCode: masterData.Province?.Code ?? "",
            WardCode: masterData.Ward?.Code ?? "",
            EthnicityOptionID: masterData.Ethnicity?.OptionID,
            BloodAboCode: masterData.BloodAbo?.Code ?? (command.BloodAboCode?.Trim() ?? ""),
            BloodRhCode: masterData.BloodRh?.Code ?? (command.BloodRhCode?.Trim() ?? ""),
            Insurance: new PatientInsuranceWrite(
                InsuranceNumber: command.InsuranceNumber?.Trim() ?? "",
                InsuranceObjectOptionID: masterData.InsuranceObject?.OptionID,
                RegistrationPlaceOptionID: masterData.RegistrationPlace?.OptionID,
                ValidFrom: command.InsuranceValidFrom,
                ValidTo: command.InsuranceValidTo),
            Employment: new PatientEmploymentWrite(
                OccupationOptionID: masterData.Occupation?.OptionID,
                StaffCode: command.StaffCode?.Trim() ?? "",
                OrgDeptName: command.OrgDeptName?.Trim() ?? "",
                JobTitle: command.JobTitle?.Trim() ?? ""),
            Relative: new PatientRelativeWrite(
                RelationshipCode: masterData.RelativeRelationship?.Code ?? (command.RelativeRelationshipCode?.Trim() ?? ""),
                RelationshipOptionID: null,
                FullName: command.RelativeFullName?.Trim() ?? "",
                IdentityNumber: command.RelativeIdentityNumber?.Trim() ?? "",
                PhoneNumber: command.RelativePhoneNumber?.Trim() ?? ""),
            SetAsActiveProfile: command.SetAsActiveProfile);

        await using var tx = await _uow.BeginAsync(ct);
        var patientResult = await _writer.UpsertAsync(patientRequest, ct);
        if (!patientResult.IsSuccess)
        {
            await tx.RollbackAsync(ct);
            return ApplicationResult<ExamRecordResult>.Fail(
                patientResult.Failure.Code,
                patientResult.Failure.Message,
                patientResult.Failure.Payload);
        }

        var writeResult = patientResult.Value;

        var entity = new ExamRecord
        {
            RecordID = Guid.NewGuid(),
            DivisionID = command.DivisionId,
            SessionID = session.SessionID,
            ImportBatchID = command.ImportBatchID,
            State = ExamRecordState.Waiting,
            AdmissionID = command.AdmissionID,
            RegisteredAt = now,
            RegisteredBy = actorId,
            VariantCode = command.VariantCode.Trim(),
            FormCode = ExamGroups.FormCodePrefix + command.VariantCode.Trim(),
            PackageID = pkg?.PackageID ?? (!command.PackageID.HasValue ? session.PackageID : null),
            PackageName = pkg?.PackageName ?? (!command.PackageID.HasValue ? session.PackageName : ""),
            Note = command.Note,
            ExamReason = command.ExamReason?.Trim() ?? "",
            PaymentSourceOther = command.PaymentSourceOther?.Trim() ?? "",
            PatientRefID = writeResult.PatientRefID,
            InsuranceRefID = writeResult.InsuranceRefID,
            EmploymentRefID = writeResult.EmploymentRefID,
            RelativeRefID = writeResult.RelativeRefID,
            Patient = writeResult.Patient,
            Insurance = writeResult.Insurance,
            Employment = writeResult.Employment,
            Relative = writeResult.Relative,
            CreatedDate = now,
            CreatedBy = actorId,
            CreatedActorKind = command.ActorKind,
            ModifiedDate = now,
            ModifiedBy = actorId,
            ModifiedActorKind = command.ActorKind
        };

        ApplyResolvedMasterData(entity, command, masterData);

        var autoPatientCode = string.IsNullOrWhiteSpace(entity.Patient?.PatientCode);

        if (string.IsNullOrWhiteSpace(command.RecordCode))
        {
            var skipped = new List<string>();

            try
            {
                for (var attempt = 1; ; attempt++)
                {
                    var allocatedCode = await _allocator.NextAsync(command.DivisionId, session.SessionID, session.SessionCode, ct);
                    entity.RecordCode = allocatedCode;

                    if (autoPatientCode)
                    {
                        var gen = ComposeGeneratedPatientCode(entity.RecordCode);
                        if (entity.Patient != null)
                            entity.Patient.PatientCode = gen ?? "";
                    }

                    const string savepoint = "hex_record_insert";
                    if (tx.SupportsSavepoints)
                        await tx.CreateSavepointAsync(savepoint, ct);

                    _recordRepository.Add(entity);
                    var saveResult = await _uow.SaveChangesAsync(ct);
                    if (saveResult.Outcome == PersistenceSaveOutcome.Saved)
                    {
                        _audit.Add(new AuditEntry(
                            command.DivisionId,
                            AuditEntityTypes.Record,
                            entity.RecordID,
                            AuditActions.Create,
                            command.ActorId,
                            null,
                            (short)entity.State,
                            new { entity.RecordCode, FullName = entity.Patient?.FullName ?? "" },
                            command.ActorKind,
                            command.TraceId));
                        await _uow.SaveChangesAsync(ct);

                        await tx.CommitAsync(ct);
                        break;
                    }

                    _uow.DiscardPendingChanges();

                    var isRecordCodeConflict = saveResult.ConstraintName != null &&
                        saveResult.ConstraintName.Contains(RecordCodeIndex, StringComparison.OrdinalIgnoreCase);

                    if (!isRecordCodeConflict || attempt >= MaxCodeAttempts)
                    {
                        if (tx.SupportsSavepoints)
                            await tx.RollbackToSavepointAsync(savepoint, ct);
                        await tx.RollbackAsync(ct);

                        if (saveResult.ConstraintName != null &&
                            (saveResult.ConstraintName.Contains(ActiveIdentityIndex, StringComparison.OrdinalIgnoreCase) ||
                             saveResult.ConstraintName.Contains(ActiveHisPatientIdIndex, StringComparison.OrdinalIgnoreCase) ||
                             saveResult.ConstraintName.Contains(PatientVersionIndex, StringComparison.OrdinalIgnoreCase)))
                        {
                            return ApplicationResult<ExamRecordResult>.Fail(
                                ApplicationFailureCode.InvalidState,
                                "Hồ sơ người bệnh đã được cập nhật, vui lòng tìm lại");
                        }

                        return ApplicationResult<ExamRecordResult>.Fail(
                            ApplicationFailureCode.DuplicateInSession,
                            isRecordCodeConflict
                                ? $"Mã hồ sơ bị chiếm {attempt} lần liên tiếp ({string.Join(", ", skipped)}, {entity.RecordCode}) — kiểm tra lại những hồ sơ được nhập tay kèm mã trong đợt này"
                                : "Người bệnh đã có hồ sơ trong đợt khám này hoặc mã hồ sơ bị trùng");
                    }

                    skipped.Add(entity.RecordCode);
                    if (tx.SupportsSavepoints)
                        await tx.RollbackToSavepointAsync(savepoint, ct);
                }
            }
            catch
            {
                _uow.DiscardPendingChanges();
                await tx.RollbackAsync(ct);
                throw;
            }
        }
        else
        {
            try
            {
                entity.RecordCode = command.RecordCode.Trim();
                if (autoPatientCode)
                {
                    var gen = ComposeGeneratedPatientCode(entity.RecordCode);
                    if (entity.Patient != null)
                        entity.Patient.PatientCode = gen ?? "";
                }

                _recordRepository.Add(entity);
                var saveResult = await _uow.SaveChangesAsync(ct);
                if (saveResult.Outcome == PersistenceSaveOutcome.UniqueConflict)
                {
                    _uow.DiscardPendingChanges();
                    await tx.RollbackAsync(ct);

                    if (saveResult.ConstraintName != null &&
                        (saveResult.ConstraintName.Contains(ActiveIdentityIndex, StringComparison.OrdinalIgnoreCase) ||
                         saveResult.ConstraintName.Contains(ActiveHisPatientIdIndex, StringComparison.OrdinalIgnoreCase) ||
                         saveResult.ConstraintName.Contains(PatientVersionIndex, StringComparison.OrdinalIgnoreCase)))
                    {
                        return ApplicationResult<ExamRecordResult>.Fail(
                            ApplicationFailureCode.InvalidState,
                            "Hồ sơ người bệnh đã được cập nhật, vui lòng tìm lại");
                    }

                    return ApplicationResult<ExamRecordResult>.Fail(
                        ApplicationFailureCode.DuplicateInSession,
                        "Người bệnh đã có hồ sơ trong đợt khám này hoặc mã hồ sơ bị trùng");
                }

                _audit.Add(new AuditEntry(
                    command.DivisionId,
                    AuditEntityTypes.Record,
                    entity.RecordID,
                    AuditActions.Create,
                    command.ActorId,
                    null,
                    (short)entity.State,
                    new { entity.RecordCode, FullName = entity.Patient?.FullName ?? "" },
                    command.ActorKind,
                    command.TraceId));
                await _uow.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
            }
            catch
            {
                _uow.DiscardPendingChanges();
                await tx.RollbackAsync(ct);
                throw;
            }
        }

        if (_ensureAdmission != null && (!entity.AdmissionID.HasValue || entity.AdmissionID.Value <= 0))
        {
            var admRes = await _ensureAdmission.HandleAsync(new EnsureHisAdmissionCommand(
                command.DivisionId,
                entity.RecordID,
                new HisCallContext(command.Credential, command.TraceId, command.DivisionId),
                actorId,
                command.ActorKind), ct);

            if (!admRes.IsSuccess)
            {
                return ApplicationResult<ExamRecordResult>.Fail(
                    admRes.Failure.Code, admRes.Failure.Message, admRes.Failure.Payload);
            }

            entity.AdmissionID = admRes.Value;
        }

        if (_registrationForms != null && _hisEmrClient != null)
        {
            var mapping = await _registrationForms.GetActiveMappingAsync(command.DivisionId, entity.VariantCode, ct);
            if (mapping != null && !string.IsNullOrWhiteSpace(mapping.MedicalTypeCode))
            {
                if (!entity.AdmissionID.HasValue || entity.AdmissionID.Value <= 0)
                {
                    return ApplicationResult<ExamRecordResult>.Fail(
                        ApplicationFailureCode.BadRequest,
                        "Hồ sơ chưa có mã tiếp nhận HIS để tạo tiến trình bệnh án");
                }

                var context = new HisCallContext(command.Credential, command.TraceId, command.DivisionId);
                var processCreatedOrExists = false;

                var listRes = await _hisEmrClient.SendAsync(
                    HisOperation.ListProcesses,
                    new HisRequest(
                        $"{HisConstants.RouteRMedicalProcess}?admissionID={entity.AdmissionID.Value}",
                        "GET",
                        command.Credential,
                        TraceId: command.TraceId,
                        DivisionId: command.DivisionId),
                    ct);

                if (listRes.IsSuccess && !string.IsNullOrWhiteSpace(listRes.Value?.RawJson))
                {
                    try
                    {
                        using var doc = JsonDocument.Parse(listRes.Value.RawJson);
                        if (doc.RootElement.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var item in doc.RootElement.EnumerateArray())
                            {
                                if (item.TryGetProperty("MedicalTypeCode", out var mProp) &&
                                    string.Equals(mProp.GetString(), mapping.MedicalTypeCode, StringComparison.OrdinalIgnoreCase))
                                {
                                    processCreatedOrExists = true;
                                    break;
                                }
                            }
                        }
                    }
                    catch
                    {
                    }
                }

                if (!processCreatedOrExists)
                {
                    var createReq = new MedicalProcessCreateRequest(
                        MedicalTypeCode: mapping.MedicalTypeCode,
                        AdmissionID: entity.AdmissionID.Value,
                        MedicalTypeCodeOld: null);

                    var createRes = await _hisEmrClient.CreateMedicalProcessAsync(createReq, context, ct);
                    if (!createRes.IsSuccess)
                    {
                        var failureCode = createRes.Outcome switch
                        {
                            HisClientOutcome.Unauthorized => ApplicationFailureCode.Unauthorized,
                            HisClientOutcome.Forbidden => ApplicationFailureCode.Forbidden,
                            HisClientOutcome.NotFound => ApplicationFailureCode.NotFound,
                            HisClientOutcome.Timeout => ApplicationFailureCode.HisTimeout,
                            HisClientOutcome.VendorNotConfigured => ApplicationFailureCode.VendorNotConfigured,
                            HisClientOutcome.SignPrecondition => ApplicationFailureCode.SignPrecondition,
                            _ => ApplicationFailureCode.HisBadGateway
                        };
                        return ApplicationResult<ExamRecordResult>.Fail(
                            failureCode,
                            !string.IsNullOrWhiteSpace(createRes.Message)
                                ? createRes.Message
                                : "Không thể tạo quy trình khám bệnh trên HIS");
                    }
                }
            }
        }

        var result = await _recordRepository.GetResultAsync(command.DivisionId, entity.RecordID, ct);
        return ApplicationResult<ExamRecordResult>.Success(result);
    }

    public static string ComposeGeneratedPatientCode(string recordCode)
    {
        var code = GeneratedPatientCodePrefix + (recordCode ?? "").Trim();
        return code.Length > 50 ? null : code;
    }

    private async Task<(bool Success, ResolvedMasterData Data, ApplicationResult<ExamRecordResult> Error)> ResolveMasterDataAsync(
        CreateExamRecordCommand cmd, CancellationToken ct)
    {
        var data = new ResolvedMasterData();

        if (cmd.EthnicityCode != null && !string.IsNullOrWhiteSpace(cmd.EthnicityCode))
        {
            var opt = await _recordRepository.ResolveMasterDataOptionAsync(cmd.DivisionId, MasterDataCategories.Ethnicity, cmd.EthnicityCode, ct);
            if (opt == null) return (false, null, MasterDataError(nameof(cmd.EthnicityCode)));
            data.Ethnicity = opt;
        }

        if (cmd.OccupationCode != null && !string.IsNullOrWhiteSpace(cmd.OccupationCode))
        {
            var opt = await _recordRepository.ResolveMasterDataOptionAsync(cmd.DivisionId, MasterDataCategories.Occupation, cmd.OccupationCode, ct);
            if (opt == null) return (false, null, MasterDataError(nameof(cmd.OccupationCode)));
            data.Occupation = opt;
        }

        if (cmd.BloodAboCode != null && !string.IsNullOrWhiteSpace(cmd.BloodAboCode))
        {
            var opt = await _recordRepository.ResolveMasterDataAsync(cmd.DivisionId, MasterDataCategories.BloodAbo, cmd.BloodAboCode, ct);
            if (opt == null) return (false, null, MasterDataError(nameof(cmd.BloodAboCode)));
            data.BloodAbo = opt;
        }

        if (cmd.BloodRhCode != null && !string.IsNullOrWhiteSpace(cmd.BloodRhCode))
        {
            var opt = await _recordRepository.ResolveMasterDataAsync(cmd.DivisionId, MasterDataCategories.BloodRh, cmd.BloodRhCode, ct);
            if (opt == null) return (false, null, MasterDataError(nameof(cmd.BloodRhCode)));
            data.BloodRh = opt;
        }

        if (cmd.ProvinceCode != null && !string.IsNullOrWhiteSpace(cmd.ProvinceCode))
        {
            var opt = await _recordRepository.ResolveMasterDataAsync(cmd.DivisionId, MasterDataCategories.Province, cmd.ProvinceCode, ct);
            if (opt == null) return (false, null, MasterDataError(nameof(cmd.ProvinceCode)));
            data.Province = opt;
        }

        if (cmd.WardCode != null && !string.IsNullOrWhiteSpace(cmd.WardCode))
        {
            if (string.IsNullOrWhiteSpace(cmd.ProvinceCode))
            {
                return (false, null, ApplicationResult<ExamRecordResult>.Fail(
                    ApplicationFailureCode.BadRequest,
                    "Mã tỉnh/thành phố không được để trống",
                    ApplicationValidationErrors.Of(nameof(cmd.ProvinceCode), "Bỏ trống (bắt buộc)")));
            }
            var opt = await _recordRepository.ResolveWardAsync(cmd.DivisionId, cmd.ProvinceCode, cmd.WardCode, ct);
            if (opt == null) return (false, null, MasterDataError(nameof(cmd.WardCode)));
            data.Ward = opt;
        }

        if (cmd.IdentityIssuerCode != null && !string.IsNullOrWhiteSpace(cmd.IdentityIssuerCode))
        {
            var opt = await _recordRepository.ResolveMasterDataOptionAsync(cmd.DivisionId, MasterDataCategories.IdentityIssuer, cmd.IdentityIssuerCode, ct);
            if (opt == null) return (false, null, MasterDataError(nameof(cmd.IdentityIssuerCode)));
            data.IdentityIssuer = opt;
        }

        if (cmd.RelativeRelationshipCode != null && !string.IsNullOrWhiteSpace(cmd.RelativeRelationshipCode))
        {
            var opt = await _recordRepository.ResolveMasterDataAsync(cmd.DivisionId, MasterDataCategories.Relationship, cmd.RelativeRelationshipCode, ct);
            if (opt == null) return (false, null, MasterDataError(nameof(cmd.RelativeRelationshipCode)));
            data.RelativeRelationship = opt;
        }

        if (cmd.InsuranceObjectCode != null && !string.IsNullOrWhiteSpace(cmd.InsuranceObjectCode))
        {
            var opt = await _recordRepository.ResolveMasterDataOptionAsync(cmd.DivisionId, MasterDataCategories.InsuranceObject, cmd.InsuranceObjectCode, ct);
            if (opt == null) return (false, null, MasterDataError(nameof(cmd.InsuranceObjectCode)));
            data.InsuranceObject = opt;
        }

        if (cmd.RegistrationPlaceCode != null && !string.IsNullOrWhiteSpace(cmd.RegistrationPlaceCode))
        {
            var opt = await _recordRepository.ResolveMasterDataOptionAsync(cmd.DivisionId, MasterDataCategories.RegistrationPlace, cmd.RegistrationPlaceCode, ct);
            if (opt == null) return (false, null, MasterDataError(nameof(cmd.RegistrationPlaceCode)));
            data.RegistrationPlace = opt;
        }

        if (cmd.PatientTypeCode != null && !string.IsNullOrWhiteSpace(cmd.PatientTypeCode))
        {
            var opt = await _recordRepository.ResolveMasterDataOptionAsync(cmd.DivisionId, MasterDataCategories.PatientType, cmd.PatientTypeCode, ct);
            if (opt == null) return (false, null, MasterDataError(nameof(cmd.PatientTypeCode)));
            data.PatientType = opt;
        }

        if (cmd.PatientSubjectCode != null && !string.IsNullOrWhiteSpace(cmd.PatientSubjectCode))
        {
            var opt = await _recordRepository.ResolveMasterDataOptionAsync(cmd.DivisionId, MasterDataCategories.PatientSubject, cmd.PatientSubjectCode, ct);
            if (opt == null) return (false, null, MasterDataError(nameof(cmd.PatientSubjectCode)));
            data.PatientSubject = opt;
        }

        if (cmd.PaymentSourceCode != null && !string.IsNullOrWhiteSpace(cmd.PaymentSourceCode))
        {
            var opt = await _recordRepository.ResolveMasterDataOptionAsync(cmd.DivisionId, MasterDataCategories.PaymentSource, cmd.PaymentSourceCode, ct);
            if (opt == null) return (false, null, MasterDataError(nameof(cmd.PaymentSourceCode)));
            data.PaymentSource = opt;
        }

        if (cmd.ExamLocationCode != null && !string.IsNullOrWhiteSpace(cmd.ExamLocationCode))
        {
            var opt = await _recordRepository.ResolveMasterDataOptionAsync(cmd.DivisionId, MasterDataCategories.ExamLocation, cmd.ExamLocationCode, ct);
            if (opt == null) return (false, null, MasterDataError(nameof(cmd.ExamLocationCode)));
            data.ExamLocation = opt;
        }

        return (true, data, null);
    }

    private static ApplicationResult<ExamRecordResult> MasterDataError(string field) =>
        ApplicationResult<ExamRecordResult>.Fail(
            ApplicationFailureCode.BadRequest,
            "Mã danh mục không hợp lệ",
            ApplicationValidationErrors.Of(field, "Mã danh mục không hợp lệ"));

    private static void ApplyResolvedMasterData(ExamRecord entity, CreateExamRecordCommand cmd, ResolvedMasterData data)
    {
        if (cmd.ProvinceCode != null)
        {
            entity.ProvinceCode = data.Province?.Code ?? "";
            entity.ProvinceName = data.Province?.Name ?? "";
        }
        if (cmd.WardCode != null)
        {
            entity.WardCode = data.Ward?.Code ?? "";
            entity.WardName = data.Ward?.Name ?? "";
        }
        if (cmd.PatientTypeCode != null)
        {
            entity.PatientTypeOption = data.PatientType;
            entity.PatientTypeOptionID = data.PatientType?.OptionID;
        }
        if (cmd.PatientSubjectCode != null)
        {
            entity.PatientSubjectOption = data.PatientSubject;
            entity.PatientSubjectOptionID = data.PatientSubject?.OptionID;
        }
        if (cmd.PaymentSourceCode != null)
        {
            entity.PaymentSourceOption = data.PaymentSource;
            entity.PaymentSourceOptionID = data.PaymentSource?.OptionID;
        }
        if (cmd.ExamLocationCode != null)
        {
            entity.ExamLocationOption = data.ExamLocation;
            entity.ExamLocationOptionID = data.ExamLocation?.OptionID;
        }

        if (entity.Patient != null)
        {
            if (data.Ethnicity != null)
            {
                entity.Patient.EthnicityOption = data.Ethnicity;
                entity.Patient.EthnicityOptionID = data.Ethnicity.OptionID;
            }
            if (data.IdentityIssuer != null)
            {
                entity.Patient.IdentityIssuerOption = data.IdentityIssuer;
                entity.Patient.IdentityIssuerOptionID = data.IdentityIssuer.OptionID;
            }
        }

        if (entity.Employment != null && data.Occupation != null)
        {
            entity.Employment.OccupationOption = data.Occupation;
            entity.Employment.OccupationOptionID = data.Occupation.OptionID;
        }

        if (entity.Insurance != null)
        {
            if (data.InsuranceObject != null)
            {
                entity.Insurance.InsuranceObjectOption = data.InsuranceObject;
                entity.Insurance.InsuranceObjectOptionID = data.InsuranceObject.OptionID;
            }
            if (data.RegistrationPlace != null)
            {
                entity.Insurance.RegistrationPlaceOption = data.RegistrationPlace;
                entity.Insurance.RegistrationPlaceOptionID = data.RegistrationPlace.OptionID;
            }
        }

        if (entity.PaymentSourceOption != null && entity.PaymentSourceOption.Code != MasterDataCategories.OtherPaymentSource)
        {
            entity.PaymentSourceOther = "";
        }
    }
}
