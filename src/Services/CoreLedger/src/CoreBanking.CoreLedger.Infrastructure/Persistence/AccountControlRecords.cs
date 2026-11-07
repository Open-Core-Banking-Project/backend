using System;
using CoreBanking.BuildingBlocks.Application.Persistence;

namespace CoreBanking.CoreLedger.Infrastructure.Persistence;

/// <summary>
/// A typed restriction: it denies only within its declared direction, channel and transaction-type scope and
/// never escapes it. Decision: P1-E04-D10.
/// </summary>
public sealed class AccountRestrictionRecord : ITenantOwnedEntity
{
    public required string InstitutionId { get; set; }
    public required string RestrictionId { get; set; }
    public required string AccountId { get; set; }
    public required string RestrictionTypeCode { get; set; }
    public required string PostingDirectionCode { get; set; }
    public required string ChannelScopeJson { get; set; }
    public required string TransactionTypeScopeJson { get; set; }
    public required string ReasonCode { get; set; }
    public string? AuthorityReference { get; set; }
    public required string Status { get; set; }
    public bool RequiresManualRelease { get; set; }
    public required string RequestId { get; set; }
    public required string AppliedByActorId { get; set; }
    public string? ReleasedByActorId { get; set; }
    public DateTimeOffset? ReleasedAt { get; set; }
    public long ControlVersion { get; set; }
    public DateTimeOffset EffectiveFrom { get; set; }
    public DateTimeOffset? EffectiveTo { get; set; }
}

/// <summary>
/// A freeze: a broad denial of debits, optionally of credits for a regulatory freeze. Decision: P1-E04-D10.
/// </summary>
public sealed class AccountFreezeRecord : ITenantOwnedEntity
{
    public required string InstitutionId { get; set; }
    public required string FreezeId { get; set; }
    public required string AccountId { get; set; }
    public required string FreezeTypeCode { get; set; }
    public required string ReasonCode { get; set; }
    public required string AuthorityReference { get; set; }
    public required string Status { get; set; }
    public bool RequiresManualRelease { get; set; }
    public required string RequestId { get; set; }
    public required string AppliedByActorId { get; set; }
    public string? ReleasedByActorId { get; set; }
    public DateTimeOffset? ReleasedAt { get; set; }
    public long ControlVersion { get; set; }
    public DateTimeOffset EffectiveFrom { get; set; }
    public DateTimeOffset? EffectiveTo { get; set; }
}

/// <summary>A block: a declared operation set is denied. Decision: P1-E04-D10.</summary>
public sealed class AccountBlockRecord : ITenantOwnedEntity
{
    public required string InstitutionId { get; set; }
    public required string BlockId { get; set; }
    public required string AccountId { get; set; }
    public required string BlockTypeCode { get; set; }
    public required string OperationScopeJson { get; set; }
    public required string ReasonCode { get; set; }
    public string? AuthorityReference { get; set; }
    public required string Status { get; set; }
    public bool RequiresManualRelease { get; set; }
    public required string RequestId { get; set; }
    public required string AppliedByActorId { get; set; }
    public string? ReleasedByActorId { get; set; }
    public DateTimeOffset? ReleasedAt { get; set; }
    public long ControlVersion { get; set; }
    public DateTimeOffset EffectiveFrom { get; set; }
    public DateTimeOffset? EffectiveTo { get; set; }
}
