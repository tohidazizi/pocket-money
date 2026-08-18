using System.Data;
using Microsoft.EntityFrameworkCore;
using PocketMoney.Application.Contract;
using PocketMoney.Application.Model.Households;
using PocketMoney.Domain.Entities;
using PocketMoney.Global;
using PocketMoney.Persistence.Data;

namespace PocketMoney.Application;

/// <summary>Parent invitation flow (API Spec §3.5–3.7, SDS §5).</summary>
public sealed class InvitationService(
    PocketMoneyDbContext db,
    IAuditService audit,
    IInvitationEmailDispatcher emailDispatcher,
    TimeProvider time) : IInvitationService
{
    public async Task<CreateInvitationResult> CreateAsync(
        string firebaseUid, string? email, string ipAddress, CancellationToken ct = default)
    {
        var parent = await db.Parents.FirstOrDefaultAsync(p => p.Id == firebaseUid, ct);
        if (parent is null)
            return new CreateInvitationResult.ParentUnknown();

        email = email?.Trim().ToLowerInvariant();
        if (!InputValidation.IsValidEmail(email))
            return new CreateInvitationResult.ValidationFailed(
                $"email must be a valid address (RFC-5322 shape, ≤ {Constants.Parent.EmailMaxLength} characters).");

        var householdId = parent.HouseholdId;
        var now = time.GetUtcNow();

        var parentCount = await db.Parents.CountAsync(p => p.HouseholdId == householdId, ct);
        if (parentCount >= Constants.MaxParentsPerHousehold)
            return new CreateInvitationResult.ParentCapReached();

        var pendingExists = await db.HouseholdInvitations
            .AnyAsync(i => i.HouseholdId == householdId && !i.IsAccepted && i.ExpiresAt > now, ct);
        if (pendingExists)
            return new CreateInvitationResult.InvitationPending();

        var token = InvitationTokens.Generate();
        var invitation = new HouseholdInvitation
        {
            HouseholdId = householdId,
            InvitedEmail = email!,
            TokenHash = InvitationTokens.Hash(token),
            InvitedByParentId = firebaseUid,
            ExpiresAt = now.AddDays(Constants.Invitation.ExpiryDays),
            CreatedAt = now,
        };
        db.HouseholdInvitations.Add(invitation);
        audit.Log(householdId, firebaseUid, ActorType.Parent,
            AuditEventType.ParentInvited, new { email }, ipAddress);
        await db.SaveChangesAsync(ct);

        // Dispatch AFTER commit: the email must not reference an invitation
        // that rolled back. The raw token travels only inside this email.
        await emailDispatcher.DispatchAsync(invitation.InvitedEmail, token, ct);

        return new CreateInvitationResult.Created(
            new InvitationResponse(invitation.Id, invitation.ExpiresAt));
    }

    public async Task<AcceptInvitationResult> AcceptAsync(
        string firebaseUid, string? email, string? token, string ipAddress, CancellationToken ct = default)
    {
        token = token?.Trim();
        if (string.IsNullOrEmpty(token)
            || token.Length != Constants.Invitation.TokenBytes * 2
            || !token.All(Uri.IsHexDigit))
        {
            return new AcceptInvitationResult.ValidationFailed(
                "token must be the invitation token from the emailed link.");
        }

        var tokenHash = InvitationTokens.Hash(token);
        var now = time.GetUtcNow();

        // SDS §5 step 5: cap + one-household re-checks INSIDE the acceptance
        // transaction. Serializable closes the race where two outstanding
        // invitations could otherwise produce 3 parents.
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        try
        {
            var invitation = await db.HouseholdInvitations
                .FirstOrDefaultAsync(i => i.TokenHash == tokenHash, ct);

            if (invitation is null || invitation.IsAccepted)
                return new AcceptInvitationResult.InvitationInvalid();
            if (invitation.ExpiresAt <= now)
                return new AcceptInvitationResult.InvitationExpired();

            // One parent — one household, ever (SDS §2.4), including
            // households auto-created at first sign-in (API Spec §3.6).
            var alreadyInHousehold = await db.Parents.AnyAsync(p => p.Id == firebaseUid, ct);
            if (alreadyInHousehold)
                return new AcceptInvitationResult.AlreadyInHousehold();

            var parentCount = await db.Parents
                .CountAsync(p => p.HouseholdId == invitation.HouseholdId, ct);
            if (parentCount >= Constants.MaxParentsPerHousehold)
                return new AcceptInvitationResult.ParentCapReached();

            invitation.IsAccepted = true;
            db.Parents.Add(new Parent
            {
                Id = firebaseUid,
                HouseholdId = invitation.HouseholdId,
                Email = email ?? string.Empty,
                CreatedAt = now,
            });
            audit.Log(invitation.HouseholdId, firebaseUid, ActorType.Parent,
                AuditEventType.ParentJoined, ipAddress: ipAddress);

            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            var household = await db.Households.FirstAsync(h => h.Id == invitation.HouseholdId, ct);
            return new AcceptInvitationResult.Accepted(
                new AcceptInvitationResponse(household.Id, household.DisplayName));
        }
        catch (DbUpdateException)
        {
            // Serialization conflict with a concurrent acceptance, or a
            // duplicate parent insert — the business outcome is "the slot is
            // gone"; surface the conflict rather than a 500.
            await tx.RollbackAsync(CancellationToken.None);
            return new AcceptInvitationResult.ParentCapReached();
        }
    }

    public async Task<CancelInvitationResult> CancelAsync(
        string firebaseUid, Guid invitationId, string ipAddress, CancellationToken ct = default)
    {
        var parent = await db.Parents.FirstOrDefaultAsync(p => p.Id == firebaseUid, ct);
        if (parent is null)
            return new CancelInvitationResult.ParentUnknown();

        var now = time.GetUtcNow();

        // 404 covers "no such invitation in the caller's household (or
        // already accepted/expired)" — checked before sender-only so an
        // outsider learns nothing (API Spec §3.7).
        var invitation = await db.HouseholdInvitations.FirstOrDefaultAsync(
            i => i.Id == invitationId && i.HouseholdId == parent.HouseholdId, ct);
        if (invitation is null || invitation.IsAccepted || invitation.ExpiresAt <= now)
            return new CancelInvitationResult.NotFound();

        if (invitation.InvitedByParentId != firebaseUid)
            return new CancelInvitationResult.SenderOnly();

        // Physical delete; the append-only AuditLog keeps the record (SDS §5 step 7).
        db.HouseholdInvitations.Remove(invitation);
        audit.Log(parent.HouseholdId, firebaseUid, ActorType.Parent,
            AuditEventType.ParentInvitationCancelled, new { invitationId }, ipAddress);
        await db.SaveChangesAsync(ct);

        return new CancelInvitationResult.Cancelled();
    }
}
