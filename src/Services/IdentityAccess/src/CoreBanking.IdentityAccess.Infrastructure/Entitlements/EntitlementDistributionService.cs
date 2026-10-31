using System;
using System.Linq;
using CoreBanking.BuildingBlocks.Application.Security;
using CoreBanking.BuildingBlocks.Domain.Identity;
using CoreBanking.IdentityAccess.Contracts;

namespace CoreBanking.IdentityAccess.Infrastructure.Entitlements;

/// <summary>
/// Either a complete, version-consistent snapshot or a stable denial code. There is no partial
/// allow: an incomplete or regressed answer is a denial for every consumer.
/// Decision: ADR-0009; slice 9.
/// </summary>
public sealed record EntitlementDistributionResult(EntitlementSnapshotV1? Snapshot, string? DenialCode)
{
    public bool IsAllowed => Snapshot is not null;

    public static EntitlementDistributionResult Denied(string code) => new(null, code);
}

/// <summary>
/// Distributes the effective permissions of one actor at one monotonic version. Today the caller
/// is always the actor themselves; a service-authenticated variant is required before another
/// service can ask on an actor's behalf (slice 13).
/// </summary>
public sealed class EntitlementDistributionService
{
    private readonly IEffectivePermissionSource _permissions;
    private readonly IEntitlementVersionSource _versions;
    private readonly TimeProvider _timeProvider;

    public EntitlementDistributionService(
        IEffectivePermissionSource permissions,
        IEntitlementVersionSource versions,
        TimeProvider timeProvider)
    {
        _permissions = permissions ?? throw new ArgumentNullException(nameof(permissions));
        _versions = versions ?? throw new ArgumentNullException(nameof(versions));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public EntitlementDistributionResult GetSnapshot(
        AuthenticatedActor? caller,
        ActorId requestedActorId,
        InstitutionId requestedInstitutionId,
        long minimumVersion)
    {
        ArgumentNullException.ThrowIfNull(requestedActorId);
        ArgumentNullException.ThrowIfNull(requestedInstitutionId);

        if (caller is null)
        {
            return EntitlementDistributionResult.Denied(AuthorisationCodes.MissingAuthentication);
        }

        if (!string.Equals(caller.Id.Value, requestedActorId.Value, StringComparison.Ordinal))
        {
            return EntitlementDistributionResult.Denied(EntitlementDistributionV1.ActorMismatch);
        }

        if (!string.Equals(caller.InstitutionId.Value, requestedInstitutionId.Value, StringComparison.Ordinal))
        {
            return EntitlementDistributionResult.Denied(EntitlementDistributionV1.InstitutionMismatch);
        }

        long currentVersion = _versions.GetCurrentVersion(requestedActorId, requestedInstitutionId);
        if (currentVersion < minimumVersion)
        {
            return EntitlementDistributionResult.Denied(EntitlementDistributionV1.VersionRegressed);
        }

        EntitlementSnapshot? snapshot = _permissions.FindSnapshot(requestedActorId, requestedInstitutionId);
        bool complete = snapshot is not null && snapshot.Version == currentVersion;

        return new EntitlementDistributionResult(
            new EntitlementSnapshotV1
            {
                ActorId = requestedActorId.Value,
                InstitutionId = requestedInstitutionId.Value,
                Version = currentVersion,
                IsComplete = complete,
                CapturedAt = snapshot?.CapturedAt ?? _timeProvider.GetUtcNow(),
                Grants = snapshot is null || snapshot.Version != currentVersion
                    ? []
                    : snapshot.Grants.Select(ToContract).ToArray(),
            },
            null);
    }

    private static PermissionGrantV1 ToContract(PermissionGrant grant) => new()
    {
        Permission = grant.Permission.Value,
        Scope = new GrantScopeV1
        {
            InstitutionId = grant.Scope.InstitutionId.Value,
            LegalEntityId = grant.Scope.LegalEntityId,
            RegionId = grant.Scope.RegionId,
            BranchId = grant.Scope.BranchId,
            ChannelId = grant.Scope.ChannelId,
            CustomerId = grant.Scope.CustomerId,
            AccountId = grant.Scope.AccountId,
            MandateId = grant.Scope.MandateId,
            Currency = grant.Scope.Currency,
            MaximumAmount = grant.Scope.MaximumAmount,
        },
        ValidTo = grant.ValidTo,
    };
}
