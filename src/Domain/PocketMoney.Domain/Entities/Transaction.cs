using PocketMoney.Global;

namespace PocketMoney.Domain.Entities;

/// <summary>Transaction — append-only ledger (SDS §2.3, entity 4).</summary>
public sealed class Transaction
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid HouseholdId { get; set; }
    public Guid ChildId { get; set; }
    public TransactionType Type { get; set; }

    /// <summary>
    /// Snapshot of the child's CurrencyKey at creation time (SDS §2.1.1) —
    /// keeps the append-only ledger self-describing per row.
    /// </summary>
    public string CurrencyKey { get; set; } = string.Empty;

    public decimal Amount { get; set; }
    public string Reason { get; set; } = string.Empty;
    public decimal RemainingAfter { get; set; }
    public string CreatorId { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public Household Household { get; set; } = null!;
    public Child Child { get; set; } = null!;
    public Parent Creator { get; set; } = null!;
}
