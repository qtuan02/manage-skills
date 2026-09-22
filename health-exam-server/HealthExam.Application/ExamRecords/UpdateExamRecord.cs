using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using HealthExam.Application.Common;
using HealthExam.Application.ExamSessions;
using HealthExam.Application.Patients;
using HealthExam.Domain.Catalogs;
using HealthExam.Domain.Common;
using HealthExam.Domain.ExamRecords;
using HealthExam.Domain.ExamSessions;

namespace HealthExam.Application.ExamRecords;

public interface IUpdateExamRecordHandler
{
    Task<ApplicationResult<ExamRecordResult>> HandleAsync(
        UpdateExamRecordCommand command, CancellationToken ct = default);
}

public sealed class UpdateExamRecordHandler : IUpdateExamRecordHandler
{
    private const string ActiveIdentityIndex = "UX_HEX_Patient_Division_ActiveIdentity";
    private const string ActiveHisPatientIdIndex = "UX_HEX_Patient_Division_HisPatientID";
    private const string PatientVersionIndex = "UX_HEX_Patient_Division_Lineage_Version";

    private readonly IExamRecordRepository _recordRepository;
    private readonly IExamSessionRepository _sessionRepository;
    private readonly IUnitOfWork _uow;
    private readonly IAuditRepository _audit;
    private readonly PatientRegistrationWriter _writer;

    public UpdateExamRecordHandler(
        IExamRecordRepository recordRepository,
        IExamSessionRepository sessionRepository,
        IUnitOfWork uow,
        IAuditRepository audit,
        PatientRegistrationWriter writer)
    {
        _recordRepository = recordRepository;
        _sessionRepository = sessionRepository;
        _uow = uow;
        _audit = audit;
        _writer = writer;
    }

