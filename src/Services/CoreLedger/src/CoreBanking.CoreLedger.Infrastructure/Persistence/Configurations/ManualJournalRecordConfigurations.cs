using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CoreBanking.CoreLedger.Infrastructure.Persistence.Configurations;

internal sealed class ManualJournalRequestRecordConfiguration : IEntityTypeConfiguration<ManualJournalRequestRecord>
{
    public void Configure(EntityTypeBuilder<ManualJournalRequestRecord> builder)
    {
        builder.ToTable("manual_journal_requests", "ledger", table =>
        {
            table.HasCheckConstraint(
                "ck_manual_journal_requests_status",
                "status IN ('draft', 'submitted', 'approved', 'rejected', 'posted')");
            table.HasCheckConstraint("ck_manual_journal_requests_hash", "request_hash ~ '^[0-9a-f]{64}$'");
            table.HasCheckConstraint("ck_manual_journal_requests_amount", "total_amount > 0");
            table.HasCheckConstraint(
                "ck_manual_journal_requests_approvers",
                "required_approvers BETWEEN 1 AND 3");
            table.HasCheckConstraint(
                "ck_manual_journal_requests_posted",
                "(status = 'posted' AND posted_at IS NOT NULL AND financial_transaction_id IS NOT NULL)"
                + " OR (status <> 'posted' AND posted_at IS NULL AND financial_transaction_id IS NULL)");
            table.HasCheckConstraint(
                "ck_manual_journal_requests_break_glass",
                "(break_glass = false AND break_glass_actor_id IS NULL AND dual_control_actor_id IS NULL"
                + " AND security_evidence_reference IS NULL AND break_glass_valid_until IS NULL)"
                + " OR (break_glass = true AND break_glass_actor_id IS NOT NULL AND dual_control_actor_id IS NOT NULL"
                + " AND security_evidence_reference IS NOT NULL AND break_glass_valid_until IS NOT NULL"
                + " AND break_glass_actor_id <> dual_control_actor_id)");
            table.HasCheckConstraint("ck_manual_journal_requests_version", "version > 0");
        });

        builder.HasKey(x => new { x.InstitutionId, x.ManualJournalId });
        builder.HasIndex(x => new { x.InstitutionId, x.RequestId }).IsUnique();
        builder.Property(x => x.InstitutionId).HasColumnName("institution_id").HasMaxLength(128);
        builder.Property(x => x.ManualJournalId).HasColumnName("manual_journal_id").HasMaxLength(128);
        builder.Property(x => x.RequestId).HasColumnName("request_id").HasMaxLength(128);
        builder.Property(x => x.Status).HasColumnName("status").HasMaxLength(32);
        builder.Property(x => x.LegalEntityCode).HasColumnName("legal_entity_code").HasMaxLength(64);
        builder.Property(x => x.CurrencyCode).HasColumnName("currency_code").HasMaxLength(3);
        builder.Property(x => x.TotalAmount).HasColumnName("total_amount").HasPrecision(19, 4);
        builder.Property(x => x.ReasonCode).HasColumnName("reason_code").HasMaxLength(64);
        builder.Property(x => x.EvidenceReference).HasColumnName("evidence_reference").HasMaxLength(256);
        builder.Property(x => x.RequestHash).HasColumnName("request_hash").HasMaxLength(64);
        builder.Property(x => x.PolicyVersionId).HasColumnName("policy_version_id").HasMaxLength(128);
        builder.Property(x => x.PolicyContentHash).HasColumnName("policy_content_hash").HasMaxLength(64);
        builder.Property(x => x.RequiredApprovers).HasColumnName("required_approvers");
        builder.Property(x => x.RequiresFinanceHead).HasColumnName("requires_finance_head");
        builder.Property(x => x.FinanceHeadPermission).HasColumnName("finance_head_permission").HasMaxLength(128);
        builder.Property(x => x.MakerActorId).HasColumnName("maker_actor_id").HasMaxLength(128);
        builder.Property(x => x.ApprovedByActorId).HasColumnName("approved_by_actor_id").HasMaxLength(128);
        builder.Property(x => x.WorkflowRequestId).HasColumnName("workflow_request_id").HasMaxLength(128);
        builder.Property(x => x.FinancialTransactionId).HasColumnName("financial_transaction_id").HasMaxLength(128);
        builder.Property(x => x.RefusalCode).HasColumnName("refusal_code").HasMaxLength(64);
        builder.Property(x => x.BreakGlass).HasColumnName("break_glass");
        builder.Property(x => x.BreakGlassActorId).HasColumnName("break_glass_actor_id").HasMaxLength(128);
        builder.Property(x => x.DualControlActorId).HasColumnName("dual_control_actor_id").HasMaxLength(128);
        builder.Property(x => x.SecurityEvidenceReference).HasColumnName("security_evidence_reference").HasMaxLength(256);
        builder.Property(x => x.BreakGlassValidUntil).HasColumnName("break_glass_valid_until");
        builder.Property(x => x.CreatedAt).HasColumnName("created_at");
        builder.Property(x => x.SubmittedAt).HasColumnName("submitted_at");
        builder.Property(x => x.DecidedAt).HasColumnName("decided_at");
        builder.Property(x => x.PostedAt).HasColumnName("posted_at");
        builder.Property(x => x.Version).HasColumnName("version");
    }
}

