using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CoreBanking.CustomerOnboarding.Infrastructure.Persistence.Configurations;

/// <summary>
/// The <c>onboarding</c> schema. The application row carries the current protected capture and the
/// revisions table carries every capture that was ever confirmed; revisions are append-only.
/// Decision: CUS-UC-01, epic 03 database design.
/// </summary>
public static class OnboardingRecordConfiguration
{
    public static void ApplyOnboardingRecords(this ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.ApplyConfiguration(new OnboardingApplicationEntityConfiguration());
        modelBuilder.ApplyConfiguration(new OnboardingApplicationRevisionEntityConfiguration());
    }

    private sealed class OnboardingApplicationEntityConfiguration : IEntityTypeConfiguration<OnboardingApplicationRecord>
    {
        public void Configure(EntityTypeBuilder<OnboardingApplicationRecord> entity)
        {
            entity.ToTable("applications", "onboarding", table =>
            {
                table.HasCheckConstraint("ck_onboarding_applications_type", "application_type = 'individual'");
                table.HasCheckConstraint(
                    "ck_onboarding_applications_status",
                    "status IN ('draft', 'submitted', 'withdrawn', 'expired', 'rejected', 'approved', 'completed')");
                table.HasCheckConstraint("ck_onboarding_applications_revision", "revision >= 1");
                table.HasCheckConstraint("ck_onboarding_applications_concurrency_version", "concurrency_version > 0");
                table.HasCheckConstraint("ck_onboarding_applications_payload_hash", "char_length(current_payload_hash) = 64");
                table.HasCheckConstraint(
                    "ck_onboarding_applications_submitted_party",
                    "status <> 'submitted' OR party_id IS NOT NULL");
                table.HasCheckConstraint(
                    "ck_onboarding_applications_withdrawn_at",
                    "(status = 'withdrawn') = (withdrawn_at IS NOT NULL)");
            });

            entity.HasKey(record => new { record.InstitutionId, record.ApplicationId });
            entity.Property(record => record.InstitutionId).HasColumnName("institution_id").HasMaxLength(128);
            entity.Property(record => record.ApplicationId).HasColumnName("application_id").HasMaxLength(64);
            entity.Property(record => record.ApplicationReference).HasColumnName("application_reference").HasMaxLength(64);
            entity.Property(record => record.ApplicationType).HasColumnName("application_type").HasMaxLength(32);
            entity.Property(record => record.BranchId).HasColumnName("branch_id").HasMaxLength(64);
            entity.Property(record => record.Status).HasColumnName("status").HasMaxLength(32);
            entity.Property(record => record.Revision).HasColumnName("revision");
            entity.Property(record => record.CurrentCapture).HasColumnName("current_capture").HasColumnType("jsonb");
            entity.Property(record => record.CurrentPayloadHash).HasColumnName("current_payload_hash").HasMaxLength(64);
            entity.Property(record => record.PartyId).HasColumnName("party_id").HasMaxLength(64);
            entity.Property(record => record.CreatedAt).HasColumnName("created_at");
            entity.Property(record => record.UpdatedAt).HasColumnName("updated_at");
            entity.Property(record => record.SubmittedAt).HasColumnName("submitted_at");
            entity.Property(record => record.WithdrawnAt).HasColumnName("withdrawn_at");
            entity.Property(record => record.ConcurrencyVersion).HasColumnName("concurrency_version").IsConcurrencyToken();

            entity.HasIndex(record => new { record.InstitutionId, record.ApplicationReference })
                .HasDatabaseName("ux_onboarding_applications_reference")
                .IsUnique();
            entity.HasIndex(record => new { record.InstitutionId, record.Status, record.UpdatedAt })
                .HasDatabaseName("ix_onboarding_applications_institution_status_updated");
        }
    }

    private sealed class OnboardingApplicationRevisionEntityConfiguration : IEntityTypeConfiguration<OnboardingApplicationRevisionRecord>
    {
        public void Configure(EntityTypeBuilder<OnboardingApplicationRevisionRecord> entity)
        {
            entity.ToTable("application_revisions", "onboarding", table =>
            {
                table.HasCheckConstraint("ck_onboarding_revisions_revision", "revision >= 1");
                table.HasCheckConstraint("ck_onboarding_revisions_payload_hash", "char_length(payload_hash) = 64");
                table.HasCheckConstraint("ck_onboarding_revisions_reason", "reason IN ('captured', 'submitted', 'withdrawn')");
            });

            entity.HasKey(record => new { record.InstitutionId, record.ApplicationId, record.Revision });
            entity.Property(record => record.InstitutionId).HasColumnName("institution_id").HasMaxLength(128);
            entity.Property(record => record.ApplicationId).HasColumnName("application_id").HasMaxLength(64);
            entity.Property(record => record.Revision).HasColumnName("revision");
            entity.Property(record => record.Status).HasColumnName("status").HasMaxLength(32);
            entity.Property(record => record.PayloadHash).HasColumnName("payload_hash").HasMaxLength(64);
            entity.Property(record => record.Capture).HasColumnName("capture").HasColumnType("jsonb");
            entity.Property(record => record.Reason).HasColumnName("reason").HasMaxLength(32);
            entity.Property(record => record.RecordedBy).HasColumnName("recorded_by").HasMaxLength(128);
            entity.Property(record => record.RecordedAt).HasColumnName("recorded_at");

            entity.HasOne(record => record.Application)
                .WithMany(record => record.Revisions)
                .HasForeignKey(record => new { record.InstitutionId, record.ApplicationId })
                .OnDelete(DeleteBehavior.Restrict);
        }
    }
}
