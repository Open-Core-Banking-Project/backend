using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CoreBanking.CustomerOnboarding.Infrastructure.Persistence.Configurations;

/// <summary>
/// The <c>audit</c> schema: append-only evidence the application principal can only insert and read.
/// Decision: P1-E03-D14.
/// </summary>
public static class AuditRecordConfiguration
{
    public static void ApplyAuditRecords(this ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.ApplyConfiguration(new CustomerAuditEventEntityConfiguration());
    }

    private sealed class CustomerAuditEventEntityConfiguration : IEntityTypeConfiguration<CustomerAuditEventRecord>
    {
        public void Configure(EntityTypeBuilder<CustomerAuditEventRecord> entity)
        {
            entity.ToTable("customer_audit_events", "audit");

            entity.HasKey(record => new { record.InstitutionId, record.AuditEventId });
            entity.Property(record => record.InstitutionId).HasColumnName("institution_id").HasMaxLength(128);
            entity.Property(record => record.AuditEventId).HasColumnName("audit_event_id").HasMaxLength(64);
            entity.Property(record => record.OccurredAt).HasColumnName("occurred_at");
            entity.Property(record => record.ActorId).HasColumnName("actor_id").HasMaxLength(128);
            entity.Property(record => record.Action).HasColumnName("action").HasMaxLength(128);
            entity.Property(record => record.SubjectType).HasColumnName("subject_type").HasMaxLength(64);
            entity.Property(record => record.SubjectReference).HasColumnName("subject_reference").HasMaxLength(128);
            entity.Property(record => record.CorrelationId).HasColumnName("correlation_id").HasMaxLength(128);
            entity.Property(record => record.Detail).HasColumnName("detail").HasColumnType("jsonb");

            entity.HasIndex(record => new { record.InstitutionId, record.OccurredAt })
                .HasDatabaseName("ix_customer_audit_events_institution_occurred");
            entity.HasIndex(record => new { record.InstitutionId, record.SubjectReference })
                .HasDatabaseName("ix_customer_audit_events_institution_subject");
        }
    }
}
