using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PocketMoney.Domain.Entities;
using PocketMoney.Global;

namespace PocketMoney.Persistence.EntityConfigurations;

public sealed class TransactionConfiguration : IEntityTypeConfiguration<Transaction>
{
    public void Configure(EntityTypeBuilder<Transaction> builder)
    {
        builder.ToTable("transactions");
        builder.HasKey(t => t.Id);

        builder.Property(t => t.Amount)
            .HasPrecision(13, 3)
            .IsRequired();

        builder.Property(t => t.RemainingAfter)
            .HasPrecision(19, 3)
            .IsRequired();

        builder.Property(t => t.Reason)
            .HasMaxLength(Constants.Transaction.ReasonMaxLength)
            .IsRequired();

        // Optimized query performance for child timeline (FR-C4, SDS §12 keyset paging).
        // created_at DESC for newest-first order; Id DESC as tiebreaker so rows
        // sharing a timestamp page without gaps or duplicates.
        builder.HasIndex(t => new { t.ChildId, t.CreatedAt, t.Id })
            .IsDescending(false, true, true);
    }
}
