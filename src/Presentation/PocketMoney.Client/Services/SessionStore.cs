using System.Text.Json;
using Microsoft.JSInterop;

namespace PocketMoney.Client.Services;

/// <summary>
/// Single active session rule (UI Spec §3.4): exactly one session exists —
/// either a parent (Firebase ID token) or a child (365-day child JWT).
/// Stored in localStorage under <c>pm_session</c> so a reload resumes the
/// correct surface (FR-C2 automatic re-entry).
/// </summary>
public sealed class SessionStore
{
    private readonly IJSRuntime _js;
    private const string Key = "pm_session";

    public SessionStore(IJSRuntime js) => _js = js;

    public sealed record Session(string Role, string Token)
    {
        public const string RoleParent = "parent";
        public const string RoleChild = "child";
    }

    public async Task<Session?> GetAsync()
    {
        var raw = await _js.InvokeAsync<string?>(
            "pmInterop.getItem", Key);
        if (string.IsNullOrEmpty(raw)) return null;
        try { return JsonSerializer.Deserialize<Session>(raw); }
        catch { return null; }
    }

    public async Task SetParentAsync(string firebaseIdToken)
    {
        // Child login clears any stored child session by replacement;
        // parent login additionally clears the child session (UI Spec §3.4).
        await _js.InvokeVoidAsync("pmInterop.setItem", Key,
            JsonSerializer.Serialize(new Session(Session.RoleParent, firebaseIdToken)));
    }

    public async Task SetChildAsync(string childToken)
    {
        await _js.InvokeVoidAsync("pmInterop.setItem", Key,
            JsonSerializer.Serialize(new Session(Session.RoleChild, childToken)));
    }

    public async Task ClearAsync() =>
        await _js.InvokeVoidAsync("pmInterop.removeItem", Key);
}

/// <summary>
/// ChildrenHistory Storage (UI Spec §3.1): localStorage map
/// <c>{ AccountID: { name, lockedUntil } }</c> of previously logged-in
/// children. Lock state is cached from 423 login responses (§3.1
/// clarification — no other lock-state source exists). A convenience list —
/// retained across parent sessions (§3.4).
/// </summary>
public sealed class ChildrenHistoryStore
{
    private readonly IJSRuntime _js;

    public ChildrenHistoryStore(IJSRuntime js) => _js = js;

    public sealed record Entry(
        [property: System.Text.Json.Serialization.JsonPropertyName("name")] string Name,
        [property: System.Text.Json.Serialization.JsonPropertyName("lockedUntil")] string? LockedUntil)
    {
        /// <summary>"permanent" or a parseable ISO timestamp (already-pruned if expired).</summary>
        public bool IsLocked =>
            LockedUntil == "permanent"
            || (LockedUntil is not null && DateTimeOffset.TryParse(LockedUntil, out _));
        public bool IsPermanent => LockedUntil == "permanent";
    }

    /// <summary>Entries in stored order (insertion order preserved by JS objects).</summary>
    public async Task<Dictionary<string, Entry>> GetAsync() =>
        await _js.InvokeAsync<Dictionary<string, Entry>>("pmInterop.getChildrenHistory");

    public async Task UpsertAsync(string accountId, string displayName, string? lockedUntil = null) =>
        await _js.InvokeVoidAsync("pmInterop.upsertChildrenHistory", accountId, displayName, lockedUntil);

    /// <summary>Caches lock state after a 423 login attempt (UI Spec §3.1).</summary>
    public async Task SetLockedAsync(string accountId, string lockedUntil) =>
        await _js.InvokeVoidAsync("pmInterop.setChildLocked", accountId, lockedUntil);

    public async Task RemoveAsync(string accountId) =>
        await _js.InvokeVoidAsync("pmInterop.removeChildFromHistory", accountId);

    public async Task ClearAsync() =>
        await _js.InvokeVoidAsync("pmInterop.clearChildrenHistory");
}
