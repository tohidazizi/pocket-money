namespace PocketMoney.Application.Contract;

/// <summary>
/// Real-time ledger push (SDS §7.2). Fired after EVERY server-side change
/// to a child's balance or currency:
/// * committed CREDIT/DEBIT (SDS §4) — carries the new transaction row;
/// * currency change (SDS §2.1.1) — balance unchanged, new denomination.
/// V1 transport: SignalR hub <c>/hubs/ledger</c>, group <c>child_{id}</c>.
/// </summary>
public interface ILedgerPushService
{
    /// <summary>Push after a committed transaction (SDS §4 payload shape).</summary>
    Task PushTransactionAsync(Guid childId, decimal newBalance, object transactionDto, CancellationToken ct = default);

    /// <summary>Push after a currency change — client re-renders the carried-over balance.</summary>
    Task PushCurrencyChangedAsync(Guid childId, decimal balance, string currencyKey, CancellationToken ct = default);
}
