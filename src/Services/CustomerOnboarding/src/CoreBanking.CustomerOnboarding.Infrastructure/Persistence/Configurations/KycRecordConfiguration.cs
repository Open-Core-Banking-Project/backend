using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CoreBanking.CustomerOnboarding.Infrastructure.Persistence.Configurations;

/// <summary>
/// The <c>kyc</c> case, requirement and verification tables. Attempts and status history are append-only,
/// and a partial unique index allows only one active verification per application requirement.
/// Decision: P1-E03-D06.
/// </summary>
public static class KycRecordConfiguration
{
    public static void ApplyKycRecords(this ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.ApplyConfiguration(new KycCaseEntityConfiguration());
        modelBuilder.ApplyConfiguration(new KycRequirementEntityConfiguration());
        modelBuilder.ApplyConfiguration(new IdentityVerificationEntityConfiguration());
        modelBuilder.ApplyConfiguration(new VerificationAttemptEntityConfiguration());
        modelBuilder.ApplyConfiguration(new VerificationStatusHistoryEntityConfiguration());
        modelBuilder.ApplyConfiguration(new KycRequirementHistoryEntityConfiguration());
    }

    private sealed class KycCaseEntityConfiguration : IEntityTypeConfiguration<KycCaseRecord>
    {
        public void Configure(EntityTypeBuilder<KycCaseRecord> entity)
        {
            entity.ToTable("kyc_cases", "kyc", table =>
            {
                table.HasCheckConstraint("ck_kyc_cases_status", "status IN ('open', 'completed')");
                table.HasCheckConstraint("ck_kyc_cases_completed", "(status = 'completed') = (completed_at IS NOT NULL)");
            });

            entity.HasKey(record => new { record.InstitutionId, record.CaseId });
            entity.Property(record => record.InstitutionId).HasColumnName("institution_id").HasMaxLength(128);
            entity.Property(record => record.CaseId).HasColumnName("case_id").HasMaxLength(64);
            entity.Property(record => record.ApplicationId).HasColumnName("application_id").HasMaxLength(64);
            entity.Property(record => record.PartyId).HasColumnName("party_id").HasMaxLength(64);
            entity.Property(record => record.RequirementSnapshotId).HasColumnName("requirement_snapshot_id").HasMaxLength(64);
            entity.Property(record => record.Status).HasColumnName("status").HasMaxLength(32);
            entity.Property(record => record.CreatedAt).HasColumnName("created_at");
            entity.Property(record => record.UpdatedAt).HasColumnName("updated_at");
            entity.Property(record => record.CompletedAt).HasColumnName("completed_at");

            entity.HasIndex(record => new { record.InstitutionId, record.ApplicationId })
                .HasDatabaseName("ux_kyc_cases_application")
                .IsUnique();

            entity.HasOne<OnboardingApplicationRecord>()
                .WithMany()
                .HasForeignKey(record => new { record.InstitutionId, record.ApplicationId })
                .OnDelete(DeleteBehavior.Restrict);
        }
    }

    private sealed class KycRequirementEntityConfiguration : IEntityTypeConfiguration<KycRequirementRecord>
    {
        public void Configure(EntityTypeBuilder<KycRequirementRecord> entity)
        {
            entity.ToTable("kyc_requirements", "kyc", table =>
            {
                table.HasCheckConstraint(
                    "ck_kyc_requirements_status",
                    "status IN ('required', 'evidence_pending', 'ready', 'in_progress', 'passed', 'failed', 'inconclusive', 'manual_review', 'waived')");
                table.HasCheckConstraint(
                    "ck_kyc_requirements_waiver",
                    "(status = 'waived') = (waived_at IS NOT NULL AND waiver_authority IS NOT NULL AND waiver_reason IS NOT NULL)");
            });

            entity.HasKey(record => new { record.InstitutionId, record.CaseId, record.RequirementCode });
            entity.Property(record => record.InstitutionId).HasColumnName("institution_id").HasMaxLength(128);
            entity.Property(record => record.CaseId).HasColumnName("case_id").HasMaxLength(64);
            entity.Property(record => record.RequirementCode).HasColumnName("requirement_code").HasMaxLength(64);
            entity.Property(record => record.Status).HasColumnName("status").HasMaxLength(32);
            entity.Property(record => record.LinkedVerificationId).HasColumnName("linked_verification_id").HasMaxLength(64);
            entity.Property(record => record.WaiverAuthority).HasColumnName("waiver_authority").HasMaxLength(128);
            entity.Property(record => record.WaiverReason).HasColumnName("waiver_reason").HasMaxLength(500);
            entity.Property(record => record.WaivedAt).HasColumnName("waived_at");
            entity.Property(record => record.UpdatedAt).HasColumnName("updated_at");

            entity.HasOne(record => record.Case)
                .WithMany(kase => kase.Requirements)
                .HasForeignKey(record => new { record.InstitutionId, record.CaseId })
                .OnDelete(DeleteBehavior.Restrict);
        }
    }

