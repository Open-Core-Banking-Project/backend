using System;
using CoreBanking.BuildingBlocks.Application.Persistence;

namespace CoreBanking.CoreLedger.Infrastructure.Persistence;

/// <summary>
/// One authorised statement or receipt request. It records the period, format, purpose, the ledger query and
/// integrity references and the artifact lifecycle, and never the statement content: no line, balance or
/// counterparty is stored here. A request record outlives its artifact. Decision: P1-E04-D15.
/// </summary>
public sealed class AccountStatementRequestRecord : ITenantOwnedEntity
{
    public required string InstitutionId { get; set; }
    public required string RequestId { get; set; }
    public required string AccountId { get; set; }
    public DateOnly PeriodFrom { get; set; }
    public DateOnly PeriodTo { get; set; }
    public required string FormatCode { get; set; }
    public required string PurposeCode { get; set; }
    public required string Status { get; set; }
    public required string PolicyVersionId { get; set; }
    public required string RequesterTypeCode { get; set; }
    public required string RequesterActorId { get; set; }
    public required string LedgerQueryReference { get; set; }
    public required string IntegrityReference { get; set; }
    public string? RefusalCodes { get; set; }
    public string? ArtifactReference { get; set; }
    public DateTimeOffset? ArtifactReadyAt { get; set; }
    public DateTimeOffset? RetentionExpiresAt { get; set; }
    public bool LegalHold { get; set; }
    public DateTimeOffset RequestedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public long Version { get; set; }
}

/// <summary>
/// One short-lived artifact reference: opaque, bound to the requesting party and the account, and expiring within
/// minutes. Re-download requires a new authorisation step. Decision: P1-E04-D15.
/// </summary>
public sealed class AccountStatementArtifactReferenceRecord : ITenantOwnedEntity
{
    public required string InstitutionId { get; set; }
    public required string ReferenceId { get; set; }
    public required string RequestId { get; set; }
    public required string AccountId { get; set; }
    public required string ArtifactReference { get; set; }
    public required string RequesterTypeCode { get; set; }
    public required string RequesterActorId { get; set; }
    public DateTimeOffset IssuedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public int AccessCount { get; set; }
    public DateTimeOffset? LastAccessedAt { get; set; }
}
