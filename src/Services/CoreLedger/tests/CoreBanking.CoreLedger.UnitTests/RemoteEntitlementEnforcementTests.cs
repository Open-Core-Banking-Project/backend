using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using CoreBanking.BuildingBlocks.Application.Security;
using CoreBanking.BuildingBlocks.Domain.Identity;
using CoreBanking.CoreLedger.Application.Institutions;
using Xunit;

namespace CoreBanking.CoreLedger.UnitTests;

public sealed class RemoteEntitlementEnforcementTests
{
    private static readonly ActorId Actor = ActorId.From("finance-operator");
    private static readonly InstitutionId Institution = InstitutionId.From("institution-1");

    [Fact]
    public async Task Financial_operation_requires_a_current_source_snapshot_and_fails_closed_on_outage()
    {
        ToggleProvider provider = new(CurrentSnapshot());
        RefreshingEntitlementStore store = new(provider);
        AuthorisationPolicy policy = new(store, store, TimeProvider.System, TimeSpan.FromMinutes(1));
        AuthenticatedActor actor = new()
        {
            Id = Actor,
            Type = ActorType.Human,
            InstitutionId = Institution,
            EntitlementVersion = 4,
            Strength = AuthenticationStrength.StepUp,
        };

        Assert.True(await store.RefreshAsync(Actor, Institution, 4, CancellationToken.None));
        Assert.True(policy.Evaluate(Request(actor)).IsAllowed);

        provider.Fail = true;
        Assert.False(await store.RefreshAsync(Actor, Institution, 4, CancellationToken.None));
        Assert.False(policy.Evaluate(Request(actor)).IsAllowed);
    }

    private static AuthorisationRequest Request(AuthenticatedActor actor) => new()
    {
        Actor = actor,
        Requirement = InstitutionPermissions.BusinessDateAdvanceRequirement,
        Scope = ResourceScope.ForInstitution(Institution),
        StepUpSatisfied = true,
    };

    private static EntitlementSnapshot CurrentSnapshot() => new()
    {
        ActorId = Actor,
        InstitutionId = Institution,
        Version = 4,
        CapturedAt = DateTimeOffset.UtcNow,
        Grants = new HashSet<PermissionGrant>
        {
            new()
            {
                Permission = InstitutionPermissions.BusinessDateAdvance,
                Scope = GrantScope.ForInstitution(Institution),
            },
        },
    };

    private sealed class ToggleProvider(EntitlementSnapshot snapshot) : IEntitlementSnapshotProvider
    {
        public bool Fail { get; set; }

        public Task<EntitlementSnapshot?> GetSnapshotAsync(
            ActorId actorId,
            InstitutionId institutionId,
            long minimumVersion,
            CancellationToken cancellationToken) =>
            Fail
                ? throw new HttpRequestException("Synthetic source outage.")
                : Task.FromResult<EntitlementSnapshot?>(snapshot);
    }
}
