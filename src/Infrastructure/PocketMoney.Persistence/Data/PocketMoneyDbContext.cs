using Microsoft.EntityFrameworkCore;
using PocketMoney.Domain.Entities;

namespace PocketMoney.Persistence.Data;

/// <summary>
/// EF Core DbContext for Pocket-Money (SDS §2). Code-first; all entity
/// mappings live in <c>EntityConfigurations/</c>.
/// </summary>
public sealed class PocketMoneyDbContext : DbContext
{
    public PocketMoneyDbContext(DbContextOptions<PocketMoneyDbContext> options)
        : base(options)
    {
    }

    public DbSet<Household> Households => Set<Household>();
    public DbSet<Parent> Parents => Set<Parent>();
    public DbSet<Child> Children => Set<Child>();
    public DbSet<Transaction> Transactions => Set<Transaction>();
    public DbSet<LoginAttempt> LoginAttempts => Set<LoginAttempt>();
    public DbSet<IpBan> IpBans => Set<IpBan>();
    public DbSet<HouseholdInvitation> HouseholdInvitations => Set<HouseholdInvitation>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Applies every IEntityTypeConfiguration<> in this assembly (SDS §2.4)
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(PocketMoneyDbContext).Assembly);
    }
}
