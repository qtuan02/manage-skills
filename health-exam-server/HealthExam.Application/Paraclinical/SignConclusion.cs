using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HealthExam.Application.Common;
using HealthExam.Application.ExamRecords;
using HealthExam.Application.Integrations;
using HealthExam.Application.Signing;
using HealthExam.Domain.Common;
using HealthExam.Domain.ExamForms;
using HealthExam.Domain.ExamRecords;

namespace HealthExam.Application.Paraclinical;

public interface ISignConclusionHandler
{
    Task<ApplicationResult<ConclusionSignResult>> HandleAsync(
        SignConclusionCommand command, CancellationToken ct = default);
}

/// <summary>
/// Ký kết luận: chốt snapshot bước kết luận, render PDF một lần, rồi ký nối chuỗi từng marker
/// qua sign-server. PDF chỉ nằm trong bộ nhớ suốt vòng lặp và chỉ ghi MinIO một lần ở cuối,
/// nên ký hỏng giữa chừng không để lại file hay trạng thái nửa vời.
/// </summary>
public class SignConclusionHandler : ISignConclusionHandler
{
    private readonly IParaclinicalRepository _paraclinical;
    private readonly IExamRecordRepository _records;
    private readonly ISignStepMapRepository _map;
    private readonly ICertificateGateway _certificates;
    private readonly IPdfSigner _signer;
    private readonly IExamFileStore _store;
    private readonly IHisEmrClient _his;
    private readonly IUnitOfWork _uow;
    private readonly IAuditRepository _audit;

    public SignConclusionHandler(
        IParaclinicalRepository paraclinical, IExamRecordRepository records,
        ISignStepMapRepository map, ICertificateGateway certificates, IPdfSigner signer,
        IExamFileStore store, IHisEmrClient his, IUnitOfWork uow, IAuditRepository audit)
    {
        _paraclinical = paraclinical;
        _records = records;
        _map = map;
        _certificates = certificates;
        _signer = signer;
        _store = store;
        _his = his;
        _uow = uow;
        _audit = audit;
    }

