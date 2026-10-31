using System;
using CoreBanking.CustomerOnboarding.Domain;
using CoreBanking.CustomerOnboarding.Domain.Onboarding;
using CoreBanking.CustomerOnboarding.Domain.Parties;
using Xunit;

namespace CoreBanking.CustomerOnboarding.UnitTests.Domain;

/// <summary>
/// Capture validation and lifecycle rules. These are the rules an onboarding officer feels as a
/// problem detail, so each failure is asserted through its stable code. Decision: CUS-UC-01.
/// </summary>
public sealed class OnboardingDomainTests
{
    private static readonly DateOnly Today = new(2026, 10, 28);

    [Fact]
    public void A_name_is_normalised_for_matching_and_keeps_its_display_case()
    {
        PersonName name = PersonName.Create("  Ada ", "  ", "Lovelace");

        Assert.Equal("Ada", name.GivenName);
        Assert.Null(name.MiddleName);
        Assert.Equal("Ada Lovelace", name.ToString());
        Assert.Equal("ADA LOVELACE", name.Normalised);
    }

    [Fact]
    public void A_missing_given_name_is_a_missing_required_field()
    {
        OnboardingRuleException exception = Assert.Throws<OnboardingRuleException>(
            () => PersonName.Create("   ", null, "Lovelace"));

        Assert.Equal("onboarding_required_field_missing", exception.Code);
    }

    [Fact]
    public void A_name_with_a_control_character_is_rejected()
    {
        OnboardingRuleException exception = Assert.Throws<OnboardingRuleException>(
            () => PersonName.Create("Ada\u0007", null, "Lovelace"));

        Assert.Equal("onboarding_field_invalid", exception.Code);
    }

    [Theory]
    [InlineData(ContactPointType.Email, "ada@example.com")]
    [InlineData(ContactPointType.Mobile, "+234 801 234 5678")]
    [InlineData(ContactPointType.Landline, "(01) 234-5678")]
    public void A_plausible_contact_point_is_accepted(ContactPointType type, string value) =>
        Assert.Equal(value, ContactPoint.Create(type, value).Value);

    [Theory]
    [InlineData(ContactPointType.Email, "ada.example.com")]
    [InlineData(ContactPointType.Email, "ada@@example.com")]
    [InlineData(ContactPointType.Mobile, "12345")]
    [InlineData(ContactPointType.Mobile, "+234 801 234 5678x")]
    public void An_implausible_contact_point_is_rejected(ContactPointType type, string value)
    {
        OnboardingRuleException exception = Assert.Throws<OnboardingRuleException>(
            () => ContactPoint.Create(type, value));

        Assert.Equal("onboarding_field_invalid", exception.Code);
    }

    [Fact]
    public void A_birth_date_in_the_future_is_rejected()
    {
        OnboardingRuleException exception = Assert.Throws<OnboardingRuleException>(
            () => BirthDetails.Create(Today.AddDays(1), null, Today));

        Assert.Equal("onboarding_field_invalid", exception.Code);
    }

    [Fact]
    public void A_birth_date_beyond_the_supported_age_is_rejected()
    {
        OnboardingRuleException exception = Assert.Throws<OnboardingRuleException>(
            () => BirthDetails.Create(Today.AddYears(-131), null, Today));

        Assert.Equal("onboarding_field_invalid", exception.Code);
    }

    [Fact]
    public void A_country_of_birth_must_be_an_alpha_two_code()
    {
        Assert.Equal("NG", BirthDetails.Create(Today.AddYears(-30), "ng", Today).CountryOfBirth);

        Assert.Throws<OnboardingRuleException>(() => BirthDetails.Create(Today.AddYears(-30), "NGA", Today));
    }

    [Fact]
    public void An_address_requires_a_country_code_and_the_first_line() =>
        Assert.Throws<OnboardingRuleException>(() => Address.Create(AddressType.Residential, " ", null, "Lagos", null, "NG"));

    [Fact]
    public void An_empty_capture_reports_every_missing_field_group()
    {
        Assert.Equal(
            ["legalName", "birthDate", "contacts", "addresses"],
            OnboardingCapture.Empty.MissingFields());
        Assert.False(OnboardingCapture.Empty.IsComplete);
    }

    [Fact]
    public void A_complete_capture_reports_nothing_missing() =>
        Assert.True(CompleteCapture().IsComplete);

    [Fact]
    public void An_application_starts_as_a_draft_at_revision_one()
    {
        OnboardingApplication application = Start();

        Assert.Equal(ApplicationStatus.Draft, application.Status);
        Assert.Equal(1, application.Revision);
        Assert.Equal(1L, application.ConcurrencyVersion);
        Assert.Null(application.PartyId);
        Assert.Null(application.SubmittedAt);
    }

    [Fact]
    public void Revising_a_draft_records_a_new_revision_and_version()
    {
        OnboardingApplication application = Start();

        application.Revise(Now.AddMinutes(5));

        Assert.Equal(2, application.Revision);
        Assert.Equal(2L, application.ConcurrencyVersion);
        Assert.Equal(Now.AddMinutes(5), application.UpdatedAt);
    }

    [Fact]
    public void Submitting_records_the_party_and_a_new_revision()
    {
        OnboardingApplication application = Start();
        application.Revise(Now.AddMinutes(1));

        application.Submit("party-1", "snapshot-1", Now.AddMinutes(2));

        Assert.Equal(ApplicationStatus.Submitted, application.Status);
        Assert.Equal("party-1", application.PartyId);
        Assert.Equal(3, application.Revision);
        Assert.Equal(Now.AddMinutes(2), application.SubmittedAt);
        Assert.Equal(3L, application.ConcurrencyVersion);
    }

    [Fact]
    public void A_submitted_application_cannot_be_revised_or_submitted_again()
    {
        OnboardingApplication application = Start();
        application.Submit("party-1", "snapshot-1", Now);

        Assert.Equal(
            "onboarding_revision_conflict",
            Assert.Throws<OnboardingRuleException>(() => application.Revise(Now)).Code);
        Assert.Equal(
            "onboarding_revision_conflict",
            Assert.Throws<OnboardingRuleException>(() => application.Submit("party-2", "snapshot-2", Now)).Code);
    }

    [Fact]
    public void A_submitted_application_can_be_withdrawn_once()
    {
        OnboardingApplication application = Start();
        application.Submit("party-1", "snapshot-1", Now);

        application.Withdraw(Now.AddMinutes(1));

        Assert.Equal(ApplicationStatus.Withdrawn, application.Status);
        Assert.Equal(Now.AddMinutes(1), application.WithdrawnAt);
        Assert.Equal(
            "onboarding_revision_conflict",
            Assert.Throws<OnboardingRuleException>(() => application.Withdraw(Now.AddMinutes(2))).Code);
    }

    private static DateTimeOffset Now { get; } = new(2026, 10, 28, 9, 0, 0, TimeSpan.Zero);

    private static OnboardingApplication Start() =>
        OnboardingApplication.Start(
            CoreBanking.BuildingBlocks.Domain.Identity.InstitutionId.From("institution-1"),
            "APP-0001",
            "branch-1",
            Now);

    private static OnboardingCapture CompleteCapture() =>
        new(
            PersonName.Create("Ada", null, "Lovelace"),
            BirthDetails.Create(new DateOnly(1990, 5, 12), "GB", Today),
            [ContactPoint.Create(ContactPointType.Email, "ada@example.com")],
            [Address.Create(AddressType.Residential, "12 Analytical Engine Way", null, "London", null, "GB")]);
}
