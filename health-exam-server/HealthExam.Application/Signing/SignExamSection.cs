using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HealthExam.Application.Common;
using HealthExam.Application.ExamRecords;
using HealthExam.Application.His;
using HealthExam.Application.Integrations;
using HealthExam.Domain.Common;
using HealthExam.Domain.ExamRecords;

namespace HealthExam.Application.Signing;

public interface ISignExamSectionHandler
{
    Task<ApplicationResult<ExamSectionSignResult>> HandleAsync(
        SignExamSectionCommand command, CancellationToken ct = default);
}

/// <summary>
/// Bác sĩ bấm Ký số ở một mục khám. KHÔNG gọi sign-server: PDF chỉ được render và ký một lần
/// ở bước kết luận, vì sign-server dùng append mode nên nội dung phải chốt trước chữ ký đầu tiên.
///
/// Ký thay (PROJ-2374): người bấm có thể chọn Người xác nhận; snapshot ghi người đó làm người ký
/// (SignedBy*, chứng thư của họ đóng lên PDF) và người bấm làm người thực hiện (PerformedBy*).
/// Người bấm không cần vai trò — chỉ người ký phải thuộc danh sách vai trò của bước (HIS) và có chứng thư.
/// </summary>
public class SignExamSectionHandler : ISignExamSectionHandler
{
    /// <summary>Lệch đồng hồ client cho phép khi FE gửi "bây giờ".</summary>
    private static readonly TimeSpan FutureTolerance = TimeSpan.FromMinutes(5);

    private readonly IExamRecordRepository _records;
    private readonly ISignStepMapRepository _map;
    private readonly ICertificateGateway _certificates;
    private readonly IHisEmrClient _his;
    private readonly IUnitOfWork _uow;
    private readonly IAuditRepository _audit;

    public SignExamSectionHandler(
        IExamRecordRepository records, ISignStepMapRepository map,
        ICertificateGateway certificates, IHisEmrClient his, IUnitOfWork uow, IAuditRepository audit)
    {
        _records = records;
        _map = map;
        _certificates = certificates;
        _his = his;
        _uow = uow;
        _audit = audit;
    }

