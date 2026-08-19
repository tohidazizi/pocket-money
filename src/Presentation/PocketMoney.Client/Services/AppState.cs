using PocketMoney.Application.Model.Children;
using PocketMoney.Application.Model.Households;

namespace PocketMoney.Client.Services;

/// <summary>
/// Central client state + navigation coordinator. One active session at a
/// time (UI Spec §3.4). Pages read/write through here and listen to
/// <see cref="StateChanged"/> for re-render.
/// </summary>
public sealed class AppState
{
    private readonly SessionStore _sessions;
    private readonly FirebaseAuthService _firebase;

    public AppState(SessionStore sessions, FirebaseAuthService firebase)
    {
        _sessions = sessions;
        _firebase = firebase;
    }

    public event Action? StateChanged;
    private void Notify() => StateChanged?.Invoke();

    // ------------------------------------------------------------------
    // Session
    // ------------------------------------------------------------------
    public enum SessionKind { None, Parent, Child }

    public SessionKind Current { get; private set; } = SessionKind.None;

    /// <summary>Parent Firebase ID token (fresh per call via Firebase SDK).</summary>
    public async Task<string?> ParentTokenAsync()
    {
        if (Current != SessionKind.Parent) return null;
        return await _firebase.GetIdTokenAsync();
    }

    /// <summary>Child 365-day JWT (from login response).</summary>
    public string? ChildToken { get; private set; }

    /// <summary>The logged-in child's profile (from /children/me).</summary>
    public ChildMeResponse? ChildProfile { get; private set; }

    /// <summary>The logged-in child's account id (for history upserts).</summary>
    public string? ChildAccountId { get; private set; }

    /// <summary>Parent landing payload, refreshed after mutations.</summary>
    public HouseholdResponse? Household { get; private set; }

    /// <summary>Parent idle-lock modal open (FR-P6).</summary>
    public bool IsParentLocked { get; set; }

    /// <summary>Invitation token carried via ?invite= on the login URL.</summary>
    public string? PendingInviteToken { get; set; }

    // ------------------------------------------------------------------
    // Actions
    // ------------------------------------------------------------------

    /// <summary>Restores a session after boot (FR-C2 automatic re-entry).</summary>
    public async Task RestoreSessionAsync()
    {
        var session = await _sessions.GetAsync();
        if (session is null) { Current = SessionKind.None; return; }

        if (session.Role == SessionStore.Session.RoleChild)
        {
            ChildToken = session.Token;
            Current = SessionKind.Child;
        }
        else
        {
            // Parent: Firebase SDK is the source of truth; re-establish if
            // it still has a signed-in user, otherwise drop the stale entry.
            var uid = await _firebase.GetIdTokenAsync();
            if (uid is not null) Current = SessionKind.Parent;
            else await _sessions.ClearAsync();
        }
        Notify();
    }

    public async Task ChildLoginSucceededAsync(ChildLoginResponse login)
    {
        // Child login clears any stored parent session (UI Spec §3.4).
        await _firebase.SignOutAsync();
        ChildToken = login.Token;
        ChildAccountId = login.Child.AccountId;
        ChildProfile = null;
        Current = SessionKind.Child;
        await _sessions.SetChildAsync(login.Token);
        Notify();
    }

    public async Task ParentLoginSucceededAsync()
    {
        // Parent login clears any stored child session (UI Spec §3.4).
        ChildToken = null;
        ChildProfile = null;
        ChildAccountId = null;
        Current = SessionKind.Parent;
        var token = await _firebase.GetIdTokenAsync();
        if (token is not null)
            await _sessions.SetParentAsync(token);
        Notify();
    }

    public async Task LogoutAsync()
    {
        if (Current == SessionKind.Parent)
            await _firebase.SignOutAsync();
        ChildToken = null;
        ChildProfile = null;
        ChildAccountId = null;
        Household = null;
        IsParentLocked = false;
        Current = SessionKind.None;
        await _sessions.ClearAsync();
        Notify();
    }

    public void SetChildProfile(ChildMeResponse profile) { ChildProfile = profile; Notify(); }
    public void SetHousehold(HouseholdResponse household) { Household = household; Notify(); }
    public void LockParent() { IsParentLocked = true; Notify(); }
    public void UnlockParent() { IsParentLocked = false; Notify(); }
}
