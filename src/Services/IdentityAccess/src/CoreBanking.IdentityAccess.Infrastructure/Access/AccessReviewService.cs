using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CoreBanking.BuildingBlocks.Domain.Identity;
using CoreBanking.IdentityAccess.Contracts;
using CoreBanking.IdentityAccess.Infrastructure.Persistence;
using CoreBanking.IdentityAccess.Infrastructure.Persistence.Security;
using Microsoft.EntityFrameworkCore;

namespace CoreBanking.IdentityAccess.Infrastructure.Access;

public sealed class AccessReviewService
{
    private const int MaximumRows = 200;
    private readonly IdentityAccessDbContext _context;
    private readonly TimeProvider _timeProvider;

    public AccessReviewService(IdentityAccessDbContext context, TimeProvider timeProvider)
    {
        _context = context;
        _timeProvider = timeProvider;
    }

    public async Task<AccessReviewResultV1> ReviewAsync(
        InstitutionId institutionId,
        ActorId reviewer,
        string identityId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identityId);
        var roleRows = await (
                from assignment in _context.IdentityRoles.AsNoTracking()
                join version in _context.RoleVersions.AsNoTracking()
                    on new { assignment.InstitutionId, assignment.RoleId, Version = assignment.RoleVersion }
                    equals new { version.InstitutionId, version.RoleId, version.Version }
                where assignment.IdentityId == identityId
                orderby version.RoleCode
                select new { assignment, version })
            .Take(MaximumRows + 1)
            .ToArrayAsync(cancellationToken).ConfigureAwait(false);
        var temporary = await _context.TemporaryGrants.AsNoTracking()
            .Where(grant => grant.IdentityId == identityId)
            .OrderBy(grant => grant.PermissionCode)
            .Take(MaximumRows + 1)
            .ToArrayAsync(cancellationToken).ConfigureAwait(false);
        if (roleRows.Length > MaximumRows || temporary.Length > MaximumRows)
        {
            throw new InvalidOperationException("The access review exceeds the bounded result limit.");
        }

        var roleIds = roleRows.Select(row => row.assignment.RoleId).ToArray();
        var permissions = await _context.RoleVersionPermissions.AsNoTracking()
            .Where(permission => roleIds.Contains(permission.RoleId))
            .ToArrayAsync(cancellationToken).ConfigureAwait(false);
        DateTimeOffset now = _timeProvider.GetUtcNow();
        _context.OperatorActionAuditEvents.Add(new OperatorActionAuditRecord
        {
            InstitutionId = institutionId.Value,
            AuditEventId = Guid.NewGuid().ToString("N"),
            ActorId = reviewer.Value,
            Action = "identity_access_reviewed",
            ResourceType = "identity",
            ResourceId = identityId,
            Reason = "authorised_access_review",
            CorrelationId = correlationId,
            OccurredAt = now,
        });
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return new AccessReviewResultV1(
            1,
            identityId,
            now,
            roleRows.Select(row => new AccessReviewRoleV1(
                row.assignment.RoleId,
                row.version.RoleCode,
                row.version.RoleName,
                row.assignment.RoleVersion,
                permissions.Where(permission => permission.RoleId == row.assignment.RoleId
                        && permission.RoleVersion == row.assignment.RoleVersion)
                    .Select(permission => permission.PermissionCode).Order().ToArray(),
                row.assignment.RegionId,
                row.assignment.BranchId,
                row.assignment.ChannelId,
                row.assignment.ValidFrom,
                row.assignment.ValidTo,
                row.assignment.AssignedBy)).ToArray(),
            temporary.Select(grant => new AccessReviewTemporaryGrantV1(
                grant.TemporaryGrantId,
                grant.PermissionCode,
                grant.RegionId,
                grant.BranchId,
                grant.ValidFrom,
                grant.ValidTo,
                grant.GrantedBy,
                grant.Reason,
                grant.ExpiredAt)).ToArray());
    }
}
