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

internal sealed class SecurityLockRecordConfiguration : IEntityTypeConfiguration<SecurityLockRecord>
{
    public void Configure(EntityTypeBuilder<SecurityLockRecord> builder)
    {
        builder.ToTable("locks", "security", table =>
        {
            table.HasCheckConstraint(
                "ck_locks_expiry_after_creation",
                "expires_at IS NULL OR expires_at >= created_at");
            table.HasCheckConstraint(
                "ck_locks_release_after_creation",
                "released_at IS NULL OR released_at >= created_at");
        });
        builder.HasKey(lockRecord => new { lockRecord.InstitutionId, lockRecord.SecurityLockId });
        builder.Property(lockRecord => lockRecord.InstitutionId).HasColumnName("institution_id");
        builder.Property(lockRecord => lockRecord.SecurityLockId).HasColumnName("security_lock_id");
        builder.Property(lockRecord => lockRecord.IdentityId).HasColumnName("identity_id");
        builder.Property(lockRecord => lockRecord.Scope).HasColumnName("scope").HasMaxLength(100);
        builder.Property(lockRecord => lockRecord.ReasonCode).HasColumnName("reason_code").HasMaxLength(100);
        builder.Property(lockRecord => lockRecord.CreatedAt).HasColumnName("created_at");
        builder.Property(lockRecord => lockRecord.ExpiresAt).HasColumnName("expires_at");
        builder.Property(lockRecord => lockRecord.ReleasedAt).HasColumnName("released_at");

        // One unreleased lock per identity and scope. The predicate is the only way to express
        // "currently locked" as a constraint, because a partial index cannot evaluate expiry.
        // Lapsed locks are closed by the maintenance sweep and by lock acquisition itself.
        builder.HasIndex(lockRecord => new { lockRecord.InstitutionId, lockRecord.IdentityId, lockRecord.Scope })
            .IsUnique()
            .HasFilter("released_at IS NULL")
            .HasDatabaseName("ux_locks_unreleased_scope");
    }
}