    public async Task<ApplicationResult<ConclusionSignResult>> HandleAsync(
        SignConclusionCommand command, CancellationToken ct = default)
    {
        // Giao dịch bao trọn cả các lượt gọi sign-server theo chủ ý: row lock trên HEX_ExamRecord
        // serialise các lượt ký đồng thời của MỘT hồ sơ (lượt thứ hai chờ lock rồi thấy Signed và
        // trả về ngay), không giữ gì khác. Mọi return sớm trước commit chỉ dispose tx → rollback.
        await using var tx = await _uow.BeginAsync(ct);
        var locked = await _records.LockRecordAsync(command.RecordId, ct);
        if (locked == null || locked.DivisionID != command.DivisionId)
            return Fail(ApplicationFailureCode.NotFound, "Không tìm thấy hồ sơ khám", null);

        var record = await _records.GetAsync(command.DivisionId, command.RecordId, forUpdate: true, ct);
        var mapSteps = await _map.ListAsync(command.DivisionId, record.VariantCode, ct);
        record.SignSteps ??= new List<ExamRecordSignStep>();
        var snapshots = SnapshotsOf(record);

        if (record.SignStatus == ExamRecordSignStatus.Signed)
            return ApplicationResult<ConclusionSignResult>.Success(
                Build(record, mapSteps, Array.Empty<int>(), Array.Empty<ConclusionConditionResult>()));

        if (command.ActorKind != ActorKind.Employee
            || !long.TryParse(command.ActorId, out var employeeId) || employeeId <= 0
            || string.IsNullOrWhiteSpace(command.ActorCode))
        {
            return Fail(ApplicationFailureCode.Forbidden, "Chỉ nhân viên mới được ký kết luận", record);
        }

        var (satisfiedB, pendingB, totalB) = await _paraclinical.EvaluateConditionBAsync(
            command.DivisionId, record.RecordID, ct);
        var conditionB = new ConclusionConditionResult("B", "Cận lâm sàng", satisfiedB,
            totalB == 0 ? "Không có chỉ định cận lâm sàng"
                : satisfiedB ? $"{totalB}/{totalB} chỉ định đã trả kết quả hoặc đã huỷ"
                : $"Còn {pendingB} chỉ định CLS chưa trả kết quả",
            "health-exam-server");
        var conditions = new List<ConclusionConditionResult> { conditionB };

        if (!satisfiedB)
            return Fail(ApplicationFailureCode.SignPrecondition,
                "Chưa đủ điều kiện ký kết luận", record, mapSteps, conditions);

        var conclusionStep = mapSteps.FirstOrDefault(m => m.IsConclusionStep);
        if (conclusionStep == null)
            return Fail(ApplicationFailureCode.SignPrecondition,
                "Chưa cấu hình bước ký kết luận cho biểu mẫu này", record, mapSteps, conditions);

        if (command.RoleIds == null || !command.RoleIds.Contains(conclusionStep.SWRoleID))
            return Fail(ApplicationFailureCode.Forbidden,
                "Bạn không có vai trò ký kết luận", record, mapSteps, conditions);

        var missing = mapSteps
            .Where(m => !m.IsConclusionStep && !snapshots.Any(s => s.SWStep == m.SWStep && s.Status == ExamRecordSignStepStatus.Signed))
            .Select(m => m.SWStep).ToList();
        if (missing.Count > 0)
            return Fail(ApplicationFailureCode.SignPrecondition,
                $"Còn {missing.Count} mục khám chưa ký số", record, mapSteps, conditions, missing);

        var cert = await _certificates.GetAsync(command.ActorCode, ct);
        if (!cert.Found)
            return Fail(ApplicationFailureCode.SignPrecondition, cert.Message, record, mapSteps, conditions);

        var now = DateTime.UtcNow;
        var conclusionSnapshot = snapshots.FirstOrDefault(s => s.SWStep == conclusionStep.SWStep);
        if (conclusionSnapshot == null)
        {
            conclusionSnapshot = new ExamRecordSignStep
            {
                DivisionID = command.DivisionId,
                RecordID = record.RecordID,
                VariantCode = record.VariantCode,
                CreatedDate = now
            };
            record.SignSteps.Add(conclusionSnapshot);
            snapshots.Add(conclusionSnapshot);
        }
        conclusionSnapshot.ItemGroupID = null;
        conclusionSnapshot.SWStep = conclusionStep.SWStep;
        conclusionSnapshot.StepName = conclusionStep.StepName;
        conclusionSnapshot.SWRoleID = conclusionStep.SWRoleID;
        conclusionSnapshot.Status = ExamRecordSignStepStatus.Snapshot;
        conclusionSnapshot.SignedByEmployeeID = employeeId;
        conclusionSnapshot.SignedByEmployeeCode = command.ActorCode;
        conclusionSnapshot.SignedByEmployeeName = command.ActorName ?? "";
        // Bước kết luận không có khái niệm "ký thay" (SignExamSection): người kết luận luôn vừa
        // ký vừa thực hiện.
        conclusionSnapshot.PerformedByEmployeeID = employeeId;
        conclusionSnapshot.PerformedByEmployeeName = command.ActorName ?? "";
        conclusionSnapshot.SignedAt = now;
        conclusionSnapshot.ModifiedDate = now;

        // Flush trong tx (chưa commit): bền cùng lượt commit Failed/Signed phía dưới; ký hỏng giữa
        // chừng thì rollback luôn snapshot kết luận, không để lại dòng Snapshot mồ côi.
        var snapshotSave = await _uow.SaveChangesAsync(ct);
        if (snapshotSave.Outcome == PersistenceSaveOutcome.UniqueConflict)
        {
            _uow.DiscardPendingChanges();
            return Fail(ApplicationFailureCode.InvalidState,
                "Hồ sơ đang có lượt ký khác đang chạy", record, mapSteps, conditions);
        }

        if (!record.HisEmrDataID.HasValue || record.HisEmrDataID == Guid.Empty)
            return Fail(ApplicationFailureCode.SignPrecondition,
                "Hồ sơ chưa có biểu mẫu trên HIS để render PDF", record, mapSteps, conditions);

        var hisRequest = new HisRequest("", "GET", command.Credential,
            TraceId: command.TraceId, DivisionId: command.DivisionId);
        var pdf = await _his.RenderFormPdfAsync(record.HisEmrDataID.Value, hisRequest, ct);
        if (!pdf.IsSuccess)
            return Fail(ApplicationFailureCode.HisBadGateway, pdf.Message, record, mapSteps, conditions);

        var bytes = pdf.Value;
        foreach (var step in mapSteps.OrderBy(m => m.SWStep))
        {
            var snap = snapshots.FirstOrDefault(s => s.SWStep == step.SWStep);
            if (snap == null)
                return Fail(ApplicationFailureCode.SignPrecondition,
                    $"Bước \"{step.StepName}\" không có snapshot", record, mapSteps, conditions);

            var stepCert = await _certificates.GetAsync(snap.SignedByEmployeeCode, ct);
            if (!stepCert.Found)
                return Fail(ApplicationFailureCode.SignPrecondition,
                    $"Bước \"{step.StepName}\": {stepCert.Message}", record, mapSteps, conditions);

            var employeeName = string.IsNullOrWhiteSpace(snap.SignedByEmployeeName)
                ? snap.StepName : snap.SignedByEmployeeName;
            var outcome = await _signer.SignAsync(new PdfSignRequest(
                bytes, stepCert.Certificate, employeeName,
                (snap.SignedAt ?? now).ToLocalTime(),
                step.SignTitle, step.SignType, step.SLType, step.SearchPattern,
                step.SLPage, step.SLX, step.SLY), ct);

            if (!outcome.Succeeded)
                return Fail(ApplicationFailureCode.HisBadGateway,
                    $"Bước \"{step.StepName}\": {outcome.Message}", record, mapSteps, conditions);

            bytes = outcome.Pdf;
        }

        var objectPath = ExamFileStorePaths.ConclusionPdf(command.DivisionId, now, record.RecordID);
        if (!await _store.UploadPdfAsync(objectPath, bytes, ct))
        {
            // Không dùng ct: upload treo tới lúc client rớt là chính ca hay gặp, và khi đó ct đã
            // hủy — lưu Failed bằng ct sẽ ném OperationCanceledException, mất luôn dấu Failed.
            record.SignStatus = ExamRecordSignStatus.Failed;
            var failedSave = await _uow.SaveChangesAsync(CancellationToken.None);
            if (failedSave.Outcome == PersistenceSaveOutcome.Saved) await tx.CommitAsync(CancellationToken.None);
            else _uow.DiscardPendingChanges();
            return Fail(ApplicationFailureCode.HisBadGateway,
                "Ký xong nhưng không lưu được file, vui lòng ký lại", record, mapSteps, conditions);
        }

        record.SignedFilePath = objectPath;
        record.SignStatus = ExamRecordSignStatus.Signed;
        record.HisSignedByEmployeeID = employeeId;
        record.HisSignedAt = now;
        record.State = ExamRecordState.Completed;
        record.ExamFinishedAt ??= now;
        foreach (var s in snapshots)
        {
            s.Status = ExamRecordSignStepStatus.Signed;
            s.ModifiedDate = now;
        }

        _audit.Add(new AuditEntry(command.DivisionId, AuditEntityTypes.Record, record.RecordID,
            AuditActions.StateChange, command.ActorId, null, null,
            new { Action = "CONCLUSION_SIGN_COMPLETED", FilePath = objectPath, Steps = mapSteps.Count },
            command.ActorKind));
        // File đã nằm trên MinIO: từ đây KHÔNG dùng token của caller nữa. Client rớt kết nối
        // sau upload mà lượt lưu/commit bị hủy theo thì file đã ký mồ côi trong bucket còn hồ sơ
        // vẫn New — gọi lại sẽ ký và ghi thêm một file nữa.
        var finalSave = await _uow.SaveChangesAsync(CancellationToken.None);
        if (finalSave.Outcome == PersistenceSaveOutcome.UniqueConflict)
        {
            _uow.DiscardPendingChanges();
            return Fail(ApplicationFailureCode.InvalidState,
                "Hồ sơ đang có lượt ký khác đang chạy", record, mapSteps, conditions);
        }
        await tx.CommitAsync(CancellationToken.None);

        return ApplicationResult<ConclusionSignResult>.Success(
            Build(record, mapSteps, Array.Empty<int>(), conditions));
    }

