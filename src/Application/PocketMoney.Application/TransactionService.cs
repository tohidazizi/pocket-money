using System.Data;
using Microsoft.EntityFrameworkCore;
using PocketMoney.Application.Contract;
using PocketMoney.Application.Model.Transactions;
using PocketMoney.Domain.Entities;
using PocketMoney.Global;
using PocketMoney.Persistence.Data;

namespace PocketMoney.Application;

/// <summary>Entity → DTO mapping (Model project has no Domain reference).</summary>
internal static class TransactionMapper
{
    public static PocketMoney.Application.Model.Transactions.TransactionDto ToDto(
        PocketMoney.Domain.Entities.Transaction t) => new(
        t.Id, t.ChildId, t.Type.ToString().ToUpperInvariant(),
        t.CurrencyKey, t.Amount, t.Reason, t.RemainingAfter, t.CreatedAt);
}

/// <summary>
/// Ledger operations (API Spec §6, SDS §4 + §12).
/// </summary>
public sealed class TransactionService(
    PocketMoneyDbContext db,
    TimeProvider time,
    ILedgerPushService ledgerPush) : ITransactionService
{
    /// <summary>
    /// Atomic CREDIT/DEBIT (SDS §4): pessimistic <c>FOR UPDATE</c> row lock
    /// inside a ReadCommitted transaction, balance re-checked under the lock,
    /// append-only ledger row with a currency snapshot, SignalR push after
    /// commit. There are no edit/delete operations — corrections are new
    /// adjustment transactions (FR-P6).
    /// </summary>
    public async Task<CreateTransactionResult> CreateAsync(
        string parentUid, CreateTransactionRequest request, string ipAddress, CancellationToken ct = default)
    {
        var parent = await db.Parents.FirstOrDefaultAsync(p => p.Id == parentUid, ct);
        if (parent is null)
            return new CreateTransactionResult.NotFound();

        if (!Enum.TryParse<TransactionType>(request.Type?.Trim(), ignoreCase: true, out var type))
            return new CreateTransactionResult.ValidationFailed("type must be CREDIT or DEBIT.");

        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        try
        {
            // Pessimistic row lock (SDS §4) — household-scoped, so a foreign
            // or missing child is the same 404 (API Spec §1.3).
            var child = await db.Children
                .FromSqlRaw(
                    "SELECT * FROM children WHERE id = {0} AND household_id = {1} FOR UPDATE",
                    request.ChildId, parent.HouseholdId)
                .SingleOrDefaultAsync(ct);

            if (child is null)
            {
                await tx.RollbackAsync(ct);
                return new CreateTransactionResult.NotFound();
            }

            var currency = CurrencyType.Parse(child.CurrencyKey);
            if (currency is null)
            {
                await tx.RollbackAsync(ct);
                return new CreateTransactionResult.ValidationFailed("Child has an unsupported currency.");
            }

            if (!InputValidation.IsValidTransactionAmount(request.Amount, currency.DecimalDigits))
            {
                await tx.RollbackAsync(ct);
                return new CreateTransactionResult.ValidationFailed(
                    $"amount must be > 0, ≤ {InputValidation.MaxTransactionAmount}, with at most {currency.DecimalDigits} fractional digits — values are rejected, never rounded.");
            }

            var reason = InputValidation.SanitizeReason(request.Reason ?? string.Empty);
            if (reason.Length == 0 || reason.Length > Constants.Transaction.ReasonMaxLength)
            {
                await tx.RollbackAsync(ct);
                return new CreateTransactionResult.ValidationFailed(
                    $"reason must be 1–{Constants.Transaction.ReasonMaxLength} characters after sanitization.");
            }

            var newBalance = type == TransactionType.Credit
                ? child.CurrentBalance + request.Amount
                : child.CurrentBalance - request.Amount;

            if (newBalance < 0)
            {
                await tx.RollbackAsync(ct);
                return new CreateTransactionResult.NegativeBalance();
            }

            child.CurrentBalance = newBalance;

            var transaction = new Transaction
            {
                HouseholdId = child.HouseholdId,
                ChildId = child.Id,
                Type = type,
                CurrencyKey = currency.Key, // snapshot — history keeps its denomination (SDS §2.1.1)
                Amount = request.Amount,
                Reason = reason,
                RemainingAfter = newBalance,
                CreatorId = parentUid,
                CreatedAt = time.GetUtcNow(),
            };
            db.Transactions.Add(transaction);

            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            // Real-time push AFTER commit (SDS §4, §7.2).
            await ledgerPush.PushTransactionAsync(child.Id, newBalance, TransactionMapper.ToDto(transaction), ct);

            return new CreateTransactionResult.Created(TransactionMapper.ToDto(transaction), newBalance);
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    /// <summary>Parent view: whole household, optionally filtered to one child (API Spec §6.2).</summary>
    public async Task<TimelineResult> GetHouseholdTimelineAsync(
        string parentUid, TimelineQuery query, CancellationToken ct = default)
    {
        var parent = await db.Parents.AsNoTracking().FirstOrDefaultAsync(p => p.Id == parentUid, ct);
        if (parent is null)
            return new TimelineResult.NotFound();

        var source = db.Transactions.AsNoTracking()
            .Where(t => t.HouseholdId == parent.HouseholdId);

        if (query.ChildId is { } childId)
            source = source.Where(t => t.ChildId == childId);

        return new TimelineResult.Ok(await PageAsync(ApplyFilters(source, query), query, ct));
    }

    /// <summary>Child view: strictly own rows — any ChildId filter is ignored (SDS §7.1).</summary>
    public async Task<TimelinePage> GetChildTimelineAsync(
        Guid childId, TimelineQuery query, CancellationToken ct = default)
    {
        var source = db.Transactions.AsNoTracking()
            .Where(t => t.ChildId == childId);

        return await PageAsync(ApplyFilters(source, query), query, ct);
    }

    // ------------------------------------------------------------------

    /// <summary>Filters apply BEFORE keyset paging (SDS §12.2).</summary>
    private static IQueryable<Transaction> ApplyFilters(IQueryable<Transaction> source, TimelineQuery query)
    {
        if (query.Type is { } type)
            source = source.Where(t => t.Type == type);

        if (query.From is { } from)
        {
            // Stored timestamps are UTC; the `from` date is interpreted as UTC midnight.
            var start = new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
            source = source.Where(t => t.CreatedAt >= start);
        }

        if (query.To is { } to)
        {
            // Exclusive upper bound at midnight of the NEXT UTC day — the
            // `to` date itself is fully included.
            var endExclusive = new DateTimeOffset(to.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
            source = source.Where(t => t.CreatedAt < endExclusive);
        }

        if (query.MinAmount is { } min)
            source = source.Where(t => t.Amount >= min);

        if (query.MaxAmount is { } max)
            source = source.Where(t => t.Amount <= max);

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var escaped = query.Search
                .Replace("\\", "\\\\")
                .Replace("%", "\\%")
                .Replace("_", "\\_");
            var pattern = $"%{escaped}%";
            source = source.Where(t => EF.Functions.ILike(t.Reason, pattern));
        }

        return source;
    }

    /// <summary>
    /// Keyset paging (SDS §12): strict (created_at DESC, id DESC) order,
    /// fetch pageSize+1 rows to detect a next page. nextCursor is null on
    /// the final page — the sole end-of-history signal (§12.3).
    /// </summary>
    private static async Task<TimelinePage> PageAsync(
        IQueryable<Transaction> filtered, TimelineQuery query, CancellationToken ct)
    {
        var pageSize = Math.Clamp(query.PageSize, 1, Constants.Timeline.MaxPageSize);

        // Keyset filter applies BEFORE ordering (Where on IOrderedQueryable
        // would lose the ordered type — and semantically it is a predicate).
        var paged = filtered;
        if (query.Keyset is { } keyset)
        {
            paged = paged.Where(t =>
                t.CreatedAt < keyset.CreatedAt
                || (t.CreatedAt == keyset.CreatedAt && t.Id < keyset.Id));
        }

        var rows = await paged
            .OrderByDescending(t => t.CreatedAt)
            .ThenByDescending(t => t.Id)
            .Take(pageSize + 1)
            .ToListAsync(ct);

        var hasMore = rows.Count > pageSize;
        var items = rows.Take(pageSize).Select(TransactionMapper.ToDto).ToList();
        var nextCursor = hasMore && items.Count > 0
            ? TimelineCursor.Encode(items[^1].CreatedAt, items[^1].Id)
            : null;

        return new TimelinePage(items, nextCursor);
    }
}
