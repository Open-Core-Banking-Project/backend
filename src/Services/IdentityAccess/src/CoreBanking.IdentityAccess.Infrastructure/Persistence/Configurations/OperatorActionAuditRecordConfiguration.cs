using CoreBanking.IdentityAccess.Infrastructure.Persistence.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CoreBanking.IdentityAccess.Infrastructure.Persistence.Configurations;

internal sealed class OperatorActionAuditRecordConfiguration : IEntityTypeConfiguration<OperatorActionAuditRecord>
{
    public void Configure(EntityTypeBuilder<OperatorActionAuditRecord> builder)
    {
        builder.ToTable("operator_actions", "audit");
        builder.HasKey(record => new { record.InstitutionId, record.AuditEventId });
        builder.Property(record => record.AuditEventId).HasColumnName("audit_event_id");
        builder.Property(record => record.InstitutionId).HasColumnName("institution_id");
        builder.Property(record => record.ActorId).HasColumnName("actor_id");
        builder.Property(record => record.Action).HasColumnName("action");
        builder.Property(record => record.ResourceType).HasColumnName("resource_type");
        builder.Property(record => record.ResourceId).HasColumnName("resource_id");
        builder.Property(record => record.Reason).HasColumnName("reason");
        builder.Property(record => record.CorrelationId).HasColumnName("correlation_id");
        builder.Property(record => record.OccurredAt).HasColumnName("occurred_at");
        builder.HasIndex(record => new { record.InstitutionId, record.OccurredAt });
    }
}
