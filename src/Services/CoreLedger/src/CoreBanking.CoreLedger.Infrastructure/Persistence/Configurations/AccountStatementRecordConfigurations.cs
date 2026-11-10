using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CoreBanking.CoreLedger.Infrastructure.Persistence.Configurations;

internal sealed class AccountStatementRequestRecordConfiguration : IEntityTypeConfiguration<AccountStatementRequestRecord>
{
    public void Configure(EntityTypeBuilder<AccountStatementRequestRecord> builder)
    {
        builder.ToTable("account_statement_requests", "account", table =>
        {
            table.HasCheckConstraint(
                "ck_account_statement_requests_status",
                "status IN ('refused', 'awaiting_artifact', 'ready', 'expired')");
            table.HasCheckConstraint(
                "ck_account_statement_requests_format",
                "format_code IN ('pdf', 'csv')");
            table.HasCheckConstraint(
                "ck_account_statement_requests_period",
                "period_to >= period_from");
            table.HasCheckConstraint("ck_account_statement_requests_version", "version >= 1");
        });
        builder.HasKey(x => new { x.InstitutionId, x.RequestId });
        builder.HasIndex(x => new { x.InstitutionId, x.AccountId, x.RequestedAt });
        builder.HasOne<AccountRecord>().WithMany()
            .HasForeignKey(x => new { x.InstitutionId, x.AccountId })
            .OnDelete(DeleteBehavior.Restrict);
        builder.Property(x => x.InstitutionId).HasColumnName("institution_id").HasMaxLength(128);
        builder.Property(x => x.RequestId).HasColumnName("request_id").HasMaxLength(128);
        builder.Property(x => x.AccountId).HasColumnName("account_id").HasMaxLength(128);
        builder.Property(x => x.PeriodFrom).HasColumnName("period_from");
        builder.Property(x => x.PeriodTo).HasColumnName("period_to");
        builder.Property(x => x.FormatCode).HasColumnName("format_code").HasMaxLength(16);
        builder.Property(x => x.PurposeCode).HasColumnName("purpose_code").HasMaxLength(32);
        builder.Property(x => x.Status).HasColumnName("status").HasMaxLength(32);
        builder.Property(x => x.PolicyVersionId).HasColumnName("policy_version_id").HasMaxLength(128);
        builder.Property(x => x.RequesterTypeCode).HasColumnName("requester_type_code").HasMaxLength(32);
        builder.Property(x => x.RequesterActorId).HasColumnName("requester_actor_id").HasMaxLength(128);
        builder.Property(x => x.LedgerQueryReference).HasColumnName("ledger_query_reference").HasMaxLength(256);
        builder.Property(x => x.IntegrityReference).HasColumnName("integrity_reference").HasMaxLength(256);
        builder.Property(x => x.RefusalCodes).HasColumnName("refusal_codes").HasMaxLength(256);
        builder.Property(x => x.ArtifactReference).HasColumnName("artifact_reference").HasMaxLength(128);
        builder.Property(x => x.ArtifactReadyAt).HasColumnName("artifact_ready_at");
        builder.Property(x => x.RetentionExpiresAt).HasColumnName("retention_expires_at");
        builder.Property(x => x.LegalHold).HasColumnName("legal_hold");
        builder.Property(x => x.RequestedAt).HasColumnName("requested_at");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        builder.Property(x => x.Version).HasColumnName("version");
    }
}

internal sealed class AccountStatementArtifactReferenceRecordConfiguration
    : IEntityTypeConfiguration<AccountStatementArtifactReferenceRecord>
{
    public void Configure(EntityTypeBuilder<AccountStatementArtifactReferenceRecord> builder)
    {
        builder.ToTable("account_statement_artifact_references", "account", table =>
        {
            table.HasCheckConstraint(
                "ck_account_statement_artifact_references_expiry",
                "expires_at > issued_at");
        });
        builder.HasKey(x => new { x.InstitutionId, x.ReferenceId });
        builder.HasIndex(x => new { x.InstitutionId, x.RequestId }).IsUnique();
        builder.HasOne<AccountStatementRequestRecord>().WithMany()
            .HasForeignKey(x => new { x.InstitutionId, x.RequestId })
            .OnDelete(DeleteBehavior.Restrict);
        builder.Property(x => x.InstitutionId).HasColumnName("institution_id").HasMaxLength(128);
        builder.Property(x => x.ReferenceId).HasColumnName("reference_id").HasMaxLength(128);
        builder.Property(x => x.RequestId).HasColumnName("request_id").HasMaxLength(128);
        builder.Property(x => x.AccountId).HasColumnName("account_id").HasMaxLength(128);
        builder.Property(x => x.ArtifactReference).HasColumnName("artifact_reference").HasMaxLength(128);
        builder.Property(x => x.RequesterTypeCode).HasColumnName("requester_type_code").HasMaxLength(32);
        builder.Property(x => x.RequesterActorId).HasColumnName("requester_actor_id").HasMaxLength(128);
        builder.Property(x => x.IssuedAt).HasColumnName("issued_at");
        builder.Property(x => x.ExpiresAt).HasColumnName("expires_at");
        builder.Property(x => x.AccessCount).HasColumnName("access_count");
        builder.Property(x => x.LastAccessedAt).HasColumnName("last_accessed_at");
    }
}
