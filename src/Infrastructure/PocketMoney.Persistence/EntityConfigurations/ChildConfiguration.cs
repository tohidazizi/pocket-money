using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PocketMoney.Domain.Entities;
using PocketMoney.Global;

namespace PocketMoney.Persistence.EntityConfigurations;

public sealed class ChildConfiguration : IEntityTypeConfiguration<Child>
{
    public void Configure(EntityTypeBuilder<Child> builder)
    {
        builder.ToTable("children");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.AccountId)
            .HasMaxLength(Constants.Child.AccountIdLength)
            .IsFixedLength()
            .IsRequired();

        builder.HasIndex(c => c.AccountId).IsUnique();

        // Balance is a running sum of Decimal(13,3) amounts and can legitimately
        // exceed narrower ranges; (19,3) is a strict superset and keeps
        // remaining_after and current_balance at equal width (SRS §4.2).
        builder.Property(c => c.CurrentBalance)
            .HasPrecision(19, 3)
            .HasDefaultValue(0.000m);

        builder.Property(c => c.DisplayName)
            .HasMaxLength(Constants.Child.DisplayNameMaxLength)
            .IsRequired();

        // Currency key into CurrencyType (SDS §2.1.1)
        builder.Property(c => c.CurrencyKey)
            .HasMaxLength(CurrencyType.KeyMaxLength)
            .IsRequired();

        builder.HasOne(c => c.Creator)
            .WithMany()
            .HasForeignKey(c => c.CreatorId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
