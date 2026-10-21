using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using CoreBanking.BuildingBlocks.Domain.Identity;
using CoreBanking.CoreLedger.Domain.Institutions;

namespace CoreBanking.CoreLedger.Domain.Products;

public sealed record ProductCode
{
    private const int MaximumLength = 64;

    private ProductCode(string value) => Value = value;

    public string Value { get; }

    public static ProductCode From(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        string normalised = value.Trim().ToUpperInvariant();
        if (normalised.Length > MaximumLength || !normalised.All(IsAllowedCharacter))
            throw new ArgumentException(
                "Product code must contain at most 64 ASCII letters, digits, periods, underscores or hyphens.",
                nameof(value));
        return new ProductCode(normalised);
    }

    public override string ToString() => Value;

    private static bool IsAllowedCharacter(char value) =>
        value is >= 'A' and <= 'Z' or >= '0' and <= '9' or '.' or '_' or '-';
}

public sealed record InstitutionProduct(
    InstitutionId InstitutionId,
    ProductCode Code,
    ProductFamily Family)
{
    public static InstitutionProduct Create(
        InstitutionId institutionId,
        ProductCode code,
        ProductFamily family)
    {
        ArgumentNullException.ThrowIfNull(institutionId);
        ArgumentNullException.ThrowIfNull(code);
        return new InstitutionProduct(institutionId, code, family);
    }
}

public enum ProductFamily
{
    Savings,
}

public static class ProductFamilyPolicy
{
    public static ProductFamily ParseLiveFamily(string value) => value?.Trim().ToLowerInvariant() switch
    {
        "savings" => ProductFamily.Savings,
        _ => throw new InvalidOperationException("Only the Savings product family is approved for Phase 1 live eligibility."),
    };
}

public sealed class EligibilityCodeCriterion
{
    private readonly HashSet<string> _allowedValues;

    private EligibilityCodeCriterion(bool allowsAny, IEnumerable<string> allowedValues)
    {
        AllowsAny = allowsAny;
        _allowedValues = new HashSet<string>(allowedValues, StringComparer.OrdinalIgnoreCase);
    }

    public bool AllowsAny { get; }
    public IReadOnlyCollection<string> AllowedValues => _allowedValues.OrderBy(x => x, StringComparer.Ordinal).ToArray();

    public static EligibilityCodeCriterion Any() => new(true, []);

    public static EligibilityCodeCriterion Only(IEnumerable<string> allowedValues)
    {
        ArgumentNullException.ThrowIfNull(allowedValues);
        string[] values = allowedValues.Select(NormaliseCode).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (values.Length == 0)
            throw new ArgumentException("An allow-list criterion must contain at least one value.", nameof(allowedValues));
        return new EligibilityCodeCriterion(false, values);
    }

    public bool Allows(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return AllowsAny || _allowedValues.Contains(value.Trim());
    }

    internal object ToContract() => AllowsAny
        ? new { mode = "any", values = Array.Empty<string>() }
        : new { mode = "allow", values = AllowedValues };

    private static string NormaliseCode(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return value.Trim().ToUpperInvariant();
    }
}

public sealed record EligibilityAgeCriterion(bool AllowsAny, int? MinimumYears, int? MaximumYears)
{
    public static EligibilityAgeCriterion Any() => new(true, null, null);

    public static EligibilityAgeCriterion Between(int minimumYears, int maximumYears)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(minimumYears);
        if (maximumYears < minimumYears)
            throw new ArgumentException("Maximum age cannot be lower than minimum age.", nameof(maximumYears));
        return new EligibilityAgeCriterion(false, minimumYears, maximumYears);
    }

    public bool Allows(int ageYears)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(ageYears);
        return AllowsAny || (ageYears >= MinimumYears && ageYears <= MaximumYears);
    }
}

public sealed class ProductEligibilityRules
{
    private static readonly HashSet<string> RootProperties = new(StringComparer.Ordinal)
    {
        "schema", "ruleSetId", "version", "customerTypes", "kycTiers", "segments",
        "riskRatings", "ages", "branches", "residencyCountries", "currencies",
    };

