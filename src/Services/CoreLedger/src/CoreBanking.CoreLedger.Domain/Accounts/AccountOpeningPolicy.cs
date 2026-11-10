using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using CoreBanking.CoreLedger.Domain.Institutions;

namespace CoreBanking.CoreLedger.Domain.Accounts;

/// <summary>
/// The basis a multiple-account limit is counted over. Decision: P1-E04-D07.
/// </summary>
public enum AccountLimitKey
{
    ProductFamily,
    Product,
    Currency,
    Institution,
}

/// <summary>
/// The institution ceiling for multiple accounts, published through the configuration lifecycle.
/// A product rule may tighten it and never loosen it, so the stricter of the two applies; a missing
/// ceiling blocks opening because an absent limit is missing configuration, not "unlimited".
/// Decision: P1-E04-D07.
/// </summary>
public sealed record AccountOpeningPolicy(
    AccountLimitKey LimitKey,
    int MaximumOpenAccounts,
    int OverrideAdditionalAccounts)
{
    public const string Area = "institution.account-opening-policy";
    public const string Schema = "institution.account-opening-policy.v1";

    private static readonly HashSet<string> AllowedProperties = new(StringComparer.Ordinal)
    {
        "limitKey", "maximumOpenAccounts", "overrideAdditionalAccounts",
    };

    public ConfigurationContent ToConfigurationContent() =>
        ConfigurationContent.Create(BuildJson(LimitKey, MaximumOpenAccounts, OverrideAdditionalAccounts));

    public static string BuildJson(AccountLimitKey limitKey, int maximumOpenAccounts, int overrideAdditionalAccounts) =>
        JsonSerializer.Serialize(new
        {
            limitKey = ToToken(limitKey),
            maximumOpenAccounts,
            overrideAdditionalAccounts,
        });

    public static AccountOpeningPolicy Parse(string contentJson)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentJson);

        using JsonDocument document = JsonDocument.Parse(contentJson);
        JsonElement root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object
            || root.EnumerateObject().Any(property => !AllowedProperties.Contains(property.Name)))
            throw new ArgumentException("The account-opening policy contains unsupported properties.", nameof(contentJson));

        if (!root.TryGetProperty("limitKey", out JsonElement limitKeyElement)
            || limitKeyElement.ValueKind != JsonValueKind.String
            || !root.TryGetProperty("maximumOpenAccounts", out JsonElement maximumElement)
            || maximumElement.ValueKind != JsonValueKind.Number
            || !root.TryGetProperty("overrideAdditionalAccounts", out JsonElement overrideElement)
            || overrideElement.ValueKind != JsonValueKind.Number)
            throw new ArgumentException(
                "The account-opening policy requires limitKey, maximumOpenAccounts and overrideAdditionalAccounts.",
                nameof(contentJson));

        return Create(
            ParseLimitKey(limitKeyElement.GetString()),
            maximumElement.GetInt32(),
            overrideElement.GetInt32());
    }

    public static AccountOpeningPolicy Create(
        AccountLimitKey limitKey,
        int maximumOpenAccounts,
        int overrideAdditionalAccounts)
    {
        if (!Enum.IsDefined(limitKey))
            throw new ArgumentException("The limit key is not an approved counting basis.", nameof(limitKey));
        if (maximumOpenAccounts < 1)
            throw new ArgumentOutOfRangeException(
                nameof(maximumOpenAccounts), "The institution ceiling must allow at least one open account.");
        if (overrideAdditionalAccounts < 0 || overrideAdditionalAccounts > maximumOpenAccounts)
            throw new ArgumentOutOfRangeException(
                nameof(overrideAdditionalAccounts),
                "The override allowance must be between zero and the institution ceiling.");
        return new AccountOpeningPolicy(limitKey, maximumOpenAccounts, overrideAdditionalAccounts);
    }

    public static string ToToken(AccountLimitKey limitKey) => limitKey switch
    {
        AccountLimitKey.ProductFamily => "product_family",
        AccountLimitKey.Product => "product",
        AccountLimitKey.Currency => "currency",
        AccountLimitKey.Institution => "institution",
        _ => throw new ArgumentException("The limit key is not an approved counting basis.", nameof(limitKey)),
    };

    public static AccountLimitKey ParseLimitKey(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "product_family" => AccountLimitKey.ProductFamily,
        "product" => AccountLimitKey.Product,
        "currency" => AccountLimitKey.Currency,
        "institution" => AccountLimitKey.Institution,
        _ => throw new ArgumentException("The limit key is not an approved counting basis.", nameof(value)),
    };
}
