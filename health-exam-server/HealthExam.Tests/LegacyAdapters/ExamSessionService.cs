using HealthExam.Domain.Common;
using HealthExam.Domain.ExamSessions;
using HealthExam.Domain.ExamRecords;
using HealthExam.Domain.Imports;
using HealthExam.Domain.Paraclinical;
using HealthExam.Domain.Webhooks;
using HealthExam.Domain.Integrations;
using HealthExam.Domain.Catalogs;
using HealthExam.Infrastructure.Persistence.Legacy;
using HealthExam.API.Contracts;
using HealthExam.API.Middlewares;
using IHealthExamContext = HealthExam.Application.Common.IHealthExamContext;
using ValidationErrors = HealthExam.Application.Common.ValidationErrors;
using ValidationError = HealthExam.Application.Common.ValidationError;
using Microsoft.EntityFrameworkCore;

namespace HealthExam.Server.Service;

/// <summary>Đợt khám — 02-api-spec §3.1 (phần GET/POST/PUT của phase đăng ký).</summary>
public class ExamSessionService
{
    public const string PhaseOneDefaultSessionCode = "PHASE1-DEFAULT";

    private readonly IUnitOfWork _uow;
    private readonly IHealthExamContext _ctx;
    private readonly AuditService _audit;

    public ExamSessionService(IUnitOfWork uow, IHealthExamContext ctx, AuditService audit)
    {
        _uow = uow;
        _ctx = ctx;
        _audit = audit;
    }

    public async Task<PaginationData<ExamSessionItem>> ListAsync(
        string keyword, Guid? organizationId, Guid? packageId, short? state,
        DateOnly? from, DateOnly? to, int page, int size, CancellationToken ct = default)
    {
        (page, size) = Paging.Normalize(page, size);

        var q = _uow.ExamSessions.Query()
            .Where(x => x.DivisionID == _ctx.DivisionId && x.IsActive);

        if (organizationId.HasValue) q = q.Where(x => x.OrganizationID == organizationId.Value);
        if (packageId.HasValue) q = q.Where(x => x.PackageID == packageId.Value);
        if (state.HasValue) q = q.Where(x => (short)x.State == state.Value);
        if (from.HasValue) q = q.Where(x => x.ExamDate >= from.Value);
        if (to.HasValue) q = q.Where(x => x.ExamDate <= to.Value);

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var kw = keyword.Trim().ToLower();
            q = q.Where(x => x.SessionCode.ToLower().Contains(kw)
                          || x.SessionName.ToLower().Contains(kw)
                          || x.OrganizationName.ToLower().Contains(kw)
                          || x.ContractNo.ToLower().Contains(kw));
        }

        var total = await q.CountAsync(ct);
        var rows = await q.OrderByDescending(x => x.ExamDate).ThenBy(x => x.SessionCode)
            .Skip((page - 1) * size).Take(size)
            .ToListAsync(ct);

