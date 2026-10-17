using System;
using System.Collections.Generic;
using System.Linq;
using CoreBanking.BuildingBlocks.Application.Security;
using CoreBanking.BuildingBlocks.Domain.Identity;
using Microsoft.EntityFrameworkCore;

namespace CoreBanking.IdentityAccess.Infrastructure.Persistence;

/// <summary>
/// Serves effective permissions from <c>identity_access_db</c>. Role assignments and temporary
/// grants are combined into one snapshot per actor; a removed role or an incremented entitlement
/// version therefore takes effect on the next evaluation without waiting for a token to expire.
/// The store also states the institution predicate explicitly: the global query filter is the
/// boundary, and the explicit predicate keeps the intent visible at the query.
/// Decision: ADR-0004, ADR-0009.
/// </summary>
public sealed class RelationalEntitlementStore : IEffectivePermissionSource, IEntitlementVersionSource
{
    private readonly IdentityAccessDbContext _context;
    private readonly TimeProvider _timeProvider;

    public RelationalEntitlementStore(IdentityAccessDbContext context, TimeProvider timeProvider)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public long GetCurrentVersion(ActorId actorId, InstitutionId institutionId)
    {
        ArgumentNullException.ThrowIfNull(actorId);
        ArgumentNullException.ThrowIfNull(institutionId);

        long? version = _context.EntitlementVersions
            .AsNoTracking()
            .Where(record => record.InstitutionId == institutionId.Value && record.IdentityId == actorId.Value)
            .Select(record => (long?)record.Version)
            .SingleOrDefault();

        return version ?? 0;
    }

    public EntitlementSnapshot? FindSnapshot(ActorId actorId, InstitutionId institutionId)
    {
        ArgumentNullException.ThrowIfNull(actorId);
        ArgumentNullException.ThrowIfNull(institutionId);

        long version = GetCurrentVersion(actorId, institutionId);
        if (version == 0L)
        {
            // No entitlement record means no grants, never an implicit set.
            return null;
        }

        DateTimeOffset now = _timeProvider.GetUtcNow();
        HashSet<PermissionGrant> grants = new();

        List<RoleGrantRow> roleGrants = (
            from assignment in _context.IdentityRoles.AsNoTracking()
            join rolePermission in _context.RolePermissions.AsNoTracking()
                on new { assignment.InstitutionId, assignment.RoleId } equals new { rolePermission.InstitutionId, rolePermission.RoleId }
            where assignment.InstitutionId == institutionId.Value
                && assignment.IdentityId == actorId.Value
                && assignment.ValidFrom <= now
                && (assignment.ValidTo == null || assignment.ValidTo > now)
            select new RoleGrantRow(
                rolePermission.PermissionCode,
                assignment.RegionId,
                assignment.BranchId,
                assignment.ChannelId,
                assignment.ValidTo))
            .ToList();

        foreach (RoleGrantRow row in roleGrants)
        {
            grants.Add(new PermissionGrant
            {
                Permission = PermissionIdentifier.Parse(row.PermissionCode),
                Scope = new GrantScope
                {
                    InstitutionId = institutionId,
                    RegionId = row.RegionId,
                    BranchId = row.BranchId,
                    ChannelId = row.ChannelId,
                },
                ValidTo = row.ValidTo,
            });
        }

        List<TemporaryGrantRow> temporaryGrants = _context.TemporaryGrants
            .AsNoTracking()
            .Where(grant => grant.InstitutionId == institutionId.Value
                && grant.IdentityId == actorId.Value
                && grant.ValidFrom <= now
                && grant.ValidTo > now)
            .Select(grant => new TemporaryGrantRow(grant.PermissionCode, grant.RegionId, grant.BranchId, grant.ValidTo))
            .ToList();

        foreach (TemporaryGrantRow row in temporaryGrants)
        {
            grants.Add(new PermissionGrant
            {
                Permission = PermissionIdentifier.Parse(row.PermissionCode),
                Scope = new GrantScope
                {
                    InstitutionId = institutionId,
                    RegionId = row.RegionId,
                    BranchId = row.BranchId,
                },
                ValidTo = row.ValidTo,
            });
        }

        return new EntitlementSnapshot
        {
            ActorId = actorId,
            InstitutionId = institutionId,
            Version = version,
            Grants = grants,
            CapturedAt = now,
        };
    }

    private sealed record RoleGrantRow(
        string PermissionCode,
        string? RegionId,
        string? BranchId,
        string? ChannelId,
        DateTimeOffset? ValidTo);

    private sealed record TemporaryGrantRow(
        string PermissionCode,
        string? RegionId,
        string? BranchId,
        DateTimeOffset ValidTo);
}
