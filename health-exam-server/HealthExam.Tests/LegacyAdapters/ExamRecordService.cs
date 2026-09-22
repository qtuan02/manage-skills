using HealthExam.Domain.Common;
using HealthExam.Domain.ExamSessions;
using HealthExam.Domain.ExamRecords;
using HealthExam.Domain.Imports;
using HealthExam.Domain.Paraclinical;
using HealthExam.Domain.Webhooks;
using HealthExam.Domain.Integrations;
using HealthExam.Domain.Catalogs;
using HealthExam.Domain.Patients;
using HealthExam.Infrastructure.Persistence.Legacy;
using HealthExam.API.Contracts;
using HealthExam.API.Middlewares;
using IHealthExamContext = HealthExam.Application.Common.IHealthExamContext;
using ValidationErrors = HealthExam.Application.Common.ValidationErrors;
using ValidationError = HealthExam.Application.Common.ValidationError;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;

namespace HealthExam.Server.Service;

public interface IHisAdmissionService
{
    Task EnsureAdmissionAsync(Guid recordId, CancellationToken ct = default);
}

public interface IHisPatientService
{
    Task EnsurePatientAsync(Guid patientRefId, CancellationToken ct = default);
}

/// <summary>
/// Hồ sơ người khám — 02-api-spec §3.2 (phần GET/POST/PUT/confirm của phase đăng ký).
///
/// PHASE ĐĂNG KÝ: chỉ hiện thực đoạn máy trạng thái Chưa đăng ký(0) → Chờ khám(1).
/// Ba chuyển tiếp còn lại (1→2, 2→3, 3→2) là hệ quả của việc khám thật và đến từ webhook
/// form-server; webhook + HEX_WebhookInbox CHƯA làm ở phase này — xem cutCorners.
/// </summary>
public class ExamRecordService
{
    private readonly IUnitOfWork _uow;
    private readonly IHealthExamContext _ctx;
    private readonly ExamSessionService _sessions;
    private readonly IMasterDataService _masterData;
    private readonly AuditService _audit;
    private readonly ILogger<ExamRecordService> _logger;
    private readonly IHisAdmissionService _hisAdmissions;
    private readonly IPatientService _patients;

    public ExamRecordService(
        IUnitOfWork uow, IHealthExamContext ctx, ExamSessionService sessions,
        IMasterDataService masterData = null, AuditService audit = null,
        ILogger<ExamRecordService> logger = null, IHisAdmissionService hisAdmissions = null,
        IPatientService patients = null, IHisPatientService hisPatients = null)
    {
        _uow = uow;
        _ctx = ctx;
        _sessions = sessions;
        _masterData = masterData;
        _audit = audit;
        _logger = logger;
        _hisAdmissions = hisAdmissions;
        _patients = patients ?? new PatientService(uow, ctx);
    }

    public async Task<PaginationData<ExamRecordItem>> ListAsync(
        Guid? sessionId, string keyword, short? state, string variantCode,
        int page, int size, CancellationToken ct = default)
        => await ListAsync(new ExamRecordListFilter
        {
            SessionID = sessionId,
            Keyword = keyword,
            State = state,
            VariantCode = variantCode
        }, page, size, ct);

    public async Task<PaginationData<ExamRecordItem>> ListAsync(
        ExamRecordListFilter filter, int page, int size, CancellationToken ct = default)
    {
        filter ??= new ExamRecordListFilter();
        (page, size) = Paging.Normalize(page, size);

        if (filter.From.HasValue && filter.To.HasValue && filter.From.Value > filter.To.Value)
            throw HealthExamException.BadRequest(
                "Khoảng ngày tạo hồ sơ không hợp lệ",
                ValidationErrors.Of("fromDate", "Phải nhỏ hơn hoặc bằng toDate"));

        var q = _uow.ExamRecords.Query().Where(x => x.DivisionID == _ctx.DivisionId);

        if (filter.SessionID.HasValue) q = q.Where(x => x.SessionID == filter.SessionID.Value);
        if (filter.State.HasValue) q = q.Where(x => (short)x.State == filter.State.Value);
        if (!string.IsNullOrWhiteSpace(filter.VariantCode))
            q = q.Where(x => x.VariantCode == filter.VariantCode.Trim());

        ApplyContainsFilter(ref q, filter.RecordCode, x => x.RecordCode);
        ApplyContainsFilter(ref q, filter.PatientCode, x => x.Patient.PatientCode);
        ApplyContainsFilter(ref q, filter.FullName, x => x.Patient.FullName);
        ApplyContainsFilter(ref q, filter.IdentityNumber, x => x.Patient.IdentityNumber);
        ApplyContainsFilter(ref q, filter.PhoneNumber, x => x.Patient.PhoneNumber);

        if (!string.IsNullOrWhiteSpace(filter.Keyword))
        {
            var kw = filter.Keyword.Trim().ToLower();
            q = q.Where(x => x.Patient.FullName.ToLower().Contains(kw)
                          || x.Patient.PatientCode.ToLower().Contains(kw)
                          || x.RecordCode.ToLower().Contains(kw)
                          || x.Patient.IdentityNumber.Contains(kw)
                          || x.Patient.PhoneNumber.Contains(kw));
        }

        if (filter.From.HasValue)
        {
            var from = new DateTimeOffset(filter.From.Value.ToDateTime(TimeOnly.MinValue), TimeSpan.FromHours(7)).UtcDateTime;
            q = q.Where(x => x.CreatedDate >= from);
        }

        if (filter.To.HasValue)
        {
            if (filter.To.Value < DateOnly.MaxValue)
            {
                var toExclusive = new DateTimeOffset(filter.To.Value.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.FromHours(7)).UtcDateTime;
                q = q.Where(x => x.CreatedDate < toExclusive);
            }
        }

        var total = await q.CountAsync(ct);
        var rows = await q
            // Danh sách vận hành ưu tiên hồ sơ vừa được tạo, kể cả hồ sơ chưa chốt đăng ký.
            .OrderByDescending(x => x.CreatedDate)
            .ThenBy(x => x.RecordCode)
            .Skip((page - 1) * size).Take(size)
            .Include(x => x.Session)
            .Include(x => x.Patient).ThenInclude(p => p.IdentityIssuerOption)
            .Include(x => x.Patient).ThenInclude(p => p.EthnicityOption)
            .Include(x => x.Insurance).ThenInclude(i => i.InsuranceObjectOption)
            .Include(x => x.Insurance).ThenInclude(i => i.RegistrationPlaceOption)
            .Include(x => x.Employment).ThenInclude(e => e.OccupationOption)
            .Include(x => x.Relative)
            .Include(x => x.PatientTypeOption)
            .Include(x => x.PatientSubjectOption)
            .Include(x => x.PaymentSourceOption)
            .Include(x => x.ExamLocationOption)
            .ToListAsync(ct);

        var items = rows.Select(r => ToItem(r, r.Session?.SessionCode ?? "", r.Session?.ExamDate ?? default)).ToList();
        return new PaginationData<ExamRecordItem>(items, page, size, total);
    }

    private static void ApplyContainsFilter(
        ref IQueryable<ExamRecord> query, string value,
        System.Linq.Expressions.Expression<Func<ExamRecord, string>> field)
    {
        if (string.IsNullOrWhiteSpace(value)) return;

        var term = value.Trim().ToLower();
        var parameter = field.Parameters[0];
        var body = System.Linq.Expressions.Expression.Call(
            System.Linq.Expressions.Expression.Call(field.Body, nameof(string.ToLower), Type.EmptyTypes),
            nameof(string.Contains), Type.EmptyTypes,
            System.Linq.Expressions.Expression.Constant(term));
        query = query.Where(System.Linq.Expressions.Expression.Lambda<Func<ExamRecord, bool>>(body, parameter));
    }

