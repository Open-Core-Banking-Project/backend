using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace CoreBanking.CoreLedger.Domain.Institutions;

/// <summary>
/// The approved supported-currency set of one institution, published through the configuration
/// lifecycle. Base currency must be a member; codes are ISO 4217 alpha-3. No code is invented here
/// and monetary precision stays with P1-E04-D03. Decision: P1-E04-D17.
/// </summary>
public sealed record InstitutionCurrencySet(
    string BaseCurrency,
    IReadOnlyList<string> SupportedCurrencies)
{
    public const string Area = "institution.currencies";
    public const string Schema = "institution.currencies.v1";

    public bool Supports(string currencyCode) =>
        SupportedCurrencies.Contains(currencyCode.Trim().ToUpperInvariant(), StringComparer.Ordinal);

    public static InstitutionCurrencySet Parse(string contentJson)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentJson);

        using JsonDocument document = JsonDocument.Parse(contentJson);
        JsonElement root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new ArgumentException("The currency configuration must be a JSON object.", nameof(contentJson));
        }

        HashSet<string> allowed = ["baseCurrency", "supportedCurrencies"];
        if (root.EnumerateObject().Any(property => !allowed.Contains(property.Name)))
        {
            throw new ArgumentException("The currency configuration contains unsupported properties.", nameof(contentJson));
        }

        if (!root.TryGetProperty("baseCurrency", out JsonElement baseElement)
            || baseElement.ValueKind != JsonValueKind.String
            || !root.TryGetProperty("supportedCurrencies", out JsonElement supportedElement)
            || supportedElement.ValueKind != JsonValueKind.Array)
        {
            throw new ArgumentException("The currency configuration requires baseCurrency and supportedCurrencies.", nameof(contentJson));
        }

        string baseCurrency = Normalise(baseElement.GetString(), nameof(contentJson));
        List<string> supported = [];
        foreach (JsonElement item in supportedElement.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
            {
                throw new ArgumentException("Every supported currency must be an ISO 4217 alpha-3 code.", nameof(contentJson));
            }

            string code = Normalise(item.GetString(), nameof(contentJson));
            if (supported.Contains(code, StringComparer.Ordinal))
            {
                throw new ArgumentException("The supported currency list contains a duplicate code.", nameof(contentJson));
            }

            supported.Add(code);
        }

        if (supported.Count == 0)
        {
            throw new ArgumentException("The supported currency list must contain at least one code.", nameof(contentJson));
        }

        if (!supported.Contains(baseCurrency, StringComparer.Ordinal))
        {
            throw new ArgumentException("The base currency must be a member of the supported currency set.", nameof(contentJson));
        }

        return new InstitutionCurrencySet(baseCurrency, supported.OrderBy(code => code, StringComparer.Ordinal).ToArray());
    }

    private static string Normalise(string? value, string parameterName)
    {
        string code = value?.Trim().ToUpperInvariant() ?? string.Empty;
        if (code.Length != 3 || !code.All(character => character is >= 'A' and <= 'Z'))
        {
            throw new ArgumentException("A currency code must be three ISO 4217 letters.", parameterName);
        }

        return code;
    }
}
