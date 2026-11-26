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

internal sealed class FinancialReversalRecordConfiguration : IEntityTypeConfiguration<FinancialReversalRecord>
{
    public void Configure(EntityTypeBuilder<FinancialReversalRecord> builder)
    {
        builder.ToTable("financial_reversals", "ledger", table =>
        {
            table.HasCheckConstraint("ck_financial_reversals_amounts", "reversed_amount > 0 AND remaining_after >= 0");
            table.HasCheckConstraint("ck_financial_reversals_distinct_transactions", "original_financial_transaction_id <> reversal_financial_transaction_id");
            table.HasCheckConstraint("ck_financial_reversals_workflow_evidence", "(workflow_decision_id IS NULL AND maker_actor_id IS NULL AND approver_actor_id IS NULL) OR (workflow_decision_id IS NOT NULL AND maker_actor_id IS NOT NULL AND approver_actor_id IS NOT NULL AND maker_actor_id <> approver_actor_id)");
        });
        builder.HasKey(x => new { x.InstitutionId, x.FinancialReversalId });
        builder.HasIndex(x => new { x.InstitutionId, x.OriginalFinancialTransactionId }).IsUnique();
        builder.HasIndex(x => new { x.InstitutionId, x.ReversalFinancialTransactionId }).IsUnique();
        builder.HasOne<FinancialTransactionRecord>().WithMany()
            .HasForeignKey(x => new { x.InstitutionId, x.OriginalFinancialTransactionId })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<FinancialTransactionRecord>().WithMany()
            .HasForeignKey(x => new { x.InstitutionId, x.ReversalFinancialTransactionId })
            .OnDelete(DeleteBehavior.Restrict);
        builder.Property(x => x.InstitutionId).HasColumnName("institution_id").HasMaxLength(128);
        builder.Property(x => x.FinancialReversalId).HasColumnName("financial_reversal_id").HasMaxLength(128);
        builder.Property(x => x.OriginalFinancialTransactionId).HasColumnName("original_financial_transaction_id").HasMaxLength(128);
        builder.Property(x => x.ReversalFinancialTransactionId).HasColumnName("reversal_financial_transaction_id").HasMaxLength(128);
        builder.Property(x => x.OriginalJournalEntryId).HasColumnName("original_journal_entry_id").HasMaxLength(128);
        builder.Property(x => x.ReversalJournalEntryId).HasColumnName("reversal_journal_entry_id").HasMaxLength(128);
        builder.Property(x => x.MappingVersionId).HasColumnName("mapping_version_id").HasMaxLength(128);
        builder.Property(x => x.CurrencyCode).HasColumnName("currency_code").HasMaxLength(3);
        builder.Property(x => x.ReversedAmount).HasColumnName("reversed_amount").HasPrecision(19, 4);
        builder.Property(x => x.RemainingAfter).HasColumnName("remaining_after").HasPrecision(19, 4);
        builder.Property(x => x.ReasonCode).HasColumnName("reason_code").HasMaxLength(64);
        builder.Property(x => x.EvidenceReference).HasColumnName("evidence_reference").HasMaxLength(256);
        builder.Property(x => x.WorkflowDecisionId).HasColumnName("workflow_decision_id").HasMaxLength(128);
        builder.Property(x => x.MakerActorId).HasColumnName("maker_actor_id").HasMaxLength(128);
        builder.Property(x => x.ApproverActorId).HasColumnName("approver_actor_id").HasMaxLength(128);
        builder.Property(x => x.OriginalValueDate).HasColumnName("original_value_date");
        builder.Property(x => x.ReversalValueDate).HasColumnName("reversal_value_date");
        builder.Property(x => x.OccurredAt).HasColumnName("occurred_at");
    }
}


