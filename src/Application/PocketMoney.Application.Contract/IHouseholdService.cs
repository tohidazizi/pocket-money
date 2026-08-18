using PocketMoney.Application.Model.Households;

namespace PocketMoney.Application.Contract;

/// <summary>
/// Household operations for authenticated parents (API Spec §3, §4.1).
/// The Firebase UID/email are resolved from the verified ID token by the API
/// layer; <see cref="GetOrCreateAsync"/> doubles as Auto-Registration
/// (SDS §7.1.2 — a household is created on the parent's first sign-in).
/// </summary>
public interface IHouseholdService
{
    /// <summary>
    /// GET /household payload with Auto-Registration (SDS §7.1.2):
    /// unknown UID + no matching invitation → new household (owner);
    /// unknown UID + pending invitation for the verified email → joined.
    /// Always succeeds for a valid Firebase token.
    /// </summary>
    Task<HouseholdResponse> GetOrCreateAsync(string firebaseUid, string? email, string ipAddress, CancellationToken ct = default);

    Task<UpdateSettingsResult> UpdateSettingsAsync(string firebaseUid, string? displayName, string? defaultCurrencyKey, string ipAddress, CancellationToken ct = default);

    Task<DeleteHouseholdResult> DeleteAsync(string firebaseUid, string ipAddress, CancellationToken ct = default);

    Task<SetParentPinResult> SetMyPinAsync(string firebaseUid, string? currentPin, string? newPin, string ipAddress, CancellationToken ct = default);
}

/// <summary>PUT /household/settings outcomes (API Spec §3.3).</summary>
public abstract record UpdateSettingsResult
{
    public sealed record Ok(HouseholdResponse Household) : UpdateSettingsResult;
    public sealed record ValidationFailed(string Detail) : UpdateSettingsResult;
    /// <summary>Parent row missing — protocol violation (GET auto-registers first).</summary>
    public sealed record ParentUnknown : UpdateSettingsResult;
}

/// <summary>DELETE /household outcomes (API Spec §3.4).</summary>
public abstract record DeleteHouseholdResult
{
    public sealed record Deleted : DeleteHouseholdResult;
    public sealed record OwnerOnly : DeleteHouseholdResult;
    public sealed record ParentUnknown : DeleteHouseholdResult;
}

/// <summary>PUT /household/parents/me/pin outcomes (API Spec §4.1).</summary>
public abstract record SetParentPinResult
{
    public sealed record Ok : SetParentPinResult;
    public sealed record ValidationFailed(string Detail) : SetParentPinResult;
    /// <summary>401 — currentPin does not match the stored hash.</summary>
    public sealed record InvalidCredentials : SetParentPinResult;
    public sealed record ParentUnknown : SetParentPinResult;
}
