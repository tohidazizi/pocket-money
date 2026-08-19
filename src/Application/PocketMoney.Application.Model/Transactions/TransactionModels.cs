using PocketMoney.Global;

namespace PocketMoney.Application.Model.Transactions;

// ---------------------------------------------------------------------------
// POST /api/v1/household/transactions (API Spec §6.1)
// ---------------------------------------------------------------------------

public sealed record CreateTransactionRequest(
    Guid ChildId,
    string Type,
    decimal Amount,
    string Reason);

/// <summary>
/// Ledger row as exchanged over the API — carries its own snapshotted
/// currencyKey (SDS §2.1.1) so history renders in the original denomination.
/// </summary>
public sealed record TransactionDto(
    Guid Id,
    Guid ChildId,
    string Type,
    string CurrencyKey,
    decimal Amount,
    string Reason,
    decimal RemainingAfter,
    DateTimeOffset CreatedAt);

// ---------------------------------------------------------------------------
// GET /api/v1/household/transactions (API Spec §6.2, SDS §12)
// ---------------------------------------------------------------------------

/// <summary>
/// Timeline filters (SDS §12.2). Filters apply BEFORE keyset paging; the
/// client must resend the same filters together with the cursor.
/// <see cref="Keyset"/> is the DECODED cursor — the endpoint decodes the
/// opaque client string and rejects malformation with 400.
/// </summary>
public sealed record TimelineQuery(
    Guid? ChildId,                      // parent-only filter; ignored for child JWTs
    TransactionType? Type,
    DateOnly? From,
    DateOnly? To,
    decimal? MinAmount,
    decimal? MaxAmount,
    string? Search,                     // reason substring, case-insensitive
    (DateTimeOffset CreatedAt, Guid Id)? Keyset,
    int PageSize);

public sealed record TimelinePage(
    IReadOnlyList<TransactionDto> Items,
    string? NextCursor);                // null = end of history (SDS §12.3 stop rule)
