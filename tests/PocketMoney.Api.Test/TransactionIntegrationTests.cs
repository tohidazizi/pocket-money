using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PocketMoney.Application;
using PocketMoney.Application.Contract;
using PocketMoney.Application.Model.Transactions;
using PocketMoney.Domain.Entities;
using PocketMoney.Global;
using Xunit;

namespace PocketMoney.Api.Test;

/// <summary>
/// Phase 4 integration tests: atomic CREDIT/DEBIT with row locking (SDS §4)
/// and keyset timeline pagination (SDS §12) against real PostgreSQL.
/// </summary>
[Collection("database")]
public class TransactionIntegrationTests
{
    private readonly DatabaseFixture _fixture;
    private static readonly FakeTimeProvider Time = new();

    public TransactionIntegrationTests(DatabaseFixture fixture) => _fixture = fixture;

    private sealed class NoOpLedgerPush : ILedgerPushService
    {
        public Task PushTransactionAsync(Guid childId, decimal newBalance, object dto, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task PushCurrencyChangedAsync(Guid childId, decimal balance, string currencyKey, CancellationToken ct = default)
            => Task.CompletedTask;
    }

    /// <summary>Fresh service + context per operation — mirrors one scoped context per request.</summary>
    private TransactionService CreateService()
    {
        var db = _fixture.CreateContext();
        return new TransactionService(db, Time, new NoOpLedgerPush());
    }

    private static string UniqueIp() =>
        $"10.{Random.Shared.Next(1, 254)}.{Random.Shared.Next(1, 254)}.{Random.Shared.Next(1, 254)}";

    /// <summary>Household + owner + one child (Point currency, balance 0).</summary>
    private async Task<(Guid HouseholdId, Parent Owner, Child Child)> SeedChildAsync()
    {
        var (household, owner) = await _fixture.SeedHouseholdAsync();
        var childService = new ChildService(_fixture.CreateContext(), new AuditService(_fixture.CreateContext()), Time);
        var created = (await childService.CreateAsync(owner.Id, "Mia", UniqueIp()))
            .Should().BeOfType<CreateChildResult.Created>().Which.Child;

        var child = await _fixture.CreateContext().Children.AsNoTracking()
            .SingleAsync(c => c.Id == created.Id);
        return (household.Id, owner, child);
    }

    private async Task<Child> ReloadAsync(Guid childId) =>
        await _fixture.CreateContext().Children.AsNoTracking().SingleAsync(c => c.Id == childId);

    // ------------------------------------------------------------------
    // POST — atomic CREDIT/DEBIT (SDS §4)
    // ------------------------------------------------------------------

    [Fact]
    public async Task Credit_and_debit_update_balance_with_currency_snapshot()
    {
        var (_, owner, child) = await SeedChildAsync();

        var credit = await CreateService().CreateAsync(owner.Id,
            new CreateTransactionRequest(child.Id, "CREDIT", 5.00m, "Mowed Lawn"), UniqueIp());

        var created = credit.Should().BeOfType<CreateTransactionResult.Created>().Which;
        created.RemainingAfter.Should().Be(5.00m);
        created.Transaction.CurrencyKey.Should().Be("Point"); // snapshot at insert time
        created.Transaction.Reason.Should().Be("Mowed Lawn");

        var debit = await CreateService().CreateAsync(owner.Id,
            new CreateTransactionRequest(child.Id, "DEBIT", 1.5m, "Candy"), UniqueIp());
        debit.Should().BeOfType<CreateTransactionResult.Created>()
            .Which.RemainingAfter.Should().Be(3.5m);

        (await ReloadAsync(child.Id)).CurrentBalance.Should().Be(3.5m);
    }

