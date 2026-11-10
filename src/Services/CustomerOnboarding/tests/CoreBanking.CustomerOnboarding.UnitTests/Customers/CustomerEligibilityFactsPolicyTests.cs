using System;
using CoreBanking.CustomerOnboarding.Application.Customers;
using Xunit;

namespace CoreBanking.CustomerOnboarding.UnitTests.Customers;

/// <summary>
/// The facts snapshot is only trustworthy if effective selection, age and the validity window are
/// computed deterministically. Decision: P1-E04-D17.
/// </summary>
public sealed class CustomerEligibilityFactsPolicyTests
{
    private static readonly DateTimeOffset AsOf = new(2026, 11, 6, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("2000-11-06", 26)]
    [InlineData("2000-11-07", 25)]
    [InlineData("2008-11-06", 18)]
    [InlineData("2008-11-07", 17)]
    public void Age_is_the_completed_years_at_the_instant(string birthDate, int expected)
    {
        int age = CustomerEligibilityFactsPolicy.ComputeAgeYears(
            DateOnly.Parse(birthDate, System.Globalization.CultureInfo.InvariantCulture),
            new DateOnly(2026, 11, 6));

        Assert.Equal(expected, age);
    }

    [Fact]
    public void A_leap_day_birth_date_ages_on_the_first_of_march_in_a_non_leap_year()
    {
        DateOnly birthDate = new(2000, 2, 29);

        Assert.Equal(25, CustomerEligibilityFactsPolicy.ComputeAgeYears(birthDate, new DateOnly(2026, 2, 28)));
        Assert.Equal(26, CustomerEligibilityFactsPolicy.ComputeAgeYears(birthDate, new DateOnly(2026, 3, 1)));
    }

    [Fact]
    public void A_future_birth_date_is_invalid_rather_than_a_negative_age()
    {
        CustomerEligibilityFactsException exception = Assert.Throws<CustomerEligibilityFactsException>(
            () => CustomerEligibilityFactsPolicy.ComputeAgeYears(new DateOnly(2026, 11, 7), new DateOnly(2026, 11, 6)));

        Assert.Equal("customer_birth_date_invalid", exception.Code);
    }

    [Fact]
    public void Only_an_approved_classification_effective_at_the_instant_qualifies()
    {
        CustomerClassificationFacts[] classifications =
        [
            Classification(version: 2, status: "proposed", approvedAt: null),
            Classification(version: 3, status: "approved", approvedAt: AsOf.AddDays(1)),
            Classification(version: 1, status: "approved", approvedAt: AsOf.AddDays(-10)),
        ];

        CustomerClassificationFacts? effective = CustomerEligibilityFactsPolicy.SelectEffective(classifications, AsOf);

        Assert.NotNull(effective);
        Assert.Equal(1, effective!.Version);
    }

    [Fact]
    public void The_highest_approved_version_wins()
    {
        CustomerClassificationFacts[] classifications =
        [
            Classification(version: 1, status: "approved", approvedAt: AsOf.AddDays(-10)),
            Classification(version: 2, status: "approved", approvedAt: AsOf.AddHours(-2)),
            Classification(version: 3, status: "superseded", approvedAt: AsOf.AddHours(-1)),
        ];

        CustomerClassificationFacts? effective = CustomerEligibilityFactsPolicy.SelectEffective(classifications, AsOf);

        Assert.Equal(2, effective!.Version);
    }

    [Fact]
    public void No_approved_classification_means_no_effective_facts()
    {
        Assert.Null(CustomerEligibilityFactsPolicy.SelectEffective(
            [Classification(version: 1, status: "proposed", approvedAt: null)],
            AsOf));
    }

    [Fact]
    public void The_facts_version_and_validity_window_are_stable()
    {
        Assert.Equal("c12", CustomerEligibilityFactsPolicy.FactsVersion(12));
        Assert.Equal(AsOf.AddSeconds(60), CustomerEligibilityFactsPolicy.ValidUntil(AsOf));
        Assert.Equal(TimeSpan.FromSeconds(60), CustomerEligibilityFactsPolicy.ValidityWindow);
    }

    private static CustomerClassificationFacts Classification(int version, string status, DateTimeOffset? approvedAt) =>
        new(version, "KYC-2", "RETAIL", "LOW", false, status, approvedAt);
}
