using System;
using System.Text;
using CoreBanking.BuildingBlocks.Application.Security;
using CoreBanking.BuildingBlocks.Domain.Identity;
using CoreBanking.OperationsWorkflow.Api.Authorisation;
using CoreBanking.OperationsWorkflow.Application.Approvals;
using Xunit;

namespace CoreBanking.OperationsWorkflow.UnitTests;

public sealed class ApprovalApiSecurityTests
{
    [Fact]
    public void Approval_decision_permission_is_stable_restricted_and_step_up_protected()
    {
        Assert.Equal("product.version.approve", ApprovalPermissions.Decide.Value);
        Assert.True(ApprovalPermissions.DecideRequirement.RequiresStepUp);
        Assert.True(ApprovalPermissions.DecideRequirement.IsRestrictedApproval);
    }

    [Fact]
    public void Production_fallback_resolves_no_authenticated_actor()
    {
        Assert.Null(new UnconfiguredActorContextResolver().Resolve("untrusted-material"));
        Assert.Null(new EmptyEntitlementStore().FindSnapshot(
            ActorId.From("actor-1"), InstitutionId.From("institution-1")));
    }

    [Fact]
    public void Development_actor_material_cannot_supply_permissions()
    {
        string json = """
            {
              "actorId": "approver-1",
              "actorType": "Human",
              "institutionId": "institution-1",
              "entitlementVersion": 1,
              "authenticationStrength": "StepUp",
              "permissions": ["product.version.approve"]
            }
            """;
        string material = Convert.ToBase64String(Encoding.UTF8.GetBytes(json));

        AuthenticatedActor actor = new DevelopmentActorContextResolver().Resolve(material)!;

        Assert.Equal(ActorId.From("approver-1"), actor.Id);
        Assert.Equal(InstitutionId.From("institution-1"), actor.InstitutionId);
        Assert.Equal(AuthenticationStrength.StepUp, actor.Strength);
    }

    [Fact]
    public void Approval_policy_requires_server_side_grant_and_step_up()
    {
        InstitutionId institutionId = InstitutionId.From("institution-1");
        ActorId actorId = ActorId.From("approver-1");
        LocalEntitlementStore store = new(TimeProvider.System);
        long version = store.Grant(actorId, institutionId, ApprovalPermissions.Decide);
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
            Requirement = ApprovalPermissions.DecideRequirement,
            Scope = ResourceScope.ForInstitution(institutionId),
            Actor = actor,
            StepUpSatisfied = false,
        });

        Assert.False(denied.IsAllowed);
        Assert.Equal(DenialReason.StepUpRequired, denied.Reason);
    }
}