internal sealed class LedgerBalanceRecordConfiguration : IEntityTypeConfiguration<LedgerBalanceRecord>
{
    public void Configure(EntityTypeBuilder<LedgerBalanceRecord> builder)
    {
        builder.ToTable("account_balances", "ledger", table =>
        {
            table.HasCheckConstraint("ck_account_balances_normal_side", "normal_side IN ('debit', 'credit')");
            table.HasCheckConstraint("ck_account_balances_totals", "debit_total >= 0 AND credit_total >= 0");
            table.HasCheckConstraint("ck_account_balances_net", "net_balance = debit_total - credit_total");
            table.HasCheckConstraint("ck_account_balances_deductions", "credit_limit >= 0 AND hold_remaining >= 0 AND pending_authorised >= 0");
            table.HasCheckConstraint("ck_account_balances_version", "version > 0");
        });
        builder.HasKey(x => new { x.InstitutionId, x.LedgerAccountId });
        builder.HasOne<LedgerAccountRecord>().WithOne()
            .HasForeignKey<LedgerBalanceRecord>(x => new { x.InstitutionId, x.LedgerAccountId })
            .OnDelete(DeleteBehavior.Restrict);
        builder.Property(x => x.InstitutionId).HasColumnName("institution_id").HasMaxLength(128);
        builder.Property(x => x.LedgerAccountId).HasColumnName("ledger_account_id").HasMaxLength(128);
        builder.Property(x => x.CurrencyCode).HasColumnName("currency_code").HasMaxLength(3);
        builder.Property(x => x.NormalSide).HasColumnName("normal_side").HasMaxLength(8);
        builder.Property(x => x.DebitTotal).HasColumnName("debit_total").HasPrecision(19, 4);
        builder.Property(x => x.CreditTotal).HasColumnName("credit_total").HasPrecision(19, 4);
        builder.Property(x => x.NetBalance).HasColumnName("net_balance").HasPrecision(19, 4);
        builder.Property(x => x.CreditLimit).HasColumnName("credit_limit").HasPrecision(19, 4);
        builder.Property(x => x.HoldRemaining).HasColumnName("hold_remaining").HasPrecision(19, 4);
        builder.Property(x => x.PendingAuthorised).HasColumnName("pending_authorised").HasPrecision(19, 4);
        builder.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken();
        builder.Property(x => x.AsOf).HasColumnName("as_of");
    }
}

internal sealed class LedgerMovementRecordConfiguration : IEntityTypeConfiguration<LedgerMovementRecord>
{
    public void Configure(EntityTypeBuilder<LedgerMovementRecord> builder)
    {
        builder.ToTable("account_movements", "ledger", table =>
        {
            table.HasCheckConstraint("ck_account_movements_direction", "direction IN ('debit', 'credit')");
            table.HasCheckConstraint("ck_account_movements_amount", "amount > 0");
            table.HasCheckConstraint("ck_account_movements_signed_amount", "(direction = 'debit' AND signed_amount = amount) OR (direction = 'credit' AND signed_amount = -amount)");
            table.HasCheckConstraint("ck_account_movements_totals", "debit_total_after >= 0 AND credit_total_after >= 0");
            table.HasCheckConstraint("ck_account_movements_version", "balance_version > 1");
        });
        builder.HasKey(x => new { x.InstitutionId, x.MovementId });
        builder.HasIndex(x => new { x.InstitutionId, x.JournalLineId }).IsUnique();
        builder.HasIndex(x => new { x.InstitutionId, x.LedgerAccountId, x.BalanceVersion }).IsUnique();
        builder.HasOne<JournalLineRecord>().WithOne()
            .HasForeignKey<LedgerMovementRecord>(x => new { x.InstitutionId, x.JournalLineId })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<LedgerBalanceRecord>().WithMany()
            .HasForeignKey(x => new { x.InstitutionId, x.LedgerAccountId })
            .OnDelete(DeleteBehavior.Restrict);
        builder.Property(x => x.InstitutionId).HasColumnName("institution_id").HasMaxLength(128);
        builder.Property(x => x.MovementId).HasColumnName("movement_id").HasMaxLength(128);
        builder.Property(x => x.JournalLineId).HasColumnName("journal_line_id").HasMaxLength(128);
        builder.Property(x => x.LedgerAccountId).HasColumnName("ledger_account_id").HasMaxLength(128);
        builder.Property(x => x.Direction).HasColumnName("direction").HasMaxLength(8);
        builder.Property(x => x.Amount).HasColumnName("amount").HasPrecision(19, 4);
        builder.Property(x => x.SignedAmount).HasColumnName("signed_amount").HasPrecision(19, 4);
        builder.Property(x => x.VisibleBalanceBefore).HasColumnName("visible_balance_before").HasPrecision(19, 4);
        builder.Property(x => x.VisibleBalanceAfter).HasColumnName("visible_balance_after").HasPrecision(19, 4);
        builder.Property(x => x.DebitTotalAfter).HasColumnName("debit_total_after").HasPrecision(19, 4);
        builder.Property(x => x.CreditTotalAfter).HasColumnName("credit_total_after").HasPrecision(19, 4);
        builder.Property(x => x.BalanceVersion).HasColumnName("balance_version");
        builder.Property(x => x.OccurredAt).HasColumnName("occurred_at");
    }
}

