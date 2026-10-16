using System;
using System.Collections.Generic;
using CoreBanking.BuildingBlocks.Application.Security;
using CoreBanking.BuildingBlocks.Domain.Identity;
using CoreBanking.IdentityAccess.Infrastructure.Authorisation;
using Xunit;

namespace CoreBanking.IdentityAccess.UnitTests;

/// <summary>
/// The authorisation test matrix required by Phase 0 epic 03. Every refusal path asserts the
/// stable reason rather than only a failure, so a regression cannot silently change semantics.
/// </summary>
[Trait("Category", "Unit")]
public sealed class AuthorisationPolicyTests
{
    private static readonly InstitutionId Institution = InstitutionId.From("institution-1");
    private static readonly InstitutionId OtherInstitution = InstitutionId.From("institution-2");
    private static readonly ActorId Administrator = ActorId.From("actor-admin");
    private static readonly ActorId ServiceClient = ActorId.From("actor-service");
    private static readonly PermissionIdentifier Execute = PermissionIdentifier.Parse("platform.foundation.execute");
    private static readonly PermissionIdentifier AssignPermission = PermissionIdentifier.Parse("identity.permission.assign");
    private static readonly DateTimeOffset Start = new(2026, 10, 16, 10, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan SnapshotLifetime = TimeSpan.FromSeconds(60);

    [Fact]
    public void Valid_identity_tenant_permission_and_scope_is_allowed()
    {
        TestSetup setup = CreateSetup();
        setup.Store.Grant(Administrator, Institution, Execute, GrantScope.ForInstitution(Institution));

        AuthorisationDecision decision = setup.Policy.Evaluate(Request(setup.ActorVersion(), Scope()));

        Assert.True(decision.IsAllowed);
        Assert.Equal(DenialReason.None, decision.Reason);
    }

    [Fact]
    public void Missing_authentication_is_denied()
    {
        TestSetup setup = CreateSetup();

        AuthorisationDecision decision = setup.Policy.Evaluate(new AuthorisationRequest
        {
            Requirement = PermissionRequirement.For(Execute),
            Scope = Scope(),
            Actor = null,
        });

        Assert.False(decision.IsAllowed);
        Assert.Equal(DenialReason.MissingAuthentication, decision.Reason);
    }

    [Fact]
    public void Wrong_institution_is_denied()
    {
        TestSetup setup = CreateSetup();
        setup.Store.Grant(Administrator, Institution, Execute, GrantScope.ForInstitution(Institution));

        AuthorisationDecision decision = setup.Policy.Evaluate(new AuthorisationRequest
        {
            Requirement = PermissionRequirement.For(Execute),
            Scope = new ResourceScope { InstitutionId = OtherInstitution },
            Actor = TestSetup.Actor(setup.ActorVersion()),
        });

        Assert.False(decision.IsAllowed);
        Assert.Equal(DenialReason.WrongInstitution, decision.Reason);
    }

    [Fact]
    public void Missing_permission_is_denied()
    {
        TestSetup setup = CreateSetup();

        AuthorisationDecision decision = setup.Policy.Evaluate(Request(setup.ActorVersion(), Scope()));

        Assert.False(decision.IsAllowed);
        Assert.Equal(DenialReason.MissingPermission, decision.Reason);
    }

    [Fact]
    public void Permission_outside_the_requested_branch_scope_is_denied()
    {
        TestSetup setup = CreateSetup();
        setup.Store.Grant(Administrator, Institution, Execute, new GrantScope { InstitutionId = Institution, BranchId = "branch-1" });

        AuthorisationDecision decision = setup.Policy.Evaluate(new AuthorisationRequest
        {
            Requirement = PermissionRequirement.For(Execute),
            Scope = new ResourceScope { InstitutionId = Institution, BranchId = "branch-2" },
            Actor = TestSetup.Actor(setup.ActorVersion()),
        });

        Assert.False(decision.IsAllowed);
        Assert.Equal(DenialReason.ScopeMismatch, decision.Reason);
    }

    [Fact]
    public void Amount_above_the_granted_limit_is_denied()
    {
        TestSetup setup = CreateSetup();
        setup.Store.Grant(Administrator, Institution, Execute, new GrantScope { InstitutionId = Institution, MaximumAmount = 1_000m });

        AuthorisationDecision decision = setup.Policy.Evaluate(new AuthorisationRequest
        {
            Requirement = PermissionRequirement.For(Execute),
            Scope = new ResourceScope { InstitutionId = Institution, Amount = 2_500m },
            Actor = TestSetup.Actor(setup.ActorVersion()),
        });

        Assert.False(decision.IsAllowed);
        Assert.Equal(DenialReason.ScopeMismatch, decision.Reason);
    }

    [Fact]
    public void Stale_entitlement_version_is_denied()
    {
        TestSetup setup = CreateSetup();
        long currentVersion = setup.Store.Grant(Administrator, Institution, Execute, GrantScope.ForInstitution(Institution));

        AuthorisationDecision decision = setup.Policy.Evaluate(Request(currentVersion - 1, Scope()));

        Assert.False(decision.IsAllowed);
        Assert.Equal(DenialReason.StaleEntitlement, decision.Reason);
    }

    [Fact]
    public void Snapshot_older_than_the_configured_lifetime_is_denied()
    {
        TestSetup setup = CreateSetup();
        long version = setup.Store.Grant(Administrator, Institution, Execute, GrantScope.ForInstitution(Institution));
        setup.Time.Advance(SnapshotLifetime + TimeSpan.FromSeconds(1));

        AuthorisationDecision decision = setup.Policy.Evaluate(Request(version, Scope()));

        Assert.False(decision.IsAllowed);
        Assert.Equal(DenialReason.StaleEntitlement, decision.Reason);
    }

    [Fact]
    public void Revoked_permission_is_denied_after_the_version_changes()
    {
        TestSetup setup = CreateSetup();
        long version = setup.Store.Grant(Administrator, Institution, Execute, GrantScope.ForInstitution(Institution));
        setup.Store.Revoke(Administrator, Institution, Execute);

        AuthorisationDecision decision = setup.Policy.Evaluate(Request(version, Scope()));

        Assert.False(decision.IsAllowed);
        Assert.Equal(DenialReason.StaleEntitlement, decision.Reason);
    }

    [Fact]
    public void Step_up_requirement_is_enforced()
    {
        TestSetup setup = CreateSetup();
        setup.Store.Grant(Administrator, Institution, Execute, GrantScope.ForInstitution(Institution));

        AuthorisationDecision decision = setup.Policy.Evaluate(new AuthorisationRequest
        {
            Requirement = new PermissionRequirement { Permission = Execute, RequiresStepUp = true },
            Scope = Scope(),
            Actor = TestSetup.Actor(setup.ActorVersion()),
            StepUpSatisfied = false,
        });

        Assert.False(decision.IsAllowed);
        Assert.Equal(DenialReason.StepUpRequired, decision.Reason);
    }

    [Fact]
    public void Service_identity_cannot_use_a_human_permission()
    {
        TestSetup setup = CreateSetup();
        long version = setup.Store.Grant(ServiceClient, Institution, Execute, GrantScope.ForInstitution(Institution));

        AuthorisationDecision decision = setup.Policy.Evaluate(new AuthorisationRequest
        {
            Requirement = PermissionRequirement.For(Execute),
            Scope = Scope(),
            Actor = TestSetup.Actor(version, ActorType.Service, ServiceClient),
        });

        Assert.False(decision.IsAllowed);
        Assert.Equal(DenialReason.ServiceIdentityNotPermitted, decision.Reason);
    }

    [Fact]
    public void Multiple_roles_combine_only_the_permitted_grants()
    {
        TestSetup setup = CreateSetup();
        setup.Store.Grant(Administrator, Institution, Execute, new GrantScope { InstitutionId = Institution, BranchId = "branch-1" });
        setup.Store.Grant(Administrator, Institution, AssignPermission, new GrantScope { InstitutionId = Institution, BranchId = "branch-2" });

        long version = setup.ActorVersion();

        AuthorisationDecision permitted = setup.Policy.Evaluate(new AuthorisationRequest
        {
            Requirement = PermissionRequirement.For(AssignPermission),
            Scope = new ResourceScope { InstitutionId = Institution, BranchId = "branch-2" },
            Actor = TestSetup.Actor(version),
        });

        AuthorisationDecision notPermitted = setup.Policy.Evaluate(new AuthorisationRequest
        {
            Requirement = PermissionRequirement.For(AssignPermission),
            Scope = new ResourceScope { InstitutionId = Institution, BranchId = "branch-1" },
            Actor = TestSetup.Actor(version),
        });

        Assert.True(permitted.IsAllowed);
        Assert.False(notPermitted.IsAllowed);
        Assert.Equal(DenialReason.ScopeMismatch, notPermitted.Reason);
    }

    [Fact]
    public void Maker_cannot_approve_their_own_restricted_action()
    {
        TestSetup setup = CreateSetup();
        setup.Store.Grant(Administrator, Institution, Execute, GrantScope.ForInstitution(Institution));

        AuthorisationDecision decision = setup.Policy.Evaluate(new AuthorisationRequest
        {
            Requirement = new PermissionRequirement { Permission = Execute, IsRestrictedApproval = true },
            Scope = Scope(),
            Actor = TestSetup.Actor(setup.ActorVersion()),
            MakerActorId = Administrator.Value,
        });

        Assert.False(decision.IsAllowed);
        Assert.Equal(DenialReason.SegregationOfDuties, decision.Reason);
    }

    [Fact]
    public void Expired_temporary_grant_is_denied()
    {
        TestSetup setup = CreateSetup();
        setup.Store.Grant(
            Administrator,
            Institution,
            Execute,
            GrantScope.ForInstitution(Institution),
            validTo: Start.AddMinutes(5));

        setup.Time.Advance(TimeSpan.FromMinutes(6));
        long version = setup.Store.IncrementVersion(Administrator, Institution);

        AuthorisationDecision decision = setup.Policy.Evaluate(Request(version, Scope()));

        Assert.False(decision.IsAllowed);
        Assert.Equal(DenialReason.MissingPermission, decision.Reason);
    }

    private static ResourceScope Scope() => ResourceScope.ForInstitution(Institution);

    private static AuthorisationRequest Request(long entitlementVersion, ResourceScope scope) => new()
    {
        Requirement = PermissionRequirement.For(Execute),
        Scope = scope,
        Actor = new AuthenticatedActor
        {
            Id = Administrator,
            Type = ActorType.Human,
            InstitutionId = Institution,
            SessionId = "session-1",
            DeviceId = "device-1",
            EntitlementVersion = entitlementVersion,
            Strength = AuthenticationStrength.MultiFactor,
        },
    };

    private static TestSetup CreateSetup() => new();

    private sealed class TestSetup
    {
        public TestSetup()
        {
            Time = new StubTimeProvider(Start);
            Store = new InMemoryEntitlementStore(Time);
            Policy = new AuthorisationPolicy(Store, Store, Time, SnapshotLifetime);
        }

        public StubTimeProvider Time { get; }

        public InMemoryEntitlementStore Store { get; }

        public AuthorisationPolicy Policy { get; }

        public long ActorVersion() => Store.FindSnapshot(Administrator, Institution)?.Version ?? 0;

        public static AuthenticatedActor Actor(long entitlementVersion, ActorType type = ActorType.Human, ActorId? actorId = null) => new()
        {
            Id = actorId ?? Administrator,
            Type = type,
            InstitutionId = Institution,
            SessionId = "session-1",
            DeviceId = "device-1",
            EntitlementVersion = entitlementVersion,
            Strength = AuthenticationStrength.MultiFactor,
        };
    }
}

/// <summary>Deterministic clock so entitlement freshness is provable without waiting.</summary>
internal sealed class StubTimeProvider : TimeProvider
{
    private DateTimeOffset _utcNow;

    public StubTimeProvider(DateTimeOffset utcNow) => _utcNow = utcNow;

    public override DateTimeOffset GetUtcNow() => _utcNow;

    public void Advance(TimeSpan delta) => _utcNow = _utcNow.Add(delta);
}
