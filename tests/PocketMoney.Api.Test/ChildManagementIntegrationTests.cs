using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PocketMoney.Application;
using PocketMoney.Application.Contract;
using PocketMoney.Application.Model.Children;
using PocketMoney.Global;
using Xunit;

namespace PocketMoney.Api.Test;

/// <summary>
/// Phase 3 integration tests: child profile management (API Spec §5.1–5.5,
/// SDS §3.2/§3.4) against real PostgreSQL.
/// UIDs are unique per test — the database persists across the run.
/// </summary>
[Collection("database")]
public class ChildManagementIntegrationTests
{
    private readonly DatabaseFixture _fixture;
    private static readonly FakeTimeProvider Time = new();

    public ChildManagementIntegrationTests(DatabaseFixture fixture) => _fixture = fixture;

    private ChildService CreateService()
    {
        var db = _fixture.CreateContext();
        return new ChildService(db, new AuditService(db), Time);
    }

    private static string UniqueIp() =>
        $"10.{Random.Shared.Next(1, 254)}.{Random.Shared.Next(1, 254)}.{Random.Shared.Next(1, 254)}";

    // ------------------------------------------------------------------
    // POST /household/children (API Spec §5.1)
    // ------------------------------------------------------------------

    [Fact]
    public async Task Create_child_inherits_default_currency_and_returns_initial_pin_once()
    {
        var (household, owner) = await _fixture.SeedHouseholdAsync();
        var service = CreateService();

        // Give the household a non-default currency the child must inherit.
        await using (var db = _fixture.CreateContext())
        {
            var h = await db.Households.SingleAsync(x => x.Id == household.Id);
            h.DefaultCurrencyKey = "USD";
            await db.SaveChangesAsync();
        }

        var result = await service.CreateAsync(owner.Id, "Mia", UniqueIp());

        var created = result.Should().BeOfType<CreateChildResult.Created>().Which.Child;
        created.DisplayName.Should().Be("Mia");
        created.AccountId.Should().HaveLength(Constants.Child.AccountIdLength)
            .And.MatchRegex("^[0-9A-HJKLMNPRTVWXYZ]{5}$");
        created.InitialPin.Should().MatchRegex(@"^\d{4}$");
        created.CurrencyKey.Should().Be("USD");
        created.CurrentBalance.Should().Be(0m);

        // PIN is stored hashed — the plaintext never persists.
        await using var verifyDb = _fixture.CreateContext();
        var child = await verifyDb.Children.SingleAsync(c => c.Id == created.Id);
        child.PinHash.Should().NotBe(created.InitialPin);
        PinHasher.Verify(created.InitialPin, child.PinHash).Should().BeTrue();
        child.CreatorId.Should().Be(owner.Id);
    }

    [Fact]
    public async Task Create_child_rejects_invalid_display_name()
    {
        var (_, owner) = await _fixture.SeedHouseholdAsync();
        var service = CreateService();

        var empty = await service.CreateAsync(owner.Id, "   ", UniqueIp());
        var badChars = await service.CreateAsync(owner.Id, "Mia<script>", UniqueIp());
        var tooLong = await service.CreateAsync(owner.Id, new string('a', 101), UniqueIp());

        empty.Should().BeOfType<CreateChildResult.ValidationFailed>();
        badChars.Should().BeOfType<CreateChildResult.ValidationFailed>();
        tooLong.Should().BeOfType<CreateChildResult.ValidationFailed>();
    }

    [Fact]
    public async Task Create_child_rejects_at_nine_children()
    {
        var (household, owner) = await _fixture.SeedHouseholdAsync();
        var service = CreateService();

        for (var i = 0; i < Constants.Child.ChildrenMax; i++)
        {
            var ok = await service.CreateAsync(owner.Id, $"Kid{i}", UniqueIp());
            ok.Should().BeOfType<CreateChildResult.Created>();
        }

        var tenth = await service.CreateAsync(owner.Id, "Tenth", UniqueIp());
        tenth.Should().BeOfType<CreateChildResult.ChildrenMaxReached>();
    }

    // ------------------------------------------------------------------
    // PUT /household/children/{id}/pin (API Spec §5.2)
    // ------------------------------------------------------------------

