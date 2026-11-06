using System;

namespace CoreBanking.CustomerOnboarding.Contracts;

/// <summary>
/// Version 1 authoritative customer eligibility facts. It carries only what a product or account
/// decision needs plus the evidence markers that prove which source state produced them. Names,
/// contacts, documents, birth dates and national identifiers never appear here.
/// Decision: P1-E04-D17.
/// </summary>
public sealed record CustomerEligibilityFactsV1
{
    public int SchemaVersion { get; init; } = CustomerFactsContractV1.SchemaVersion;

    public required string InstitutionId { get; init; }

    public required string CustomerId { get; init; }

    public required string CustomerStatus { get; init; }

    public int? EffectiveClassificationVersion { get; init; }

    public required string KycTier { get; init; }

    public required string Segment { get; init; }

    public required string RiskClass { get; init; }

    public required bool RequiresEnhancedDueDiligence { get; init; }

    public required string CustomerType { get; init; }

    public required int AgeYears { get; init; }

    public required string FactsVersion { get; init; }

    public required DateTimeOffset AsOf { get; init; }

    public required DateTimeOffset ValidUntil { get; init; }
}

/// <summary>Stable error body for the internal facts route. Codes are bounded and carry no detail.</summary>
public sealed record CustomerFactsErrorV1(string Code);

/// <summary>Producer, route and capability values shared by the owner and its consumers.</summary>
public static class CustomerFactsContractV1
{
    public const string Producer = "customer-onboarding";

    public const int SchemaVersion = 1;

    public const string Audience = "corebanking-customer-api";

    public const string Capability = "service.customer.eligibility_facts_read";

    public const string RoutePattern = "/internal/v1/customers/{customerId}/eligibility-facts";

    /// <summary>The longest validity window the source may declare. Decision: P1-E04-D17.</summary>
    public static readonly TimeSpan MaximumValidity = TimeSpan.FromSeconds(60);
}