    public async Task<ExamRecordItem> GetAsync(Guid recordId, CancellationToken ct = default)
    {
        var row = await _uow.ExamRecords.Query()
            .Where(x => x.RecordID == recordId && x.DivisionID == _ctx.DivisionId)
            .Include(x => x.Session)
            .Include(x => x.Patient).ThenInclude(p => p.IdentityIssuerOption)
            .Include(x => x.Patient).ThenInclude(p => p.EthnicityOption)
            .Include(x => x.Insurance).ThenInclude(i => i.InsuranceObjectOption)
            .Include(x => x.Insurance).ThenInclude(i => i.RegistrationPlaceOption)
            .Include(x => x.Employment).ThenInclude(e => e.OccupationOption)
            .Include(x => x.Relative)
            .Include(x => x.PatientTypeOption)
            .Include(x => x.PatientSubjectOption)
            .Include(x => x.PaymentSourceOption)
            .Include(x => x.ExamLocationOption)
            .FirstOrDefaultAsync(ct)
            ?? throw HealthExamException.NotFound("Không tìm thấy hồ sơ khám");

        return ToItem(row, row.Session?.SessionCode ?? "", row.Session?.ExamDate ?? default);
    }

    /// <summary>
    /// Bước 1 wizard: ghi thông tin hành chính, hồ sơ ra đời ở trạng thái Chưa đăng ký(0).
    ///
    /// <paramref name="importBatchId"/> khác null khi hồ sơ đến từ một lô nạp Excel (UC03.5).
    /// Đường nạp Excel đi CHUNG hàm này thay vì có đường ghi riêng: hai đường ghi là hai định
    /// nghĩa "hồ sơ hợp lệ", và cái sai sẽ là cái ít người nhìn hơn.
    /// </summary>
    public async Task<ExamRecordItem> CreateInDefaultSessionAsync(
        ExamRecordWriteRequest req, CancellationToken ct = default)
    {
        var session = await _sessions.GetOrCreatePhaseOneDefaultAsync(ct);
        var created = await CreateCoreAsync(req, session, ct, importBatchId: null,
            generatePatientCodeWhenMissing: true);

        if (_hisAdmissions == null || created.AdmissionID.GetValueOrDefault() > 0)
            return await GetAsync(created.RecordID, ct);

        try
        {
            await _hisAdmissions.EnsureAdmissionAsync(created.RecordID, ct);
        }
        catch (HealthExamException ex)
        {
            _logger?.LogWarning(ex,
                "HIS admission pending for record {RecordId} (Division: {Division}, TraceId: {TraceId}, ErrorCode: {ErrorCode})",
                created.RecordID, _ctx.DivisionId, _ctx.TraceId, ex.ErrorCode);
        }

        return await GetAsync(created.RecordID, ct);
    }

    public async Task<ExamRecordItem> CreateAsync(
        ExamRecordSaveRequest req, CancellationToken ct = default, Guid? importBatchId = null)
    {
        var errors = new ValidationErrors();
        if (!req.SessionID.HasValue)
            errors.Errors.Add(new ValidationError { Field = nameof(req.SessionID), Reason = "Bỏ trống (bắt buộc)" });
        if (errors.Errors.Count > 0) throw HealthExamException.BadRequest(payload: errors);

        var session = await _sessions.LoadAsync(req.SessionID.Value, ct);
        return await CreateCoreAsync(req, session, ct, importBatchId,
            generatePatientCodeWhenMissing: importBatchId.HasValue);
    }

    private async Task<ExamRecordItem> CreateCoreAsync(
        ExamRecordWriteRequest req, ExamSession session, CancellationToken ct,
        Guid? importBatchId, bool generatePatientCodeWhenMissing)
    {
        var errors = new ValidationErrors();
        if (string.IsNullOrWhiteSpace(req.FullName))
            errors.Errors.Add(new ValidationError { Field = nameof(req.FullName), Reason = "Bỏ trống (bắt buộc)" });
        if (string.IsNullOrWhiteSpace(req.VariantCode))
            errors.Errors.Add(new ValidationError { Field = nameof(req.VariantCode), Reason = "Bỏ trống (bắt buộc)" });
        else if (!ExamGroups.IsValid(req.VariantCode))
            errors.Errors.Add(new ValidationError { Field = nameof(req.VariantCode), Reason = "Nhóm khám không hợp lệ (DTK_01..DTK_10)" });
        if (errors.Errors.Count > 0) throw HealthExamException.BadRequest(payload: errors);

        GuardSessionWritable(session);

        await GuardDuplicateAsync(session.SessionID, req.IdentityNumber, req.PatientCode, null, ct);

        var now = DateTime.UtcNow;
        var entity = new ExamRecord
        {
            RecordID = Guid.NewGuid(),
            DivisionID = _ctx.DivisionId,
            SessionID = session.SessionID,
            ImportBatchID = importBatchId,
            State = ExamRecordState.NotRegistered,
            CreatedDate = now,
            CreatedBy = _ctx.ActorId,
            CreatedActorKind = _ctx.ActorKind,
            ModifiedDate = now,
            ModifiedBy = _ctx.ActorId,
            ModifiedActorKind = _ctx.ActorKind
        };

        // Gói khám: mặc định lấy theo đợt nếu FE không chọn riêng cho người này.
        if (!req.PackageID.HasValue && session.PackageID.HasValue)
        {
            entity.PackageID = session.PackageID;
            entity.PackageName = session.PackageName;
        }

        await using var tx = await _uow.BeginTransactionAsync(ct);
        await ApplyAsync(entity, req, ct);
        _uow.ExamRecords.Add(entity);

        // Chốt "có cấp mã NB tự sinh hay không" NGAY TẠI ĐÂY, trước vòng cấp mã: quyết định
        // này phải dựa trên mã người dùng KHAI, mà sau lượt cấp mã đầu tiên thì ô đó đã mang
        // giá trị tự sinh — đọc lại lúc đó là không bao giờ cấp lại được ở lượt thử thứ hai.
        var autoPatientCode = generatePatientCodeWhenMissing &&
                              string.IsNullOrWhiteSpace(entity.Patient?.PatientCode);

        if (string.IsNullOrWhiteSpace(req.RecordCode))
        {
            // Cấp số và chèn hồ sơ PHẢI nằm trong cùng một transaction. Tách ra thì hỏng theo
            // cả hai chiều: bộ đếm commit trước mà INSERT hỏng → đợt thủng một số vĩnh viễn;
            // INSERT commit trước mà bộ đếm chưa tăng → request kế tiếp cấp lại đúng số vừa
            // dùng. Câu UPDATE giữ khoá hàng đợt khám đến hết transaction, nên hai request
            // song song bị tuần tự hoá đúng ở chỗ cần tuần tự — thay vì cùng đọc một con số cũ.
            await InsertWithAllocatedCodeAsync(entity, session, tx, autoPatientCode, ct);
        }
        else
        {
            // Mã do client tự truyền vào: không đụng bộ đếm của đợt (nếu không, một mã nhập
            // tay sẽ ăn mất một số của dãy tự sinh). Chốt chặn trùng ở đây chỉ còn chỉ mục UNIQUE.
            entity.RecordCode = req.RecordCode.Trim();
            if (autoPatientCode) FillGeneratedPatientCode(entity);
            await SaveNewRecordAsync(entity, ct);
        }
        await tx.CommitAsync(ct);

        return await GetAsync(entity.RecordID, ct);
    }