    public async Task<ApplicationResult<ExamRecordResult>> HandleAsync(
        UpdateExamRecordCommand command, CancellationToken ct = default)
    {
        var entity = await _recordRepository.GetWithRegistrationAsync(command.DivisionId, command.RecordId, forUpdate: true, ct);
        if (entity == null)
        {
            return ApplicationResult<ExamRecordResult>.Fail(
                ApplicationFailureCode.NotFound, "Không tìm thấy hồ sơ khám");
        }

        if (ExamRecordStates.IsCancelled(entity.State))
        {
            return ApplicationResult<ExamRecordResult>.Fail(
                ApplicationFailureCode.InvalidState, "Hồ sơ đã hủy, không sửa được");
        }

        // Spec §2: hồ sơ đã ký kết luận là bất biến — hành chính in trên PDF đã ký.
        if (entity.SignStatus == ExamRecordSignStatus.Signed)
        {
            return ApplicationResult<ExamRecordResult>.Fail(
                ApplicationFailureCode.InvalidState, "Hồ sơ đã ký kết luận, không sửa được");
        }

        var session = await _sessionRepository.GetAsync(command.DivisionId, entity.SessionID, forUpdate: false, ct);
        if (session != null && (session.State == ExamSessionState.Closed || session.State == ExamSessionState.Cancelled))
        {
            return ApplicationResult<ExamRecordResult>.Fail(
                ApplicationFailureCode.SessionClosed,
                $"Đợt khám {session.SessionCode} đã đóng, cần mở lại đợt trước khi ghi hồ sơ");
        }

        if (!string.IsNullOrWhiteSpace(command.VariantCode) && !ExamGroups.IsValid(command.VariantCode))
        {
            return ApplicationResult<ExamRecordResult>.Fail(
                ApplicationFailureCode.BadRequest,
                "Nhóm khám không hợp lệ",
                ApplicationValidationErrors.Of(nameof(command.VariantCode), "Nhóm khám không hợp lệ (DTK_01..DTK_10)"));
        }

        if (command.AdmissionID.HasValue && command.AdmissionID.Value <= 0)
        {
            return ApplicationResult<ExamRecordResult>.Fail(
                ApplicationFailureCode.BadRequest,
                "Mã lượt tiếp nhận không hợp lệ",
                ApplicationValidationErrors.Of(nameof(command.AdmissionID), "Phải lớn hơn 0"));
        }

        if (!string.IsNullOrWhiteSpace(command.IdentityNumber))
        {
            var id = command.IdentityNumber.Trim();
            if (await _recordRepository.ExistsDuplicateAsync(command.DivisionId, entity.SessionID, id, null, command.RecordId, ct))
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
            if (await _recordRepository.ExistsDuplicateAsync(command.DivisionId, entity.SessionID, null, pc, command.RecordId, ct))
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
            entity.PackageID = pkg.PackageID;
            entity.PackageName = pkg.PackageName;
        }

        var fromDate = command.InsuranceValidFrom ?? entity.Insurance?.ValidFrom;
        var toDate = command.InsuranceValidTo ?? entity.Insurance?.ValidTo;
        if (fromDate.HasValue && toDate.HasValue && toDate.Value < fromDate.Value)
        {
            return ApplicationResult<ExamRecordResult>.Fail(
                ApplicationFailureCode.BadRequest,
                "Hạn thẻ bảo hiểm y tế không hợp lệ",
                ApplicationValidationErrors.Of(nameof(command.InsuranceValidTo), "Ngày hết hạn phải lớn hơn hoặc bằng ngày bắt đầu"));
        }

        var (masterOk, masterData, masterResult) = await ResolveMasterDataAsync(command, entity.ProvinceCode, ct);
        if (!masterOk) return masterResult;

        var ethOptId = masterData.Ethnicity != null ? masterData.Ethnicity.OptionID : entity.Patient?.EthnicityOptionID;
        if (command.EthnicityCode != null && string.IsNullOrWhiteSpace(command.EthnicityCode)) ethOptId = null;

        var issuerOptId = masterData.IdentityIssuer != null ? masterData.IdentityIssuer.OptionID : entity.Patient?.IdentityIssuerOptionID;
        if (command.IdentityIssuerCode != null && string.IsNullOrWhiteSpace(command.IdentityIssuerCode)) issuerOptId = null;

        var insObjOptId = masterData.InsuranceObject != null ? masterData.InsuranceObject.OptionID : entity.Insurance?.InsuranceObjectOptionID;
        if (command.InsuranceObjectCode != null && string.IsNullOrWhiteSpace(command.InsuranceObjectCode)) insObjOptId = null;

        var regPlaceOptId = masterData.RegistrationPlace != null ? masterData.RegistrationPlace.OptionID : entity.Insurance?.RegistrationPlaceOptionID;
        if (command.RegistrationPlaceCode != null && string.IsNullOrWhiteSpace(command.RegistrationPlaceCode)) regPlaceOptId = null;

        var occOptId = masterData.Occupation != null ? masterData.Occupation.OptionID : entity.Employment?.OccupationOptionID;
        if (command.OccupationCode != null && string.IsNullOrWhiteSpace(command.OccupationCode)) occOptId = null;

        var insuranceWrite = new PatientInsuranceWrite(
            InsuranceNumber: command.InsuranceNumber != null ? command.InsuranceNumber.Trim() : (entity.Insurance?.InsuranceNumber ?? ""),
            InsuranceObjectOptionID: insObjOptId,
            RegistrationPlaceOptionID: regPlaceOptId,
            ValidFrom: command.InsuranceValidFrom ?? entity.Insurance?.ValidFrom,
            ValidTo: command.InsuranceValidTo ?? entity.Insurance?.ValidTo);

        var employmentWrite = new PatientEmploymentWrite(
            OccupationOptionID: occOptId,
            StaffCode: command.StaffCode != null ? command.StaffCode.Trim() : (entity.Employment?.StaffCode ?? ""),
            OrgDeptName: command.OrgDeptName != null ? command.OrgDeptName.Trim() : (entity.Employment?.OrgDeptName ?? ""),
            JobTitle: command.JobTitle != null ? command.JobTitle.Trim() : (entity.Employment?.JobTitle ?? ""));

        var relativeWrite = new PatientRelativeWrite(
            RelationshipCode: command.RelativeRelationshipCode != null ? command.RelativeRelationshipCode.Trim() : (entity.Relative?.RelationshipCode ?? ""),
            RelationshipOptionID: null,
            FullName: command.RelativeFullName != null ? command.RelativeFullName.Trim() : (entity.Relative?.FullName ?? ""),
            IdentityNumber: command.RelativeIdentityNumber != null ? command.RelativeIdentityNumber.Trim() : (entity.Relative?.IdentityNumber ?? ""),
            PhoneNumber: command.RelativePhoneNumber != null ? command.RelativePhoneNumber.Trim() : (entity.Relative?.PhoneNumber ?? ""));

        var actorId = long.TryParse(command.ActorId, out var parsedActorId) ? parsedActorId : 0L;

        var sourcePatientRefId = command.PatientRefID ?? entity.PatientRefID;
        var hisPatientId = (command.PatientID.HasValue && command.PatientID.Value > 0)
            ? command.PatientID.Value
            : entity.Patient?.HisPatientID;

        var patientRequest = new PatientWriteRequest(
            DivisionId: command.DivisionId,
            ActorId: actorId,
            ActorKind: command.ActorKind,
            PatientRefID: sourcePatientRefId,
            HisPatientID: hisPatientId,
            PatientCode: command.PatientCode != null ? command.PatientCode.Trim() : (entity.Patient?.PatientCode ?? ""),
            FullName: command.FullName != null ? command.FullName.Trim() : (entity.Patient?.FullName ?? ""),
            Dob: command.Dob ?? entity.Patient?.Dob,
            BirthYear: command.BirthYear ?? entity.Patient?.BirthYear,
            GenderID: command.GenderID ?? entity.Patient?.GenderID ?? 0,
            IdentityNumber: command.IdentityNumber != null ? command.IdentityNumber.Trim() : (entity.Patient?.IdentityNumber ?? ""),
            IdentityIssuedDate: command.IdentityIssuedDate ?? entity.Patient?.IdentityIssuedDate,
            IdentityIssuerOptionID: issuerOptId,
            PhoneNumber: command.PhoneNumber != null ? command.PhoneNumber.Trim() : (entity.Patient?.PhoneNumber ?? ""),
            Email: command.Email != null ? command.Email.Trim() : (entity.Patient?.Email ?? ""),
            Address: command.Address != null ? command.Address.Trim() : (entity.Patient?.Address ?? ""),
            ProvinceCode: command.ProvinceCode != null ? (masterData.Province?.Code ?? "") : (entity.Patient?.ProvinceCode ?? ""),
            WardCode: command.WardCode != null ? (masterData.Ward?.Code ?? "") : (entity.Patient?.WardCode ?? ""),
            EthnicityOptionID: ethOptId,
            BloodAboCode: command.BloodAboCode != null ? command.BloodAboCode.Trim() : (entity.Patient?.BloodAboCode ?? ""),
            BloodRhCode: command.BloodRhCode != null ? command.BloodRhCode.Trim() : (entity.Patient?.BloodRhCode ?? ""),
            Insurance: insuranceWrite,
            Employment: employmentWrite,
            Relative: relativeWrite,
            SetAsActiveProfile: command.SetAsActiveProfile);

        await using var tx = await _uow.BeginAsync(ct);
        try
        {
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

            entity.Patient = writeResult.Patient;
            entity.PatientRefID = writeResult.PatientRefID;
            entity.Insurance = writeResult.Insurance;
            entity.InsuranceRefID = writeResult.InsuranceRefID;
            entity.Employment = writeResult.Employment;
            entity.EmploymentRefID = writeResult.EmploymentRefID;
            entity.Relative = writeResult.Relative;
            entity.RelativeRefID = writeResult.RelativeRefID;

            if (command.AdmissionID.HasValue) entity.AdmissionID = command.AdmissionID.Value;
            if (command.Note != null) entity.Note = command.Note;

            if (!string.IsNullOrWhiteSpace(command.RecordCode))
                entity.RecordCode = command.RecordCode.Trim();

            if (!string.IsNullOrWhiteSpace(command.VariantCode))
            {
                var variantChanged = entity.VariantCode != command.VariantCode.Trim();
                entity.VariantCode = command.VariantCode.Trim();
                if (variantChanged)
                {
                    entity.FormID = null;
                    entity.SubmissionID = null;
                }
                entity.FormCode = ExamGroups.FormCodePrefix + command.VariantCode.Trim();
            }

            if (command.ExamReason != null) entity.ExamReason = command.ExamReason.Trim();
            if (command.PaymentSourceOther != null) entity.PaymentSourceOther = command.PaymentSourceOther.Trim();

            ApplyResolvedMasterData(entity, command, masterData);

            var now = DateTime.UtcNow;
            entity.ModifiedDate = now;
            entity.ModifiedBy = actorId;
            entity.ModifiedActorKind = command.ActorKind;

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
                AuditActions.Update,
                command.ActorId,
                null,
                null,
                new { entity.RecordCode, FullName = entity.Patient?.FullName ?? "" },
                command.ActorKind,
                command.TraceId));
            await _uow.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            var result = await _recordRepository.GetResultAsync(command.DivisionId, entity.RecordID, ct);
            return ApplicationResult<ExamRecordResult>.Success(result);
        }
        catch
        {
            _uow.DiscardPendingChanges();
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    private async Task<(bool Success, ResolvedMasterData Data, ApplicationResult<ExamRecordResult> Error)> ResolveMasterDataAsync(
        UpdateExamRecordCommand cmd, string existingProvinceCode, CancellationToken ct)
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
            var provCode = cmd.ProvinceCode ?? existingProvinceCode;
            if (string.IsNullOrWhiteSpace(provCode))
            {
                return (false, null, ApplicationResult<ExamRecordResult>.Fail(
                    ApplicationFailureCode.BadRequest,
                    "Mã tỉnh/thành phố không được để trống",
                    ApplicationValidationErrors.Of("ProvinceCode", "Bỏ trống (bắt buộc)")));
            }
            var opt = await _recordRepository.ResolveWardAsync(cmd.DivisionId, provCode, cmd.WardCode, ct);
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

    private static void ApplyResolvedMasterData(ExamRecord entity, UpdateExamRecordCommand cmd, ResolvedMasterData data)
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