    private static readonly HashSet<string> CodeCriterionProperties = new(StringComparer.Ordinal)
    {
        "mode", "values",
    };

    private static readonly HashSet<string> AgeCriterionProperties = new(StringComparer.Ordinal)
    {
        "mode", "minimumYears", "maximumYears",
    };

    private ProductEligibilityRules(
        string ruleSetId,
        int version,
        EligibilityCodeCriterion customerTypes,
        EligibilityCodeCriterion kycTiers,
        EligibilityCodeCriterion segments,
        EligibilityCodeCriterion riskRatings,
        EligibilityAgeCriterion ages,
        EligibilityCodeCriterion branches,
        EligibilityCodeCriterion residencyCountries,
        EligibilityCodeCriterion currencies)
    {
        RuleSetId = ruleSetId;
        Version = version;
        CustomerTypes = customerTypes;
        KycTiers = kycTiers;
        Segments = segments;
        RiskRatings = riskRatings;
        Ages = ages;
        Branches = branches;
        ResidencyCountries = residencyCountries;
        Currencies = currencies;
    }

    public string RuleSetId { get; }
    public int Version { get; }
    public EligibilityCodeCriterion CustomerTypes { get; }
    public EligibilityCodeCriterion KycTiers { get; }
    public EligibilityCodeCriterion Segments { get; }
    public EligibilityCodeCriterion RiskRatings { get; }
    public EligibilityAgeCriterion Ages { get; }
    public EligibilityCodeCriterion Branches { get; }
    public EligibilityCodeCriterion ResidencyCountries { get; }
    public EligibilityCodeCriterion Currencies { get; }

