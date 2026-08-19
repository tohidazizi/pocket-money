using Microsoft.AspNetCore.SignalR.Client;

namespace PocketMoney.Client.Services;

/// <summary>
/// Real-time ledger push client (SDS §7.2): connects to /hubs/ledger with
/// the child's JWT, joins child_{id}, and surfaces OnBalanceUpdated events.
/// </summary>
public sealed class LedgerHubService : IAsyncDisposable
{
    private readonly ApiEndpoints _endpoints;
    private HubConnection? _connection;

    /// <summary>(newBalance, transactionJson) after a committed CREDIT/DEBIT.</summary>
    public event Action<decimal, string>? OnBalanceUpdated;

    public async Task StartAsync(string childToken, Guid childId)
    {
        _connection = new HubConnectionBuilder()
            .WithUrl(_endpoints.LedgerHub, options =>
            {
                options.AccessTokenProvider = () => Task.FromResult<string?>(childToken);
            })
            .WithAutomaticReconnect()
            .Build();

        _connection.On<decimal, System.Text.Json.JsonElement>("OnBalanceUpdated", (balance, txn) =>
            OnBalanceUpdated?.Invoke(balance, txn.GetRawText()));

        await _connection.StartAsync();
        await _connection.InvokeAsync("JoinChildGroup", childId.ToString("D"));
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null)
            await _connection.DisposeAsync();
    }
}