internal sealed class ManualJournalLineRecordConfiguration : IEntityTypeConfiguration<ManualJournalLineRecord>
{
    public void Configure(EntityTypeBuilder<ManualJournalLineRecord> builder)
    {
        builder.ToTable("manual_journal_lines", "ledger", table =>
        {
            table.HasCheckConstraint("ck_manual_journal_lines_sequence", "sequence > 0");
            table.HasCheckConstraint("ck_manual_journal_lines_amount", "amount > 0");
            table.HasCheckConstraint("ck_manual_journal_lines_direction", "direction IN ('debit', 'credit')");
            table.HasCheckConstraint(
                "ck_manual_journal_lines_component",
                "component IN ('principal', 'fee', 'tax', 'commission', 'penalty', 'interest', 'waiver', 'rounding')");
        });

        builder.HasKey(x => new { x.InstitutionId, x.ManualJournalId, x.Sequence });
        builder.Property(x => x.InstitutionId).HasColumnName("institution_id").HasMaxLength(128);
        builder.Property(x => x.ManualJournalId).HasColumnName("manual_journal_id").HasMaxLength(128);
        builder.Property(x => x.Sequence).HasColumnName("sequence");
        builder.Property(x => x.LedgerAccountId).HasColumnName("ledger_account_id").HasMaxLength(128);
        builder.Property(x => x.ChartAccountCode).HasColumnName("chart_account_code").HasMaxLength(64);
        builder.Property(x => x.AccountRole).HasColumnName("account_role").HasMaxLength(64);
        builder.Property(x => x.AccountClass).HasColumnName("account_class").HasMaxLength(64);
        builder.Property(x => x.Direction).HasColumnName("direction").HasMaxLength(8);
        builder.Property(x => x.Amount).HasColumnName("amount").HasPrecision(19, 4);
        builder.Property(x => x.Component).HasColumnName("component").HasMaxLength(32);
        builder.Property(x => x.RequiresCompensatingAccount).HasColumnName("requires_compensating_account");
    }
}

internal sealed class ManualJournalApprovalRecordConfiguration : IEntityTypeConfiguration<ManualJournalApprovalRecord>
{
    public void Configure(EntityTypeBuilder<ManualJournalApprovalRecord> builder)
    {
        builder.ToTable("manual_journal_approvals", "ledger");
        builder.HasKey(x => new { x.InstitutionId, x.ManualJournalId, x.DecisionId });
        builder.HasIndex(x => new { x.InstitutionId, x.ManualJournalId, x.ApproverActorId }).IsUnique();
        builder.Property(x => x.InstitutionId).HasColumnName("institution_id").HasMaxLength(128);
        builder.Property(x => x.ManualJournalId).HasColumnName("manual_journal_id").HasMaxLength(128);
        builder.Property(x => x.DecisionId).HasColumnName("decision_id").HasMaxLength(128);
        builder.Property(x => x.ApproverActorId).HasColumnName("approver_actor_id").HasMaxLength(128);
        builder.Property(x => x.Permission).HasColumnName("permission").HasMaxLength(128);
        builder.Property(x => x.IsFinanceHead).HasColumnName("is_finance_head");
        builder.Property(x => x.EntitlementVersion).HasColumnName("entitlement_version").HasMaxLength(64);
        builder.Property(x => x.DecidedAt).HasColumnName("decided_at");
    }
}
