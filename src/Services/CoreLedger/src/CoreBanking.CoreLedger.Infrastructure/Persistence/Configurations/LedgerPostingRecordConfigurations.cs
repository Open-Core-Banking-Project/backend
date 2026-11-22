using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CoreBanking.CoreLedger.Infrastructure.Persistence.Configurations;

internal sealed class FinancialTransactionRecordConfiguration : IEntityTypeConfiguration<FinancialTransactionRecord>
{
    public void Configure(EntityTypeBuilder<FinancialTransactionRecord> builder)
    {
        builder.ToTable("financial_transactions", "ledger", table =>
        {
            table.HasCheckConstraint("ck_financial_transactions_status", "status IN ('received', 'pending_authorisation', 'ready_to_post', 'posted', 'rejected', 'failed_before_commit', 'disputed', 'reversed')");
            table.HasCheckConstraint("ck_financial_transactions_request_hash", "request_hash ~ '^[0-9a-f]{64}$'");
            table.HasCheckConstraint("ck_financial_transactions_external_reference", "(external_reference_producer IS NULL AND external_reference_type IS NULL AND external_reference IS NULL) OR (external_reference_producer IS NOT NULL AND external_reference_type IS NOT NULL AND external_reference IS NOT NULL)");
            table.HasCheckConstraint("ck_financial_transactions_external_reference_type", "external_reference_type IS NULL OR external_reference_type IN ('provider_reference', 'channel_reference', 'settlement_reference', 'statement_reference', 'external_reference', 'internal_reference')");
            table.HasCheckConstraint("ck_financial_transactions_posted_at", "(status = 'posted' AND posted_at IS NOT NULL) OR (status <> 'posted' AND posted_at IS NULL)");
        });
        builder.HasKey(x => new { x.InstitutionId, x.FinancialTransactionId });
        builder.HasIndex(x => new { x.InstitutionId, x.TransactionReference }).IsUnique();
        builder.HasIndex(x => new { x.InstitutionId, x.SourceService, x.SourceReference }).IsUnique();
        builder.HasIndex(x => new { x.InstitutionId, x.ExternalReferenceProducer, x.ExternalReferenceType, x.ExternalReference })
            .IsUnique()
            .HasFilter("external_reference IS NOT NULL");
        builder.Property(x => x.InstitutionId).HasColumnName("institution_id").HasMaxLength(128);
        builder.Property(x => x.FinancialTransactionId).HasColumnName("financial_transaction_id").HasMaxLength(128);
        builder.Property(x => x.TransactionReference).HasColumnName("transaction_reference").HasMaxLength(128).UseCollation("C");
        builder.Property(x => x.SourceService).HasColumnName("source_service").HasMaxLength(128).UseCollation("C");
        builder.Property(x => x.SourceReference).HasColumnName("source_reference").HasMaxLength(128).UseCollation("C");
        builder.Property(x => x.ExternalReferenceProducer).HasColumnName("external_reference_producer").HasMaxLength(128).UseCollation("C");
        builder.Property(x => x.ExternalReferenceType).HasColumnName("external_reference_type").HasMaxLength(32);
        builder.Property(x => x.ExternalReference).HasColumnName("external_reference").HasMaxLength(128).UseCollation("C");
        builder.Property(x => x.EventCode).HasColumnName("event_code").HasMaxLength(128);
        builder.Property(x => x.RequestHash).HasColumnName("request_hash").HasMaxLength(64).UseCollation("C");
        builder.Property(x => x.Status).HasColumnName("status").HasMaxLength(32);
        builder.Property(x => x.TransactionTimestamp).HasColumnName("transaction_timestamp");
        builder.Property(x => x.BusinessDate).HasColumnName("business_date");
        builder.Property(x => x.PostingDate).HasColumnName("posting_date");
        builder.Property(x => x.ValueDate).HasColumnName("value_date");
        builder.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at");
        builder.Property(x => x.PostedAt).HasColumnName("posted_at");
    }
}

internal sealed class FinancialTransactionStatusHistoryRecordConfiguration : IEntityTypeConfiguration<FinancialTransactionStatusHistoryRecord>
{
    public void Configure(EntityTypeBuilder<FinancialTransactionStatusHistoryRecord> builder)
    {
        builder.ToTable("transaction_status_history", "ledger", table =>
        {
            table.HasCheckConstraint("ck_transaction_status_history_sequence", "sequence > 0");
            table.HasCheckConstraint("ck_transaction_status_history_status", "status IN ('received', 'pending_authorisation', 'ready_to_post', 'posted', 'rejected', 'failed_before_commit', 'disputed', 'reversed')");
        });
        builder.HasKey(x => new { x.InstitutionId, x.HistoryId });
        builder.HasIndex(x => new { x.InstitutionId, x.FinancialTransactionId, x.Sequence }).IsUnique();
        builder.HasOne<FinancialTransactionRecord>().WithMany()
            .HasForeignKey(x => new { x.InstitutionId, x.FinancialTransactionId })
            .OnDelete(DeleteBehavior.Restrict);
        builder.Property(x => x.InstitutionId).HasColumnName("institution_id").HasMaxLength(128);
        builder.Property(x => x.HistoryId).HasColumnName("history_id").HasMaxLength(128);
        builder.Property(x => x.FinancialTransactionId).HasColumnName("financial_transaction_id").HasMaxLength(128);
        builder.Property(x => x.Sequence).HasColumnName("sequence");
        builder.Property(x => x.Status).HasColumnName("status").HasMaxLength(32);
        builder.Property(x => x.ReasonCode).HasColumnName("reason_code").HasMaxLength(64);
        builder.Property(x => x.ActorId).HasColumnName("actor_id").HasMaxLength(128);
        builder.Property(x => x.OccurredAt).HasColumnName("occurred_at");
    }
}

