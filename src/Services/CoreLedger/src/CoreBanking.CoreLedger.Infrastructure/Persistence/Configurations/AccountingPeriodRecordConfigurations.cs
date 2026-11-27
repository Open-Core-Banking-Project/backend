using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CoreBanking.CoreLedger.Infrastructure.Persistence.Configurations;

internal sealed class AccountingPeriodRecordConfiguration : IEntityTypeConfiguration<AccountingPeriodRecord>
{
    public void Configure(EntityTypeBuilder<AccountingPeriodRecord> builder)
    {
        builder.ToTable("accounting_periods", "ledger", table =>
        {
            table.HasCheckConstraint(
                "ck_accounting_periods_status",
                "status IN ('open', 'soft_closed', 'closed', 'locked', 'reopened')");
            table.HasCheckConstraint("ck_accounting_periods_version", "version > 0");
            table.HasCheckConstraint(
                "ck_accounting_periods_closed_evidence",
                "status NOT IN ('closed', 'locked') OR (closed_at IS NOT NULL AND close_summary_hash IS NOT NULL"
                + " AND closed_debit_total IS NOT NULL AND closed_credit_total IS NOT NULL"
                + " AND closed_debit_total = closed_credit_total)");
            table.HasCheckConstraint(
                "ck_accounting_periods_reopen_evidence",
                "status <> 'reopened' OR (reopen_expires_at IS NOT NULL AND reopen_reason_code IS NOT NULL)");
            table.HasCheckConstraint(
                "ck_accounting_periods_locked_evidence",
                "status <> 'locked' OR locked_at IS NOT NULL");
        });

        builder.HasKey(x => new { x.InstitutionId, x.LegalEntityCode, x.PeriodCode });
        builder.Property(x => x.InstitutionId).HasColumnName("institution_id").HasMaxLength(128);
        builder.Property(x => x.LegalEntityCode).HasColumnName("legal_entity_code").HasMaxLength(64);
        builder.Property(x => x.PeriodCode).HasColumnName("period_code").HasMaxLength(7);
        builder.Property(x => x.Status).HasColumnName("status").HasMaxLength(32);
        builder.Property(x => x.AllowedEventCodesJson).HasColumnName("allowed_event_codes_json");
        builder.Property(x => x.Version).HasColumnName("version");
        builder.Property(x => x.ClosedDebitTotal).HasColumnName("closed_debit_total").HasPrecision(19, 4);
        builder.Property(x => x.ClosedCreditTotal).HasColumnName("closed_credit_total").HasPrecision(19, 4);
        builder.Property(x => x.CloseSummaryHash).HasColumnName("close_summary_hash").HasMaxLength(64);
        builder.Property(x => x.CloseApprovalReference).HasColumnName("close_approval_reference").HasMaxLength(256);
        builder.Property(x => x.ClosedAt).HasColumnName("closed_at");
        builder.Property(x => x.ReopenExpiresAt).HasColumnName("reopen_expires_at");
        builder.Property(x => x.ReopenReasonCode).HasColumnName("reopen_reason_code").HasMaxLength(64);
        builder.Property(x => x.ReclosedAt).HasColumnName("reclosed_at");
        builder.Property(x => x.RecloseReconciliationReference).HasColumnName("reclose_reconciliation_reference").HasMaxLength(256);
        builder.Property(x => x.LockedAt).HasColumnName("locked_at");
        builder.Property(x => x.LockApprovalReference).HasColumnName("lock_approval_reference").HasMaxLength(256);
        builder.Property(x => x.CreatedAt).HasColumnName("created_at");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at");
    }
}

internal sealed class AccountingPeriodHistoryRecordConfiguration : IEntityTypeConfiguration<AccountingPeriodHistoryRecord>
{
    public void Configure(EntityTypeBuilder<AccountingPeriodHistoryRecord> builder)
    {
        builder.ToTable("accounting_period_history", "ledger", table =>
        {
            table.HasCheckConstraint("ck_accounting_period_history_sequence", "sequence > 0");
            table.HasCheckConstraint(
                "ck_accounting_period_history_status",
                "from_status IN ('open', 'soft_closed', 'closed', 'locked', 'reopened')"
                + " AND to_status IN ('open', 'soft_closed', 'closed', 'locked', 'reopened')");
        });

        builder.HasKey(x => new { x.InstitutionId, x.LegalEntityCode, x.PeriodCode, x.Sequence });
        builder.Property(x => x.InstitutionId).HasColumnName("institution_id").HasMaxLength(128);
        builder.Property(x => x.LegalEntityCode).HasColumnName("legal_entity_code").HasMaxLength(64);
        builder.Property(x => x.PeriodCode).HasColumnName("period_code").HasMaxLength(7);
        builder.Property(x => x.Sequence).HasColumnName("sequence");
        builder.Property(x => x.FromStatus).HasColumnName("from_status").HasMaxLength(32);
        builder.Property(x => x.ToStatus).HasColumnName("to_status").HasMaxLength(32);
        builder.Property(x => x.ActorId).HasColumnName("actor_id").HasMaxLength(128);
        builder.Property(x => x.ApprovalReference).HasColumnName("approval_reference").HasMaxLength(256);
        builder.Property(x => x.AllowedEventCodesJson).HasColumnName("allowed_event_codes_json");
        builder.Property(x => x.ReasonCode).HasColumnName("reason_code").HasMaxLength(64);
        builder.Property(x => x.OccurredAt).HasColumnName("occurred_at");
    }
}

internal sealed class RecalculationHandoffRecordConfiguration : IEntityTypeConfiguration<RecalculationHandoffRecord>
{
    public void Configure(EntityTypeBuilder<RecalculationHandoffRecord> builder)
    {
        builder.ToTable("recalculation_handoffs", "ledger", table =>
        {
            table.HasCheckConstraint("ck_recalculation_handoffs_age", "age_days > 90");
        });

        builder.HasKey(x => new { x.InstitutionId, x.HandoffId });
        builder.HasIndex(x => new { x.InstitutionId, x.FinancialTransactionId }).IsUnique();
        builder.Property(x => x.InstitutionId).HasColumnName("institution_id").HasMaxLength(128);
        builder.Property(x => x.HandoffId).HasColumnName("handoff_id").HasMaxLength(128);
        builder.Property(x => x.LegalEntityCode).HasColumnName("legal_entity_code").HasMaxLength(64);
        builder.Property(x => x.PeriodCode).HasColumnName("period_code").HasMaxLength(7);
        builder.Property(x => x.FinancialTransactionId).HasColumnName("financial_transaction_id").HasMaxLength(128);
        builder.Property(x => x.ValueDate).HasColumnName("value_date");
        builder.Property(x => x.AgeDays).HasColumnName("age_days");
        builder.Property(x => x.ReasonCode).HasColumnName("reason_code").HasMaxLength(64);
        builder.Property(x => x.EvidenceReference).HasColumnName("evidence_reference").HasMaxLength(256);
        builder.Property(x => x.ApprovalReference).HasColumnName("approval_reference").HasMaxLength(256);
        builder.Property(x => x.RecordedAt).HasColumnName("recorded_at");
    }
}
