using PocketMoney.Global;

namespace PocketMoney.Domain.Entities;

/// <summary>Child (SDS §2.3, entity 3).</summary>
public sealed class Child
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>5-char Base31 string.</summary>
    public string AccountId { get; set; } = string.Empty;
    public Guid HouseholdId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string PinHash { get; set; } = string.Empty;

    /// <summary>
    /// Currency of this child's balance — key into CurrencyType (SDS §2.1.1).
    /// Inherited from Household.DefaultCurrencyKey at creation; parents may
    /// change it at any time (PUT /api/v1/household/children/{id}/currency).
    /// </summary>
    public string CurrencyKey { get; set; } = CurrencyType.PointKey;

    public decimal CurrentBalance { get; set; } = 0.000m;
    public string CreatorId { get; set; } = string.Empty;
    public byte UnsuccessfulLoginAttempts { get; set; } = 0;
    public DateTimeOffset? LockedUntil { get; set; }
    public bool IsPermanentlyLocked => LockedUntil == DateTimeOffset.MaxValue;

    /// <summary>
    /// Security Stamp changes on PIN reset and on manual lock/unlock (FR-P8)
    /// to invalidate active 365-day tokens.
    /// </summary>
    public Guid SecurityStamp { get; set; } = Guid.NewGuid();
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public Household Household { get; set; } = null!;
    public Parent Creator { get; set; } = null!;
    public ICollection<Transaction> Transactions { get; set; } = new List<Transaction>();
}
