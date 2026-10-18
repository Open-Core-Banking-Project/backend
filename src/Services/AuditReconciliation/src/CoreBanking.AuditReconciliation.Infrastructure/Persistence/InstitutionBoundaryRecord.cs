using System;
using CoreBanking.BuildingBlocks.Application.Persistence;

namespace CoreBanking.AuditReconciliation.Infrastructure.Persistence;

/// <summary>The non-active local boundary created before audit traffic is accepted.</summary>
public sealed class InstitutionBoundaryRecord : ITenantOwnedEntity
{
    public required string InstitutionId { get; set; }

    public int ProvisioningPlanVersion { get; set; }

    public required string Status { get; set; }

    public required string SourceEventId { get; set; }

    public DateTimeOffset InitialisedAt { get; set; }
}
