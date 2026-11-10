using System;
using System.Collections.Generic;

namespace CoreBanking.CoreLedger.Api.Authorisation;

/// <summary>Private wire projection of Identity's version 1 snapshot contract.</summary>
public sealed record EntitlementSnapshotRequestV1
{
    public int SchemaVersion { get; init; } = 1;
    public required string ActorId { get; init; }
    public required string InstitutionId { get; init; }
    public required long MinimumVersion { get; init; }
}

public sealed record EntitlementSnapshotV1
{
    public int SchemaVersion { get; init; } = 1;
    public required string ActorId { get; init; }
    public required string InstitutionId { get; init; }
    public required long Version { get; init; }
    public required bool IsComplete { get; init; }
    public required DateTimeOffset CapturedAt { get; init; }
    public required IReadOnlyList<PermissionGrantV1> Grants { get; init; }
}

public sealed record PermissionGrantV1
{
    public required string Permission { get; init; }
    public required GrantScopeV1 Scope { get; init; }
    public DateTimeOffset? ValidFrom { get; init; }
    public DateTimeOffset? ValidTo { get; init; }
}

public sealed record GrantScopeV1
{
    public required string InstitutionId { get; init; }
    public string? LegalEntityId { get; init; }
    public string? RegionId { get; init; }
    public string? BranchId { get; init; }
    public string? ChannelId { get; init; }
    public string? CustomerId { get; init; }
    public string? AccountId { get; init; }
    public string? MandateId { get; init; }
    public string? Currency { get; init; }
    public decimal? MaximumAmount { get; init; }
    public string? DeviceId { get; init; }
    public string? MinimumAuthenticationStrength { get; init; }
}
