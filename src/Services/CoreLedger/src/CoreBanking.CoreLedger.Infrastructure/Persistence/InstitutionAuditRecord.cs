using System;
using CoreBanking.BuildingBlocks.Application.Persistence;

namespace CoreBanking.CoreLedger.Infrastructure.Persistence;

/// <summary>
/// Source-owned, append-only evidence for a privileged institution operation. It is committed in
/// the same transaction as the governed state change and is not exposed through a mutation API.
/// </summary>
public sealed class InstitutionAuditRecord : ITenantOwnedEntity
{
    public required string InstitutionId { get; set; }
    public required string AuditEventId { get; set; }
    public required string ActorId { get; set; }
    public required string ActorType { get; set; }
    public required string Permission { get; set; }
    public required string Action { get; set; }
    public required string ResourceType { get; set; }
    public required string ResourceId { get; set; }
    public required string Outcome { get; set; }
    public required string Reason { get; set; }
    public required string CorrelationId { get; set; }
    public string? BeforeReference { get; set; }
    public string? AfterReference { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
}
