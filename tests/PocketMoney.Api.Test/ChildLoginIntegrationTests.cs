using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PocketMoney.Application;
using PocketMoney.Application.Contract;
using PocketMoney.Global;
using Xunit;

namespace PocketMoney.Api.Test;

/// <summary>
/// Vertical slice integration tests: child login + lockout ladder (SDS §3.3)
/// against real PostgreSQL. Deterministic via injected TimeProvider.
/// </summary>
[Collection("database")]
public class ChildLoginIntegrationTests
{
    private readonly DatabaseFixture _fixture;

    // The IP-ban ladder (SDS §3.3) keys on IP. Each test gets its own IP so
    // attempts never accumulate across tests; within a test the same IP is
    // reused so the ladder counts correctly.
    private static ClientInfo NewClient() =>
        new($"10.{Random.Shared.Next(1, 254)}.{Random.Shared.Next(1, 254)}.{Random.Shared.Next(1, 254)}",
            "POST /api/v1/auth/child/login | UA: test");

    public ChildLoginIntegrationTests(DatabaseFixture fixture) => _fixture = fixture;

    private ChildAuthService CreateService(FakeTimeProvider time)
    {
        // The issuer needs config; a stub avoids Jwt key plumbing in tests.
        return new ChildAuthService(_fixture.CreateContext(), new StubTokenIssuer(), time);
    }

    [Fact]
    public async Task Successful_login_returns_token_and_resets_counter()
    {
        var client = NewClient();
        var child = await _fixture.SeedChildAsync("1234");
        var service = CreateService(new FakeTimeProvider());

        var result = await service.LoginAsync(child.AccountId, "1234", client);

        result.Should().BeOfType<ChildLoginResult.Success>()
            .Which.DisplayName.Should().Be("Mia");

        await using var db = _fixture.CreateContext();
        var reloaded = await db.Children.FirstAsync(c => c.Id == child.Id);
        reloaded.UnsuccessfulLoginAttempts.Should().Be(0);
    }

    [Fact]
    public async Task Wrong_pin_returns_invalid_credentials()
    {
        var client = NewClient();
        var child = await _fixture.SeedChildAsync("1234");
        var service = CreateService(new FakeTimeProvider());

        var result = await service.LoginAsync(child.AccountId, "9999", client);

        result.Should().BeOfType<ChildLoginResult.InvalidCredentials>();
    }

    [Fact]
    public async Task Lowercase_account_id_is_normalized_to_uppercase()
    {
        var client = NewClient();
        var child = await _fixture.SeedChildAsync("1234", accountId: "MJ74K");
        var service = CreateService(new FakeTimeProvider());

        var result = await service.LoginAsync("mj74k", "1234", client);

        result.Should().BeOfType<ChildLoginResult.Success>();
    }

    [Fact]
    public async Task Malformed_input_returns_validation_failed_and_is_audited()
    {
        var client = NewClient();
        var service = CreateService(new FakeTimeProvider());

        var result = await service.LoginAsync("bad", "12", client);

        result.Should().BeOfType<ChildLoginResult.ValidationFailed>();

        await using var db = _fixture.CreateContext();
        (await db.LoginAttempts.CountAsync(l => l.AccountId == "bad")).Should().Be(1);
    }

    [Fact]
    public async Task Unknown_account_returns_invalid_credentials_and_is_audited()
    {
        var client = NewClient();
        var service = CreateService(new FakeTimeProvider());

        var result = await service.LoginAsync("ZZZZZ", "1234", client);

        result.Should().BeOfType<ChildLoginResult.InvalidCredentials>();

        await using var db = _fixture.CreateContext();
        (await db.LoginAttempts.CountAsync(l => l.AccountId == "ZZZZZ" && !l.IsSuccessful)).Should().Be(1);
    }

    [Fact]
    public async Task Lockout_ladder_3_failures_locks_for_5_minutes()
    {
        var client = NewClient();
        var child = await _fixture.SeedChildAsync("1234");
        var time = new FakeTimeProvider();
        var service = CreateService(time);

        for (var i = 0; i < Constants.Lockout.MaxFailedAttemptsPerLockout; i++)
            await service.LoginAsync(child.AccountId, "0000", client);

        var locked = await service.LoginAsync(child.AccountId, "1234", client); // correct PIN still fails

        locked.Should().BeOfType<ChildLoginResult.Locked>()
            .Which.LockedUntil.Should().Be(time.GetUtcNow().AddMinutes(Constants.Lockout.FirstLockoutMinutes));
    }

    [Fact]
    public async Task Lockout_ladder_6_failures_locks_for_15_minutes()
    {
        var client = NewClient();
        var child = await _fixture.SeedChildAsync("1234");
        var time = new FakeTimeProvider();
        var service = CreateService(time);

        for (var i = 0; i < Constants.Lockout.MaxFailedAttemptsPerLockout * 2; i++)
            await service.LoginAsync(child.AccountId, "0000", client);

        var locked = await service.LoginAsync(child.AccountId, "1234", client);

        locked.Should().BeOfType<ChildLoginResult.Locked>()
            .Which.LockedUntil.Should().Be(time.GetUtcNow().AddMinutes(Constants.Lockout.SecondLockoutMinutes));
    }

    [Fact]
    public async Task Lockout_ladder_9_failures_locks_permanently()
    {
        var client = NewClient();
        var child = await _fixture.SeedChildAsync("1234");
        var time = new FakeTimeProvider();
        var service = CreateService(time);

        for (var i = 0; i < Constants.Lockout.PermanentLockThreshold; i++)
            await service.LoginAsync(child.AccountId, "0000", client);

        var locked = await service.LoginAsync(child.AccountId, "1234", client);

        locked.Should().BeOfType<ChildLoginResult.PermanentlyLocked>();

        await using var db = _fixture.CreateContext();
        var reloaded = await db.Children.FirstAsync(c => c.Id == child.Id);
        reloaded.IsPermanentlyLocked.Should().BeTrue();
    }

    [Fact]
    public async Task Timed_lock_expires_and_allows_login_again()
    {
        var client = NewClient();
        var child = await _fixture.SeedChildAsync("1234");
        var time = new FakeTimeProvider();
        var service = CreateService(time);

        for (var i = 0; i < Constants.Lockout.MaxFailedAttemptsPerLockout; i++)
            await service.LoginAsync(child.AccountId, "0000", client);

        time.Advance(TimeSpan.FromMinutes(Constants.Lockout.FirstLockoutMinutes + 1));

        var result = await service.LoginAsync(child.AccountId, "1234", client);
        result.Should().BeOfType<ChildLoginResult.Success>();
    }

    [Fact]
    public async Task Ip_ban_after_threshold_failures_returns_banned()
    {
        var client = NewClient();
        var child = await _fixture.SeedChildAsync("1234");
        var time = new FakeTimeProvider();
        var service = CreateService(time);

        // 10 failures from one IP within the window triggers the ban (NFR-4)
        for (var i = 0; i < Constants.IpBan.FailureThreshold; i++)
            await service.LoginAsync(child.AccountId, "0000", client);

        var result = await service.LoginAsync(child.AccountId, "1234", client);

        result.Should().BeOfType<ChildLoginResult.IpBanned>()
            .Which.BannedUntil.Should().Be(time.GetUtcNow().AddDays(Constants.IpBan.FirstBanDays));
    }

    private sealed class StubTokenIssuer : IChildTokenIssuer
    {
        public ChildToken Issue(Guid childId, Guid householdId, Guid securityStamp) =>
            new("stub-token", DateTimeOffset.UtcNow.AddDays(Constants.Child.TokenLifetimeDays));
    }
}
