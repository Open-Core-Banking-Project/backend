using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CoreBanking.CoreLedger.Infrastructure.Persistence.Configurations;

internal sealed class InstitutionRecordConfiguration : IEntityTypeConfiguration<InstitutionRecord>
{
    public void Configure(EntityTypeBuilder<InstitutionRecord> builder)
    {
        builder.ToTable("institutions", "institution", table =>
        {
            table.HasCheckConstraint(
                "ck_institutions_status",
                "status IN ('draft', 'provisioning', 'provisioning_failed', 'pending_activation', 'active', 'suspended')");
            table.HasCheckConstraint("ck_institutions_plan_version", "provisioning_plan_version IS NULL OR provisioning_plan_version > 0");
        });
        builder.HasKey(record => record.InstitutionId);
        builder.HasIndex(record => record.Code).IsUnique();

        builder.Property(record => record.InstitutionId).HasColumnName("institution_id").HasMaxLength(128);
        builder.Property(record => record.Code).HasColumnName("code").HasMaxLength(64);
        builder.Property(record => record.LegalName).HasColumnName("legal_name").HasMaxLength(256);
        builder.Property(record => record.CountryCode).HasColumnName("country_code").HasMaxLength(2).IsFixedLength();
        builder.Property(record => record.BaseCurrency).HasColumnName("base_currency").HasMaxLength(3).IsFixedLength();
        builder.Property(record => record.Status).HasColumnName("status").HasMaxLength(32);
        builder.Property(record => record.CreatedAt).HasColumnName("created_at");
        builder.Property(record => record.UpdatedAt).HasColumnName("updated_at");
        builder.Property(record => record.ProvisioningStartedAt).HasColumnName("provisioning_started_at");
        builder.Property(record => record.ProvisioningFailedAt).HasColumnName("provisioning_failed_at");
        builder.Property(record => record.LastProvisioningAttemptAt).HasColumnName("last_provisioning_attempt_at");
        builder.Property(record => record.ProvisioningPlanVersion).HasColumnName("provisioning_plan_version");
        builder.Property(record => record.ReadyForActivationAt).HasColumnName("ready_for_activation_at");
        builder.Property(record => record.ActivatedAt).HasColumnName("activated_at");
        builder.Property(record => record.SuspendedAt).HasColumnName("suspended_at");
    }
}
