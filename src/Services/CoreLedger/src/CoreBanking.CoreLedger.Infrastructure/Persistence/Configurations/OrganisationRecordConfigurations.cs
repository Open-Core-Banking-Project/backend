using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CoreBanking.CoreLedger.Infrastructure.Persistence.Configurations;

internal sealed class RegionRecordConfiguration : IEntityTypeConfiguration<RegionRecord>
{
    public void Configure(EntityTypeBuilder<RegionRecord> builder)
    {
        builder.ToTable("regions", "institution", table => table.HasCheckConstraint("ck_regions_status", "status IN ('draft', 'active', 'suspended', 'closed')"));
        builder.HasKey(x => new { x.InstitutionId, x.RegionId });
        builder.HasIndex(x => new { x.InstitutionId, x.Code }).IsUnique();
        builder.Property(x => x.InstitutionId).HasColumnName("institution_id").HasMaxLength(128);
        builder.Property(x => x.RegionId).HasColumnName("region_id").HasMaxLength(128);
        builder.Property(x => x.Code).HasColumnName("code").HasMaxLength(64);
        builder.Property(x => x.Name).HasColumnName("name").HasMaxLength(256);
        builder.Property(x => x.Status).HasColumnName("status").HasMaxLength(32);
        builder.Property(x => x.CreatedByActorId).HasColumnName("created_by_actor_id").HasMaxLength(128);
        builder.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at");
    }
}

internal sealed class BranchRecordConfiguration : IEntityTypeConfiguration<BranchRecord>
{
    public void Configure(EntityTypeBuilder<BranchRecord> builder)
    {
        builder.ToTable("branches", "institution", table => table.HasCheckConstraint("ck_branches_status", "status IN ('draft', 'active', 'suspended', 'closed')"));
        builder.HasKey(x => new { x.InstitutionId, x.BranchId });
        builder.HasIndex(x => new { x.InstitutionId, x.Code }).IsUnique();
        builder.HasOne<RegionRecord>().WithMany().HasForeignKey(x => new { x.InstitutionId, x.RegionId }).OnDelete(DeleteBehavior.Restrict);
        builder.Property(x => x.InstitutionId).HasColumnName("institution_id").HasMaxLength(128);
        builder.Property(x => x.BranchId).HasColumnName("branch_id").HasMaxLength(128);
        builder.Property(x => x.RegionId).HasColumnName("region_id").HasMaxLength(128);
        builder.Property(x => x.Code).HasColumnName("code").HasMaxLength(64);
        builder.Property(x => x.Name).HasColumnName("name").HasMaxLength(256);
        builder.Property(x => x.Status).HasColumnName("status").HasMaxLength(32);
        builder.Property(x => x.CreatedByActorId).HasColumnName("created_by_actor_id").HasMaxLength(128);
        builder.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at");
    }
}

internal sealed class OrganisationStatusHistoryRecordConfiguration : IEntityTypeConfiguration<OrganisationStatusHistoryRecord>
{
    public void Configure(EntityTypeBuilder<OrganisationStatusHistoryRecord> builder)
    {
        builder.ToTable("organisation_status_history", "institution", table => table.HasCheckConstraint("ck_organisation_history_resource", "resource_type IN ('region', 'branch')"));
        builder.HasKey(x => new { x.InstitutionId, x.HistoryId });
        builder.HasIndex(x => new { x.InstitutionId, x.ResourceType, x.ResourceId, x.ChangedAt });
        builder.Property(x => x.InstitutionId).HasColumnName("institution_id").HasMaxLength(128);
        builder.Property(x => x.HistoryId).HasColumnName("history_id").HasMaxLength(128);
        builder.Property(x => x.ResourceType).HasColumnName("resource_type").HasMaxLength(16);
        builder.Property(x => x.ResourceId).HasColumnName("resource_id").HasMaxLength(128);
        builder.Property(x => x.PreviousStatus).HasColumnName("previous_status").HasMaxLength(32);
        builder.Property(x => x.NewStatus).HasColumnName("new_status").HasMaxLength(32);
        builder.Property(x => x.ChangedByActorId).HasColumnName("changed_by_actor_id").HasMaxLength(128);
        builder.Property(x => x.Reason).HasColumnName("reason").HasMaxLength(512);
        builder.Property(x => x.CorrelationId).HasColumnName("correlation_id").HasMaxLength(128);
        builder.Property(x => x.ChangedAt).HasColumnName("changed_at");
    }
}
