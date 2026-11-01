using System;
using System.Collections.Generic;
using System.Linq;
using CoreBanking.BuildingBlocks.Application.Security;
using CoreBanking.BuildingBlocks.Domain.Identity;
using CoreBanking.IdentityAccess.Contracts;
using CoreBanking.IdentityAccess.Infrastructure.Authorisation;
using CoreBanking.IdentityAccess.Infrastructure.Entitlements;
using Xunit;

namespace CoreBanking.IdentityAccess.UnitTests;

/// <summary>
/// Entitlement distribution: the actor reads their own complete snapshot, and a stale, regressed or
/// foreign request is a denial rather than an empty permission set. Decision: ADR-0009; slice 9.
/// </summary>
[Trait("Category", "Unit")]
public sealed class EntitlementDistributionTests
{
    private static readonly InstitutionId Institution = InstitutionId.From("institution-1");
    private static readonly InstitutionId OtherInstitution = InstitutionId.From("institution-2");
    private static readonly ActorId Actor = ActorId.From("actor-1");
    private static readonly ActorId OtherActor = ActorId.From("actor-2");
    private static readonly PermissionIdentifier View = PermissionIdentifier.Parse("identity.identity.view");
    private static readonly DateTimeOffset Now = new(2026, 10, 28, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_snapshot_returns_the_grants_at_the_current_version()
    {
        TestSetup setup = new();
        setup.Grant(Actor, View);

        EntitlementDistributionResult result = setup.GetSnapshot(Actor, Institution, 0);

        Assert.True(result.IsAllowed);
        Assert.True(result.Snapshot!.IsComplete);
        Assert.Equal(1, result.Snapshot.Version);
        PermissionGrantV1 grant = Assert.Single(result.Snapshot.Grants);
        Assert.Equal(View.Value, grant.Permission);
        Assert.Equal(Institution.Value, grant.Scope.InstitutionId);
    }

    [Fact]
    public void Another_actor_cannot_read_this_actor_snapshot()
    {
        TestSetup setup = new();
        setup.Grant(Actor, View);

        EntitlementDistributionResult result = setup.GetSnapshot(Actor, Institution, 0, OtherActor);

        Assert.False(result.IsAllowed);
        Assert.Equal(EntitlementDistributionV1.ActorMismatch, result.DenialCode);
    }

    [Fact]
    public void Another_institution_is_refused()
    {
        TestSetup setup = new();
        setup.Grant(Actor, View);

        EntitlementDistributionResult result = setup.GetSnapshot(Actor, OtherInstitution, 0);

        Assert.False(result.IsAllowed);
        Assert.Equal(EntitlementDistributionV1.InstitutionMismatch, result.DenialCode);
    }

    [Fact]
    public void A_minimum_version_above_the_current_version_is_regressed()
    {
        TestSetup setup = new();
        setup.Grant(Actor, View);

        EntitlementDistributionResult result = setup.GetSnapshot(Actor, Institution, 5);

        Assert.False(result.IsAllowed);
        Assert.Equal(EntitlementDistributionV1.VersionRegressed, result.DenialCode);
    }

    [Fact]
    public void An_actor_without_grants_receives_an_incomplete_snapshot()
    {
        TestSetup setup = new();

        EntitlementDistributionResult result = setup.GetSnapshot(Actor, Institution, 0);

        Assert.True(result.IsAllowed);
        Assert.False(result.Snapshot!.IsComplete);
        Assert.Empty(result.Snapshot.Grants);
        Assert.Equal(0, result.Snapshot.Version);
    }

    [Fact]
    public void A_snapshot_older_than_the_version_is_incomplete()
    {
        EntitlementDistributionService service = new(new StaleSource(), new FixedVersions(3), TimeProvider.System);

        EntitlementDistributionResult result = service.GetSnapshot(
            new AuthenticatedActor
            {
                Id = Actor,
                Type = ActorType.Human,
                InstitutionId = Institution,
                EntitlementVersion = 3,
                Strength = AuthenticationStrength.MultiFactor,
            },
            Actor,
            Institution,
            0);

        Assert.True(result.IsAllowed);
        Assert.False(result.Snapshot!.IsComplete);
        Assert.Equal(3, result.Snapshot.Version);
        Assert.Empty(result.Snapshot.Grants);
    }

    [Fact]
    public void A_missing_caller_is_denied()
    {
        TestSetup setup = new();

        EntitlementDistributionResult result = setup.Service.GetSnapshot(null, Actor, Institution, 0);

        Assert.False(result.IsAllowed);
        Assert.Equal(AuthorisationCodes.MissingAuthentication, result.DenialCode);
    }

    private sealed class TestSetup
    {
        private readonly InMemoryEntitlementStore _store;

        public TestSetup()
        {
            _store = new InMemoryEntitlementStore(new FixedTime());
            Service = new EntitlementDistributionService(_store, _store, new FixedTime());
        }

        public EntitlementDistributionService Service { get; }

        public void Grant(ActorId actorId, PermissionIdentifier permission) =>
            _store.Grant(actorId, Institution, permission, GrantScope.ForInstitution(Institution));

        public EntitlementDistributionResult GetSnapshot(
            ActorId actorId,
            InstitutionId institution,
            long minimumVersion,
            ActorId? requestedActorId = null) =>
            Service.GetSnapshot(
                new AuthenticatedActor
                {
                    Id = actorId,
                    Type = ActorType.Human,
                    InstitutionId = Institution,
                    SessionId = "session-1",
                    EntitlementVersion = _store.GetCurrentVersion(actorId, Institution),
                    Strength = AuthenticationStrength.MultiFactor,
                },
                requestedActorId ?? actorId,
                institution,
                minimumVersion);
    }

    private sealed class FixedTime : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class FixedVersions : IEntitlementVersionSource
    {
        private readonly long _version;

        public FixedVersions(long version) => _version = version;

        public long GetCurrentVersion(ActorId actorId, InstitutionId institutionId) => _version;
    }

    private sealed class StaleSource : IEffectivePermissionSource
    {
        public EntitlementSnapshot? FindSnapshot(ActorId actorId, InstitutionId institutionId) => new()
        {
            ActorId = actorId,
            InstitutionId = institutionId,
            Version = 2,
            CapturedAt = Now.AddMinutes(-1),
            Grants = new HashSet<PermissionGrant>
            {
                new() { Permission = View, Scope = GrantScope.ForInstitution(institutionId) },
            },
        };
    }
}