    [Fact]
    public async Task Ledger_rows_keep_original_currency_after_currency_change()
    {
        var (_, owner, child) = await SeedChildAsync();

        var before = await CreateService().CreateAsync(owner.Id,
            new CreateTransactionRequest(child.Id, "CREDIT", 10m, "Before switch"), UniqueIp());
        var beforeId = before.Should().BeOfType<CreateTransactionResult.Created>().Which.Transaction.Id;

        // Parent switches currency Point → USD; balance carries over numerically.
        var childService = new ChildService(_fixture.CreateContext(),
            new AuditService(_fixture.CreateContext()), Time);
        await childService.ChangeCurrencyAsync(owner.Id, child.Id, "USD", UniqueIp());

        var after = await CreateService().CreateAsync(owner.Id,
            new CreateTransactionRequest(child.Id, "CREDIT", 2.50m, "After switch"), UniqueIp());

        var created = after.Should().BeOfType<CreateTransactionResult.Created>().Which;
        created.Transaction.CurrencyKey.Should().Be("USD");
        created.RemainingAfter.Should().Be(12.50m); // 10 carried over + 2.50

        // The old row keeps its Point snapshot (SDS §2.1.1). Assert by ID —
        // both rows share the frozen fake-clock timestamp, so index-based
        // ordering would be undefined.
        var rows = await _fixture.CreateContext().Transactions.AsNoTracking()
            .Where(t => t.ChildId == child.Id).ToListAsync();
        rows.Single(t => t.Id == beforeId).CurrencyKey.Should().Be("Point");
        rows.Single(t => t.Id == created.Transaction.Id).CurrencyKey.Should().Be("USD");
    }

    [Fact]
    public async Task Debit_below_zero_returns_422_and_leaves_balance_untouched()
    {
        var (_, owner, child) = await SeedChildAsync();

        var result = await CreateService().CreateAsync(owner.Id,
            new CreateTransactionRequest(child.Id, "DEBIT", 1m, "Too much"), UniqueIp());

        result.Should().BeOfType<CreateTransactionResult.NegativeBalance>();
        (await ReloadAsync(child.Id)).CurrentBalance.Should().Be(0m);

        var rowsAfter = await _fixture.CreateContext().Transactions.CountAsync(t => t.ChildId == child.Id);
        rowsAfter.Should().Be(0); // rolled back — no orphan ledger row
    }

    [Theory]
    [InlineData("0", "zero")]
    [InlineData("-5", "negative")]
    [InlineData("10000000000", "above cap")]
    [InlineData("1.5555", "scale exceeds Point DecimalDigits")]
    public async Task Invalid_amounts_are_rejected(string amountStr, string why)
    {
        var (_, owner, child) = await SeedChildAsync();

        var result = await CreateService().CreateAsync(owner.Id,
            new CreateTransactionRequest(child.Id, "CREDIT", decimal.Parse(amountStr), why), UniqueIp());

        result.Should().BeOfType<CreateTransactionResult.ValidationFailed>();
        (await ReloadAsync(child.Id)).CurrentBalance.Should().Be(0m);
    }

    [Fact]
    public async Task Fractional_scale_checked_against_child_currency_digits()
    {
        var (_, owner, child) = await SeedChildAsync();

        var childService = new ChildService(_fixture.CreateContext(),
            new AuditService(_fixture.CreateContext()), Time);
        await childService.ChangeCurrencyAsync(owner.Id, child.Id, "USD", UniqueIp());

        // USD = 2 digits: 1.234 must be rejected, 1.23 accepted.
        var rejected = await CreateService().CreateAsync(owner.Id,
            new CreateTransactionRequest(child.Id, "CREDIT", 1.234m, "three decimals"), UniqueIp());
        rejected.Should().BeOfType<CreateTransactionResult.ValidationFailed>();

        var accepted = await CreateService().CreateAsync(owner.Id,
            new CreateTransactionRequest(child.Id, "CREDIT", 1.23m, "two decimals"), UniqueIp());
        accepted.Should().BeOfType<CreateTransactionResult.Created>();
    }

    [Fact]
    public async Task Reason_sanitization_strips_control_chars_and_bad_emoji_keeps_whitelist()
    {
        var (_, owner, child) = await SeedChildAsync();

        var result = await CreateService().CreateAsync(owner.Id,
            new CreateTransactionRequest(child.Id, "CREDIT", 1m,
                "Chores\u0007 done \U0001F600 but \U0001F47F sneaks in \u2705\u2705"), UniqueIp());

        var created = result.Should().BeOfType<CreateTransactionResult.Created>().Which;
        // 😀 (U+1F600) IS whitelisted → kept; 👿 (U+1F47F) and ✅ (U+2705)
        // are NOT → stripped; the bell control char is stripped.
        created.Transaction.Reason.Should().Be("Chores done 😀 but  sneaks in");
    }

