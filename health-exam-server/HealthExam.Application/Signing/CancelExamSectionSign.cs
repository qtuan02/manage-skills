using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HealthExam.Application.Common;
using HealthExam.Application.ExamRecords;
using HealthExam.Domain.Common;
using HealthExam.Domain.ExamRecords;

namespace HealthExam.Application.Signing;

public interface ICancelExamSectionSignHandler
{
    Task<ApplicationResult<bool>> HandleAsync(
        CancelExamSectionSignCommand command, CancellationToken ct = default);
}

/// <summary>
/// Hủy ký một mục khám. Giữ lại dòng để bảo toàn người thực hiện; chỉ xóa trạng thái ký.
/// </summary>
public class CancelExamSectionSignHandler : ICancelExamSectionSignHandler
{
    private readonly IExamRecordRepository _records;
    private readonly IUnitOfWork _uow;
    private readonly IAuditRepository _audit;

    public CancelExamSectionSignHandler(
        IExamRecordRepository records, IUnitOfWork uow, IAuditRepository audit)
    {
        _records = records;
        _uow = uow;
        _audit = audit;
    }

    public async Task<ApplicationResult<bool>> HandleAsync(
        CancelExamSectionSignCommand command, CancellationToken ct = default)
    {
        // Cùng khuôn với SignConclusion: row lock trước khi đọc để sửa; LockRecordAsync không lọc
        // tenant nên tự đối chiếu DivisionID. Return sớm trước commit → dispose tx → rollback.
        await using var tx = await _uow.BeginAsync(ct);
        var locked = await _records.LockRecordAsync(command.RecordId, ct);
        if (locked == null || locked.DivisionID != command.DivisionId)
            return ApplicationResult<bool>.Fail(ApplicationFailureCode.NotFound, "Không tìm thấy hồ sơ khám");

        var record = await _records.GetAsync(command.DivisionId, command.RecordId, forUpdate: true, ct);
        if (record == null)
            return ApplicationResult<bool>.Fail(ApplicationFailureCode.NotFound, "Không tìm thấy hồ sơ khám");

        if (record.SignStatus == ExamRecordSignStatus.Signed)
            return ApplicationResult<bool>.Fail(
                ApplicationFailureCode.InvalidState, "Hồ sơ đã ký kết luận, không hủy ký từng mục được");

        var snapshot = record.SignSteps?.FirstOrDefault(s => s.ItemGroupID == command.ItemGroupId);
        if (snapshot == null)
            return ApplicationResult<bool>.Fail(
                ApplicationFailureCode.NotFound, "Mục khám này chưa được ký");

        if (snapshot.PerformedByEmployeeID is long performedBy && performedBy != command.EmployeeId)
            return ApplicationResult<bool>.Fail(
                ApplicationFailureCode.Forbidden, "Chỉ người thực hiện bước này mới được hủy ký");

        snapshot.Status = ExamRecordSignStepStatus.InProgress;
        snapshot.SignedByEmployeeID = null;
        snapshot.SignedByEmployeeCode = "";
        snapshot.SignedByEmployeeName = "";
        snapshot.SignedAt = null;
        snapshot.ModifiedDate = System.DateTime.UtcNow;

        _audit.Add(new AuditEntry(command.DivisionId, AuditEntityTypes.Record, record.RecordID,
            AuditActions.StateChange, command.EmployeeId.ToString(), null, null,
            new { Action = "SECTION_SIGN_CANCELLED", snapshot.SWStep, ItemGroupID = command.ItemGroupId },
            command.ActorKind));

        var save = await _uow.SaveChangesAsync(ct);
        if (save.Outcome == PersistenceSaveOutcome.UniqueConflict)
        {
            _uow.DiscardPendingChanges();
            return ApplicationResult<bool>.Fail(
                ApplicationFailureCode.InvalidState, "Mục khám đang được ký bởi lượt khác");
        }
        await tx.CommitAsync(ct);
        return ApplicationResult<bool>.Success(true);
    }
}
