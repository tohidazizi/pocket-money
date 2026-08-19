using Microsoft.JSInterop;
using PocketMoney.Global;

namespace PocketMoney.Client.Services;

/// <summary>
/// Parent inactivity lock (FR-P6, SDS §6.1): a client-side route guard.
/// After <see cref="Constants.ParentInactivityLockMs"/> without activity the
/// parent surface locks and the Parent PIN Unlock Modal appears. Unlock is
/// the ONLY purpose of the Parent Lock PIN (UI Spec §4).
/// </summary>
public sealed class InactivityTimerService : IAsyncDisposable
{
    private readonly IJSRuntime _js;
    private DotNetObjectReference<InactivityTimerService>? _self;
    private Timer? _timer;
    private bool _started;

    public event Action? OnInactivityTimeout;

    private static readonly int InactivityTimeoutMs = Constants.ParentInactivityLockMs;

    /// <summary>Milliseconds remaining; used by the countdown chip (&lt; 2:01 shown).</summary>
    public event Action<int>? OnTicking;

    public InactivityTimerService(IJSRuntime js) => _js = js;

    public async Task StartAsync()
    {
        if (_started) return;
        _started = true;
        _self = DotNetObjectReference.Create(this);
        await _js.InvokeVoidAsync("pmInterop.startActivityTracking", _self);
        ResetTimer();
    }

    public void ResetTimer()
    {
        _timer?.Dispose();
        _timer = new Timer(Tick, null, 1000, 1000);
        _remainingMs = InactivityTimeoutMs;
    }

    private int _remainingMs;

    private void Tick(object? state)
    {
        _remainingMs -= 1000;
        if (_remainingMs <= 0)
        {
            _timer?.Dispose();
            OnInactivityTimeout?.Invoke();
            return;
        }
        OnTicking?.Invoke(_remainingMs);
    }

    [JSInvokable]
    public void OnUserActivity() => ResetTimer();

    public async ValueTask DisposeAsync()
    {
        _timer?.Dispose();
        if (_started)
        {
            try { await _js.InvokeVoidAsync("pmInterop.stopActivityTracking"); }
            catch (JSDisconnectedException) { }
        }
        _self?.Dispose();
    }
}
