using System;
using System.Linq;
using CoreBanking.BuildingBlocks.Domain.Identity;
using CoreBanking.CustomerOnboarding.Domain;
using CoreBanking.CustomerOnboarding.Domain.Customers;
using Xunit;

namespace CoreBanking.CustomerOnboarding.UnitTests.Domain;

/// <summary>
/// Customer classification and lifecycle rules: only an approved classification activates, the epic's
/// transition table is enforced, sensitive transitions need an approval reference and terminal states stay
/// terminal. Decision: P1-E03-D09, P1-E03-D10.
/// </summary>
public sealed class CustomerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 29, 14, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_registered_customer_is_pending_with_no_status_history()
    {
        Customer customer = NewCustomer();

        Assert.Equal(CustomerStatus.Pending, customer.Status);
        Assert.Equal("1", customer.CustomerNumber);
        Assert.Equal("party-1", customer.PartyId);
        Assert.Empty(customer.StatusHistory);
        Assert.Null(customer.EffectiveClassification);
    }

    [Fact]
    public void Activation_requires_an_approved_classification()
    {
        Customer customer = NewCustomer();
        customer.ProposeClassification("tier_1", "retail", "low", requiresEnhancedDueDiligence: false, Now);

        Assert.Equal(
            "customer_classification_required",
            Assert.Throws<OnboardingRuleException>(() => customer.Activate(1, Now)).Code);

        customer.ApproveClassification(1, "workflow-decision-1", Now);
        customer.Activate(1, Now);

        Assert.Equal(CustomerStatus.Active, customer.Status);
        CustomerStatusChange change = Assert.Single(customer.StatusHistory);
        Assert.Equal(CustomerStatus.Pending, change.From);
        Assert.Equal(CustomerStatus.Active, change.To);
        Assert.Equal("workflow-decision-1", change.ApprovalReference);
    }

    [Fact]
    public void Approving_a_new_version_supersedes_the_earlier_approved_one()
    {
        Customer customer = NewCustomer();
        customer.ProposeClassification("tier_1", "retail", "low", false, Now);
        customer.ApproveClassification(1, "workflow-1", Now);
        customer.ProposeClassification("tier_2", "retail", "medium", true, Now);
        customer.ApproveClassification(2, "workflow-2", Now);

        CustomerClassification first = customer.Classifications.Single(classification => classification.Version == 1);
        CustomerClassification second = customer.Classifications.Single(classification => classification.Version == 2);

        Assert.Equal(ClassificationStatus.Superseded, first.Status);
        Assert.Equal(ClassificationStatus.Approved, second.Status);
        Assert.Equal(2, customer.EffectiveClassification!.Version);
        Assert.True(second.RequiresEnhancedDueDiligence);
    }

    [Fact]
    public void Only_the_epic_transitions_are_allowed()
    {
        Customer customer = Activated();
        customer.ChangeStatus(CustomerStatus.Dormant, "no customer-initiated activity", "ops-1", null, Now);
        customer.ChangeStatus(CustomerStatus.Active, "activity resumed", "ops-1", null, Now);

        Assert.Equal(CustomerStatus.Active, customer.Status);
        Assert.Equal(3, customer.StatusHistory.Count);

        Customer pending = NewCustomer();
        Assert.Equal(
            "customer_transition_forbidden",
            Assert.Throws<OnboardingRuleException>(
                () => pending.ChangeStatus(CustomerStatus.Dormant, "skipping activation", "ops-1", null, Now)).Code);
    }

    [Fact]
    public void Terminal_states_are_never_reversed()
    {
        Customer customer = Activated();
        customer.ChangeStatus(CustomerStatus.Closed, "customer requested closure", "ops-1", "workflow-close-1", Now);

        Assert.Equal(CustomerStatus.Closed, customer.Status);
        Assert.Equal(
            "customer_transition_forbidden",
            Assert.Throws<OnboardingRuleException>(
                () => customer.ChangeStatus(CustomerStatus.Active, "reopen request", "ops-1", "workflow-reopen-1", Now)).Code);
    }

    [Fact]
    public void Sensitive_transitions_require_an_approval_reference()
    {
        Customer customer = Activated();

        Assert.Equal(
            "customer_approval_required",
            Assert.Throws<OnboardingRuleException>(
                () => customer.ChangeStatus(CustomerStatus.Restricted, "fraud investigation", "ops-1", null, Now)).Code);

        customer.ChangeStatus(CustomerStatus.Restricted, "fraud investigation", "ops-1", "workflow-restrict-1", Now);

        Assert.Equal(CustomerStatus.Restricted, customer.Status);
        Assert.Equal("workflow-restrict-1", customer.StatusHistory[^1].ApprovalReference);
    }

    [Fact]
    public void A_status_change_requires_a_reason()
    {
        Customer customer = Activated();

        Assert.Equal(
            "customer_status_reason_required",
            Assert.Throws<OnboardingRuleException>(
                () => customer.ChangeStatus(CustomerStatus.Dormant, "  ", "ops-1", null, Now)).Code);
        Assert.Equal(CustomerStatus.Active, customer.Status);
    }

    [Fact]
    public void Classification_values_must_be_stable_codes()
    {
        Customer customer = NewCustomer();

        Assert.Equal(
            "customer_classification_invalid",
            Assert.Throws<OnboardingRuleException>(
                () => customer.ProposeClassification("Tier One", "retail", "low", false, Now)).Code);
    }

    [Fact]
    public void A_terminal_customer_cannot_receive_a_new_classification()
    {
        Customer customer = Activated();
        customer.ChangeStatus(CustomerStatus.Deceased, "death notification received", "ops-1", "workflow-death-1", Now);

        Assert.Equal(
            "customer_status_terminal",
            Assert.Throws<OnboardingRuleException>(
                () => customer.ProposeClassification("tier_1", "retail", "low", false, Now)).Code);
    }

    private static Customer NewCustomer() =>
        Customer.Register(InstitutionId.From("institution-1"), "party-1", "1", "branch-1", Now);

    private static Customer Activated()
    {
        Customer customer = NewCustomer();
        customer.ProposeClassification("tier_1", "retail", "low", false, Now);
        customer.ApproveClassification(1, "workflow-1", Now);
        customer.Activate(1, Now);
        return customer;
    }
}
