using System;
using CoreBanking.BuildingBlocks.Application.Persistence;

namespace CoreBanking.IntegrationHub.Infrastructure.Persistence;

public sealed class InstitutionBoundaryRecord : ITenantOwnedEntity
{
    public required string InstitutionId { get; set; }
    public int ProvisioningPlanVersion { get; set; }
    public required string Status { get; set; }
    public required string SourceEventId { get; set; }
    public DateTimeOffset InitialisedAt { get; set; }
}
