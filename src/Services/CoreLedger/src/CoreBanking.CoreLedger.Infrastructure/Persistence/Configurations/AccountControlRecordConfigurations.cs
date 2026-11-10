using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CoreBanking.CoreLedger.Infrastructure.Persistence.Configurations;

internal sealed class AccountRestrictionRecordConfiguration : IEntityTypeConfiguration<AccountRestrictionRecord>
{
    public void Configure(EntityTypeBuilder<AccountRestrictionRecord> builder)
    {
        builder.ToTable("account_restrictions", "account", table =>
        {
            table.HasCheckConstraint(
                "ck_account_restrictions_status",
                "status IN ('active', 'released', 'expired')");
            table.HasCheckConstraint(
                "ck_account_restrictions_direction",
                "posting_direction_code IN ('debit', 'credit', 'both')");
            table.HasCheckConstraint(
                "ck_account_restrictions_period",
                "effective_to IS NULL OR effective_to > effective_from");
        });
        builder.HasKey(x => new { x.InstitutionId, x.RestrictionId });
        builder.HasIndex(x => new { x.InstitutionId, x.AccountId, x.Status });
        builder.HasOne<AccountRecord>().WithMany()
            .HasForeignKey(x => new { x.InstitutionId, x.AccountId })
            .OnDelete(DeleteBehavior.Restrict);
        builder.Property(x => x.InstitutionId).HasColumnName("institution_id").HasMaxLength(128);
        builder.Property(x => x.RestrictionId).HasColumnName("restriction_id").HasMaxLength(128);
        builder.Property(x => x.AccountId).HasColumnName("account_id").HasMaxLength(128);
        builder.Property(x => x.RestrictionTypeCode).HasColumnName("restriction_type_code").HasMaxLength(64);
        builder.Property(x => x.PostingDirectionCode).HasColumnName("posting_direction_code").HasMaxLength(16);
        builder.Property(x => x.ChannelScopeJson).HasColumnName("channel_scope_json").HasColumnType("jsonb");
        builder.Property(x => x.TransactionTypeScopeJson).HasColumnName("transaction_type_scope_json").HasColumnType("jsonb");
        builder.Property(x => x.ReasonCode).HasColumnName("reason_code").HasMaxLength(64);
        builder.Property(x => x.AuthorityReference).HasColumnName("authority_reference").HasMaxLength(128);
        builder.Property(x => x.Status).HasColumnName("status").HasMaxLength(32);
        builder.Property(x => x.RequiresManualRelease).HasColumnName("requires_manual_release");
        builder.Property(x => x.RequestId).HasColumnName("request_id").HasMaxLength(128);
        builder.Property(x => x.AppliedByActorId).HasColumnName("applied_by_actor_id").HasMaxLength(128);
        builder.Property(x => x.ReleasedByActorId).HasColumnName("released_by_actor_id").HasMaxLength(128);
        builder.Property(x => x.ReleasedAt).HasColumnName("released_at");
        builder.Property(x => x.ControlVersion).HasColumnName("control_version");
        builder.Property(x => x.EffectiveFrom).HasColumnName("effective_from");
        builder.Property(x => x.EffectiveTo).HasColumnName("effective_to");
    }
}