    [Fact]
    public async Task Unknown_type_and_missing_child_and_foreign_child()
    {
        var (_, owner, child) = await SeedChildAsync();

        var badType = await CreateService().CreateAsync(owner.Id,
            new CreateTransactionRequest(child.Id, "TRANSFER", 1m, "x"), UniqueIp());
        badType.Should().BeOfType<CreateTransactionResult.ValidationFailed>();

        var missing = await CreateService().CreateAsync(owner.Id,
            new CreateTransactionRequest(Guid.NewGuid(), "CREDIT", 1m, "x"), UniqueIp());
        missing.Should().BeOfType<CreateTransactionResult.NotFound>();

        // Foreign household → 404 (API Spec §1.3).
        var (_, ownerB, _) = await SeedChildAsync();
        var foreign = await CreateService().CreateAsync(ownerB.Id,
            new CreateTransactionRequest(child.Id, "CREDIT", 1m, "x"), UniqueIp());
        foreign.Should().BeOfType<CreateTransactionResult.NotFound>();
    }

    // ------------------------------------------------------------------
    // Concurrency — the FOR UPDATE proof (SDS §4)
    // ------------------------------------------------------------------

    [Fact]
    public async Task Concurrent_debits_serialize_and_never_overdraw()
    {
        var (_, owner, child) = await SeedChildAsync();

        // Credit 10.
        await CreateService().CreateAsync(owner.Id,
            new CreateTransactionRequest(child.Id, "CREDIT", 10m, "Seed"), UniqueIp());

        // 10 parents race to debit 3 each — only 3 can fit (9 ≤ 10), the
        // 4th onward must hit negative_balance, never an overdraft.
        var tasks = Enumerable.Range(0, 10).Select(i =>
            CreateService().CreateAsync(owner.Id,
                new CreateTransactionRequest(child.Id, "DEBIT", 3m, $"Debit {i}"), UniqueIp()));

        var results = await Task.WhenAll(tasks);

        var successes = results.OfType<CreateTransactionResult.Created>().Count();
        var negatives = results.OfType<CreateTransactionResult.NegativeBalance>().Count();

        successes.Should().Be(3);
        negatives.Should().Be(7);
        (await ReloadAsync(child.Id)).CurrentBalance.Should().Be(1m); // 10 - 9

        // Ledger integrity: the serialized debits must produce DISTINCT,
        // overdraft-free remainingAfter values {7, 4, 1} plus the seed credit.
        // (Rows share the fake-clock timestamp, so ordering by Id is not the
        // execution order — the multiset is the invariant.)
        var rows = await _fixture.CreateContext().Transactions.AsNoTracking()
            .Where(t => t.ChildId == child.Id)
            .ToListAsync();
        rows.Select(r => r.RemainingAfter).OrderBy(x => x)
            .Should().Equal([1m, 4m, 7m, 10m]);
        rows.Should().AllSatisfy(r => r.RemainingAfter.Should().BeGreaterThanOrEqualTo(0m));
    }

    // ------------------------------------------------------------------
    // GET — keyset timeline (SDS §12)
    // ------------------------------------------------------------------