    private sealed class IdentityVerificationEntityConfiguration : IEntityTypeConfiguration<IdentityVerificationRecord>
    {
        public void Configure(EntityTypeBuilder<IdentityVerificationRecord> entity)
        {
            entity.ToTable("identity_verifications", "kyc", table =>
            {
                table.HasCheckConstraint(
                    "ck_identity_verifications_status",
                    "status IN ('created', 'submitted', 'pending', 'succeeded', 'failed', 'uncertain', 'manual_review', 'cancelled')");
                table.HasCheckConstraint(
                    "ck_identity_verifications_outcome",
                    "outcome IS NULL OR outcome IN ('passed', 'failed', 'inconclusive', 'manual_review')");
                table.HasCheckConstraint(
                    "ck_identity_verifications_succeeded",
                    "status <> 'succeeded' OR (outcome IS NOT NULL AND provider_mapping_version IS NOT NULL)");
                table.HasCheckConstraint(
                    "ck_identity_verifications_evidence_hash",
                    "evidence_hash IS NULL OR length(evidence_hash) = 64");
            });

            entity.HasKey(record => new { record.InstitutionId, record.VerificationId });
            entity.Property(record => record.InstitutionId).HasColumnName("institution_id").HasMaxLength(128);
            entity.Property(record => record.VerificationId).HasColumnName("verification_id").HasMaxLength(64);
            entity.Property(record => record.CaseId).HasColumnName("case_id").HasMaxLength(64);
            entity.Property(record => record.ApplicationId).HasColumnName("application_id").HasMaxLength(64);
            entity.Property(record => record.PartyId).HasColumnName("party_id").HasMaxLength(64);
            entity.Property(record => record.RequirementCode).HasColumnName("requirement_code").HasMaxLength(64);
            entity.Property(record => record.CheckCode).HasColumnName("check_code").HasMaxLength(64);
            entity.Property(record => record.RequirementSnapshotId).HasColumnName("requirement_snapshot_id").HasMaxLength(64);
            entity.Property(record => record.Status).HasColumnName("status").HasMaxLength(32);
            entity.Property(record => record.Outcome).HasColumnName("outcome").HasMaxLength(32);
            entity.Property(record => record.ProviderMappingVersion).HasColumnName("provider_mapping_version").HasMaxLength(64);
            entity.Property(record => record.EvidenceReference).HasColumnName("evidence_reference").HasMaxLength(256);
            entity.Property(record => record.EvidenceHash).HasColumnName("evidence_hash").HasMaxLength(64);
            entity.Property(record => record.CreatedAt).HasColumnName("created_at");
            entity.Property(record => record.UpdatedAt).HasColumnName("updated_at");

            entity.HasIndex(record => new { record.InstitutionId, record.ApplicationId, record.RequirementCode })
                .HasDatabaseName("ux_identity_verifications_active_requirement")
                .HasFilter("status NOT IN ('succeeded', 'failed', 'cancelled')")
                .IsUnique();
        }
    }

