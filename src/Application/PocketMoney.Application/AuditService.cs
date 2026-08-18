using System.Text.Json;
using PocketMoney.Application.Contract;
using PocketMoney.Domain.Entities;
using PocketMoney.Global;
using PocketMoney.Persistence.Data;

namespace PocketMoney.Application;

/// <summary>
/// Append-only audit trail (SDS §8).
///
/// Deliberate deviation from the SDS §8 sample: entries are STAGED on the
/// ambient DbContext without an immediate SaveChanges, so the audit row
/// commits (or rolls back) in the very same transaction as the mutation it
/// describes. The calling service owns the unit of work.
/// </summary>
public sealed class AuditService(PocketMoneyDbContext db) : IAuditService
{
    public void Log(
        Guid? householdId,
        string actorId,
        ActorType actorType,
        AuditEventType eventType,
        object? details = null,
        string? ipAddress = null)
    {
        db.AuditLogs.Add(new AuditLog
        {
            HouseholdId = householdId,
            ActorId = actorId,
            ActorType = actorType,
            EventType = eventType,
            DetailsJson = details is null ? null : JsonSerializer.Serialize(details),
            IpAddress = ipAddress,
        });
    }
}
