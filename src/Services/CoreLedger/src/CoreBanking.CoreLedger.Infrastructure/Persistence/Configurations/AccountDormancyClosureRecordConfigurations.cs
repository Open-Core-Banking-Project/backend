using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CoreBanking.CoreLedger.Infrastructure.Persistence.Configurations;

internal sealed class AccountDormancyAssessmentRecordConfiguration
    : IEntityTypeConfiguration<AccountDormancyAssessmentRecord>
{
    public void Configure(EntityTypeBuilder<AccountDormancyAssessmentRecord> builder)
    {
        builder.ToTable("account_dormancy_assessments", "account", table =>
        {
            table.HasCheckConstraint(
                "ck_account_dormancy_assessments_decision",
                "decision IN ('NotDormant', 'NoticeDue', 'Dormant', 'NotAssessable')");
            table.HasCheckConstraint("ck_account_dormancy_assessments_threshold", "threshold_days >= 0");
            table.HasCheckConstraint(
                "ck_account_dormancy_assessments_lead",
                "notice_lead_days >= 0 AND notice_lead_days <= threshold_days");
        });
        builder.HasKey(x => new { x.InstitutionId, x.AssessmentId });
        // One assessment per account, business date and rule version: a repeated run returns the recorded one.
        builder.HasIndex(x => new { x.InstitutionId, x.AccountId, x.BusinessDate, x.RuleVersionId }).IsUnique();
        builder.HasOne<AccountRecord>().WithMany()
            .HasForeignKey(x => new { x.InstitutionId, x.AccountId })
            .OnDelete(DeleteBehavior.Restrict);
        builder.Property(x => x.InstitutionId).HasColumnName("institution_id").HasMaxLength(128);
        builder.Property(x => x.AssessmentId).HasColumnName("assessment_id").HasMaxLength(128);
        builder.Property(x => x.AccountId).HasColumnName("account_id").HasMaxLength(128);
        builder.Property(x => x.BusinessDate).HasColumnName("business_date");
        builder.Property(x => x.RuleVersionId).HasColumnName("rule_version_id").HasMaxLength(128);
        builder.Property(x => x.RuleContentHash).HasColumnName("rule_content_hash").HasMaxLength(128);
        builder.Property(x => x.ThresholdDays).HasColumnName("threshold_days");
        builder.Property(x => x.NoticeLeadDays).HasColumnName("notice_lead_days");
        builder.Property(x => x.ClockOriginDate).HasColumnName("clock_origin_date");
        builder.Property(x => x.LastQualifyingActivityDate).HasColumnName("last_qualifying_activity_date");
        builder.Property(x => x.InactivityDays).HasColumnName("inactivity_days");
        builder.Property(x => x.Decision).HasColumnName("decision").HasMaxLength(32);
        builder.Property(x => x.ReasonCodes).HasColumnName("reason_codes").HasMaxLength(256);
        builder.Property(x => x.TransitionPermitted).HasColumnName("transition_permitted");
        builder.Property(x => x.PreviousStatus).HasColumnName("previous_status").HasMaxLength(32);
        builder.Property(x => x.ResultingStatus).HasColumnName("resulting_status").HasMaxLength(32);
        builder.Property(x => x.AssessedAt).HasColumnName("assessed_at");
    }
}

internal sealed class AccountDormancyNoticeRecordConfiguration : IEntityTypeConfiguration<AccountDormancyNoticeRecord>
{
    public void Configure(EntityTypeBuilder<AccountDormancyNoticeRecord> builder)
    {
        builder.ToTable("account_dormancy_notices", "account", table =>
        {
            table.HasCheckConstraint("ck_account_dormancy_notices_status", "status IN ('recorded', 'failed')");
        });
        builder.HasKey(x => new { x.InstitutionId, x.NoticeId });
        builder.HasIndex(x => new { x.InstitutionId, x.AccountId, x.RuleVersionId });
        builder.HasOne<AccountRecord>().WithMany()
            .HasForeignKey(x => new { x.InstitutionId, x.AccountId })
            .OnDelete(DeleteBehavior.Restrict);
        builder.Property(x => x.InstitutionId).HasColumnName("institution_id").HasMaxLength(128);
        builder.Property(x => x.NoticeId).HasColumnName("notice_id").HasMaxLength(128);
        builder.Property(x => x.AccountId).HasColumnName("account_id").HasMaxLength(128);
        builder.Property(x => x.RuleVersionId).HasColumnName("rule_version_id").HasMaxLength(128);
        builder.Property(x => x.BusinessDate).HasColumnName("business_date");
        builder.Property(x => x.ChannelCode).HasColumnName("channel_code").HasMaxLength(64);
        builder.Property(x => x.Status).HasColumnName("status").HasMaxLength(32);
        builder.Property(x => x.RecordedAt).HasColumnName("recorded_at");
    }
}

