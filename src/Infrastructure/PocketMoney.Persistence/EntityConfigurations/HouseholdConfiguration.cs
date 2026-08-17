using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PocketMoney.Domain.Entities;
using PocketMoney.Global;

namespace PocketMoney.Persistence.EntityConfigurations;

public sealed class HouseholdConfiguration : IEntityTypeConfiguration<Household>
{
    public void Configure(EntityTypeBuilder<Household> builder)
    {
        builder.ToTable("households");
        builder.HasKey(h => h.Id);

        // Default currency for new children (SDS §2.1.1) — same key constraint
        // as Child.CurrencyKey
        builder.Property(h => h.DefaultCurrencyKey)
            .HasMaxLength(CurrencyType.KeyMaxLength)
            .IsRequired();
    }
}
