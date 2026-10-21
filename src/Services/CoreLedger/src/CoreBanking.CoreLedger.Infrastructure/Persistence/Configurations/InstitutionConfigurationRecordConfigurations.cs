using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CoreBanking.CoreLedger.Infrastructure.Persistence.Configurations;

internal sealed class ConfigurationSetRecordConfiguration : IEntityTypeConfiguration<ConfigurationSetRecord>
{
    public void Configure(EntityTypeBuilder<ConfigurationSetRecord> builder)
    {
        builder.ToTable("configuration_sets", "institution");
        builder.HasKey(x => new { x.InstitutionId, x.ConfigurationSetId });
        builder.HasIndex(x => new { x.InstitutionId, x.Area, x.Scope }).IsUnique();
        builder.Property(x => x.InstitutionId).HasColumnName("institution_id").HasMaxLength(128);
        builder.Property(x => x.ConfigurationSetId).HasColumnName("configuration_set_id").HasMaxLength(128);
        builder.Property(x => x.Area).HasColumnName("area").HasMaxLength(128);
        builder.Property(x => x.Scope).HasColumnName("scope").HasMaxLength(256);
        builder.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at");
    }
}

internal sealed class ConfigurationVersionRecordConfiguration : IEntityTypeConfiguration<ConfigurationVersionRecord>
{
    public void Configure(EntityTypeBuilder<ConfigurationVersionRecord> builder)
    {
        builder.ToTable("configuration_versions", "institution", table =>
        {
            table.HasCheckConstraint("ck_configuration_versions_number", "version_number > 0");
            table.HasCheckConstraint(
                "ck_configuration_versions_status",
                "status IN ('draft', 'submitted', 'approved', 'published', 'superseded')");
            table.HasCheckConstraint(
                "ck_configuration_versions_effective_period",
                "(effective_to IS NULL OR (effective_from IS NOT NULL AND effective_to > effective_from)) " +
                "AND (status NOT IN ('published', 'superseded') OR effective_from IS NOT NULL)");
        });
        builder.HasKey(x => new { x.InstitutionId, x.VersionId });
        builder.HasIndex(x => new { x.InstitutionId, x.ConfigurationSetId, x.VersionNumber }).IsUnique();
        builder.HasIndex(x => new { x.InstitutionId, x.ConfigurationSetId, x.Status });
        builder.HasOne<ConfigurationSetRecord>().WithMany()
            .HasForeignKey(x => new { x.InstitutionId, x.ConfigurationSetId })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ConfigurationVersionRecord>().WithMany()
            .HasForeignKey(x => new { x.InstitutionId, x.RollbackOfVersionId })
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired(false);
        builder.Property(x => x.InstitutionId).HasColumnName("institution_id").HasMaxLength(128);
        builder.Property(x => x.VersionId).HasColumnName("version_id").HasMaxLength(128);
        builder.Property(x => x.ConfigurationSetId).HasColumnName("configuration_set_id").HasMaxLength(128);
        builder.Property(x => x.VersionNumber).HasColumnName("version_number");
        builder.Property(x => x.Schema).HasColumnName("schema").HasMaxLength(128);
        builder.Property(x => x.ContentJson).HasColumnName("content_json").HasColumnType("jsonb");
        builder.Property(x => x.ContentHash).HasColumnName("content_hash").HasMaxLength(64);
        builder.Property(x => x.Status).HasColumnName("status").HasMaxLength(32);
        builder.Property(x => x.CreatedByActorId).HasColumnName("created_by_actor_id").HasMaxLength(128);
        builder.Property(x => x.RollbackOfVersionId).HasColumnName("rollback_of_version_id").HasMaxLength(128);
        builder.Property(x => x.ApprovedByActorId).HasColumnName("approved_by_actor_id").HasMaxLength(128);
        builder.Property(x => x.ApprovalReference).HasColumnName("approval_reference").HasMaxLength(128);
        builder.Property(x => x.EffectiveFrom).HasColumnName("effective_from");
        builder.Property(x => x.EffectiveTo).HasColumnName("effective_to");
        builder.Property(x => x.PublishedAt).HasColumnName("published_at");
        builder.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at");
    }
}