internal sealed class AccountDormancyLeaseRecordConfiguration : IEntityTypeConfiguration<AccountDormancyLeaseRecord>
{
    public void Configure(EntityTypeBuilder<AccountDormancyLeaseRecord> builder)
    {
        builder.ToTable("account_dormancy_leases", "account");
        builder.HasKey(x => new { x.InstitutionId, x.LeaseId });
        // One unreleased lease per account; this is what stops two workers assessing the same account.
        builder.HasIndex(x => new { x.InstitutionId, x.AccountId })
            .IsUnique()
            .HasFilter("released_at IS NULL");
        builder.HasOne<AccountRecord>().WithMany()
            .HasForeignKey(x => new { x.InstitutionId, x.AccountId })
            .OnDelete(DeleteBehavior.Restrict);
        builder.Property(x => x.InstitutionId).HasColumnName("institution_id").HasMaxLength(128);
        builder.Property(x => x.LeaseId).HasColumnName("lease_id").HasMaxLength(128);
        builder.Property(x => x.AccountId).HasColumnName("account_id").HasMaxLength(128);
        builder.Property(x => x.AcquiredAt).HasColumnName("acquired_at");
        builder.Property(x => x.ExpiresAt).HasColumnName("expires_at");
        builder.Property(x => x.ReleasedAt).HasColumnName("released_at");
    }
}

internal sealed class AccountClosureProposalRecordConfiguration : IEntityTypeConfiguration<AccountClosureProposalRecord>
{
    public void Configure(EntityTypeBuilder<AccountClosureProposalRecord> builder)
    {
        builder.ToTable("account_closure_proposals", "account", table =>
        {
            table.HasCheckConstraint(
                "ck_account_closure_proposals_status",
                "status IN ('conditions_met', 'conditions_failed', 'pending_approval', 'pending_closure', 'closed', 'rejected')");
            table.HasCheckConstraint("ck_account_closure_proposals_version", "version >= 1");
        });
        builder.HasKey(x => new { x.InstitutionId, x.ProposalId });
        builder.HasIndex(x => new { x.InstitutionId, x.AccountId, x.Status });
        builder.HasIndex(x => new { x.InstitutionId, x.RequestId }).IsUnique();
        builder.HasOne<AccountRecord>().WithMany()
            .HasForeignKey(x => new { x.InstitutionId, x.AccountId })
            .OnDelete(DeleteBehavior.Restrict);
        builder.Property(x => x.InstitutionId).HasColumnName("institution_id").HasMaxLength(128);
        builder.Property(x => x.ProposalId).HasColumnName("proposal_id").HasMaxLength(128);
        builder.Property(x => x.AccountId).HasColumnName("account_id").HasMaxLength(128);
        builder.Property(x => x.RequestId).HasColumnName("request_id").HasMaxLength(128);
        builder.Property(x => x.ReasonCode).HasColumnName("reason_code").HasMaxLength(64);
        builder.Property(x => x.SupportingReference).HasColumnName("supporting_reference").HasMaxLength(128);
        builder.Property(x => x.Status).HasColumnName("status").HasMaxLength(32);
        builder.Property(x => x.ConditionSnapshotHash).HasColumnName("condition_snapshot_hash").HasMaxLength(128);
        builder.Property(x => x.LedgerAccountReference).HasColumnName("ledger_account_reference").HasMaxLength(128);
        builder.Property(x => x.LedgerSnapshotVersion).HasColumnName("ledger_snapshot_version").HasMaxLength(128);
        builder.Property(x => x.ConditionsAsOf).HasColumnName("conditions_as_of");
        builder.Property(x => x.CurrencyCode).HasColumnName("currency_code").HasMaxLength(3);
        builder.Property(x => x.LedgerBalance).HasColumnName("ledger_balance").HasPrecision(19, 4);
        builder.Property(x => x.AvailableBalance).HasColumnName("available_balance").HasPrecision(19, 4);
        builder.Property(x => x.Holds).HasColumnName("holds").HasPrecision(19, 4);
        builder.Property(x => x.OutstandingLienAmount).HasColumnName("outstanding_lien_amount").HasPrecision(19, 4);
        builder.Property(x => x.PendingSettlements).HasColumnName("pending_settlements");
        builder.Property(x => x.LienReleaseDecisionRecorded).HasColumnName("lien_release_decision_recorded");
        builder.Property(x => x.DisputeOpen).HasColumnName("dispute_open");
        builder.Property(x => x.NoticePending).HasColumnName("notice_pending");
        builder.Property(x => x.RefusalCodes).HasColumnName("refusal_codes").HasMaxLength(256);
        builder.Property(x => x.ApprovalRequestId).HasColumnName("approval_request_id").HasMaxLength(128);
        builder.Property(x => x.ApprovedByActorId).HasColumnName("approved_by_actor_id").HasMaxLength(128);
        builder.Property(x => x.ApprovedAt).HasColumnName("approved_at");
        builder.Property(x => x.ClosedAt).HasColumnName("closed_at");
        builder.Property(x => x.CreatedByActorId).HasColumnName("created_by_actor_id").HasMaxLength(128);
        builder.Property(x => x.Version).HasColumnName("version");
        builder.Property(x => x.CreatedAt).HasColumnName("created_at");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at");
    }
}