    private sealed class VerificationAttemptEntityConfiguration : IEntityTypeConfiguration<VerificationAttemptRecord>
    {
        public void Configure(EntityTypeBuilder<VerificationAttemptRecord> entity)
        {
            entity.ToTable("verification_attempts", "kyc", table =>
                table.HasCheckConstraint(
                    "ck_verification_attempts_status",
                    "status IN ('submitted', 'pending', 'succeeded', 'failed', 'uncertain', 'cancelled_before_submission')"));

            entity.HasKey(record => new { record.InstitutionId, record.VerificationId, record.AttemptId });
            entity.Property(record => record.InstitutionId).HasColumnName("institution_id").HasMaxLength(128);
            entity.Property(record => record.VerificationId).HasColumnName("verification_id").HasMaxLength(64);
            entity.Property(record => record.AttemptId).HasColumnName("attempt_id").HasMaxLength(64);
            entity.Property(record => record.ProviderReference).HasColumnName("provider_reference").HasMaxLength(256);
            entity.Property(record => record.ProviderRouteId).HasColumnName("provider_route_id").HasMaxLength(64);
            entity.Property(record => record.Status).HasColumnName("status").HasMaxLength(32);
            entity.Property(record => record.SubmittedAt).HasColumnName("submitted_at");
            entity.Property(record => record.ResolvedAt).HasColumnName("resolved_at");

            entity.HasOne(record => record.Verification)
                .WithMany(verification => verification.Attempts)
                .HasForeignKey(record => new { record.InstitutionId, record.VerificationId })
                .OnDelete(DeleteBehavior.Restrict);
        }
    }

    private sealed class VerificationStatusHistoryEntityConfiguration : IEntityTypeConfiguration<VerificationStatusHistoryRecord>
    {
        public void Configure(EntityTypeBuilder<VerificationStatusHistoryRecord> entity)
        {
            entity.ToTable("verification_status_history", "kyc", table =>
            {
                table.HasCheckConstraint("ck_verification_status_history_sequence", "sequence >= 1");
                table.HasCheckConstraint(
                    "ck_verification_status_history_outcome",
                    "outcome IS NULL OR outcome IN ('passed', 'failed', 'inconclusive', 'manual_review')");
            });

            entity.HasKey(record => new { record.InstitutionId, record.VerificationId, record.Sequence });
            entity.Property(record => record.InstitutionId).HasColumnName("institution_id").HasMaxLength(128);
            entity.Property(record => record.VerificationId).HasColumnName("verification_id").HasMaxLength(64);
            entity.Property(record => record.Sequence).HasColumnName("sequence");
            entity.Property(record => record.Status).HasColumnName("status").HasMaxLength(32);
            entity.Property(record => record.Outcome).HasColumnName("outcome").HasMaxLength(32);
            entity.Property(record => record.RecordedBy).HasColumnName("recorded_by").HasMaxLength(128);
            entity.Property(record => record.RecordedAt).HasColumnName("recorded_at");
        }
    }

    private sealed class KycRequirementHistoryEntityConfiguration : IEntityTypeConfiguration<KycRequirementHistoryRecord>
    {
        public void Configure(EntityTypeBuilder<KycRequirementHistoryRecord> entity)
        {
            entity.ToTable("kyc_requirement_history", "kyc", table =>
                table.HasCheckConstraint("ck_kyc_requirement_history_sequence", "sequence >= 1"));

            entity.HasKey(record => new { record.InstitutionId, record.CaseId, record.RequirementCode, record.Sequence });
            entity.Property(record => record.InstitutionId).HasColumnName("institution_id").HasMaxLength(128);
            entity.Property(record => record.CaseId).HasColumnName("case_id").HasMaxLength(64);
            entity.Property(record => record.RequirementCode).HasColumnName("requirement_code").HasMaxLength(64);
            entity.Property(record => record.Sequence).HasColumnName("sequence");
            entity.Property(record => record.Status).HasColumnName("status").HasMaxLength(32);
            entity.Property(record => record.Reason).HasColumnName("reason").HasMaxLength(500);
            entity.Property(record => record.RecordedBy).HasColumnName("recorded_by").HasMaxLength(128);
            entity.Property(record => record.RecordedAt).HasColumnName("recorded_at");
        }
    }
}
