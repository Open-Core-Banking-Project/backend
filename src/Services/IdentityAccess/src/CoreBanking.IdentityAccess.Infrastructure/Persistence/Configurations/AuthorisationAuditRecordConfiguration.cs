using CoreBanking.IdentityAccess.Infrastructure.Persistence.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CoreBanking.IdentityAccess.Infrastructure.Persistence.Configurations;

internal sealed class AuthorisationAuditRecordConfiguration : IEntityTypeConfiguration<AuthorisationAuditRecord>
{
    public void Configure(EntityTypeBuilder<AuthorisationAuditRecord> builder)
    {
        builder.ToTable("authorisation_events", "audit");
        builder.HasKey(record => record.AuditEventId);

        builder.Property(record => record.AuditEventId).HasColumnName("audit_event_id");
        builder.Property(record => record.OccurredAt).HasColumnName("occurred_at");
        builder.Property(record => record.ActorId).HasColumnName("actor_id");
        builder.Property(record => record.ActorType).HasColumnName("actor_type");
        builder.Property(record => record.InstitutionId).HasColumnName("institution_id");
        builder.Property(record => record.Permission).HasColumnName("permission");
        builder.Property(record => record.Action).HasColumnName("action");
        builder.Property(record => record.Decision).HasColumnName("decision");
        builder.Property(record => record.Reason).HasColumnName("reason");
        builder.Property(record => record.Source).HasColumnName("source");
        builder.Property(record => record.CorrelationId).HasColumnName("correlation_id");
        builder.Property(record => record.EntitlementVersion).HasColumnName("entitlement_version");
        builder.Property(record => record.SessionId).HasColumnName("session_id");
        builder.Property(record => record.DeviceId).HasColumnName("device_id");
        builder.Property(record => record.PolicyVersion).HasColumnName("policy_version");

        builder.HasIndex(record => new { record.InstitutionId, record.OccurredAt });
    }
}
