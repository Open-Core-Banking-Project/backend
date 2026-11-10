using System;
using System.Collections.Generic;
using System.Linq;
using CoreBanking.CoreLedger.Application.Accounts;
using CoreBanking.CoreLedger.Application.Products;
using CoreBanking.CoreLedger.Contracts;
using CoreBanking.CoreLedger.Domain.Accounts;
using Xunit;

namespace CoreBanking.CoreLedger.UnitTests;

/// <summary>
/// The approved closure conditions, ordering and post-close rules (P1-E04-D13). The account module records the
/// ledger's answer, never forces a balance to zero and never reopens a closed account.
/// </summary>
public sealed class AccountClosureTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 7, 10, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Freshness = TimeSpan.FromSeconds(60);

    private static LedgerClosureConditions Conditions(
        decimal ledgerBalance = 0m,
        decimal holds = 0m,
        decimal liens = 0m,
        int pendingSettlements = 0,
        DateTimeOffset? asOf = null) =>
        new(
            "ledger-account-1",
            "ledger:ledger-account-1:closure:v1",
            asOf ?? Now,
            "NGN",
            ledgerBalance,
            ledgerBalance,
            holds,
            liens,
            pendingSettlements);

    private static AccountClosureFacts Facts(
        bool legalControlActive = false,
        decimal statutoryOrTaxLien = 0m,
        decimal institutionOrCustomerLien = 0m,
        bool disputeOpen = false,
        bool noticePending = false,
        bool lienReleaseDecisionRecorded = false) =>
        new(legalControlActive, statutoryOrTaxLien, institutionOrCustomerLien, disputeOpen, noticePending, lienReleaseDecisionRecorded);

    [Fact]
    public void A_settled_zero_balance_with_no_holds_or_liens_is_closable()
    {
        AccountClosureConditionResult result = AccountClosurePolicy.Evaluate(Conditions(), Facts(), Now, Freshness);

        Assert.True(result.Settled);
        Assert.Empty(result.RefusalCodes);
        Assert.Equal(64, result.SnapshotHash.Length);
    }

    [Fact]
    public void A_non_zero_balance_refuses_closure_and_is_never_forced_to_zero()
    {
        AccountClosureConditionResult result = AccountClosurePolicy.Evaluate(
            Conditions(ledgerBalance: 250m), Facts(), Now, Freshness);

        Assert.False(result.Settled);
        Assert.Equal([AccountClosurePolicy.BalanceNotSettledCode], result.RefusalCodes);
    }

    [Fact]
    public void Holds_and_pending_settlements_refuse_closure()
    {
        Assert.Equal(
            [AccountClosurePolicy.FundsNotSettledCode],
            AccountClosurePolicy.Evaluate(Conditions(holds: 10m), Facts(), Now, Freshness).RefusalCodes);
        Assert.Equal(
            [AccountClosurePolicy.FundsNotSettledCode],
            AccountClosurePolicy.Evaluate(Conditions(pendingSettlements: 1), Facts(), Now, Freshness).RefusalCodes);
    }

    [Fact]
    public void A_statutory_or_tax_lien_refuses_closure_even_with_a_release_decision()
    {
        Assert.Equal(
            [AccountClosurePolicy.LienOutstandingCode],
            AccountClosurePolicy.Evaluate(Conditions(), Facts(statutoryOrTaxLien: 500m), Now, Freshness).RefusalCodes);
        Assert.Equal(
            [AccountClosurePolicy.LienOutstandingCode],
            AccountClosurePolicy.Evaluate(Conditions(liens: 500m), Facts(), Now, Freshness).RefusalCodes);
    }

    [Fact]
    public void An_institution_or_customer_lien_refuses_closure_unless_a_release_decision_is_recorded()
    {
        Assert.Equal(
            [AccountClosurePolicy.LienOutstandingCode],
            AccountClosurePolicy.Evaluate(Conditions(), Facts(institutionOrCustomerLien: 200m), Now, Freshness).RefusalCodes);
        Assert.True(AccountClosurePolicy.Evaluate(
            Conditions(), Facts(institutionOrCustomerLien: 200m, lienReleaseDecisionRecorded: true), Now, Freshness).Settled);
    }

    [Fact]
    public void An_active_legal_or_regulatory_control_refuses_closure()
    {
        Assert.Equal(
            [AccountClosurePolicy.LegalControlActiveCode],
            AccountClosurePolicy.Evaluate(Conditions(), Facts(legalControlActive: true), Now, Freshness).RefusalCodes);
    }

    [Fact]
    public void An_open_dispute_or_pending_notice_refuses_closure()
    {
        Assert.Equal(
            [AccountClosurePolicy.DisputeOpenCode],
            AccountClosurePolicy.Evaluate(Conditions(), Facts(disputeOpen: true), Now, Freshness).RefusalCodes);
        Assert.Equal(
            [AccountClosurePolicy.NoticePendingCode],
            AccountClosurePolicy.Evaluate(Conditions(), Facts(noticePending: true), Now, Freshness).RefusalCodes);
    }

    [Fact]
    public void A_stale_ledger_answer_refuses_closure_and_the_snapshot_hash_is_stable()
    {
        LedgerClosureConditions stale = Conditions(asOf: Now.AddMinutes(-5));

        AccountClosureConditionResult result = AccountClosurePolicy.Evaluate(stale, Facts(), Now, Freshness);

        Assert.Equal([AccountClosurePolicy.BalanceSourceStaleCode], result.RefusalCodes);
        Assert.Equal(result.SnapshotHash, AccountClosurePolicy.SnapshotHash(Conditions(asOf: Now.AddMinutes(-5))));
        Assert.NotEqual(result.SnapshotHash, AccountClosurePolicy.SnapshotHash(Conditions()));
    }

    [Fact]
    public void Refusal_codes_are_ordered_and_deduplicated_so_the_evidence_is_stable()
    {
        AccountClosureConditionResult result = AccountClosurePolicy.Evaluate(
            Conditions(ledgerBalance: 1m, holds: 1m, liens: 1m),
            Facts(legalControlActive: true, disputeOpen: true),
            Now,
            Freshness);

        Assert.Equal(
            [
                AccountClosurePolicy.BalanceNotSettledCode,
                AccountClosurePolicy.DisputeOpenCode,
                AccountClosurePolicy.FundsNotSettledCode,
                AccountClosurePolicy.LegalControlActiveCode,
                AccountClosurePolicy.LienOutstandingCode,
            ],
            result.RefusalCodes);
        Assert.Equal(result.RefusalCodes, [.. result.RefusalCodes.OrderBy(code => code, StringComparer.Ordinal)]);
    }

    [Fact]
    public void A_closed_account_refuses_an_ordinary_posting_and_admits_a_classified_one()
    {
        Assert.False(AccountPostClosePolicy.IsPermitted(AccountStatus.Closed, "customer_transfer"));
        Assert.False(AccountPostClosePolicy.IsPermitted(AccountStatus.Closed, string.Empty));
        Assert.False(AccountPostClosePolicy.IsPermitted(AccountStatus.Closed, "correction"));
        Assert.True(AccountPostClosePolicy.IsPermitted(AccountStatus.Closed, "correction_bank_error"));
        Assert.True(AccountPostClosePolicy.IsPermitted(AccountStatus.Closed, "late_settlement"));
        Assert.True(AccountPostClosePolicy.IsPermitted(AccountStatus.Closed, "chargeback_settlement"));
        Assert.True(AccountPostClosePolicy.IsPermitted(AccountStatus.Closed, "dispute_settlement"));
        Assert.True(AccountPostClosePolicy.IsPermitted(AccountStatus.Active, "customer_transfer"));
        Assert.Equal(5, AccountPostClosePolicy.PermittedOperations.Count);
    }

    [Fact]
    public void Closure_always_passes_through_pending_closure_and_a_closed_account_is_terminal()
    {
        Assert.Equal(AccountStatus.PendingClosure, AccountLifecycle.ProposeClosure(AccountStatus.Active));
        Assert.Equal(AccountStatus.PendingClosure, AccountLifecycle.ProposeClosure(AccountStatus.Dormant));
        Assert.Equal(AccountStatus.Closed, AccountLifecycle.Close(AccountStatus.PendingClosure));
        Assert.Throws<InvalidOperationException>(() => AccountLifecycle.Close(AccountStatus.Active));
        Assert.Throws<InvalidOperationException>(() => AccountLifecycle.ProposeClosure(AccountStatus.PendingClosure));
    }

    [Fact]
    public void The_closure_profile_is_pinned_identically_in_the_source_service_contract()
    {
        Assert.Equal("account.close.v1", AccountClosureApprovalV1.WorkflowKey);
        Assert.Equal("account-close-approval-v1", AccountClosureApprovalV1.WorkflowVersionId);
        Assert.Equal("account.closure.proposal", AccountClosureApprovalV1.SubjectType);
        Assert.Equal("core-ledger", AccountClosureApprovalV1.SourceService);
        Assert.Equal("account.closure.approve", AccountClosureApprovalV1.RequiredPermission);
        Assert.Equal(
            "77d2bfc028a198a906f8a848eb9d84b5a51df1678133a252f4cf5ecc7d3db437",
            AccountClosureApprovalV1.WorkflowDefinitionHash);
    }

    [Fact]
    public void The_closure_capabilities_use_the_three_segment_grammar_and_require_dual_control()
    {
        Assert.Equal("account.closure.propose", AccountPermissions.ClosurePropose.Value);
        Assert.Equal("account.closure.approve", AccountPermissions.ClosureApprove.Value);
        Assert.Equal(3, AccountPermissions.ClosurePropose.Value.Split('.').Length);
        Assert.Equal(3, AccountPermissions.ClosureApprove.Value.Split('.').Length);
        Assert.True(AccountPermissions.ClosureProposeRequirement.RequiresStepUp);
        Assert.True(AccountPermissions.ClosureApproveRequirement.IsRestrictedApproval);
    }

    [Fact]
    public void Reconciliation_reports_a_closed_account_with_a_residual_balance()
    {
        IReadOnlyList<ConfigurationReconciliationFinding> findings = AccountReconciliationPolicy.Evaluate(
            [new AccountReconciliationFacts("account-1", "application-1", "Closed", "initialised", "ledger-1")],
            [],
            [new ClosureReconciliationFacts("proposal-1", "account-1", "closed", 25m, "hash-1")]);

        ConfigurationReconciliationFinding finding = Assert.Single(findings);
        Assert.Equal(AccountReconciliationPolicy.ClosedWithResidualBalanceCode, finding.Code);
        Assert.Equal("account-1", finding.Reference);
    }

    [Fact]
    public void Reconciliation_reports_a_closed_account_without_a_condition_snapshot()
    {
        IReadOnlyList<ConfigurationReconciliationFinding> findings = AccountReconciliationPolicy.Evaluate(
            [new AccountReconciliationFacts("account-1", "application-1", "Closed", "initialised", "ledger-1")],
            [],
            [new ClosureReconciliationFacts("proposal-1", "account-1", "closed", 0m, null)]);

        ConfigurationReconciliationFinding finding = Assert.Single(findings);
        Assert.Equal(AccountReconciliationPolicy.ClosureWithoutSnapshotCode, finding.Code);
        Assert.Equal("application-1", finding.ConfigurationSetId);
    }

    [Fact]
    public void Reconciliation_accepts_a_closure_that_recorded_its_snapshot_and_no_residual()
    {
        Assert.Empty(AccountReconciliationPolicy.Evaluate(
            [new AccountReconciliationFacts("account-1", "application-1", "Closed", "initialised", "ledger-1")],
            [],
            [new ClosureReconciliationFacts("proposal-1", "account-1", "closed", 0m, "hash-1")]));
    }

    [Fact]
    public void The_closure_contract_exposes_the_condition_evidence_and_refusals()
    {
        AccountClosureProposalResponseV1 response = new(
            "institution-1",
            "proposal-1",
            "account-1",
            "request-1",
            "conditions_failed",
            "customer_request",
            null,
            [AccountClosurePolicy.LienOutstandingCode],
            "hash-1",
            "ledger-1",
            "ledger:ledger-1:closure:v1",
            Now,
            0m,
            0m,
            0m,
            500m,
            0,
            null,
            Now,
            1,
            Replayed: false);

        Assert.Equal("hash-1", response.ConditionSnapshotHash);
        Assert.Equal(500m, response.OutstandingLienAmount);
        Assert.Contains(AccountClosurePolicy.LienOutstandingCode, response.RefusalCodes);
    }
}
