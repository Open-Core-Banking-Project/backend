using System;
using CoreBanking.BuildingBlocks.Application.Persistence;

namespace CoreBanking.CoreLedger.Infrastructure.Persistence;

public sealed class ConfigurationSetRecord : ITenantOwnedEntity
{
    public required string InstitutionId { get; set; }
    public required string ConfigurationSetId { get; set; }
    public required string Area { get; set; }
    public required string Scope { get; set; }
    public long Version { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class ConfigurationVersionRecord : ITenantOwnedEntity
{
    public required string InstitutionId { get; set; }
    public required string VersionId { get; set; }
    public required string ConfigurationSetId { get; set; }
    public int VersionNumber { get; set; }
    public required string Schema { get; set; }
    public required string ContentJson { get; set; }
    public required string ContentHash { get; set; }
    public required string Status { get; set; }
    public required string CreatedByActorId { get; set; }
    public string? RollbackOfVersionId { get; set; }
    public string? ApprovedByActorId { get; set; }
    public string? ApprovalReference { get; set; }
    public DateTimeOffset? EffectiveFrom { get; set; }
    public DateTimeOffset? EffectiveTo { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
    public long Version { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class ConfigurationSimulationRecord : ITenantOwnedEntity
{
    public required string InstitutionId { get; set; }
    public required string SimulationId { get; set; }
    public required string VersionId { get; set; }
    public required string DatasetVersion { get; set; }
    public required string Engine { get; set; }
    public required string ContentHash { get; set; }
    public required string InputHash { get; set; }
    public required string OutputHash { get; set; }
    public required string Outcome { get; set; }
    public DateTimeOffset SimulatedAt { get; set; }
}

public sealed class ConfigurationPublicationRecord : ITenantOwnedEntity
{
    public required string InstitutionId { get; set; }
    public required string PublicationId { get; set; }
    public required string ConfigurationSetId { get; set; }
    public required string VersionId { get; set; }
    public required string ContentHash { get; set; }
    public required string PublishedByActorId { get; set; }
    public required string ApprovalReference { get; set; }
    public DateTimeOffset EffectiveFrom { get; set; }
    public DateTimeOffset? EffectiveTo { get; set; }
    public DateTimeOffset PublishedAt { get; set; }
}
