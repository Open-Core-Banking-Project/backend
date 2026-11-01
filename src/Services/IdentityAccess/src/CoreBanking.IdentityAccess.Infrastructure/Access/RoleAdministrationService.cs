using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CoreBanking.BuildingBlocks.Application.Security;
using CoreBanking.BuildingBlocks.Domain.Identity;
using CoreBanking.IdentityAccess.Contracts;
using CoreBanking.IdentityAccess.Domain.Access;
using CoreBanking.IdentityAccess.Infrastructure.Persistence;
using CoreBanking.IdentityAccess.Infrastructure.Persistence.Access;
using Microsoft.EntityFrameworkCore;

namespace CoreBanking.IdentityAccess.Infrastructure.Access;

/// <summary>
/// Role drafting and simulation over the approved catalogue. A draft stays inert: grants accept
/// only active roles, so an unpublished draft can never grant access. Publication through the
/// approval path is the remaining part of slice 7.
/// Decision: ADR-0009, P1-E02-D09 and P1-E02-D10.
/// </summary>
public sealed class RoleAdministrationService
{
    private readonly IdentityAccessDbContext _context;
    private readonly TimeProvider _timeProvider;

    public RoleAdministrationService(IdentityAccessDbContext context, TimeProvider timeProvider)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public async Task<RoleDraftResultV1> CreateDraftAsync(
        InstitutionId institutionId,
        ActorId createdBy,
        string roleCode,
        string roleName,
        IReadOnlyList<string> permissions,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(institutionId);
        ArgumentNullException.ThrowIfNull(createdBy);

        string code = NormaliseCode(roleCode);
        string name = RoleDraftRules.NormaliseName(roleName);
        IReadOnlyList<string> normalised = RoleDraftRules.NormalisePermissions(permissions);

        bool duplicate = await _context.Roles.AnyAsync(role => role.RoleCode == code, cancellationToken).ConfigureAwait(false);
        if (duplicate)
        {
            throw new RoleAdministrationException(RoleContractV1.DuplicateRole, "A role with this code already exists.");
        }

        await ValidatePermissionsAsync(normalised, cancellationToken).ConfigureAwait(false);

        DateTimeOffset now = _timeProvider.GetUtcNow();
        RoleRecord role = new()
        {
            InstitutionId = institutionId.Value,
            RoleId = Guid.NewGuid().ToString("N"),
            RoleCode = code,
            RoleName = name,
            Version = 1,
            Status = RecordStatus.Pending,
            CreatedAt = now,
            UpdatedAt = now,
        };
        _context.Roles.Add(role);
        AddPermissions(institutionId, role.RoleId, normalised);

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return ToResult(role, normalised);
    }

    public async Task<RoleDraftResultV1> UpdateDraftAsync(
        InstitutionId institutionId,
        string roleId,
        string roleName,
        IReadOnlyList<string> permissions,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(institutionId);

        RoleRecord role = await LoadEditableDraftAsync(roleId, cancellationToken).ConfigureAwait(false);
        string name = RoleDraftRules.NormaliseName(roleName);
        IReadOnlyList<string> normalised = RoleDraftRules.NormalisePermissions(permissions);

        await ValidatePermissionsAsync(normalised, cancellationToken).ConfigureAwait(false);

        RolePermissionRecord[] existing = await _context.RolePermissions
            .Where(permission => permission.RoleId == roleId)
            .ToArrayAsync(cancellationToken).ConfigureAwait(false);
        _context.RolePermissions.RemoveRange(existing);

        role.RoleName = name;
        role.Version += 1;
        role.UpdatedAt = _timeProvider.GetUtcNow();
        AddPermissions(institutionId, role.RoleId, normalised);

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return ToResult(role, normalised);
    }

    public async Task<RoleDraftResultV1> GetAsync(InstitutionId institutionId, string roleId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(institutionId);

        RoleRecord? role = await _context.Roles
            .SingleOrDefaultAsync(record => record.RoleId == roleId, cancellationToken).ConfigureAwait(false);
        if (role is null)
        {
            throw new RoleAdministrationException(RoleContractV1.RoleNotFound, "The role is not available.");
        }

        string[] permissions = await _context.RolePermissions
            .Where(permission => permission.RoleId == roleId)
            .Select(permission => permission.PermissionCode)
            .OrderBy(permission => permission)
            .ToArrayAsync(cancellationToken).ConfigureAwait(false);

        return ToResult(role, permissions);
    }

