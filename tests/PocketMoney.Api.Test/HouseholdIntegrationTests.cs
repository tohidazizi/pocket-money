using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PocketMoney.Application;
using PocketMoney.Application.Contract;
using PocketMoney.Application.Model.Households;
using PocketMoney.Global;
using Xunit;

namespace PocketMoney.Api.Test;

/// <summary>
/// Phase 2 integration tests: household CRUD, auto-registration, parent PIN
/// (API Spec §3, §4.1, SDS §7.1.2) against real PostgreSQL.
/// UIDs are unique per test — the database persists across the run.
/// </summary>
[Collection("database")]
public class HouseholdIntegrationTests
{
    private readonly DatabaseFixture _fixture;
    private static readonly FakeTimeProvider Time = new();

    public HouseholdIntegrationTests(DatabaseFixture fixture) => _fixture = fixture;

    private HouseholdService CreateService(DatabaseFixture fixture)
    {
        // One shared context: the audit service stages rows on the same
        // unit of work the service saves.
        var db = fixture.CreateContext();
        return new HouseholdService(db, new AuditService(db), Time);
    }

    private static string UniqueIp() =>
        $"10.{Random.Shared.Next(1, 254)}.{Random.Shared.Next(1, 254)}.{Random.Shared.Next(1, 254)}";

    // ------------------------------------------------------------------
    // Auto-Registration (SDS §7.1.2)
    // ------------------------------------------------------------------

    [Fact]
    public async Task First_sign_in_creates_household_with_owner_parent()
    {
        var uid = Guid.NewGuid().ToString("D");
        var service = CreateService(_fixture);

        var response = await service.GetOrCreateAsync(uid, "new@test.local", UniqueIp());

        var self = response.Parents.Should().ContainSingle().Subject;
        self.Id.Should().Be(uid);
        self.IsOwner.Should().BeTrue();
        self.HasPin.Should().BeFalse();
        response.MaxParents.Should().Be(Constants.MaxParentsPerHousehold);

        await using var db = _fixture.CreateContext();
        var household = await db.Households.SingleAsync(h => h.Id == response.Id);
        household.DefaultCurrencyKey.Should().Be(CurrencyType.PointKey);
    }

    [Fact]
    public async Task Second_sign_in_returns_existing_household_without_duplication()
    {
        var (household, owner) = await _fixture.SeedHouseholdAsync();
        var service = CreateService(_fixture);

        var first = await service.GetOrCreateAsync(owner.Id, owner.Email, UniqueIp());
        var second = await service.GetOrCreateAsync(owner.Id, email: null, UniqueIp());

        first.Id.Should().Be(household.Id);
        second.Id.Should().Be(household.Id);
        second.Parents.Should().HaveCount(1);
    }

    [Fact]
    public async Task Sign_in_with_pending_invitation_email_joins_inviting_household()
    {
        var (household, owner) = await _fixture.SeedHouseholdAsync();
        var invitedEmail = $"invited-{Guid.NewGuid():N}@test.local";
        await _fixture.SeedPendingInvitationAsync(household.Id, owner.Id, invitedEmail);
        var guestUid = Guid.NewGuid().ToString("D");
        var service = CreateService(_fixture);

        var response = await service.GetOrCreateAsync(guestUid, invitedEmail.ToUpperInvariant(), UniqueIp());

        response.Id.Should().Be(household.Id);
        response.Parents.Should().HaveCount(2);
        response.Parents.Single(p => p.Id == guestUid).IsOwner.Should().BeFalse();

        await using var db = _fixture.CreateContext();
        var invitation = await db.HouseholdInvitations.SingleAsync(i => i.HouseholdId == household.Id);
        invitation.IsAccepted.Should().BeTrue();
        var audit = await db.AuditLogs
            .Where(a => a.EventType == AuditEventType.ParentJoined && a.ActorId == guestUid)
            .ToListAsync();
        audit.Should().ContainSingle();
    }

    // ------------------------------------------------------------------
    // PUT /household/settings (API Spec §3.3)
    // ------------------------------------------------------------------

    [Fact]
    public async Task Update_settings_changes_name_and_default_currency()
    {
        var (household, owner) = await _fixture.SeedHouseholdAsync();
        var service = CreateService(_fixture);

        var result = await service.UpdateSettingsAsync(owner.Id, "The Azizi Family", "USD", UniqueIp());

        result.Should().BeOfType<UpdateSettingsResult.Ok>()
            .Which.Household.DisplayName.Should().Be("The Azizi Family");

        await using var db = _fixture.CreateContext();
        var reloaded = await db.Households.SingleAsync(h => h.Id == household.Id);
        reloaded.DefaultCurrencyKey.Should().Be("USD");
    }

