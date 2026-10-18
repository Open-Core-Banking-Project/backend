using CoreBanking.IdentityAccess.Infrastructure.Persistence.Platform;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CoreBanking.IdentityAccess.Infrastructure.Persistence.Configurations;

internal sealed class InstitutionBoundaryRecordConfiguration : IEntityTypeConfiguration<InstitutionBoundaryRecord>
{
    public void Configure(EntityTypeBuilder<InstitutionBoundaryRecord> builder)
    {
        builder.ToTable("institution_boundaries", "platform", table =>
        {
            table.HasCheckConstraint("ck_institution_boundaries_plan_version", "provisioning_plan_version > 0");
            table.HasCheckConstraint("ck_institution_boundaries_status", "status IN ('provisioning', 'active', 'suspended')");
        });
        builder.HasKey(record => record.InstitutionId);
        builder.HasIndex(record => record.SourceEventId).IsUnique();

        builder.Property(record => record.InstitutionId).HasColumnName("institution_id").HasMaxLength(128);
        builder.Property(record => record.ProvisioningPlanVersion).HasColumnName("provisioning_plan_version");
        builder.Property(record => record.Status).HasColumnName("status").HasMaxLength(32);
        builder.Property(record => record.SourceEventId).HasColumnName("source_event_id").HasMaxLength(128);
        builder.Property(record => record.InitialisedAt).HasColumnName("initialised_at");
    }
}
