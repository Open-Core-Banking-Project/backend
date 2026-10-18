using System;
using System.Collections.Generic;
using CoreBanking.BuildingBlocks.Domain.Identity;

namespace CoreBanking.BuildingBlocks.Application.Security;

/// <summary>One granted permission with its contextual scope and validity.</summary>
public sealed record PermissionGrant
{
    public required PermissionIdentifier Permission { get; init; }

    public required GrantScope Scope { get; init; }

    /// <summary>Optional expiry, used by temporary grants.</summary>
    public DateTimeOffset? ValidTo { get; init; }

    public bool IsValidAt(DateTimeOffset now) => ValidTo is null || ValidTo > now;
}

/// <summary>
/// The effective permissions of one actor in one institution at one entitlement version.
/// Decision: ADR-0009.
/// </summary>
public sealed record EntitlementSnapshot
{
    public required ActorId ActorId { get; init; }

    public required InstitutionId InstitutionId { get; init; }

    public required long Version { get; init; }

    public required IReadOnlySet<PermissionGrant> Grants { get; init; }

    public DateTimeOffset CapturedAt { get; init; }
}

/// <summary>
/// Supplies the effective permissions for an actor. Implementations are replaceable; the
/// relational implementation backed by `identity_access_db` arrives with the persistence
/// slice of this epic.
/// Decision: ADR-0009.
/// </summary>
public interface IEffectivePermissionSource
{
    EntitlementSnapshot? FindSnapshot(ActorId actorId, InstitutionId institutionId);
}

/// <summary>
/// Supplies the current entitlement version so a stale token is detected and refused.
/// Decision: ADR-0009.
/// </summary>
public interface IEntitlementVersionSource
{
    long GetCurrentVersion(ActorId actorId, InstitutionId institutionId);
}
