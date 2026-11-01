using System;
using CoreBanking.BuildingBlocks.Domain.Identity;
using CoreBanking.CustomerOnboarding.Domain;
using CoreBanking.CustomerOnboarding.Domain.Customers;
using Xunit;

namespace CoreBanking.CustomerOnboarding.UnitTests.Domain;

/// <summary>
/// Correction rules: a reason is required, the values must actually change, a verified attribute needs an
/// approval, and an invalidating correction must name the requirement to re-evidence.
/// Decision: epic 03 CUS-UC-06, invariant 15.
/// </summary>
public sealed class CustomerCorrectionTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 30, 18, 30, 0, TimeSpan.Zero);

    [Fact]
    public void A_plain_correction_records_both_references_and_needs_no_approval()
    {
        CustomerCorrection correction = Record();

        Assert.Equal("contact.email", correction.FieldCode);
        Assert.Equal("ref-prior-1", correction.PriorValueReference);
        Assert.Equal("ref-new-1", correction.NewValueReference);
        Assert.False(correction.RequiresReverification);
        Assert.Null(correction.RequirementCode);
        Assert.Null(correction.ApprovalReference);
        Assert.Equal(Now, correction.RecordedAt);
    }

    [Fact]
    public void A_verified_attribute_requires_an_approval_and_always_needs_reverification()
    {
        OnboardingRuleException missingApproval = Assert.Throws<OnboardingRuleException>(() =>
            Record(isVerifiedIdentifier: true, requirementCode: "contact_detail"));
        Assert.Equal("customer_correction_approval_required", missingApproval.Code);

        CustomerCorrection approved = Record(
            isVerifiedIdentifier: true,
            requirementCode: "contact_detail",
            approvalReference: "workflow-correction-1");

        Assert.True(approved.RequiresReverification);
        Assert.Equal("workflow-correction-1", approved.ApprovalReference);
    }

    [Fact]
    public void An_invalidating_correction_must_name_the_requirement()
    {
        OnboardingRuleException exception = Assert.Throws<OnboardingRuleException>(() =>
            Record(isVerifiedIdentifier: true, requirementCode: null, approvalReference: "workflow-correction-1"));

        Assert.Equal("customer_correction_requirement_required", exception.Code);
    }

    [Fact]
    public void A_correction_must_change_the_value_it_cites()
    {
        OnboardingRuleException exception = Assert.Throws<OnboardingRuleException>(() =>
            Record(newValueReference: "ref-prior-1"));

        Assert.Equal("customer_correction_unchanged", exception.Code);
    }

    [Theory]
    [InlineData("")]
    [InlineData("no")]
    public void A_correction_requires_a_meaningful_reason(string reason)
    {
        OnboardingRuleException exception = Assert.Throws<OnboardingRuleException>(() => Record(reason: reason));
        Assert.Equal("customer_correction_reason_required", exception.Code);
    }

    [Fact]
    public void A_field_code_must_be_a_stable_code()
    {
        OnboardingRuleException exception = Assert.Throws<OnboardingRuleException>(() => Record(fieldCode: "legal name"));
        Assert.Equal("customer_correction_field_invalid", exception.Code);
    }

    private static CustomerCorrection Record(
        string fieldCode = "contact.email",
        string priorValueReference = "ref-prior-1",
        string newValueReference = "ref-new-1",
        string reason = "customer supplied the corrected address",
        bool isVerifiedIdentifier = false,
        string? requirementCode = null,
        string? approvalReference = null) =>
        CustomerCorrection.Record(
            InstitutionId.From("inst-1"),
            "customer-1",
            fieldCode,
            priorValueReference,
            newValueReference,
            reason,
            evidenceReference: null,
            isVerifiedIdentifier,
            requirementCode,
            approvalReference,
            "steward-1",
            Now);
}
