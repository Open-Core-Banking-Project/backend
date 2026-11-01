using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CoreBanking.CustomerOnboarding.Infrastructure.Persistence.Configurations;

/// <summary>
/// Protected-data corrections. The table is append-only: there is no update path and the runtime principal is
/// granted no additional rights beyond the mutable customer schema. Decision: epic 03 CUS-UC-06.
/// </summary>
public static class CorrectionRecordConfiguration
{
    public static void ApplyCorrectionRecords(this ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.ApplyConfiguration(new CustomerCorrectionEntityConfiguration());
    }

    private sealed class CustomerCorrectionEntityConfiguration : IEntityTypeConfiguration<CustomerCorrectionRecord>
    {
        public void Configure(EntityTypeBuilder<CustomerCorrectionRecord> entity)
        {
            entity.ToTable("customer_corrections", "customer", table =>
            {
                table.HasCheckConstraint(
                    "ck_customer_corrections_reason",
                    "length(btrim(reason)) >= 3");
                table.HasCheckConstraint(
                    "ck_customer_corrections_reverification",
                    "NOT requires_reverification OR requirement_code IS NOT NULL");
                table.HasCheckConstraint(
                    "ck_customer_corrections_changed",
                    "prior_value_reference <> new_value_reference");
            });

            entity.HasKey(record => new { record.InstitutionId, record.CorrectionId });
            entity.Property(record => record.InstitutionId).HasColumnName("institution_id").HasMaxLength(128);
            entity.Property(record => record.CorrectionId).HasColumnName("correction_id").HasMaxLength(64);
            entity.Property(record => record.CustomerId).HasColumnName("customer_id").HasMaxLength(64);
            entity.Property(record => record.FieldCode).HasColumnName("field_code").HasMaxLength(64);
            entity.Property(record => record.PriorValueReference).HasColumnName("prior_value_reference").HasMaxLength(256);
            entity.Property(record => record.NewValueReference).HasColumnName("new_value_reference").HasMaxLength(256);
            entity.Property(record => record.Reason).HasColumnName("reason").HasMaxLength(500);
            entity.Property(record => record.EvidenceReference).HasColumnName("evidence_reference").HasMaxLength(256);
            entity.Property(record => record.RequiresReverification).HasColumnName("requires_reverification");
            entity.Property(record => record.RequirementCode).HasColumnName("requirement_code").HasMaxLength(64);
            entity.Property(record => record.ApprovalReference).HasColumnName("approval_reference").HasMaxLength(128);
            entity.Property(record => record.RecordedBy).HasColumnName("recorded_by").HasMaxLength(128);
            entity.Property(record => record.RecordedAt).HasColumnName("recorded_at");

            entity.HasOne(record => record.Customer)
                .WithMany()
                .HasForeignKey(record => new { record.InstitutionId, record.CustomerId })
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(record => new { record.InstitutionId, record.CustomerId, record.RecordedAt })
                .HasDatabaseName("ix_customer_corrections_customer");
        }
    }
}
