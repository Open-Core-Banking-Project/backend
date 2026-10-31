using System;
using System.Collections.Generic;

namespace CoreBanking.IdentityAccess.Contracts;

/// <summary>Stable values for role drafting. Decision: ADR-0009, P1-E02-D09 and P1-E02-D10.</summary>
public static class RoleContractV1
{
    public const int SchemaVersion = IdentityAccessContractV1.SchemaVersion;

    public const string StatusDraft = "draft";
    public const string Simulated = "simulated";

    public const string InvalidRole = "request.invalid_role";
    public const string DuplicateRole = "identity.role_exists";
    public const string RoleNotFound = "identity.role_not_found";
    public const string RoleNotDraft = "identity.role_not_editable";
    public const string PermissionNotInCatalogue = "identity.permission_not_found";
    public const string RoleConflictsWithItself = "identity.role_conflict";
}

/// <summary>A role draft is inert and cannot be granted until it is published.</summary>
public sealed record RoleDraftRequestV1
{
    public int SchemaVersion { get; init; } = RoleContractV1.SchemaVersion;

    public required string RoleCode { get; init; }

    public required string RoleName { get; init; }

    public required IReadOnlyList<string> Permissions { get; init; }
}

/// <summary>Updates a draft in place. The code is immutable once created.</summary>
public sealed record RoleUpdateRequestV1
{
    public int SchemaVersion { get; init; } = RoleContractV1.SchemaVersion;

    public required string RoleName { get; init; }

    public required IReadOnlyList<string> Permissions { get; init; }
}

/// <summary>Simulates a proposed permission set before it is saved.</summary>
public sealed record RoleSimulationRequestV1
{
    public int SchemaVersion { get; init; } = RoleContractV1.SchemaVersion;

    public required IReadOnlyList<string> Permissions { get; init; }
}

public sealed record RoleDraftResultV1
{
    public int SchemaVersion { get; init; } = RoleContractV1.SchemaVersion;

    public required string RoleId { get; init; }

    public required string RoleCode { get; init; }

    public required string RoleName { get; init; }

    public required long Version { get; init; }

    public required string Status { get; init; }

    public required IReadOnlyList<string> Permissions { get; init; }
}

/// <summary>One prohibited pair inside the simulated permission set.</summary>
public sealed record RoleConflictV1
{
    public required string PermissionCode { get; init; }

    public required string ConflictingPermissionCode { get; init; }
}

/// <summary>
/// The simulation answer. <see cref="IsPublishable"/> is true only when every code is in the
/// catalogue, the set is non-empty and no prohibited pair is present.
/// </summary>
public sealed record RoleSimulationResultV1
{
    public int SchemaVersion { get; init; } = RoleContractV1.SchemaVersion;

    public required string Status { get; init; }

    public required IReadOnlyList<string> Permissions { get; init; }

    public required IReadOnlyList<string> UnknownPermissions { get; init; }

    public required IReadOnlyList<RoleConflictV1> Conflicts { get; init; }

    public required bool IsPublishable { get; init; }
}
