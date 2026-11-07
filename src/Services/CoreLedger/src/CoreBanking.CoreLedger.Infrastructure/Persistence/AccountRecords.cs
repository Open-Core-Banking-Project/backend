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
    public string? EligibilityEvaluationId { get; set; }
    public string? EligibilitySnapshotHash { get; set; }
    public string? ApprovalRequestId { get; set; }
    public string? ApprovedByActorId { get; set; }
    public DateTimeOffset? ApprovedAt { get; set; }
    public required string Status { get; set; }
    public required string CreatedByActorId { get; set; }
    public long Version { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>
/// One account. It carries its full number, which is never projected into a list response, an event or
/// a log; contracts expose only the masked form. Decision: P1-E04-D06.
/// </summary>
public sealed class AccountRecord : ITenantOwnedEntity
{
    public required string InstitutionId { get; set; }
    public required string AccountId { get; set; }
    public required string AccountApplicationId { get; set; }
    public required string CustomerId { get; set; }
    public required string ProductId { get; set; }
    public required string ProductCode { get; set; }
    public required string ProductVersionId { get; set; }
    public required string ProductContentHash { get; set; }
    public required string BranchId { get; set; }
    public required string CurrencyCode { get; set; }
    public required string AccountPurposeCode { get; set; }
    public required string OwnershipTypeCode { get; set; }
    public required string AccountNumber { get; set; }
    public required string MaskedAccountNumber { get; set; }
    public required string SchemeId { get; set; }
    public required string SchemeVersionId { get; set; }
    public required string Status { get; set; }
    public required string LedgerInitialisationStatus { get; set; }
    public string? LedgerAccountReference { get; set; }
    public long Version { get; set; }
    public DateTimeOffset OpenedAt { get; set; }
}

/// <summary>
/// The ledger account opened for one bank account. It deliberately has no amount, balance or posting
/// column: opening and activation can never create money. Decision: P1-E04-D09.
/// </summary>
public sealed class LedgerAccountRecord : ITenantOwnedEntity
{
    public required string InstitutionId { get; set; }
    public required string LedgerAccountId { get; set; }
    public required string AccountId { get; set; }
    public required string CurrencyCode { get; set; }
    public required string Status { get; set; }
    public long Version { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>
/// The durable allocation state of one published scheme instance. The next value is the only source of
/// a new number and the row is updated under a lock. Decision: P1-E04-D06.
/// </summary>
public sealed class AccountNumberReservationRecord : ITenantOwnedEntity
{
    public required string InstitutionId { get; set; }
    public required string SchemeVersionId { get; set; }
    public required string SchemeId { get; set; }
    public long NextValue { get; set; }
    public long HighWaterMark { get; set; }
    public long Capacity { get; set; }
    public long Version { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>
/// A released sequence is recorded here and never reissued, so a scheme stays gap-detectable.
/// Decision: P1-E04-D06.
/// </summary>
public sealed class AccountNumberRetirementRecord : ITenantOwnedEntity
{
    public required string InstitutionId { get; set; }
    public required string RetirementId { get; set; }
    public required string SchemeVersionId { get; set; }
    public long SequenceValue { get; set; }
    public required string Reason { get; set; }
    public required string RetiredByActorId { get; set; }
    public DateTimeOffset RetiredAt { get; set; }
}

public sealed class AccountStatusHistoryRecord : ITenantOwnedEntity
{
    public required string InstitutionId { get; set; }
    public required string HistoryId { get; set; }
    public required string AccountId { get; set; }
    public required string Status { get; set; }
    public required string ReasonCode { get; set; }
    public required string ActorId { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
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
