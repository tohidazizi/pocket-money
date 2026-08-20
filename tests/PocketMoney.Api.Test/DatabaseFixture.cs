using Microsoft.EntityFrameworkCore;
using PocketMoney.Domain.Entities;
using PocketMoney.Global;
using PocketMoney.Persistence.Data;
using Xunit;

namespace PocketMoney.Api.Test;

/// <summary>
/// Shared fixture: one dedicated Postgres database on the dev instance,
/// wiped + migrated fresh for each test run (CI/CD doc §3.2 — CI uses a
/// service container; locally the Windows Docker Postgres on :5432).
///
/// The database name is MACHINE-scoped (MACHINE_NAME suffix): the repo is
/// exercised from both Windows and WSL against the same Docker Postgres,
/// and a hardcoded name meant one side's `DROP DATABASE ... WITH (FORCE)`
/// severed the other side's live test connections mid-run ("I/O operation
/// aborted" Npgsql exceptions). Override with POCKETMONEY_TEST_DB when an
/// isolated database is wanted (CI).
/// </summary>
public sealed class DatabaseFixture : IAsyncLifetime
{
    public static string GetConnectionString()
    {
        var database = Environment.GetEnvironmentVariable("POCKETMONEY_TEST_DB");
        if (string.IsNullOrWhiteSpace(database))
        {
            // Strip anything outside [a-z0-9_] — WSL MACHINE_NAME can carry
            // hyphens (e.g. "Tohid-Laptop") which are invalid unquoted SQL.
            var suffix = new string((Environment.MachineName ?? "local")
                .ToLowerInvariant()
                .Select(c => char.IsLetterOrDigit(c) ? c : '_')
                .ToArray())
                .Trim('_');
            if (suffix.Length == 0) suffix = "local";
            database = $"pocketmoney_test_{suffix}";
        }
        return $"Host=localhost;Port=5432;Database={database};Username=postgres;Password=postgres";
    }

    private const string AdminConnectionString =
        "Host=localhost;Port=5432;Database=postgres;Username=postgres;Password=postgres";

    public async Task InitializeAsync()
    {
        var connectionString = GetConnectionString();
        var database = new Npgsql.NpgsqlConnectionStringBuilder(connectionString).Database!;

        var options = new DbContextOptionsBuilder<PocketMoneyDbContext>()
            .UseNpgsql(connectionString).UseSnakeCaseNamingConvention().Options;

        await using var admin = new Npgsql.NpgsqlConnection(AdminConnectionString);
        await admin.OpenAsync();

        await using (var drop = new Npgsql.NpgsqlCommand(
            $"DROP DATABASE IF EXISTS {database} WITH (FORCE)", admin))
        {
            await drop.ExecuteNonQueryAsync();
        }
        await using (var create = new Npgsql.NpgsqlCommand($"CREATE DATABASE {database}", admin))
        {
            await create.ExecuteNonQueryAsync();
        }

        await using var db = new PocketMoneyDbContext(options);
        await db.Database.MigrateAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public PocketMoneyDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<PocketMoneyDbContext>()
            .UseNpgsql(GetConnectionString()).UseSnakeCaseNamingConvention().Options);

    /// <summary>Seeds a child with a known PIN hash for login tests.</summary>
    public async Task<Child> SeedChildAsync(string pin, string? accountId = null, string displayName = "Mia")
    {
        await using var db = CreateContext();
        var household = new Household { DisplayName = "Test Home" };
        var parent = new Parent
        {
            Id = Guid.NewGuid().ToString("D"),
            Email = "parent@test.local",
        };
        var child = new Child
        {
            AccountId = accountId ?? PocketMoney.Application.Base31Generator.GenerateAccountId(),
            DisplayName = displayName,
            PinHash = PocketMoney.Application.PinHasher.Hash(pin),
            CurrencyKey = CurrencyType.PointKey,
        };
        parent.HouseholdId = household.Id;
        child.HouseholdId = household.Id;
        child.CreatorId = parent.Id;

        db.Households.Add(household);
        db.Parents.Add(parent);
        db.Children.Add(child);
        await db.SaveChangesAsync();
        return child;
    }
}