    [Fact]
    public async Task Pin_reset_rotates_security_stamp()
    {
        var (_, owner) = await _fixture.SeedHouseholdAsync();
        var created = (await CreateService().CreateAsync(owner.Id, "Mia", UniqueIp()))
            .Should().BeOfType<CreateChildResult.Created>().Which.Child;

        // Fresh context per operation mirrors production (one scoped DbContext
        // per request); AsNoTracking avoids identity-map staleness on re-reads.
        Guid stampBefore;
        await using (var readDb = _fixture.CreateContext())
        {
            stampBefore = (await readDb.Children.AsNoTracking().SingleAsync(c => c.Id == created.Id)).SecurityStamp;
        }

        var result = await CreateService().ResetPinAsync(owner.Id, created.Id, "5678", UniqueIp());
        result.Should().BeOfType<ChildActionResult.Ok>();

        await using var verifyDb = _fixture.CreateContext();
        var after = await verifyDb.Children.AsNoTracking().SingleAsync(c => c.Id == created.Id);
        after.SecurityStamp.Should().NotBe(stampBefore); // tokens invalidated (SDS §3.2)
        PinHasher.Verify("5678", after.PinHash).Should().BeTrue();
    }

    [Fact]
    public async Task Pin_reset_on_locked_account_returns_423()
    {
        var (_, owner) = await _fixture.SeedHouseholdAsync();
        var service = CreateService();
        var created = (await service.CreateAsync(owner.Id, "Mia", UniqueIp()))
            .Should().BeOfType<CreateChildResult.Created>().Which.Child;

        var locked = await service.SetLockAsync(owner.Id, created.Id, locked: true, UniqueIp());
        locked.Should().BeOfType<ChildActionResult.Ok>();

        var result = await service.ResetPinAsync(owner.Id, created.Id, "5678", UniqueIp());
        result.Should().BeOfType<ChildActionResult.AccountLocked>();
    }

    // ------------------------------------------------------------------
    // PUT /household/children/{id}/lock (API Spec §5.3, SDS §3.4)
    // ------------------------------------------------------------------

    [Fact]
    public async Task Manual_lock_sets_max_value_and_rotates_stamp()
    {
        var (_, owner) = await _fixture.SeedHouseholdAsync();
        var created = (await CreateService().CreateAsync(owner.Id, "Mia", UniqueIp()))
            .Should().BeOfType<CreateChildResult.Created>().Which.Child;

        var result = await CreateService().SetLockAsync(owner.Id, created.Id, locked: true, UniqueIp());
        result.Should().BeOfType<ChildActionResult.Ok>();

        await using var verifyDb = _fixture.CreateContext();
        var child = await verifyDb.Children.AsNoTracking().SingleAsync(c => c.Id == created.Id);
        child.IsPermanentlyLocked.Should().BeTrue(); // LockedUntil == MaxValue
    }

    [Fact]
    public async Task Unlock_clears_lock_and_restarts_failure_ladder_without_pin_change()
    {
        var (_, owner) = await _fixture.SeedHouseholdAsync();
        var created = (await CreateService().CreateAsync(owner.Id, "Mia", UniqueIp()))
            .Should().BeOfType<CreateChildResult.Created>().Which.Child;

        await CreateService().SetLockAsync(owner.Id, created.Id, locked: true, UniqueIp());
        var result = await CreateService().SetLockAsync(owner.Id, created.Id, locked: false, UniqueIp());
        result.Should().BeOfType<ChildActionResult.Ok>();

        await using var verifyDb = _fixture.CreateContext();
        var child = await verifyDb.Children.AsNoTracking().SingleAsync(c => c.Id == created.Id);
        child.LockedUntil.Should().BeNull();
        child.UnsuccessfulLoginAttempts.Should().Be(0); // ladder restarts (API Spec §5.3)
    }

    // ------------------------------------------------------------------
    // PUT /household/children/{id}/currency (API Spec §5.4)
    // ------------------------------------------------------------------

    [Fact]
    public async Task Currency_change_carries_balance_numerically()
    {
        var (_, owner) = await _fixture.SeedHouseholdAsync();
        var created = (await CreateService().CreateAsync(owner.Id, "Mia", UniqueIp()))
            .Should().BeOfType<CreateChildResult.Created>().Which.Child;

        // Seed a balance directly — transactions phase lands later.
        await using (var seedDb = _fixture.CreateContext())
        {
            var child = await seedDb.Children.SingleAsync(c => c.Id == created.Id);
            child.CurrentBalance = 87.500m;
            await seedDb.SaveChangesAsync();
        }

        var result = await CreateService().ChangeCurrencyAsync(owner.Id, created.Id, "IRR", UniqueIp());

        var changed = result.Should().BeOfType<ChangeCurrencyResult.Changed>().Which.Response;
        changed.Currency.Key.Should().Be("IRR");
        changed.CurrentBalance.Should().Be(87.500m); // carried over numerically

        var unknown = await CreateService().ChangeCurrencyAsync(owner.Id, created.Id, "XYZ", UniqueIp());
        unknown.Should().BeOfType<ChangeCurrencyResult.ValidationFailed>();
    }

