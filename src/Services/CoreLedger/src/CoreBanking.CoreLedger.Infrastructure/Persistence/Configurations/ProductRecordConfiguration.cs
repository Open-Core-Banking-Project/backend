using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CoreBanking.CoreLedger.Infrastructure.Persistence.Configurations;

internal sealed class ProductRecordConfiguration : IEntityTypeConfiguration<ProductRecord>
{
    public void Configure(EntityTypeBuilder<ProductRecord> builder)
    {
        builder.ToTable("account_products", "product", table =>
            table.HasCheckConstraint("ck_account_products_family", "family IN ('savings')"));
        builder.HasKey(x => new { x.InstitutionId, x.ProductId });
        builder.HasIndex(x => new { x.InstitutionId, x.Code }).IsUnique();
        builder.HasIndex(x => new { x.InstitutionId, x.ConfigurationSetId }).IsUnique();
        builder.HasOne<ConfigurationSetRecord>().WithOne()
            .HasForeignKey<ProductRecord>(x => new { x.InstitutionId, x.ConfigurationSetId })
            .OnDelete(DeleteBehavior.Restrict);
        builder.Property(x => x.InstitutionId).HasColumnName("institution_id").HasMaxLength(128);
        builder.Property(x => x.ProductId).HasColumnName("product_id").HasMaxLength(128);
        builder.Property(x => x.Code).HasColumnName("code").HasMaxLength(64);
        builder.Property(x => x.Family).HasColumnName("family").HasMaxLength(32);
        builder.Property(x => x.ConfigurationSetId).HasColumnName("configuration_set_id").HasMaxLength(128);
        builder.Property(x => x.CreatedByActorId).HasColumnName("created_by_actor_id").HasMaxLength(128);
        builder.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at");
    }
}
