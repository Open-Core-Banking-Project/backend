using System;
using System.Collections.Generic;

namespace CoreBanking.IdentityAccess.Contracts;

public sealed record AccessReviewRoleV1(
    string RoleId,
    string RoleCode,
    string RoleName,
    long RoleVersion,
    IReadOnlyList<string> Permissions,
    string? RegionId,
    string? BranchId,
    string? ChannelId,
    DateTimeOffset ValidFrom,
    DateTimeOffset? ValidTo,
    string AssignedBy);

public sealed record AccessReviewTemporaryGrantV1(
    string GrantId,
    string PermissionCode,
    string? RegionId,
    string? BranchId,
    DateTimeOffset ValidFrom,
    DateTimeOffset ValidTo,
    string GrantedBy,
    string Reason,
    DateTimeOffset? ExpiredAt);

public sealed record AccessReviewResultV1(
    int SchemaVersion,
    string IdentityId,
    DateTimeOffset ReviewedAt,
    IReadOnlyList<AccessReviewRoleV1> Roles,
    IReadOnlyList<AccessReviewTemporaryGrantV1> TemporaryGrants);
