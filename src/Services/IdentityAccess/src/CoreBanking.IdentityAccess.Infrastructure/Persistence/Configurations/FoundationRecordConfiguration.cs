using CoreBanking.IdentityAccess.Infrastructure.Persistence.Platform;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CoreBanking.IdentityAccess.Infrastructure.Persistence.Configurations;

/// <summary>Mapping for the synthetic Phase 0 foundation record. Decision: ADR-0004, epic 04.</summary>
internal sealed class FoundationRecordConfiguration : IEntityTypeConfiguration<FoundationRecord>
{
    public void Configure(EntityTypeBuilder<FoundationRecord> builder)
    {
        builder.ToTable("foundation_records", "platform");
        builder.HasKey(record => new { record.InstitutionId, record.FoundationRecordId });

        builder.Property(record => record.InstitutionId).HasColumnName("institution_id");
        builder.Property(record => record.FoundationRecordId).HasColumnName("foundation_record_id");
        builder.Property(record => record.Description).HasColumnName("description");
        builder.Property(record => record.CreatedBy).HasColumnName("created_by");
        builder.Property(record => record.CreatedAt).HasColumnName("created_at");
    }
}
