using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CoreBanking.CoreLedger.Infrastructure.Persistence.Configurations;

internal sealed class ProvisioningStepRecordConfiguration : IEntityTypeConfiguration<ProvisioningStepRecord>
{
    public void Configure(EntityTypeBuilder<ProvisioningStepRecord> builder)
    {
        builder.ToTable("provisioning_steps", "institution", table =>
        {
            table.HasCheckConstraint("ck_provisioning_steps_status", "status IN ('pending', 'initialised', 'failed')");
            table.HasCheckConstraint(
                "ck_provisioning_steps_failure_code",
                "(status = 'failed' AND failure_code IS NOT NULL) OR (status <> 'failed' AND failure_code IS NULL)");
        });
        builder.HasKey(record => new { record.InstitutionId, record.ProvisioningRunId, record.ServiceId });

        builder.Property(record => record.InstitutionId).HasColumnName("institution_id").HasMaxLength(128);
        builder.Property(record => record.ProvisioningRunId).HasColumnName("provisioning_run_id").HasMaxLength(128);
        builder.Property(record => record.ServiceId).HasColumnName("service_id").HasMaxLength(128);
        builder.Property(record => record.Status).HasColumnName("status").HasMaxLength(32);
        builder.Property(record => record.FailureCode).HasColumnName("failure_code").HasMaxLength(128);
        builder.Property(record => record.AcknowledgementEventId).HasColumnName("acknowledgement_event_id").HasMaxLength(128);
        builder.Property(record => record.AcknowledgedAt).HasColumnName("acknowledged_at");
        builder.Property(record => record.UpdatedAt).HasColumnName("updated_at");

        builder.HasOne(record => record.Run)
            .WithMany(run => run.Steps)
            .HasForeignKey(record => new { record.InstitutionId, record.ProvisioningRunId })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
