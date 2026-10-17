using CoreBanking.IdentityAccess.Infrastructure.Persistence.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CoreBanking.IdentityAccess.Infrastructure.Persistence.Configurations;

/// <summary>
/// Mapping for the <c>security</c> schema. Authentication evidence carries the institution but
/// deliberately no foreign key to the identity, because a failed attempt may name an identity
/// that does not exist; the audit trail must still be written.
/// Decision: ADR-0004, ADR-0010.
/// </summary>
internal sealed class AuthenticationEventRecordConfiguration : IEntityTypeConfiguration<AuthenticationEventRecord>
{
    public void Configure(EntityTypeBuilder<AuthenticationEventRecord> builder)
    {
        builder.ToTable("authentication_events", "security");
        builder.HasKey(evidence => new { evidence.InstitutionId, evidence.AuthenticationEventId });

        builder.Property(evidence => evidence.InstitutionId).HasColumnName("institution_id");
        builder.Property(evidence => evidence.AuthenticationEventId).HasColumnName("authentication_event_id");
        builder.Property(evidence => evidence.IdentityId).HasColumnName("identity_id");
        builder.Property(evidence => evidence.EventType).HasColumnName("event_type");
        builder.Property(evidence => evidence.Outcome).HasColumnName("outcome").HasConversion<string>();
        builder.Property(evidence => evidence.Detail).HasColumnName("detail");
        builder.Property(evidence => evidence.OccurredAt).HasColumnName("occurred_at");

        builder.HasIndex(evidence => new { evidence.InstitutionId, evidence.IdentityId, evidence.OccurredAt });
    }
}
