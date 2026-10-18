using System;
using CoreBanking.BuildingBlocks.Application.Persistence;

namespace CoreBanking.IdentityAccess.Infrastructure.Persistence.Security;

/// <summary>Append-only evidence for a privileged messaging repair or replay action.</summary>
public sealed class OperatorActionAuditRecord : ITenantOwnedEntity
{
    public required string AuditEventId { get; set; }

    public required string InstitutionId { get; set; }

    public required string ActorId { get; set; }

    public required string Action { get; set; }

    public required string ResourceType { get; set; }

    public required string ResourceId { get; set; }

    public required string Reason { get; set; }

    public required string CorrelationId { get; set; }

    public DateTimeOffset OccurredAt { get; set; }
}
