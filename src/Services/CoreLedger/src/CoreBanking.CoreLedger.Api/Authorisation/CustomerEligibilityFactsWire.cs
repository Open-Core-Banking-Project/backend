using System;

namespace CoreBanking.CoreLedger.Api.Authorisation;

/// <summary>Private wire projection of Customer and Onboarding's version 1 facts contract. Decision: P1-E04-D17.</summary>
public sealed record CustomerEligibilityFactsWireV1
{
    public int SchemaVersion { get; init; } = 1;

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

/// <summary>The bounded error body the facts route returns.</summary>
public sealed record CustomerFactsErrorWireV1
{
    public required string Code { get; init; }
}