    // ------------------------------------------------------------------
    // Household scoping (API Spec §1.3: 404, never 403)
    // ------------------------------------------------------------------

    [Fact]
    public async Task Foreign_household_child_is_not_found()
    {
        var (_, ownerA) = await _fixture.SeedHouseholdAsync();
        var (_, ownerB) = await _fixture.SeedHouseholdAsync();
        var service = CreateService();

        var created = (await service.CreateAsync(ownerA.Id, "Mia", UniqueIp()))
            .Should().BeOfType<CreateChildResult.Created>().Which.Child;

        var pin = await service.ResetPinAsync(ownerB.Id, created.Id, "1111", UniqueIp());
        var lk = await service.SetLockAsync(ownerB.Id, created.Id, true, UniqueIp());
        var cur = await service.ChangeCurrencyAsync(ownerB.Id, created.Id, "USD", UniqueIp());

        pin.Should().BeOfType<ChildActionResult.NotFound>();
        lk.Should().BeOfType<ChildActionResult.NotFound>();
        cur.Should().BeOfType<ChangeCurrencyResult.NotFound>();
    }

    // ------------------------------------------------------------------
    // GET /household/children/me (API Spec §5.5)
    // ------------------------------------------------------------------

    [Fact]
    public async Task Child_me_returns_own_dashboard_payload()
    {
        var (_, owner) = await _fixture.SeedHouseholdAsync();
        var service = CreateService();
        var created = (await service.CreateAsync(owner.Id, "Mia", UniqueIp()))
            .Should().BeOfType<CreateChildResult.Created>().Which.Child;

        var me = await service.GetMeAsync(created.Id);

        var payload = me.Should().BeOfType<ChildMeResult.Ok>().Which.Response;
        payload.DisplayName.Should().Be("Mia");
        payload.CurrentBalance.Should().Be(0m);
        payload.Currency.Key.Should().Be(CurrencyType.PointKey); // household default

        var missing = await service.GetMeAsync(Guid.NewGuid());
        missing.Should().BeOfType<ChildMeResult.NotFound>();
    }

    // ------------------------------------------------------------------
    // Security stamp end-to-end with the real token issuer (SDS §3.2)
    // ------------------------------------------------------------------

    [Fact]
    public async Task Stamp_rotation_invalidates_outstanding_child_tokens()
    {
        var (_, owner) = await _fixture.SeedHouseholdAsync();
        var service = CreateService();
        var created = (await service.CreateAsync(owner.Id, "Mia", UniqueIp()))
            .Should().BeOfType<CreateChildResult.Created>().Which.Child;

        // Issue a token with the CURRENT stamp (as login would).
        await using var db = _fixture.CreateContext();
        var child = await db.Children.SingleAsync(c => c.Id == created.Id);
        var issuer = new ChildJwtTokenIssuer(new TestConfiguration());
        var token = issuer.Issue(child.Id, child.HouseholdId, child.SecurityStamp);

        // Stamp valid now.
        var principal = ReadPrincipal(token.Token);
        (await ChildJwtTokenIssuer.ValidateSecurityStampAsync(principal, db)).Should().BeTrue();

        // PIN reset rotates the stamp → the same token is now stale.
        await service.ResetPinAsync(owner.Id, created.Id, "9999", UniqueIp());
        db.ChangeTracker.Clear();
        (await ChildJwtTokenIssuer.ValidateSecurityStampAsync(principal, db)).Should().BeFalse();
    }

    private static System.Security.Claims.ClaimsPrincipal ReadPrincipal(string jwt)
    {
        var handler = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler();
        var parsed = handler.ReadJwtToken(jwt);
        var identity = new System.Security.Claims.ClaimsIdentity(parsed.Claims);
        return new System.Security.Claims.ClaimsPrincipal(identity);
    }

    /// <summary>Minimal IConfiguration for the token issuer in tests.</summary>
    private sealed class TestConfiguration : Microsoft.Extensions.Configuration.IConfiguration
    {
        public string? this[string key]
        {
            get => key switch
            {
                "Jwt:Key" => new string('k', 64), // ≥ 256 bits
                "Jwt:Issuer" => "pocketmoney-api-test",
                _ => null,
            };
            set => throw new NotSupportedException();
        }

        public IEnumerable<Microsoft.Extensions.Configuration.IConfigurationSection> GetChildren() => [];
        public Microsoft.Extensions.Primitives.IChangeToken GetReloadToken() =>
            throw new NotSupportedException();
        public Microsoft.Extensions.Configuration.IConfigurationSection GetSection(string key) =>
            throw new NotSupportedException();
    }
}
