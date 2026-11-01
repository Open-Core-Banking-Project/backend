using System;

namespace CoreBanking.CustomerOnboarding.Domain.Parties;

/// <summary>
/// Date of birth and optional country of birth. Evaluation is explicit about the reference date so
/// the rule is deterministic in tests and in replay. Decision: CUS-UC-01.
/// </summary>
public sealed record BirthDetails
{
    private const int MaximumAge = 130;

    private BirthDetails(DateOnly birthDate, string? countryOfBirth)
    {
        BirthDate = birthDate;
        CountryOfBirth = countryOfBirth;
    }

    public DateOnly BirthDate { get; }

    /// <summary>ISO 3166-1 alpha-2 code, or null when the country was not captured.</summary>
    public string? CountryOfBirth { get; }

    public static BirthDetails Create(DateOnly birthDate, string? countryOfBirth, DateOnly asOf)
    {
        if (birthDate == default)
        {
            throw new OnboardingRuleException("onboarding_required_field_missing", "A date of birth is required.");
        }

        if (birthDate > asOf)
        {
            throw new OnboardingRuleException("onboarding_field_invalid", "A date of birth cannot be in the future.");
        }

        if (birthDate < asOf.AddYears(-MaximumAge))
        {
            throw new OnboardingRuleException("onboarding_field_invalid", $"A date of birth cannot be more than {MaximumAge} years before the evaluation date.");
        }

        string? country = string.IsNullOrWhiteSpace(countryOfBirth) ? null : countryOfBirth.Trim().ToUpperInvariant();
        if (country is not null && (country.Length != 2 || !char.IsAsciiLetter(country[0]) || !char.IsAsciiLetter(country[1])))
        {
            throw new OnboardingRuleException("onboarding_field_invalid", "A country of birth must be an ISO 3166-1 alpha-2 code.");
        }

        return new BirthDetails(birthDate, country);
    }
}
