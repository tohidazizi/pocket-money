namespace PocketMoney.Domain.Entities;

/// <summary>Household Invitation — Parent 2 flow (SDS §2.3, entity 7).</summary>
public sealed class HouseholdInvitation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid HouseholdId { get; set; }
    public string InvitedEmail { get; set; } = string.Empty;
    public string TokenHash { get; set; } = string.Empty;
    public string InvitedByParentId { get; set; } = string.Empty;
    public bool IsAccepted { get; set; } = false;
    public DateTimeOffset ExpiresAt { get; set; } = DateTimeOffset.UtcNow.AddDays(7);
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public Household Household { get; set; } = null!;
    public Parent InvitedByParent { get; set; } = null!;
}
