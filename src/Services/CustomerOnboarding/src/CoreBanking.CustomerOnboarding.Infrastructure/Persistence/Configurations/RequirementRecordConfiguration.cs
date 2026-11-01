using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CoreBanking.CustomerOnboarding.Infrastructure.Persistence.Configurations;

/// <summary>
/// Requirement packs, their immutable snapshots and document reference metadata. One effective pack per
/// institution and one snapshot per application revision are enforced by the database, not by caller
/// discipline. Decision: P1-E03-D01, invariant 3.
/// </summary>
public static class RequirementRecordConfiguration
{
    public static void ApplyRequirementRecords(this ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.ApplyConfiguration(new RequirementPackEntityConfiguration());
        modelBuilder.ApplyConfiguration(new RequirementSnapshotEntityConfiguration());
        modelBuilder.ApplyConfiguration(new DocumentReferenceEntityConfiguration());
    }

    private sealed class RequirementPackEntityConfiguration : IEntityTypeConfiguration<RequirementPackRecord>
    {
        public void Configure(EntityTypeBuilder<RequirementPackRecord> entity)
        {
            entity.ToTable("requirement_packs", "onboarding", table =>
            {
                table.HasCheckConstraint("ck_requirement_packs_status", "status IN ('effective', 'superseded')");
                table.HasCheckConstraint("ck_requirement_packs_version", "version >= 1");
                table.HasCheckConstraint("ck_requirement_packs_content_hash", "char_length(content_hash) = 64");
                table.HasCheckConstraint(
                    "ck_requirement_packs_superseded",
                    "(status = 'superseded') = (superseded_at IS NOT NULL)");
            });

            entity.HasKey(record => new { record.InstitutionId, record.PackCode, record.Version });
            entity.Property(record => record.InstitutionId).HasColumnName("institution_id").HasMaxLength(128);
            entity.Property(record => record.PackCode).HasColumnName("pack_code").HasMaxLength(64);
            entity.Property(record => record.Version).HasColumnName("version");
            entity.Property(record => record.ContentHash).HasColumnName("content_hash").HasMaxLength(64);
            entity.Property(record => record.Requirements).HasColumnName("requirements").HasColumnType("jsonb");
            entity.Property(record => record.Status).HasColumnName("status").HasMaxLength(32);
            entity.Property(record => record.PublishedBy).HasColumnName("published_by").HasMaxLength(128);
            entity.Property(record => record.PublishedAt).HasColumnName("published_at");
            entity.Property(record => record.SupersededAt).HasColumnName("superseded_at");

            entity.HasIndex(record => record.InstitutionId)
                .HasDatabaseName("ux_requirement_packs_effective")
                .HasFilter("status = 'effective'")
                .IsUnique();
        }
    }

    private sealed class RequirementSnapshotEntityConfiguration : IEntityTypeConfiguration<RequirementSnapshotRecord>
    {
        public void Configure(EntityTypeBuilder<RequirementSnapshotRecord> entity)
        {
            entity.ToTable("requirement_snapshots", "onboarding", table =>
            {
                table.HasCheckConstraint("ck_requirement_snapshots_revision", "application_revision >= 1");
                table.HasCheckConstraint("ck_requirement_snapshots_pack_version", "pack_version >= 1");
                table.HasCheckConstraint("ck_requirement_snapshots_content_hash", "char_length(content_hash) = 64");
            });

            entity.HasKey(record => new { record.InstitutionId, record.RequirementSnapshotId });
            entity.Property(record => record.InstitutionId).HasColumnName("institution_id").HasMaxLength(128);
            entity.Property(record => record.RequirementSnapshotId).HasColumnName("requirement_snapshot_id").HasMaxLength(64);
            entity.Property(record => record.ApplicationId).HasColumnName("application_id").HasMaxLength(64);
            entity.Property(record => record.ApplicationRevision).HasColumnName("application_revision");
            entity.Property(record => record.PackCode).HasColumnName("pack_code").HasMaxLength(64);
            entity.Property(record => record.PackVersion).HasColumnName("pack_version");
            entity.Property(record => record.ContentHash).HasColumnName("content_hash").HasMaxLength(64);
            entity.Property(record => record.Requirements).HasColumnName("requirements").HasColumnType("jsonb");
            entity.Property(record => record.CapturedBy).HasColumnName("captured_by").HasMaxLength(128);
            entity.Property(record => record.CapturedAt).HasColumnName("captured_at");

            entity.HasIndex(record => new { record.InstitutionId, record.ApplicationId, record.ApplicationRevision })
                .HasDatabaseName("ux_requirement_snapshots_application_revision")
                .IsUnique();

            entity.HasOne(record => record.Application)
                .WithMany()
                .HasForeignKey(record => new { record.InstitutionId, record.ApplicationId })
                .OnDelete(DeleteBehavior.Restrict);
        }
    }

    private sealed class DocumentReferenceEntityConfiguration : IEntityTypeConfiguration<DocumentReferenceRecord>
    {
        public void Configure(EntityTypeBuilder<DocumentReferenceRecord> entity)
        {
            entity.ToTable("document_references", "kyc", table =>
            {
                table.HasCheckConstraint("ck_document_references_scan_status", "scan_status IN ('pending_scan', 'clean', 'rejected')");
                table.HasCheckConstraint("ck_document_references_content_hash", "char_length(content_hash) = 64");
                table.HasCheckConstraint("ck_document_references_content_length", "content_length > 0");
            });

            entity.HasKey(record => new { record.InstitutionId, record.DocumentReferenceId });
            entity.Property(record => record.InstitutionId).HasColumnName("institution_id").HasMaxLength(128);
            entity.Property(record => record.DocumentReferenceId).HasColumnName("document_reference_id").HasMaxLength(64);
            entity.Property(record => record.ApplicationId).HasColumnName("application_id").HasMaxLength(64);
            entity.Property(record => record.RequirementCode).HasColumnName("requirement_code").HasMaxLength(64);
            entity.Property(record => record.ContentType).HasColumnName("content_type").HasMaxLength(128);
            entity.Property(record => record.ContentLength).HasColumnName("content_length");
            entity.Property(record => record.ContentHash).HasColumnName("content_hash").HasMaxLength(64);
            entity.Property(record => record.StorageReference).HasColumnName("storage_reference").HasMaxLength(512);
            entity.Property(record => record.ScanStatus).HasColumnName("scan_status").HasMaxLength(32);
            entity.Property(record => record.ProviderReference).HasColumnName("provider_reference").HasMaxLength(256);
            entity.Property(record => record.RegisteredBy).HasColumnName("registered_by").HasMaxLength(128);
            entity.Property(record => record.RegisteredAt).HasColumnName("registered_at");

            entity.HasIndex(record => new { record.InstitutionId, record.ApplicationId, record.ScanStatus })
                .HasDatabaseName("ix_document_references_institution_application_status");
        }
    }
}
