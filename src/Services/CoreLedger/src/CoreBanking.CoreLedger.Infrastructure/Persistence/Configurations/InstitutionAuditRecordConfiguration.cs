using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CoreBanking.CoreLedger.Infrastructure.Persistence.Configurations;

internal sealed class InstitutionAuditRecordConfiguration : IEntityTypeConfiguration<InstitutionAuditRecord>
{
    public void Configure(EntityTypeBuilder<InstitutionAuditRecord> builder)
    {
        builder.ToTable("institution_audit_events", "audit", table =>
        {
            table.HasCheckConstraint("ck_institution_audit_outcome", "outcome IN ('succeeded', 'rejected', 'failed')");
        });
        builder.HasKey(record => new { record.InstitutionId, record.AuditEventId });
        builder.HasIndex(record => new { record.InstitutionId, record.OccurredAt });
        builder.HasIndex(record => new { record.InstitutionId, record.CorrelationId });

        builder.Property(record => record.InstitutionId).HasColumnName("institution_id").HasMaxLength(128);
        builder.Property(record => record.AuditEventId).HasColumnName("audit_event_id").HasMaxLength(128);
        builder.Property(record => record.ActorId).HasColumnName("actor_id").HasMaxLength(128);
        builder.Property(record => record.ActorType).HasColumnName("actor_type").HasMaxLength(32);
        builder.Property(record => record.Permission).HasColumnName("permission").HasMaxLength(128);
        builder.Property(record => record.Action).HasColumnName("action").HasMaxLength(128);
        builder.Property(record => record.ResourceType).HasColumnName("resource_type").HasMaxLength(64);
        builder.Property(record => record.ResourceId).HasColumnName("resource_id").HasMaxLength(128);
        builder.Property(record => record.Outcome).HasColumnName("outcome").HasMaxLength(32);
        builder.Property(record => record.Reason).HasColumnName("reason").HasMaxLength(512);
        builder.Property(record => record.CorrelationId).HasColumnName("correlation_id").HasMaxLength(128);
        builder.Property(record => record.BeforeReference).HasColumnName("before_reference").HasMaxLength(256);
        builder.Property(record => record.AfterReference).HasColumnName("after_reference").HasMaxLength(256);
        builder.Property(record => record.OccurredAt).HasColumnName("occurred_at");
    }
}