internal sealed class LedgerHoldRecordConfiguration : IEntityTypeConfiguration<LedgerHoldRecord>
{
    public void Configure(EntityTypeBuilder<LedgerHoldRecord> builder)
    {
        builder.ToTable("holds", "ledger", table =>
        {
            table.HasCheckConstraint("ck_holds_amounts", "original_amount > 0 AND remaining_amount >= 0 AND captured_amount >= 0 AND released_amount >= 0 AND remaining_amount + captured_amount + released_amount = original_amount");
            table.HasCheckConstraint("ck_holds_status", "status IN ('active', 'partially_captured', 'captured', 'released', 'expired')");
            table.HasCheckConstraint("ck_holds_version", "version > 0");
            table.HasCheckConstraint("ck_holds_expiry", "expires_at > created_at");
        });
        builder.HasKey(x => new { x.InstitutionId, x.HoldId });
        builder.HasIndex(x => new { x.InstitutionId, x.SourceService, x.SourceReference }).IsUnique();
        builder.HasIndex(x => new { x.InstitutionId, x.LedgerAccountId, x.Status, x.ExpiresAt });
        builder.HasOne<LedgerAccountRecord>().WithMany()
            .HasForeignKey(x => new { x.InstitutionId, x.LedgerAccountId })
            .OnDelete(DeleteBehavior.Restrict);
        builder.Property(x => x.InstitutionId).HasColumnName("institution_id").HasMaxLength(128);
        builder.Property(x => x.HoldId).HasColumnName("hold_id").HasMaxLength(128);
        builder.Property(x => x.LedgerAccountId).HasColumnName("ledger_account_id").HasMaxLength(128);
        builder.Property(x => x.SourceService).HasColumnName("source_service").HasMaxLength(128);
        builder.Property(x => x.SourceReference).HasColumnName("source_reference").HasMaxLength(128);
        builder.Property(x => x.PurposeCode).HasColumnName("purpose_code").HasMaxLength(64);
        builder.Property(x => x.CurrencyCode).HasColumnName("currency_code").HasMaxLength(3);
        builder.Property(x => x.OriginalAmount).HasColumnName("original_amount").HasPrecision(19, 4);
        builder.Property(x => x.RemainingAmount).HasColumnName("remaining_amount").HasPrecision(19, 4);
        builder.Property(x => x.CapturedAmount).HasColumnName("captured_amount").HasPrecision(19, 4);
        builder.Property(x => x.ReleasedAmount).HasColumnName("released_amount").HasPrecision(19, 4);
        builder.Property(x => x.Status).HasColumnName("status").HasMaxLength(32);
        builder.Property(x => x.ExpiresAt).HasColumnName("expires_at");
        builder.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at");
    }
}

internal sealed class LedgerHoldHistoryRecordConfiguration : IEntityTypeConfiguration<LedgerHoldHistoryRecord>
{
    public void Configure(EntityTypeBuilder<LedgerHoldHistoryRecord> builder)
    {
        builder.ToTable("hold_status_history", "ledger", table =>
        {
            table.HasCheckConstraint("ck_hold_history_sequence", "sequence > 0");
            table.HasCheckConstraint("ck_hold_history_amounts", "amount >= 0 AND remaining_after >= 0");
        });
        builder.HasKey(x => new { x.InstitutionId, x.HistoryId });
        builder.HasIndex(x => new { x.InstitutionId, x.HoldId, x.Sequence }).IsUnique();
        builder.HasOne<LedgerHoldRecord>().WithMany()
            .HasForeignKey(x => new { x.InstitutionId, x.HoldId })
            .OnDelete(DeleteBehavior.Restrict);
        builder.Property(x => x.InstitutionId).HasColumnName("institution_id").HasMaxLength(128);
        builder.Property(x => x.HistoryId).HasColumnName("history_id").HasMaxLength(128);
        builder.Property(x => x.HoldId).HasColumnName("hold_id").HasMaxLength(128);
        builder.Property(x => x.Sequence).HasColumnName("sequence");
        builder.Property(x => x.Transition).HasColumnName("transition").HasMaxLength(32);
        builder.Property(x => x.Amount).HasColumnName("amount").HasPrecision(19, 4);
        builder.Property(x => x.RemainingAfter).HasColumnName("remaining_after").HasPrecision(19, 4);
        builder.Property(x => x.FinancialTransactionId).HasColumnName("financial_transaction_id").HasMaxLength(128);
        builder.Property(x => x.ReasonCode).HasColumnName("reason_code").HasMaxLength(128);
        builder.Property(x => x.ActorId).HasColumnName("actor_id").HasMaxLength(128);
        builder.Property(x => x.OccurredAt).HasColumnName("occurred_at");
    }
}