    public static ProductEligibilityRules Create(
        string ruleSetId,
        int version,
        EligibilityCodeCriterion customerTypes,
        EligibilityCodeCriterion kycTiers,
        EligibilityCodeCriterion segments,
        EligibilityCodeCriterion riskRatings,
        EligibilityAgeCriterion ages,
        EligibilityCodeCriterion branches,
        EligibilityCodeCriterion residencyCountries,
        EligibilityCodeCriterion currencies)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ruleSetId);
        ArgumentOutOfRangeException.ThrowIfLessThan(version, 1);
        ArgumentNullException.ThrowIfNull(customerTypes);
        ArgumentNullException.ThrowIfNull(kycTiers);
        ArgumentNullException.ThrowIfNull(segments);
        ArgumentNullException.ThrowIfNull(riskRatings);
        ArgumentNullException.ThrowIfNull(ages);
        ArgumentNullException.ThrowIfNull(branches);
        ArgumentNullException.ThrowIfNull(residencyCountries);
        ArgumentNullException.ThrowIfNull(currencies);
        return new ProductEligibilityRules(
            ruleSetId.Trim(), version, customerTypes, kycTiers, segments, riskRatings,
            ages, branches, residencyCountries, currencies);
    }

    public ConfigurationContent ToConfigurationContent() => ConfigurationContent.Create(JsonSerializer.Serialize(new
    {
        schema = "product_eligibility.v1",
        ruleSetId = RuleSetId,
        version = Version,
        customerTypes = CustomerTypes.ToContract(),
        kycTiers = KycTiers.ToContract(),
        segments = Segments.ToContract(),
        riskRatings = RiskRatings.ToContract(),
        ages = Ages.AllowsAny
            ? new { mode = "any", minimumYears = (int?)null, maximumYears = (int?)null }
            : new { mode = "range", minimumYears = Ages.MinimumYears, maximumYears = Ages.MaximumYears },
        branches = Branches.ToContract(),
        residencyCountries = ResidencyCountries.ToContract(),
        currencies = Currencies.ToContract(),
    }));

    public static ProductEligibilityRules FromConfigurationContent(ConfigurationContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        try
        {
            using JsonDocument document = JsonDocument.Parse(content.CanonicalJson);
            JsonElement root = document.RootElement;
            EnsureOnlyProperties(root, RootProperties, "product eligibility");
            if (RequiredString(root, "schema") != "product_eligibility.v1")
                throw new ArgumentException("Unsupported product eligibility schema.", nameof(content));
            return Create(
                RequiredString(root, "ruleSetId"),
                root.GetProperty("version").GetInt32(),
                ParseCodeCriterion(root.GetProperty("customerTypes"), "customerTypes"),
                ParseCodeCriterion(root.GetProperty("kycTiers"), "kycTiers"),
                ParseCodeCriterion(root.GetProperty("segments"), "segments"),
                ParseCodeCriterion(root.GetProperty("riskRatings"), "riskRatings"),
                ParseAgeCriterion(root.GetProperty("ages")),
                ParseCodeCriterion(root.GetProperty("branches"), "branches"),
                ParseCodeCriterion(root.GetProperty("residencyCountries"), "residencyCountries"),
                ParseCodeCriterion(root.GetProperty("currencies"), "currencies"));
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            throw new ArgumentException("Configuration content is not a valid product_eligibility.v1 document.", nameof(content), exception);
        }
    }

    private static EligibilityCodeCriterion ParseCodeCriterion(JsonElement element, string name)
    {
        EnsureOnlyProperties(element, CodeCriterionProperties, name);
        string mode = RequiredString(element, "mode");
        JsonElement values = element.GetProperty("values");
        if (values.ValueKind != JsonValueKind.Array)
            throw new ArgumentException($"The {name} values must be an array.", nameof(element));
        string[] parsed = values.EnumerateArray().Select(x => x.GetString() ?? string.Empty).ToArray();
        return mode switch
        {
            "any" when parsed.Length == 0 => EligibilityCodeCriterion.Any(),
            "allow" => EligibilityCodeCriterion.Only(parsed),
            _ => throw new ArgumentException($"The {name} mode or values are invalid.", nameof(element)),
        };
    }

    private static EligibilityAgeCriterion ParseAgeCriterion(JsonElement element)
    {
        EnsureOnlyProperties(element, AgeCriterionProperties, "ages");
        string mode = RequiredString(element, "mode");
        return mode switch
        {
            "any" when element.GetProperty("minimumYears").ValueKind == JsonValueKind.Null
                && element.GetProperty("maximumYears").ValueKind == JsonValueKind.Null => EligibilityAgeCriterion.Any(),
            "range" => EligibilityAgeCriterion.Between(
                element.GetProperty("minimumYears").GetInt32(),
                element.GetProperty("maximumYears").GetInt32()),
            _ => throw new ArgumentException("The ages mode or boundaries are invalid.", nameof(element)),
        };
    }

    private static string RequiredString(JsonElement element, string propertyName)
    {
        string? value = element.GetProperty(propertyName).GetString();
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException($"The {propertyName} value is required.", nameof(element));
        return value;
    }

    private static void EnsureOnlyProperties(JsonElement element, HashSet<string> allowed, string section)
    {
        if (element.ValueKind != JsonValueKind.Object
            || element.EnumerateObject().Any(property => !allowed.Contains(property.Name)))
            throw new ArgumentException($"The {section} section contains unsupported properties.", nameof(element));
    }
}

public sealed record ProductEligibilitySimulationInput(
    string ScenarioId,
    string DatasetVersion,
    string CustomerFactsVersion,
    string BranchReferenceVersion,
    string CurrencyReferenceVersion,
    string CustomerType,
    string KycTier,
    string Segment,
    string RiskRating,
    int AgeYears,
    string BranchCode,
    string ResidencyCountryCode,
    string CurrencyCode,
    bool ExpectedEligible);

public sealed record EligibilityRuleTrace(string Rule, string Input, bool Passed);