    private async Task<List<Transaction>> SeedTimelineAsync(Parent owner, Child child, int count)
    {
        var seeded = new List<Transaction>();

        // Fund the account first — mixed DEBIT rows below must not overdraw.
        Time.Advance(TimeSpan.FromMinutes(1));
        (await CreateService().CreateAsync(owner.Id,
            new CreateTransactionRequest(child.Id, "CREDIT", 1000m, "Seed fund"), UniqueIp()))
            .Should().BeOfType<CreateTransactionResult.Created>();

        for (var i = 0; i < count; i++)
        {
            Time.Advance(TimeSpan.FromMinutes(1)); // strictly increasing timestamps
            var amount = (i % 2 == 0 ? 1m : 2m) + (i % 5) * 0.01m;
            var reason = i % 3 == 0 ? "Mowed Lawn" : i % 3 == 1 ? "Dishes" : "Homework";
            var r = await CreateService().CreateAsync(owner.Id,
                new CreateTransactionRequest(child.Id,
                    i % 4 == 0 ? "DEBIT" : "CREDIT",
                    i % 4 == 0 ? 0.5m : amount,
                    reason), UniqueIp());
            r.Should().BeOfType<CreateTransactionResult.Created>();
        }

        await using var db = _fixture.CreateContext();
        seeded = await db.Transactions.AsNoTracking()
            .Where(t => t.ChildId == child.Id)
            .OrderByDescending(t => t.CreatedAt).ThenByDescending(t => t.Id)
            .ToListAsync();
        return seeded;
    }

    [Fact]
    public async Task Keyset_pages_cover_history_exactly_once_with_null_stop_cursor()
    {
        var (_, owner, child) = await SeedChildAsync();
        await SeedTimelineAsync(owner, child, 27);

        var query1 = new TimelineQuery(null, null, null, null, null, null, null, null, 10);
        var page1 = await CreateService().GetChildTimelineAsync(child.Id, query1);
        page1.Items.Should().HaveCount(10);
        page1.NextCursor.Should().NotBeNull();

        var keyset2 = TimelineCursor.Decode(page1.NextCursor);
        keyset2.Should().NotBeNull();
        var page2 = await CreateService().GetChildTimelineAsync(child.Id, query1 with { Keyset = keyset2!.Value });
        page2.Items.Should().HaveCount(10);
        page2.NextCursor.Should().NotBeNull();

        var keyset3 = TimelineCursor.Decode(page2.NextCursor);
        keyset3.Should().NotBeNull();
        var page3 = await CreateService().GetChildTimelineAsync(child.Id, query1 with { Keyset = keyset3!.Value });
        page3.Items.Should().HaveCount(8); // 28 total = 10 + 10 + 8
        page3.NextCursor.Should().BeNull(); // end-of-history signal (SDS §12.3)

        // No gaps, no duplicates, strictly newest-first.
        var all = page1.Items.Concat(page2.Items).Concat(page3.Items).ToList();
        all.Should().HaveCount(28); // 27 seeded + 1 seed-fund credit
        all.Select(i => i.Id).Distinct().Should().HaveCount(28);
        all.Should().BeInDescendingOrder(i => i.CreatedAt);
    }

    [Fact]
    public async Task Filters_apply_before_paging_and_cursor_stays_consistent()
    {
        var (_, owner, child) = await SeedChildAsync();
        await SeedTimelineAsync(owner, child, 30);

        // Type filter: DEBIT rows only, paginated at 3.
        var q = new TimelineQuery(null, TransactionType.Debit, null, null, null, null, null, null, 3);
        var p1 = await CreateService().GetChildTimelineAsync(child.Id, q);
        p1.Items.Should().AllSatisfy(i => i.Type.Should().Be("DEBIT"));

        var collected = p1.Items.ToList();
        var cursor = p1.NextCursor;
        while (cursor is not null)
        {
            var keyset = TimelineCursor.Decode(cursor);
            keyset.Should().NotBeNull();
            var page = await CreateService().GetChildTimelineAsync(child.Id, q with { Keyset = keyset!.Value });
            collected.AddRange(page.Items);
            cursor = page.NextCursor;
        }

        var expectedDebts = await _fixture.CreateContext().Transactions.AsNoTracking()
            .CountAsync(t => t.ChildId == child.Id && t.Type == TransactionType.Debit);
        collected.Should().HaveCount(expectedDebts);
        collected.Select(i => i.Id).Distinct().Should().HaveCount(expectedDebts);

        // Amount-range + reason-search filters.
        var searched = await CreateService().GetChildTimelineAsync(child.Id,
            new TimelineQuery(null, null, null, null, null, null, "mowed", null, 100));
        searched.Items.Should().AllSatisfy(i =>
            i.Reason.Contains("Mowed", StringComparison.OrdinalIgnoreCase).Should().BeTrue());

        var ranged = await CreateService().GetChildTimelineAsync(child.Id,
            new TimelineQuery(null, null, null, null, 1m, 1m, null, null, 100));
        ranged.Items.Should().AllSatisfy(i => i.Amount.Should().Be(1m));
    }

