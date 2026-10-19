using System;
using CoreBanking.BuildingBlocks.Application.Persistence;

namespace CoreBanking.CoreLedger.Infrastructure.Persistence;

public sealed class RegionRecord : ITenantOwnedEntity
{
    public required string InstitutionId { get; set; }
    public required string RegionId { get; set; }
    public required string Code { get; set; }
    public required string Name { get; set; }
    public required string Status { get; set; }
    public required string CreatedByActorId { get; set; }
    public long Version { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class BranchRecord : ITenantOwnedEntity
{
    public required string InstitutionId { get; set; }
    public required string BranchId { get; set; }
    public required string RegionId { get; set; }
    public required string Code { get; set; }
    public required string Name { get; set; }
    public required string Status { get; set; }
    public required string CreatedByActorId { get; set; }
    public long Version { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class OrganisationStatusHistoryRecord : ITenantOwnedEntity
{
    public required string InstitutionId { get; set; }
    public required string HistoryId { get; set; }
    public required string ResourceType { get; set; }
    public required string ResourceId { get; set; }
    public string? PreviousStatus { get; set; }
    public required string NewStatus { get; set; }
    public required string ChangedByActorId { get; set; }
    public string? Reason { get; set; }
    public required string CorrelationId { get; set; }
    public DateTimeOffset ChangedAt { get; set; }
}
