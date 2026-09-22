using System;
using HealthExam.Domain.Common;

namespace HealthExam.Application.Common;

public sealed record AuditEntry(
    string DivisionId,
    string EntityType,
    Guid EntityId,
    string Action,
    string ActorId,
    short? FromState,
    short? ToState,
    object Payload,
    ActorKind ActorKind = ActorKind.Employee,
    string TraceId = null);

public interface IAuditRepository
{
    void Add(AuditEntry entry);
}