internal sealed class ConfigurationSimulationRecordConfiguration : IEntityTypeConfiguration<ConfigurationSimulationRecord>
{
    public void Configure(EntityTypeBuilder<ConfigurationSimulationRecord> builder)
    {
        builder.ToTable("configuration_simulations", "institution", table =>
            table.HasCheckConstraint("ck_configuration_simulations_outcome", "outcome IN ('passed', 'failed')"));
        builder.HasKey(x => new { x.InstitutionId, x.SimulationId });
        builder.HasIndex(x => new { x.InstitutionId, x.VersionId, x.InputHash }).IsUnique();
        builder.HasOne<ConfigurationVersionRecord>().WithMany()
            .HasForeignKey(x => new { x.InstitutionId, x.VersionId })
            .OnDelete(DeleteBehavior.Restrict);
        builder.Property(x => x.InstitutionId).HasColumnName("institution_id").HasMaxLength(128);
        builder.Property(x => x.SimulationId).HasColumnName("simulation_id").HasMaxLength(128);
        builder.Property(x => x.VersionId).HasColumnName("version_id").HasMaxLength(128);
        builder.Property(x => x.DatasetVersion).HasColumnName("dataset_version").HasMaxLength(128);
        builder.Property(x => x.Engine).HasColumnName("engine").HasMaxLength(128);
        builder.Property(x => x.ContentHash).HasColumnName("content_hash").HasMaxLength(64);
        builder.Property(x => x.InputHash).HasColumnName("input_hash").HasMaxLength(64);
        builder.Property(x => x.OutputHash).HasColumnName("output_hash").HasMaxLength(64);
        builder.Property(x => x.Outcome).HasColumnName("outcome").HasMaxLength(32);
        builder.Property(x => x.SimulatedAt).HasColumnName("simulated_at");
    }
}

internal sealed class ConfigurationPublicationRecordConfiguration : IEntityTypeConfiguration<ConfigurationPublicationRecord>
{
    public void Configure(EntityTypeBuilder<ConfigurationPublicationRecord> builder)
    {
        builder.ToTable("configuration_publications", "institution", table =>
            table.HasCheckConstraint(
                "ck_configuration_publications_effective_period",
                "effective_to IS NULL OR effective_to > effective_from"));
        builder.HasKey(x => new { x.InstitutionId, x.PublicationId });
        builder.HasIndex(x => new { x.InstitutionId, x.ConfigurationSetId, x.EffectiveFrom }).IsUnique();
        builder.HasIndex(x => new { x.InstitutionId, x.VersionId }).IsUnique();
        builder.HasOne<ConfigurationSetRecord>().WithMany()
            .HasForeignKey(x => new { x.InstitutionId, x.ConfigurationSetId })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ConfigurationVersionRecord>().WithMany()
            .HasForeignKey(x => new { x.InstitutionId, x.VersionId })
            .OnDelete(DeleteBehavior.Restrict);
        builder.Property(x => x.InstitutionId).HasColumnName("institution_id").HasMaxLength(128);
        builder.Property(x => x.PublicationId).HasColumnName("publication_id").HasMaxLength(128);
        builder.Property(x => x.ConfigurationSetId).HasColumnName("configuration_set_id").HasMaxLength(128);
        builder.Property(x => x.VersionId).HasColumnName("version_id").HasMaxLength(128);
        builder.Property(x => x.ContentHash).HasColumnName("content_hash").HasMaxLength(64);
        builder.Property(x => x.PublishedByActorId).HasColumnName("published_by_actor_id").HasMaxLength(128);
        builder.Property(x => x.ApprovalReference).HasColumnName("approval_reference").HasMaxLength(128);
        builder.Property(x => x.EffectiveFrom).HasColumnName("effective_from");
        builder.Property(x => x.EffectiveTo).HasColumnName("effective_to");
        builder.Property(x => x.PublishedAt).HasColumnName("published_at");
    }
}
