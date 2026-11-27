using System;
using CoreBanking.CoreLedger.Domain.Ledger;
using Xunit;

namespace CoreBanking.CoreLedger.UnitTests;

public sealed class FinancialReversalTests
{
    private static readonly DateTimeOffset EvaluatedAt = new(2026, 11, 26, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Full_reversal_requires_exact_opposite_lines()
    {
        JournalEntry original = Journal("original", DebitCreditIndicator.Debit, DebitCreditIndicator.Credit);
        JournalEntry reversal = Journal("reversal", DebitCreditIndicator.Credit, DebitCreditIndicator.Debit);

        FinancialReversalPolicy.ValidateFull(
            original,
            reversal,
            "customer_refund",
            "case-123",
            new string('a', 64),
            ReversalFinalityEvidence.LedgerPosted,
            null,
            EvaluatedAt);
    }

    [Fact]
    public void Component_or_amount_change_is_refused_without_mapping_policy()
    {
        JournalEntry original = Journal("original", DebitCreditIndicator.Debit, DebitCreditIndicator.Credit);
        JournalEntry changed = Journal("changed", DebitCreditIndicator.Credit, DebitCreditIndicator.Debit, 99m);

        LedgerPostingException exception = Assert.Throws<LedgerPostingException>(() =>
            FinancialReversalPolicy.ValidateFull(
                original,
                changed,
                "customer_refund",
                "case-123",
                new string('a', 64),
                ReversalFinalityEvidence.LedgerPosted,
                null,
                EvaluatedAt));

        Assert.Equal("financial_reversal_not_exact_opposite", exception.Code);
    }

    [Fact]
    public void Uncertain_provider_outcome_is_never_reversal_evidence()
    {
        JournalEntry original = Journal("original", DebitCreditIndicator.Debit, DebitCreditIndicator.Credit);
        JournalEntry reversal = Journal("reversal", DebitCreditIndicator.Credit, DebitCreditIndicator.Debit);

        LedgerPostingException exception = Assert.Throws<LedgerPostingException>(() =>
            FinancialReversalPolicy.ValidateFull(
                original,
                reversal,
                "provider_timeout",
                "provider-case-123",
                new string('a', 64),
                ReversalFinalityEvidence.ProviderUncertain,
                null,
                EvaluatedAt));

        Assert.Equal("financial_reversal_original_not_final", exception.Code);
    }

    [Fact]
    public void Maker_cannot_approve_their_own_reversal()
    {
        JournalEntry original = Journal("original", DebitCreditIndicator.Debit, DebitCreditIndicator.Credit);
        JournalEntry reversal = Journal("reversal", DebitCreditIndicator.Credit, DebitCreditIndicator.Debit);
        ReversalApprovalEvidence approval = new(
            "workflow-1",
            "actor-1",
            "actor-1",
            new string('a', 64),
            EvaluatedAt.AddHours(-1),
            null);

        LedgerPostingException exception = Assert.Throws<LedgerPostingException>(() =>
            FinancialReversalPolicy.ValidateFull(
                original,
                reversal,
                "manual_refund",
                "case-123",
                new string('a', 64),
                ReversalFinalityEvidence.LedgerPosted,
                approval,
                EvaluatedAt));

        Assert.Equal("financial_reversal_self_approval_denied", exception.Code);
    }

    [Fact]
    public void Stale_or_differently_bound_approval_is_refused()
    {
        JournalEntry original = Journal("original", DebitCreditIndicator.Debit, DebitCreditIndicator.Credit);
        JournalEntry reversal = Journal("reversal", DebitCreditIndicator.Credit, DebitCreditIndicator.Debit);
        ReversalApprovalEvidence approval = new(
            "workflow-1",
            "maker-1",
            "approver-1",
            new string('b', 64),
            EvaluatedAt.AddHours(-2),
            EvaluatedAt.AddMinutes(-1));

        LedgerPostingException mismatch = Assert.Throws<LedgerPostingException>(() =>
            FinancialReversalPolicy.ValidateFull(
                original,
                reversal,
                "manual_refund",
                "case-123",
                new string('a', 64),
                ReversalFinalityEvidence.LedgerPosted,
                approval,
                EvaluatedAt));

        Assert.Equal("financial_reversal_approval_mismatch", mismatch.Code);

        LedgerPostingException expired = Assert.Throws<LedgerPostingException>(() =>
            FinancialReversalPolicy.ValidateFull(
                original,
                reversal,
                "manual_refund",
                "case-123",
                new string('a', 64),
                ReversalFinalityEvidence.LedgerPosted,
                approval with { ApprovedRequestHash = new string('a', 64) },
                EvaluatedAt));

        Assert.Equal("financial_reversal_approval_expired", expired.Code);
    }

    private static JournalEntry Journal(
        string prefix,
        DebitCreditIndicator first,
        DebitCreditIndicator second,
        decimal amount = 100m)
    {
        AccountingScope scope = new("institution-1", "legal-entity-1", CurrencyCode.Parse("NGN"));
        FinancialDates dates = FinancialDates.Create(
            EvaluatedAt,
            new DateOnly(2026, 11, 26),
            new DateOnly(2026, 11, 26),
            new DateOnly(2026, 11, 26));
        return JournalEntry.Create(
            prefix + "-journal",
            prefix + "-transaction",
            prefix + "-reference",
            "mapping-v1",
            scope,
            dates,
            [
                JournalLine.Create(prefix + "-line-1", 1, "account-1", first,
                    AccountingComponentType.Principal, Money.Create(amount, "NGN"), scope),
                JournalLine.Create(prefix + "-line-2", 2, "account-2", second,
                    AccountingComponentType.Principal, Money.Create(amount, "NGN"), scope),
            ]);
    }
}
