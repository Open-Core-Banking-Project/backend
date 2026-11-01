using System;
using CoreBanking.BuildingBlocks.Domain.Identity;
using CoreBanking.CustomerOnboarding.Domain;
using CoreBanking.CustomerOnboarding.Domain.Customers;
using Xunit;

namespace CoreBanking.CustomerOnboarding.UnitTests.Domain;

/// <summary>
/// Effective-dated relationship-manager assignment rules. Requirement: CUS-010, CUS-026.
/// </summary>
public sealed class RelationshipManagerTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 30, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void An_assignment_is_current_until_it_is_closed()
    {
        RelationshipManagerAssignment assignment = RelationshipManagerAssignment.Assign(
            InstitutionId.From("inst-1"),
            "customer-1",
            "staff-1",
            "branch-1",
            previousAssignmentId: null,
            "officer-1",
            "relationship handover",
            Start);

        Assert.True(assignment.IsCurrent);
        Assert.Null(assignment.EffectiveTo);
        Assert.Equal(Start, assignment.EffectiveFrom);
        Assert.Equal("staff-1", assignment.ManagerActorId);
        Assert.Equal("branch-1", assignment.ScopeBranchId);
        Assert.Equal("relationship handover", assignment.Reason);

        assignment.Close(Start.AddDays(5));

        Assert.False(assignment.IsCurrent);
        Assert.Equal(Start.AddDays(5), assignment.EffectiveTo);
    }

    [Fact]
    public void A_closed_assignment_cannot_be_closed_again()
    {
        RelationshipManagerAssignment assignment = RelationshipManagerAssignment.Assign(
            InstitutionId.From("inst-1"),
            "customer-1",
            "staff-1",
            null,
            null,
            "officer-1",
            null,
            Start);
        assignment.Close(Start.AddDays(1));

        OnboardingRuleException exception = Assert.Throws<OnboardingRuleException>(() => assignment.Close(Start.AddDays(2)));
        Assert.Equal("customer_relationship_manager_closed", exception.Code);
    }

    [Fact]
    public void An_assignment_cannot_end_before_it_starts()
    {
        RelationshipManagerAssignment assignment = RelationshipManagerAssignment.Assign(
            InstitutionId.From("inst-1"),
            "customer-1",
            "staff-1",
            null,
            null,
            "officer-1",
            null,
            Start);

        OnboardingRuleException exception = Assert.Throws<OnboardingRuleException>(() => assignment.Close(Start.AddSeconds(-1)));
        Assert.Equal("customer_relationship_manager_period_invalid", exception.Code);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("staff with spaces")]
    public void A_manager_reference_must_be_a_stable_code(string managerActorId)
    {
        OnboardingRuleException exception = Assert.Throws<OnboardingRuleException>(() =>
            RelationshipManagerAssignment.Assign(
                InstitutionId.From("inst-1"),
                "customer-1",
                managerActorId,
                null,
                null,
                "officer-1",
                null,
                Start));

        Assert.Equal("customer_relationship_manager_invalid", exception.Code);
    }

    [Fact]
    public void A_scope_branch_must_be_a_stable_code()
    {
        OnboardingRuleException exception = Assert.Throws<OnboardingRuleException>(() =>
            RelationshipManagerAssignment.Assign(
                InstitutionId.From("inst-1"),
                "customer-1",
                "staff-1",
                "branch with spaces",
                null,
                "officer-1",
                null,
                Start));

        Assert.Equal("customer_relationship_manager_scope_invalid", exception.Code);
    }

    [Fact]
    public void A_customer_identifier_is_required()
    {
        Assert.Throws<ArgumentException>(() =>
            RelationshipManagerAssignment.Assign(
                InstitutionId.From("inst-1"),
                " ",
                "staff-1",
                null,
                null,
                "officer-1",
                null,
                Start));
    }
}
