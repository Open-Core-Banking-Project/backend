using System;
using CoreBanking.BuildingBlocks.Application.Persistence;

namespace CoreBanking.IdentityAccess.Infrastructure.Persistence.Platform;

/// <summary>
/// Identity and Access' local knowledge that an institution exists. It does not make the
/// institution active; activation remains an explicit later event.
/// </summary>
public sealed class InstitutionBoundaryRecord : ITenantOwnedEntity
{
    public required string InstitutionId { get; set; }

    public int ProvisioningPlanVersion { get; set; }

    public required string Status { get; set; }

    public required string SourceEventId { get; set; }

    public DateTimeOffset InitialisedAt { get; set; }
}
