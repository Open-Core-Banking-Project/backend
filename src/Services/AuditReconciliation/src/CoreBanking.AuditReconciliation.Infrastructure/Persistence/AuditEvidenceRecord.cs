using System;
using CoreBanking.BuildingBlocks.Application.Persistence;

namespace CoreBanking.AuditReconciliation.Infrastructure.Persistence;

/// <summary>Append-only evidence for the Phase 0 foundation-record creation action.</summary>
public sealed class AuditEvidenceRecord : ITenantOwnedEntity
{
    public required string InstitutionId { get; set; }

    public required string AuditEventId { get; set; }

    public required string ActorId { get; set; }

    public required string Action { get; set; }

    public required string ResourceType { get; set; }

    public required string ResourceId { get; set; }

    public required string Decision { get; set; }

    public required string Source { get; set; }

    public required string CorrelationId { get; set; }

    public DateTimeOffset OccurredAt { get; set; }
}
