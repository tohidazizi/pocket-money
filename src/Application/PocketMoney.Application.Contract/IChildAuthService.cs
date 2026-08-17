namespace PocketMoney.Application.Contract;

/// <summary>
/// Child authentication (FR-C1). Public endpoint — no auth required.
/// Every attempt, successful or not, is recorded in login_attempts (SDS §3.3).
/// </summary>
public interface IChildAuthService
{
    Task<ChildLoginResult> LoginAsync(string? accountId, string? pin, ClientInfo clientInfo, CancellationToken ct = default);
}

/// <summary>Caller metadata captured by the API layer for audit & IP ban (SDS §3.3).</summary>
public sealed record ClientInfo(string IpAddress, string HttpRequestInfo);

/// <summary>
/// Discriminated result of a child login attempt (API Spec §2.1).
/// The API layer maps each case to its HTTP status + ProblemDetails code.
/// </summary>
public abstract record ChildLoginResult
{
    /// <summary>200 — token issued, failure counter reset.</summary>
    public sealed record Success(string Token, DateTimeOffset ExpiresAt, Guid ChildId, string AccountId, string DisplayName) : ChildLoginResult;

    /// <summary>400 validation_error — input violates SDS §9 before any domain logic.</summary>
    public sealed record ValidationFailed(string Detail) : ChildLoginResult;

    /// <summary>401 invalid_credentials — wrong account ID or PIN (also: unknown account).</summary>
    public sealed record InvalidCredentials : ChildLoginResult;

    /// <summary>423 account_locked — timed lockout tier, lockedUntil carries expiry.</summary>
    public sealed record Locked(DateTimeOffset LockedUntil) : ChildLoginResult;

    /// <summary>423 account_permanently_locked — 9+ failures or manual parent lock.</summary>
    public sealed record PermanentlyLocked : ChildLoginResult;

    /// <summary>403 ip_banned — IP is in an active ban.</summary>
    public sealed record IpBanned(DateTimeOffset BannedUntil) : ChildLoginResult;
}