internal sealed class JournalEntryRecordConfiguration : IEntityTypeConfiguration<JournalEntryRecord>
{
    public void Configure(EntityTypeBuilder<JournalEntryRecord> builder)
    {
        builder.ToTable("journal_entries", "ledger", table =>
        {
            table.HasCheckConstraint("ck_journal_entries_status", "status = 'posted'");
            table.HasCheckConstraint("ck_journal_entries_balanced_totals", "total_debits > 0 AND total_debits = total_credits");
        });
        builder.HasKey(x => new { x.InstitutionId, x.JournalEntryId });
        builder.HasIndex(x => new { x.InstitutionId, x.JournalReference }).IsUnique();
        builder.HasOne<FinancialTransactionRecord>().WithMany()
            .HasForeignKey(x => new { x.InstitutionId, x.FinancialTransactionId })
            .OnDelete(DeleteBehavior.Restrict);
        builder.Property(x => x.InstitutionId).HasColumnName("institution_id").HasMaxLength(128);
        builder.Property(x => x.JournalEntryId).HasColumnName("journal_entry_id").HasMaxLength(128);
        builder.Property(x => x.FinancialTransactionId).HasColumnName("financial_transaction_id").HasMaxLength(128);
        builder.Property(x => x.JournalReference).HasColumnName("journal_reference").HasMaxLength(128).UseCollation("C");
        builder.Property(x => x.MappingVersionId).HasColumnName("mapping_version_id").HasMaxLength(128);
        builder.Property(x => x.LegalEntityCode).HasColumnName("legal_entity_code").HasMaxLength(64);
        builder.Property(x => x.CurrencyCode).HasColumnName("currency_code").HasMaxLength(3);
        builder.Property(x => x.Status).HasColumnName("status").HasMaxLength(16);
        builder.Property(x => x.TotalDebits).HasColumnName("total_debits").HasPrecision(19, 4);
        builder.Property(x => x.TotalCredits).HasColumnName("total_credits").HasPrecision(19, 4);
        builder.Property(x => x.TransactionTimestamp).HasColumnName("transaction_timestamp");
        builder.Property(x => x.BusinessDate).HasColumnName("business_date");
        builder.Property(x => x.PostingDate).HasColumnName("posting_date");
        builder.Property(x => x.ValueDate).HasColumnName("value_date");
        builder.Property(x => x.CreatedAt).HasColumnName("created_at");
    }
}

internal sealed class JournalLineRecordConfiguration : IEntityTypeConfiguration<JournalLineRecord>
{
    public void Configure(EntityTypeBuilder<JournalLineRecord> builder)
    {
        builder.ToTable("journal_lines", "ledger", table =>
        {
            table.HasCheckConstraint("ck_journal_lines_sequence", "sequence > 0");
            table.HasCheckConstraint("ck_journal_lines_amount", "amount > 0");
            table.HasCheckConstraint("ck_journal_lines_direction", "direction IN ('debit', 'credit')");
            table.HasCheckConstraint("ck_journal_lines_component", "component_type IN ('principal', 'fee', 'tax', 'commission', 'penalty', 'interest', 'waiver', 'rounding')");
            table.HasCheckConstraint("ck_journal_lines_reason", "component_type = 'principal' OR reason_code IS NOT NULL");
        });
        builder.HasKey(x => new { x.InstitutionId, x.JournalLineId });
        builder.HasIndex(x => new { x.InstitutionId, x.JournalEntryId, x.Sequence }).IsUnique();
        builder.HasOne<JournalEntryRecord>().WithMany()
            .HasForeignKey(x => new { x.InstitutionId, x.JournalEntryId })
            .OnDelete(DeleteBehavior.Restrict);
        builder.Property(x => x.InstitutionId).HasColumnName("institution_id").HasMaxLength(128);
        builder.Property(x => x.JournalLineId).HasColumnName("journal_line_id").HasMaxLength(128);
        builder.Property(x => x.JournalEntryId).HasColumnName("journal_entry_id").HasMaxLength(128);
        builder.Property(x => x.Sequence).HasColumnName("sequence");
        builder.Property(x => x.LedgerAccountId).HasColumnName("ledger_account_id").HasMaxLength(128);
        builder.Property(x => x.LegalEntityCode).HasColumnName("legal_entity_code").HasMaxLength(64);
        builder.Property(x => x.CurrencyCode).HasColumnName("currency_code").HasMaxLength(3);
        builder.Property(x => x.Direction).HasColumnName("direction").HasMaxLength(8);
        builder.Property(x => x.ComponentType).HasColumnName("component_type").HasMaxLength(32);
        builder.Property(x => x.Amount).HasColumnName("amount").HasPrecision(19, 4);
        builder.Property(x => x.ReasonCode).HasColumnName("reason_code").HasMaxLength(64);
        builder.Property(x => x.CreatedAt).HasColumnName("created_at");
    }
}
