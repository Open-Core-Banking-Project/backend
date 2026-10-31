using System;
using System.Collections.Generic;
using System.Linq;
using CoreBanking.BuildingBlocks.Domain.Identity;
using CoreBanking.CustomerOnboarding.Domain;
using CoreBanking.CustomerOnboarding.Domain.Duplicates;
using Xunit;

namespace CoreBanking.CustomerOnboarding.UnitTests.Domain;

/// <summary>
/// Exact duplicate rules and the candidate set lifecycle: explainable factors only, a band derived from
/// the evidence, bounded runs and a decision that is final for its set.
/// Decision: P1-E03-D04.
/// </summary>
public sealed class DuplicateDetectionTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 29, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_shared_contact_token_raises_one_contact_factor_per_channel()
    {
        DuplicateSubject subject = Subject("party-a", email: "token-email", mobile: "token-mobile");
        DuplicateSubject candidate = Subject("party-b", name: "ADA LOVELACE", birthDateToken: "another-birth-token", email: "token-email", mobile: "token-mobile");

        IReadOnlyList<DuplicateMatchFactor> factors = DuplicateMatchRules.Compare(subject, candidate);

        Assert.Equal(2, factors.Count);
        Assert.All(factors, factor => Assert.Equal(DuplicateMatchFactorKind.ContactPoint, factor.Kind));
        Assert.Equal(["email", "mobile"], factors.Select(factor => factor.Basis).OrderBy(basis => basis, StringComparer.Ordinal));
        Assert.Equal(DuplicateMatchBand.Possible, DuplicateMatchRules.Band(factors));
    }

    [Fact]
    public void The_same_token_on_a_different_channel_is_not_a_match()
    {
        DuplicateSubject subject = Subject("party-a", email: "shared-token");
        DuplicateSubject candidate = Subject("party-b", name: "ADA LOVELACE", birthDateToken: "another-birth-token", mobile: "shared-token");

        Assert.Empty(DuplicateMatchRules.Compare(subject, candidate));
    }

    [Fact]
    public void Name_and_birth_date_are_required_together_for_the_name_factor()
    {
        DuplicateSubject subject = Subject("party-a", name: "ADA LOVELACE", birthDateToken: "birth-token");
        DuplicateSubject sameName = Subject("party-b", name: "ADA LOVELACE", birthDateToken: "different-birth-token");
        DuplicateSubject sameBirth = Subject("party-c", name: "GRACE HOPPER", birthDateToken: "birth-token");
        DuplicateSubject both = Subject("party-d", name: "ADA LOVELACE", birthDateToken: "birth-token");

        Assert.DoesNotContain(DuplicateMatchRules.Compare(subject, sameName), factor => factor.Kind == DuplicateMatchFactorKind.NameAndBirthDate);
        Assert.DoesNotContain(DuplicateMatchRules.Compare(subject, sameBirth), factor => factor.Kind == DuplicateMatchFactorKind.NameAndBirthDate);

        IReadOnlyList<DuplicateMatchFactor> factors = DuplicateMatchRules.Compare(subject, both);
        Assert.Single(factors);
        Assert.Equal(DuplicateMatchFactorKind.NameAndBirthDate, factors[0].Kind);
        Assert.Equal(DuplicateMatchBand.Probable, DuplicateMatchRules.Band(factors));
    }

    [Fact]
    public void A_shared_address_line_raises_an_address_factor_and_stays_possible()
    {
        DuplicateSubject subject = Subject("party-a", address: "line-token");
        DuplicateSubject candidate = Subject("party-b", name: "ADA LOVELACE", birthDateToken: "another-birth-token", address: "line-token");

        IReadOnlyList<DuplicateMatchFactor> factors = DuplicateMatchRules.Compare(subject, candidate);

        Assert.Single(factors);
        Assert.Equal(DuplicateMatchFactorKind.AddressLine, factors[0].Kind);
        Assert.Equal(DuplicateMatchBand.Possible, DuplicateMatchRules.Band(factors));
    }

    [Fact]
    public void A_party_is_never_compared_with_itself()
    {
        DuplicateSubject subject = Subject("party-a", email: "token-email", name: "ADA LOVELACE");

        Assert.Empty(DuplicateMatchRules.Compare(subject, subject));
    }

    [Fact]
    public void A_candidate_without_evidence_is_refused()
    {
        OnboardingRuleException exception = Assert.Throws<OnboardingRuleException>(
            () => DuplicateCandidate.Create("party-b", [], Now));

        Assert.Equal("duplicate_factors_missing", exception.Code);
    }

    [Fact]
    public void A_candidate_derives_its_band_and_cannot_claim_more_than_its_evidence()
    {
        DuplicateCandidate contactOnly = DuplicateCandidate.Create(
            "party-b",
            [DuplicateMatchFactor.Create(DuplicateMatchFactorKind.ContactPoint, "email")],
            Now);
        DuplicateCandidate nameAndBirth = DuplicateCandidate.Create(
            "party-c",
            [DuplicateMatchFactor.Create(DuplicateMatchFactorKind.NameAndBirthDate, DuplicateMatchFactor.NameAndBirthDateBasis)],
            Now);

        Assert.Equal(DuplicateMatchBand.Possible, contactOnly.Band);
        Assert.Equal(DuplicateMatchBand.Probable, nameAndBirth.Band);
    }

    [Fact]
    public void Masked_matches_expose_masked_forms_and_the_birth_year_only()
    {
        DuplicateSubject subject = Subject("party-a", email: "token-email", birthDateToken: "birth-token", birthYear: 1990);
        DuplicateSubject candidate = Subject("party-b", email: "token-email", birthDateToken: "birth-token", birthYear: 1990);

        IReadOnlyList<DuplicateMatchFactor> factors = DuplicateMatchRules.Compare(subject, candidate);
        IReadOnlyList<string> masked = DuplicateMatchRules.MaskedMatches(subject, candidate, factors);

        Assert.Contains("m***@example.com", masked);
        Assert.Contains("birthYear:1990", masked);
        Assert.DoesNotContain(masked, value => value.Contains("plaintext", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void A_factor_basis_must_be_a_code()
    {
        OnboardingRuleException exception = Assert.Throws<OnboardingRuleException>(
            () => DuplicateMatchFactor.Create(DuplicateMatchFactorKind.ContactPoint, "ada@example.com"));

        Assert.Equal("duplicate_factor_invalid", exception.Code);
    }

    [Fact]
    public void An_open_set_starts_at_version_one_and_supersedes_without_deleting_evidence()
    {
        DuplicateCandidateSet set = OpenSet(candidates: 1);

        Assert.Equal(DuplicateCandidateSetStatus.Open, set.Status);
        Assert.Equal(1L, set.Version);
        Assert.Single(set.Candidates);

        set.Supersede(Now);

        Assert.Equal(DuplicateCandidateSetStatus.Superseded, set.Status);
        Assert.Equal(2L, set.Version);
        Assert.Single(set.Candidates);
    }

    [Fact]
    public void A_decision_records_the_reviewer_reason_and_result()
    {
        DuplicateCandidateSet set = OpenSet(candidates: 1);

        set.Decide(DuplicateReviewDecision.Distinct, "reviewer-1", "different person; shared family email", 1L, Now);

        Assert.Equal(DuplicateCandidateSetStatus.Decided, set.Status);
        Assert.Equal(DuplicateReviewDecision.Distinct, set.Decision);
        Assert.Equal("reviewer-1", set.DecidedBy);
        Assert.Equal(Now, set.DecidedAt);
        Assert.Equal(2L, set.Version);
        Assert.Equal("different person; shared family email", set.DecisionReason);
    }

    [Fact]
    public void A_decision_requires_the_current_version_and_a_reason()
    {
        DuplicateCandidateSet staleVersion = OpenSet(candidates: 1);
        OnboardingRuleException versionException = Assert.Throws<OnboardingRuleException>(
            () => staleVersion.Decide(DuplicateReviewDecision.SamePerson, "reviewer-1", "same person", 7L, Now));
        Assert.Equal("duplicate_candidate_set_stale_version", versionException.Code);

        DuplicateCandidateSet missingReason = OpenSet(candidates: 1);
        OnboardingRuleException reasonException = Assert.Throws<OnboardingRuleException>(
            () => missingReason.Decide(DuplicateReviewDecision.SamePerson, "reviewer-1", "  ", 1L, Now));
        Assert.Equal("duplicate_decision_reason_required", reasonException.Code);

        Assert.Equal(DuplicateCandidateSetStatus.Open, missingReason.Status);
    }

    [Fact]
    public void A_decided_set_cannot_be_decided_or_superseded_again()
    {
        DuplicateCandidateSet set = OpenSet(candidates: 1);
        set.Decide(DuplicateReviewDecision.InsufficientEvidence, "reviewer-1", "needs identity document", 1L, Now);

        OnboardingRuleException decideException = Assert.Throws<OnboardingRuleException>(
            () => set.Decide(DuplicateReviewDecision.Distinct, "reviewer-2", "second opinion", 2L, Now));
        Assert.Equal("duplicate_candidate_set_closed", decideException.Code);

        OnboardingRuleException supersedeException = Assert.Throws<OnboardingRuleException>(() => set.Supersede(Now));
        Assert.Equal("duplicate_candidate_set_closed", supersedeException.Code);
    }

    [Fact]
    public void A_detection_run_is_bounded()
    {
        List<DuplicateCandidate> candidates =
        [
            .. Enumerable.Range(0, DuplicateMatchRules.MaximumCandidates + 1).Select(index => DuplicateCandidate.Create(
                "party-" + index.ToString("D3", System.Globalization.CultureInfo.InvariantCulture),
                [DuplicateMatchFactor.Create(DuplicateMatchFactorKind.ContactPoint, "email")],
                Now)),
        ];

        OnboardingRuleException exception = Assert.Throws<OnboardingRuleException>(
            () => DuplicateCandidateSet.Open(
                InstitutionId.From("institution-1"),
                "application-1",
                2,
                "reviewer-1",
                candidates,
                Now));

        Assert.Equal("duplicate_candidates_too_many", exception.Code);
    }

    private static DuplicateCandidateSet OpenSet(int candidates)
    {
        List<DuplicateCandidate> detected =
        [
            .. Enumerable.Range(0, candidates).Select(index => DuplicateCandidate.Create(
                "candidate-" + index.ToString(System.Globalization.CultureInfo.InvariantCulture),
                [DuplicateMatchFactor.Create(DuplicateMatchFactorKind.ContactPoint, "email")],
                Now)),
        ];

        return DuplicateCandidateSet.Open(
            InstitutionId.From("institution-1"),
            "application-1",
            2,
            "reviewer-1",
            detected,
            Now);
    }

    private static DuplicateSubject Subject(
        string partyId,
        string name = "GRACE HOPPER",
        string birthDateToken = "other-birth-token",
        int birthYear = 1906,
        string? email = null,
        string? mobile = null,
        string? address = null) =>
        new(
            partyId,
            name,
            birthDateToken,
            birthYear,
            [
                .. new[] { email is null ? null : new DuplicateContactToken("email", email, "m***@example.com"), mobile is null ? null : new DuplicateContactToken("mobile", mobile, "+234***678") }
                    .Where(contact => contact is not null)
                    .Select(contact => contact!),
            ],
            [.. new[] { address is null ? null : new DuplicateAddressToken(address, "*** Way") }.Where(entry => entry is not null).Select(entry => entry!)]);
}
