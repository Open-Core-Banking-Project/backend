using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using CoreBanking.CoreLedger.Domain.Institutions;

namespace CoreBanking.CoreLedger.Domain.Accounts;

public enum AccountNumberCheckDigit
{
    None,
    Mod10Luhn,
    Mod11Weighted21,
}

/// <summary>
/// The approved account-number scheme of one institution: allow-listed segments, a declared sequence
/// width and an allow-listed check-digit algorithm. No default scheme exists and no number may encode
/// personal data. Decision: P1-E04-D06.
/// </summary>
public sealed record AccountNumberScheme(
    string SchemeId,
    int Version,
    string Prefix,
    int ProductCodeWidth,
    int BranchCodeWidth,
    int SequenceWidth,
    AccountNumberCheckDigit CheckDigit)
{
    public const string Area = "institution.account-numbering";
    public const string Schema = "institution.account-numbering.v1";

    private const int MaximumPrefixLength = 6;
    private const int MinimumSequenceWidth = 6;
    private const int MaximumSequenceWidth = 12;
    private const int MaximumCodeWidth = 12;

    private static readonly HashSet<string> AllowedProperties = new(StringComparer.Ordinal)
    {
        "schemeId", "version", "prefix", "productCodeWidth", "branchCodeWidth", "sequenceWidth", "checkDigit",
    };

    /// <summary>How many numbers the declared sequence width can issue before it is exhausted.</summary>
    public long Capacity
    {
        get
        {
            long capacity = 1;
            for (int index = 0; index < SequenceWidth; index++)
            {
                capacity *= 10;
            }

            return capacity;
        }
    }

    public ConfigurationContent ToConfigurationContent() => ConfigurationContent.Create(BuildJson(
        SchemeId, Version, Prefix, ProductCodeWidth, BranchCodeWidth, SequenceWidth, CheckDigit));

    public static string BuildJson(
        string schemeId,
        int version,
        string prefix,
        int productCodeWidth,
        int branchCodeWidth,
        int sequenceWidth,
        AccountNumberCheckDigit checkDigit) =>
        JsonSerializer.Serialize(new
        {
            schemeId,
            version,
            prefix = prefix ?? string.Empty,
            productCodeWidth,
            branchCodeWidth,
            sequenceWidth,
            checkDigit = ToToken(checkDigit),
        });

    public static AccountNumberScheme Parse(string contentJson)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentJson);

        using JsonDocument document = JsonDocument.Parse(contentJson);
        JsonElement root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object
            || root.EnumerateObject().Any(property => !AllowedProperties.Contains(property.Name)))
            throw new ArgumentException("The account-number scheme contains unsupported properties.", nameof(contentJson));

        foreach (string required in AllowedProperties)
        {
            if (!root.TryGetProperty(required, out _))
                throw new ArgumentException("The account-number scheme is missing a required property.", nameof(contentJson));
        }

        return Create(
            root.GetProperty("schemeId").GetString() ?? string.Empty,
            root.GetProperty("version").GetInt32(),
            root.GetProperty("prefix").GetString() ?? string.Empty,
            root.GetProperty("productCodeWidth").GetInt32(),
            root.GetProperty("branchCodeWidth").GetInt32(),
            root.GetProperty("sequenceWidth").GetInt32(),
            ParseCheckDigit(root.GetProperty("checkDigit").GetString()));
    }

    public static AccountNumberScheme Create(
        string schemeId,
        int version,
        string prefix,
        int productCodeWidth,
        int branchCodeWidth,
        int sequenceWidth,
        AccountNumberCheckDigit checkDigit)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(schemeId);
        if (schemeId.Trim().Length > 64)
            throw new ArgumentException("A scheme identifier may contain at most 64 characters.", nameof(schemeId));
        ArgumentOutOfRangeException.ThrowIfLessThan(version, 1);
        if (!Enum.IsDefined(checkDigit))
            throw new ArgumentException("The check-digit algorithm is not approved.", nameof(checkDigit));

        string normalisedPrefix = (prefix ?? string.Empty).Trim();
        if (normalisedPrefix.Length > MaximumPrefixLength || !normalisedPrefix.All(char.IsAsciiDigit))
            throw new ArgumentException($"A prefix must be at most {MaximumPrefixLength} digits.", nameof(prefix));

        if (productCodeWidth is < 0 or > MaximumCodeWidth)
            throw new ArgumentOutOfRangeException(nameof(productCodeWidth), "A product segment width must be between 0 and 12.");
        if (branchCodeWidth is < 0 or > MaximumCodeWidth)
            throw new ArgumentOutOfRangeException(nameof(branchCodeWidth), "A branch segment width must be between 0 and 12.");
        if (sequenceWidth is < MinimumSequenceWidth or > MaximumSequenceWidth)
            throw new ArgumentOutOfRangeException(nameof(sequenceWidth), "A sequence width must be between 6 and 12.");

        return new AccountNumberScheme(
            schemeId.Trim(),
            version,
            normalisedPrefix,
            productCodeWidth,
            branchCodeWidth,
            sequenceWidth,
            checkDigit);
    }

    public static string ToToken(AccountNumberCheckDigit checkDigit) => checkDigit switch
    {
        AccountNumberCheckDigit.None => "none",
        AccountNumberCheckDigit.Mod10Luhn => "mod10_luhn",
        AccountNumberCheckDigit.Mod11Weighted21 => "mod11_weighted_2_1",
        _ => throw new ArgumentException("The check-digit algorithm is not approved.", nameof(checkDigit)),
    };

    public static AccountNumberCheckDigit ParseCheckDigit(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "none" => AccountNumberCheckDigit.None,
        "mod10_luhn" => AccountNumberCheckDigit.Mod10Luhn,
        "mod11_weighted_2_1" => AccountNumberCheckDigit.Mod11Weighted21,
        _ => throw new ArgumentException("The check-digit algorithm is not approved.", nameof(value)),
    };

    /// <summary>
    /// Formats one number from the declared segments. Segment values must be exactly the declared
    /// width and numeric, so a scheme can never silently absorb a foreign or non-numeric code.
    /// </summary>
    public string Format(string productCode, string branchCode, long sequence)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(sequence);
        if (sequence >= Capacity)
            throw new InvalidOperationException("account_number_sequence_out_of_range");

        string productSegment = Project(productCode, ProductCodeWidth, "product");
        string branchSegment = Project(branchCode, BranchCodeWidth, "branch");
        string sequenceSegment = sequence.ToString(CultureInfo.InvariantCulture).PadLeft(SequenceWidth, '0');
        string body = string.Concat(Prefix, productSegment, branchSegment, sequenceSegment);
        return CheckDigit == AccountNumberCheckDigit.None
            ? body
            : string.Concat(body, ComputeCheckDigit(body));
    }

    /// <summary>Masks all but the four significant digits that precede any check digit.</summary>
    public string Mask(string accountNumber)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountNumber);
        int significant = CheckDigit == AccountNumberCheckDigit.None
            ? accountNumber.Length
            : accountNumber.Length - 1;
        if (significant <= 4)
        {
            return accountNumber;
        }

        string visible = accountNumber.Substring(significant - 4, 4);
        string tail = CheckDigit == AccountNumberCheckDigit.None ? string.Empty : accountNumber[^1..];
        return string.Concat(new string('*', significant - 4), visible, tail);
    }

    /// <summary>Verifies the check digit where the scheme declares one. A scheme with none is never "verified".</summary>
    public bool Verify(string accountNumber)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountNumber);
        if (CheckDigit == AccountNumberCheckDigit.None)
        {
            return false;
        }

        if (accountNumber.Length < 2 || !accountNumber.All(char.IsAsciiDigit))
        {
            return false;
        }

        string body = accountNumber[..^1];
        return string.Equals(ComputeCheckDigit(body), accountNumber[^1..], StringComparison.Ordinal);
    }

    private string ComputeCheckDigit(string body) => CheckDigit switch
    {
        AccountNumberCheckDigit.None => string.Empty,
        AccountNumberCheckDigit.Mod10Luhn => Mod10Luhn(body).ToString(CultureInfo.InvariantCulture),
        AccountNumberCheckDigit.Mod11Weighted21 => Mod11Weighted21(body).ToString(CultureInfo.InvariantCulture),
        _ => throw new InvalidOperationException("account_number_check_digit_unsupported"),
    };

    private static int Mod10Luhn(string digits)
    {
        int sum = 0;
        bool doubling = true;
        for (int index = digits.Length - 1; index >= 0; index--)
        {
            int value = digits[index] - '0';
            if (doubling)
            {
                value *= 2;
                if (value > 9)
                {
                    value -= 9;
                }
            }

            sum += value;
            doubling = !doubling;
        }

        return (10 - (sum % 10)) % 10;
    }

    private static int Mod11Weighted21(string digits)
    {
        int sum = 0;
        int weight = 2;
        for (int index = digits.Length - 1; index >= 0; index--)
        {
            sum += (digits[index] - '0') * weight;
            weight = weight == 2 ? 1 : 2;
        }

        int remainder = sum % 11;
        int check = 11 - remainder;
        // 11 folds to 0 and 10 folds to 0 so every body maps to one digit.
        return check is 11 or 10 ? 0 : check;
    }

    private static string Project(string? value, int width, string segmentName)
    {
        if (width == 0)
        {
            return string.Empty;
        }

        string trimmed = (value ?? string.Empty).Trim();
        if (trimmed.Length != width || !trimmed.All(char.IsAsciiDigit))
        {
            throw new InvalidOperationException($"account_number_{segmentName}_segment_not_numeric");
        }

        return trimmed;
    }
}