    /// <summary>Chỉ snapshot của bộ biểu mẫu hiện tại — VariantCode đổi được, snapshot cũ không tính.</summary>
    private static List<ExamRecordSignStep> SnapshotsOf(ExamRecord record)
        => (record.SignSteps ?? new List<ExamRecordSignStep>())
            .Where(s => s.VariantCode == record.VariantCode).ToList();

    private static ConclusionSignResult Build(
        ExamRecord record,
        IReadOnlyList<SignStepMap> mapSteps,
        IReadOnlyList<int> missing,
        IReadOnlyList<ConclusionConditionResult> conditions)
    {
        var snapshots = SnapshotsOf(record);
        var steps = mapSteps.Select(m =>
        {
            var snap = snapshots.FirstOrDefault(s => s.SWStep == m.SWStep);
            return new ConclusionSignStepResult(
                m.SWStep, m.StepName, m.ItemGroupID, m.SWRoleID,
                snap?.Status ?? "", snap?.SignedByEmployeeID, snap?.SignedAt,
                snap?.SignedByEmployeeName ?? "",
                snap?.PerformedByEmployeeID,
                snap?.PerformedByEmployeeName ?? "");
        }).ToList();

        return new ConclusionSignResult(
            record.RecordID, record.SignStatus, record.SignedFilePath,
            record.HisSignedByEmployeeID, record.HisSignedAt, conditions, steps, missing);
    }

    private static ApplicationResult<ConclusionSignResult> Fail(
        ApplicationFailureCode code, string message, ExamRecord record,
        IReadOnlyList<SignStepMap> mapSteps = null,
        IReadOnlyList<ConclusionConditionResult> conditions = null,
        IReadOnlyList<int> missing = null)
    {
        var payload = record == null ? null : Build(
            record,
            mapSteps ?? Array.Empty<SignStepMap>(),
            missing ?? Array.Empty<int>(),
            conditions ?? Array.Empty<ConclusionConditionResult>());
        return ApplicationResult<ConclusionSignResult>.Fail(code, message, payload);
    }
}