internal sealed class AccountFreezeRecordConfiguration : IEntityTypeConfiguration<AccountFreezeRecord>
{
    public void Configure(EntityTypeBuilder<AccountFreezeRecord> builder)
    {
        builder.ToTable("account_freezes", "account", table =>
        {
            table.HasCheckConstraint("ck_account_freezes_status", "status IN ('active', 'released', 'expired')");
            table.HasCheckConstraint(
                "ck_account_freezes_type",
                "freeze_type_code IN ('regulatory', 'institution')");
            table.HasCheckConstraint(
                "ck_account_freezes_period",
                "effective_to IS NULL OR effective_to > effective_from");
        });
        builder.HasKey(x => new { x.InstitutionId, x.FreezeId });
        builder.HasIndex(x => new { x.InstitutionId, x.AccountId, x.Status });
        builder.HasOne<AccountRecord>().WithMany()
            .HasForeignKey(x => new { x.InstitutionId, x.AccountId })
            .OnDelete(DeleteBehavior.Restrict);
        builder.Property(x => x.InstitutionId).HasColumnName("institution_id").HasMaxLength(128);
        builder.Property(x => x.FreezeId).HasColumnName("freeze_id").HasMaxLength(128);
        builder.Property(x => x.AccountId).HasColumnName("account_id").HasMaxLength(128);
        builder.Property(x => x.FreezeTypeCode).HasColumnName("freeze_type_code").HasMaxLength(32);
        builder.Property(x => x.ReasonCode).HasColumnName("reason_code").HasMaxLength(64);
        builder.Property(x => x.AuthorityReference).HasColumnName("authority_reference").HasMaxLength(128);
        builder.Property(x => x.Status).HasColumnName("status").HasMaxLength(32);
        builder.Property(x => x.RequiresManualRelease).HasColumnName("requires_manual_release");
        builder.Property(x => x.RequestId).HasColumnName("request_id").HasMaxLength(128);
        builder.Property(x => x.AppliedByActorId).HasColumnName("applied_by_actor_id").HasMaxLength(128);
        builder.Property(x => x.ReleasedByActorId).HasColumnName("released_by_actor_id").HasMaxLength(128);
        builder.Property(x => x.ReleasedAt).HasColumnName("released_at");
        builder.Property(x => x.ControlVersion).HasColumnName("control_version");
        builder.Property(x => x.EffectiveFrom).HasColumnName("effective_from");
        builder.Property(x => x.EffectiveTo).HasColumnName("effective_to");
    }
}

internal sealed class AccountBlockRecordConfiguration : IEntityTypeConfiguration<AccountBlockRecord>
{
    public void Configure(EntityTypeBuilder<AccountBlockRecord> builder)
    {
        builder.ToTable("account_blocks", "account", table =>
        {
            table.HasCheckConstraint("ck_account_blocks_status", "status IN ('active', 'released', 'expired')");
            table.HasCheckConstraint(
                "ck_account_blocks_period",
                "effective_to IS NULL OR effective_to > effective_from");
        });
        builder.HasKey(x => new { x.InstitutionId, x.BlockId });
        builder.HasIndex(x => new { x.InstitutionId, x.AccountId, x.Status });
        builder.HasOne<AccountRecord>().WithMany()
            .HasForeignKey(x => new { x.InstitutionId, x.AccountId })
            .OnDelete(DeleteBehavior.Restrict);
        builder.Property(x => x.InstitutionId).HasColumnName("institution_id").HasMaxLength(128);
        builder.Property(x => x.BlockId).HasColumnName("block_id").HasMaxLength(128);
        builder.Property(x => x.AccountId).HasColumnName("account_id").HasMaxLength(128);
        builder.Property(x => x.BlockTypeCode).HasColumnName("block_type_code").HasMaxLength(64);
        builder.Property(x => x.OperationScopeJson).HasColumnName("operation_scope_json").HasColumnType("jsonb");
        builder.Property(x => x.ReasonCode).HasColumnName("reason_code").HasMaxLength(64);
        builder.Property(x => x.AuthorityReference).HasColumnName("authority_reference").HasMaxLength(128);
        builder.Property(x => x.Status).HasColumnName("status").HasMaxLength(32);
        builder.Property(x => x.RequiresManualRelease).HasColumnName("requires_manual_release");
        builder.Property(x => x.RequestId).HasColumnName("request_id").HasMaxLength(128);
        builder.Property(x => x.AppliedByActorId).HasColumnName("applied_by_actor_id").HasMaxLength(128);
        builder.Property(x => x.ReleasedByActorId).HasColumnName("released_by_actor_id").HasMaxLength(128);
        builder.Property(x => x.ReleasedAt).HasColumnName("released_at");
        builder.Property(x => x.ControlVersion).HasColumnName("control_version");
        builder.Property(x => x.EffectiveFrom).HasColumnName("effective_from");
        builder.Property(x => x.EffectiveTo).HasColumnName("effective_to");
    }
}
