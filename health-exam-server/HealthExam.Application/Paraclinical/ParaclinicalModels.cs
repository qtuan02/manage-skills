using System;
using System.Collections.Generic;
using HealthExam.Domain.Common;
using HealthExam.Domain.Paraclinical;

namespace HealthExam.Application.Paraclinical;

public static class ParaclinicalItemStateNames
{
    public static string Of(ParaclinicalItemState state) => state switch
    {
        ParaclinicalItemState.Ordered => "Chờ chỉ định",
        ParaclinicalItemState.Waiting => "Chờ thực hiện",
        ParaclinicalItemState.InProgress => "Đang thực hiện",
        ParaclinicalItemState.Done => "Đã trả KQ",
        ParaclinicalItemState.Cancelled => "Hủy",
        _ => ""
    };
}

public sealed record ParaclinicalOrderItemResult(
    Guid OrderItemID,
    Guid OrderID,
    string OrderNo,
    long ServiceID,
    string ServiceCode,
    string ServiceName,
    string ServiceGroupCode,
    short Quantity,
    short State,
    string StateName,
    DateTime? PerformedAt,
    DateTime? ResultAt,
    string ResultSourceKind,
    string ResultRefID,
    Guid? AttachmentID,
    bool? IsAbnormal,
    Guid? SourcePackageID,
    DateTime? CancelledAt,
    string CancelReason);

public sealed record ParaclinicalOrderResult(
    Guid OrderID,
    Guid RecordID,
    Guid SessionID,
    string OrderNo,
    string ParaclinicalKind,
    Guid? SourcePackageID,
    long OrderedByID,
    string OrderedByName,
    DateTime OrderedAt,
    string TargetSystem,
    short SentStatus,
    DateTime? SentAt,
    string Note,
    bool IsActive,
    IReadOnlyList<ParaclinicalOrderItemResult> Items,
    short Status = 0,
    string StatusName = "Draft",
    long? HisParaClinReqId = null,
    long? HisAdmissionId = null,
    string HisAdmissionCode = "",
    DateTime? SubmittedAt = null,
    DateTime? CancelledAt = null,
    DateTime? LastSyncAt = null,
    string LastSyncError = "");

public sealed record ParaclinicalResultItemDto(
    Guid ResultItemId,
    Guid ResultId,
    Guid? OrderItemId,
    string HisDetailId,
    string Value,
    string Text,
    string Unit,
    string ReferenceRange,
    string AbnormalFlag);

public sealed record ParaclinicalResultDto(
    Guid ResultId,
    Guid OrderId,
    string HisResultId,
    DateTime? ResultDate,
    string Status,
    DateTime ImportedAt,
    IReadOnlyList<ParaclinicalResultItemDto> Items);

public sealed record ConclusionConditionResult(
    string Code,
    string Label,
    bool Satisfied,
    string Detail,
    string Source);

public sealed record ConclusionEligibilityResult(
    Guid ProfileID,
    string RecordCode,
    Guid? SubmissionID,
    IReadOnlyList<ConclusionConditionResult> Conditions,
    bool CanSignConclusion,
    long? SignedByEmployeeID = null,
    DateTime? SignedAt = null,
    IReadOnlyList<ConclusionSignStepResult> Steps = null,
    IReadOnlyList<int> MissingSteps = null,
    bool IsSigned = false,
    bool CanCancelSign = false,
    bool IsReadOnly = false);

public sealed record ConclusionSignStepResult(
    int SwStep,
    string StepName,
    int? ItemGroupID,
    long SwRoleId,
    string Status,
    long? SignedByEmployeeID,
    DateTime? SignedAt,
    string SignedByEmployeeName,
    long? PerformedByEmployeeID = null,
    string PerformedByEmployeeName = "");

public sealed record ConclusionSignResult(
    Guid RecordID,
    string Status,
    string SignedFilePath,
    long? SignedByEmployeeID,
    DateTime? SignedAt,
    IReadOnlyList<ConclusionConditionResult> Conditions,
    IReadOnlyList<ConclusionSignStepResult> Steps,
    IReadOnlyList<int> MissingSteps);

public class ActorInfo
{
    public long ActorID { get; set; }
    public short ActorKind { get; set; } = (short)HealthExam.Domain.Common.ActorKind.Employee;
    public string ActorName { get; set; } = "";

    public ActorInfo() { }

    public ActorInfo(long actorId, short actorKind, string actorName)
    {
        ActorID = actorId;
        ActorKind = actorKind;
        ActorName = actorName ?? "";
    }
}

