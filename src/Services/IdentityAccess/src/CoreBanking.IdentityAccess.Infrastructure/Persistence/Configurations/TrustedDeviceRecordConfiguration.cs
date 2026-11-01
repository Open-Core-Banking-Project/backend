using CoreBanking.IdentityAccess.Infrastructure.Persistence.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CoreBanking.IdentityAccess.Infrastructure.Persistence.Configurations;

/// <summary>
/// Trusted-device registrations in the <c>security</c> schema: one trusted registration per identity and key
/// thumbprint, an effective-dated trust and a revoked state that is terminal. Decision: P1-E02-D11.
/// </summary>
internal sealed class TrustedDeviceRecordConfiguration : IEntityTypeConfiguration<TrustedDeviceRecord>
{
    public void Configure(EntityTypeBuilder<TrustedDeviceRecord> builder)
    {
        builder.ToTable("trusted_devices", "security", table =>
        {
            table.HasCheckConstraint(
                "ck_trusted_devices_state",
                "trust_state IN ('PendingStepUp', 'Trusted', 'Suspended', 'Revoked')");
            table.HasCheckConstraint(
                "ck_trusted_devices_trust_period",
                "trust_expires_at IS NULL OR trusted_at IS NULL OR trust_expires_at > trusted_at");
            table.HasCheckConstraint(
                "ck_trusted_devices_revoked",
                "trust_state <> 'Revoked' OR (revoked_at IS NOT NULL AND revocation_reason IS NOT NULL)");
        });

        builder.HasKey(device => new { device.InstitutionId, device.DeviceId });

        builder.Property(device => device.InstitutionId).HasColumnName("institution_id");
        builder.Property(device => device.DeviceId).HasColumnName("device_id");
        builder.Property(device => device.IdentityId).HasColumnName("identity_id");
        builder.Property(device => device.KeyThumbprint).HasColumnName("key_thumbprint").HasMaxLength(64);
        builder.Property(device => device.Platform).HasColumnName("platform").HasMaxLength(16);
        builder.Property(device => device.DisplayName).HasColumnName("display_name").HasMaxLength(64);
        builder.Property(device => device.TrustState).HasColumnName("trust_state").HasConversion<string>();
        builder.Property(device => device.CreatedAt).HasColumnName("created_at");
        builder.Property(device => device.TrustedAt).HasColumnName("trusted_at");
        builder.Property(device => device.TrustExpiresAt).HasColumnName("trust_expires_at");
        builder.Property(device => device.RevokedAt).HasColumnName("revoked_at");
        builder.Property(device => device.RevocationReason).HasColumnName("revocation_reason").HasMaxLength(500);
        builder.Property(device => device.StepUpEvidenceId).HasColumnName("step_up_evidence_id").HasMaxLength(64);
        builder.Property(device => device.ConcurrencyVersion).HasColumnName("concurrency_version").IsConcurrencyToken();

        builder.HasIndex(device => new { device.InstitutionId, device.IdentityId, device.CreatedAt })
            .HasDatabaseName("ix_trusted_devices_identity");

        builder.HasIndex(device => new { device.InstitutionId, device.IdentityId, device.KeyThumbprint })
            .HasDatabaseName("ux_trusted_devices_trusted_key")
            .HasFilter("trust_state = 'Trusted'")
            .IsUnique();
    }
}
