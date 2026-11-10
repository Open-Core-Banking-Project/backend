using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CoreBanking.BuildingBlocks.Application.Security;
using CoreBanking.BuildingBlocks.Domain.Identity;
using CoreBanking.OperationsWorkflow.Application.Approvals;
using Xunit;

namespace CoreBanking.OperationsWorkflow.UnitTests;

public sealed class RemoteEntitlementEnforcementTests
{
    [Fact]
    public async Task Approval_operation_uses_only_a_current_identity_snapshot()
    {
        ActorId actorId = ActorId.From("approver-1");
        InstitutionId institution = InstitutionId.From("institution-1");
        EntitlementSnapshot snapshot = new()
        {
            ActorId = actorId,
            InstitutionId = institution,
            Version = 7,
            CapturedAt = DateTimeOffset.UtcNow,
            Grants = new HashSet<PermissionGrant>
            {
                new()
                {
                    Permission = ApprovalPermissions.Decide,
                    Scope = GrantScope.ForInstitution(institution),
                },
            },
        };
        RefreshingEntitlementStore store = new(new FixedProvider(snapshot));
        AuthorisationPolicy policy = new(store, store, TimeProvider.System, TimeSpan.FromMinutes(1));
        AuthenticatedActor actor = new()
        {
            Id = actorId,
            Type = ActorType.Human,
            InstitutionId = institution,
            EntitlementVersion = 7,
            Strength = AuthenticationStrength.StepUp,
        };

        Assert.True(await store.RefreshAsync(actorId, institution, 7, CancellationToken.None));
        AuthorisationDecision allowed = policy.Evaluate(new AuthorisationRequest
        {
            Actor = actor,
            Requirement = ApprovalPermissions.DecideRequirement,
            Scope = ResourceScope.ForInstitution(institution),
            StepUpSatisfied = true,
        });
        Assert.True(allowed.IsAllowed);

        Assert.False(await store.RefreshAsync(actorId, institution, 8, CancellationToken.None));
        Assert.Null(store.FindSnapshot(actorId, institution));
    }

    private sealed class FixedProvider(EntitlementSnapshot snapshot) : IEntitlementSnapshotProvider
    {
        public Task<EntitlementSnapshot?> GetSnapshotAsync(
            ActorId actorId,
            InstitutionId institutionId,
            long minimumVersion,
            CancellationToken cancellationToken) =>
            Task.FromResult<EntitlementSnapshot?>(snapshot);
    }
}
