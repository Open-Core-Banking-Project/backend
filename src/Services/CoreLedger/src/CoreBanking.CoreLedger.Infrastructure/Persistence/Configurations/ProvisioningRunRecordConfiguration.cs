using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CoreBanking.CoreLedger.Infrastructure.Persistence.Configurations;

internal sealed class ProvisioningRunRecordConfiguration : IEntityTypeConfiguration<ProvisioningRunRecord>
{
    public void Configure(EntityTypeBuilder<ProvisioningRunRecord> builder)
    {
        builder.ToTable("provisioning_runs", "institution", table =>
        {
            table.HasCheckConstraint("ck_provisioning_runs_plan_version", "plan_version > 0");
            table.HasCheckConstraint(
                "ck_provisioning_runs_status",
                "status IN ('provisioning', 'provisioning_failed', 'pending_activation', 'completed')");
        });
        builder.HasKey(record => new { record.InstitutionId, record.ProvisioningRunId });
        builder.HasIndex(record => record.InstitutionId)
            .IsUnique()
            .HasFilter("status IN ('provisioning', 'provisioning_failed', 'pending_activation')");

        builder.Property(record => record.InstitutionId).HasColumnName("institution_id").HasMaxLength(128);
        builder.Property(record => record.ProvisioningRunId).HasColumnName("provisioning_run_id").HasMaxLength(128);
        builder.Property(record => record.PlanVersion).HasColumnName("plan_version");
        builder.Property(record => record.Status).HasColumnName("status").HasMaxLength(32);
        builder.Property(record => record.StartedAt).HasColumnName("started_at");
        builder.Property(record => record.CompletedAt).HasColumnName("completed_at");
        builder.Property(record => record.UpdatedAt).HasColumnName("updated_at");

        builder.HasOne<InstitutionRecord>()
            .WithMany()
            .HasForeignKey(record => record.InstitutionId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