    /// <summary>Sửa bước 1. Không nhận SessionID hoặc State — xem ExamRecordWriteRequest.</summary>
    public async Task<ExamRecordItem> UpdateAsync(Guid recordId, ExamRecordWriteRequest req, CancellationToken ct = default)
    {
        var entity = await _uow.ExamRecords.Query().AsTracking()
            .Include(x => x.Patient)
            .Include(x => x.Insurance)
            .Include(x => x.Employment)
            .Include(x => x.Relative)
            .FirstOrDefaultAsync(x => x.RecordID == recordId && x.DivisionID == _ctx.DivisionId, ct)
            ?? throw HealthExamException.NotFound("Không tìm thấy hồ sơ khám");

        if (ExamRecordStates.IsCancelled(entity.State))
            throw HealthExamException.InvalidState("Hồ sơ đã hủy, không sửa được");

        // Contract phase 1 không có SessionID: chuyển người sang đợt khác sẽ làm hỏng chỉ mục
        // chống trùng theo đợt và làm lệch mọi số liệu tiến độ đã chốt của đợt cũ.

        var session = await _sessions.LoadAsync(entity.SessionID, ct);
        GuardSessionWritable(session);

        if (!string.IsNullOrWhiteSpace(req.VariantCode) && !ExamGroups.IsValid(req.VariantCode))
            throw HealthExamException.BadRequest(
                "Nhóm khám không hợp lệ",
                ValidationErrors.Of(nameof(req.VariantCode), "Nhóm khám không hợp lệ (DTK_01..DTK_10)"));

        await GuardDuplicateAsync(entity.SessionID, req.IdentityNumber, req.PatientCode, recordId, ct);

        if (!string.IsNullOrWhiteSpace(req.RecordCode)) entity.RecordCode = req.RecordCode.Trim();

        await using var tx = await _uow.BeginTransactionAsync(ct);
        await ApplyAsync(entity, req, ct);
        entity.ModifiedDate = DateTime.UtcNow;
        entity.ModifiedBy = _ctx.ActorId;
        entity.ModifiedActorKind = _ctx.ActorKind;

        await _uow.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return await GetAsync(recordId, ct);
    }

    /// <summary>
    /// Chốt đăng ký tại quầy: Chưa đăng ký(0) → Chờ khám(1).
    ///
    /// Đây là ĐƯỜNG THỦ CÔNG được 02-api-spec §3.2 cho phép. Đường còn lại của chuyển tiếp
    /// này là webhook `submission.section.signed` khi người bệnh tự ký xác nhận đăng ký ở
    /// cổng NB (UC04.2) — chưa làm ở phase này.
    /// </summary>
    public async Task<ExamRecordItem> ConfirmAsync(Guid recordId, CancellationToken ct = default)
    {
        var entity = await _uow.ExamRecords.Query().AsTracking()
            .Include(x => x.PaymentSourceOption)
            .FirstOrDefaultAsync(x => x.RecordID == recordId && x.DivisionID == _ctx.DivisionId, ct)
            ?? throw HealthExamException.NotFound("Không tìm thấy hồ sơ khám");

        if (entity.State != ExamRecordState.NotRegistered)
            throw HealthExamException.InvalidState(
                $"Hồ sơ đang ở trạng thái \"{StateNames.Of(entity.State)}\", chỉ hồ sơ \"Chưa đăng ký\" mới chốt đăng ký được");

        var session = await _sessions.LoadAsync(entity.SessionID, ct);
        GuardSessionWritable(session);

        var errors = new ValidationErrors();
        if (string.IsNullOrWhiteSpace(entity.ExamReason))
            errors.Errors.Add(new ValidationError { Field = nameof(entity.ExamReason), Reason = "Bỏ trống (bắt buộc)" });
        if (entity.PaymentSourceOption?.Code == MasterDataCategories.OtherPaymentSource &&
            string.IsNullOrWhiteSpace(entity.PaymentSourceOther))
            errors.Errors.Add(new ValidationError { Field = nameof(entity.PaymentSourceOther), Reason = "Bỏ trống (bắt buộc)" });
        if (errors.Errors.Count > 0)
            throw HealthExamException.BadRequest("Thông tin đăng ký chưa đầy đủ", errors);

        entity.State = ExamRecordState.Waiting;
        entity.RegisteredAt = DateTime.UtcNow;
        entity.RegisteredBy = _ctx.ActorId;
        entity.ModifiedDate = DateTime.UtcNow;
        entity.ModifiedBy = _ctx.ActorId;
        entity.ModifiedActorKind = _ctx.ActorKind;

        await _uow.SaveChangesAsync(ct);
        return await GetAsync(recordId, ct);
    }

    /// <summary>
    /// Hủy đăng ký hoặc hủy khám — 02-api-spec §3.2.
    /// Chưa đăng ký(0) → Hủy đăng ký(4).
    /// Chờ khám(1) / Đang khám(2) → Hủy khám(5).
    /// Đã khám(3) → InvalidState.
    /// Đã hủy(4 hoặc 5) → trả về kết quả hiện tại mà không thẩm định lại/ghi audit (idempotent).
    /// </summary>
    public async Task<ExamRecordItem> CancelAsync(Guid recordId, ExamRecordCancelRequest req, CancellationToken ct = default)
    {
        var entity = await _uow.ExamRecords.Query().AsTracking()
            .FirstOrDefaultAsync(x => x.RecordID == recordId && x.DivisionID == _ctx.DivisionId, ct)
            ?? throw HealthExamException.NotFound("Không tìm thấy hồ sơ khám");

        if (ExamRecordStates.IsCancelled(entity.State))
            return await GetAsync(recordId, ct);

        var reason = req?.Reason?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(reason))
            throw HealthExamException.BadRequest(
                "Lý do hủy không được để trống",
                ValidationErrors.Of(nameof(ExamRecordCancelRequest.Reason), "Bỏ trống (bắt buộc)"));

        var session = await _sessions.LoadAsync(entity.SessionID, ct);
        GuardSessionWritable(session);

        var fromState = entity.State;
        ExamRecordState toState;

        if (fromState == ExamRecordState.NotRegistered)
        {
            toState = ExamRecordState.RegistrationCancelled;
        }
        else if (fromState is ExamRecordState.Waiting or ExamRecordState.InProgress)
        {
            toState = ExamRecordState.ExamCancelled;
        }
        else
        {
            throw HealthExamException.InvalidState(
                $"Hồ sơ đang ở trạng thái \"{StateNames.Of(entity.State)}\", không thể hủy");
        }

        entity.State = toState;
        entity.CancelledAt = DateTime.UtcNow;
        entity.CancelledBy = _ctx.ActorId;
        entity.CancelReason = reason;
        entity.ModifiedDate = DateTime.UtcNow;
        entity.ModifiedBy = _ctx.ActorId;
        entity.ModifiedActorKind = _ctx.ActorKind;

        _audit?.StateChange(AuditEntityTypes.Record, entity.RecordID, (short)fromState, (short)toState,
            new { Reason = reason, entity.RecordCode });

