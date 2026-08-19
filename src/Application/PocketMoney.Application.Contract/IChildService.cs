using PocketMoney.Application.Model.Children;

namespace PocketMoney.Application.Contract;

/// <summary>
/// Child profile management (API Spec §5.1–5.5, FR-P3/P4/P7/P8, FR-C3).
/// All parent operations are household-scoped; /children/me is child-scoped.
/// </summary>
public interface IChildService
{
    /// <summary>POST /household/children — creates a profile, inherits the household default currency.</summary>
    Task<CreateChildResult> CreateAsync(string parentUid, string? displayName, string ipAddress, CancellationToken ct = default);

    /// <summary>PUT /household/children/{id}/pin — resets PIN + rotates SecurityStamp.</summary>
    Task<ChildActionResult> ResetPinAsync(string parentUid, Guid childId, string? newPin, string ipAddress, CancellationToken ct = default);

    /// <summary>PUT /household/children/{id}/lock — manual lock/unlock (FR-P8).</summary>
    Task<ChildActionResult> SetLockAsync(string parentUid, Guid childId, bool locked, string ipAddress, CancellationToken ct = default);

    /// <summary>PUT /household/children/{id}/currency — balance carries over numerically.</summary>
    Task<ChangeCurrencyResult> ChangeCurrencyAsync(string parentUid, Guid childId, string? currencyKey, string ipAddress, CancellationToken ct = default);

    /// <summary>GET /household/children/me — child dashboard (FR-C3).</summary>
    Task<ChildMeResult> GetMeAsync(Guid childId, CancellationToken ct = default);
}

/// <summary>POST /household/children outcomes (API Spec §5.1).</summary>
public abstract record CreateChildResult
{
    public sealed record Created(CreateChildResponse Child) : CreateChildResult;
    public sealed record ValidationFailed(string Detail) : CreateChildResult;
    public sealed record ChildrenMaxReached : CreateChildResult;
    public sealed record ParentUnknown : CreateChildResult;
}

/// <summary>Shared outcome for PIN reset and lock toggle.</summary>
public abstract record ChildActionResult
{
    public sealed record Ok : ChildActionResult;
    public sealed record ValidationFailed(string Detail) : ChildActionResult;
    /// <summary>404 — missing or outside caller's household.</summary>
    public sealed record NotFound : ChildActionResult;
    /// <summary>423 account_locked — PIN change while locked (API Spec §5.2).</summary>
    public sealed record AccountLocked : ChildActionResult;
}

/// <summary>PUT /household/children/{id}/currency outcomes (API Spec §5.4).</summary>
public abstract record ChangeCurrencyResult
{
    public sealed record Changed(ChildCurrencyResponse Response) : ChangeCurrencyResult;
    public sealed record ValidationFailed(string Detail) : ChangeCurrencyResult;
    public sealed record NotFound : ChangeCurrencyResult;
}

/// <summary>GET /household/children/me outcomes (API Spec §5.5).</summary>
public abstract record ChildMeResult
{
    public sealed record Ok(ChildMeResponse Response) : ChildMeResult;
    public sealed record NotFound : ChildMeResult;
}
