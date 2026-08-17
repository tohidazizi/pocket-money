namespace PocketMoney.Domain.Entities;

/// <summary>Global IP Ban (SDS §2.3, entity 6).</summary>
public sealed class IpBan
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string IpAddress { get; set; } = string.Empty;
    public int BanCount { get; set; } = 1;
    public DateTimeOffset BannedUntil { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAt { get; set; }
}
