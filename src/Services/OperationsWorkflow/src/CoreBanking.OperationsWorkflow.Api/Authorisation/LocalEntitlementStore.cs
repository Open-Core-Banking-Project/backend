using System;
using System.Collections.Generic;
using CoreBanking.BuildingBlocks.Application.Security;
using CoreBanking.BuildingBlocks.Domain.Identity;

namespace CoreBanking.OperationsWorkflow.Api.Authorisation;

public sealed class LocalEntitlementStore : IEffectivePermissionSource, IEntitlementVersionSource
{
    private readonly object _gate = new();
    private readonly Dictionary<string, EntitlementSnapshot> _snapshots = new(StringComparer.Ordinal);
    private readonly TimeProvider _timeProvider;

    public LocalEntitlementStore(TimeProvider timeProvider) =>
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));

    public EntitlementSnapshot? FindSnapshot(ActorId actorId, InstitutionId institutionId)
    {
        lock (_gate)
        {
            EntitlementSnapshot? stored = _snapshots.GetValueOrDefault(Key(actorId, institutionId));
            return stored is null ? null : stored with { CapturedAt = _timeProvider.GetUtcNow() };
        }
    }

    public long GetCurrentVersion(ActorId actorId, InstitutionId institutionId)
    {
        lock (_gate)
        {
            return _snapshots.GetValueOrDefault(Key(actorId, institutionId))?.Version ?? 0;
        }
    }

    public long Grant(ActorId actorId, InstitutionId institutionId, PermissionIdentifier permission)
    {
        lock (_gate)
        {
            string key = Key(actorId, institutionId);
            EntitlementSnapshot? existing = _snapshots.GetValueOrDefault(key);
            long version = (existing?.Version ?? 0) + 1;
            HashSet<PermissionGrant> grants = existing is null ? [] : new(existing.Grants);
            grants.RemoveWhere(grant => grant.Permission == permission);
            grants.Add(new PermissionGrant
            {
                Permission = permission,
                Scope = GrantScope.ForInstitution(institutionId),
            });
            _snapshots[key] = new EntitlementSnapshot
            {
                ActorId = actorId,
                InstitutionId = institutionId,
                Version = version,
                Grants = grants,
                CapturedAt = _timeProvider.GetUtcNow(),
            };
            return version;
        }
    }

    private static string Key(ActorId actorId, InstitutionId institutionId) =>
        actorId.Value + "|" + institutionId.Value;
}

public sealed class EmptyEntitlementStore : IEffectivePermissionSource, IEntitlementVersionSource
{
    public EntitlementSnapshot? FindSnapshot(ActorId actorId, InstitutionId institutionId) => null;
    public long GetCurrentVersion(ActorId actorId, InstitutionId institutionId) => 0;
}
