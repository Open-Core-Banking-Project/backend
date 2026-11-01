using System;
using System.Linq;
using CoreBanking.BuildingBlocks.Domain.Identity;
using CoreBanking.CustomerOnboarding.Domain;
using CoreBanking.CustomerOnboarding.Domain.Kyc;
using Xunit;

namespace CoreBanking.CustomerOnboarding.UnitTests.Domain;

/// <summary>
/// KYC requirement, case and verification rules: the epic's lifecycle, reviewer-only resolution, explicit
/// waivers, canonical outcomes only through a mapping version, and uncertain never treated as a decision.
/// Decision: P1-E03-D06.
/// </summary>
public sealed class KycCaseTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 29, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_case_opens_from_the_requirements_it_is_given()
    {
        KycCase kycCase = NewCase("contact_detail", "address_detail");

        Assert.Equal(KycCaseStatus.Open, kycCase.Status);
        Assert.Equal(2, kycCase.Requirements.Count);
        Assert.All(kycCase.Requirements, requirement => Assert.Equal(KycRequirementStatus.Required, requirement.Status));
        Assert.False(kycCase.IsComplete);
        Assert.Equal("application-1", kycCase.ApplicationId);
        Assert.Equal("snapshot-1", kycCase.RequirementSnapshotId);
    }

    [Fact]
    public void A_requirement_code_may_appear_only_once_in_a_case()
    {
        OnboardingRuleException exception = Assert.Throws<OnboardingRuleException>(
            () => KycCase.Open(
                InstitutionId.From("institution-1"),
                "application-1",
                "party-1",
                "snapshot-1",
                ["contact_detail", "contact_detail"],
                Now));

        Assert.Equal("kyc_case_invalid", exception.Code);
    }

    [Fact]
    public void A_case_completes_when_every_requirement_is_passed_or_waived()
    {
        KycCase kycCase = NewCase("contact_detail", "address_detail");
        kycCase.StartVerification("contact_detail", "verification-1", Now);
        kycCase.ApplyOutcome("contact_detail", CanonicalVerificationOutcome.Passed, Now);
        Assert.Equal(KycCaseStatus.Open, kycCase.Status);

        kycCase.Waive("address_detail", "risk-head", "document not available in this channel", Now);

        Assert.Equal(KycCaseStatus.Completed, kycCase.Status);
        Assert.Equal(Now, kycCase.CompletedAt);
        Assert.Equal(
            "kyc_case_completed",
            Assert.Throws<OnboardingRuleException>(() => kycCase.RegisterEvidence("address_detail", Now)).Code);
    }

    [Fact]
    public void Requirement_transitions_cannot_be_skipped_or_repeated()
    {
        KycCase kycCase = NewCase("contact_detail", "address_detail");

        Assert.Equal(
            "kyc_requirement_transition_invalid",
            Assert.Throws<OnboardingRuleException>(() => kycCase.MarkReady("contact_detail", Now)).Code);

        kycCase.RegisterEvidence("contact_detail", Now);
        kycCase.MarkReady("contact_detail", Now);
        kycCase.StartVerification("contact_detail", "verification-1", Now);
        kycCase.ApplyOutcome("contact_detail", CanonicalVerificationOutcome.Passed, Now);

        Assert.Equal(
            "kyc_requirement_transition_invalid",
            Assert.Throws<OnboardingRuleException>(
                () => kycCase.ApplyOutcome("contact_detail", CanonicalVerificationOutcome.Failed, Now)).Code);
    }

    [Fact]
    public void An_unknown_requirement_is_reported_as_such()
    {
        KycCase kycCase = NewCase("contact_detail");

        Assert.Equal(
            "kyc_requirement_not_found",
            Assert.Throws<OnboardingRuleException>(() => kycCase.StartVerification("tax_check", "verification-1", Now)).Code);
    }

    [Fact]
    public void An_inconclusive_item_needs_a_reviewer_decision_and_a_pass_is_final()
    {
        KycCase kycCase = NewCase("contact_detail", "address_detail");
        kycCase.StartVerification("contact_detail", "verification-1", Now);
        kycCase.ApplyOutcome("contact_detail", CanonicalVerificationOutcome.Inconclusive, Now);

        Assert.Equal(KycRequirementStatus.Inconclusive, kycCase.Require("contact_detail").Status);

        kycCase.ApplyOutcome("contact_detail", CanonicalVerificationOutcome.Passed, Now);
        Assert.Equal(
            "kyc_requirement_transition_invalid",
            Assert.Throws<OnboardingRuleException>(
                () => kycCase.ApplyOutcome("contact_detail", CanonicalVerificationOutcome.Inconclusive, Now)).Code);
        Assert.Equal(KycRequirementStatus.Passed, kycCase.Require("contact_detail").Status);
    }

    [Fact]
    public void A_waiver_requires_an_authority_and_a_reason()
    {
        KycCase kycCase = NewCase("contact_detail");

        Assert.Equal(
            "kyc_waiver_reason_required",
            Assert.Throws<OnboardingRuleException>(() => kycCase.Waive("contact_detail", "risk-head", "  ", Now)).Code);

        kycCase.Waive("contact_detail", "risk-head", "not available in this channel", Now);
        KycRequirement requirement = kycCase.Require("contact_detail");
        Assert.Equal(KycRequirementStatus.Waived, requirement.Status);
        Assert.Equal("risk-head", requirement.WaiverAuthority);
        Assert.Equal("not available in this channel", requirement.WaiverReason);
    }

    [Fact]
    public void A_succeeded_attempt_requires_a_canonical_outcome_and_a_mapping_version()
    {
        IdentityVerification missingOutcome = NewVerification();
        missingOutcome.RecordAttempt("attempt-1", "provider-ref-1", Now);
        Assert.Equal(
            "kyc_verification_outcome_required",
            Assert.Throws<OnboardingRuleException>(
                () => missingOutcome.ApplyAttemptResult(
                    "attempt-1",
                    VerificationAttemptStatus.Succeeded,
                    outcome: null,
                    providerMappingVersion: "mapping-v1",
                    Now)).Code);

        IdentityVerification missingMapping = NewVerification();
        missingMapping.RecordAttempt("attempt-1", "provider-ref-1", Now);
        Assert.Equal(
            "kyc_mapping_version_required",
            Assert.Throws<OnboardingRuleException>(
                () => missingMapping.ApplyAttemptResult(
                    "attempt-1",
                    VerificationAttemptStatus.Succeeded,
                    CanonicalVerificationOutcome.Passed,
                    providerMappingVersion: null,
                    Now)).Code);
    }

    [Fact]
    public void An_uncertain_attempt_is_neither_a_pass_nor_a_failure()
    {
        IdentityVerification verification = NewVerification();
        verification.RecordAttempt("attempt-1", "provider-ref-1", Now);

        verification.ApplyAttemptResult(
            "attempt-1",
            VerificationAttemptStatus.Uncertain,
            outcome: null,
            providerMappingVersion: null,
            Now);

        Assert.Equal(VerificationStatus.Uncertain, verification.Status);
        Assert.Null(verification.Outcome);
        Assert.False(verification.IsTerminal);
    }

    [Fact]
    public void A_resolved_verification_cannot_be_resolved_again_or_cancelled()
    {
        IdentityVerification verification = NewVerification();
        verification.RecordAttempt("attempt-1", "provider-ref-1", Now);
        verification.ApplyAttemptResult(
            "attempt-1",
            VerificationAttemptStatus.Succeeded,
            CanonicalVerificationOutcome.Passed,
            "mapping-v1",
            new VerificationEvidence("evidence-ref-1", new string('a', 64)),
            Now);

        Assert.Equal(VerificationStatus.Succeeded, verification.Status);
        Assert.Equal(CanonicalVerificationOutcome.Passed, verification.Outcome);
        Assert.Equal("mapping-v1", verification.ProviderMappingVersion);

        Assert.Equal(
            "kyc_verification_closed",
            Assert.Throws<OnboardingRuleException>(() => verification.Cancel(Now)).Code);
        Assert.Equal(
            "kyc_verification_closed",
            Assert.Throws<OnboardingRuleException>(
                () => verification.ApplyAttemptResult(
                    "attempt-1",
                    VerificationAttemptStatus.Failed,
                    outcome: null,
                    providerMappingVersion: null,
                    Now)).Code);
    }

    [Fact]
    public void A_retry_is_a_new_attempt_and_the_earlier_attempt_is_untouched()
    {
        IdentityVerification verification = NewVerification();
        VerificationAttempt first = verification.RecordAttempt("attempt-1", "provider-ref-1", Now);
        verification.ApplyAttemptResult(
            "attempt-1",
            VerificationAttemptStatus.Uncertain,
            outcome: null,
            providerMappingVersion: null,
            Now);
        verification.RecordAttempt("attempt-2", "provider-ref-2", Now);

        Assert.Equal(2, verification.Attempts.Count);
        Assert.Equal(VerificationAttemptStatus.Uncertain, first.Status);
        Assert.Equal(VerificationAttemptStatus.Submitted, verification.Attempts[1].Status);
        Assert.Equal(VerificationStatus.Submitted, verification.Status);

        Assert.Equal(
            "kyc_attempt_duplicate",
            Assert.Throws<OnboardingRuleException>(() => verification.RecordAttempt("attempt-1", null, Now)).Code);
    }

    [Fact]
    public void An_unknown_attempt_cannot_be_resolved()
    {
        IdentityVerification verification = NewVerification();
        verification.RecordAttempt("attempt-1", null, Now);

        Assert.Equal(
            "kyc_attempt_not_found",
            Assert.Throws<OnboardingRuleException>(
                () => verification.ApplyAttemptResult(
                    "attempt-9",
                    VerificationAttemptStatus.Pending,
                    outcome: null,
                    providerMappingVersion: null,
                    Now)).Code);
    }

    [Fact]
    public void A_manual_review_outcome_sends_the_verification_to_a_human_decision()
    {
        IdentityVerification verification = NewVerification();
        verification.RecordAttempt("attempt-1", null, Now);

        verification.ApplyAttemptResult(
            "attempt-1",
            VerificationAttemptStatus.Succeeded,
            CanonicalVerificationOutcome.ManualReview,
            "mapping-v1",
            new VerificationEvidence("evidence-ref-1", new string('a', 64)),
            Now);

        Assert.Equal(VerificationStatus.ManualReview, verification.Status);
        Assert.False(verification.IsTerminal);
    }

    private static KycCase NewCase(params string[] requirementCodes) =>
        KycCase.Open(
            InstitutionId.From("institution-1"),
            "application-1",
            "party-1",
            "snapshot-1",
            [.. requirementCodes],
            Now);

    private static IdentityVerification NewVerification() =>
        IdentityVerification.Request(
            InstitutionId.From("institution-1"),
            "application-1",
            "party-1",
            "contact_detail",
            "document_check",
            "snapshot-1",
            Now);
}
