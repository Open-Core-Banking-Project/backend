using System;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using CoreBanking.BuildingBlocks.Application.Security;
using CoreBanking.BuildingBlocks.Domain.Identity;

namespace CoreBanking.CoreLedger.Api.Authorisation;

public sealed class IdentityEntitlementSnapshotProvider : IEntitlementSnapshotProvider
{
    private readonly HttpClient _client;

    public IdentityEntitlementSnapshotProvider(HttpClient client) =>
        _client = client ?? throw new ArgumentNullException(nameof(client));

    public async Task<EntitlementSnapshot?> GetSnapshotAsync(
        ActorId actorId,
        InstitutionId institutionId,
        long minimumVersion,
        CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = new(HttpMethod.Post, "/internal/v1/entitlements/snapshot")
        {
            Content = JsonContent.Create(new EntitlementSnapshotRequestV1
            {
                ActorId = actorId.Value,
                InstitutionId = institutionId.Value,
                MinimumVersion = minimumVersion,
            }),
        };
        request.Headers.Add("X-Institution-Id", institutionId.Value);

        using HttpResponseMessage response = await _client.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        EntitlementSnapshotV1? snapshot = await response.Content
            .ReadFromJsonAsync<EntitlementSnapshotV1>(cancellationToken)
            .ConfigureAwait(false);
        if (snapshot is null || !snapshot.IsComplete)
        {
            return null;
        }

        return Map(snapshot);
    }

    private static EntitlementSnapshot Map(EntitlementSnapshotV1 snapshot) => new()
    {
        ActorId = ActorId.From(snapshot.ActorId),
        InstitutionId = InstitutionId.From(snapshot.InstitutionId),
        Version = snapshot.Version,
        CapturedAt = snapshot.CapturedAt,
        Grants = snapshot.Grants.Select(Map).ToHashSet(),
    };

    private static PermissionGrant Map(PermissionGrantV1 grant) => new()
    {
        Permission = Permission(grant),
        Scope = Scope(grant),
        ValidTo = grant.ValidTo,
    };

    private static PermissionIdentifier Permission(PermissionGrantV1 grant)
    {
        EnsureSupported(grant);
        return PermissionIdentifier.Parse(grant.Permission);
    }

    private static GrantScope Scope(PermissionGrantV1 grant) => new()
    {
        InstitutionId = InstitutionId.From(grant.Scope.InstitutionId),
        LegalEntityId = grant.Scope.LegalEntityId,
        RegionId = grant.Scope.RegionId,
        BranchId = grant.Scope.BranchId,
        ChannelId = grant.Scope.ChannelId,
        CustomerId = grant.Scope.CustomerId,
        AccountId = grant.Scope.AccountId,
        MandateId = grant.Scope.MandateId,
        Currency = grant.Scope.Currency,
        MaximumAmount = grant.Scope.MaximumAmount,
    };

    private static void EnsureSupported(PermissionGrantV1 grant)
    {
        if (grant.ValidFrom is not null
            || grant.Scope.DeviceId is not null
            || grant.Scope.MinimumAuthenticationStrength is not null)
        {
            throw new InvalidOperationException("The entitlement snapshot contains a scope dimension this consumer cannot enforce.");
        }
    }
}
