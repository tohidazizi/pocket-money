namespace PocketMoney.Api;

/// <summary>
/// ProblemDetails infrastructure per SDS §7.0 / API Spec §1.4 (RFC 9457).
/// The domain error code rides in the `code` extension member.
/// </summary>
public static class ErrorCodes
{
    public const string ValidationError = "validation_error";
    public const string InvalidCredentials = "invalid_credentials";
    public const string TokenInvalid = "token_invalid";
    public const string TokenExpired = "token_expired";
    public const string SecurityStampMismatch = "security_stamp_mismatch";
    public const string IpBanned = "ip_banned";
    public const string OwnerOnly = "owner_only";
    public const string InvitationSenderOnly = "invitation_sender_only";
    public const string NotFound = "not_found";
    public const string AccountLocked = "account_locked";
    public const string AccountPermanentlyLocked = "account_permanently_locked";
    public const string ChildrenMaxReached = "children_max_reached";
    public const string ParentCapReached = "parent_cap_reached";
    public const string InvitationPending = "invitation_pending";
    public const string AlreadyInHousehold = "already_in_household";
    public const string InvitationInvalid = "invitation_invalid";
    public const string InvitationExpired = "invitation_expired";
}

/// <summary>
/// Locked responses extend ProblemDetails with `lockedUntil` (SDS §7.0,
/// LockedErrorDetails : ProblemDetails). Timed lockout tiers only —
/// permanent locks carry NO lockedUntil (API Spec §2.1).
/// </summary>
public sealed class LockedErrorDetails : Microsoft.AspNetCore.Mvc.ProblemDetails
{
    public DateTimeOffset LockedUntil { get; set; }
}
