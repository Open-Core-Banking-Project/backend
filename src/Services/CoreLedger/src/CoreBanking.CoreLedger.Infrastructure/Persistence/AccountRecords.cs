using System;
using CoreBanking.BuildingBlocks.Application.Persistence;

namespace CoreBanking.CoreLedger.Infrastructure.Persistence;

/// <summary>
/// One account application. It binds the requested customer, product version, branch and currency
/// and carries no account number: the number and account start at approval. Decision: P1-E04-D09.
/// </summary>
public sealed class AccountApplicationRecord : ITenantOwnedEntity
{
    public required string InstitutionId { get; set; }
    public required string AccountApplicationId { get; set; }
    public required string RequestId { get; set; }
    public required string CustomerId { get; set; }
    public required string ProductId { get; set; }
    public required string ProductCode { get; set; }
    public required string ProductFamily { get; set; }
    public required string ProductVersionId { get; set; }
    public int ProductVersionNumber { get; set; }
    public required string ProductContentHash { get; set; }
    public DateTimeOffset ProductEffectiveFrom { get; set; }
    public DateTimeOffset? ProductEffectiveTo { get; set; }
    public required string BranchId { get; set; }
    public required string CurrencyCode { get; set; }
    public required string AccountPurposeCode { get; set; }
    public required string OwnershipTypeCode { get; set; }
    public required string Status { get; set; }
    public required string CreatedByActorId { get; set; }
    public long Version { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>
/// A short-lived claim that serialises concurrent openings for one customer on one counting basis.
/// The unique index over the unreleased claim is what stops two evaluations from both passing the
/// same limit. Decision: P1-E04-D07.
/// </summary>
public sealed class AccountOpeningClaimRecord : ITenantOwnedEntity
{
    public required string InstitutionId { get; set; }
    public required string ClaimId { get; set; }
    public required string CustomerId { get; set; }
    public required string LimitKey { get; set; }
    public required string LimitKeyValue { get; set; }
    public required string AccountApplicationId { get; set; }
    public DateTimeOffset AcquiredAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? ReleasedAt { get; set; }
}

/// <summary>
/// Immutable evidence of one authoritative eligibility evaluation: the exact facts versions, the
/// product content hash, the counted holdings and the decision. Decision: P1-E04-D07, P1-E04-D17.
/// </summary>
public sealed class AccountEligibilityEvaluationRecord : ITenantOwnedEntity
{
    public required string InstitutionId { get; set; }
    public required string EvaluationId { get; set; }
    public required string AccountApplicationId { get; set; }
    public required string ProductVersionId { get; set; }
    public required string ProductContentHash { get; set; }
    public required string CustomerFactsVersion { get; set; }
    public required string BranchReferenceVersion { get; set; }
    public required string CurrencyReferenceVersion { get; set; }
    public DateTimeOffset FactsValidUntil { get; set; }
    public bool Eligible { get; set; }
    public bool FactsCurrent { get; set; }
    public required string DecisionCode { get; set; }
    public required string TraceJson { get; set; }
    public string? LimitKey { get; set; }
    public int? ProductMaximum { get; set; }
    public int? CountedHoldings { get; set; }
    public int? EffectiveMaximum { get; set; }
    public required string CountedHoldingIdsJson { get; set; }
    public string? PolicyVersionId { get; set; }
    public required string EvaluatedByActorId { get; set; }
    public DateTimeOffset EvaluatedAt { get; set; }
}
