using System;
using System.Collections.Generic;
using System.Linq;
using CoreBanking.BuildingBlocks.Application.Security;
using CoreBanking.BuildingBlocks.Domain.Identity;

namespace CoreBanking.IdentityAccess.Infrastructure.Authorisation;

/// <summary>
/// Development and test implementation of the effective-permission and entitlement-version
/// ports. It is replaceable: the relational implementation backed by `identity_access_db`
/// arrives with the persistence slice of epic 03. It is not the production source of truth.
/// Decision: ADR-0009.
/// </summary>
public sealed class InMemoryEntitlementStore : IEffectivePermissionSource, IEntitlementVersionSource
{
    private readonly object _gate = new();
    private readonly Dictionary<string, ActorState> _actors = new(StringComparer.Ordinal);
    private readonly TimeProvider _timeProvider;

    public InMemoryEntitlementStore(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public EntitlementSnapshot? FindSnapshot(ActorId actorId, InstitutionId institutionId)
    {
        ArgumentNullException.ThrowIfNull(actorId);
        ArgumentNullException.ThrowIfNull(institutionId);

        lock (_gate)
        {
            if (!_actors.TryGetValue(Key(actorId, institutionId), out ActorState? state))
            {
                return null;
            }

            return new EntitlementSnapshot
            {
                ActorId = actorId,
                InstitutionId = institutionId,
                Version = state.Version,
                Grants = new HashSet<PermissionGrant>(state.Grants),
                CapturedAt = state.CapturedAt,
            };
        }
    }

    public long GetCurrentVersion(ActorId actorId, InstitutionId institutionId)
    {
        ArgumentNullException.ThrowIfNull(actorId);
        ArgumentNullException.ThrowIfNull(institutionId);

        lock (_gate)
        {
            // An unknown actor is treated as version zero so that a token claiming any other
            // version is refused as stale rather than being trusted.
            return _actors.TryGetValue(Key(actorId, institutionId), out ActorState? state) ? state.Version : 0;
        }
    }

    /// <summary>Grants a permission and invalidates cached entitlements by incrementing the version.</summary>
    public long Grant(ActorId actorId, InstitutionId institutionId, PermissionIdentifier permission, GrantScope scope, DateTimeOffset? validTo = null)
    {
        ArgumentNullException.ThrowIfNull(permission);
        ArgumentNullException.ThrowIfNull(scope);

        lock (_gate)
        {
            ActorState state = GetOrCreate(actorId, institutionId);
            state.Grants.RemoveWhere(grant => grant.Permission == permission);
            state.Grants.Add(new PermissionGrant { Permission = permission, Scope = scope, ValidTo = validTo });
            state.Version++;
            state.CapturedAt = _timeProvider.GetUtcNow();

            return state.Version;
        }
    }

    /// <summary>Revokes a permission and increments the entitlement version so the change takes effect.</summary>
    public long Revoke(ActorId actorId, InstitutionId institutionId, PermissionIdentifier permission)
    {
        ArgumentNullException.ThrowIfNull(permission);

        lock (_gate)
        {
            ActorState state = GetOrCreate(actorId, institutionId);
            state.Grants.RemoveWhere(grant => grant.Permission == permission);
            state.Version++;
            state.CapturedAt = _timeProvider.GetUtcNow();

            return state.Version;
        }
    }

    /// <summary>Increments the version without changing grants, for example when a session is revoked.</summary>
    public long IncrementVersion(ActorId actorId, InstitutionId institutionId)
    {
        lock (_gate)
        {
            ActorState state = GetOrCreate(actorId, institutionId);
            state.Version++;
            state.CapturedAt = _timeProvider.GetUtcNow();

            return state.Version;
        }
    }

    /// <summary>Re-stamps the snapshot without changing its content, to model a cache refresh.</summary>
    public void RefreshCaptureTime(ActorId actorId, InstitutionId institutionId)
    {
        lock (_gate)
        {
            if (_actors.TryGetValue(Key(actorId, institutionId), out ActorState? state))
            {
                state.CapturedAt = _timeProvider.GetUtcNow();
            }
        }
    }

    private ActorState GetOrCreate(ActorId actorId, InstitutionId institutionId)
    {
        string key = Key(actorId, institutionId);
        if (!_actors.TryGetValue(key, out ActorState? state))
        {
            state = new ActorState();
            _actors[key] = state;
        }

        return state;
    }

    private static string Key(ActorId actorId, InstitutionId institutionId) => actorId.Value + "|" + institutionId.Value;

    private sealed class ActorState
    {
        public long Version { get; set; }

        public DateTimeOffset CapturedAt { get; set; }

        public HashSet<PermissionGrant> Grants { get; } = new();
    }
}
