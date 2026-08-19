using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using PocketMoney.Application;
using PocketMoney.Application.Contract;
using PocketMoney.Authentication;
using PocketMoney.Persistence.Data;

namespace PocketMoney.Api.Hubs;

/// <summary>
/// Real-time ledger hub (SDS §7.2) at <c>/hubs/ledger</c>.
/// Group naming: <c>child_{id}</c>. Pushes:
/// * <c>OnBalanceUpdated</c>(newBalance, transaction) after CREDIT/DEBIT (SDS §4)
/// * <c>OnBalanceUpdated</c>(balance, { currencyKey }) after currency change (SDS §2.1.1)
/// </summary>
[Authorize]
public sealed class LedgerHub(ILogger<LedgerHub> logger, PocketMoneyDbContext db) : Hub
{
    /// <summary>
    /// Joins the caller to a child's update group. Data isolation is enforced
    /// here (SDS §7.2): the child must exist and either the caller IS that
    /// child (Child scheme) or the caller is a parent in the same household
    /// (Firebase scheme). Failed joins are rejected silently — no group
    /// membership, no push traffic, no enumeration oracle.
    /// </summary>
    public async Task JoinChildGroup(string childId)
    {
        if (!Guid.TryParse(childId, out var id))
        {
            logger.LogInformation("SignalR JoinChildGroup rejected: malformed childId");
            return;
        }

        var child = await db.Children.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == id);
        if (child is null)
            return;

        var callerChildId = Context.User?.FindFirst(ChildJwtTokenIssuer.ClaimChildId)?.Value;
        if (callerChildId == id.ToString("D"))
        {
            // The child themselves — always allowed for their own group.
            await Groups.AddToGroupAsync(Context.ConnectionId, $"child_{id}");
            return;
        }

        var firebaseUid = Context.User?.FindFirst(FirebaseAuthDefaults.UserIdClaim)?.Value;
        if (firebaseUid is not null)
        {
            var sameHousehold = await db.Parents.AsNoTracking()
                .AnyAsync(p => p.Id == firebaseUid && p.HouseholdId == child.HouseholdId);
            if (sameHousehold)
            {
                await Groups.AddToGroupAsync(Context.ConnectionId, $"child_{id}");
                return;
            }
        }

        logger.LogInformation("SignalR JoinChildGroup rejected: child {ChildId} outside caller's household", id);
    }
}

/// <summary>
/// SignalR-backed <see cref="ILedgerPushService"/> (SDS §7.2).
/// Registered as singleton — <see cref="IHubContext{T}"/> is singleton-safe.
/// </summary>
public sealed class SignalRLedgerPushService(
    IHubContext<LedgerHub> hub) : ILedgerPushService
{
    public async Task PushTransactionAsync(
        Guid childId, decimal newBalance, object transactionDto, CancellationToken ct = default)
    {
        await hub.Clients.Group($"child_{childId}")
            .SendAsync("OnBalanceUpdated", newBalance, transactionDto, ct);
    }

    public async Task PushCurrencyChangedAsync(
        Guid childId, decimal balance, string currencyKey, CancellationToken ct = default)
    {
        await hub.Clients.Group($"child_{childId}")
            .SendAsync("OnBalanceUpdated", balance, new { currencyKey }, ct);
    }
}