        await _uow.SaveChangesAsync(ct);
        return await GetAsync(recordId, ct);
    }

    /// <summary>Đợt đã đóng/hủy thì khoá mọi đường ghi thuộc đợt — 4091 chứ không phải 4090 chung chung.</summary>
    internal static void GuardSessionWritable(ExamSession session)
    {
        if (session.State == ExamSessionState.Closed || session.State == ExamSessionState.Cancelled)
            throw HealthExamException.SessionClosed(
                $"Đợt khám {session.SessionCode} đã đóng, cần mở lại đợt trước khi ghi hồ sơ");
    }

    private async Task GuardDuplicateAsync(Guid sessionId, string identityNumber, string patientCode,
        Guid? exceptRecordId, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(identityNumber))
        {
            var id = identityNumber.Trim();
            if (await _uow.ExamRecords.AnyAsync(
                    x => x.SessionID == sessionId && x.Patient.IdentityNumber == id
                      && x.State != ExamRecordState.RegistrationCancelled
                      && x.State != ExamRecordState.ExamCancelled
                      && (exceptRecordId == null || x.RecordID != exceptRecordId), ct))
                throw HealthExamException.DuplicateInSession(
                    $"CCCD {id} đã có hồ sơ trong đợt khám này",
                    ValidationErrors.Of("IdentityNumber", "Đã có hồ sơ trong đợt"));
        }

        if (!string.IsNullOrWhiteSpace(patientCode))
        {
            var pc = patientCode.Trim();
            if (await _uow.ExamRecords.AnyAsync(
                    x => x.SessionID == sessionId && x.Patient.PatientCode == pc
                      && x.State != ExamRecordState.RegistrationCancelled
                      && x.State != ExamRecordState.ExamCancelled
                      && (exceptRecordId == null || x.RecordID != exceptRecordId), ct))
                throw HealthExamException.DuplicateInSession(
                    $"Mã người bệnh {pc} đã có hồ sơ trong đợt khám này",
                    ValidationErrors.Of("PatientCode", "Đã có hồ sơ trong đợt"));
        }
    }

    /// <summary>
    /// Chèn hồ sơ mới. Chốt chặn trùng THẬT nằm ở chỉ mục UNIQUE của DB (xem
    /// HealthExamDbContext), không ở GuardDuplicateAsync: hai request song song đều lọt qua
    /// bước kiểm trước rồi mới va nhau lúc ghi. Bộ đếm LastRecordNo KHÔNG thay thế lưới này,
    /// vì mã hồ sơ còn có thể do client tự truyền vào và trùng với mã đã cấp.
    /// </summary>
    private async Task SaveNewRecordAsync(ExamRecord entity, CancellationToken ct)
    {
        var violated = await TryInsertAsync(entity, ct);
        if (violated != null)
            throw HealthExamException.DuplicateInSession(
                "Người bệnh đã có hồ sơ trong đợt khám này hoặc mã hồ sơ bị trùng");
    }

    /// <summary>
    /// Chỉ mục UNIQUE của mã hồ sơ. Phân biệt được "trùng MÃ" với "trùng NGƯỜI" là điều kiện
    /// để <see cref="InsertWithAllocatedCodeAsync"/> biết khi nào cấp lại số thì cứu được, và
    /// khi nào phải trả 4093 cho người dùng.
    /// </summary>
    private const string RecordCodeIndex = "IX_HEX_ExamRecord_DivisionID_RecordCode";

    /// <summary>
    /// Số lần cấp lại mã trước khi chịu thua. Mỗi lần đụng là một mã đã bị ai đó chiếm bằng
    /// đường client tự truyền; chiếm liên tiếp 5 số là chuyện của người, không phải của cuộc
    /// đua, nên dừng lại và báo còn hơn quay vòng.
    /// </summary>
    private const int MaxCodeAttempts = 5;

    /// <summary>
    /// Chèn hồ sơ với mã do hệ thống cấp — cấp lại số khi mã vừa cấp đã bị chiếm.
    ///
    /// Vì sao phải cấp lại chứ không chỉ báo lỗi: mã do client tự truyền (đăng ký tay,
    /// ExamRecordSaveRequest.RecordCode) CỐ Ý không tăng bộ đếm của đợt, nên một hồ sơ nhập
    /// tay mang mã "DK-2026-001-0002" sẽ nằm chình ình đúng chỗ dãy tự sinh sắp đi qua. Cú va
    /// đó làm INSERT hỏng, transaction rollback, và bộ đếm quay về giá trị cũ — nên lần sau
    /// cấp lại ĐÚNG con số vừa đụng. Kết quả đo được trước khi vá: một mã nhập tay làm hỏng
    /// TOÀN BỘ các dòng phía sau trong lô Excel (4 dòng lành bị đánh DUPLICATE).
    ///
    /// Vì sao dùng SAVEPOINT: PostgreSQL huỷ hiệu lực cả transaction sau một lỗi, nên không
    /// thể "thử lại lệnh kế tiếp" nếu không có điểm lùi. Lùi về savepoint gỡ đúng lệnh INSERT
    /// hỏng và GIỮ LẠI phần tăng bộ đếm — nhờ vậy lượt sau cấp số mới thật, không phải số cũ.
    /// </summary>
    private async Task InsertWithAllocatedCodeAsync(
        ExamRecord entity, ExamSession session, IDbContextTransaction tx,
        bool generatePatientCode, CancellationToken ct)
    {
        const string savepoint = "hex_record_insert";
        var skipped = new List<string>();

        for (var attempt = 1; ; attempt++)
        {
            entity.RecordCode = ComposeRecordCode(session.SessionCode, await AllocateRecordNoAsync(session, ct));

            // Mã NB tự sinh phải đi theo mã hồ sơ CUỐI CÙNG, nên đặt trong vòng lặp: cấp lại
            // số vì mã bị chiếm mà mã NB vẫn giữ giá trị của lượt trước là hai mã trỏ về hai
            // hồ sơ khác nhau — đúng kiểu lệch không ai nhìn ra cho tới lúc tra cứu sai người.
            if (generatePatientCode) FillGeneratedPatientCode(entity);

            if (tx.SupportsSavepoints)
                await tx.CreateSavepointAsync(savepoint, ct);
            var violated = await TryInsertAsync(entity, ct);
            if (violated == null)
            {
                if (skipped.Count > 0)
                    _logger?.LogWarning(
                        "Cấp mã hồ sơ bỏ qua {Count} mã đã bị chiếm ({Codes}) trước khi dùng được {RecordCode}. "
                        + "Mã bị chiếm là mã do client tự truyền — dãy tự sinh của đợt vì thế có lỗ.",
                        skipped.Count, string.Join(", ", skipped), entity.RecordCode);
                return;
            }

            // Trùng NGƯỜI (CCCD/mã NB) thì cấp lại mã bao nhiêu lần cũng vô ích — và cũng
            // không được phép ghi, vì đó đúng là điều lưới UNIQUE đang chặn.
            if (!violated.Contains(RecordCodeIndex, StringComparison.OrdinalIgnoreCase)
                || attempt >= MaxCodeAttempts)
            {
                if (tx.SupportsSavepoints)
                    await tx.RollbackToSavepointAsync(savepoint, ct);
                throw HealthExamException.DuplicateInSession(
                    violated.Contains(RecordCodeIndex, StringComparison.OrdinalIgnoreCase)
                        ? $"Mã hồ sơ bị chiếm {attempt} lần liên tiếp ({string.Join(", ", skipped)}, {entity.RecordCode}) — "
                          + "kiểm tra lại những hồ sơ được nhập tay kèm mã trong đợt này"
                        : "Người bệnh đã có hồ sơ trong đợt khám này hoặc mã hồ sơ bị trùng");
            }

            skipped.Add(entity.RecordCode);
            if (tx.SupportsSavepoints)
                await tx.RollbackToSavepointAsync(savepoint, ct);
            _uow.ExamRecords.Add(entity);
        }
    }

    /// <summary>
    /// Chèn một lần. Trả về TÊN RÀNG BUỘC bị vi phạm nếu đụng lưới UNIQUE, null nếu ghi được.
    /// Mọi DbUpdateException khác ném tiếp — chúng không phải chuyện của tầng này.
    /// </summary>
    private async Task<string> TryInsertAsync(ExamRecord entity, CancellationToken ct)
    {
        try
        {
            await _uow.SaveChangesAsync(ct);
            return null;
        }
        catch (DbUpdateException ex)
        {
            // ★ GỠ entity khỏi ChangeTracker trước khi ném. Không gỡ thì EF vẫn giữ nó ở
            // trạng thái Added, và LẦN SaveChanges KẾ TIẾP của cùng DbContext — kể cả một
            // SaveChanges hoàn toàn không liên quan, như lúc nạp Excel ghi sổ sách cuối lô —
            // sẽ chèn lại đúng hàng vừa hỏng. Triệu chứng là 500 ở một chỗ không có gì sai,
            // và mọi thay đổi đi cùng SaveChanges đó mất trắng (F3, review MR !4).
            //
            // Gỡ cho MỌI DbUpdateException chứ không chỉ unique-violation: lỗi độ dài (22001)
            // hay ràng buộc khác cũng để lại đúng cái xác đó.
            _uow.Context.Entry(entity).State = EntityState.Detached;

            if (ex.InnerException is Npgsql.PostgresException { SqlState: "23505" } pg)
                return pg.ConstraintName ?? "";
            throw;
        }
    }

    /// <summary>
    /// Cấp một số thứ tự mới cho đợt: UPDATE ... RETURNING trên đúng hàng HEX_ExamSession.
    ///
    /// Vì sao đọc-ghi trong MỘT câu lệnh: PostgreSQL khoá hàng khi UPDATE và giữ đến hết
    /// transaction, nên request thứ hai phải đợi request thứ nhất commit rồi mới đọc được
    /// giá trị MỚI. Tách thành SELECT rồi UPDATE (hay COUNT + 1 như trước) thì cả hai cùng
    /// đọc một con số cũ và cùng sinh ra một mã.
    ///
    /// Vì sao đi thẳng ADO.NET thay vì Database.SqlQueryRaw: EF gói SQL thô vào một subquery
    /// (SELECT ... FROM (sql) AS t), mà PostgreSQL không cho UPDATE nằm trong FROM lẫn cho CTE
    /// sửa dữ liệu ở tầng dưới. Đi thẳng DbCommand thì câu lệnh gửi xuống đúng như viết ra.
    /// Vẫn gán CurrentTransaction để lệnh này nằm CÙNG transaction với INSERT hồ sơ — thiếu
    /// dòng đó, Npgsql coi đây là lệnh tự trị và bộ đếm tăng cả khi hồ sơ rollback.
    /// </summary>
    private async Task<int> AllocateRecordNoAsync(ExamSession session, CancellationToken ct)
    {
        if (!_uow.Context.Database.IsRelational())
        {
            session.LastRecordNo++;
            _uow.ExamSessions.Update(session);
            await _uow.SaveChangesAsync(ct);
            return session.LastRecordNo;
        }

        const string sql = """
            UPDATE "HEX_ExamSession"
               SET "LastRecordNo" = "LastRecordNo" + 1
             WHERE "SessionID" = @sid AND "DivisionID" = @div
            RETURNING "LastRecordNo"
            """;

        var db = _uow.Context.Database;
        await db.OpenConnectionAsync(ct);
        try
        {
            await using var cmd = db.GetDbConnection().CreateCommand();
            cmd.CommandText = sql;
            cmd.Transaction = db.CurrentTransaction?.GetDbTransaction();
            cmd.Parameters.Add(Param(cmd, "sid", session.SessionID));
            cmd.Parameters.Add(Param(cmd, "div", _ctx.DivisionId));

            var allocated = await cmd.ExecuteScalarAsync(ct);
            if (allocated is null or DBNull)
                // Không có hàng nào bị UPDATE: đợt vừa bị xoá, hoặc thuộc DivisionID khác.
                // Trả 4040 chứ không âm thầm cấp số 1 cho một đợt không thuộc đơn vị đang gọi.
                throw HealthExamException.NotFound("Không tìm thấy đợt khám để cấp mã hồ sơ");

            return Convert.ToInt32(allocated);
        }
        finally
        {
            await db.CloseConnectionAsync();
        }
    }

    private static DbParameter Param(DbCommand cmd, string name, object value)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        p.Value = value;
        return p;
    }

    /// <summary>
    /// Ghép mã hồ sơ từ mã đợt và số thứ tự đã cấp: {SessionCode}-0001.
    ///
    /// Tách ra thành hàm thuần (không đụng DB) để kiểm thử được phần quy ước đặt mã mà không
    /// cần dựng PostgreSQL. Chèn 0 tới 4 chữ số, nhưng KHÔNG cắt bớt khi vượt 9999: đợt trên
    /// một vạn người thì mã dài ra 5 chữ số — chấp nhận dài, không chấp nhận trùng.
    /// </summary>
    public static string ComposeRecordCode(string sessionCode, int recordNo)
        => $"{sessionCode}-{recordNo:D4}";

    /// <summary>
    /// Tiền tố của mã người bệnh do HỆ THỐNG tự sinh cho hồ sơ nạp bằng Excel và hồ sơ
    /// đăng ký thủ công ở phase một.
    ///
    /// Có tiền tố vì hai lý do cụ thể, không phải cho đẹp:
    ///   • không đụng mã nhập tay ở nhánh bắt trùng của lô Excel;
    ///   • sau này HIS đồng bộ danh mục người bệnh thật thì grep ra được ngay cái nào là mã
    ///     bịa — một mã "không phải mã NB thật" nằm trong cột PatientCode mà không phân biệt
    ///     được là thứ sẽ không ai gỡ ra nổi.
    /// </summary>
    public const string GeneratedPatientCodePrefix = "HEX-";

    /// <summary>
    /// Mã người bệnh tự sinh cho hồ sơ nạp bằng Excel hoặc đăng ký thủ công phase một — trả
    /// null nếu KHÔNG sinh được.
    ///
    /// Vì sao cần (F4, chốt của 236): đơn vị ký hợp đồng gửi danh sách NHÂN VIÊN, họ không có
    /// mã người bệnh của bệnh viện, nên mẫu Excel cố ý không bắt buộc cột đó. Nhưng cổng người
    /// bệnh so `PatientCode` bằng phép so tuyệt đối, nên hồ sơ để trống ô ấy thì KHÔNG bao giờ
    /// đăng nhập được. Cấp mã tự sinh giữ nguyên được cả hai hợp đồng.
    ///
    /// Suy từ <c>RecordCode</c> vì đó là mã DUY NHẤT trong tenant và đã có sẵn ngay lúc chèn.
    /// Trong cùng một đợt, mọi RecordCode chỉ khác nhau ở số thứ tự, nên lưới
    /// UX_HEX_Record_Session_Patient không bao giờ bị chính dãy này làm đụng.
    ///
    /// ⚠️ Về bảo mật: đây là mã TUẦN TỰ, đoán được — nó là TÊN ĐĂNG NHẬP chứ không phải bí
    /// mật. Chấp nhận được vì cổng NB còn ép thêm một dữ kiện định danh (CCCD hoặc BHYT) phải
    /// khớp, và phép so đó từ chối mọi giá trị lưu rỗng.
    ///
    /// ❗ KHÔNG cắt bớt khi vượt trần: cắt là mở đường cho hai người khác nhau nhận cùng một
    /// mã, tức người bệnh A đăng nhập ra hồ sơ người bệnh B. Thà không sinh và kêu to.
    /// Chỉ xảy ra khi mã đợt dài quá (Q-HEX-11 chưa chốt quy tắc sinh mã đợt).
    /// </summary>
    public static string ComposeGeneratedPatientCode(string recordCode)
    {
        var code = GeneratedPatientCodePrefix + (recordCode ?? "").Trim();
        return code.Length > RecordFieldLengths.PatientCode ? null : code;
    }

    /// <summary>
    /// Cấp mã người bệnh tự sinh cho hồ sơ chưa khai mã (lô Excel hoặc đăng ký thủ công phase
    /// một).
    /// Gọi SAU khi RecordCode đã có giá trị cuối cùng — mã cấp lại thì mã NB đi theo.
    /// </summary>
    private void FillGeneratedPatientCode(ExamRecord entity)
    {
        var generated = ComposeGeneratedPatientCode(entity.RecordCode);
        if (generated == null)
        {
            // Xoá phần đã gán ở lượt cấp mã trước (nếu có) — để lại mã cũ là để lại một mã
            // trỏ về hồ sơ khác.
            if (entity.Patient != null && entity.Patient.PatientCode.StartsWith(GeneratedPatientCodePrefix))
                entity.Patient.PatientCode = "";

            _logger?.LogError(
                "Hồ sơ {RecordCode} nạp từ Excel KHÔNG được cấp mã người bệnh tự sinh: "
                + "\"{Prefix}\" + mã hồ sơ dài {Length} ký tự, vượt trần {Max} của cột PatientCode. "
                + "Người này chỉ khám được tại quầy, không đăng nhập được cổng người bệnh — "
                + "mã đợt khám đang quá dài (Q-HEX-11).",
                entity.RecordCode, GeneratedPatientCodePrefix,
                GeneratedPatientCodePrefix.Length + (entity.RecordCode ?? "").Length,
                RecordFieldLengths.PatientCode);
            return;
        }

        if (entity.Patient != null && (string.IsNullOrWhiteSpace(entity.Patient.PatientCode) || entity.Patient.PatientCode.StartsWith(GeneratedPatientCodePrefix)))
        {
            entity.Patient.PatientCode = generated;
        }
    }

    private async Task ApplyAsync(ExamRecord entity, ExamRecordWriteRequest req, CancellationToken ct)
    {
        if (req.AdmissionID.HasValue)
        {
            if (req.AdmissionID.Value <= 0)
                throw HealthExamException.BadRequest("Mã lượt tiếp nhận không hợp lệ",
                    ValidationErrors.Of(nameof(req.AdmissionID), "Phải lớn hơn 0"));
            entity.AdmissionID = req.AdmissionID.Value;
        }
        if (req.Note != null) entity.Note = req.Note;

        if (!string.IsNullOrWhiteSpace(req.VariantCode))
        {
            var variantChanged = entity.VariantCode != req.VariantCode;
            entity.VariantCode = req.VariantCode;

            // ĐỔI Nhóm khám thì phải BỎ biểu mẫu đã chốt của nhóm cũ.
            //
            // Không bỏ thì hỏng lặng lẽ: FormCode bị ghi đè theo quy ước bên dưới, nhưng FormID
            // vẫn là của nhóm khám CŨ — mà GET /form-draft ưu tiên FormID đã lưu (dùng lại,
            // không resolve lại). Người dùng đổi từ "Người lái xe" sang "Người cao tuổi" sẽ
            // nhận lại đúng biểu mẫu lái xe, không có lỗi nào báo ra.
            if (variantChanged)
            {
                entity.FormID = null;
                entity.SubmissionID = null;
            }

            // FormCode suy ra từ Nhóm khám theo quy ước KSK-V1-DTK_xx (03-ksk-mapping §2.2).
            // Đây chỉ là PHỎNG ĐOÁN để FE có gì đó hiển thị trước bước 2; mã thật do
            // form-server trả về và được ExamFormDraftService ghi đè khi resolve.
            entity.FormCode = ExamGroups.FormCodePrefix + req.VariantCode;
        }

        if (req.PackageID.HasValue)
        {
            var pkg = await _uow.ExamPackages.GetAsync(
                          x => x.PackageID == req.PackageID.Value && x.DivisionID == _ctx.DivisionId, ct)
                      ?? throw HealthExamException.BadRequest(
                          "Không tìm thấy gói khám",
                          ValidationErrors.Of(nameof(req.PackageID), "Không tồn tại"));
            entity.PackageID = pkg.PackageID;
            entity.PackageName = pkg.PackageName;
        }

        var existingValidFrom = entity.Insurance?.ValidFrom;
        var existingValidTo = entity.Insurance?.ValidTo;
        if (req.InsuranceValidFrom.HasValue || req.InsuranceValidTo.HasValue)
        {
            var from = req.InsuranceValidFrom ?? existingValidFrom;
            var to = req.InsuranceValidTo ?? existingValidTo;
            if (from.HasValue && to.HasValue && to.Value < from.Value)
                throw HealthExamException.BadRequest("Hạn thẻ bảo hiểm y tế không hợp lệ",
                    ValidationErrors.Of(nameof(req.InsuranceValidTo), "Ngày hết hạn phải lớn hơn hoặc bằng ngày bắt đầu"));
        }

        ResolvedMasterDataOption ethnicityOpt = null;
        ResolvedMasterDataOption occupationOpt = null;
        ResolvedMasterDataOption identityIssuerOpt = null;
        ResolvedMasterDataOption insuranceObjectOpt = null;
        ResolvedMasterDataOption registrationPlaceOpt = null;
        ResolvedMasterDataOption patientTypeOpt = null;
        ResolvedMasterDataOption patientSubjectOpt = null;
        ResolvedMasterDataOption paymentSourceOpt = null;
        ResolvedMasterDataOption examLocationOpt = null;

        if (_masterData != null)
        {
            if (req.EthnicityCode != null)
            {
                ethnicityOpt = await _masterData.ResolveOptionAsync(MasterDataCategories.Ethnicity, req.EthnicityCode, nameof(req.EthnicityCode), ct);
            }
            if (req.OccupationCode != null)
            {
                occupationOpt = await _masterData.ResolveOptionAsync(MasterDataCategories.Occupation, req.OccupationCode, nameof(req.OccupationCode), ct);
            }
            if (req.ProvinceCode != null)
            {
                var province = await _masterData.ResolveAsync(MasterDataCategories.Province, req.ProvinceCode, nameof(req.ProvinceCode), ct);
                entity.ProvinceCode = province?.Code ?? "";
                entity.ProvinceName = province?.Name ?? "";
            }
            if (req.WardCode != null)
            {
                var provCode = req.ProvinceCode ?? entity.ProvinceCode;
                var ward = await _masterData.ResolveWardAsync(provCode, req.WardCode, nameof(req.WardCode), ct);
                entity.WardCode = ward?.Code ?? "";
                entity.WardName = ward?.Name ?? "";
            }
            if (req.IdentityIssuerCode != null)
            {
                identityIssuerOpt = await _masterData.ResolveOptionAsync(MasterDataCategories.IdentityIssuer, req.IdentityIssuerCode, nameof(req.IdentityIssuerCode), ct);
            }

            if (req.InsuranceObjectCode != null)
            {
                insuranceObjectOpt = await _masterData.ResolveOptionAsync(MasterDataCategories.InsuranceObject, req.InsuranceObjectCode, nameof(req.InsuranceObjectCode), ct);
            }

            if (req.RegistrationPlaceCode != null)
            {
                registrationPlaceOpt = await _masterData.ResolveOptionAsync(
                    MasterDataCategories.RegistrationPlace,
                    req.RegistrationPlaceCode,
                    nameof(req.RegistrationPlaceCode), ct);
            }

            if (req.BloodAboCode != null)
            {
                await _masterData.ResolveOptionAsync(MasterDataCategories.BloodAbo, req.BloodAboCode, nameof(req.BloodAboCode), ct);
            }

            if (req.BloodRhCode != null)
            {
                await _masterData.ResolveOptionAsync(MasterDataCategories.BloodRh, req.BloodRhCode, nameof(req.BloodRhCode), ct);
            }

            if (req.RelativeRelationshipCode != null)
            {
                await _masterData.ResolveOptionAsync(MasterDataCategories.Relationship, req.RelativeRelationshipCode, nameof(req.RelativeRelationshipCode), ct);
            }

            if (req.ExamReason != null) entity.ExamReason = req.ExamReason.Trim();

            if (req.PatientTypeCode != null)
            {
                patientTypeOpt = await _masterData.ResolveOptionAsync(MasterDataCategories.PatientType, req.PatientTypeCode, nameof(req.PatientTypeCode), ct);
                entity.PatientTypeOptionID = patientTypeOpt?.OptionID;
            }
            if (req.PatientSubjectCode != null)
            {
                patientSubjectOpt = await _masterData.ResolveOptionAsync(MasterDataCategories.PatientSubject, req.PatientSubjectCode, nameof(req.PatientSubjectCode), ct);
                entity.PatientSubjectOptionID = patientSubjectOpt?.OptionID;
            }
            if (req.PaymentSourceCode != null)
            {
                paymentSourceOpt = await _masterData.ResolveOptionAsync(MasterDataCategories.PaymentSource, req.PaymentSourceCode, nameof(req.PaymentSourceCode), ct);
                entity.PaymentSourceOptionID = paymentSourceOpt?.OptionID;
            }
            if (req.PaymentSourceOther != null)
            {
                entity.PaymentSourceOther = req.PaymentSourceOther.Trim();
            }
            if (paymentSourceOpt != null && paymentSourceOpt.Code != MasterDataCategories.OtherPaymentSource)
            {
                entity.PaymentSourceOther = "";
            }
            if (req.ExamLocationCode != null)
            {
                examLocationOpt = await _masterData.ResolveOptionAsync(MasterDataCategories.ExamLocation, req.ExamLocationCode, nameof(req.ExamLocationCode), ct);
                entity.ExamLocationOptionID = examLocationOpt?.OptionID;
            }
        }

        if (_patients != null)
        {
            Guid? ethOptionId = ethnicityOpt != null ? ethnicityOpt.OptionID : entity.Patient?.EthnicityOptionID;
            if (req.EthnicityCode != null && string.IsNullOrWhiteSpace(req.EthnicityCode)) ethOptionId = null;

            Guid? issuerOptionId = identityIssuerOpt != null ? identityIssuerOpt.OptionID : entity.Patient?.IdentityIssuerOptionID;
            if (req.IdentityIssuerCode != null && string.IsNullOrWhiteSpace(req.IdentityIssuerCode)) issuerOptionId = null;

            Guid? insObjOptionId = insuranceObjectOpt != null ? insuranceObjectOpt.OptionID : entity.Insurance?.InsuranceObjectOptionID;
            if (req.InsuranceObjectCode != null && string.IsNullOrWhiteSpace(req.InsuranceObjectCode)) insObjOptionId = null;

            Guid? registrationPlaceOptionId = registrationPlaceOpt != null
                ? registrationPlaceOpt.OptionID
                : entity.Insurance?.RegistrationPlaceOptionID;
            if (req.RegistrationPlaceCode != null && string.IsNullOrWhiteSpace(req.RegistrationPlaceCode)) registrationPlaceOptionId = null;

            Guid? occOptionId = occupationOpt != null ? occupationOpt.OptionID : entity.Employment?.OccupationOptionID;
            if (req.OccupationCode != null && string.IsNullOrWhiteSpace(req.OccupationCode)) occOptionId = null;

            var insuranceData = new PatientInsuranceData
            {
                InsuranceNumber = req.InsuranceNumber != null ? req.InsuranceNumber.Trim() : (entity.Insurance?.InsuranceNumber ?? ""),
                InsuranceObjectOptionID = insObjOptionId,
                RegistrationPlaceOptionID = registrationPlaceOptionId,
                ValidFrom = req.InsuranceValidFrom ?? entity.Insurance?.ValidFrom,
                ValidTo = req.InsuranceValidTo ?? entity.Insurance?.ValidTo
            };

            var employmentData = new PatientEmploymentData
            {
                OccupationOptionID = occOptionId,
                StaffCode = req.StaffCode != null ? req.StaffCode.Trim() : (entity.Employment?.StaffCode ?? ""),
                OrgDeptName = req.OrgDeptName != null ? req.OrgDeptName.Trim() : (entity.Employment?.OrgDeptName ?? ""),
                JobTitle = req.JobTitle != null ? req.JobTitle.Trim() : (entity.Employment?.JobTitle ?? "")
            };

            var relativeData = new PatientRelativeData
            {
                RelationshipCode = req.RelativeRelationshipCode != null ? req.RelativeRelationshipCode.Trim() : (entity.Relative?.RelationshipCode ?? ""),
                RelationshipOptionID = null,
                FullName = req.RelativeFullName != null ? req.RelativeFullName.Trim() : (entity.Relative?.FullName ?? ""),
                IdentityNumber = req.RelativeIdentityNumber != null ? req.RelativeIdentityNumber.Trim() : (entity.Relative?.IdentityNumber ?? ""),
                PhoneNumber = req.RelativePhoneNumber != null ? req.RelativePhoneNumber.Trim() : (entity.Relative?.PhoneNumber ?? "")
            };

            var patientData = new PatientWriteData
            {
                PatientRefID = entity.PatientRefID,
                HisPatientID = (req.PatientID.HasValue && req.PatientID.Value > 0) ? req.PatientID.Value : entity.Patient?.HisPatientID,
                PatientCode = req.PatientCode != null ? req.PatientCode.Trim() : (entity.Patient?.PatientCode ?? ""),
                FullName = req.FullName != null ? req.FullName.Trim() : (entity.Patient?.FullName ?? ""),
                Dob = req.Dob ?? entity.Patient?.Dob,
                BirthYear = req.BirthYear ?? entity.Patient?.BirthYear,
                GenderID = req.GenderID ?? entity.Patient?.GenderID ?? 0,
                IdentityNumber = req.IdentityNumber != null ? req.IdentityNumber.Trim() : (entity.Patient?.IdentityNumber ?? ""),
                IdentityIssuedDate = req.IdentityIssuedDate ?? entity.Patient?.IdentityIssuedDate,
                IdentityIssuerOptionID = issuerOptionId,
                PhoneNumber = req.PhoneNumber != null ? req.PhoneNumber.Trim() : (entity.Patient?.PhoneNumber ?? ""),
                Email = req.Email != null ? req.Email.Trim() : (entity.Patient?.Email ?? ""),
                Address = req.Address != null ? req.Address.Trim() : (entity.Patient?.Address ?? ""),
                EthnicityOptionID = ethOptionId,
                BloodAboCode = req.BloodAboCode != null ? req.BloodAboCode.Trim() : (entity.Patient?.BloodAboCode ?? ""),
                BloodRhCode = req.BloodRhCode != null ? req.BloodRhCode.Trim() : (entity.Patient?.BloodRhCode ?? ""),
                Insurance = insuranceData,
                Employment = employmentData,
                Relative = relativeData
            };

            var writeResult = await _patients.UpsertLocalAsync(patientData, ct);
            entity.Patient = writeResult.Patient;
            entity.PatientRefID = writeResult.Patient?.PatientRefID;
            entity.Insurance = writeResult.Insurance;
            entity.InsuranceRefID = writeResult.Insurance?.InsuranceRefID;
            entity.Employment = writeResult.Employment;
            entity.EmploymentRefID = writeResult.Employment?.EmploymentRefID;
            entity.Relative = writeResult.Relative;
            entity.RelativeRefID = writeResult.Relative?.RelativeRefID;
        }
    }

    private static ExamRecordItem ToItem(ExamRecord x, string sessionCode, DateOnly examDate) => new()
    {
        RecordID = x.RecordID,
        SessionID = x.SessionID,
        SessionCode = sessionCode ?? "",
        ExamDate = examDate,
        RecordCode = x.RecordCode,
        PatientID = x.Patient?.HisPatientID ?? 0,
        AdmissionID = x.AdmissionID,
        HisAdmissionLinkStatus = x.AdmissionID.GetValueOrDefault() > 0 ? "Linked" : "Pending",
        PatientCode = x.Patient?.PatientCode ?? "",
        FullName = x.Patient?.FullName ?? "",
        Dob = x.Patient?.Dob,
        BirthYear = x.Patient?.BirthYear,
        GenderID = x.Patient?.GenderID ?? 0,
        IdentityNumber = x.Patient?.IdentityNumber ?? "",
        InsuranceNumber = x.Insurance?.InsuranceNumber ?? "",
        PhoneNumber = x.Patient?.PhoneNumber ?? "",
        Email = x.Patient?.Email ?? "",
        Address = x.Patient?.Address ?? "",
        StaffCode = x.Employment?.StaffCode ?? "",
        OrgDeptName = x.Employment?.OrgDeptName ?? "",
        JobTitle = x.Employment?.JobTitle ?? "",
        VariantCode = x.VariantCode,
        VariantName = ExamGroups.All.FirstOrDefault(g => g.VariantCode == x.VariantCode)?.GroupName ?? "",
        PackageID = x.PackageID,
        PackageName = x.PackageName,
        FormID = x.FormID,
        FormCode = x.FormCode,
        SubmissionID = x.SubmissionID,
        State = x.State,
        StateName = StateNames.Of(x.State),
        RegisteredAt = x.RegisteredAt,
        ExamStartedAt = x.ExamStartedAt,
        ExamFinishedAt = x.ExamFinishedAt,
        CancelledAt = x.CancelledAt,
        CancelReason = x.CancelReason,
        ProgressDone = x.ProgressDone,
        ProgressTotal = x.ProgressTotal,
        HealthClassCode = x.HealthClassCode,
        Note = x.Note,
        EthnicityCode = x.Patient?.EthnicityOption != null ? x.Patient.EthnicityOption.Code : "",
        EthnicityName = x.Patient?.EthnicityOption != null ? x.Patient.EthnicityOption.Name : "",
        OccupationCode = x.Employment?.OccupationOption != null ? x.Employment.OccupationOption.Code : "",
        OccupationName = x.Employment?.OccupationOption != null ? x.Employment.OccupationOption.Name : "",
        BloodAboCode = x.Patient?.BloodAboCode ?? "",
        BloodAboName = MasterDataService.StaticBloodAbos.FirstOrDefault(o => string.Equals(o.Code, x.Patient?.BloodAboCode, StringComparison.OrdinalIgnoreCase))?.Name ?? (x.Patient?.BloodAboCode ?? ""),
        BloodRhCode = x.Patient?.BloodRhCode ?? "",
        BloodRhName = MasterDataService.StaticBloodRhs.FirstOrDefault(o => string.Equals(o.Code, x.Patient?.BloodRhCode, StringComparison.OrdinalIgnoreCase))?.Name ?? (x.Patient?.BloodRhCode ?? ""),
        ProvinceCode = x.ProvinceCode,
        ProvinceName = x.ProvinceName,
        WardCode = x.WardCode,
        WardName = x.WardName,
        IdentityIssuedDate = x.Patient?.IdentityIssuedDate,
        IdentityIssuerCode = x.Patient?.IdentityIssuerOption != null ? x.Patient.IdentityIssuerOption.Code : "",
        IdentityIssuerName = x.Patient?.IdentityIssuerOption != null ? x.Patient.IdentityIssuerOption.Name : "",
        RelativeRelationshipCode = x.Relative?.RelationshipCode ?? "",
        RelativeRelationshipName = MasterDataService.StaticRelationships.FirstOrDefault(o => string.Equals(o.Code, x.Relative?.RelationshipCode, StringComparison.OrdinalIgnoreCase))?.Name ?? (x.Relative?.RelationshipCode ?? ""),
        RelativeFullName = x.Relative?.FullName ?? "",
        RelativeIdentityNumber = x.Relative?.IdentityNumber ?? "",
        RelativePhoneNumber = x.Relative?.PhoneNumber ?? "",
        InsuranceObjectCode = x.Insurance?.InsuranceObjectOption != null ? x.Insurance.InsuranceObjectOption.Code : "",
        InsuranceObjectName = x.Insurance?.InsuranceObjectOption != null ? x.Insurance.InsuranceObjectOption.Name : "",
        RegistrationPlaceCode = x.Insurance?.RegistrationPlaceOption != null ? x.Insurance.RegistrationPlaceOption.Code : "",
        RegistrationPlaceName = x.Insurance?.RegistrationPlaceOption != null ? x.Insurance.RegistrationPlaceOption.Name : "",
        InsuranceValidFrom = x.Insurance?.ValidFrom,
        InsuranceValidTo = x.Insurance?.ValidTo,
        ExamReason = x.ExamReason,
        PatientTypeCode = x.PatientTypeOption != null ? x.PatientTypeOption.Code : "",
        PatientTypeName = x.PatientTypeOption != null ? x.PatientTypeOption.Name : "",
        PatientSubjectCode = x.PatientSubjectOption != null ? x.PatientSubjectOption.Code : "",
        PatientSubjectName = x.PatientSubjectOption != null ? x.PatientSubjectOption.Name : "",
        PaymentSourceCode = x.PaymentSourceOption != null ? x.PaymentSourceOption.Code : "",
        PaymentSourceName = x.PaymentSourceOption != null ? x.PaymentSourceOption.Name : "",
        PaymentSourceOther = x.PaymentSourceOther,
        ExamLocationCode = x.ExamLocationOption != null ? x.ExamLocationOption.Code : "",
        ExamLocationName = x.ExamLocationOption != null ? x.ExamLocationOption.Name : "",
        PatientTypeOptionID = x.PatientTypeOptionID,
        PaymentSourceOptionID = x.PaymentSourceOptionID,
        ExamLocationOptionID = x.ExamLocationOptionID,
        PatientRefID = x.PatientRefID,
        InsuranceRefID = x.InsuranceRefID,
        EmploymentRefID = x.EmploymentRefID,
        RelativeRefID = x.RelativeRefID
    };
}
