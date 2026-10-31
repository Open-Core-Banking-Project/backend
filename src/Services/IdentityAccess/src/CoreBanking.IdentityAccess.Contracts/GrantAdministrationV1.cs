using System;

namespace CoreBanking.IdentityAccess.Contracts;

/// <summary>Stable values for grant administration. Decision: ADR-0009 and P1-E02-D10.</summary>
public static class GrantContractV1
{
    public const int SchemaVersion = IdentityAccessContractV1.SchemaVersion;
    public const string RoleAssigned = "role_assigned";
    public const string RoleRevoked = "role_revoked";
    public const string TemporaryGrantAdded = "temporary_grant_added";
    public const string TemporaryGrantRevoked = "temporary_grant_revoked";
    public const string TemporaryGrantExpired = "temporary_grant_expired";

    public const string ConflictOfDuty = "identity.conflict_of_duty";
    public const string RoleNotFound = "identity.role_not_found";
    public const string PermissionNotInCatalogue = "identity.permission_not_found";
    public const string GrantNotFound = "identity.grant_not_found";
    public const string InvalidGrant = "request.invalid_grant";
}

/// <summary>Assigns one catalogued role, with its contextual scope, to one identity.</summary>
public sealed record RoleAssignmentRequestV1
{
    public int SchemaVersion { get; init; } = GrantContractV1.SchemaVersion;

    public required string IdentityId { get; init; }

    public required string RoleId { get; init; }

    public string? RegionId { get; init; }

    public string? BranchId { get; init; }

    public string? ChannelId { get; init; }

    /// <summary>Optional end of the assignment. A lapsed assignment is not an effective grant.</summary>
    public DateTimeOffset? ValidTo { get; init; }
}

/// <summary>Grants one catalogue permission for a bounded period, outside the actor's roles.</summary>
public sealed record TemporaryGrantRequestV1
{
    public int SchemaVersion { get; init; } = GrantContractV1.SchemaVersion;

    public required string IdentityId { get; init; }

    public required string PermissionCode { get; init; }

    public string? RegionId { get; init; }

    public string? BranchId { get; init; }

    public required DateTimeOffset ValidTo { get; init; }

    /// <summary>The recorded reason. Access without a stated reason is refused.</summary>
    public required string Reason { get; init; }
}

public sealed record GrantRevocationRequestV1
{
    public int SchemaVersion { get; init; } = GrantContractV1.SchemaVersion;

    public required string IdentityId { get; init; }

    public required string GrantId { get; init; }
}

/// <summary>The resulting grant and the entitlement version every existing token must now beat.</summary>
public sealed record GrantResultV1
{
    public int SchemaVersion { get; init; } = GrantContractV1.SchemaVersion;

    public required string GrantId { get; init; }

    public required long EntitlementVersion { get; init; }

    public required DateTimeOffset OccurredAt { get; init; }
}
