using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CoreBanking.CoreLedger.Domain.Products;

namespace CoreBanking.CoreLedger.Domain.Accounts;

/// <summary>
/// One holding that counts against a multiple-account limit, with the attributes the approved
/// counting basis needs. Nothing here is caller supplied.
/// </summary>
public sealed record AccountHolding(
    string HoldingId,
    string Status,
    string ProductCode,
    string ProductFamily,
    string CurrencyCode)
{
    public bool Counts => AccountApplicationLifecycle.CountsTowardsLimit(ParseStatus(Status));

    internal bool Matches(AccountLimitKey limitKey, string productCode, string productFamily, string currencyCode) =>
        limitKey switch
        {
            AccountLimitKey.ProductFamily => string.Equals(ProductFamily, productFamily, StringComparison.OrdinalIgnoreCase),
            AccountLimitKey.Product => string.Equals(ProductCode, productCode, StringComparison.OrdinalIgnoreCase),
            AccountLimitKey.Currency => string.Equals(CurrencyCode, currencyCode, StringComparison.OrdinalIgnoreCase),
            AccountLimitKey.Institution => true,
            _ => false,
        };

    private static AccountApplicationStatus ParseStatus(string status) =>
        Enum.TryParse(status, ignoreCase: true, out AccountApplicationStatus parsed)
            ? parsed
            : throw new InvalidOperationException($"account_holding_status_unknown:{status}");
}

/// <summary>The counted set and the limit that applied, recorded as decision evidence.</summary>
public sealed record AccountLimitAssessment(
    string LimitKey,
    int Counted,
    int? ProductMaximum,
    int InstitutionMaximum,
    int EffectiveMaximum,
    bool WithinLimit,
    IReadOnlyCollection<string> CountedHoldingIds);

public sealed record AccountEligibilityDecision(
    bool Eligible,
    bool FactsCurrent,
    string CustomerFactsVersion,
    string BranchReferenceVersion,
    string CurrencyReferenceVersion,
    IReadOnlyCollection<EligibilityRuleTrace> Trace,
    AccountLimitAssessment? Limit,
    string Code);

/// <summary>The counted dimension and its value for one application.</summary>
public sealed record AccountLimitBasis(
    AccountLimitKey LimitKey,
    string LimitKeyValue,
    int? ProductMaximum,
    int InstitutionMaximum)
{
    public int EffectiveMaximum => ProductMaximum is null
        ? InstitutionMaximum
        : Math.Min(ProductMaximum.Value, InstitutionMaximum);
}

/// <summary>
/// The authoritative decision that an application may proceed. It combines the published product
/// rules, the institution opening ceiling and the counted holdings; the stricter limit wins and a
/// missing ceiling blocks opening. Decision: P1-E04-D07.
/// </summary>
public static class AccountEligibilityPolicy
{
    public const string LimitReachedCode = "account_limit_reached";
    public const string PolicyUnavailableCode = "account_opening_policy_unavailable";

    /// <summary>
    /// Resolves the counting basis before any holding is read, so a concurrent opening can be claimed
    /// on exactly this key. Decision: P1-E04-D07.
    /// </summary>
    public static AccountLimitBasis ResolveLimitBasis(
        ProductEligibilityRules rules,
        AccountOpeningPolicy openingPolicy,
        string productCode,
        string productFamily,
        string currencyCode)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(openingPolicy);

        AccountLimitKey limitKey = rules.ExistingAccounts.AllowsAny
            ? openingPolicy.LimitKey
            : rules.ExistingAccounts.LimitKey;
        string value = limitKey switch
        {
            AccountLimitKey.ProductFamily => productFamily,
            AccountLimitKey.Product => productCode,
            AccountLimitKey.Currency => currencyCode,
            AccountLimitKey.Institution => "*",
            _ => throw new ArgumentException("The limit key is not an approved counting basis.", nameof(rules)),
        };
        return new AccountLimitBasis(
            limitKey,
            value,
            rules.ExistingAccounts.AllowsAny ? null : rules.ExistingAccounts.MaximumAccounts,
            openingPolicy.MaximumOpenAccounts);
    }

    public static AccountEligibilityDecision Decide(
        ProductEligibilityRules rules,
        AuthoritativeProductEligibilityFacts facts,
        AccountOpeningPolicy? openingPolicy,
        IReadOnlyCollection<AccountHolding> holdings,
        string productCode,
        string productFamily,
        string currencyCode,
        DateTimeOffset evaluatedAt)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(facts);
        ArgumentNullException.ThrowIfNull(holdings);

        FinalEligibilityDecision productDecision = ProductEligibilityApprovalPolicy.RecheckForFinalApproval(
            rules,
            facts,
            evaluatedAt);
        if (!productDecision.Eligible)
        {
            return new AccountEligibilityDecision(
                false,
                productDecision.FactsCurrent,
                productDecision.CustomerFactsVersion,
                productDecision.BranchReferenceVersion,
                productDecision.CurrencyReferenceVersion,
                productDecision.Trace,
                null,
                productDecision.Code);
        }

        if (openingPolicy is null)
        {
            return new AccountEligibilityDecision(
                false,
                true,
                productDecision.CustomerFactsVersion,
                productDecision.BranchReferenceVersion,
                productDecision.CurrencyReferenceVersion,
                productDecision.Trace,
                null,
                PolicyUnavailableCode);
        }

        AccountLimitBasis basis = ResolveLimitBasis(rules, openingPolicy, productCode, productFamily, currencyCode);
        string[] counted = holdings
            .Where(holding => holding.Counts
                && holding.Matches(basis.LimitKey, productCode, productFamily, currencyCode))
            .Select(holding => holding.HoldingId)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();
        bool withinLimit = counted.Length < basis.EffectiveMaximum;
        AccountLimitAssessment limit = new(
            AccountOpeningPolicy.ToToken(basis.LimitKey),
            counted.Length,
            basis.ProductMaximum,
            basis.InstitutionMaximum,
            basis.EffectiveMaximum,
            withinLimit,
            counted);

        List<EligibilityRuleTrace> trace = [.. productDecision.Trace];
        trace.Add(new EligibilityRuleTrace(
            "existing_accounts",
            string.Create(
                CultureInfo.InvariantCulture,
                $"{counted.Length}/{basis.EffectiveMaximum} on {limit.LimitKey}"),
            withinLimit));

        return new AccountEligibilityDecision(
            withinLimit,
            true,
            productDecision.CustomerFactsVersion,
            productDecision.BranchReferenceVersion,
            productDecision.CurrencyReferenceVersion,
            trace,
            limit,
            withinLimit ? "eligible" : LimitReachedCode);
    }
}
