using System.Linq;

namespace CoreBanking.CustomerOnboarding.Domain.Parties;

/// <summary>Supported address kinds. Decision: CUS-UC-01.</summary>
public enum AddressType
{
    Residential,
    Postal,
    Business,
}

/// <summary>
/// A captured address. Country is an ISO 3166-1 alpha-2 code; the free-form lines stay short enough
/// that they cannot be used to smuggle document content or binary evidence.
/// Decision: CUS-UC-01, CUS-028.
/// </summary>
public sealed record Address
{
    private const int MaximumLineLength = 200;

    private Address(AddressType type, string line1, string? line2, string city, string? region, string countryCode)
    {
        Type = type;
        Line1 = line1;
        Line2 = line2;
        City = city;
        Region = region;
        CountryCode = countryCode;
    }

    public AddressType Type { get; }

    public string Line1 { get; }

    public string? Line2 { get; }

    public string City { get; }

    public string? Region { get; }

    public string CountryCode { get; }

    public static Address Create(AddressType type, string line1, string? line2, string city, string? region, string countryCode)
    {
        string country = Required(countryCode, "countryCode").ToUpperInvariant();
        if (country.Length != 2 || !char.IsAsciiLetter(country[0]) || !char.IsAsciiLetter(country[1]))
        {
            throw new OnboardingRuleException("onboarding_field_invalid", "An address country must be an ISO 3166-1 alpha-2 code.");
        }

        return new Address(
            type,
            Required(line1, "line1"),
            string.IsNullOrWhiteSpace(line2) ? null : Required(line2, "line2"),
            Required(city, "city"),
            string.IsNullOrWhiteSpace(region) ? null : Required(region, "region"),
            country);
    }

    private static string Required(string value, string field)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new OnboardingRuleException("onboarding_required_field_missing", $"An address requires {field}.");
        }

        string trimmed = value.Trim();
        if (trimmed.Length > MaximumLineLength || trimmed.Any(char.IsControl))
        {
            throw new OnboardingRuleException("onboarding_field_invalid", $"Address {field} is not a valid text value.");
        }

        return trimmed;
    }
}
