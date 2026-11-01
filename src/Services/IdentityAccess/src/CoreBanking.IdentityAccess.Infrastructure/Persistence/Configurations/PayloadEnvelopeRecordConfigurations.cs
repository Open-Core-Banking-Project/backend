using CoreBanking.IdentityAccess.Infrastructure.Persistence.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CoreBanking.IdentityAccess.Infrastructure.Persistence.Configurations;

/// <summary>
/// Institution key metadata in <c>security.key_metadata</c>: one row per key identifier and version, public
/// parameters only, and a state that never returns from revoked. Decision: P1-E02-D13.
/// </summary>
internal sealed class SecurityKeyMetadataRecordConfiguration : IEntityTypeConfiguration<SecurityKeyMetadataRecord>
{
    public void Configure(EntityTypeBuilder<SecurityKeyMetadataRecord> builder)
    {
        builder.ToTable("key_metadata", "security", table =>
        {
            table.HasCheckConstraint(
                "ck_key_metadata_state",
                "state IN ('Active', 'Retired', 'Revoked')");
            table.HasCheckConstraint(
                "ck_key_metadata_version",
                "key_version >= 1");
            table.HasCheckConstraint(
                "ck_key_metadata_validity",
                "not_after IS NULL OR not_after > not_before");
            table.HasCheckConstraint(
                "ck_key_metadata_revoked",
                "state <> 'Revoked' OR (revoked_at IS NOT NULL AND revocation_reason IS NOT NULL)");
        });

        builder.HasKey(key => new { key.InstitutionId, key.KeyId, key.KeyVersion });

        builder.Property(key => key.InstitutionId).HasColumnName("institution_id");
        builder.Property(key => key.KeyId).HasColumnName("key_id").HasMaxLength(64);
        builder.Property(key => key.KeyVersion).HasColumnName("key_version");
        builder.Property(key => key.Purpose).HasColumnName("purpose").HasMaxLength(64);
        builder.Property(key => key.Algorithm).HasColumnName("algorithm").HasMaxLength(64);
        builder.Property(key => key.PublicKey).HasColumnName("public_key").HasMaxLength(4096);
        builder.Property(key => key.ProviderReference).HasColumnName("provider_reference").HasMaxLength(256);
        builder.Property(key => key.State).HasColumnName("state").HasConversion<string>().HasMaxLength(16);
        builder.Property(key => key.CreatedAt).HasColumnName("created_at");
        builder.Property(key => key.NotBefore).HasColumnName("not_before");
        builder.Property(key => key.NotAfter).HasColumnName("not_after");
        builder.Property(key => key.RetiredAt).HasColumnName("retired_at");
        builder.Property(key => key.RevokedAt).HasColumnName("revoked_at");
        builder.Property(key => key.RevocationReason).HasColumnName("revocation_reason").HasMaxLength(500);

        builder.HasIndex(key => new { key.InstitutionId, key.Purpose, key.State })
            .HasDatabaseName("ix_key_metadata_purpose");
    }
}

/// <summary>
/// Claimed envelope replay identifiers in <c>security.envelope_replay_records</c>. The unique key is the control
/// that refuses a second use of one envelope. Decision: P1-E02-D12 item D12-D.
/// </summary>
internal sealed class EnvelopeReplayRecordConfiguration : IEntityTypeConfiguration<EnvelopeReplayRecord>
{
    public void Configure(EntityTypeBuilder<EnvelopeReplayRecord> builder)
    {
        builder.ToTable("envelope_replay_records", "security", table =>
            table.HasCheckConstraint("ck_envelope_replay_window", "expires_at > claimed_at"));

        builder.HasKey(replay => new { replay.InstitutionId, replay.Purpose, replay.ReplayId });

        builder.Property(replay => replay.InstitutionId).HasColumnName("institution_id");
        builder.Property(replay => replay.Purpose).HasColumnName("purpose").HasMaxLength(64);
        builder.Property(replay => replay.ReplayId).HasColumnName("replay_id").HasMaxLength(64);
        builder.Property(replay => replay.ClaimedAt).HasColumnName("claimed_at");
        builder.Property(replay => replay.ExpiresAt).HasColumnName("expires_at");

        builder.HasIndex(replay => new { replay.InstitutionId, replay.ExpiresAt })
            .HasDatabaseName("ix_envelope_replay_records_expiry");
    }
}
