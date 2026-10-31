using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CoreBanking.BuildingBlocks.Application.Messaging;
using CoreBanking.BuildingBlocks.Domain.Identity;
using CoreBanking.BuildingBlocks.Infrastructure.Messaging;
using CoreBanking.IdentityAccess.Contracts;
using CoreBanking.IdentityAccess.Domain.Access;
using CoreBanking.IdentityAccess.Infrastructure.Persistence;
using CoreBanking.IdentityAccess.Infrastructure.Persistence.Access;
using Microsoft.EntityFrameworkCore;

namespace CoreBanking.IdentityAccess.Infrastructure.Access;

/// <summary>
/// Grant administration for the approved Phase 1 catalogue: role assignment with contextual scope,
/// bounded temporary grants, segregation-of-duties evaluation, revocation and expiry. Every
/// mutation advances the actor's entitlement version and commits a durable invalidation event in
/// the same transaction, which is the only way existing tokens stop being trusted.
/// Decision: ADR-0009 and P1-E02-D10; slice 8.
/// </summary>
public sealed class GrantAdministrationService
{
    public const string Producer = "identity-access";
    public const string EventFamily = IdentityAccessContractV1.EntitlementEventFamily;
    public const int SchemaVersion = IdentityAccessContractV1.SchemaVersion;

    private readonly IdentityAccessDbContext _context;
    private readonly TimeProvider _timeProvider;

    public GrantAdministrationService(IdentityAccessDbContext context, TimeProvider timeProvider)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public async Task<GrantResultV1> AssignRoleAsync(
        InstitutionId institutionId,
        ActorId grantedBy,
        string identityId,
        string roleId,
        string? regionId,
        string? branchId,
        string? channelId,
        DateTimeOffset? validTo,
        string correlationId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(institutionId);
        ArgumentNullException.ThrowIfNull(grantedBy);
        ArgumentException.ThrowIfNullOrWhiteSpace(identityId);
        ArgumentException.ThrowIfNullOrWhiteSpace(roleId);

        DateTimeOffset now = _timeProvider.GetUtcNow();
        if (validTo is not null && validTo <= now)
        {
            throw new GrantAdministrationException(GrantContractV1.InvalidGrant, "The assignment end must be in the future.");
        }

        bool roleExists = await _context.Roles.AnyAsync(
            role => role.RoleId == roleId && role.Status == RecordStatus.Active,
            cancellationToken).ConfigureAwait(false);
        if (!roleExists)
        {
            throw new GrantAdministrationException(GrantContractV1.RoleNotFound, "The role is not available.");
        }

        string[] rolePermissions = await _context.RolePermissions
            .Where(permission => permission.RoleId == roleId)
            .Select(permission => permission.PermissionCode)
            .ToArrayAsync(cancellationToken).ConfigureAwait(false);

        HashSet<string> prospective = await EffectivePermissionsAsync(identityId, now, cancellationToken).ConfigureAwait(false);
        prospective.UnionWith(rolePermissions);

        await EnsureNoConflictAsync(prospective, cancellationToken).ConfigureAwait(false);

        // One assignment per identity and role: the key is (institution, identity, role), so a
        // repeated assignment updates its scope and validity rather than duplicating it.
        IdentityRoleRecord? existing = await _context.IdentityRoles
            .SingleOrDefaultAsync(assignment => assignment.IdentityId == identityId && assignment.RoleId == roleId, cancellationToken)
            .ConfigureAwait(false);

        if (existing is null)
        {
            _context.IdentityRoles.Add(new IdentityRoleRecord
            {
                InstitutionId = institutionId.Value,
                IdentityId = identityId,
                RoleId = roleId,
                RegionId = regionId,
                BranchId = branchId,
                ChannelId = channelId,
                ValidFrom = now,
                ValidTo = validTo,
                AssignedBy = grantedBy.Value,
            });
        }
        else
        {
            existing.RegionId = regionId;
            existing.BranchId = branchId;
            existing.ChannelId = channelId;
            existing.ValidFrom = now;
            existing.ValidTo = validTo;
            existing.AssignedBy = grantedBy.Value;
        }

        long version = await AdvanceVersionAsync(institutionId, identityId, GrantContractV1.RoleAssigned, roleId, correlationId, now, cancellationToken)
            .ConfigureAwait(false);

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return new GrantResultV1
        {
            GrantId = roleId,
            EntitlementVersion = version,
            OccurredAt = now,
        };
    }

