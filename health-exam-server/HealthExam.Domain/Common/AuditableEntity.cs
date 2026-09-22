namespace HealthExam.Domain.Common;

/// <summary>
/// Khối audit chung của mọi bảng FRM_* — 01-db-model §2.
/// CreatedBy là EmployeeID hoặc PatientID tuỳ CreatedActorKind, nên hai cột đi thành cặp;
/// tách riêng sẽ không dựng lại được "ai đã ghi dòng này".
/// </summary>
public abstract class AuditableEntity
{
    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;
    public long CreatedBy { get; set; }
    public ActorKind CreatedActorKind { get; set; } = ActorKind.Employee;
    public DateTime ModifiedDate { get; set; } = DateTime.UtcNow;
    public long ModifiedBy { get; set; }
    public ActorKind ModifiedActorKind { get; set; } = ActorKind.Employee;
}