// ───────────────────────── Commands & Queries ──────────────────────────────────────

public sealed record GetRecordOrdersQuery(
    string DivisionId,
    Guid RecordId,
    bool IncludeCancelled = true);

public sealed record CreateOrdersCommand(
    string DivisionId,
    string ActorId,
    string ActorName,
    ActorKind ActorKind,
    Guid RecordId,
    IReadOnlyList<long> ServiceIds,
    string Note,
    int RoomId);

public sealed record CreateOrdersFromPackageCommand(
    string DivisionId,
    string ActorId,
    string ActorName,
    ActorKind ActorKind,
    Guid RecordId,
    Guid? PackageId,
    string Note,
    int RoomId);

public sealed record GetOrderQuery(
    string DivisionId,
    Guid OrderId);

public sealed record CancelOrderCommand(
    string DivisionId,
    string ActorId,
    ActorKind ActorKind,
    Guid OrderId,
    IReadOnlyList<Guid> OrderItemIds,
    string Reason);

public sealed record ChangeOrderStateCommand(
    string DivisionId,
    string ActorId,
    ActorKind ActorKind,
    Guid OrderId,
    short State,
    IReadOnlyList<Guid> OrderItemIds,
    bool? IsAbnormal = null,
    string ResultRefId = null);

public sealed record GetConclusionEligibilityQuery(
    string DivisionId,
    Guid RecordId,
    string Credential = null,
    string TraceId = null,
    string ActorId = null,
    ActorKind ActorKind = ActorKind.Employee,
    int DepartmentId = 0,
    IReadOnlyCollection<long> RoleIds = null);

public sealed record SignConclusionCommand(
    string DivisionId,
    string ActorId,
    string ActorName,
    ActorKind ActorKind,
    Guid RecordId,
    string Credential,
    string TraceId,
    string ActorCode,
    IReadOnlyCollection<long> RoleIds,
    int DepartmentId = 0);

public sealed record CancelConclusionSignCommand(
    string DivisionId,
    string ActorId,
    ActorKind ActorKind,
    Guid RecordId);

public static class ParaclinicalMappingExtensions
{
    public static ParaclinicalOrderResult ToResult(this ParaclinicalOrder order, bool includeCancelled = true)
        => new(
            order.OrderID,
            order.RecordID,
            order.SessionID,
            order.OrderNo,
            order.ParaclinicalKind,
            order.SourcePackageID,
            order.OrderedByID,
            order.OrderedByName,
            order.OrderedAt,
            order.TargetSystem,
            (short)order.SentStatus,
            order.SentAt,
            order.Note,
            order.IsActive,
            (order.Items ?? Array.Empty<ParaclinicalOrderItem>())
                .Where(x => includeCancelled || x.State != ParaclinicalItemState.Cancelled)
                .OrderBy(x => x.ServiceCode)
                .Select(ToResult)
                .ToList(),
            (short)order.Status,
            order.Status.ToString(),
            order.HisParaClinReqId,
            order.HisAdmissionId,
            order.HisAdmissionCode,
            order.SubmittedAt,
            order.CancelledAt,
            order.LastSyncAt,
            order.LastSyncError);

    public static ParaclinicalOrderItemResult ToResult(this ParaclinicalOrderItem item)
        => new(
            item.OrderItemID,
            item.OrderID,
            item.Order?.OrderNo ?? "",
            item.ServiceID,
            item.ServiceCode,
            item.ServiceName,
            item.ServiceGroupCode,
            item.Quantity,
            (short)item.State,
            ParaclinicalItemStateNames.Of(item.State),
            item.PerformedAt,
            item.ResultAt,
            item.ResultSourceKind,
            item.ResultRefID,
            item.AttachmentID,
            item.IsAbnormal,
            item.SourcePackageID,
            item.CancelledAt,
            item.CancelReason);

    public static ParaclinicalResultDto ToResult(this ParaclinicalResult res)
        => new(
            res.ResultId,
            res.OrderId,
            res.HisResultId,
            res.ResultDate,
            res.Status,
            res.ImportedAt,
            (res.Items ?? Array.Empty<ParaclinicalResultItem>())
                .Select(i => new ParaclinicalResultItemDto(
                    i.ResultItemId,
                    i.ResultId,
                    i.OrderItemId,
                    i.HisDetailId,
                    i.Value,
                    i.Text,
                    i.Unit,
                    i.ReferenceRange,
                    i.AbnormalFlag))
                .ToList());
}
