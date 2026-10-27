using System;
using CoreBanking.BuildingBlocks.Application.Persistence;

namespace CoreBanking.IntegrationHub.Infrastructure.Persistence;

public sealed class CredentialReferenceRecord : ITenantOwnedEntity
{
    public required string RecordId { get; set; }
    public required string CredentialReferenceId { get; set; }
    public required string InstitutionId { get; set; }
    public required string Environment { get; set; }
    public required string ProviderCode { get; set; }
    public required string Kind { get; set; }
    public required string ReferenceName { get; set; }
    public required string Owner { get; set; }
    public int Version { get; set; }
    public required string Status { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public string? PreviousRecordId { get; set; }
    public required string ActorId { get; set; }
    public required string Reason { get; set; }
    public required string CorrelationId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class ApiClientRecord : ITenantOwnedEntity
{
    public required string ApiClientId { get; set; }
    public required string InstitutionId { get; set; }
    public required string Environment { get; set; }
    public required string ClientIdentifier { get; set; }
    public required string DisplayName { get; set; }
    public required string AllowedScopesJson { get; set; }
    public required string AllowedNetworkRangesJson { get; set; }
    public required string Status { get; set; }
    public required string ActorId { get; set; }
    public required string Reason { get; set; }
    public required string CorrelationId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public string? RevokedBy { get; set; }
    public string? RevocationReason { get; set; }
    public uint Version { get; set; }
}
