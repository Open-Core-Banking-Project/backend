using System.Collections.Generic;
using CoreBanking.BuildingBlocks.Application.Security;

namespace CoreBanking.IdentityAccess.Application.Permissions;

/// <summary>
/// The foundation permission catalogue: synthetic administrative permissions used to prove
/// the model. This is deliberately not the complete production catalogue, and no permission
/// here grants a wildcard.
/// Decision: docs/adr/ADR-0009-roles-permissions-contextual-scopes-and-invalidation.md
/// </summary>
public static class IdentityPermissions
{
    public static PermissionIdentifier PlatformFoundationExecute { get; } = PermissionIdentifier.Parse("platform.foundation.execute");

    public static PermissionIdentifier InstitutionTenantCreate { get; } = PermissionIdentifier.Parse("institution.tenant.create");

    public static PermissionIdentifier IdentityRoleCreate { get; } = PermissionIdentifier.Parse("identity.role.create");

    public static PermissionIdentifier IdentityPermissionAssign { get; } = PermissionIdentifier.Parse("identity.permission.assign");

    public static PermissionIdentifier IdentityAccessReview { get; } = PermissionIdentifier.Parse("identity.access.review");

    public static PermissionIdentifier AuditEventView { get; } = PermissionIdentifier.Parse("audit.event.view");

    public static IReadOnlySet<PermissionIdentifier> All { get; } = new HashSet<PermissionIdentifier>
    {
        PlatformFoundationExecute,
        InstitutionTenantCreate,
        IdentityRoleCreate,
        IdentityPermissionAssign,
        IdentityAccessReview,
        AuditEventView,
    };
}
