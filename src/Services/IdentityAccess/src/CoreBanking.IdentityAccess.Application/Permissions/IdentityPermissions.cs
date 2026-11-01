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

    public static PermissionIdentifier IdentityCreate { get; } = PermissionIdentifier.Parse("identity.identity.create");

    public static PermissionIdentifier IdentityView { get; } = PermissionIdentifier.Parse("identity.identity.view");

    public static PermissionIdentifier IdentityUpdate { get; } = PermissionIdentifier.Parse("identity.identity.update");

    public static PermissionIdentifier AuditEventView { get; } = PermissionIdentifier.Parse("audit.event.view");

    /// <summary>Capability to ask whether the caller may decide another actor's request. Epic 02 slice 10.</summary>
    public static PermissionIdentifier ApprovalEligibilityReview { get; } = PermissionIdentifier.Parse("identity.approval.review");

    /// <summary>Capability to manage the caller's own approved second factors and step-up evidence. Epic 02 slice 6.</summary>
    public static PermissionIdentifier MfaManage { get; } = PermissionIdentifier.Parse("identity.mfa.manage");

    /// <summary>Capability to register, list and revoke the caller's own trusted devices. Epic 02 slice 11.</summary>
    public static PermissionIdentifier DeviceSelfManage { get; } = PermissionIdentifier.Parse("identity.device.manage");

    /// <summary>Capability for a support officer to read another identity's devices. Epic 02 slice 11.</summary>
    public static PermissionIdentifier DeviceView { get; } = PermissionIdentifier.Parse("identity.device.view");

    /// <summary>Capability for a security operator to revoke any device in the institution. Epic 02 slice 11.</summary>
    public static PermissionIdentifier DeviceRevoke { get; } = PermissionIdentifier.Parse("identity.device.revoke");

    public static IReadOnlySet<PermissionIdentifier> All { get; } = new HashSet<PermissionIdentifier>
    {
        PlatformFoundationExecute,
        InstitutionTenantCreate,
        IdentityRoleCreate,
        IdentityPermissionAssign,
        IdentityAccessReview,
        IdentityCreate,
        IdentityView,
        IdentityUpdate,
        AuditEventView,
        ApprovalEligibilityReview,
        MfaManage,
        DeviceSelfManage,
        DeviceView,
        DeviceRevoke,
    };
}