        var items = await ToItemsAsync(rows, ct);
        return new PaginationData<ExamSessionItem>(items, page, size, total);
    }

    public async Task<ExamSessionItem> GetAsync(Guid sessionId, CancellationToken ct = default)
    {
        var entity = await LoadAsync(sessionId, ct);
        return (await ToItemsAsync(new[] { entity }, ct))[0];
    }

    /// <summary>
    /// Phase 1 chưa cho FE quản lý đợt. Mỗi tenant dùng một đợt kỹ thuật cố định để giữ nguyên
    /// các bất biến SessionID/FK, cấp mã hồ sơ và HostRefID của form-server.
    /// </summary>
    public async Task<ExamSession> GetOrCreatePhaseOneDefaultAsync(CancellationToken ct = default)
    {
        var existing = await _uow.ExamSessions.Query().FirstOrDefaultAsync(
            x => x.DivisionID == _ctx.DivisionId && x.SessionCode == PhaseOneDefaultSessionCode, ct);
        if (existing != null) return existing;

        var now = DateTime.UtcNow;
        var sessionId = Guid.NewGuid();

        // Hai request đầu tiên có thể cùng đọc thấy "chưa có". PostgreSQL phải tự phân xử
        // bằng unique (DivisionID, SessionCode): request thắng INSERT, request còn lại bỏ qua,
        // rồi cả hai cùng đọc lại đúng một hàng. Không bắt unique-violation vì lỗi đó làm hỏng
        // transaction hiện hành nếu sau này lời gọi này được đặt trong transaction lớn hơn.
        if (_uow.Context.Database.IsNpgsql())
        {
            await _uow.Context.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "HEX_ExamSession"
                    ("SessionID", "DivisionID", "SessionCode", "SessionName", "ExamDate",
                     "DepartmentID", "State", "ExpectedCount", "IsActive",
                     "CreatedDate", "CreatedBy", "CreatedActorKind",
                     "ModifiedDate", "ModifiedBy", "ModifiedActorKind")
                VALUES
                    ({sessionId}, {_ctx.DivisionId}, {PhaseOneDefaultSessionCode}, {"Đợt mặc định Phase 1"},
                     {DateOnly.FromDateTime(now)}, {0}, {(short)ExamSessionState.Open}, {0}, {true},
                     {now}, {_ctx.ActorId}, {(short)_ctx.ActorKind},
                     {now}, {_ctx.ActorId}, {(short)_ctx.ActorKind})
                ON CONFLICT ("DivisionID", "SessionCode") DO NOTHING
                """, ct);

            return await _uow.ExamSessions.Query().SingleAsync(
                x => x.DivisionID == _ctx.DivisionId && x.SessionCode == PhaseOneDefaultSessionCode, ct);
        }

        var session = new ExamSession
        {
            SessionID = sessionId,
            DivisionID = _ctx.DivisionId,
            SessionCode = PhaseOneDefaultSessionCode,
            SessionName = "Đợt mặc định Phase 1",
            ExamDate = DateOnly.FromDateTime(now),
            State = ExamSessionState.Open,
            IsActive = true,
            CreatedDate = now,
            CreatedBy = _ctx.ActorId,
            CreatedActorKind = _ctx.ActorKind,
            ModifiedDate = now,
            ModifiedBy = _ctx.ActorId,
            ModifiedActorKind = _ctx.ActorKind
        };

        _uow.ExamSessions.Add(session);
        await _uow.SaveChangesAsync(ct);
        return session;
    }

    public async Task<ExamSessionItem> CreateAsync(ExamSessionSaveRequest req, CancellationToken ct = default)
    {
        Validate(req, isCreate: true);

        var code = req.SessionCode.Trim();
        if (await _uow.ExamSessions.AnyAsync(
                x => x.DivisionID == _ctx.DivisionId && x.SessionCode == code, ct))
            throw HealthExamException.BadRequest(
                $"Mã đợt khám {code} đã tồn tại",
                ValidationErrors.Of(nameof(req.SessionCode), "Đã tồn tại"));

        var now = DateTime.UtcNow;
        var entity = new ExamSession
        {
            SessionID = Guid.NewGuid(),
            DivisionID = _ctx.DivisionId,
            SessionCode = code,
            State = ExamSessionState.Draft,   // đợt luôn ra đời ở Nháp; đổi trạng thái đi đường riêng
            CreatedDate = now,
            CreatedBy = _ctx.ActorId,
            CreatedActorKind = _ctx.ActorKind,
            ModifiedDate = now,
            ModifiedBy = _ctx.ActorId,
            ModifiedActorKind = _ctx.ActorKind
        };

        await ApplyAsync(entity, req, ct);
        _uow.ExamSessions.Add(entity);
        await _uow.SaveChangesAsync(ct);

        return await GetAsync(entity.SessionID, ct);
    }

    public async Task<ExamSessionItem> UpdateAsync(Guid sessionId, ExamSessionSaveRequest req, CancellationToken ct = default)
    {
        var entity = await _uow.ExamSessions.Query().AsTracking()
            .FirstOrDefaultAsync(x => x.SessionID == sessionId && x.DivisionID == _ctx.DivisionId, ct)
            ?? throw HealthExamException.NotFound("Không tìm thấy đợt khám");

        Validate(req, isCreate: false);

        if (!string.IsNullOrWhiteSpace(req.SessionCode))
        {
            var code = req.SessionCode.Trim();
            if (code != entity.SessionCode &&
                await _uow.ExamSessions.AnyAsync(
                    x => x.DivisionID == _ctx.DivisionId && x.SessionCode == code && x.SessionID != sessionId, ct))
                throw HealthExamException.BadRequest(
                    $"Mã đợt khám {code} đã tồn tại",
                    ValidationErrors.Of(nameof(req.SessionCode), "Đã tồn tại"));
            entity.SessionCode = code;
        }

        await ApplyAsync(entity, req, ct);
        entity.ModifiedDate = DateTime.UtcNow;
        entity.ModifiedBy = _ctx.ActorId;
        entity.ModifiedActorKind = _ctx.ActorKind;

        await _uow.SaveChangesAsync(ct);
        return await GetAsync(sessionId, ct);
    }

    /// <summary>
    /// Đóng đợt khám — 02-api-spec §3.1.
    ///
    /// Là hành động RIÊNG chứ không phải `PUT State`: đóng đợt kéo theo hiệu ứng phụ (khoá mọi
    /// đường ghi thuộc đợt, chốt số liệu đối soát với đơn vị ký hợp đồng). Đi chung đường với
    /// sửa tên đợt thì hai việc rất khác nhau dùng chung một quyền và một dòng audit.
    /// </summary>
    public async Task<ExamSessionItem> CloseAsync(Guid sessionId, ExamSessionCloseRequest req, CancellationToken ct = default)
    {
        var entity = await TrackAsync(sessionId, ct);

        var from = entity.State;
        var res = entity.Close();
        if (!res.IsSuccess)
        {
            if (from == ExamSessionState.Closed)
                throw HealthExamException.SessionClosed(res.Failure.Message);
            throw HealthExamException.InvalidState(res.Failure.Message);
        }

        Touch(entity);

        _audit.StateChange(AuditEntityTypes.Session, entity.SessionID, (short)from, (short)entity.State,
            new { Reason = req?.Reason ?? "", entity.SessionCode });

        await _uow.SaveChangesAsync(ct);
        return await GetAsync(sessionId, ct);
    }

    /// <summary>
    /// Mở lại đợt đã đóng — 02-api-spec §3.1. LÝ DO BẮT BUỘC.
    ///
    /// Bắt buộc lý do vì mở lại đợt là mở lại một sổ đã chốt: số liệu đã đối soát với đơn vị có
    /// thể đổi sau đó. Không có lý do thì về sau không ai dựng lại được vì sao con số hôm nay
    /// khác con số đã ký biên bản.
    /// </summary>
    public async Task<ExamSessionItem> ReopenAsync(Guid sessionId, ExamSessionReopenRequest req, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(req?.Reason))
            throw HealthExamException.BadRequest(
                "Mở lại đợt khám phải có lý do",
                ValidationErrors.Of(nameof(ExamSessionReopenRequest.Reason), "Bỏ trống (bắt buộc)"));

        var entity = await TrackAsync(sessionId, ct);

        var from = entity.State;
        var res = entity.Reopen(req.Reason);
        if (!res.IsSuccess)
            throw HealthExamException.InvalidState(res.Failure.Message);

        Touch(entity);

        _audit.StateChange(AuditEntityTypes.Session, entity.SessionID, (short)from, (short)entity.State,
            new { Reason = req.Reason.Trim(), entity.SessionCode });

        await _uow.SaveChangesAsync(ct);
        return await GetAsync(sessionId, ct);
    }

    private async Task<ExamSession> TrackAsync(Guid sessionId, CancellationToken ct)
        => await _uow.ExamSessions.Query().AsTracking()
               .FirstOrDefaultAsync(x => x.SessionID == sessionId && x.DivisionID == _ctx.DivisionId, ct)
           ?? throw HealthExamException.NotFound("Không tìm thấy đợt khám");

    private void Touch(ExamSession entity)
    {
        entity.ModifiedDate = DateTime.UtcNow;
        entity.ModifiedBy = _ctx.ActorId;
        entity.ModifiedActorKind = _ctx.ActorKind;
    }

    /// <summary>
    /// Chặn đường đổi trạng thái đợt qua PUT.
    ///
    /// Trường `State` đã BỎ khỏi ExamSessionSaveRequest (nay đi qua POST /close và /reopen).
    /// Nhưng bỏ trường suông thì Newtonsoft LẶNG LẼ bỏ qua khoá lạ: FE cũ vẫn gửi State, vẫn
    /// nhận 200, và tưởng đợt đã đóng trong khi không có gì xảy ra. Bắt tường minh ở đây để
    /// lỗi nổ ra ngay tại chỗ gọi sai, kèm chỉ đường sang endpoint đúng.
    /// </summary>
    private static void RejectStateField(ExamSessionSaveRequest req)
    {
        if (req.ExtraFields == null) return;

        var key = req.ExtraFields.Keys.FirstOrDefault(
            k => string.Equals(k, "State", StringComparison.OrdinalIgnoreCase));
        if (key == null) return;

        throw HealthExamException.BadRequest(
            "Không đổi trạng thái đợt khám qua PUT. Dùng POST /v1/exam-sessions/{sessionId}/close hoặc /reopen",
            ValidationErrors.Of(key, "Không còn được chấp nhận — đóng/mở đợt là hành động riêng"));
    }

    /// <summary>Nạp đợt khám và chốt chặn tenant — dùng chung cho cả ExamRecordService.</summary>
    public async Task<ExamSession> LoadAsync(Guid sessionId, CancellationToken ct = default)
        => await _uow.ExamSessions.GetAsync(
               x => x.SessionID == sessionId && x.DivisionID == _ctx.DivisionId, ct)
           ?? throw HealthExamException.NotFound("Không tìm thấy đợt khám");

    private static void Validate(ExamSessionSaveRequest req, bool isCreate)
    {
        RejectStateField(req);

        var errors = new ValidationErrors();
        if (isCreate)
        {
            if (string.IsNullOrWhiteSpace(req.SessionCode))
                errors.Errors.Add(new ValidationError { Field = nameof(req.SessionCode), Reason = "Bỏ trống (bắt buộc)" });
            if (!req.ExamDate.HasValue)
                errors.Errors.Add(new ValidationError { Field = nameof(req.ExamDate), Reason = "Bỏ trống (bắt buộc)" });
        }

        if (req.ExamDate.HasValue && req.ExamDateTo.HasValue && req.ExamDateTo.Value < req.ExamDate.Value)
            errors.Errors.Add(new ValidationError { Field = nameof(req.ExamDateTo), Reason = "Nhỏ hơn ngày bắt đầu khám" });

        if (!string.IsNullOrWhiteSpace(req.VariantCode) && !ExamGroups.IsValid(req.VariantCode))
            errors.Errors.Add(new ValidationError { Field = nameof(req.VariantCode), Reason = "Nhóm khám không hợp lệ (DTK_01..DTK_10)" });

        if (errors.Errors.Count > 0)
            throw HealthExamException.BadRequest(payload: errors);
    }

    /// <summary>
    /// Gán trường + CHỤP ẢNH tên đơn vị / tên gói vào đợt.
    ///
    /// Chụp chứ không join live vì bốn giá trị đẩy sang form-server làm ContextValues phải
    /// bất biến theo đợt: đơn vị đổi tên sau ba tháng thì phiếu của đợt cũ vẫn phải in tên cũ.
    /// </summary>
    private async Task ApplyAsync(ExamSession entity, ExamSessionSaveRequest req, CancellationToken ct)
    {
        if (req.SessionName != null) entity.SessionName = req.SessionName.Trim();
        if (req.ContractNo != null) entity.ContractNo = req.ContractNo.Trim();
        if (req.ContractDate.HasValue) entity.ContractDate = req.ContractDate;
        if (req.ExamDate.HasValue) entity.ExamDate = req.ExamDate.Value;
        if (req.ExamDateTo.HasValue) entity.ExamDateTo = req.ExamDateTo;
        if (req.ExamPlace != null) entity.ExamPlace = req.ExamPlace.Trim();
        if (req.DepartmentID.HasValue) entity.DepartmentID = req.DepartmentID.Value;
        if (req.VariantCode != null) entity.VariantCode = req.VariantCode;
        if (req.ExpectedCount.HasValue) entity.ExpectedCount = req.ExpectedCount.Value;
        if (req.Note != null) entity.Note = req.Note;

        if (req.OrganizationID.HasValue)
        {
            var org = await _uow.Organizations.GetAsync(
                          x => x.OrganizationID == req.OrganizationID.Value && x.DivisionID == _ctx.DivisionId, ct)
                      ?? throw HealthExamException.BadRequest(
                          "Không tìm thấy đơn vị ký hợp đồng",
                          ValidationErrors.Of(nameof(req.OrganizationID), "Không tồn tại"));
            entity.OrganizationID = org.OrganizationID;
            entity.OrganizationName = org.OrgName;
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
    }

    /// <summary>
    /// Đếm hồ sơ bằng MỘT truy vấn gộp cho cả trang, không phải mỗi dòng một lần đếm:
    /// danh sách 20 đợt mà đếm lẻ là 20 round-trip.
    /// </summary>
    private async Task<List<ExamSessionItem>> ToItemsAsync(IReadOnlyCollection<ExamSession> rows, CancellationToken ct)
    {
        var ids = rows.Select(x => x.SessionID).ToList();
        var counts = await _uow.ExamRecords.Query()
            .Where(r => ids.Contains(r.SessionID)
                     && r.State != ExamRecordState.RegistrationCancelled
                     && r.State != ExamRecordState.ExamCancelled)
            .GroupBy(r => r.SessionID)
            .Select(g => new { SessionID = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        return rows.Select(x => new ExamSessionItem
        {
            SessionID = x.SessionID,
            SessionCode = x.SessionCode,
            SessionName = x.SessionName,
            OrganizationID = x.OrganizationID,
            OrganizationName = x.OrganizationName,
            ContractNo = x.ContractNo,
            ContractDate = x.ContractDate,
            ExamDate = x.ExamDate,
            ExamDateTo = x.ExamDateTo,
            ExamPlace = x.ExamPlace,
            PackageID = x.PackageID,
            PackageName = x.PackageName,
            VariantCode = x.VariantCode,
            State = x.State,
            StateName = StateNames.Of(x.State),
            ExpectedCount = x.ExpectedCount,
            RecordCount = counts.FirstOrDefault(c => c.SessionID == x.SessionID)?.Count ?? 0,
            Note = x.Note,
            IsActive = x.IsActive
        }).ToList();
    }
}
