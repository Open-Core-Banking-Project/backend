using System;
using CoreBanking.BuildingBlocks.Application.Persistence;

namespace CoreBanking.IdentityAccess.Infrastructure.Persistence.Access;

/// <summary>
/// A versioned, institution-scoped bundle of permissions. Application code never tests a role
/// name; the role exists so an administrator can assign a stable set of permissions at once.
/// Table: <c>access.roles</c>. Decision: ADR-0009.
/// </summary>
public sealed class RoleRecord : ITenantOwnedEntity
{
    public required string InstitutionId { get; set; }

    public required string RoleId { get; set; }

    public required string RoleCode { get; set; }

    public required string RoleName { get; set; }

    public long Version { get; set; }

    public RecordStatus Status { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>
/// The platform permission catalogue. Permission codes are permanent identifiers and are shared
/// by every institution, so this is the one table in the database that is deliberately not
/// tenant-owned; institutions reference codes, they do not define them.
/// Table: <c>access.permissions</c>. Decision: ADR-0009.
/// </summary>
public sealed class PermissionRecord
{
    public required string PermissionCode { get; set; }

    public required string Description { get; set; }

    public DateTimeOffset IntroducedAt { get; set; }
}

/// <summary>The permissions a role grants. Table: <c>access.role_permissions</c>. Decision: ADR-0009.</summary>
public sealed class RolePermissionRecord : ITenantOwnedEntity
{
    public required string InstitutionId { get; set; }

    public required string RoleId { get; set; }

    public required string PermissionCode { get; set; }
}

/// <summary>
/// One role assignment, with the contextual dimensions that narrow it. The institution column is
/// part of the key and of both foreign keys, so an assignment cannot reference another tenant's
/// identity or role. Table: <c>access.identity_roles</c>. Decision: ADR-0009, ADR-0004.
/// </summary>
public sealed class IdentityRoleRecord : ITenantOwnedEntity
{
    public required string InstitutionId { get; set; }

    public required string IdentityId { get; set; }

    public required string RoleId { get; set; }

    public string? RegionId { get; set; }

    public string? BranchId { get; set; }

    public string? ChannelId { get; set; }

    public DateTimeOffset ValidFrom { get; set; }

    public DateTimeOffset? ValidTo { get; set; }

    public required string AssignedBy { get; set; }
}

/// <summary>
/// The catalogue of resource scopes a grant may name, including the parent relationship that
/// makes a branch scope narrower than its region. Table: <c>access.resource_scopes</c>. Decision: ADR-0009.
/// </summary>
public sealed class ResourceScopeRecord : ITenantOwnedEntity
{
    public required string InstitutionId { get; set; }

    public required string ResourceScopeId { get; set; }

    public required string ScopeType { get; set; }

    public required string ScopeValue { get; set; }

    public string? ParentResourceScopeId { get; set; }
}

/// <summary>
/// An additional condition attached to a role permission, such as a monetary limit or a channel
/// restriction. Table: <c>access.permission_conditions</c>. Decision: ADR-0009.
/// </summary>
public sealed class PermissionConditionRecord : ITenantOwnedEntity
{
    public required string InstitutionId { get; set; }

    public required string PermissionConditionId { get; set; }

    public required string RoleId { get; set; }

    public required string PermissionCode { get; set; }

    public required string ConditionType { get; set; }

    public required string ConditionValue { get; set; }
}

/// <summary>
/// A rule that two permissions, or two roles, must not be held by one actor. Segmentation of
/// duties is decided here rather than hard-coded. Table: <c>access.conflict_rules</c>. Decision: ADR-0009.
/// </summary>
public sealed class ConflictRuleRecord : ITenantOwnedEntity
{
    public required string InstitutionId { get; set; }

    public required string ConflictRuleId { get; set; }

    public required string PermissionCode { get; set; }

    public required string ConflictingPermissionCode { get; set; }

    public required string RuleType { get; set; }

    public string? Description { get; set; }
}

/// <summary>
/// A time-boxed grant of one permission outside the actor's roles, with the reason and the
/// granting actor recorded. Table: <c>access.temporary_grants</c>. Decision: ADR-0009.
/// </summary>
public sealed class TemporaryGrantRecord : ITenantOwnedEntity
{
    public required string InstitutionId { get; set; }

    public required string TemporaryGrantId { get; set; }

    public required string IdentityId { get; set; }

    public required string PermissionCode { get; set; }

    public string? RegionId { get; set; }

    public string? BranchId { get; set; }

    public DateTimeOffset ValidFrom { get; set; }

    public DateTimeOffset ValidTo { get; set; }

    public required string GrantedBy { get; set; }

    public required string Reason { get; set; }

    /// <summary>Set by the expiry sweep. A lapsed grant keeps its history and stops being effective.</summary>
    public DateTimeOffset? ExpiredAt { get; set; }
}

/// <summary>
/// The current entitlement version of one actor. Incrementing the row is what invalidates every
/// token issued earlier, which is the invalidation path ADR-0009 requires.
/// Table: <c>access.entitlement_versions</c>. Decision: ADR-0009.
/// </summary>
public sealed class EntitlementVersionRecord : ITenantOwnedEntity
{
    public required string InstitutionId { get; set; }

    public required string IdentityId { get; set; }

    public long Version { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
