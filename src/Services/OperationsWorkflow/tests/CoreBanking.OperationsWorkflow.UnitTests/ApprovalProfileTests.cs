using System;
using System.Linq;
using CoreBanking.BuildingBlocks.Domain.Identity;
using CoreBanking.OperationsWorkflow.Domain.Approvals;
using Xunit;

namespace CoreBanking.OperationsWorkflow.UnitTests;

/// <summary>
/// The approved profile catalogue. Each hash is pinned to a literal so a change to a key, version, pattern, maker
/// policy, permission or entitlement policy is visible here and in the source service that computed the same value.
/// Decision: ADR-0016, P1-E03-D09, P1-E03-D11.
/// </summary>
public sealed class ApprovalProfileTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 31, 20, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Every_approved_profile_hash_is_pinned()
    {
        Assert.Equal(
            "61905622f89148127dfc877d7b6753e65c7b432dd56f9cca2cee567bec20b70a",
            ApprovalProfiles.SavingsEligibilityPublication.DefinitionHash);
        Assert.Equal(
            "57d30d22b9182e0461b3ae80681c1999e0ccf73d6acd9186e5b0c7f8ecf23380",
            ApprovalProfiles.CustomerClassification.DefinitionHash);
        Assert.Equal(
            "8af7874ad1e2c63ebcf210a75acd533e12372136f22a38e0f97ad4c321e4cb37",
            ApprovalProfiles.CustomerMerge.DefinitionHash);
    }

    [Fact]
    public void The_source_services_are_derived_from_the_catalogue()
    {
        Assert.Equal(["core-ledger", "customer-onboarding"], ApprovalProfiles.SourceServices.ToArray());
        Assert.Equal(3, ApprovalProfiles.All.Count);
        Assert.Equal(
            ApprovalProfiles.SavingsEligibilityPublication.DefinitionHash,
            SavingsEligibilityPublicationProfile.DefinitionHash);
    }

    [Fact]
    public void An_unknown_profile_key_is_refused()
    {
        Assert.Null(ApprovalProfiles.Find("customer.classification.approve.v2"));
        Assert.Null(ApprovalProfiles.Find(" "));
        Assert.NotNull(ApprovalProfiles.Find(ApprovalProfiles.CustomerMerge.WorkflowKey));
    }

    [Fact]
    public void A_customer_request_pins_the_profile_instead_of_the_caller()
    {
        ApprovalRequest request = ApprovalRequest.Submit(
            ApprovalProfiles.CustomerClassification,
            InstitutionId.From("institution-1"),
            "request-1",
            "source-key-1",
            "customer-1",
            "3",
            new string('b', 64),
            ActorId.From("maker-1"),
            "correlation-1",
            Now);

        Assert.Equal("customer.classification.approve.v1", request.WorkflowKey);
        Assert.Equal("customer-classification-approval-v1", request.WorkflowVersionId);
        Assert.Equal("customer-onboarding", request.SourceService);
        Assert.Equal("customer.classification.version", request.SubjectType);
        Assert.Equal("customer.classification.approve", request.RequiredPermission);
        Assert.Equal(ApprovalProfiles.CustomerClassification.DefinitionHash, request.WorkflowDefinitionHash);
        Assert.Equal(ApprovalRequestStatus.Submitted, request.Status);
    }

    [Fact]
    public void A_decision_must_carry_the_permission_of_the_request_profile()
    {
        ApprovalRequest request = ApprovalRequest.Submit(
            ApprovalProfiles.CustomerMerge,
            InstitutionId.From("institution-1"),
            "request-1",
            "source-key-1",
            "customer-1",
            "1",
            new string('c', 64),
            ActorId.From("maker-1"),
            "correlation-1",
            Now);

        EntitlementDecisionEvidence wrongPermission = EntitlementDecisionEvidence.Create(
            InstitutionId.From("institution-1"),
            ActorId.From("approver-1"),
            "customer.classification.approve",
            "4",
            Now.AddMinutes(-1),
            Now.AddMinutes(4));

        InvalidOperationException refusal = Assert.Throws<InvalidOperationException>(() =>
            request.Decide("decision-1", ApprovalDecisionType.Approve, wrongPermission, null, Now));
        Assert.Equal("approval_permission_missing", refusal.Message);

        EntitlementDecisionEvidence correct = EntitlementDecisionEvidence.Create(
            InstitutionId.From("institution-1"),
            ActorId.From("approver-1"),
            "customer.merge.approve",
            "4",
            Now.AddMinutes(-1),
            Now.AddMinutes(4));

        ApprovalDecision decision = request.Decide("decision-1", ApprovalDecisionType.Approve, correct, null, Now);
        Assert.Equal(ApprovalDecisionType.Approve, decision.Decision);
        Assert.Equal(ApprovalRequestStatus.Approved, request.Status);
    }
}
