using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PocketMoney.Application;
using PocketMoney.Application.Contract;
using PocketMoney.Global;
using Xunit;

namespace PocketMoney.Api.Test;

/// <summary>
/// Phase 2 integration tests: parent invitation flow (API Spec §3.5–3.7,
/// SDS §5) against real PostgreSQL.
/// UIDs are unique per test — the database persists across the run.
/// </summary>
[Collection("database")]
public class InvitationIntegrationTests
{
    private readonly DatabaseFixture _fixture;
    private static readonly FakeTimeProvider Time = new();

    public InvitationIntegrationTests(DatabaseFixture fixture) => _fixture = fixture;

    private (InvitationService Service, RecordingDispatcher Mail) CreateService()
    {
        var db = _fixture.CreateContext();
        var mail = new RecordingDispatcher();
        return (new InvitationService(db, new AuditService(db), mail, Time), mail);
    }

    private static string UniqueIp() =>
        $"10.{Random.Shared.Next(1, 254)}.{Random.Shared.Next(1, 254)}.{Random.Shared.Next(1, 254)}";

    private sealed class RecordingDispatcher : IInvitationEmailDispatcher
    {
        public readonly List<(string Email, string Token)> Sent = [];
        public Task DispatchAsync(string invitedEmail, string token, CancellationToken ct = default)
        {
            Sent.Add((invitedEmail, token));
            return Task.CompletedTask;
        }
    }

    // ------------------------------------------------------------------
    // POST /household/invitations (API Spec §3.5)
    // ------------------------------------------------------------------

    [Fact]
    public async Task Create_invitation_persists_hash_and_dispatches_only_raw_token()
    {
        var (household, owner) = await _fixture.SeedHouseholdAsync();
        var (service, mail) = CreateService();

        var result = await service.CreateAsync(owner.Id, "Invited@Test.local ", UniqueIp());

        var created = result.Should().BeOfType<CreateInvitationResult.Created>().Which;
        created.Invitation.ExpiresAt.Should().Be(Time.GetUtcNow().AddDays(Constants.Invitation.ExpiryDays));

        mail.Sent.Should().ContainSingle();
        mail.Sent[0].Email.Should().Be("invited@test.local"); // trimmed + lowercased

        await using var db = _fixture.CreateContext();
        var stored = await db.HouseholdInvitations.SingleAsync(i => i.HouseholdId == household.Id);
        stored.InvitedEmail.Should().Be("invited@test.local");
        stored.TokenHash.Should().Be(InvitationTokens.Hash(mail.Sent[0].Token));
    }

    [Fact]
    public async Task Create_invitation_rejects_when_cap_reached()
    {
        var (household, owner) = await _fixture.SeedHouseholdAsync();
        await _fixture.SeedSecondParentAsync(household.Id);
        var (service, _) = CreateService();

        var result = await service.CreateAsync(owner.Id, "x@test.local", UniqueIp());

        result.Should().BeOfType<CreateInvitationResult.ParentCapReached>();
    }

    [Fact]
    public async Task Create_invitation_rejects_when_one_is_pending()
    {
        var (household, owner) = await _fixture.SeedHouseholdAsync();
        await _fixture.SeedPendingInvitationAsync(household.Id, owner.Id);
        var (service, _) = CreateService();

        var result = await service.CreateAsync(owner.Id, "other@test.local", UniqueIp());

        result.Should().BeOfType<CreateInvitationResult.InvitationPending>();
    }

    [Fact]
    public async Task Create_invitation_rejects_malformed_email()
    {
        var (_, owner) = await _fixture.SeedHouseholdAsync();
        var (service, mail) = CreateService();

        var result = await service.CreateAsync(owner.Id, "not-an-email", UniqueIp());

        result.Should().BeOfType<CreateInvitationResult.ValidationFailed>();
        mail.Sent.Should().BeEmpty(); // nothing dispatched on validation failure
    }

    // ------------------------------------------------------------------
    // POST /household/invitations/accept (API Spec §3.6)
    // ------------------------------------------------------------------

    [Fact]
    public async Task Accept_links_firebase_uid_to_inviting_household()
    {
        var guestUid = Guid.NewGuid().ToString("D");
        var (household, owner) = await _fixture.SeedHouseholdAsync();
        var (_, rawToken) = await _fixture.SeedPendingInvitationAsync(household.Id, owner.Id);
        var (service, _) = CreateService();

        var result = await service.AcceptAsync(guestUid, "guest@test.local", rawToken, UniqueIp());

        result.Should().BeOfType<AcceptInvitationResult.Accepted>()
            .Which.Household.HouseholdId.Should().Be(household.Id);

        await using var db = _fixture.CreateContext();
        var joined = await db.Parents.SingleAsync(p => p.Id == guestUid);
        joined.HouseholdId.Should().Be(household.Id);
        var invitation = await db.HouseholdInvitations.SingleAsync(i => i.HouseholdId == household.Id);
        invitation.IsAccepted.Should().BeTrue();
    }

