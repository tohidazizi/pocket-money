using Microsoft.EntityFrameworkCore;
using PocketMoney.Application.Contract;
using PocketMoney.Domain.Entities;
using PocketMoney.Global;
using PocketMoney.Persistence.Data;

namespace PocketMoney.Application;

/// <summary>
/// Child login (FR-C1) implementing SDS §3.3: attempt audit, global IP ban
/// ladder, child lockout ladder, 365-day token issue.
///
/// Design decision (SDS §3.3 faithful reading): EVERY failed attempt — wrong
/// PIN, unknown account, malformed input, or an attempt against an
/// already-locked account — flows through <see cref="HandleFailedLoginAsync"/>
/// (audit + IP-ban evaluation + ladder). Attempts during a lock therefore
/// count: the ladder is cumulative failures, and a locked account cannot be
/// hammered without tripping the IP ban (NFR-4). The lock check runs BEFORE
/// PIN verification so a locked account leaks no PIN-correctness signal.
/// </summary>
public sealed class ChildAuthService : IChildAuthService
{
    private readonly PocketMoneyDbContext _db;
    private readonly IChildTokenIssuer _tokenIssuer;
    private readonly TimeProvider _time;

    public ChildAuthService(PocketMoneyDbContext db, IChildTokenIssuer tokenIssuer, TimeProvider time)
    {
        _db = db;
        _tokenIssuer = tokenIssuer;
        _time = time;
    }

    public async Task<ChildLoginResult> LoginAsync(string? accountId, string? pin, ClientInfo clientInfo, CancellationToken ct = default)
    {
        var now = _time.GetUtcNow();

        // Layer 1 (SDS §10): active IP ban short-circuits everything.
        var activeBan = await _db.IpBans
            .FirstOrDefaultAsync(b => b.IpAddress == clientInfo.IpAddress && b.BannedUntil > now, ct);
        if (activeBan is not null)
            return new ChildLoginResult.IpBanned(activeBan.BannedUntil);

        // SDS §9.2: trim inputs; accountId normalized to uppercase before lookup.
        // The RAW (pre-normalization) value is captured for audit: SDS §3.3
        // stores accountId verbatim, even if invalid.
        var rawAccountId = accountId?.Trim() ?? string.Empty;
        accountId = rawAccountId.ToUpperInvariant();
        pin = pin?.Trim();

        var formatValid = Base31Generator.IsValid(accountId)
            && pin is not null && pin.Length == 4 && pin.All(char.IsDigit);

        if (!formatValid)
        {
            // Invalid input is still an audited attempt (SDS §3.3).
            await HandleFailedLoginAsync(rawAccountId, clientInfo, child: null, now, ct);
            return new ChildLoginResult.ValidationFailed("accountId must be a 5-character Base-31 code and pin must be exactly 4 digits.");
        }

        var child = await _db.Children.FirstOrDefaultAsync(c => c.AccountId == accountId, ct);
        if (child is null)
        {
            // Unknown account: audited + IP ban ladder, but no child lockout ladder.
            await HandleFailedLoginAsync(accountId!, clientInfo, child: null, now, ct);
            return new ChildLoginResult.InvalidCredentials();
        }

        // Lock state BEFORE PIN verification — "a correct PIN would still fail
        // until the tier expires" (API Spec §2.1), and no PIN-correctness
        // signal may leak from a locked account. Timed tiers expire on their
        // own (SDS §3.4). The attempt still counts (design decision above).
        if (child.IsPermanentlyLocked)
        {
            await HandleFailedLoginAsync(accountId!, clientInfo, child, now, ct);
            return new ChildLoginResult.PermanentlyLocked();
        }
        if (child.LockedUntil is { } lockedUntil && lockedUntil > now)
        {
            await HandleFailedLoginAsync(accountId!, clientInfo, child, now, ct);
            return new ChildLoginResult.Locked(lockedUntil);
        }

        if (!PinHasher.Verify(pin!, child.PinHash))
        {
            await HandleFailedLoginAsync(accountId!, clientInfo, child, now, ct);
            return new ChildLoginResult.InvalidCredentials();
        }

        // Success: audited, counter reset (SDS §2.1 Lockout), token issued.
        _db.LoginAttempts.Add(new LoginAttempt
        {
            AccountId = accountId!,
            IpAddress = clientInfo.IpAddress,
            HttpRequestInfo = clientInfo.HttpRequestInfo,
            IsSuccessful = true,
            CreatedAt = now,
        });
        child.UnsuccessfulLoginAttempts = 0;
        var token = _tokenIssuer.Issue(child.Id, child.HouseholdId, child.SecurityStamp);
        await _db.SaveChangesAsync(ct);

        return new ChildLoginResult.Success(token.Token, token.ExpiresAt, child.Id, child.AccountId, child.DisplayName);
    }

