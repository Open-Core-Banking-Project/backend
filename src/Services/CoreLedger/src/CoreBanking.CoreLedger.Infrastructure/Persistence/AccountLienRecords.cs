using System;
using CoreBanking.BuildingBlocks.Application.Persistence;

namespace CoreBanking.CoreLedger.Infrastructure.Persistence;

/// <summary>
/// A lien claim: the amount is immutable and the outstanding amount is tracked separately so partial
/// release is an entry, not an edit. Decision: P1-E04-D11.
/// </summary>
public sealed class AccountLienRecord : ITenantOwnedEntity
{
    public required string InstitutionId { get; set; }
    public required string LienId { get; set; }
    public required string AccountId { get; set; }
    public required string PriorityCode { get; set; }
    public int PriorityRank { get; set; }
    public decimal Amount { get; set; }
    public decimal OutstandingAmount { get; set; }
    public required string CurrencyCode { get; set; }
    public required string ReasonCode { get; set; }
    public string? AuthorityReference { get; set; }
    public required string Status { get; set; }
    public required string RequestId { get; set; }
    public required string CreatedByActorId { get; set; }
    public DateTimeOffset EffectiveFrom { get; set; }
    public DateTimeOffset? EffectiveTo { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>An append-only lien movement: partial release, full release or ledger satisfaction.</summary>
public sealed class AccountLienMovementRecord : ITenantOwnedEntity
{
    public required string InstitutionId { get; set; }
    public required string MovementId { get; set; }
    public required string LienId { get; set; }
    public required string MovementType { get; set; }
    public decimal Amount { get; set; }
    public decimal OutstandingAfter { get; set; }
    public required string ReasonCode { get; set; }
    public string? AuthorityReference { get; set; }
    public required string ActorId { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
}
