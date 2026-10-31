using CoreBanking.IdentityAccess.Infrastructure.Persistence.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CoreBanking.IdentityAccess.Infrastructure.Persistence.Configurations;

/// <summary>
/// Mapping for the approved MFA slice. Enrolments hold an opaque provider reference only, and
/// step-up evidence is bounded by its expiry and single use.
/// Decision: ADR-0004, ADR-0008, ADR-0010 and P1-E02-D08.
/// </summary>
internal sealed class MfaEnrolmentRecordConfiguration : IEntityTypeConfiguration<MfaEnrolmentRecord>
{
    public void Configure(EntityTypeBuilder<MfaEnrolmentRecord> builder)
    {
        builder.ToTable("mfa_enrolments", "security");
        builder.HasKey(enrolment => new { enrolment.InstitutionId, enrolment.EnrolmentId });

        builder.Property(enrolment => enrolment.InstitutionId).HasColumnName("institution_id");
        builder.Property(enrolment => enrolment.EnrolmentId).HasColumnName("enrolment_id");
        builder.Property(enrolment => enrolment.IdentityId).HasColumnName("identity_id");
        builder.Property(enrolment => enrolment.FactorKind).HasColumnName("factor_kind").HasMaxLength(20);
        builder.Property(enrolment => enrolment.Status).HasColumnName("status").HasMaxLength(20);
        builder.Property(enrolment => enrolment.ProviderReference).HasColumnName("provider_reference").HasMaxLength(200);
        builder.Property(enrolment => enrolment.CreatedAt).HasColumnName("created_at");
        builder.Property(enrolment => enrolment.VerifiedAt).HasColumnName("verified_at");
        builder.Property(enrolment => enrolment.RevokedAt).HasColumnName("revoked_at");
        builder.Property(enrolment => enrolment.ConcurrencyVersion).HasColumnName("concurrency_version").IsConcurrencyToken();

        // One active enrolment per actor and factor family; new enrolment requires explicit revocation.
        builder.HasIndex(enrolment => new { enrolment.InstitutionId, enrolment.IdentityId, enrolment.FactorKind })
            .IsUnique()
            .HasFilter("status <> 'revoked'")
            .HasDatabaseName("ux_mfa_enrolments_active_factor");
    }
}

internal sealed class StepUpEvidenceRecordConfiguration : IEntityTypeConfiguration<StepUpEvidenceRecord>
{
    public void Configure(EntityTypeBuilder<StepUpEvidenceRecord> builder)
    {
        builder.ToTable("step_up_evidence", "security", table =>
            table.HasCheckConstraint("ck_step_up_expiry_after_issue", "expires_at > issued_at"));
        builder.HasKey(evidence => new { evidence.InstitutionId, evidence.EvidenceId });

        builder.Property(evidence => evidence.InstitutionId).HasColumnName("institution_id");
        builder.Property(evidence => evidence.EvidenceId).HasColumnName("evidence_id");
        builder.Property(evidence => evidence.IdentityId).HasColumnName("identity_id");
        builder.Property(evidence => evidence.SessionId).HasColumnName("session_id").HasMaxLength(100);
        builder.Property(evidence => evidence.DeviceId).HasColumnName("device_id").HasMaxLength(200);
        builder.Property(evidence => evidence.Purpose).HasColumnName("purpose").HasMaxLength(150);
        builder.Property(evidence => evidence.FactorKind).HasColumnName("factor_kind").HasMaxLength(20);
        builder.Property(evidence => evidence.CorrelationId).HasColumnName("correlation_id").HasMaxLength(100);
        builder.Property(evidence => evidence.IssuedAt).HasColumnName("issued_at");
        builder.Property(evidence => evidence.ExpiresAt).HasColumnName("expires_at");
        builder.Property(evidence => evidence.ConsumedAt).HasColumnName("consumed_at");

        builder.HasIndex(evidence => new { evidence.InstitutionId, evidence.IdentityId, evidence.SessionId, evidence.Purpose })
            .HasDatabaseName("ix_step_up_evidence_lookup");
    }
}