    [Fact]
    public async Task Accept_rejects_unknown_or_malformed_token()
    {
        var guestUid = Guid.NewGuid().ToString("D");
        var (household, owner) = await _fixture.SeedHouseholdAsync();
        var (service, _) = CreateService();

        var unknown = await service.AcceptAsync(guestUid, null, InvitationTokens.Generate(), UniqueIp());
        var malformed = await service.AcceptAsync(guestUid, null, "short-token", UniqueIp());

        unknown.Should().BeOfType<AcceptInvitationResult.InvitationInvalid>();
        malformed.Should().BeOfType<AcceptInvitationResult.ValidationFailed>();
    }

    [Fact]
    public async Task Accept_rejects_expired_invitation()
    {
        var guestUid = Guid.NewGuid().ToString("D");
        var (household, owner) = await _fixture.SeedHouseholdAsync();
        var (_, rawToken) = await _fixture.SeedPendingInvitationAsync(household.Id, owner.Id);

        // Force expiry in the DB, then accept after the deadline.
        await using (var db = _fixture.CreateContext())
        {
            var invitation = await db.HouseholdInvitations.SingleAsync(i => i.HouseholdId == household.Id);
            invitation.ExpiresAt = Time.GetUtcNow().AddMinutes(-1);
            await db.SaveChangesAsync();
        }
        var (service, _) = CreateService();

        var result = await service.AcceptAsync(guestUid, null, rawToken, UniqueIp());

        result.Should().BeOfType<AcceptInvitationResult.InvitationExpired>();
    }

    [Fact]
    public async Task Accept_rejects_uid_already_in_a_household()
    {
        var guestUid = Guid.NewGuid().ToString("D");
        var (household, owner) = await _fixture.SeedHouseholdAsync();
        var (_, rawToken) = await _fixture.SeedPendingInvitationAsync(household.Id, owner.Id);
        var otherHousehold = await _fixture.SeedHouseholdAsync(ownerUid: guestUid); // guest already belongs somewhere
        var (service, _) = CreateService();

        var result = await service.AcceptAsync(guestUid, null, rawToken, UniqueIp());

        result.Should().BeOfType<AcceptInvitationResult.AlreadyInHousehold>();
        otherHousehold.Household.Id.Should().NotBe(household.Id);
    }

    [Fact]
    public async Task Accept_rechecks_cap_inside_transaction()
    {
        var guestUid = Guid.NewGuid().ToString("D");
        var (household, owner) = await _fixture.SeedHouseholdAsync();
        var (_, rawToken) = await _fixture.SeedPendingInvitationAsync(household.Id, owner.Id);
        await _fixture.SeedSecondParentAsync(household.Id); // cap filled after invitation was sent
        var (service, _) = CreateService();

        var result = await service.AcceptAsync(guestUid, null, rawToken, UniqueIp());

        result.Should().BeOfType<AcceptInvitationResult.ParentCapReached>();

        await using var db = _fixture.CreateContext();
        var count = await db.Parents.CountAsync(p => p.HouseholdId == household.Id);
        count.Should().Be(Constants.MaxParentsPerHousehold); // never 3
    }

    // ------------------------------------------------------------------
    // DELETE /household/invitations/{id} (API Spec §3.7)
    // ------------------------------------------------------------------

    [Fact]
    public async Task Sender_can_cancel_pending_invitation()
    {
        var (household, owner) = await _fixture.SeedHouseholdAsync();
        var (invitation, _) = await _fixture.SeedPendingInvitationAsync(household.Id, owner.Id);
        var (service, _) = CreateService();

        var result = await service.CancelAsync(owner.Id, invitation.Id, UniqueIp());

        result.Should().BeOfType<CancelInvitationResult.Cancelled>();

        await using var db = _fixture.CreateContext();
        (await db.HouseholdInvitations.AnyAsync(i => i.HouseholdId == household.Id)).Should().BeFalse(); // physically deleted
        (await db.AuditLogs.AnyAsync(a => a.EventType == AuditEventType.ParentInvitationCancelled
            && a.HouseholdId == household.Id)).Should().BeTrue();
    }

    [Fact]
    public async Task Non_sender_cancel_is_rejected()
    {
        var (household, owner) = await _fixture.SeedHouseholdAsync();
        var second = await _fixture.SeedSecondParentAsync(household.Id);
        var (invitation, _) = await _fixture.SeedPendingInvitationAsync(household.Id, owner.Id);
        var (service, _) = CreateService();

        var result = await service.CancelAsync(second.Id, invitation.Id, UniqueIp());

        result.Should().BeOfType<CancelInvitationResult.SenderOnly>();
    }

    [Fact]
    public async Task Cancel_outside_household_is_not_found()
    {
        var (household, owner) = await _fixture.SeedHouseholdAsync();
        var (invitation, _) = await _fixture.SeedPendingInvitationAsync(household.Id, owner.Id);
        var (_, otherParent) = await _fixture.SeedHouseholdAsync();
        var (service, _) = CreateService();

        var result = await service.CancelAsync(otherParent.Id, invitation.Id, UniqueIp());

        result.Should().BeOfType<CancelInvitationResult.NotFound>();
    }
}
