using PocketMoney.Global;

namespace PocketMoney.Domain.Entities;

/// <summary>Audit Log — append-only (SDS §2.3, entity 8).</summary>
public sealed class AuditLog
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? HouseholdId { get; set; }
    public string ActorId { get; set; } = string.Empty;
    public ActorType ActorType { get; set; }
    public AuditEventType EventType { get; set; }
    public string? DetailsJson { get; set; }
    public string? IpAddress { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