public sealed record AuthoritativeProductEligibilityFacts(
    string CustomerFactsVersion,
    string BranchReferenceVersion,
    string CurrencyReferenceVersion,
    DateTimeOffset ValidUntil,
    string CustomerType,
    string KycTier,
    string Segment,
    string RiskRating,
    int AgeYears,
    string BranchCode,
    string ResidencyCountryCode,
    string CurrencyCode);

public sealed record FinalEligibilityDecision(
    bool Eligible,
    bool FactsCurrent,
    string CustomerFactsVersion,
    string BranchReferenceVersion,
    string CurrencyReferenceVersion,
    IReadOnlyCollection<EligibilityRuleTrace> Trace,
    string Code);

public static class ProductEligibilityApprovalPolicy
{
    public static FinalEligibilityDecision RecheckForFinalApproval(
        ProductEligibilityRules rules,
        AuthoritativeProductEligibilityFacts facts,
        DateTimeOffset evaluatedAt)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(facts);
        ValidateFacts(facts);
        if (facts.ValidUntil < evaluatedAt)
        {
            return new FinalEligibilityDecision(
                false, false, facts.CustomerFactsVersion, facts.BranchReferenceVersion,
                facts.CurrencyReferenceVersion, [], "eligibility_facts_stale");
        }

        EligibilityRuleTrace[] trace = ProductEligibilityEvaluator.Evaluate(
            rules,
            facts.CustomerType,
            facts.KycTier,
            facts.Segment,
            facts.RiskRating,
            facts.AgeYears,
            facts.BranchCode,
            facts.ResidencyCountryCode,
            facts.CurrencyCode);
        bool eligible = trace.All(x => x.Passed);
        return new FinalEligibilityDecision(
            eligible, true, facts.CustomerFactsVersion, facts.BranchReferenceVersion,
            facts.CurrencyReferenceVersion, trace,
            eligible ? "eligible" : "product_eligibility_failed");
    }

    private static void ValidateFacts(AuthoritativeProductEligibilityFacts facts)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(facts.CustomerFactsVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(facts.BranchReferenceVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(facts.CurrencyReferenceVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(facts.CustomerType);
        ArgumentException.ThrowIfNullOrWhiteSpace(facts.KycTier);
        ArgumentException.ThrowIfNullOrWhiteSpace(facts.Segment);
        ArgumentException.ThrowIfNullOrWhiteSpace(facts.RiskRating);
        ArgumentOutOfRangeException.ThrowIfNegative(facts.AgeYears);
        ArgumentException.ThrowIfNullOrWhiteSpace(facts.BranchCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(facts.ResidencyCountryCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(facts.CurrencyCode);
    }
}

internal static class ProductEligibilityEvaluator
{
    public static EligibilityRuleTrace[] Evaluate(
        ProductEligibilityRules rules,
        string customerType,
        string kycTier,
        string segment,
        string riskRating,
        int ageYears,
        string branchCode,
        string residencyCountryCode,
        string currencyCode) =>
    [
        new("customer_type", customerType, rules.CustomerTypes.Allows(customerType)),
        new("kyc_tier", kycTier, rules.KycTiers.Allows(kycTier)),
        new("segment", segment, rules.Segments.Allows(segment)),
        new("risk_rating", riskRating, rules.RiskRatings.Allows(riskRating)),
        new("age_years", ageYears.ToString(System.Globalization.CultureInfo.InvariantCulture), rules.Ages.Allows(ageYears)),
        new("branch", branchCode, rules.Branches.Allows(branchCode)),
        new("residency_country", residencyCountryCode, rules.ResidencyCountries.Allows(residencyCountryCode)),
        new("currency", currencyCode, rules.Currencies.Allows(currencyCode)),
    ];
}

public sealed record ProductEligibilityScenarioResult(
    string ScenarioId,
    bool ExpectedEligible,
    bool Eligible,
    IReadOnlyCollection<EligibilityRuleTrace> Trace);

public sealed record ProductEligibilitySimulationResult(
    string SimulationId,
    string DatasetVersion,
    string ContentHash,
    string InputHash,
    string OutputHash,
    bool Passed,
    IReadOnlyCollection<ProductEligibilityScenarioResult> Scenarios,
    DateTimeOffset SimulatedAt)
{
    public ConfigurationSimulationEvidence ToConfigurationEvidence() => new(
        SimulationId,
        ContentHash,
        "product-eligibility-v1",
        Passed ? "passed" : "failed",
        SimulatedAt);
}

public static class ProductEligibilitySimulator
{
    public static ProductEligibilitySimulationResult Simulate(
        string simulationId,
        ProductEligibilityRules rules,
        IReadOnlyCollection<ProductEligibilitySimulationInput> inputs,
        DateTimeOffset simulatedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(simulationId);
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(inputs);
        if (inputs.Count == 0) throw new ArgumentException("At least one simulation scenario is required.", nameof(inputs));
        if (inputs.Select(x => x.ScenarioId).Distinct(StringComparer.Ordinal).Count() != inputs.Count)
            throw new ArgumentException("Simulation scenario identifiers must be unique.", nameof(inputs));

        string datasetVersion = RequireOneVersion(inputs.Select(x => x.DatasetVersion), "dataset");
        foreach (ProductEligibilitySimulationInput input in inputs) ValidateInput(input);

        ProductEligibilityScenarioResult[] scenarios = inputs
            .OrderBy(x => x.ScenarioId, StringComparer.Ordinal)
            .Select(input => Evaluate(rules, input))
            .ToArray();
        ConfigurationContent content = rules.ToConfigurationContent();
        string inputHash = Hash(new
        {
            datasetVersion,
            scenarios = inputs.OrderBy(x => x.ScenarioId, StringComparer.Ordinal),
        });
        string outputHash = Hash(new
        {
            passed = scenarios.All(x => x.ExpectedEligible == x.Eligible),
            scenarios,
        });
        return new ProductEligibilitySimulationResult(
            simulationId.Trim(), datasetVersion, content.Hash, inputHash, outputHash,
            scenarios.All(x => x.ExpectedEligible == x.Eligible), scenarios, simulatedAt);
    }

    private static ProductEligibilityScenarioResult Evaluate(ProductEligibilityRules rules, ProductEligibilitySimulationInput input)
    {
        EligibilityRuleTrace[] trace = ProductEligibilityEvaluator.Evaluate(
            rules, input.CustomerType, input.KycTier, input.Segment, input.RiskRating,
            input.AgeYears, input.BranchCode, input.ResidencyCountryCode, input.CurrencyCode);
        return new ProductEligibilityScenarioResult(input.ScenarioId, input.ExpectedEligible, trace.All(x => x.Passed), trace);
    }

    private static void ValidateInput(ProductEligibilitySimulationInput input)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(input.ScenarioId);
        ArgumentException.ThrowIfNullOrWhiteSpace(input.DatasetVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(input.CustomerFactsVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(input.BranchReferenceVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(input.CurrencyReferenceVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(input.CustomerType);
        ArgumentException.ThrowIfNullOrWhiteSpace(input.KycTier);
        ArgumentException.ThrowIfNullOrWhiteSpace(input.Segment);
        ArgumentException.ThrowIfNullOrWhiteSpace(input.RiskRating);
        ArgumentOutOfRangeException.ThrowIfNegative(input.AgeYears);
        ArgumentException.ThrowIfNullOrWhiteSpace(input.BranchCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(input.ResidencyCountryCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(input.CurrencyCode);
    }

    private static string RequireOneVersion(IEnumerable<string> versions, string name)
    {
        string[] distinct = versions.Select(x => x?.Trim()).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.Ordinal).ToArray()!;
        if (distinct.Length != 1) throw new ArgumentException($"Simulation scenarios must use one explicit {name} version.", nameof(versions));
        return distinct[0];
    }

    private static string Hash(object value) => ConfigurationContent.Create(JsonSerializer.Serialize(value)).Hash;
}