    [Fact]
    public async Task Update_settings_rejects_unknown_currency_key()
    {
        var (_, owner) = await _fixture.SeedHouseholdAsync();
        var service = CreateService(_fixture);

        var result = await service.UpdateSettingsAsync(owner.Id, null, "XYZ", UniqueIp());

        result.Should().BeOfType<UpdateSettingsResult.ValidationFailed>();
    }

    [Fact]
    public async Task Update_settings_rejects_oversized_display_name()
    {
        var (_, owner) = await _fixture.SeedHouseholdAsync();
        var service = CreateService(_fixture);

        var result = await service.UpdateSettingsAsync(owner.Id, new string('a', 61), "USD", UniqueIp());

        result.Should().BeOfType<UpdateSettingsResult.ValidationFailed>();
    }

    // ------------------------------------------------------------------
    // DELETE /household (API Spec §3.4)
    // ------------------------------------------------------------------

    [Fact]
    public async Task Owner_can_delete_household_and_audit_survives()
    {
        var (household, owner) = await _fixture.SeedHouseholdAsync();
        var service = CreateService(_fixture);

        var result = await service.DeleteAsync(owner.Id, UniqueIp());

        result.Should().BeOfType<DeleteHouseholdResult.Deleted>();

        await using var db = _fixture.CreateContext();
        (await db.Households.AnyAsync(h => h.Id == household.Id)).Should().BeFalse();
        (await db.Parents.AnyAsync(p => p.Id == owner.Id)).Should().BeFalse();
        var audit = await db.AuditLogs
            .Where(a => a.EventType == AuditEventType.HouseholdDeleted && a.HouseholdId == household.Id)
            .ToListAsync();
        audit.Should().ContainSingle();
    }

    [Fact]
    public async Task Non_owner_delete_is_rejected()
    {
        var (household, owner) = await _fixture.SeedHouseholdAsync();
        var second = await _fixture.SeedSecondParentAsync(household.Id);
        var service = CreateService(_fixture);

        var result = await service.DeleteAsync(second.Id, UniqueIp());

        result.Should().BeOfType<DeleteHouseholdResult.OwnerOnly>();

        await using var db = _fixture.CreateContext();
        (await db.Households.AnyAsync(h => h.Id == household.Id)).Should().BeTrue();
    }

    // ------------------------------------------------------------------
    // PUT /household/parents/me/pin (API Spec §4.1)
    // ------------------------------------------------------------------

    [Fact]
    public async Task First_time_pin_set_succeeds_without_current_pin()
    {
        var (_, owner) = await _fixture.SeedHouseholdAsync();
        var service = CreateService(_fixture);

        var result = await service.SetMyPinAsync(owner.Id, currentPin: null, newPin: "4321", UniqueIp());

        result.Should().BeOfType<SetParentPinResult.Ok>();

        await using var db = _fixture.CreateContext();
        var parent = await db.Parents.SingleAsync(p => p.Id == owner.Id);
        PinHasher.Verify("4321", parent.ParentPinHash).Should().BeTrue();
    }

    [Fact]
    public async Task First_time_pin_set_with_current_pin_is_validation_error()
    {
        var (_, owner) = await _fixture.SeedHouseholdAsync();
        var service = CreateService(_fixture);

        var result = await service.SetMyPinAsync(owner.Id, currentPin: "1234", newPin: "4321", UniqueIp());

        result.Should().BeOfType<SetParentPinResult.ValidationFailed>();
    }

    [Fact]
    public async Task Pin_change_requires_matching_current_pin()
    {
        var (_, owner) = await _fixture.SeedHouseholdAsync();
        var service = CreateService(_fixture);
        await service.SetMyPinAsync(owner.Id, null, "4321", UniqueIp());

        var wrong = await service.SetMyPinAsync(owner.Id, "9999", "5555", UniqueIp());
        var missing = await service.SetMyPinAsync(owner.Id, null, "5555", UniqueIp());
        var right = await service.SetMyPinAsync(owner.Id, "4321", "5555", UniqueIp());

        wrong.Should().BeOfType<SetParentPinResult.InvalidCredentials>();
        missing.Should().BeOfType<SetParentPinResult.ValidationFailed>();
        right.Should().BeOfType<SetParentPinResult.Ok>();
    }
}
