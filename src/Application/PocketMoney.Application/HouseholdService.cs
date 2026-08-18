using Microsoft.EntityFrameworkCore;
using PocketMoney.Application.Contract;
using PocketMoney.Application.Model.Common;
using PocketMoney.Application.Model.Households;
using PocketMoney.Domain.Entities;
using PocketMoney.Global;
using PocketMoney.Persistence.Data;

namespace PocketMoney.Application;

/// <summary>
/// Household operations (API Spec §3, §4.1, SDS §7.1.2).
///
/// <see cref="GetOrCreateAsync"/> doubles as Auto-Registration: a household
/// has no creation endpoint — the first Firebase sign-in creates it, and an
/// invited parent's first sign-in joins the inviting household.
/// </summary>
public sealed class HouseholdService(
    PocketMoneyDbContext db,
    IAuditService audit,
    TimeProvider time) : IHouseholdService
{
    public async Task<HouseholdResponse> GetOrCreateAsync(
        string firebaseUid, string? email, string ipAddress, CancellationToken ct = default)
    {
        var now = time.GetUtcNow();
        var parent = await db.Parents.FirstOrDefaultAsync(p => p.Id == firebaseUid, ct);

        if (parent is null)
        {
            parent = await AutoRegisterAsync(firebaseUid, email, now, ipAddress, ct);
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException)
            {
                // Concurrent auto-registration for the same Firebase UID: the
                // PK on parents.id rejects the second insert. Reload and serve.
                db.ChangeTracker.Clear();
                parent = await db.Parents.FirstAsync(p => p.Id == firebaseUid, ct);
            }
        }

        return await BuildResponseAsync(parent.HouseholdId, firebaseUid, now, ct);
    }

    /// <summary>
    /// SDS §7.1.2 Auto-Registration:
    /// first parent → new household (owner); invited parent (verified email
    /// matches a pending invitation) → joins the inviting household.
    /// </summary>
    private async Task<Parent> AutoRegisterAsync(
        string firebaseUid, string? email, DateTimeOffset now, string ipAddress, CancellationToken ct)
    {
        Household? invitedHousehold = null;

        if (!string.IsNullOrWhiteSpace(email))
        {
            var invitation = await db.HouseholdInvitations
                .Where(i => !i.IsAccepted && i.ExpiresAt > now)
                .Where(i => EF.Functions.ILike(i.InvitedEmail, email))
                .OrderBy(i => i.CreatedAt)
                .FirstOrDefaultAsync(ct);

            if (invitation is not null)
            {
                var parentCount = await db.Parents.CountAsync(p => p.HouseholdId == invitation.HouseholdId, ct);
                if (parentCount < Constants.MaxParentsPerHousehold)
                {
                    invitation.IsAccepted = true;
                    invitedHousehold = await db.Households.FirstAsync(h => h.Id == invitation.HouseholdId, ct);
                }
            }
        }

        var household = invitedHousehold ?? new Household();
        if (invitedHousehold is null)
            db.Households.Add(household);

        var parent = new Parent
        {
            Id = firebaseUid,
            HouseholdId = household.Id,
            Email = email ?? string.Empty,
            CreatedAt = now,
        };
        db.Parents.Add(parent);

        if (invitedHousehold is not null)
            audit.Log(household.Id, firebaseUid, ActorType.Parent, AuditEventType.ParentJoined, ipAddress: ipAddress);

        return parent;
    }

    public async Task<UpdateSettingsResult> UpdateSettingsAsync(
        string firebaseUid, string? displayName, string? defaultCurrencyKey,
        string ipAddress, CancellationToken ct = default)
    {
        var parent = await db.Parents.FirstOrDefaultAsync(p => p.Id == firebaseUid, ct);
        if (parent is null)
            return new UpdateSettingsResult.ParentUnknown();

        displayName = displayName?.Trim();
        if (!string.IsNullOrEmpty(displayName) &&
            (displayName.Length > Constants.Household.DisplayNameMaxLength || !InputValidation.IsValidDisplayName(displayName)))
        {
            return new UpdateSettingsResult.ValidationFailed(
                $"displayName must be ≤ {Constants.Household.DisplayNameMaxLength} characters (letters, digits, space, -, ', .).");
        }

        defaultCurrencyKey = defaultCurrencyKey?.Trim();
        var currency = CurrencyType.Parse(defaultCurrencyKey ?? string.Empty);
        if (currency is null)
        {
            return new UpdateSettingsResult.ValidationFailed(
                "defaultCurrencyKey must be a supported currency key.");
        }

        var household = await db.Households.FirstAsync(h => h.Id == parent.HouseholdId, ct);
        household.DisplayName = string.IsNullOrEmpty(displayName) ? null : displayName;
        household.DefaultCurrencyKey = currency.Key;

        audit.Log(household.Id, firebaseUid, ActorType.Parent,
            AuditEventType.HouseholdSettingsUpdated,
            new { household.DisplayName, household.DefaultCurrencyKey }, ipAddress);

        await db.SaveChangesAsync(ct);
        return new UpdateSettingsResult.Ok(await BuildResponseAsync(household.Id, firebaseUid, time.GetUtcNow(), ct));
    }

    public async Task<DeleteHouseholdResult> DeleteAsync(
        string firebaseUid, string ipAddress, CancellationToken ct = default)
    {
        var parent = await db.Parents.FirstOrDefaultAsync(p => p.Id == firebaseUid, ct);
        if (parent is null)
            return new DeleteHouseholdResult.ParentUnknown();

        var householdId = parent.HouseholdId;

        // Owner = earliest Parent.CreatedAt in the household (SDS §2.3, API Spec §3.4).
        var ownerId = await db.Parents
            .Where(p => p.HouseholdId == householdId)
            .OrderBy(p => p.CreatedAt).ThenBy(p => p.Id)
            .Select(p => p.Id)
            .FirstAsync(ct);
        if (ownerId != firebaseUid)
            return new DeleteHouseholdResult.OwnerOnly();

        // Audit row has no FK to households, so it survives the deletion
        // (FR-P1: audit_logs persist). Logged BEFORE the delete so it commits
        // in the same transaction.
        audit.Log(householdId, firebaseUid, ActorType.Parent, AuditEventType.HouseholdDeleted, ipAddress: ipAddress);

        // Physical deletion of the tenant subtree (SDS §10). invitations →
        // parents is Restrict, so invitations go first; parents/children/
        // transactions cascade from the household delete.
        await db.HouseholdInvitations.Where(i => i.HouseholdId == householdId).ExecuteDeleteAsync(ct);
        await db.Households.Where(h => h.Id == householdId).ExecuteDeleteAsync(ct);
        await db.SaveChangesAsync(ct);

        return new DeleteHouseholdResult.Deleted();
    }

    public async Task<SetParentPinResult> SetMyPinAsync(
        string firebaseUid, string? currentPin, string? newPin,
        string ipAddress, CancellationToken ct = default)
    {
        var parent = await db.Parents.FirstOrDefaultAsync(p => p.Id == firebaseUid, ct);
        if (parent is null)
            return new SetParentPinResult.ParentUnknown();

        newPin = newPin?.Trim();
        currentPin = currentPin?.Trim();
        var hasPin = parent.ParentPinHash.Length > 0;

        if (!InputValidation.IsValidPin(newPin))
            return new SetParentPinResult.ValidationFailed("newPin must be exactly 4 digits.");

        // API Spec §4.1: currentPin absent on first-time set; supplied with
        // hasPin=false (or omitted with hasPin=true) is a 400.
        if (!hasPin && !string.IsNullOrEmpty(currentPin))
            return new SetParentPinResult.ValidationFailed("currentPin must be omitted on the first-time PIN set.");

        if (hasPin && string.IsNullOrEmpty(currentPin))
            return new SetParentPinResult.ValidationFailed("currentPin is required when a PIN is already set.");

        if (hasPin && !PinHasher.Verify(currentPin!, parent.ParentPinHash))
            return new SetParentPinResult.InvalidCredentials();

        parent.ParentPinHash = PinHasher.Hash(newPin!);
        audit.Log(parent.HouseholdId, firebaseUid, ActorType.Parent, AuditEventType.ParentPinChanged, ipAddress: ipAddress);
        await db.SaveChangesAsync(ct);

        return new SetParentPinResult.Ok();
    }

    // ------------------------------------------------------------------

    /// <summary>Assembles the GET /household payload (API Spec §3.2 shape).</summary>
    internal async Task<HouseholdResponse> BuildResponseAsync(
        Guid householdId, string callerUid, DateTimeOffset now, CancellationToken ct)
    {
        var household = await db.Households.FirstAsync(h => h.Id == householdId, ct);

        var parents = await db.Parents
            .Where(p => p.HouseholdId == householdId)
            .OrderBy(p => p.CreatedAt).ThenBy(p => p.Id)
            .ToListAsync(ct);

        // Owner = earliest Parent.CreatedAt (SDS §2.3).
        var ownerId = parents[0].Id;

        var pendingInvitations = await db.HouseholdInvitations
            .Where(i => i.HouseholdId == householdId && !i.IsAccepted && i.ExpiresAt > now)
            .OrderBy(i => i.CreatedAt)
            .Select(i => new PendingInvitationDto(i.Id, i.InvitedEmail, i.ExpiresAt))
            .ToListAsync(ct);

        var children = await db.Children
            .Where(c => c.HouseholdId == householdId)
            .OrderBy(c => c.CreatedAt)
            .ToListAsync(ct);

        return new HouseholdResponse(
            household.Id,
            household.DisplayName,
            CurrencyDto.From(CurrencyType.Parse(household.DefaultCurrencyKey)!),
            Constants.MaxParentsPerHousehold,
            pendingInvitations,
            household.CreatedAt,
            parents.Select(p => new ParentDto(p.Id, p.DisplayName, p.Id == ownerId, p.ParentPinHash.Length > 0)).ToList(),
            children.Select(c =>
            {
                var timedLock = c.LockedUntil is { } until && !c.IsPermanentlyLocked && until > now;
                return new ChildSummaryDto(
                    c.Id, c.AccountId, c.DisplayName,
                    CurrencyDto.From(CurrencyType.Parse(c.CurrencyKey)!),
                    c.CurrentBalance,
                    Locked: c.IsPermanentlyLocked || timedLock,
                    LockedUntil: timedLock ? c.LockedUntil : null);
            }).ToList());
    }
}
