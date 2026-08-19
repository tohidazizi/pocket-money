using Microsoft.JSInterop;

namespace PocketMoney.Client.Services;

/// <summary>
/// Firebase Authentication bridge for parent sign-in (email/password and
/// Google popup). The JS side loads its config from firebase-config.json.
/// </summary>
public sealed class FirebaseAuthService : IAsyncDisposable
{
    private readonly IJSRuntime _js;
    private DotNetObjectReference<FirebaseAuthService>? _self;

    public event Action<FirebaseUser?>? AuthStateChanged;

    public sealed record FirebaseUser(string Uid, string? DisplayName, string? Email);

    public FirebaseAuthService(IJSRuntime js) => _js = js;

    public async Task<FirebaseUser> SignInWithEmailAsync(string email, string password) =>
        await _js.InvokeAsync<FirebaseUser>("pmFirebase.signInWithEmail", email, password);

    public async Task<FirebaseUser> SignInWithGoogleAsync() =>
        await _js.InvokeAsync<FirebaseUser>("pmFirebase.signInWithGoogle");

    /// <summary>Fresh ID token for API calls (Firebase tokens live ~1h).</summary>
    public async Task<string?> GetIdTokenAsync() =>
        await _js.InvokeAsync<string?>("pmFirebase.getIdToken");

    public async Task SignOutAsync() =>
        await _js.InvokeVoidAsync("pmFirebase.signOut");

    /// <summary>Subscribes to auth-state changes; .NET callback via [JSInvokable].</summary>
    public async Task WatchAuthAsync()
    {
        _self ??= DotNetObjectReference.Create(this);
        await _js.InvokeVoidAsync("pmFirebase.onAuthChanged", _self);
    }

    [JSInvokable]
    public void OnAuthChanged(FirebaseUser? user) => AuthStateChanged?.Invoke(user);

    public async ValueTask DisposeAsync()
    {
        try
        {
            await _js.InvokeVoidAsync("pmFirebase.unsubscribe");
        }
        catch (JSDisconnectedException) { /* host is gone */ }
        _self?.Dispose();
    }
}
