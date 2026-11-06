using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using CoreBanking.BuildingBlocks.Domain.Identity;

namespace CoreBanking.BuildingBlocks.Application.Security;

/// <summary>
/// Port used by business services to obtain a current Identity and Access snapshot through an
/// authenticated, audience-restricted service call.
/// </summary>
public interface IEntitlementSnapshotProvider
{
    Task<EntitlementSnapshot?> GetSnapshotAsync(
        ActorId actorId,
        InstitutionId institutionId,
        long minimumVersion,
        CancellationToken cancellationToken);
}

/// <summary>
/// Non-authoritative local projection. A protected operation refreshes this store before policy
/// evaluation; an unavailable, incomplete or mismatched source evicts the entry and fails closed.
/// </summary>
public sealed class RefreshingEntitlementStore : IEffectivePermissionSource, IEntitlementVersionSource
{
    private readonly ConcurrentDictionary<string, EntitlementSnapshot> _snapshots = new(StringComparer.Ordinal);
    private readonly IEntitlementSnapshotProvider _provider;

    public RefreshingEntitlementStore(IEntitlementSnapshotProvider provider) =>
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));

    public EntitlementSnapshot? FindSnapshot(ActorId actorId, InstitutionId institutionId) =>
        _snapshots.TryGetValue(Key(actorId, institutionId), out EntitlementSnapshot? snapshot)
            ? snapshot
            : null;

    public long GetCurrentVersion(ActorId actorId, InstitutionId institutionId) =>
        FindSnapshot(actorId, institutionId)?.Version ?? 0;

    public async Task<bool> RefreshAsync(
        ActorId actorId,
        InstitutionId institutionId,
        long requiredVersion,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actorId);
        ArgumentNullException.ThrowIfNull(institutionId);
        ArgumentOutOfRangeException.ThrowIfLessThan(requiredVersion, 1);

        string key = Key(actorId, institutionId);
        try
        {
            EntitlementSnapshot? snapshot = await _provider.GetSnapshotAsync(
                actorId, institutionId, requiredVersion, cancellationToken).ConfigureAwait(false);
            if (snapshot is null
                || snapshot.ActorId != actorId
                || snapshot.InstitutionId != institutionId
                || snapshot.Version != requiredVersion)
            {
                _snapshots.TryRemove(key, out _);
                return false;
            }

            _snapshots[key] = snapshot;
            return true;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _snapshots.TryRemove(key, out _);
            return false;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _snapshots.TryRemove(key, out _);
            return false;
        }
    }

    private static string Key(ActorId actorId, InstitutionId institutionId) =>
        actorId.Value + "|" + institutionId.Value;
}
