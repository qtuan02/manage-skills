using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using HealthExam.Application.Common;
using HealthExam.Application.ExamSessions;
using HealthExam.Domain.Catalogs;
using HealthExam.Domain.Common;
using HealthExam.Domain.ExamRecords;
using HealthExam.Domain.ExamSessions;

namespace HealthExam.Application.ExamRecords;

public interface IConfirmExamRecordHandler
{
    Task<ApplicationResult<ExamRecordResult>> HandleAsync(
        ConfirmExamRecordCommand command, CancellationToken ct = default);
}

public sealed class ConfirmExamRecordHandler : IConfirmExamRecordHandler
{
    private readonly IExamRecordRepository _recordRepository;
    private readonly IExamSessionRepository _sessionRepository;
    private readonly IUnitOfWork _uow;
    private readonly IAuditRepository _audit;

    public ConfirmExamRecordHandler(
        IExamRecordRepository recordRepository,
        IExamSessionRepository sessionRepository,
        IUnitOfWork uow,
        IAuditRepository audit)
    {
        _recordRepository = recordRepository;
        _sessionRepository = sessionRepository;
        _uow = uow;
        _audit = audit;
    }

    public async Task<ApplicationResult<ExamRecordResult>> HandleAsync(
        ConfirmExamRecordCommand command, CancellationToken ct = default)
    {
        var record = await _recordRepository.GetWithRegistrationAsync(command.DivisionId, command.RecordId, forUpdate: true, ct);
        if (record == null)
        {
            return ApplicationResult<ExamRecordResult>.Fail(
                ApplicationFailureCode.NotFound, "Không tìm thấy hồ sơ khám");
        }

        var fromState = record.State;
        if (fromState != ExamRecordState.NotRegistered)
        {
            return ApplicationResult<ExamRecordResult>.Fail(
                ApplicationFailureCode.InvalidState,
                $"Hồ sơ đang ở trạng thái \"{ExamRecordStateNames.Of(fromState)}\", chỉ hồ sơ \"Chưa đăng ký\" mới chốt đăng ký được");
        }

        var session = await _sessionRepository.GetAsync(command.DivisionId, record.SessionID, forUpdate: false, ct);
        if (session != null && (session.State == ExamSessionState.Closed || session.State == ExamSessionState.Cancelled))
        {
            return ApplicationResult<ExamRecordResult>.Fail(
                ApplicationFailureCode.SessionClosed,
                $"Đợt khám {session.SessionCode} đã đóng, cần mở lại đợt trước khi ghi hồ sơ");
        }

        var errors = new List<ApplicationValidationError>();
        if (string.IsNullOrWhiteSpace(record.ExamReason))
            errors.Add(new ApplicationValidationError(nameof(record.ExamReason), "Bỏ trống (bắt buộc)"));
        if (record.PaymentSourceOption?.Code == MasterDataCategories.OtherPaymentSource &&
            string.IsNullOrWhiteSpace(record.PaymentSourceOther))
            errors.Add(new ApplicationValidationError(nameof(record.PaymentSourceOther), "Bỏ trống (bắt buộc)"));

        if (errors.Count > 0)
        {
            return ApplicationResult<ExamRecordResult>.Fail(
                ApplicationFailureCode.BadRequest, "Thông tin đăng ký chưa đầy đủ", new ApplicationValidationErrors(errors));
        }

        var domainResult = record.Confirm();
        if (!domainResult.IsSuccess)
        {
            return ApplicationResult<ExamRecordResult>.Fail(
                ApplicationFailureCode.InvalidState,
                $"Hồ sơ đang ở trạng thái \"{ExamRecordStateNames.Of(fromState)}\", chỉ hồ sơ \"Chưa đăng ký\" mới chốt đăng ký được");
        }

        var now = DateTime.UtcNow;
        var actorId = long.TryParse(command.ActorId, out var parsedActorId) ? parsedActorId : 0L;

        record.RegisteredAt = now;
        record.RegisteredBy = actorId;
        record.ModifiedDate = now;
        record.ModifiedBy = actorId;
        record.ModifiedActorKind = command.ActorKind;

        _audit.Add(new AuditEntry(
            command.DivisionId,
            AuditEntityTypes.Record,
            record.RecordID,
            AuditActions.StateChange,
            command.ActorId,
            (short)fromState,
            (short)record.State,
            new { record.RecordCode },
            command.ActorKind,
            command.TraceId));

        await _uow.SaveChangesAsync(ct);

        var result = await _recordRepository.GetResultAsync(command.DivisionId, command.RecordId, ct);
        return ApplicationResult<ExamRecordResult>.Success(result);
    }
}
