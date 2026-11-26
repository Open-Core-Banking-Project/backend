using System;
using CoreBanking.CoreLedger.Domain.Ledger;
using Xunit;

namespace CoreBanking.CoreLedger.UnitTests;

/// <summary>Domain evidence for the approved P1-E05-D12 date policy and P1-E05-D13 period rules.</summary>
public sealed class AccountingPeriodTests
{
    private static readonly DateOnly BusinessDate = new(2026, 11, 8);

    [Theory]
    [InlineData(AccountingPeriodStatus.Open, true)]
    [InlineData(AccountingPeriodStatus.SoftClosed, false)]
    [InlineData(AccountingPeriodStatus.Closed, false)]
    [InlineData(AccountingPeriodStatus.Locked, false)]
    [InlineData(AccountingPeriodStatus.Reopened, false)]
    public void An_empty_allow_list_only_posts_in_an_open_period(AccountingPeriodStatus status, bool expected) =>
        Assert.Equal(expected, AccountingPeriodRules.IsPostable(status, [], "internal_transfer.v1"));

    [Fact]
    public void Soft_closed_and_reopened_periods_only_post_listed_events()
    {
        Assert.True(AccountingPeriodRules.IsPostable(
            AccountingPeriodStatus.SoftClosed, ["internal_transfer.v1"], "internal_transfer.v1"));
        Assert.False(AccountingPeriodRules.IsPostable(
            AccountingPeriodStatus.SoftClosed, ["internal_transfer.v1"], "manual_journal.v1"));
        Assert.True(AccountingPeriodRules.IsPostable(
            AccountingPeriodStatus.Reopened, ["manual_journal.v1"], "manual_journal.v1"));
    }

    [Theory]
    [InlineData(AccountingPeriodStatus.Open, AccountingPeriodStatus.SoftClosed)]
    [InlineData(AccountingPeriodStatus.Open, AccountingPeriodStatus.Closed)]
    [InlineData(AccountingPeriodStatus.SoftClosed, AccountingPeriodStatus.Open)]
    [InlineData(AccountingPeriodStatus.SoftClosed, AccountingPeriodStatus.Closed)]
    [InlineData(AccountingPeriodStatus.Closed, AccountingPeriodStatus.Reopened)]
    [InlineData(AccountingPeriodStatus.Closed, AccountingPeriodStatus.Locked)]
    [InlineData(AccountingPeriodStatus.Reopened, AccountingPeriodStatus.Closed)]
    public void The_approved_transitions_are_allowed(AccountingPeriodStatus from, AccountingPeriodStatus to) =>
        AccountingPeriodRules.EnsureTransition(from, to);

    [Theory]
    [InlineData(AccountingPeriodStatus.Open, AccountingPeriodStatus.Locked)]
    [InlineData(AccountingPeriodStatus.Locked, AccountingPeriodStatus.Open)]
    [InlineData(AccountingPeriodStatus.Locked, AccountingPeriodStatus.Reopened)]
    [InlineData(AccountingPeriodStatus.Reopened, AccountingPeriodStatus.Open)]
    [InlineData(AccountingPeriodStatus.SoftClosed, AccountingPeriodStatus.Reopened)]
    public void Other_transitions_are_refused(AccountingPeriodStatus from, AccountingPeriodStatus to)
    {
        LedgerPostingException exception = Assert.Throws<LedgerPostingException>(
            () => AccountingPeriodRules.EnsureTransition(from, to));
        Assert.Equal("accounting_period_transition_invalid", exception.Code);
    }

    [Fact]
    public void Close_requires_balanced_reconciled_processing_evidence()
    {
        AccountingPeriodRules.EnsureCloseEvidence(Healthy());

        Assert.Equal(
            "accounting_period_unbalanced",
            Assert.Throws<LedgerPostingException>(() =>
                AccountingPeriodRules.EnsureCloseEvidence(Healthy() with { CreditTotal = 99m })).Code);
        Assert.Equal(
            "accounting_period_reconciliation_blocked",
            Assert.Throws<LedgerPostingException>(() =>
                AccountingPeriodRules.EnsureCloseEvidence(Healthy() with { BlockingReconciliationExceptions = 1 })).Code);
        Assert.Equal(
            "accounting_period_processing_incomplete",
            Assert.Throws<LedgerPostingException>(() =>
                AccountingPeriodRules.EnsureCloseEvidence(Healthy() with { RequiredProcessingStepsCompleted = false })).Code);
        Assert.Equal(
            "accounting_period_processing_incomplete",
            Assert.Throws<LedgerPostingException>(() =>
                AccountingPeriodRules.EnsureCloseEvidence(Healthy() with { OutboxHealthy = false })).Code);
    }

