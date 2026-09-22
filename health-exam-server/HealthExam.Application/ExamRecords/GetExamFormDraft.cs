using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using HealthExam.Application.Common;
using HealthExam.Application.ExamSessions;
using HealthExam.Application.Integrations;
using HealthExam.Domain.Common;
using HealthExam.Domain.ExamRecords;
using HealthExam.Domain.ExamSessions;

namespace HealthExam.Application.ExamRecords;

public sealed record GetExamFormDraftQuery(
    string DivisionId,
    Guid RecordId,
    string ActorId = "",
    ActorKind ActorKind = ActorKind.Employee);

public sealed record ExamFormDraftResult(
    Guid RecordID,
    string RecordCode,
    Guid SessionID,
    string SessionCode,
    string VariantCode,
    Guid FormID,
    string FormCode,
    string FormName,
    string VersionCode,
    DateOnly EffectiveOn,
    string HostRefType,
    string HostRefID,
    object Layout,
    object PrefillValues,
    object MissingContext,
    Dictionary<string, Dictionary<string, string>> ContextValues);

public interface IGetExamFormDraftHandler
{
    Task<ApplicationResult<ExamFormDraftResult>> HandleAsync(
        GetExamFormDraftQuery query, CancellationToken ct = default);
}

public class GetExamFormDraftHandler : IGetExamFormDraftHandler
{
    private readonly IExamRecordRepository _recordRepository;
    private readonly IExamSessionRepository _sessionRepository;
    private readonly IFormServerClient _formServerClient;
    private readonly IUnitOfWork _uow;
    private readonly IClock _clock;

    public GetExamFormDraftHandler(
        IExamRecordRepository recordRepository,
        IExamSessionRepository sessionRepository,
        IFormServerClient formServerClient,
        IUnitOfWork uow,
        IClock clock)
    {
        _recordRepository = recordRepository;
        _sessionRepository = sessionRepository;
        _formServerClient = formServerClient;
        _uow = uow;
        _clock = clock;
    }

    public async Task<ApplicationResult<ExamFormDraftResult>> HandleAsync(
        GetExamFormDraftQuery query, CancellationToken ct = default)
    {
        var record = await _recordRepository.GetWithRegistrationAsync(query.DivisionId, query.RecordId, forUpdate: true, ct);
        if (record == null)
            return ApplicationResult<ExamFormDraftResult>.Fail(
                ApplicationFailureCode.NotFound, "Không tìm thấy hồ sơ khám");

        if (string.IsNullOrWhiteSpace(record.VariantCode))
            return ApplicationResult<ExamFormDraftResult>.Fail(
                ApplicationFailureCode.BadRequest,
                "Hồ sơ chưa chọn Nhóm khám nên chưa xác định được biểu mẫu",
                new Dictionary<string, string[]> { [nameof(record.VariantCode)] = new[] { "Bỏ trống (bắt buộc)" } });

        var session = await _sessionRepository.GetAsync(query.DivisionId, record.SessionID, forUpdate: false, ct);
        if (session == null)
            return ApplicationResult<ExamFormDraftResult>.Fail(
                ApplicationFailureCode.NotFound, "Không tìm thấy đợt khám");

        var effectiveOn = EffectiveOnOf(session);

        Guid formId;
        string formCode;
        string formName = "";
        string versionCode = "";

        if (record.FormID.HasValue && record.FormID.Value != Guid.Empty)
        {
            formId = record.FormID.Value;
            formCode = record.FormCode;
        }
        else
        {
            if (session.State == ExamSessionState.Closed || session.State == ExamSessionState.Cancelled)
            {
                return ApplicationResult<ExamFormDraftResult>.Fail(
                    ApplicationFailureCode.SessionClosed,
                    $"Đợt khám {session.SessionCode} đã đóng, cần mở lại đợt trước khi ghi hồ sơ");
            }

            var resolved = await _formServerClient.ResolveFormAsync(record.VariantCode, effectiveOn, ct);
            formId = resolved.FormID;
            formCode = resolved.FormCode;
            formName = resolved.FormName;
            versionCode = resolved.VersionCode;

            record.FormID = formId;
            record.FormCode = formCode;
            record.ModifiedDate = _clock.UtcNow;
            if (long.TryParse(query.ActorId, out var aid))
                record.ModifiedBy = aid;
            record.ModifiedActorKind = query.ActorKind;

            await _uow.SaveChangesAsync(ct);
        }

        var contextValues = BuildContextValues(session, record);
        var hostRefId = HostRefIdOf(session, record);

        var draft = await _formServerClient.CreateDraftAsync(
            formId, ModuleCodes.HostRefType, hostRefId, contextValues, ct);

        var result = new ExamFormDraftResult(
            RecordID: record.RecordID,
            RecordCode: record.RecordCode,
            SessionID: session.SessionID,
            SessionCode: session.SessionCode,
            VariantCode: record.VariantCode,
            FormID: formId,
            FormCode: formCode,
            FormName: formName,
            VersionCode: versionCode,
            EffectiveOn: effectiveOn,
            HostRefType: ModuleCodes.HostRefType,
            HostRefID: hostRefId,
            Layout: draft.Layout,
            PrefillValues: draft.PrefillValues,
            MissingContext: draft.MissingContext,
            ContextValues: contextValues);

        return ApplicationResult<ExamFormDraftResult>.Success(result);
    }

    public static DateOnly EffectiveOnOf(ExamSession session) => session.ExamDate;

    public static string HostRefIdOf(ExamSession session, ExamRecord record) => session.SessionCode;

    public static Dictionary<string, Dictionary<string, string>> BuildContextValues(
        ExamSession session, ExamRecord record)
    {
        var values = new Dictionary<string, Dictionary<string, string>>
        {
            [FormContextKeys.SessionProvider] = new()
            {
                [FormContextKeys.SessionCode] = session.SessionCode ?? "",
                [FormContextKeys.OrganizationName] = session.OrganizationName ?? "",
                [FormContextKeys.ExamDate] = Date(session.ExamDate),
                [FormContextKeys.PackageName] = PackageNameOf(session, record)
            },
            [FormContextKeys.SystemProvider] = new()
            {
                [FormContextKeys.CurrentDate] = Date(DateOnly.FromDateTime(DateTime.Now))
            }
        };

        var patient = new Dictionary<string, string>();
        if (!string.IsNullOrWhiteSpace(record?.Patient?.FullName))
            patient[FormContextKeys.FullName] = record.Patient.FullName;
        if (record?.Patient?.Dob is { } dob)
            patient[FormContextKeys.Dob] = Date(dob);

        if (patient.Count > 0)
            values[FormContextKeys.PatientProvider] = patient;

        return values;
    }

    private static string PackageNameOf(ExamSession session, ExamRecord record)
        => !string.IsNullOrWhiteSpace(record?.PackageName) ? record.PackageName : session.PackageName ?? "";

    private static string Date(DateOnly value)
        => value.ToString(FormContextKeys.DateFormat, CultureInfo.InvariantCulture);
}
