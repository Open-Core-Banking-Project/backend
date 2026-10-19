using System;
using System.Text;
using CoreBanking.BuildingBlocks.Application.Security;
using CoreBanking.BuildingBlocks.Domain.Identity;
using CoreBanking.CoreLedger.Api.Authorisation;
using CoreBanking.CoreLedger.Application.Institutions;
using Xunit;

namespace CoreBanking.CoreLedger.UnitTests;

public sealed class InstitutionApiSecurityTests
{
    [Fact]
    public void Institution_permissions_are_stable_capability_identifiers()
    {
        Assert.Equal("platform.institution.create", InstitutionPermissions.Create.Value);
        Assert.Equal("platform.institution.activate", InstitutionPermissions.Activate.Value);
        Assert.Equal("platform.institution.suspend", InstitutionPermissions.Suspend.Value);
        Assert.Equal("institution.region.manage", InstitutionPermissions.RegionManage.Value);
        Assert.Equal("institution.branch.create", InstitutionPermissions.BranchCreate.Value);
        Assert.Equal("institution.branch.activate", InstitutionPermissions.BranchActivate.Value);
        Assert.Equal("institution.branch.suspend", InstitutionPermissions.BranchSuspend.Value);
        Assert.Equal("institution.calendar.manage", InstitutionPermissions.CalendarManage.Value);
        Assert.Equal("institution.business_date.advance", InstitutionPermissions.BusinessDateAdvance.Value);
        Assert.True(InstitutionPermissions.CreateRequirement.RequiresStepUp);
        Assert.True(InstitutionPermissions.ActivateRequirement.RequiresStepUp);
        Assert.True(InstitutionPermissions.ActivateRequirement.IsRestrictedApproval);
        Assert.True(InstitutionPermissions.SuspendRequirement.RequiresStepUp);
        Assert.True(InstitutionPermissions.SuspendRequirement.IsRestrictedApproval);
        Assert.True(InstitutionPermissions.BranchActivateRequirement.IsRestrictedApproval);
        Assert.True(InstitutionPermissions.BranchSuspendRequirement.IsRestrictedApproval);
        Assert.True(InstitutionPermissions.CalendarPublishRequirement.RequiresStepUp);
        Assert.True(InstitutionPermissions.CalendarPublishRequirement.IsRestrictedApproval);
        Assert.True(InstitutionPermissions.BusinessDateAdvanceRequirement.RequiresStepUp);
        Assert.True(InstitutionPermissions.BusinessDateAdvanceRequirement.IsRestrictedApproval);
    }

    [Fact]
    public void Production_fallback_resolves_no_authenticated_actor()
    {
        Assert.Null(new UnconfiguredActorContextResolver().Resolve("untrusted-material"));
    }

    [Fact]
    public void Development_actor_material_cannot_supply_permissions()
    {
        string json = """
            {
              "actorId": "actor-1",
              "actorType": "Human",
              "institutionId": "institution-1",
              "entitlementVersion": 1,
              "authenticationStrength": "StepUp",
              "permissions": ["platform.institution.activate"]
            }
            """;
        string material = Convert.ToBase64String(Encoding.UTF8.GetBytes(json));

        AuthenticatedActor actor = new DevelopmentActorContextResolver().Resolve(material)!;

        Assert.Equal(ActorId.From("actor-1"), actor.Id);
        Assert.Equal(AuthenticationStrength.StepUp, actor.Strength);
    }

    [Fact]
    public void Activation_policy_requires_step_up_and_the_local_entitlement()
    {
        InstitutionId institutionId = InstitutionId.From("institution-1");
        ActorId actorId = ActorId.From("approver-1");
        LocalEntitlementStore store = new(TimeProvider.System);
        long version = store.Grant(actorId, institutionId, InstitutionPermissions.Activate);
        AuthorisationPolicy policy = new(store, store, TimeProvider.System, TimeSpan.FromMinutes(1));
        AuthenticatedActor actor = new()
        {
            Id = actorId,
            Type = ActorType.Human,
            InstitutionId = institutionId,
            EntitlementVersion = version,
            Strength = AuthenticationStrength.MultiFactor,
        };

        AuthorisationDecision denied = policy.Evaluate(new AuthorisationRequest
        {
            Requirement = InstitutionPermissions.ActivateRequirement,
            Scope = ResourceScope.ForInstitution(institutionId),
            Actor = actor,
            StepUpSatisfied = false,
        });

        Assert.False(denied.IsAllowed);
        Assert.Equal(DenialReason.StepUpRequired, denied.Reason);
    }
}