    /// <summary>SDS §3.3: audit + global IP ban ladder + child lockout ladder.</summary>
    private async Task HandleFailedLoginAsync(string accountId, ClientInfo clientInfo, Child? child, DateTimeOffset now, CancellationToken ct)
    {
        // 1. Log attempt (accountId stored verbatim, even if invalid — audit requirement)
        _db.LoginAttempts.Add(new LoginAttempt
        {
            AccountId = accountId,
            IpAddress = clientInfo.IpAddress,
            HttpRequestInfo = clientInfo.HttpRequestInfo,
            IsSuccessful = false,
            CreatedAt = now,
        });

        // 2. Check Global IP Ban threshold (NFR-4). The current attempt is not
        //    persisted yet, so it is counted explicitly (+1) — the ban fires on
        //    the threshold-th failure, not the one after it.
        var windowStart = now.AddHours(-Constants.IpBan.FailureWindowHours);
        var priorIpFailures = await _db.LoginAttempts
            .CountAsync(l => l.IpAddress == clientInfo.IpAddress && !l.IsSuccessful && l.CreatedAt >= windowStart, ct);

        if (priorIpFailures + 1 >= Constants.IpBan.FailureThreshold)
        {
            var existingBan = await _db.IpBans.FirstOrDefaultAsync(b => b.IpAddress == clientInfo.IpAddress, ct);
            var banCount = (existingBan?.BanCount ?? 0) + 1;

            var bannedUntil = banCount switch
            {
                1 => now.AddDays(Constants.IpBan.FirstBanDays),
                2 => now.AddDays(Constants.IpBan.SecondBanDays),
                _ => now.AddDays(Constants.IpBan.ThirdBanDays),
            };

            if (existingBan is not null)
            {
                existingBan.BanCount = banCount;
                existingBan.BannedUntil = bannedUntil;
                existingBan.UpdatedAt = now;
            }
            else
            {
                _db.IpBans.Add(new IpBan
                {
                    IpAddress = clientInfo.IpAddress,
                    BanCount = banCount,
                    BannedUntil = bannedUntil,
                    CreatedAt = now,
                });
            }
        }

        // 3. Child-specific lockout ladder (NFR-4): 3 → 5 min, 6 → 15 min, 9 → permanent
        if (child is not null)
        {
            child.UnsuccessfulLoginAttempts++;

            if (child.UnsuccessfulLoginAttempts >= Constants.Lockout.PermanentLockThreshold)
            {
                child.LockedUntil = DateTimeOffset.MaxValue;
            }
            else if (child.UnsuccessfulLoginAttempts == Constants.Lockout.MaxFailedAttemptsPerLockout * 2)
            {
                child.LockedUntil = now.AddMinutes(Constants.Lockout.SecondLockoutMinutes);
            }
            else if (child.UnsuccessfulLoginAttempts == Constants.Lockout.MaxFailedAttemptsPerLockout)
            {
                child.LockedUntil = now.AddMinutes(Constants.Lockout.FirstLockoutMinutes);
            }
        }

        await _db.SaveChangesAsync(ct);
    }
}
