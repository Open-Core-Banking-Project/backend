using System;
using CoreBanking.BuildingBlocks.Domain.Identity;
using CoreBanking.CustomerOnboarding.Domain;
using CoreBanking.CustomerOnboarding.Domain.Kyc;
using Xunit;

namespace CoreBanking.CustomerOnboarding.UnitTests.Domain;

/// <summary>
/// An accepted provider outcome must cite the mapping version and the evidence reference with a SHA-256 hash.
/// Decision: P1-E03-D08.
/// </summary>
public sealed class VerificationEvidenceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 30, 8, 0, 0, TimeSpan.Zero);

    private static readonly string Hash = new('a', 64);

    [Fact]
    public void An_accepted_outcome_records_its_evidence_reference_and_hash()
    {
        IdentityVerification verification = NewVerification();
        verification.RecordAttempt("attempt-1", "provider-ref-1", "synthetic-kyc-route-1", Now);

        verification.ApplyAttemptResult(
            "attempt-1",
            VerificationAttemptStatus.Succeeded,
            CanonicalVerificationOutcome.Passed,
            "mapping-v1",
            new VerificationEvidence("evidence-1", Hash),
            Now);

        Assert.Equal(VerificationStatus.Succeeded, verification.Status);
        Assert.Equal("evidence-1", verification.EvidenceReference);
        Assert.Equal(Hash, verification.EvidenceHash);
        Assert.Equal("synthetic-kyc-route-1", verification.Attempts[0].ProviderRouteId);
    }

    [Fact]
    public void An_accepted_outcome_without_evidence_is_refused()
    {
        IdentityVerification verification = NewVerification();
        verification.RecordAttempt("attempt-1", "provider-ref-1", Now);

        OnboardingRuleException exception = Assert.Throws<OnboardingRuleException>(() =>
            verification.ApplyAttemptResult(
                "attempt-1",
                VerificationAttemptStatus.Succeeded,
                CanonicalVerificationOutcome.Passed,
                "mapping-v1",
                evidence: null,
                Now));

        Assert.Equal("kyc_evidence_reference_required", exception.Code);
        Assert.Equal(VerificationStatus.Submitted, verification.Status);
    }

    [Theory]
    [InlineData("not-a-hash")]
    [InlineData("zzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzz")]
    public void An_evidence_hash_must_be_a_sha256_digest(string hash)
    {
        IdentityVerification verification = NewVerification();
        verification.RecordAttempt("attempt-1", "provider-ref-1", Now);

        OnboardingRuleException exception = Assert.Throws<OnboardingRuleException>(() =>
            verification.ApplyAttemptResult(
                "attempt-1",
                VerificationAttemptStatus.Succeeded,
                CanonicalVerificationOutcome.Passed,
                "mapping-v1",
                new VerificationEvidence("evidence-1", hash),
                Now));

        Assert.Equal("kyc_evidence_hash_invalid", exception.Code);
    }

    [Fact]
    public void An_uncertain_attempt_needs_no_evidence()
    {
        IdentityVerification verification = NewVerification();
        verification.RecordAttempt("attempt-1", null, Now);

        verification.ApplyAttemptResult(
            "attempt-1",
            VerificationAttemptStatus.Uncertain,
            outcome: null,
            providerMappingVersion: null,
            Now);

        Assert.Equal(VerificationStatus.Uncertain, verification.Status);
        Assert.Null(verification.EvidenceReference);
    }

    private static IdentityVerification NewVerification() =>
        IdentityVerification.Request(
            InstitutionId.From("inst-1"),
            "application-1",
            "party-1",
            "contact_detail",
            "identity_document_check",
            "snapshot-1",
            Now);
}
