using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PocketMoney.Domain.Entities;

namespace PocketMoney.Persistence.EntityConfigurations;

public sealed class LoginAttemptConfiguration : IEntityTypeConfiguration<LoginAttempt>
{
    public void Configure(EntityTypeBuilder<LoginAttempt> builder)
    {
        builder.ToTable("login_attempts");
        builder.HasKey(l => l.Id);

        // AccountId stored verbatim — even for invalid IDs (audit requirement, SDS §3.3)
        builder.Property(l => l.AccountId).IsRequired();
        builder.Property(l => l.IpAddress).IsRequired();
        builder.Property(l => l.HttpRequestInfo).IsRequired();

        // Global IP ban lookups (SDS §3.3 step 2): failures from one IP in a window
        builder.HasIndex(l => new { l.IpAddress, l.IsSuccessful, l.CreatedAt });
    }
}

public sealed class IpBanConfiguration : IEntityTypeConfiguration<IpBan>
{
    public void Configure(EntityTypeBuilder<IpBan> builder)
    {
        builder.ToTable("ip_bans");
        builder.HasKey(b => b.Id);

        builder.Property(b => b.IpAddress).IsRequired();
        builder.HasIndex(b => b.IpAddress).IsUnique();
    }
}
