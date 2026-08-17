namespace PocketMoney.Domain.Entities;

/// <summary>Parent (SDS §2.3, entity 2).</summary>
public sealed class Parent
{
    /// <summary>Firebase User UID.</summary>
    public string Id { get; set; } = string.Empty;
    public Guid HouseholdId { get; set; }
    public string? DisplayName { get; set; }
    public string Email { get; set; } = string.Empty;

    /// <summary>
    /// Empty string = no PIN set yet (first-time parent, SDS §7.1.2).
    /// hasPin := ParentPinHash is non-empty.
    /// </summary>
    public string ParentPinHash { get; set; } = string.Empty;

    /// <summary>
    /// Earliest Parent.CreatedAt in the household identifies the household
    /// owner — the only parent who may delete it (FR-P1, SDS §7.1 DELETE).
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public Household Household { get; set; } = null!;
}
