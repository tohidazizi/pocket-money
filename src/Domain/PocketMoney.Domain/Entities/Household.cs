using PocketMoney.Global;

namespace PocketMoney.Domain.Entities;

/// <summary>Household (SDS §2.3, entity 1).</summary>
public sealed class Household
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string? DisplayName { get; set; }

    /// <summary>Currency new child profiles inherit at creation (SDS §2.1.1).</summary>
    public string DefaultCurrencyKey { get; set; } = CurrencyType.PointKey;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    // Navigation properties
    public ICollection<Parent> Parents { get; set; } = new List<Parent>();
    public ICollection<Child> Children { get; set; } = new List<Child>();
    public ICollection<Transaction> Transactions { get; set; } = new List<Transaction>();
    public ICollection<HouseholdInvitation> Invitations { get; set; } = new List<HouseholdInvitation>();
}