    /// <summary>
    /// Simulates a proposed permission set against the catalogue and the conflict rules, so a draft
    /// can be corrected before it is stored or sent for approval.
    /// </summary>
    public async Task<RoleSimulationResultV1> SimulateAsync(IReadOnlyList<string> permissions, CancellationToken cancellationToken)
    {
        IReadOnlyList<string> normalised = RoleDraftRules.NormalisePermissions(permissions);
        HashSet<string> catalogue = await CatalogueAsync(cancellationToken).ConfigureAwait(false);
        IReadOnlyList<string> unknown = RoleDraftRules.UnknownPermissions(normalised, catalogue);
        IReadOnlyList<GrantConflict> conflicts = await SelfConflictsAsync(normalised, cancellationToken).ConfigureAwait(false);

        return new RoleSimulationResultV1
        {
            Status = RoleContractV1.Simulated,
            Permissions = normalised,
            UnknownPermissions = unknown,
            Conflicts = conflicts
                .Select(conflict => new RoleConflictV1
                {
                    PermissionCode = conflict.PermissionCode,
                    ConflictingPermissionCode = conflict.ConflictingPermissionCode,
                })
                .ToArray(),
            IsPublishable = normalised.Count > 0 && unknown.Count == 0 && conflicts.Count == 0,
        };
    }

    private async Task<RoleRecord> LoadEditableDraftAsync(string roleId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(roleId);

        RoleRecord? role = await _context.Roles
            .SingleOrDefaultAsync(record => record.RoleId == roleId, cancellationToken).ConfigureAwait(false);
        if (role is null)
        {
            throw new RoleAdministrationException(RoleContractV1.RoleNotFound, "The role is not available.");
        }

        if (role.Status != RecordStatus.Pending)
        {
            throw new RoleAdministrationException(RoleContractV1.RoleNotDraft, "Only a draft role can be edited.");
        }

        return role;
    }

    private async Task ValidatePermissionsAsync(IReadOnlyList<string> permissions, CancellationToken cancellationToken)
    {
        foreach (string permission in permissions)
        {
            if (!IsPermissionIdentifier(permission))
            {
                throw new RoleAdministrationException(RoleContractV1.InvalidRole, "A permission identifier is malformed.");
            }
        }

        HashSet<string> catalogue = await CatalogueAsync(cancellationToken).ConfigureAwait(false);
        if (RoleDraftRules.UnknownPermissions(permissions, catalogue).Count > 0)
        {
            throw new RoleAdministrationException(RoleContractV1.PermissionNotInCatalogue, "A permission is not in the catalogue.");
        }

        IReadOnlyList<GrantConflict> conflicts = await SelfConflictsAsync(permissions, cancellationToken).ConfigureAwait(false);
        if (conflicts.Count > 0)
        {
            throw new RoleAdministrationException(
                RoleContractV1.RoleConflictsWithItself,
                "The role would contain a prohibited combination of duties.");
        }
    }

    private async Task<HashSet<string>> CatalogueAsync(CancellationToken cancellationToken) =>
        new(await _context.Permissions
            .Select(permission => permission.PermissionCode)
            .ToArrayAsync(cancellationToken).ConfigureAwait(false), StringComparer.Ordinal);

    private async Task<IReadOnlyList<GrantConflict>> SelfConflictsAsync(IReadOnlyList<string> permissions, CancellationToken cancellationToken)
    {
        GrantConflict[] rules = await _context.ConflictRules
            .Select(rule => new GrantConflict(rule.PermissionCode, rule.ConflictingPermissionCode))
            .ToArrayAsync(cancellationToken).ConfigureAwait(false);

        return RoleDraftRules.SelfConflicts(permissions, rules);
    }

    private void AddPermissions(InstitutionId institutionId, string roleId, IReadOnlyList<string> permissions)
    {
        foreach (string permission in permissions)
        {
            _context.RolePermissions.Add(new RolePermissionRecord
            {
                InstitutionId = institutionId.Value,
                RoleId = roleId,
                PermissionCode = permission,
            });
        }
    }

    private static string NormaliseCode(string roleCode)
    {
        try
        {
            return RoleDraftRules.NormaliseCode(roleCode);
        }
        catch (ArgumentException exception)
        {
            throw new RoleAdministrationException(RoleContractV1.InvalidRole, exception.Message);
        }
    }

    private static bool IsPermissionIdentifier(string permission)
    {
        if (string.IsNullOrWhiteSpace(permission))
        {
            return false;
        }

        try
        {
            PermissionIdentifier.Parse(permission);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static RoleDraftResultV1 ToResult(RoleRecord role, IReadOnlyList<string> permissions) => new()
    {
        RoleId = role.RoleId,
        RoleCode = role.RoleCode,
        RoleName = role.RoleName,
        Version = role.Version,
        Status = role.Status == RecordStatus.Pending ? RoleContractV1.StatusDraft : role.Status.ToString().ToLowerInvariant(),
        Permissions = permissions,
    };
}

/// <summary>Stable refusal with a contract code. It carries no role or permission detail.</summary>
public sealed class RoleAdministrationException : Exception
{
    public RoleAdministrationException(string code, string message)
        : base(message)
    {
        Code = code;
    }

    public string Code { get; }
}
