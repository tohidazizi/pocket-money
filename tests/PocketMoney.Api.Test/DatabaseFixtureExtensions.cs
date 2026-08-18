using Microsoft.EntityFrameworkCore;
using PocketMoney.Domain.Entities;
using PocketMoney.Global;
using PocketMoney.Persistence.Data;

namespace PocketMoney.Api.Test;

/// <summary>Household &amp; invitation seeds for Phase 2 tests.</summary>
public static class DatabaseFixtureExtensions
{
    /// <summary>Seeds a household with one parent (the owner). UIDs are unique per call — the database persists across tests.</summary>
    public static async Task<(Household Household, Parent Parent)> SeedHouseholdAsync(
        this DatabaseFixture fixture,
        string? ownerUid = null,
        string ownerEmail = "owner@test.local",
        string? displayName = null)
    {
        await using var db = fixture.CreateContext();
        var household = new Household { DisplayName = displayName };
        var parent = new Parent
        {
            Id = ownerUid ?? Guid.NewGuid().ToString("D"),
            Email = ownerEmail,
            HouseholdId = household.Id,
            // Fixed early date so "owner = earliest CreatedAt" (SDS §2.3)
            // holds even when a test's frozen fake clock (2026-08-16) makes a
            // later-created parent compare earlier than the real seeded UtcNow.
            CreatedAt = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero),
        };
        db.Households.Add(household);
        db.Parents.Add(parent);
        await db.SaveChangesAsync();
        return (household, parent);
    }

    /// <summary>Seeds a pending invitation in the household (returns the SHA-256 hash seed helper).</summary>
    public static async Task<(HouseholdInvitation Invitation, string RawToken)> SeedPendingInvitationAsync(
        this DatabaseFixture fixture,
        Guid householdId,
        string invitedByParentId,
        string invitedEmail = "invited@test.local")
    {
        await using var db = fixture.CreateContext();
        var rawToken = PocketMoney.Application.InvitationTokens.Generate();
        var invitation = new HouseholdInvitation
        {
            HouseholdId = householdId,
            InvitedEmail = invitedEmail,
            TokenHash = PocketMoney.Application.InvitationTokens.Hash(rawToken),
            InvitedByParentId = invitedByParentId,
        };
        db.HouseholdInvitations.Add(invitation);
        await db.SaveChangesAsync();
        return (invitation, rawToken);
    }

    /// <summary>Seeds a second parent directly (cap scenarios). UID unique per call.</summary>
    public static async Task<Parent> SeedSecondParentAsync(
        this DatabaseFixture fixture, Guid householdId, string? uid = null)
    {
        await using var db = fixture.CreateContext();
        var parent = new Parent
        {
            Id = uid ?? Guid.NewGuid().ToString("D"),
            Email = "second@test.local",
            HouseholdId = householdId,
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(5),
        };
        db.Parents.Add(parent);
        await db.SaveChangesAsync();
        return parent;
    }
}
