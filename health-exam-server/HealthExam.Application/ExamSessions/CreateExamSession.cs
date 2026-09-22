using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HealthExam.Application.Common;
using HealthExam.Domain.Catalogs;
using HealthExam.Domain.Common;
using HealthExam.Domain.ExamSessions;

namespace HealthExam.Application.ExamSessions;

public interface ICreateExamSessionHandler
{
    Task<ApplicationResult<ExamSessionResult>> HandleAsync(
        CreateExamSessionCommand command, CancellationToken ct = default);
}

public sealed class CreateExamSessionHandler : ICreateExamSessionHandler
{
    private readonly IExamSessionRepository _repository;
    private readonly IUnitOfWork _uow;

    public CreateExamSessionHandler(IExamSessionRepository repository, IUnitOfWork uow)
    {
        _repository = repository;
        _uow = uow;
    }

    public async Task<ApplicationResult<ExamSessionResult>> HandleAsync(
        CreateExamSessionCommand command, CancellationToken ct = default)
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

        var errors = new List<ApplicationValidationError>();
        if (string.IsNullOrWhiteSpace(command.SessionCode))
            errors.Add(new ApplicationValidationError(nameof(command.SessionCode), "Bỏ trống (bắt buộc)"));
        if (!command.ExamDate.HasValue)
            errors.Add(new ApplicationValidationError(nameof(command.ExamDate), "Bỏ trống (bắt buộc)"));
        if (command.ExamDate.HasValue && command.ExamDateTo.HasValue && command.ExamDateTo.Value < command.ExamDate.Value)
            errors.Add(new ApplicationValidationError(nameof(command.ExamDateTo), "Nhỏ hơn ngày bắt đầu khám"));
        if (!string.IsNullOrWhiteSpace(command.VariantCode) && !ExamGroups.IsValid(command.VariantCode))
            errors.Add(new ApplicationValidationError(nameof(command.VariantCode), "Nhóm khám không hợp lệ (DTK_01..DTK_10)"));

        if (errors.Count > 0)
        {
            return ApplicationResult<ExamSessionResult>.Fail(
                ApplicationFailureCode.BadRequest, "Dữ liệu không hợp lệ", new ApplicationValidationErrors(errors));
        }

        var code = command.SessionCode.Trim();
        if (await _repository.ExistsByCodeAsync(command.DivisionId, code, null, ct))
        {
            return ApplicationResult<ExamSessionResult>.Fail(
                ApplicationFailureCode.BadRequest,
                $"Mã đợt khám {code} đã tồn tại",
                ApplicationValidationErrors.Of(nameof(command.SessionCode), "Đã tồn tại"));
        }

        Organization org = null;
        if (command.OrganizationID.HasValue)
        {
            org = await _repository.GetOrganizationAsync(command.DivisionId, command.OrganizationID.Value, ct);
            if (org == null)
            {
                return ApplicationResult<ExamSessionResult>.Fail(
                    ApplicationFailureCode.BadRequest,
                    "Không tìm thấy đơn vị ký hợp đồng",
                    ApplicationValidationErrors.Of(nameof(command.OrganizationID), "Không tồn tại"));
            }
        }

        ExamPackage pkg = null;
        if (command.PackageID.HasValue)
        {
            pkg = await _repository.GetPackageAsync(command.DivisionId, command.PackageID.Value, ct);
            if (pkg == null)
            {
                return ApplicationResult<ExamSessionResult>.Fail(
                    ApplicationFailureCode.BadRequest,
                    "Không tìm thấy gói khám",
                    ApplicationValidationErrors.Of(nameof(command.PackageID), "Không tồn tại"));
            }
        }

        var now = DateTime.UtcNow;
        var actorId = long.TryParse(command.ActorId, out var parsedActorId) ? parsedActorId : 0;
        var entity = new ExamSession
        {
            SessionID = Guid.NewGuid(),
            DivisionID = command.DivisionId,
            SessionCode = code,
            SessionName = command.SessionName?.Trim() ?? "",
            OrganizationID = org?.OrganizationID,
            OrganizationName = org?.OrgName ?? "",
            ContractNo = command.ContractNo?.Trim() ?? "",
            ContractDate = command.ContractDate,
            ExamDate = command.ExamDate.Value,
            ExamDateTo = command.ExamDateTo,
            ExamPlace = command.ExamPlace?.Trim() ?? "",
            DepartmentID = command.DepartmentID ?? 0,
            PackageID = pkg?.PackageID,
            PackageName = pkg?.PackageName ?? "",
            VariantCode = command.VariantCode,
            State = ExamSessionState.Draft,
            ExpectedCount = command.ExpectedCount ?? 0,
            Note = command.Note,
            IsActive = true,
            CreatedDate = now,
            CreatedBy = actorId,
            CreatedActorKind = command.ActorKind,
            ModifiedDate = now,
            ModifiedBy = actorId,
            ModifiedActorKind = command.ActorKind
        };

        _repository.Add(entity);
        await _uow.SaveChangesAsync(ct);

        var result = GetExamSessionHandler.MapToResult(entity, recordCount: 0);
        return ApplicationResult<ExamSessionResult>.Success(result);
    }
}
