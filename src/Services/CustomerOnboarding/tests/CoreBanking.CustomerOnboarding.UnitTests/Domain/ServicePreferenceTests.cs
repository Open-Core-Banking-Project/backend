using System;
using CoreBanking.BuildingBlocks.Domain.Identity;
using CoreBanking.CustomerOnboarding.Domain;
using CoreBanking.CustomerOnboarding.Domain.Privacy;
using Xunit;

namespace CoreBanking.CustomerOnboarding.UnitTests.Domain;

/// <summary>
/// Preference version rules: approved codes only, a lawful basis for every grant, and a version that can be
/// closed exactly once. Decision: P1-E03-D12.
/// </summary>
public sealed class ServicePreferenceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 29, 16, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_granted_preference_requires_a_lawful_basis()
    {
        OnboardingRuleException exception = Assert.Throws<OnboardingRuleException>(
            () => Create(PreferenceState.Permitted, lawfulBasis: null));

        Assert.Equal("consent_lawful_basis_required", exception.Code);
    }

    [Fact]
    public void A_denial_needs_no_lawful_basis()
    {
        ServicePreference preference = Create(PreferenceState.Denied, lawfulBasis: null);

        Assert.Equal(PreferenceState.Denied, preference.State);
        Assert.Null(preference.LawfulBasis);
        Assert.True(preference.IsCurrent);
    }

    [Fact]
    public void Purpose_channel_and_source_must_be_stable_codes()
    {
        Assert.Equal(
            "preference_code_invalid",
            Assert.Throws<OnboardingRuleException>(
                () => ServicePreference.Create(
                    InstitutionId.From("institution-1"),
                    "customer-1",
                    "Marketing Email",
                    "mobile_app",
                    1,
                    PreferenceState.Denied,
                    lawfulBasis: null,
                    "customer",
                    evidenceReference: null,
                    reason: null,
                    "customer-1",
                    Now)).Code);

        Assert.Equal(
            "preference_code_invalid",
            Assert.Throws<OnboardingRuleException>(
                () => ServicePreference.Create(
                    InstitutionId.From("institution-1"),
                    "customer-1",
                    "marketing",
                    "mobile_app",
                    1,
                    PreferenceState.Denied,
                    lawfulBasis: null,
                    "unknown source",
                    evidenceReference: null,
                    reason: null,
                    "customer-1",
                    Now)).Code);
    }

    [Fact]
    public void A_version_starts_at_one()
    {
        Assert.Equal(
            "preference_version_invalid",
            Assert.Throws<OnboardingRuleException>(() => Create(PreferenceState.Denied, lawfulBasis: null, version: 0)).Code);
    }

    [Fact]
    public void A_version_can_be_closed_only_once()
    {
        ServicePreference preference = Create(PreferenceState.Permitted, "consent");

        preference.Close(Now.AddMinutes(5));

        Assert.False(preference.IsCurrent);
        Assert.Equal(Now.AddMinutes(5), preference.EffectiveTo);
        Assert.Equal(
            "preference_version_closed",
            Assert.Throws<OnboardingRuleException>(() => preference.Close(Now.AddMinutes(9))).Code);
    }

    [Fact]
    public void A_version_cannot_close_before_it_starts()
    {
        ServicePreference preference = Create(PreferenceState.Denied, lawfulBasis: null);

        Assert.Equal(
            "preference_version_invalid",
            Assert.Throws<OnboardingRuleException>(() => preference.Close(Now.AddMinutes(-1))).Code);
    }

    [Fact]
    public void Codes_round_trip()
    {
        Assert.Equal("permitted", PreferenceCodes.ToCode(PreferenceState.Permitted));
        Assert.Equal(PreferenceState.Denied, PreferenceCodes.FromStateCode("denied"));
        Assert.Equal(
            "preference_state_invalid",
            Assert.Throws<OnboardingRuleException>(() => PreferenceCodes.FromStateCode("maybe")).Code);
    }

    private static ServicePreference Create(
        PreferenceState state,
        string? lawfulBasis,
        int version = 1) =>
        ServicePreference.Create(
            InstitutionId.From("institution-1"),
            "customer-1",
            "marketing",
            "mobile_app",
            version,
            state,
            lawfulBasis,
            "customer",
            evidenceReference: null,
            reason: null,
            "customer-1",
            Now);
}
