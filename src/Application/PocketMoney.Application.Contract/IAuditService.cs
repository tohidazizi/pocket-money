using PocketMoney.Domain.Entities;
using PocketMoney.Global;

namespace PocketMoney.Application.Contract;

/// <summary>
/// Append-only audit trail (SDS §8). Implementations stage the entry on the
/// ambient DbContext WITHOUT saving — the calling service owns the unit of
/// work, so the audit row commits (or rolls back) with the mutation it
/// describes. This intentionally deviates from the SDS §8 sample, which
/// saved immediately; single-SaveChanges keeps transactions atomic.
/// </summary>
public interface IAuditService
{
    void Log(
        Guid? householdId,
        string actorId,
        ActorType actorType,
        AuditEventType eventType,
        object? details = null,
        string? ipAddress = null);
}
