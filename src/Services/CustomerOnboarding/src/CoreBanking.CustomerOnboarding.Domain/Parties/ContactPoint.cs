namespace CoreBanking.CustomerOnboarding.Domain.Parties;

/// <summary>Supported contact point kinds. Decision: CUS-UC-01, privacy controls.</summary>
public enum ContactPointType
{
    Mobile,
    Email,
    Landline,
}

/// <summary>
/// A captured contact point before protection. The value is plaintext only inside the request that
/// captured it; persistence stores the protected token, the masked display value and the lookup
/// token produced by <c>IProtectedValueProtector</c>. Decision: CUS-028.
/// </summary>
public sealed record ContactPoint(ContactPointType Type, string Value)
{
    private const int MinimumPhoneDigits = 7;
    private const int MaximumPhoneDigits = 20;
    private const int MaximumEmailLength = 254;

    public static ContactPoint Create(ContactPointType type, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new OnboardingRuleException("onboarding_required_field_missing", "A contact point value is required.");
        }

        string trimmed = value.Trim();
        switch (type)
        {
            case ContactPointType.Email:
                EnsureEmail(trimmed);
                break;
            case ContactPointType.Mobile:
            case ContactPointType.Landline:
                EnsurePhone(trimmed);
                break;
            default:
                throw new OnboardingRuleException("onboarding_field_invalid", $"Contact point type '{type}' is not supported.");
        }

        return new ContactPoint(type, trimmed);
    }

    private static void EnsureEmail(string value)
    {
        int at = value.IndexOf('@', System.StringComparison.Ordinal);
        bool valid = value.Length <= MaximumEmailLength
            && at > 0
            && at < value.Length - 3
            && value.IndexOf('@', at + 1) < 0
            && value.IndexOf(' ', System.StringComparison.Ordinal) < 0
            && value.IndexOf('.', at) > at + 1;

        if (!valid)
        {
            throw new OnboardingRuleException("onboarding_field_invalid", "An email contact point is not a valid address.");
        }
    }

    private static void EnsurePhone(string value)
    {
        int digits = 0;
        for (int index = 0; index < value.Length; index++)
        {
            char character = value[index];
            if (char.IsAsciiDigit(character))
            {
                digits++;
                continue;
            }

            bool allowedSeparator = (character == '+' && index == 0) || character is '-' or ' ' or '(' or ')';
            if (!allowedSeparator)
            {
                throw new OnboardingRuleException("onboarding_field_invalid", "A phone contact point may contain digits, spaces, '+', '-' and parentheses.");
            }
        }

        if (digits < MinimumPhoneDigits || digits > MaximumPhoneDigits)
        {
            throw new OnboardingRuleException("onboarding_field_invalid", "A phone contact point must contain a plausible number of digits.");
        }
    }
}