    public async Task<GrantResultV1> GrantTemporaryPermissionAsync(
        InstitutionId institutionId,
        ActorId grantedBy,
        string identityId,
        string permissionCode,
        string? regionId,
        string? branchId,
        DateTimeOffset validTo,
        string reason,
        string correlationId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(institutionId);
        ArgumentNullException.ThrowIfNull(grantedBy);
        ArgumentException.ThrowIfNullOrWhiteSpace(identityId);
        ArgumentException.ThrowIfNullOrWhiteSpace(permissionCode);

        DateTimeOffset now = _timeProvider.GetUtcNow();
        if (validTo <= now)
        {
            throw new GrantAdministrationException(GrantContractV1.InvalidGrant, "A temporary grant needs a future end.");
        }

        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length < 10)
        {
            throw new GrantAdministrationException(GrantContractV1.InvalidGrant, "A recorded reason is required for a temporary grant.");
        }

        bool catalogueEntry = await _context.Permissions.AnyAsync(
            permission => permission.PermissionCode == permissionCode,
            cancellationToken).ConfigureAwait(false);
        if (!catalogueEntry)
        {
            throw new GrantAdministrationException(GrantContractV1.PermissionNotInCatalogue, "The permission is not in the catalogue.");
        }

        HashSet<string> prospective = await EffectivePermissionsAsync(identityId, now, cancellationToken).ConfigureAwait(false);
        prospective.Add(permissionCode);

        await EnsureNoConflictAsync(prospective, cancellationToken).ConfigureAwait(false);

        TemporaryGrantRecord grant = new()
        {
            InstitutionId = institutionId.Value,
            TemporaryGrantId = Guid.NewGuid().ToString("N"),
            IdentityId = identityId,
            PermissionCode = permissionCode,
            RegionId = regionId,
            BranchId = branchId,
            ValidFrom = now,
            ValidTo = validTo,
            GrantedBy = grantedBy.Value,
            Reason = reason.Trim(),
        };
        _context.TemporaryGrants.Add(grant);

