using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CoreBanking.CustomerOnboarding.Infrastructure.Persistence.Configurations;

/// <summary>
/// Preferences and consent evidence. The partial unique index enforces one current version per customer,
/// purpose and channel, and consent evidence is append-only. Decision: P1-E03-D12.
/// </summary>
public static class PreferenceRecordConfiguration
{
    public static void ApplyPreferenceRecords(this ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.ApplyConfiguration(new ServicePreferenceEntityConfiguration());
        modelBuilder.ApplyConfiguration(new ConsentEvidenceEntityConfiguration());
    }

    private sealed class ServicePreferenceEntityConfiguration : IEntityTypeConfiguration<ServicePreferenceRecord>
    {
        public void Configure(EntityTypeBuilder<ServicePreferenceRecord> entity)
        {
            entity.ToTable("service_preferences", "customer", table =>
            {
                table.HasCheckConstraint("ck_service_preferences_state", "state IN ('permitted', 'denied')");
                table.HasCheckConstraint("ck_service_preferences_source", "source IN ('customer', 'officer', 'channel')");
                table.HasCheckConstraint("ck_service_preferences_version", "version >= 1");
                table.HasCheckConstraint(
                    "ck_service_preferences_permitted",
                    "state <> 'permitted' OR lawful_basis IS NOT NULL");
                table.HasCheckConstraint(
                    "ck_service_preferences_period",
                    "effective_to IS NULL OR effective_to >= effective_from");
            });

            entity.HasKey(record => new { record.InstitutionId, record.CustomerId, record.Purpose, record.Channel, record.Version });
            entity.Property(record => record.InstitutionId).HasColumnName("institution_id").HasMaxLength(128);
            entity.Property(record => record.PreferenceId).HasColumnName("preference_id").HasMaxLength(64);
            entity.Property(record => record.CustomerId).HasColumnName("customer_id").HasMaxLength(64);
            entity.Property(record => record.Purpose).HasColumnName("purpose").HasMaxLength(64);
            entity.Property(record => record.Channel).HasColumnName("channel").HasMaxLength(64);
            entity.Property(record => record.Version).HasColumnName("version");
            entity.Property(record => record.State).HasColumnName("state").HasMaxLength(32);
            entity.Property(record => record.LawfulBasis).HasColumnName("lawful_basis").HasMaxLength(64);
            entity.Property(record => record.Source).HasColumnName("source").HasMaxLength(32);
            entity.Property(record => record.EvidenceReference).HasColumnName("evidence_reference").HasMaxLength(256);
            entity.Property(record => record.Reason).HasColumnName("reason").HasMaxLength(500);
            entity.Property(record => record.EffectiveFrom).HasColumnName("effective_from");
            entity.Property(record => record.EffectiveTo).HasColumnName("effective_to");
            entity.Property(record => record.RecordedBy).HasColumnName("recorded_by").HasMaxLength(128);

            entity.HasIndex(record => new { record.InstitutionId, record.CustomerId, record.Purpose, record.Channel })
                .HasDatabaseName("ux_service_preferences_current")
                .HasFilter("effective_to IS NULL")
                .IsUnique();
            entity.HasIndex(record => new { record.InstitutionId, record.PreferenceId })
                .HasDatabaseName("ux_service_preferences_preference_id")
                .IsUnique();
        }
    }

    private sealed class ConsentEvidenceEntityConfiguration : IEntityTypeConfiguration<ConsentEvidenceRecord>
    {
        public void Configure(EntityTypeBuilder<ConsentEvidenceRecord> entity)
        {
            entity.ToTable("consents", "privacy", table =>
            {
                table.HasCheckConstraint("ck_consents_state", "state IN ('permitted', 'denied')");
                table.HasCheckConstraint("ck_consents_version", "version >= 1");
                table.HasCheckConstraint("ck_consents_permitted", "state <> 'permitted' OR lawful_basis IS NOT NULL");
            });

            entity.HasKey(record => new { record.InstitutionId, record.ConsentId });
            entity.Property(record => record.InstitutionId).HasColumnName("institution_id").HasMaxLength(128);
            entity.Property(record => record.ConsentId).HasColumnName("consent_id").HasMaxLength(64);
            entity.Property(record => record.CustomerId).HasColumnName("customer_id").HasMaxLength(64);
            entity.Property(record => record.Purpose).HasColumnName("purpose").HasMaxLength(64);
            entity.Property(record => record.Channel).HasColumnName("channel").HasMaxLength(64);
            entity.Property(record => record.Version).HasColumnName("version");
            entity.Property(record => record.State).HasColumnName("state").HasMaxLength(32);
            entity.Property(record => record.LawfulBasis).HasColumnName("lawful_basis").HasMaxLength(64);
            entity.Property(record => record.Source).HasColumnName("source").HasMaxLength(32);
            entity.Property(record => record.EvidenceReference).HasColumnName("evidence_reference").HasMaxLength(256);
            entity.Property(record => record.RecordedBy).HasColumnName("recorded_by").HasMaxLength(128);
            entity.Property(record => record.RecordedAt).HasColumnName("recorded_at");
            entity.Property(record => record.WithdrawnAt).HasColumnName("withdrawn_at");

            entity.HasIndex(record => new { record.InstitutionId, record.CustomerId, record.Purpose, record.Version })
                .HasDatabaseName("ux_consents_customer_purpose_version")
                .IsUnique();
        }
    }
}
