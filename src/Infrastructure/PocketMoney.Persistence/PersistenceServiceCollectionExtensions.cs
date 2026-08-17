using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PocketMoney.Persistence.Data;

namespace PocketMoney.Persistence;

/// <summary>
/// DI registration for the persistence layer (SDS §1.3 Infrastructure).
/// </summary>
public static class PersistenceServiceCollectionExtensions
{
    public static IServiceCollection AddPocketMoneyPersistence(
        this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("PocketMoney")
            ?? throw new InvalidOperationException(
                "Missing connection string 'PocketMoney' (SDS §1.4).");

        services.AddDbContext<PocketMoneyDbContext>(options =>
            options.UseNpgsql(connectionString)
                   .UseSnakeCaseNamingConvention()); // SDS references snake_case columns

        return services;
    }
}