        long version = await AdvanceVersionAsync(
            institutionId, identityId, GrantContractV1.TemporaryGrantAdded, grant.TemporaryGrantId, correlationId, now, cancellationToken)
            .ConfigureAwait(false);

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return new GrantResultV1
        {
            GrantId = grant.TemporaryGrantId,
            EntitlementVersion = version,
            OccurredAt = now,
        };
    }

    public async Task<long> RevokeRoleAsync(
        InstitutionId institutionId,
        ActorId revokedBy,
        string identityId,
        string roleId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(institutionId);
        ArgumentNullException.ThrowIfNull(revokedBy);
        ArgumentException.ThrowIfNullOrWhiteSpace(identityId);
        ArgumentException.ThrowIfNullOrWhiteSpace(roleId);

        IdentityRoleRecord? assignment = await _context.IdentityRoles
            .SingleOrDefaultAsync(record => record.IdentityId == identityId && record.RoleId == roleId, cancellationToken)
            .ConfigureAwait(false);
        if (assignment is null)
        {
            throw new GrantAdministrationException(GrantContractV1.GrantNotFound, "No matching role assignment exists.");
        }

        DateTimeOffset now = _timeProvider.GetUtcNow();
        _context.IdentityRoles.Remove(assignment);
        long version = await AdvanceVersionAsync(institutionId, identityId, GrantContractV1.RoleRevoked, roleId, correlationId, now, cancellationToken)
            .ConfigureAwait(false);
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return version;
    }

    public async Task<long> RevokeTemporaryGrantAsync(
        InstitutionId institutionId,
        ActorId revokedBy,
        string identityId,
        string grantId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(institutionId);
        ArgumentNullException.ThrowIfNull(revokedBy);

        TemporaryGrantRecord? grant = await _context.TemporaryGrants
            .SingleOrDefaultAsync(record => record.IdentityId == identityId && record.TemporaryGrantId == grantId, cancellationToken)
            .ConfigureAwait(false);
        if (grant is null)
        {
            throw new GrantAdministrationException(GrantContractV1.GrantNotFound, "No matching temporary grant exists.");
        }

        DateTimeOffset now = _timeProvider.GetUtcNow();
        _context.TemporaryGrants.Remove(grant);
        long version = await AdvanceVersionAsync(
            institutionId, identityId, GrantContractV1.TemporaryGrantRevoked, grantId, correlationId, now, cancellationToken)
            .ConfigureAwait(false);
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return version;
    }

    /// <summary>
    /// Closes lapsed temporary grants and advances each affected actor's version, so a consumer
    /// holding an older version denies rather than keeping a lapsed permission alive. The sweep
    /// ignores the tenant filter because a maintenance pass has no request context; every write
    /// still carries the institution of the row it is derived from.
    /// </summary>
    public async Task<int> ExpireDueTemporaryGrantsAsync(DateTimeOffset now, string correlationId, CancellationToken cancellationToken)
    {
        TemporaryGrantRecord[] due = await _context.TemporaryGrants
            .IgnoreQueryFilters()
            .Where(grant => grant.ExpiredAt == null && grant.ValidTo <= now)
            .ToArrayAsync(cancellationToken).ConfigureAwait(false);

        foreach (TemporaryGrantRecord grant in due)
        {
            grant.ExpiredAt = now;
            await AdvanceVersionAsync(
                InstitutionId.From(grant.InstitutionId),
                grant.IdentityId,
                GrantContractV1.TemporaryGrantExpired,
                grant.TemporaryGrantId,
                correlationId,
                now,
                cancellationToken).ConfigureAwait(false);
        }

        if (due.Length > 0)
        {
            await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return due.Length;
    }

    private async Task<HashSet<string>> EffectivePermissionsAsync(string identityId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        string[] fromRoles = await (
                from assignment in _context.IdentityRoles
                join permission in _context.RolePermissions
                    on new { assignment.InstitutionId, assignment.RoleId } equals new { permission.InstitutionId, permission.RoleId }
                where assignment.IdentityId == identityId
                    && assignment.ValidFrom <= now
                    && (assignment.ValidTo == null || assignment.ValidTo > now)
                select permission.PermissionCode)
            .ToArrayAsync(cancellationToken).ConfigureAwait(false);

        string[] fromTemporary = await _context.TemporaryGrants
            .Where(grant => grant.IdentityId == identityId
                && grant.ExpiredAt == null
                && grant.ValidFrom <= now
                && grant.ValidTo > now)
            .Select(grant => grant.PermissionCode)
            .ToArrayAsync(cancellationToken).ConfigureAwait(false);

        return new HashSet<string>(fromRoles.Concat(fromTemporary), StringComparer.Ordinal);
    }

    private async Task EnsureNoConflictAsync(HashSet<string> permissions, CancellationToken cancellationToken)
    {
        GrantConflict[] rules = await _context.ConflictRules
            .Select(rule => new GrantConflict(rule.PermissionCode, rule.ConflictingPermissionCode))
            .ToArrayAsync(cancellationToken).ConfigureAwait(false);

        GrantConflict? conflict = ConflictEvaluator.FindFirst(permissions, rules);
        if (conflict is not null)
        {
            throw new GrantAdministrationException(
                GrantContractV1.ConflictOfDuty,
                "The grant would create a prohibited combination of duties.");
        }
    }

    private async Task<long> AdvanceVersionAsync(
        InstitutionId institutionId,
        string identityId,
        string reasonCode,
        string sourceReference,
        string correlationId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        EntitlementVersionRecord? record = await _context.EntitlementVersions
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(
                version => version.IdentityId == identityId && version.InstitutionId == institutionId.Value,
                cancellationToken).ConfigureAwait(false);

        long previous = record?.Version ?? 0;
        long next = previous + 1;

        if (record is null)
        {
            _context.EntitlementVersions.Add(new EntitlementVersionRecord
            {
                InstitutionId = institutionId.Value,
                IdentityId = identityId,
                Version = next,
                UpdatedAt = now,
            });
        }
        else
        {
            record.Version = next;
            record.UpdatedAt = now;
        }

        string eventId = Guid.NewGuid().ToString("N");
        IntegrationEventEnvelope envelope = new()
        {
            EventId = eventId,
            EventType = IdentityAccessContractV1.EntitlementVersionAdvancedEventType,
            SchemaVersion = SchemaVersion,
            OccurredAt = now,
            InstitutionId = institutionId.Value,
            CorrelationId = correlationId,
            Producer = Producer,
            BusinessReference = identityId,
            Payload = EventContract.Serialize(new EntitlementVersionAdvancedV1
            {
                ActorId = identityId,
                InstitutionId = institutionId.Value,
                PreviousVersion = previous,
                NewVersion = next,
                ChangeReasonCode = reasonCode,
                SourceReference = sourceReference,
            }),
        };

        _context.OutboxMessages.Add(new OutboxRecord
        {
            InstitutionId = institutionId.Value,
            MessageId = Guid.NewGuid().ToString("N"),
            EventId = eventId,
            EventType = IdentityAccessContractV1.EntitlementVersionAdvancedEventType,
            SchemaVersion = SchemaVersion,
            Topic = EventContract.TopicFor(Producer, EventFamily, SchemaVersion),
            BusinessReference = identityId,
            CorrelationId = correlationId,
            Producer = Producer,
            Envelope = EventContract.Serialize(envelope),
            Status = OutboxStatus.Pending,
            Attempts = 0,
            NextAttemptAt = now,
            CreatedAt = now,
        });

        return next;
    }
}

/// <summary>Stable refusal with a contract code. It carries no role, identity or rule detail.</summary>
public sealed class GrantAdministrationException : Exception
{
    public GrantAdministrationException(string code, string message)
        : base(message)
    {
        Code = code;
    }

    public string Code { get; }
}
