using System;
using System.Linq;
using CoreBanking.BuildingBlocks.Domain.Identity;
using CoreBanking.CustomerOnboarding.Domain;
using CoreBanking.CustomerOnboarding.Domain.Customers;
using Xunit;

namespace CoreBanking.CustomerOnboarding.UnitTests.Domain;

/// <summary>
/// Merge plan rules: a frozen hash, the epic's lifecycle order, consumer-only acknowledgements and completion
/// that waits for every required consumer. Decision: P1-E03-D11.
/// </summary>
public sealed class CustomerMergeTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 29, 18, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_merge_requires_two_different_customers_and_a_reason()
    {
        Assert.Equal(
            "merge_same_customer",
            Assert.Throws<OnboardingRuleException>(
                () => CustomerMergeRequest.Propose(InstitutionId.From("institution-1"), "customer-1", "customer-1", "duplicate person", ["core-banking"], Now)).Code);

        Assert.Equal(
            "merge_reason_required",
            Assert.Throws<OnboardingRuleException>(
                () => CustomerMergeRequest.Propose(InstitutionId.From("institution-1"), "customer-1", "customer-2", "  ", ["core-banking"], Now)).Code);
    }

    [Fact]
    public void A_merge_requires_a_unique_bounded_consumer_list()
    {
        Assert.Equal(
            "merge_consumers_invalid",
            Assert.Throws<OnboardingRuleException>(
                () => CustomerMergeRequest.Propose(InstitutionId.From("institution-1"), "customer-1", "customer-2", "duplicate person", [], Now)).Code);

        Assert.Equal(
            "merge_consumers_invalid",
            Assert.Throws<OnboardingRuleException>(
                () => CustomerMergeRequest.Propose(
                    InstitutionId.From("institution-1"),
                    "customer-1",
                    "customer-2",
                    "duplicate person",
                    ["core-banking", "core-banking"],
                    Now)).Code);
    }

    [Fact]
    public void The_plan_hash_covers_the_survivor_source_reason_and_consumers()
    {
        CustomerMergeRequest first = NewRequest();
        CustomerMergeRequest samePlan = NewRequest();
        CustomerMergeRequest otherReason = CustomerMergeRequest.Propose(
            InstitutionId.From("institution-1"),
            "customer-1",
            "customer-2",
            "second view of the same person",
            ["core-banking", "risk-reporting"],
            Now);

        Assert.Equal(first.PlanHash, samePlan.PlanHash);
        Assert.NotEqual(first.PlanHash, otherReason.PlanHash);
        Assert.Equal(64, first.PlanHash.Length);
        Assert.Equal(["core-banking", "risk-reporting"], first.RequiredConsumers);
    }

    [Fact]
    public void The_lifecycle_runs_in_order_and_completes_only_when_every_consumer_answers()
    {
        CustomerMergeRequest request = NewRequest();

        Assert.Equal(
            "merge_status_invalid",
            Assert.Throws<OnboardingRuleException>(() => request.Approve("workflow-1", Now)).Code);

        request.Analyse(Now);
        request.SubmitForApproval(Now);
        request.Approve("workflow-decision-1", Now);
        request.BeginApplying(Now);
        request.RequireReconciliation(Now);

        Assert.Equal(MergeRequestStatus.ReconciliationRequired, request.Status);
        Assert.Equal(["core-banking", "risk-reporting"], request.OutstandingConsumers);

        request.Acknowledge("core-banking", Now);
        Assert.Equal(MergeRequestStatus.ReconciliationRequired, request.Status);
        Assert.Equal(["risk-reporting"], request.OutstandingConsumers);

        request.Acknowledge("risk-reporting", Now);
        Assert.Equal(MergeRequestStatus.Completed, request.Status);
        Assert.Equal(Now, request.CompletedAt);
        Assert.Equal("workflow-decision-1", request.ApprovalReference);
    }

    [Fact]
    public void Only_required_consumers_may_acknowledge_and_repeats_are_idempotent()
    {
        CustomerMergeRequest request = Applying();

        Assert.Equal(
            "merge_consumer_not_required",
            Assert.Throws<OnboardingRuleException>(() => request.Acknowledge("payments-vtu", Now)).Code);

        request.Acknowledge("core-banking", Now);
        request.Acknowledge("core-banking", Now);

        Assert.Single(request.Acknowledgements);
        Assert.Equal(["risk-reporting"], request.OutstandingConsumers);
    }

    [Fact]
    public void An_approved_merge_cannot_be_rejected()
    {
        CustomerMergeRequest request = NewRequest();
        request.Analyse(Now);
        request.SubmitForApproval(Now);
        request.Approve("workflow-decision-1", Now);

        Assert.Equal(
            "merge_status_invalid",
            Assert.Throws<OnboardingRuleException>(() => request.Reject("changed mind", Now)).Code);
    }

    [Fact]
    public void Withdrawal_requires_a_reason_and_is_final()
    {
        CustomerMergeRequest request = NewRequest();

        Assert.Equal(
            "merge_reason_required",
            Assert.Throws<OnboardingRuleException>(() => request.Withdraw("  ", Now)).Code);

        request.Withdraw("proposed in error", Now);

        Assert.Equal(MergeRequestStatus.Withdrawn, request.Status);
        Assert.Equal(
            "merge_status_invalid",
            Assert.Throws<OnboardingRuleException>(() => request.Withdraw("again", Now)).Code);
    }

    [Fact]
    public void Reconciliation_requires_something_outstanding()
    {
        CustomerMergeRequest request = NewRequest();
        request.Analyse(Now);
        request.SubmitForApproval(Now);
        request.Approve("workflow-decision-1", Now);
        request.BeginApplying(Now);
        request.Acknowledge("core-banking", Now);
        request.Acknowledge("risk-reporting", Now);

        Assert.Equal(MergeRequestStatus.Completed, request.Status);
        Assert.Equal(
            "merge_status_invalid",
            Assert.Throws<OnboardingRuleException>(() => request.RequireReconciliation(Now)).Code);
    }

    private static CustomerMergeRequest NewRequest() =>
        CustomerMergeRequest.Propose(
            InstitutionId.From("institution-1"),
            "customer-1",
            "customer-2",
            "same person confirmed by duplicate review",
            ["core-banking", "risk-reporting"],
            Now);

    private static CustomerMergeRequest Applying()
    {
        CustomerMergeRequest request = NewRequest();
        request.Analyse(Now);
        request.SubmitForApproval(Now);
        request.Approve("workflow-decision-1", Now);
        request.BeginApplying(Now);
        return request;
    }
}
