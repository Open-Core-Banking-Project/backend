using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CoreBanking.CoreLedger.Infrastructure.Persistence.Configurations;

internal sealed class AccountLienRecordConfiguration : IEntityTypeConfiguration<AccountLienRecord>
{
    public void Configure(EntityTypeBuilder<AccountLienRecord> builder)
    {
        builder.ToTable("account_liens", "account", table =>
        {
            table.HasCheckConstraint(
                "ck_account_liens_priority",
                "priority_code IN ('statutory_first', 'tax', 'institution', 'customer')");
            table.HasCheckConstraint("ck_account_liens_status", "status IN ('active', 'released')");
            table.HasCheckConstraint("ck_account_liens_amount", "amount > 0");
            table.HasCheckConstraint(
                "ck_account_liens_outstanding",
                "outstanding_amount >= 0 AND outstanding_amount <= amount");
            table.HasCheckConstraint(
                "ck_account_liens_period",
                "effective_to IS NULL OR effective_to > effective_from");
            table.HasCheckConstraint("ck_account_liens_rank", "priority_rank >= 0");
        });
        builder.HasKey(x => new { x.InstitutionId, x.LienId });
        builder.HasIndex(x => new { x.InstitutionId, x.AccountId, x.Status });
        builder.HasOne<AccountRecord>().WithMany()
            .HasForeignKey(x => new { x.InstitutionId, x.AccountId })
            .OnDelete(DeleteBehavior.Restrict);
        builder.Property(x => x.InstitutionId).HasColumnName("institution_id").HasMaxLength(128);
        builder.Property(x => x.LienId).HasColumnName("lien_id").HasMaxLength(128);
        builder.Property(x => x.AccountId).HasColumnName("account_id").HasMaxLength(128);
        builder.Property(x => x.PriorityCode).HasColumnName("priority_code").HasMaxLength(32);
        builder.Property(x => x.PriorityRank).HasColumnName("priority_rank");
        builder.Property(x => x.Amount).HasColumnName("amount").HasPrecision(19, 4);
        builder.Property(x => x.OutstandingAmount).HasColumnName("outstanding_amount").HasPrecision(19, 4);
        builder.Property(x => x.CurrencyCode).HasColumnName("currency_code").HasMaxLength(3);
        builder.Property(x => x.ReasonCode).HasColumnName("reason_code").HasMaxLength(64);
        builder.Property(x => x.AuthorityReference).HasColumnName("authority_reference").HasMaxLength(128);
        builder.Property(x => x.Status).HasColumnName("status").HasMaxLength(32);
        builder.Property(x => x.RequestId).HasColumnName("request_id").HasMaxLength(128);
        builder.Property(x => x.CreatedByActorId).HasColumnName("created_by_actor_id").HasMaxLength(128);
        builder.Property(x => x.EffectiveFrom).HasColumnName("effective_from");
        builder.Property(x => x.EffectiveTo).HasColumnName("effective_to");
        builder.Property(x => x.CreatedAt).HasColumnName("created_at");
    }
}

internal sealed class AccountLienMovementRecordConfiguration : IEntityTypeConfiguration<AccountLienMovementRecord>
{
    public void Configure(EntityTypeBuilder<AccountLienMovementRecord> builder)
    {
        builder.ToTable("account_lien_movements", "account", table =>
        {
            table.HasCheckConstraint(
                "ck_account_lien_movements_type",
                "movement_type IN ('partial_release', 'full_release', 'satisfaction')");
            table.HasCheckConstraint("ck_account_lien_movements_amount", "amount > 0");
            table.HasCheckConstraint("ck_account_lien_movements_outstanding", "outstanding_after >= 0");
        });
        builder.HasKey(x => new { x.InstitutionId, x.MovementId });
        builder.HasIndex(x => new { x.InstitutionId, x.LienId, x.OccurredAt });
        builder.HasOne<AccountLienRecord>().WithMany()
            .HasForeignKey(x => new { x.InstitutionId, x.LienId })
            .OnDelete(DeleteBehavior.Restrict);
        builder.Property(x => x.InstitutionId).HasColumnName("institution_id").HasMaxLength(128);
        builder.Property(x => x.MovementId).HasColumnName("movement_id").HasMaxLength(128);
        builder.Property(x => x.LienId).HasColumnName("lien_id").HasMaxLength(128);
        builder.Property(x => x.MovementType).HasColumnName("movement_type").HasMaxLength(32);
        builder.Property(x => x.Amount).HasColumnName("amount").HasPrecision(19, 4);
        builder.Property(x => x.OutstandingAfter).HasColumnName("outstanding_after").HasPrecision(19, 4);
        builder.Property(x => x.ReasonCode).HasColumnName("reason_code").HasMaxLength(64);
        builder.Property(x => x.AuthorityReference).HasColumnName("authority_reference").HasMaxLength(128);
        builder.Property(x => x.ActorId).HasColumnName("actor_id").HasMaxLength(128);
        builder.Property(x => x.OccurredAt).HasColumnName("occurred_at");
    }
}
