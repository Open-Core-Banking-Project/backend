using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CoreBanking.CustomerOnboarding.Infrastructure.Persistence.Configurations;

/// <summary>
/// The <c>customer</c> schema tables used by duplicate detection. The partial unique index enforces at
/// most one live candidate set per application revision while superseded evidence is retained.
/// Decision: P1-E03-D04-D.
/// </summary>
public static class DuplicateRecordConfiguration
{
    public static void ApplyDuplicateRecords(this ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.ApplyConfiguration(new DuplicateCandidateSetEntityConfiguration());
        modelBuilder.ApplyConfiguration(new DuplicateCandidateEntityConfiguration());
    }

    private sealed class DuplicateCandidateSetEntityConfiguration : IEntityTypeConfiguration<DuplicateCandidateSetRecord>
    {
        public void Configure(EntityTypeBuilder<DuplicateCandidateSetRecord> entity)
        {
            entity.ToTable("duplicate_candidate_sets", "customer", table =>
            {
                table.HasCheckConstraint("ck_duplicate_candidate_sets_status", "status IN ('open', 'decided', 'superseded')");
                table.HasCheckConstraint(
                    "ck_duplicate_candidate_sets_decision",
                    "decision IS NULL OR decision IN ('same_person', 'distinct', 'insufficient_evidence')");
                table.HasCheckConstraint("ck_duplicate_candidate_sets_version", "version > 0");
                table.HasCheckConstraint("ck_duplicate_candidate_sets_revision", "application_revision >= 1");
                table.HasCheckConstraint(
                    "ck_duplicate_candidate_sets_decided",
                    "(status <> 'decided' AND decision IS NULL) OR (status = 'decided' AND decision IS NOT NULL AND decided_at IS NOT NULL AND decided_by IS NOT NULL AND decision_reason IS NOT NULL)");
                table.HasCheckConstraint("ck_duplicate_candidate_sets_rule_version", "char_length(rule_version) > 0");
            });

            entity.HasKey(record => new { record.InstitutionId, record.SetId });
            entity.Property(record => record.InstitutionId).HasColumnName("institution_id").HasMaxLength(128);
            entity.Property(record => record.SetId).HasColumnName("set_id").HasMaxLength(64);
            entity.Property(record => record.ApplicationId).HasColumnName("application_id").HasMaxLength(64);
            entity.Property(record => record.ApplicationRevision).HasColumnName("application_revision");
            entity.Property(record => record.RuleVersion).HasColumnName("rule_version").HasMaxLength(64);
            entity.Property(record => record.Status).HasColumnName("status").HasMaxLength(32);
            entity.Property(record => record.Version).HasColumnName("version").IsConcurrencyToken();
            entity.Property(record => record.CreatedBy).HasColumnName("created_by").HasMaxLength(128);
            entity.Property(record => record.CreatedAt).HasColumnName("created_at");
            entity.Property(record => record.UpdatedAt).HasColumnName("updated_at");
            entity.Property(record => record.DecidedAt).HasColumnName("decided_at");
            entity.Property(record => record.DecidedBy).HasColumnName("decided_by").HasMaxLength(128);
            entity.Property(record => record.Decision).HasColumnName("decision").HasMaxLength(32);
            entity.Property(record => record.DecisionReason).HasColumnName("decision_reason").HasMaxLength(500);

            entity.HasIndex(record => new { record.InstitutionId, record.ApplicationId, record.ApplicationRevision })
                .HasDatabaseName("ux_duplicate_candidate_sets_live")
                .HasFilter("status <> 'superseded'")
                .IsUnique();
            entity.HasIndex(record => new { record.InstitutionId, record.Status, record.CreatedAt })
                .HasDatabaseName("ix_duplicate_candidate_sets_institution_status_created");

            entity.HasOne<OnboardingApplicationRecord>()
                .WithMany()
                .HasForeignKey(record => new { record.InstitutionId, record.ApplicationId })
                .OnDelete(DeleteBehavior.Restrict);
        }
    }

    private sealed class DuplicateCandidateEntityConfiguration : IEntityTypeConfiguration<DuplicateCandidateRecord>
    {
        public void Configure(EntityTypeBuilder<DuplicateCandidateRecord> entity)
        {
            entity.ToTable("duplicate_candidates", "customer", table =>
            {
                table.HasCheckConstraint("ck_duplicate_candidates_band", "band IN ('possible', 'probable', 'confirmed')");
                table.HasCheckConstraint("ck_duplicate_candidates_factors", "jsonb_array_length(factors) > 0");
            });

            entity.HasKey(record => new { record.InstitutionId, record.SetId, record.CandidatePartyId });
            entity.Property(record => record.InstitutionId).HasColumnName("institution_id").HasMaxLength(128);
            entity.Property(record => record.SetId).HasColumnName("set_id").HasMaxLength(64);
            entity.Property(record => record.CandidatePartyId).HasColumnName("candidate_party_id").HasMaxLength(64);
            entity.Property(record => record.Band).HasColumnName("band").HasMaxLength(32);
            entity.Property(record => record.Factors).HasColumnName("factors").HasColumnType("jsonb");
            entity.Property(record => record.DetectedAt).HasColumnName("detected_at");

            entity.HasIndex(record => new { record.InstitutionId, record.CandidatePartyId })
                .HasDatabaseName("ix_duplicate_candidates_institution_party");

            entity.HasOne(record => record.Set)
                .WithMany(set => set.Candidates)
                .HasForeignKey(record => new { record.InstitutionId, record.SetId })
                .OnDelete(DeleteBehavior.Restrict);
        }
    }
}
