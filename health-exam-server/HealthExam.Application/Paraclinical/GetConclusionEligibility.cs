using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HealthExam.Application.Common;
using HealthExam.Application.ExamRecords;
using HealthExam.Application.Signing;
using HealthExam.Domain.ExamRecords;

namespace HealthExam.Application.Paraclinical;

public interface IGetConclusionEligibilityHandler
{
    Task<ApplicationResult<ConclusionEligibilityResult>> HandleAsync(
        GetConclusionEligibilityQuery query, CancellationToken ct = default);
}

public class GetConclusionEligibilityHandler : IGetConclusionEligibilityHandler
{
    private const string LabelA = "Khám lâm sàng";
    private const string LabelB = "Cận lâm sàng";
    private const string SourceHealthExam = "health-exam-server";

    private readonly IParaclinicalRepository _paraclinicalRepository;
    private readonly IExamRecordRepository _recordRepository;
    private readonly ISignStepMapRepository _map;

    public GetConclusionEligibilityHandler(
        IParaclinicalRepository paraclinicalRepository,
        IExamRecordRepository recordRepository,
        ISignStepMapRepository map)
    {
        _paraclinicalRepository = paraclinicalRepository;
        _recordRepository = recordRepository;
        _map = map;
    }

    public async Task<ApplicationResult<ConclusionEligibilityResult>> HandleAsync(
        GetConclusionEligibilityQuery query, CancellationToken ct = default)
    {
        var record = await _recordRepository.GetAsync(query.DivisionId, query.RecordId, forUpdate: false, ct);
        if (record == null)
        {
            return ApplicationResult<ConclusionEligibilityResult>.Fail(
                ApplicationFailureCode.NotFound, "Không tìm thấy hồ sơ khám");
        }

        var (satisfiedB, pendingB, totalB) = await _paraclinicalRepository.EvaluateConditionBAsync(
            query.DivisionId, record.RecordID, ct);

        var conditionB = new ConclusionConditionResult(
            "B",
            LabelB,
            satisfiedB,
            totalB == 0
                ? "Không có chỉ định cận lâm sàng"
                : satisfiedB
                    ? $"{totalB}/{totalB} chỉ định đã trả kết quả hoặc đã huỷ"
                    : $"Còn {pendingB} chỉ định CLS chưa trả kết quả",
            SourceHealthExam);

        var mapSteps = await _map.ListAsync(query.DivisionId, record.VariantCode, ct);
        // VariantCode đổi được (UpdateExamRecord) — snapshot của bộ biểu mẫu cũ không được tính.
        var snapshots = record.SignSteps?.Where(s => s.VariantCode == record.VariantCode).ToList()
            ?? new List<ExamRecordSignStep>();

        var stepResults = mapSteps.Select(m =>
        {
            var snap = snapshots.FirstOrDefault(s => s.SWStep == m.SWStep);
            return new ConclusionSignStepResult(
                m.SWStep, m.StepName, m.ItemGroupID, m.SWRoleID,
                snap?.Status ?? "",
                snap?.SignedByEmployeeID,
                snap?.SignedAt,
                snap?.SignedByEmployeeName ?? "",
                snap?.PerformedByEmployeeID,
                snap?.PerformedByEmployeeName ?? "");
        }).ToList();

        var clinicalSteps = mapSteps.Where(m => !m.IsConclusionStep).ToList();
        var signedClinical = clinicalSteps.Count(m => snapshots.Any(s => s.SWStep == m.SWStep && s.Status == ExamRecordSignStepStatus.Signed));
        var conditionA = new ConclusionConditionResult(
            "A", LabelA, clinicalSteps.Count > 0 && signedClinical == clinicalSteps.Count,
            $"{signedClinical}/{clinicalSteps.Count} mục khám đã ký số",
            SourceHealthExam);

        var conditions = new List<ConclusionConditionResult> { conditionA, conditionB };

        var conclusionStep = mapSteps.FirstOrDefault(m => m.IsConclusionStep);
        var missingSteps = mapSteps
            .Where(m => !m.IsConclusionStep && !snapshots.Any(s => s.SWStep == m.SWStep && s.Status == ExamRecordSignStepStatus.Signed))
            .Select(m => m.SWStep)
            .ToList();

        var isSigned = record.SignStatus == ExamRecordSignStatus.Signed;
        var holdsConclusionRole = conclusionStep != null
            && query.RoleIds != null && query.RoleIds.Contains(conclusionStep.SWRoleID);

        var canSign = !isSigned
            && conditions.All(x => x.Satisfied)
            && missingSteps.Count == 0
            && holdsConclusionRole;

        var canCancelSign = isSigned
            && record.HisSignedByEmployeeID.HasValue
            && long.TryParse(query.ActorId, out var actorEmployeeId)
            && record.HisSignedByEmployeeID.Value == actorEmployeeId;

        return ApplicationResult<ConclusionEligibilityResult>.Success(
            new ConclusionEligibilityResult(
                record.RecordID,
                record.RecordCode,
                record.SubmissionID,
                conditions,
                canSign,
                SignedByEmployeeID: record.HisSignedByEmployeeID,
                SignedAt: record.HisSignedAt,
                Steps: stepResults,
                MissingSteps: missingSteps,
                IsSigned: isSigned,
                CanCancelSign: canCancelSign,
                IsReadOnly: isSigned));
    }
}
