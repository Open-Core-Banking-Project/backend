using System;
using System.Linq;

namespace CoreBanking.CustomerOnboarding.Domain.Parties;

/// <summary>
/// A person's legal name. <see cref="GivenName"/> and <see cref="FamilyName"/> are required;
/// a middle name is optional and an absent one is stored as null rather than an empty string.
/// <see cref="Normalised"/> is the comparison form used by duplicate detection and is never
/// displayed. Decision: docs/implementation/phase-1-banking-foundation/03-customer-and-onboarding.md
/// </summary>
public sealed record PersonName
{
    private const int MaximumLength = 200;

    private PersonName(string givenName, string? middleName, string familyName, string normalised)
    {
        GivenName = givenName;
        MiddleName = middleName;
        FamilyName = familyName;
        Normalised = normalised;
    }

    public string GivenName { get; }

    public string? MiddleName { get; }

    public string FamilyName { get; }

    /// <summary>Upper-invariant, whitespace-collapsed form used for matching only.</summary>
    public string Normalised { get; }

    public static PersonName Create(string givenName, string? middleName, string familyName)
    {
        string given = Required(givenName, nameof(givenName));
        string family = Required(familyName, nameof(familyName));
        string? middle = string.IsNullOrWhiteSpace(middleName) ? null : Optional(middleName, nameof(middleName));

        string normalised = string.Join(
            ' ',
            new[] { given, middle, family }
                .Where(part => !string.IsNullOrWhiteSpace(part))
                .Select(part => part!.ToUpperInvariant()));

        return new PersonName(given, middle, family, normalised);
    }

    public override string ToString() =>
        string.IsNullOrWhiteSpace(MiddleName) ? $"{GivenName} {FamilyName}" : $"{GivenName} {MiddleName} {FamilyName}";

    private static string Required(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new OnboardingRuleException("onboarding_required_field_missing", $"A value is required for {parameterName}.");
        }

        return Optional(value, parameterName);
    }

    private static string Optional(string value, string parameterName)
    {
        string trimmed = value.Trim();
        if (trimmed.Length > MaximumLength)
        {
            throw new OnboardingRuleException("onboarding_field_too_long", $"{parameterName} must be at most {MaximumLength} characters.");
        }

        foreach (char character in trimmed)
        {
            if (char.IsControl(character))
            {
                throw new OnboardingRuleException("onboarding_field_invalid", $"{parameterName} contains a control character.");
            }
        }

        return trimmed;
    }
}
