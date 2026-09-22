using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HealthExam.Application.Common;
using HealthExam.Domain.ExamSessions;

namespace HealthExam.Application.ExamSessions;

public interface IUpdateExamSessionHandler
{
    Task<ApplicationResult<ExamSessionResult>> HandleAsync(
        UpdateExamSessionCommand command, CancellationToken ct = default);
}

public sealed class UpdateExamSessionHandler : IUpdateExamSessionHandler
{
    private readonly IExamSessionRepository _repository;
    private readonly IUnitOfWork _uow;

    public UpdateExamSessionHandler(IExamSessionRepository repository, IUnitOfWork uow)
    {
        _repository = repository;
        _uow = uow;
    }

    public async Task<ApplicationResult<ExamSessionResult>> HandleAsync(
        UpdateExamSessionCommand command, CancellationToken ct = default)
    {
        if (command.ExtraFieldKeys != null)
        {
            var stateKey = command.ExtraFieldKeys.FirstOrDefault(
                k => string.Equals(k, "State", StringComparison.OrdinalIgnoreCase));
            if (stateKey != null)
            {
                return ApplicationResult<ExamSessionResult>.Fail(
                    ApplicationFailureCode.BadRequest,
                    "Không đổi trạng thái đợt khám qua PUT. Dùng POST /v1/exam-sessions/{sessionId}/close hoặc /reopen",
                    ApplicationValidationErrors.Of(stateKey, "Không còn được chấp nhận — đóng/mở đợt là hành động riêng"));
            }
        }

        var entity = await _repository.GetAsync(command.DivisionId, command.SessionId, forUpdate: true, ct);
        if (entity == null)
        {
            return ApplicationResult<ExamSessionResult>.Fail(
                ApplicationFailureCode.NotFound, "Không tìm thấy đợt khám");
        }

        var errors = new List<ApplicationValidationError>();
        if (command.ExamDate.HasValue && command.ExamDateTo.HasValue && command.ExamDateTo.Value < command.ExamDate.Value)
            errors.Add(new ApplicationValidationError(nameof(command.ExamDateTo), "Nhỏ hơn ngày bắt đầu khám"));
        if (!string.IsNullOrWhiteSpace(command.VariantCode) && !ExamGroups.IsValid(command.VariantCode))
            errors.Add(new ApplicationValidationError(nameof(command.VariantCode), "Nhóm khám không hợp lệ (DTK_01..DTK_10)"));

        if (errors.Count > 0)
        {
            return ApplicationResult<ExamSessionResult>.Fail(
                ApplicationFailureCode.BadRequest, "Dữ liệu không hợp lệ", new ApplicationValidationErrors(errors));
        }

        if (!string.IsNullOrWhiteSpace(command.SessionCode))
        {
            var code = command.SessionCode.Trim();
            if (code != entity.SessionCode &&
                await _repository.ExistsByCodeAsync(command.DivisionId, code, command.SessionId, ct))
            {
                return ApplicationResult<ExamSessionResult>.Fail(
                    ApplicationFailureCode.BadRequest,
                    $"Mã đợt khám {code} đã tồn tại",
                    ApplicationValidationErrors.Of(nameof(command.SessionCode), "Đã tồn tại"));
            }
            entity.SessionCode = code;
        }

        if (command.SessionName != null) entity.SessionName = command.SessionName.Trim();
        if (command.ContractNo != null) entity.ContractNo = command.ContractNo.Trim();
        if (command.ContractDate.HasValue) entity.ContractDate = command.ContractDate;
        if (command.ExamDate.HasValue) entity.ExamDate = command.ExamDate.Value;
        if (command.ExamDateTo.HasValue) entity.ExamDateTo = command.ExamDateTo;
        if (command.ExamPlace != null) entity.ExamPlace = command.ExamPlace.Trim();
        if (command.DepartmentID.HasValue) entity.DepartmentID = command.DepartmentID.Value;
        if (command.VariantCode != null) entity.VariantCode = command.VariantCode;
        if (command.ExpectedCount.HasValue) entity.ExpectedCount = command.ExpectedCount.Value;
        if (command.Note != null) entity.Note = command.Note;

        if (command.OrganizationID.HasValue)
        {
            var org = await _repository.GetOrganizationAsync(command.DivisionId, command.OrganizationID.Value, ct);
            if (org == null)
            {
                return ApplicationResult<ExamSessionResult>.Fail(
                    ApplicationFailureCode.BadRequest,
                    "Không tìm thấy đơn vị ký hợp đồng",
                    ApplicationValidationErrors.Of(nameof(command.OrganizationID), "Không tồn tại"));
            }
            entity.OrganizationID = org.OrganizationID;
            entity.OrganizationName = org.OrgName;
        }

        if (command.PackageID.HasValue)
        {
            var pkg = await _repository.GetPackageAsync(command.DivisionId, command.PackageID.Value, ct);
            if (pkg == null)
            {
                return ApplicationResult<ExamSessionResult>.Fail(
                    ApplicationFailureCode.BadRequest,
                    "Không tìm thấy gói khám",
                    ApplicationValidationErrors.Of(nameof(command.PackageID), "Không tồn tại"));
            }
            entity.PackageID = pkg.PackageID;
            entity.PackageName = pkg.PackageName;
        }

        var actorId = long.TryParse(command.ActorId, out var parsedActorId) ? parsedActorId : 0;
        entity.ModifiedDate = DateTime.UtcNow;
        entity.ModifiedBy = actorId;
        entity.ModifiedActorKind = command.ActorKind;

        await _uow.SaveChangesAsync(ct);

        var recordCount = await _repository.GetRecordCountAsync(command.DivisionId, command.SessionId, ct);
        var result = GetExamSessionHandler.MapToResult(entity, recordCount);
        return ApplicationResult<ExamSessionResult>.Success(result);
    }
}
