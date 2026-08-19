using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using PocketMoney.Application.Contract;
using PocketMoney.Application.Model.Common;
using PocketMoney.Application.Model.Children;
using PocketMoney.Domain.Entities;
using PocketMoney.Global;
using PocketMoney.Persistence.Data;

namespace PocketMoney.Application;

/// <summary>
/// Child profile management (API Spec §5.1–5.5).
/// </summary>
public sealed class ChildService(
    PocketMoneyDbContext db,
    IAuditService audit,
    TimeProvider time,
    ILedgerPushService? ledgerPush = null) : IChildService
{
    /// <summary>Uniqueness retries before deferring to the unique index (SDS §3.1).</summary>
    private const int AccountIdRetries = 5;

    public async Task<CreateChildResult> CreateAsync(
        string parentUid, string? displayName, string ipAddress, CancellationToken ct = default)
    {
        var parent = await db.Parents.FirstOrDefaultAsync(p => p.Id == parentUid, ct);
        if (parent is null)
            return new CreateChildResult.ParentUnknown();

        displayName = displayName?.Trim();
        if (string.IsNullOrEmpty(displayName)
            || displayName.Length > Constants.Child.DisplayNameMaxLength
            || !InputValidation.IsValidDisplayName(displayName))
        {
            return new CreateChildResult.ValidationFailed(
                $"displayName is required and must be ≤ {Constants.Child.DisplayNameMaxLength} characters (letters, digits, space, -, ', .).");
        }

        var householdId = parent.HouseholdId;
        var childCount = await db.Children.CountAsync(c => c.HouseholdId == householdId, ct);
        if (childCount >= Constants.Child.ChildrenMax)
            return new CreateChildResult.ChildrenMaxReached();

        var household = await db.Households.FirstAsync(h => h.Id == householdId, ct);

        // Initial random PIN — returned ONLY in this creation response (SDS §7.1).
        var initialPin = RandomNumberGenerator.GetInt32(0, 10_000).ToString("D4");

        var child = new Child
        {
            AccountId = await GenerateUniqueAccountIdAsync(ct),
            HouseholdId = householdId,
            DisplayName = displayName,
            PinHash = PinHasher.Hash(initialPin),
            CurrencyKey = household.DefaultCurrencyKey, // inherits (SDS §7.1)
            CreatorId = parentUid,
            CreatedAt = time.GetUtcNow(),
        };
        db.Children.Add(child);

        audit.Log(householdId, parentUid, ActorType.Parent, AuditEventType.ChildCreated,
            new { child.AccountId, child.DisplayName }, ipAddress);
        await db.SaveChangesAsync(ct);

        return new CreateChildResult.Created(new CreateChildResponse(
            child.Id, child.AccountId, child.DisplayName, initialPin,
            child.CurrencyKey, child.CurrentBalance));
    }

    public async Task<ChildActionResult> ResetPinAsync(
        string parentUid, Guid childId, string? newPin, string ipAddress, CancellationToken ct = default)
    {
        newPin = newPin?.Trim();
        if (!InputValidation.IsValidPin(newPin))
            return new ChildActionResult.ValidationFailed("newPin must be exactly 4 digits.");

        var child = await FindInHouseholdAsync(parentUid, childId, ct);
        if (child is null)
            return new ChildActionResult.NotFound();

        // API Spec §5.2: a locked account must be unlocked first (§5.3) —
        // PIN change never clears a lock.
        if (IsLocked(child, time.GetUtcNow()))
            return new ChildActionResult.AccountLocked();

        child.PinHash = PinHasher.Hash(newPin!);
        child.SecurityStamp = Guid.NewGuid(); // invalidates 365-day tokens (SDS §3.2)

        audit.Log(child.HouseholdId, parentUid, ActorType.Parent, AuditEventType.ChildPinReset,
            new { childId = child.Id }, ipAddress);
        await db.SaveChangesAsync(ct);

        return new ChildActionResult.Ok();
    }

    public async Task<ChildActionResult> SetLockAsync(
        string parentUid, Guid childId, bool locked, string ipAddress, CancellationToken ct = default)
    {
        var child = await FindInHouseholdAsync(parentUid, childId, ct);
        if (child is null)
            return new ChildActionResult.NotFound();

        if (locked)
        {
            // Manual lock = same representation as a permanent ladder lock (SDS §3.4).
            child.LockedUntil = DateTimeOffset.MaxValue;
        }
        else
        {
            // Parent unlock: clears any lock (ladder or manual) and restarts
            // the failure ladder. Never changes the PIN (API Spec §5.3).
            child.LockedUntil = null;
            child.UnsuccessfulLoginAttempts = 0;
        }
        child.SecurityStamp = Guid.NewGuid(); // active child tokens die with 401 (SDS §3.2)

        audit.Log(child.HouseholdId, parentUid, ActorType.Parent,
            locked ? AuditEventType.ChildAccountLocked : AuditEventType.ChildAccountUnlocked,
            new { childId = child.Id }, ipAddress);
        await db.SaveChangesAsync(ct);

        return new ChildActionResult.Ok();
    }

    public async Task<ChangeCurrencyResult> ChangeCurrencyAsync(
        string parentUid, Guid childId, string? currencyKey, string ipAddress, CancellationToken ct = default)
    {
        currencyKey = currencyKey?.Trim();
        var currency = CurrencyType.Parse(currencyKey ?? string.Empty);
        if (currency is null)
            return new ChangeCurrencyResult.ValidationFailed("currencyKey must be a supported currency key.");

        var child = await FindInHouseholdAsync(parentUid, childId, ct);
        if (child is null)
            return new ChangeCurrencyResult.NotFound();

        // Balance carries over numerically — no conversion (API Spec §5.4).
        // Past ledger rows keep their snapshotted currency (SDS §4).
        child.CurrencyKey = currency.Key;

        audit.Log(child.HouseholdId, parentUid, ActorType.Parent, AuditEventType.ChildCurrencyChanged,
            new { childId = child.Id, currencyKey = currency.Key }, ipAddress);
        await db.SaveChangesAsync(ct);

        // Balance unchanged, new denomination — the child dashboard must
        // re-render the carried-over balance (SDS §7.2).
        if (ledgerPush is not null)
            await ledgerPush.PushCurrencyChangedAsync(child.Id, child.CurrentBalance, currency.Key, ct);

        return new ChangeCurrencyResult.Changed(new ChildCurrencyResponse(
            CurrencyDto.From(currency), child.CurrentBalance));
    }

    public async Task<ChildMeResult> GetMeAsync(Guid childId, CancellationToken ct = default)
    {
        var child = await db.Children.FirstOrDefaultAsync(c => c.Id == childId, ct);
        if (child is null)
            return new ChildMeResult.NotFound();

        return new ChildMeResult.Ok(new ChildMeResponse(
            child.DisplayName,
            child.CurrentBalance,
            CurrencyDto.From(CurrencyType.Parse(child.CurrencyKey)!)));
    }

    // ------------------------------------------------------------------

    /// <summary>Household-scoped child lookup — 404 for missing or foreign children (API Spec §1.3).</summary>
    private async Task<Child?> FindInHouseholdAsync(string parentUid, Guid childId, CancellationToken ct)
    {
        var parent = await db.Parents.FirstOrDefaultAsync(p => p.Id == parentUid, ct);
        if (parent is null)
            return null;

        return await db.Children
            .FirstOrDefaultAsync(c => c.Id == childId && c.HouseholdId == parent.HouseholdId, ct);
    }

    /// <summary>Any active lock: manual, permanent ladder, or unexpired timed tier (SDS §3.4).</summary>
    internal static bool IsLocked(Child child, DateTimeOffset now) =>
        child.IsPermanentlyLocked || (child.LockedUntil is { } until && until > now);

    /// <summary>
    /// Base-31 account ID with uniqueness retry (SDS §3.1); the unique index
    /// on children.account_id is the final guarantee.
    /// </summary>
    private async Task<string> GenerateUniqueAccountIdAsync(CancellationToken ct)
    {
        for (var i = 0; i < AccountIdRetries; i++)
        {
            var candidate = Base31Generator.GenerateAccountId();
            var exists = await db.Children.AnyAsync(c => c.AccountId == candidate, ct);
            if (!exists)
                return candidate;
        }

        throw new InvalidOperationException(
            $"Could not generate a unique account ID after {AccountIdRetries} attempts — Base-31 space exhausted?");
    }
}
