using System;
using HealthExam.Application.Common;
using HealthExam.Domain.Common;
using Newtonsoft.Json;

namespace HealthExam.Infrastructure.Persistence;

public class AuditRepository : IAuditRepository
{
    private readonly HealthExamDbContext _db;

    public AuditRepository(HealthExamDbContext db)
    {
        _db = db;
    }

    public void Add(AuditEntry entry)
    {
        var log = new AuditLog
        {
            DivisionID = entry.DivisionId ?? string.Empty,
            EntityType = entry.EntityType ?? string.Empty,
            EntityID = entry.EntityId,
            Action = entry.Action ?? string.Empty,
            ActorID = long.TryParse(entry.ActorId, out var actorId) ? actorId : 0,
            ActorName = entry.ActorId ?? string.Empty,
            ActorKind = entry.ActorKind,
            TraceID = entry.TraceId ?? string.Empty,
            FromState = entry.FromState,
            ToState = entry.ToState,
            Payload = entry.Payload switch
            {
                null => null,
                string str => str,
                _ => JsonConvert.SerializeObject(entry.Payload)
            },
            OccurredAt = DateTime.UtcNow
        };

        _db.AuditLogs.Add(log);
    }
}
