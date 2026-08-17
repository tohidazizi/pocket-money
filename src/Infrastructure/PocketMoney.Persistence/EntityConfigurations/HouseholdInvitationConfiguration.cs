using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PocketMoney.Domain.Entities;

namespace PocketMoney.Persistence.EntityConfigurations;

public sealed class HouseholdInvitationConfiguration : IEntityTypeConfiguration<HouseholdInvitation>
{
    public void Configure(EntityTypeBuilder<HouseholdInvitation> builder)
    {
        builder.ToTable("household_invitations");
        builder.HasKey(i => i.Id);

        builder.Property(i => i.InvitedEmail).IsRequired();
        builder.Property(i => i.TokenHash).IsRequired();

        builder.HasOne(i => i.InvitedByParent)
            .WithMany()
            .HasForeignKey(i => i.InvitedByParentId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.ToTable("audit_logs");
        builder.HasKey(a => a.Id);

        builder.Property(a => a.ActorId).IsRequired();

        // Tenant-scoped audit trail queries (SDS §10): filter by household
        builder.HasIndex(a => a.HouseholdId);
    }
}