    [Fact]
    public async Task Date_range_filter_uses_utc_day_boundaries()
    {
        var (_, owner, child) = await SeedChildAsync();
        await SeedTimelineAsync(owner, child, 5); // fake clock: 2026-08-16

        var today = DateOnly.FromDateTime(Time.GetUtcNow().UtcDateTime);

        var onDay = await CreateService().GetChildTimelineAsync(child.Id,
            new TimelineQuery(null, null, today, today, null, null, null, null, 100));
        onDay.Items.Should().HaveCount(6); // 5 seeded + 1 seed-fund credit

        var tomorrow = await CreateService().GetChildTimelineAsync(child.Id,
            new TimelineQuery(null, null, today.AddDays(1), today.AddDays(2), null, null, null, null, 100));
        tomorrow.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task Parent_timeline_is_household_scoped_and_filterable_per_child()
    {
        var (_, owner, child) = await SeedChildAsync();
        await SeedTimelineAsync(owner, child, 5);

        var other = await SeedChildAsync(); // different household + child

        var page = await CreateService().GetHouseholdTimelineAsync(owner.Id,
            new TimelineQuery(null, null, null, null, null, null, null, null, 100));

        var own = page.Should().BeOfType<TimelineResult.Ok>().Which.Page;
        own.Items.Should().HaveCount(6); // 5 seeded + 1 seed-fund credit
        own.Items.Should().AllSatisfy(i => i.ChildId.Should().Be(child.Id));

        // childId filter to a foreign child → simply empty (household scoping first).
        var filtered = await CreateService().GetHouseholdTimelineAsync(owner.Id,
            new TimelineQuery(other.Child.Id, null, null, null, null, null, null, null, 100));
        filtered.Should().BeOfType<TimelineResult.Ok>().Which.Page.Items.Should().BeEmpty();

        // Unknown parent → not_found.
        var ghost = await CreateService().GetHouseholdTimelineAsync(Guid.NewGuid().ToString("D"),
            new TimelineQuery(null, null, null, null, null, null, null, null, 10));
        ghost.Should().BeOfType<TimelineResult.NotFound>();
    }

    [Fact]
    public async Task Child_timeline_ignores_childid_filter_always_own_rows()
    {
        var (_, owner, child) = await SeedChildAsync();
        await SeedTimelineAsync(owner, child, 3);

        var other = await SeedChildAsync();
        await SeedTimelineAsync(other.Owner, other.Child, 4);

        // Ask with the OTHER child's id — must still return only own rows.
        var page = await CreateService().GetChildTimelineAsync(child.Id,
            new TimelineQuery(other.Child.Id, null, null, null, null, null, null, null, 100));

        page.Items.Should().HaveCount(4); // 3 seeded + 1 seed-fund credit
        page.Items.Should().AllSatisfy(i => i.ChildId.Should().Be(child.Id));
    }

    // ------------------------------------------------------------------
    // Cursor codec
    // ------------------------------------------------------------------

    [Fact]
    public void Cursor_roundtrips_and_rejects_garbage()
    {
        var ts = new DateTimeOffset(2026, 8, 16, 12, 34, 56, 789, TimeSpan.Zero);
        var id = Guid.NewGuid();

        var encoded = TimelineCursor.Encode(ts, id);
        encoded.Should().NotContainAny("+", "/", "="); // URL-safe

        var decoded = TimelineCursor.Decode(encoded);
        decoded.Should().NotBeNull();
        decoded!.Value.CreatedAt.Should().Be(ts);
        decoded.Value.Id.Should().Be(id);

        TimelineCursor.Decode("not-base64!!!").Should().BeNull();
        TimelineCursor.Decode("").Should().BeNull();
        TimelineCursor.Decode(Convert.ToBase64String("garbage-without-pipe"u8.ToArray())).Should().BeNull();
    }
}
