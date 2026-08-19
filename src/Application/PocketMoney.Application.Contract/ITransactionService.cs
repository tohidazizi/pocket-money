using PocketMoney.Application.Model.Transactions;

namespace PocketMoney.Application.Contract;

/// <summary>
/// Ledger operations (API Spec §6, SDS §4 + §12). Append-only: there are
/// no edit/delete operations — corrections are new adjustment transactions
/// (FR-P6).
/// </summary>
public interface ITransactionService
{
    /// <summary>
    /// Atomic CREDIT/DEBIT with pessimistic row locking (SDS §4).
    /// Pushes SignalR <c>OnBalanceUpdated</c> to group <c>child_{id}</c>
    /// after commit.
    /// </summary>
    Task<CreateTransactionResult> CreateAsync(
        string parentUid, CreateTransactionRequest request, string ipAddress, CancellationToken ct = default);

    /// <summary>
    /// Parent view: whole household, optionally filtered to one child
    /// (API Spec §6.2). Household scoping is enforced internally (SDS §10).
    /// </summary>
    Task<TimelineResult> GetHouseholdTimelineAsync(
        string parentUid, TimelineQuery query, CancellationToken ct = default);

    /// <summary>
    /// Child view: strictly own rows — <see cref="TimelineQuery.ChildId"/>
    /// is ignored (SDS §7.1 footnote).
    /// </summary>
    Task<TimelinePage> GetChildTimelineAsync(
        Guid childId, TimelineQuery query, CancellationToken ct = default);
}

/// <summary>Discriminated result of <see cref="ITransactionService.CreateAsync"/>.</summary>
public abstract record CreateTransactionResult
{
    public sealed record Created(TransactionDto Transaction, decimal RemainingAfter) : CreateTransactionResult;
    public sealed record ValidationFailed(string Detail) : CreateTransactionResult;
    public sealed record NotFound() : CreateTransactionResult;
    public sealed record NegativeBalance() : CreateTransactionResult;
}

/// <summary>Discriminated result of <see cref="ITransactionService.GetHouseholdTimelineAsync"/>.</summary>
public abstract record TimelineResult
{
    public sealed record Ok(TimelinePage Page) : TimelineResult;
    public sealed record NotFound() : TimelineResult;
}
