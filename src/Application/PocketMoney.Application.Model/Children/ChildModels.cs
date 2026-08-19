using PocketMoney.Application.Model.Common;

namespace PocketMoney.Application.Model.Children;

// ---------------------------------------------------------------------------
// POST /api/v1/household/children — API Spec §5.1
// ---------------------------------------------------------------------------

public sealed record CreateChildRequest(string? DisplayName);

/// <summary>
/// 201 response. `initialPin` is returned ONLY here — shown to the parent
/// once and never retrievable again (SDS §7.1).
/// </summary>
public sealed record CreateChildResponse(
    Guid Id,
    string AccountId,
    string DisplayName,
    string InitialPin,
    string CurrencyKey,
    decimal CurrentBalance);

// ---------------------------------------------------------------------------
// PUT /api/v1/household/children/{id}/pin — API Spec §5.2
// ---------------------------------------------------------------------------

public sealed record ResetChildPinRequest(string? NewPin);

// ---------------------------------------------------------------------------
// PUT /api/v1/household/children/{id}/lock — API Spec §5.3
// ---------------------------------------------------------------------------

public sealed record SetChildLockRequest(bool? Locked);

// ---------------------------------------------------------------------------
// PUT /api/v1/household/children/{id}/currency — API Spec §5.4
// ---------------------------------------------------------------------------

public sealed record ChangeChildCurrencyRequest(string? CurrencyKey);

/// <summary>200 response: resolved currency + balance carried over numerically.</summary>
public sealed record ChildCurrencyResponse(CurrencyDto Currency, decimal CurrentBalance);

// ---------------------------------------------------------------------------
// GET /api/v1/household/children/me — API Spec §5.5
// ---------------------------------------------------------------------------

/// <summary>Child dashboard source (FR-C3).</summary>
public sealed record ChildMeResponse(
    string DisplayName,
    decimal CurrentBalance,
    CurrencyDto Currency);