    [Fact]
    public void Reopen_needs_two_distinct_approvers_and_never_the_maker()
    {
        AccountingPeriodApproval[] approvals =
        [
            new("finance-one", "approval-one", DateTimeOffset.UnixEpoch),
            new("finance-two", "approval-two", DateTimeOffset.UnixEpoch),
        ];

        AccountingPeriodRules.EnsureDistinctApprovers("maker", approvals, 2);
        Assert.Equal(
            "accounting_period_approval_insufficient",
            Assert.Throws<LedgerPostingException>(() =>
                AccountingPeriodRules.EnsureDistinctApprovers("maker", approvals[..1], 2)).Code);
        Assert.Equal(
            "accounting_period_self_approval_denied",
            Assert.Throws<LedgerPostingException>(() =>
                AccountingPeriodRules.EnsureDistinctApprovers("finance-one", approvals, 2)).Code);
        Assert.Equal(
            "accounting_period_approval_insufficient",
            Assert.Throws<LedgerPostingException>(() =>
                AccountingPeriodRules.EnsureDistinctApprovers(
                    "maker",
                    [approvals[0], new AccountingPeriodApproval("finance-one", "approval-three", DateTimeOffset.UnixEpoch)],
                    2)).Code);
    }

    [Fact]
    public void A_reopen_is_time_bounded_to_five_business_days()
    {
        DateTimeOffset now = new(2026, 11, 8, 9, 0, 0, TimeSpan.Zero);
        AccountingPeriodRules.EnsureReopenWindow(now, now.AddDays(5));
        Assert.Equal(
            "accounting_period_reopen_expiry_too_long",
            Assert.Throws<LedgerPostingException>(() =>
                AccountingPeriodRules.EnsureReopenWindow(now, now.AddDays(6))).Code);
        Assert.Equal(
            "accounting_period_reopen_expiry_invalid",
            Assert.Throws<LedgerPostingException>(() =>
                AccountingPeriodRules.EnsureReopenWindow(now, now)).Code);
    }

    [Fact]
    public void A_value_date_inside_ninety_days_is_ordinary()
    {
        PostingDateDecision decision = FinancialDatePolicy.Evaluate(
            BusinessDate, BusinessDate, BusinessDate.AddDays(-90), authority: null);

        Assert.True(decision.Allowed);
        Assert.Equal(ValueDateClassification.Ordinary, decision.Classification);
        Assert.False(decision.RequiresRecalculationHandoff);
    }

    [Fact]
    public void A_value_date_between_ninety_one_and_a_year_needs_a_bound_authority()
    {
        DateOnly historical = BusinessDate.AddDays(-120);

        Assert.Equal(
            "backdate_authority_required",
            FinancialDatePolicy.Evaluate(BusinessDate, BusinessDate, historical, authority: null).RefusalCode);

        PostingDateDecision allowed = FinancialDatePolicy.Evaluate(
            BusinessDate,
            BusinessDate,
            historical,
            new DateAuthority("approval-1", "customer_refund", "case-1", 180, StepUpVerified: true));
        Assert.True(allowed.Allowed);
        Assert.Equal(ValueDateClassification.HistoricalPriorPeriod, allowed.Classification);
        Assert.True(allowed.RequiresRecalculationHandoff);

        Assert.Equal(
            "backdate_authority_required",
            FinancialDatePolicy.Evaluate(
                BusinessDate,
                BusinessDate,
                historical,
                new DateAuthority("approval-1", "customer_refund", "case-1", 180, StepUpVerified: false)).RefusalCode);
        Assert.Equal(
            "backdate_authority_required",
            FinancialDatePolicy.Evaluate(
                BusinessDate,
                BusinessDate,
                historical,
                new DateAuthority("approval-1", "customer_refund", "case-1", 100, StepUpVerified: true)).RefusalCode);
    }

    [Fact]
    public void Older_than_a_year_future_and_mismatched_dates_are_refused()
    {
        Assert.Equal(
            "value_date_too_old",
            FinancialDatePolicy.Evaluate(BusinessDate, BusinessDate, BusinessDate.AddDays(-366), authority: null).RefusalCode);
        Assert.Equal(
            "value_date_future_denied",
            FinancialDatePolicy.Evaluate(BusinessDate, BusinessDate, BusinessDate.AddDays(1), authority: null).RefusalCode);
        Assert.Equal(
            "posting_date_business_date_mismatch",
            FinancialDatePolicy.Evaluate(
                BusinessDate, BusinessDate.AddDays(-1), BusinessDate, authority: null).RefusalCode);
    }

    private static AccountingPeriodCloseEvidence Healthy() =>
        new(
            DebitTotal: 100m,
            CreditTotal: 100m,
            BlockingReconciliationExceptions: 0,
            RequiredProcessingStepsCompleted: true,
            OutboxHealthy: true,
            ApprovalReference: "close-approval",
            SummaryHash: new string('a', 64));
}
