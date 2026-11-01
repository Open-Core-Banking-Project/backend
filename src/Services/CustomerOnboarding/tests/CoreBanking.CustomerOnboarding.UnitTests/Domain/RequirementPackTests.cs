using System;
using System.Collections.Generic;
using System.Linq;
using CoreBanking.BuildingBlocks.Domain.Identity;
using CoreBanking.CustomerOnboarding.Domain;
using CoreBanking.CustomerOnboarding.Domain.Requirements;
using Xunit;

namespace CoreBanking.CustomerOnboarding.UnitTests.Domain;

/// <summary>
/// Requirement packs are institution data with a stable content hash, and a snapshot cites the exact pack
/// it was captured from. Decision: P1-E03-D01.
/// </summary>
public sealed class RequirementPackTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 29, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void The_content_hash_is_stable_and_covers_every_requirement_detail()
    {
        OnboardingRequirement[] requirements =
        [
            OnboardingRequirement.Create("contact_detail", "Contact detail", true, ["email", "mobile"]),
            OnboardingRequirement.Create("address_detail", "Address detail", false, []),
        ];

        RequirementPack first = RequirementPack.Create("phase1-test", 1, requirements);
        RequirementPack again = RequirementPack.Create("phase1-test", 1, requirements);

        Assert.Equal(first.ContentHash, again.ContentHash);
        Assert.Equal(64, first.ContentHash.Length);

        RequirementPack reordered = RequirementPack.Create("phase1-test", 1, [.. requirements.Reverse()]);
        RequirementPack notMandatory = RequirementPack.Create(
            "phase1-test",
            1,
            [OnboardingRequirement.Create("contact_detail", "Contact detail", false, ["email", "mobile"]), requirements[1]]);
        RequirementPack otherVersion = RequirementPack.Create("phase1-test", 2, requirements);

        Assert.NotEqual(first.ContentHash, reordered.ContentHash);
        Assert.NotEqual(first.ContentHash, notMandatory.ContentHash);
        Assert.NotEqual(first.ContentHash, otherVersion.ContentHash);
    }

    [Fact]
    public void A_requirement_code_may_appear_only_once()
    {
        OnboardingRequirement duplicate = OnboardingRequirement.Create("contact_detail", "Contact detail", true, []);

        OnboardingRuleException exception = Assert.Throws<OnboardingRuleException>(
            () => RequirementPack.Create("phase1-test", 1, [duplicate, duplicate]));

        Assert.Equal("requirement_pack_invalid", exception.Code);
    }

    [Fact]
    public void A_requirement_needs_a_stable_code_a_label_and_code_shaped_evidence()
    {
        Assert.Equal(
            "requirement_code_invalid",
            Assert.Throws<OnboardingRuleException>(() => OnboardingRequirement.Create("Contact Detail", "Contact", true, [])).Code);
        Assert.Equal(
            "requirement_label_invalid",
            Assert.Throws<OnboardingRuleException>(() => OnboardingRequirement.Create("contact_detail", "  ", true, [])).Code);
        Assert.Equal(
            "requirement_evidence_invalid",
            Assert.Throws<OnboardingRuleException>(() => OnboardingRequirement.Create("contact_detail", "Contact", true, ["Email Document"])).Code);
    }

    [Fact]
    public void A_pack_version_starts_at_one()
    {
        Assert.Equal(
            "requirement_pack_invalid",
            Assert.Throws<OnboardingRuleException>(() => RequirementPack.Create("phase1-test", 0, [])).Code);
    }

    [Fact]
    public void A_snapshot_cites_the_pack_version_and_hash_it_was_captured_from()
    {
        RequirementPack pack = RequirementPack.Create(
            "phase1-test",
            3,
            [OnboardingRequirement.Create("contact_detail", "Contact detail", true, ["email"])]);

        RequirementSnapshot snapshot = RequirementSnapshot.Capture(
            InstitutionId.From("institution-1"),
            "application-1",
            2,
            pack,
            "officer-1",
            Now);

        Assert.Equal("phase1-test", snapshot.PackCode);
        Assert.Equal(3, snapshot.PackVersion);
        Assert.Equal(pack.ContentHash, snapshot.ContentHash);
        Assert.Equal(2, snapshot.ApplicationRevision);
        Assert.Equal("application-1", snapshot.ApplicationId);
        Assert.Equal("officer-1", snapshot.CapturedBy);
        Assert.Equal(Now, snapshot.CapturedAt);
        Assert.Single(snapshot.MandatoryRequirements);
    }

    [Fact]
    public void A_snapshot_requires_a_recorded_application_revision()
    {
        RequirementPack pack = RequirementPack.Create("phase1-test", 1, []);

        OnboardingRuleException exception = Assert.Throws<OnboardingRuleException>(
            () => RequirementSnapshot.Capture(InstitutionId.From("institution-1"), "application-1", 0, pack, "officer-1", Now));

        Assert.Equal("onboarding_revision_conflict", exception.Code);
    }
}
