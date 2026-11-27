using System;
using System.Collections.Generic;
using System.Linq;
using CoreBanking.CoreLedger.Domain.Ledger;
using Xunit;

namespace CoreBanking.CoreLedger.UnitTests;

/// <summary>Domain evidence for the approved P1-E05-D11 manual-journal authority and content rules.</summary>
public sealed class ManualJournalPolicyTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 26, 9, 0, 0, TimeSpan.Zero);

    internal static ManualJournalPolicy Policy() => ManualJournalPolicy.Create(
        "manual-journal-policy",
        1,
        "ledger.manual_journal.approve.head",
        ["customer_refund", "fee_correction"],
        [
            new ManualJournalClassGrant("customer_account_adjustment", [LedgerAccountRole.CustomerDeposit], true, 2),
            new ManualJournalClassGrant("cash", [LedgerAccountRole.CashSettlement], false, 1),
            new ManualJournalClassGrant("suspense", [LedgerAccountRole.Suspense], false, 1),
            new ManualJournalClassGrant(
                "income_expense",
                [LedgerAccountRole.FeeIncome, LedgerAccountRole.InterestExpense],
                false,
                1),
        ],
        [
            new ManualJournalTier(100_000m, 1, false),
            new ManualJournalTier(1_000_000m, 2, false),
            new ManualJournalTier(null, 2, true),
        ]);

    private static ManualJournalLine Line(
        int sequence,
        LedgerAccountRole role,
        string accountClass,
        DebitCreditIndicator direction,
        decimal amount) =>
        new(sequence, "account-" + sequence, "chart-" + sequence, role, accountClass, direction, amount, AccountingComponentType.Principal);

    [Fact]
    public void Customer_adjustment_needs_two_approvers_even_below_the_first_tier()
    {
        ManualJournalApprovalRequirement requirement = Policy()
            .RequirementFor(50m, ["customer_account_adjustment", "cash"]);

        Assert.Equal(2, requirement.RequiredApprovers);
        Assert.False(requirement.RequiresFinanceHead);
    }

    [Fact]
    public void Above_the_top_tier_requires_a_finance_head()
    {
        ManualJournalApprovalRequirement requirement = Policy().RequirementFor(1_000_001m, ["cash"]);

        Assert.Equal(2, requirement.RequiredApprovers);
        Assert.True(requirement.RequiresFinanceHead);
    }

    [Fact]
    public void A_class_may_not_claim_a_role_twice()
    {
        ArgumentException exception = Assert.Throws<ArgumentException>(() => ManualJournalPolicy.Create(
            "policy",
            1,
            "permission",
            ["reason"],
            [
                new ManualJournalClassGrant("customer_account_adjustment", [LedgerAccountRole.CustomerDeposit], true, 2),
                new ManualJournalClassGrant("cash", [LedgerAccountRole.CustomerDeposit], false, 1),
            ],
            [new ManualJournalTier(null, 1, false)]));

        Assert.Contains("only one account class", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Only_the_final_tier_may_be_unbounded()
    {
        ArgumentException exception = Assert.Throws<ArgumentException>(() => ManualJournalPolicy.Create(
            "policy",
            1,
            "permission",
            ["reason"],
            [new ManualJournalClassGrant("cash", [LedgerAccountRole.CashSettlement], false, 1)],
            [new ManualJournalTier(null, 1, false), new ManualJournalTier(100m, 1, false)]));

        Assert.Contains("final tier", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void An_unapproved_reason_code_is_refused()
    {
        LedgerPostingException exception = Assert.Throws<LedgerPostingException>(() => ManualJournalContent.Create(
            Policy(),
            "journal",
            "request",
            "institution",
            "legal-entity",
            "NGN",
            "not_a_reason",
            "case-1",
            "maker",
            [
                Line(1, LedgerAccountRole.CustomerDeposit, "customer_account_adjustment", DebitCreditIndicator.Debit, 50m),
                Line(2, LedgerAccountRole.CashSettlement, "cash", DebitCreditIndicator.Credit, 50m),
            ]));

        Assert.Equal("manual_journal_reason_not_approved", exception.Code);
    }

    [Fact]
    public void A_customer_adjustment_needs_a_compensating_class()
    {
        LedgerPostingException exception = Assert.Throws<LedgerPostingException>(() => ManualJournalContent.Create(
            Policy(),
            "journal",
            "request",
            "institution",
            "legal-entity",
            "NGN",
            "customer_refund",
            "case-1",
            "maker",
            [
                Line(1, LedgerAccountRole.CustomerDeposit, "customer_account_adjustment", DebitCreditIndicator.Debit, 50m),
                Line(2, LedgerAccountRole.CustomerDeposit, "customer_account_adjustment", DebitCreditIndicator.Credit, 50m),
            ]));

        Assert.Equal("manual_journal_compensating_account_required", exception.Code);
    }

    [Fact]
    public void The_content_hash_changes_when_the_evidence_changes()
    {
        ManualJournalPolicy policy = Policy();
        ManualJournalLine[] lines =
        [
            Line(1, LedgerAccountRole.CustomerDeposit, "customer_account_adjustment", DebitCreditIndicator.Debit, 50m),
            Line(2, LedgerAccountRole.CashSettlement, "cash", DebitCreditIndicator.Credit, 50m),
        ];

        ManualJournalContent first = ManualJournalContent.Create(
            policy, "journal", "request", "institution", "legal-entity", "NGN", "customer_refund", "case-1", "maker", lines);
        ManualJournalContent second = ManualJournalContent.Create(
            policy, "journal", "request", "institution", "legal-entity", "NGN", "customer_refund", "case-2", "maker", lines);

        Assert.NotEqual(first.RequestHash, second.RequestHash);
        Assert.Equal(64, first.RequestHash.Length);
        Assert.Equal(50m, first.TotalAmount);
    }

    [Fact]
    public void Quorum_excludes_the_maker_and_needs_the_finance_head()
    {
        ManualJournalApprovalRequirement requirement = new(2, true);
        ManualJournalApproval[] approvals =
        [
            new("d1", "maker", "ledger.manual_journal.approve", false, Now),
            new("d2", "approver-one", "ledger.manual_journal.approve", false, Now),
            new("d3", "approver-two", "ledger.manual_journal.approve.head", true, Now),
        ];

        Assert.True(ManualJournalPolicy.IsSatisfiedBy(requirement, approvals, "maker"));
        Assert.False(ManualJournalPolicy.IsSatisfiedBy(requirement, approvals.Take(2).ToArray(), "maker"));
        Assert.False(ManualJournalPolicy.IsSatisfiedBy(
            requirement,
            [
                new("d1", "approver-one", "ledger.manual_journal.approve", false, Now),
                new("d2", "approver-two", "ledger.manual_journal.approve", false, Now),
            ],
            "maker"));
    }

    [Fact]
    public void An_unbalanced_journal_is_refused()
    {
        LedgerPostingException exception = Assert.Throws<LedgerPostingException>(() => ManualJournalContent.Create(
            Policy(),
            "journal",
            "request",
            "institution",
            "legal-entity",
            "NGN",
            "customer_refund",
            "case-1",
            "maker",
            [
                Line(1, LedgerAccountRole.CustomerDeposit, "customer_account_adjustment", DebitCreditIndicator.Debit, 50m),
                Line(2, LedgerAccountRole.CashSettlement, "cash", DebitCreditIndicator.Credit, 40m),
            ]));

        Assert.Equal("journal_unbalanced", exception.Code);
    }
}