    public async Task<ApplicationResult<ExamSectionSignResult>> HandleAsync(
        SignExamSectionCommand command, CancellationToken ct = default)
    {
        if (command.ActorKind != ActorKind.Employee || command.EmployeeId <= 0
            || string.IsNullOrWhiteSpace(command.EmployeeCode))
        {
            return ApplicationResult<ExamSectionSignResult>.Fail(
                ApplicationFailureCode.Forbidden, "Chỉ nhân viên mới được ký số");
        }

        var now = DateTime.UtcNow;
        var signedAt = command.SignedAt?.ToUniversalTime() ?? now;
        if (signedAt > now + FutureTolerance)
            return ApplicationResult<ExamSectionSignResult>.Fail(
                ApplicationFailureCode.BadRequest, "Thời gian ký không được ở tương lai");

        // Cùng khuôn với SignConclusion: row lock trên HEX_ExamRecord serialise các lượt ký đồng
        // thời của MỘT hồ sơ. LockRecordAsync khóa theo RecordID không lọc tenant → tự đối chiếu
        // DivisionID. Mọi return sớm trước commit chỉ dispose tx → rollback.
        // Cả hai lượt gọi ngoài (HIS danh sách vai trò, SSM chứng thư) chạy TRONG lúc giữ lock:
        // tệ nhất ≈ timeout HIS (10s, HisEmrOptions.DefaultTimeout) + timeout SSM (10s, đăng ký
        // ở DependencyInjection cho ICertificateGateway) ≈ 20s, suốt đó SignConclusion,
        // CancelExamSectionSign và các lượt ký mục khác của CÙNG hồ sơ phải chờ (lưu form không
        // bị ảnh hưởng — SaveHisFormSection không lấy lock này). Chấp nhận vì ký đồng thời trên
        // một hồ sơ là ca hiếm.
        await using var tx = await _uow.BeginAsync(ct);
        var locked = await _records.LockRecordAsync(command.RecordId, ct);
        if (locked == null || locked.DivisionID != command.DivisionId)
            return ApplicationResult<ExamSectionSignResult>.Fail(
                ApplicationFailureCode.NotFound, "Không tìm thấy hồ sơ khám");

        var record = await _records.GetAsync(command.DivisionId, command.RecordId, forUpdate: true, ct);
        if (record == null)
            return ApplicationResult<ExamSectionSignResult>.Fail(
                ApplicationFailureCode.NotFound, "Không tìm thấy hồ sơ khám");

        if (record.SignStatus == ExamRecordSignStatus.Signed)
            return ApplicationResult<ExamSectionSignResult>.Fail(
                ApplicationFailureCode.InvalidState, "Hồ sơ đã ký kết luận, không sửa được chữ ký");

        var steps = await _map.ListAsync(command.DivisionId, record.VariantCode, ct);
        var step = steps.FirstOrDefault(s => s.ItemGroupID == command.ItemGroupId);
        if (step == null)
            return ApplicationResult<ExamSectionSignResult>.Fail(
                ApplicationFailureCode.SignPrecondition,
                "Mục khám này chưa được cấu hình bước ký");

        // Người ký phải nằm trong danh sách HIS giữ SWRoleID của bước — áp cho cả nhánh không
        // chọn ai (người ký = người bấm). Mã/tên lấy từ HIS, không tin FE. HIS lỗi → fail-closed.
        var signerId = command.ConfirmedByEmployeeId ?? command.EmployeeId;
        var members = await _his.ListSignRoleEmployeesAsync(step.SWRoleID,
            new HisCallContext(command.Credential, command.TraceId, command.DivisionId), ct);
        if (!members.IsSuccess)
            return ApplicationResult<ExamSectionSignResult>.Fail(
                ApplicationFailureCode.HisBadGateway,
                string.IsNullOrWhiteSpace(members.Message) ? "Không tra được danh sách người ký từ HIS" : members.Message);

        var signer = members.Value.FirstOrDefault(m => m.EmployeeID == signerId);
        if (signer == null)
            return ApplicationResult<ExamSectionSignResult>.Fail(
                ApplicationFailureCode.Forbidden,
                $"Người xác nhận không có vai trò ký của bước \"{step.StepName}\"");

        // HIS đôi khi trả dòng thiếu mã/tên (dữ liệu danh mục lỗi) — chặn tại đây, trước khi tốn
        // một lượt gọi SSM: để lọt thì snapshot mang tên rỗng và PDF ký kết luận in tên bước
        // thay cho tên bác sĩ (SignConclusionHandler fallback về StepName khi SignedByEmployeeName rỗng).
        if (string.IsNullOrWhiteSpace(signer.EmployeeCode) || string.IsNullOrWhiteSpace(signer.EmployeeName))
            return ApplicationResult<ExamSectionSignResult>.Fail(
                ApplicationFailureCode.SignPrecondition,
                "HIS không trả mã/tên nhân viên của người ký, không thể ký");

        var cert = await _certificates.GetAsync(signer.EmployeeCode, ct);
        if (!cert.Found)
            return ApplicationResult<ExamSectionSignResult>.Fail(
                ApplicationFailureCode.SignPrecondition, cert.Message);

        record.SignSteps ??= new System.Collections.Generic.List<ExamRecordSignStep>();
        var snapshot = record.SignSteps.FirstOrDefault(
            s => s.VariantCode == record.VariantCode && s.SWStep == step.SWStep);

        if (snapshot?.PerformedByEmployeeID is long performedBy && performedBy != command.EmployeeId)
            return ApplicationResult<ExamSectionSignResult>.Fail(
                ApplicationFailureCode.Forbidden, "Chỉ người thực hiện bước này mới được ký số");

        if (snapshot == null)
        {
            snapshot = new ExamRecordSignStep
            {
                DivisionID = command.DivisionId,
                RecordID = record.RecordID,
                VariantCode = record.VariantCode,
                CreatedDate = now
            };
            record.SignSteps.Add(snapshot);
        }

        snapshot.ItemGroupID = step.ItemGroupID;
        snapshot.SWStep = step.SWStep;
        snapshot.StepName = step.StepName;
        snapshot.SWRoleID = step.SWRoleID;
        snapshot.Status = ExamRecordSignStepStatus.Signed;
        snapshot.SignedByEmployeeID = signer.EmployeeID;
        snapshot.SignedByEmployeeCode = signer.EmployeeCode;
        snapshot.SignedByEmployeeName = signer.EmployeeName;
        snapshot.PerformedByEmployeeID = command.EmployeeId;
        snapshot.PerformedByEmployeeName = command.EmployeeName ?? "";
        snapshot.SignedAt = signedAt;
        snapshot.ModifiedDate = now;

        _audit.Add(new AuditEntry(command.DivisionId, AuditEntityTypes.Record, record.RecordID,
            AuditActions.StateChange, command.EmployeeId.ToString(), null, null,
            new
            {
                Action = "SECTION_SIGN_SNAPSHOT", step.SWStep, ItemGroupID = command.ItemGroupId,
                ActorEmployeeID = command.EmployeeId, SignerEmployeeID = signer.EmployeeID, SignedAt = signedAt
            },
            command.ActorKind));

        var save = await _uow.SaveChangesAsync(ct);
        if (save.Outcome == PersistenceSaveOutcome.UniqueConflict)
        {
            _uow.DiscardPendingChanges();
            return ApplicationResult<ExamSectionSignResult>.Fail(
                ApplicationFailureCode.InvalidState, "Mục khám đang được ký bởi lượt khác");
        }
        await tx.CommitAsync(ct);

        var progress = SigningProgressCalculator.Calculate(steps, record.SignSteps);

        return ApplicationResult<ExamSectionSignResult>.Success(new ExamSectionSignResult(
            command.ItemGroupId, step.SWStep, step.StepName, signer.EmployeeID, signedAt,
            Status: ExamRecordSignStepStatus.Signed,
            SigningProgressDone: progress.Done,
            SigningProgressTotal: progress.Total,
            SignedByEmployeeName: signer.EmployeeName,
            PerformedByEmployeeID: command.EmployeeId,
            PerformedByEmployeeName: command.EmployeeName ?? ""));
    }
}
