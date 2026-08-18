using PocketMoney.Application.Model.Common;

namespace PocketMoney.Application.Model.Households;

// ---------------------------------------------------------------------------
// GET /api/v1/household — API Spec §3.2
// ---------------------------------------------------------------------------

/// <summary>Parent landing-page payload (API Spec §3.2, SDS §7.1).</summary>
public sealed record HouseholdResponse(
    Guid Id,
    string? DisplayName,
    CurrencyDto? DefaultCurrency,
    byte MaxParents,
    IReadOnlyList<PendingInvitationDto> PendingInvitations,
    DateTimeOffset CreatedAt,
    IReadOnlyList<ParentDto> Parents,
    IReadOnlyList<ChildSummaryDto> Children);

/// <summary>One parent entry in the `parents` array (API Spec §3.2).</summary>
public sealed record ParentDto(
    string Id,
    string? DisplayName,
    bool IsOwner,
    bool HasPin);

/// <summary>Outstanding parent invitation (API Spec §3.2).</summary>
public sealed record PendingInvitationDto(
    Guid Id,
    string Email,
    DateTimeOffset ExpiresAt);

/// <summary>Child row on the parent landing page (API Spec §3.2).</summary>
public sealed record ChildSummaryDto(
    Guid Id,
    string AccountId,
    string DisplayName,
    CurrencyDto Currency,
    decimal CurrentBalance,
    bool Locked,
    DateTimeOffset? LockedUntil);

// ---------------------------------------------------------------------------
// PUT /api/v1/household/settings — API Spec §3.3
// ---------------------------------------------------------------------------

public sealed record UpdateHouseholdSettingsRequest(string? DisplayName, string? DefaultCurrencyKey);

// ---------------------------------------------------------------------------
// POST /api/v1/household/invitations — API Spec §3.5
// ---------------------------------------------------------------------------

public sealed record CreateInvitationRequest(string? Email);

public sealed record InvitationResponse(Guid InvitationId, DateTimeOffset ExpiresAt);

// ---------------------------------------------------------------------------
// POST /api/v1/household/invitations/accept — API Spec §3.6
// ---------------------------------------------------------------------------

public sealed record AcceptInvitationRequest(string? Token);

public sealed record AcceptInvitationResponse(Guid HouseholdId, string? DisplayName);
