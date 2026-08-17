using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PocketMoney.Domain.Entities;

namespace PocketMoney.Persistence.EntityConfigurations;

public sealed class ParentConfiguration : IEntityTypeConfiguration<Parent>
{
    public void Configure(EntityTypeBuilder<Parent> builder)
    {
        builder.ToTable("parents");
        builder.HasKey(p => p.Id); // Firebase User UID

        // One Firebase user belongs to at most one household, ever.
        // The PK already makes Id globally unique; this composite unique
        // index is explicit defense-in-depth documenting that contract.
        builder.HasIndex(p => new { p.Id, p.HouseholdId }).IsUnique();

        builder.Property(p => p.Email).IsRequired();

        // IsRequired but may be the empty-string sentinel ("no PIN yet", SDS §2.3)
        builder.Property(p => p.ParentPinHash).IsRequired();
    }
}
