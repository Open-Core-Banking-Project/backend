using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using CoreBanking.BuildingBlocks.Application.Messaging;
using CoreBanking.BuildingBlocks.Application.Persistence;
using CoreBanking.BuildingBlocks.Application.Security;
using CoreBanking.BuildingBlocks.Domain.Identity;
using CoreBanking.BuildingBlocks.Infrastructure.Tenancy;
using CoreBanking.OperationsWorkflow.Contracts;
using CoreBanking.OperationsWorkflow.Domain.Approvals;
using CoreBanking.OperationsWorkflow.Infrastructure.Approvals;
using CoreBanking.OperationsWorkflow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Xunit;

namespace CoreBanking.OperationsWorkflow.UnitTests;

public sealed class ApprovalWorkflowTests
{
    private static readonly InstitutionId Institution = InstitutionId.From("institution-1");
    private static readonly ActorId Maker = ActorId.From("maker-1");
    private static readonly ActorId Approver = ActorId.From("approver-1");
    private static readonly DateTimeOffset DecisionAt = new(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Initial_profile_is_pinned_and_hash_bound()
    {
        ApprovalRequest request = Request();

        Assert.Equal("product.savings.eligibility.publish.v1", request.WorkflowKey);
        Assert.Equal("savings-eligibility-publication-v1", request.WorkflowVersionId);
        Assert.Equal(64, request.WorkflowDefinitionHash.Length);
        Assert.Equal("product.version.approve", request.RequiredPermission);
        Assert.Equal(new string('a', 64), request.SubjectHash);
        Assert.Equal(SavingsEligibilityPublicationProfile.WorkflowKey, SavingsEligibilityApprovalV1.WorkflowKey);
        Assert.Equal(SavingsEligibilityPublicationProfile.WorkflowVersionId, SavingsEligibilityApprovalV1.WorkflowVersionId);
        Assert.Equal(SavingsEligibilityPublicationProfile.DefinitionHash, SavingsEligibilityApprovalV1.WorkflowDefinitionHash);
    }

    [Fact]
    public async System.Threading.Tasks.Task Decision_time_verifier_requires_current_version_exact_permission_and_institution_scope()
    {
        EntitlementSnapshot snapshot = new()
        {
            ActorId = Approver,
            InstitutionId = Institution,
            Version = 7,
            CapturedAt = DecisionAt.AddSeconds(-10),
            Grants = new HashSet<PermissionGrant>
            {
                new()
                {
                    Permission = PermissionIdentifier.Parse(SavingsEligibilityPublicationProfile.RequiredPermission),
                    Scope = GrantScope.ForInstitution(Institution),
                    ValidTo = DecisionAt.AddMinutes(5),
                },
            },
        };
        CurrentEntitlementVerifier verifier = new(
            new StaticPermissionSource(snapshot), new StaticVersionSource(7), TimeSpan.FromSeconds(60));

        EntitlementDecisionEvidence? evidence = await verifier.VerifyAsync(
            Institution, Approver, SavingsEligibilityPublicationProfile.RequiredPermission,
            DecisionAt, CancellationToken.None);

        Assert.NotNull(evidence);
        Assert.Equal("7", evidence.EntitlementVersion);
        Assert.Equal(DecisionAt.AddSeconds(50), evidence.ValidUntil);

        CurrentEntitlementVerifier staleVersion = new(
            new StaticPermissionSource(snapshot), new StaticVersionSource(8), TimeSpan.FromSeconds(60));
        Assert.Null(await staleVersion.VerifyAsync(
            Institution, Approver, SavingsEligibilityPublicationProfile.RequiredPermission,
            DecisionAt, CancellationToken.None));
        Assert.Null(await verifier.VerifyAsync(
            Institution, Approver, "product.version.view", DecisionAt, CancellationToken.None));
    }

    [Fact]
    public void Maker_cannot_decide_their_own_request()
    {
        ApprovalRequest request = Request();

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => request.Decide(
            "decision-1", ApprovalDecisionType.Approve, Evidence(Maker), null, DecisionAt));

        Assert.Equal("maker_cannot_approve_own_request", exception.Message);
    }

    [Fact]
    public void Decision_requires_exact_permission_and_current_entitlement_evidence()
    {
        ApprovalRequest wrongPermission = Request();
        EntitlementDecisionEvidence permissionEvidence = EntitlementDecisionEvidence.Create(
            Institution, Approver, "product.version.view", "entitlement-7", DecisionAt.AddMinutes(-1), DecisionAt.AddMinutes(1));
        Assert.Equal("approval_permission_missing", Assert.Throws<InvalidOperationException>(() =>
            wrongPermission.Decide("decision-1", ApprovalDecisionType.Approve, permissionEvidence, null, DecisionAt)).Message);

        ApprovalRequest stale = Request();
        EntitlementDecisionEvidence staleEvidence = EntitlementDecisionEvidence.Create(
            Institution, Approver, SavingsEligibilityPublicationProfile.RequiredPermission,
            "entitlement-7", DecisionAt.AddMinutes(-2), DecisionAt);
        Assert.Equal("approval_entitlement_stale", Assert.Throws<InvalidOperationException>(() =>
            stale.Decide("decision-2", ApprovalDecisionType.Approve, staleEvidence, null, DecisionAt)).Message);
    }

    [Fact]
    public void Rejection_requires_a_reason_and_final_decision_is_immutable()
    {
        ApprovalRequest request = Request();
        Assert.Equal("approval_rejection_reason_required", Assert.Throws<InvalidOperationException>(() =>
            request.Decide("decision-1", ApprovalDecisionType.Reject, Evidence(Approver), null, DecisionAt)).Message);

        ApprovalDecision decision = request.Decide(
            "decision-1", ApprovalDecisionType.Reject, Evidence(Approver), "Rules require correction", DecisionAt);

        Assert.Equal(ApprovalRequestStatus.Rejected, request.Status);
        Assert.Equal("Rules require correction", decision.Reason);
        Assert.Equal("approval_request_already_decided", Assert.Throws<InvalidOperationException>(() =>
            request.Decide("decision-2", ApprovalDecisionType.Approve, Evidence(ActorId.From("approver-2")), null, DecisionAt)).Message);
    }

    [Fact]
    public void Decision_contract_preserves_exact_subject_and_entitlement_evidence()
    {
        ApprovalDecidedV1 payload = new(
            "institution-1", "request-1", "decision-1", "approved",
            SavingsEligibilityPublicationProfile.WorkflowKey,
            SavingsEligibilityPublicationProfile.WorkflowVersionId,
            SavingsEligibilityPublicationProfile.DefinitionHash,
            SavingsEligibilityPublicationProfile.SubjectType,
            "version-1", "1", new string('a', 64), "approver-1",
            SavingsEligibilityPublicationProfile.RequiredPermission,
            "entitlement-7", DecisionAt.AddMinutes(-1), DecisionAt.AddMinutes(1), null,
            "correlation-1", DecisionAt);

        ApprovalDecidedV1 restored = EventContract.Deserialize<ApprovalDecidedV1>(EventContract.Serialize(payload))!;

        Assert.Equal(payload, restored);
    }

    [Fact]
    public void Approval_records_are_tenant_scoped_and_decision_is_unique_per_request()
    {
        using OperationsWorkflowDbContext context = CreateContext();
        IEntityType request = context.Model.FindEntityType(typeof(ApprovalRequestRecord))!;
        IEntityType decision = context.Model.FindEntityType(typeof(ApprovalDecisionRecord))!;
        IEntityType[] tenantEntities = context.Model.GetEntityTypes()
            .Where(type => typeof(ITenantOwnedEntity).IsAssignableFrom(type.ClrType)).ToArray();

        Assert.All(tenantEntities, entity => Assert.NotEmpty(entity.GetDeclaredQueryFilters()));
        Assert.True(request.FindProperty(nameof(ApprovalRequestRecord.Version))!.IsConcurrencyToken);
        Assert.Contains(request.GetIndexes(), index => index.IsUnique
            && index.Properties.Select(x => x.Name).SequenceEqual(["InstitutionId", "SourceService", "SourceRequestKey"]));
        Assert.Contains(decision.GetIndexes(), index => index.IsUnique
            && index.Properties.Select(x => x.Name).SequenceEqual(["InstitutionId", "RequestId"]));
        Assert.Contains(decision.GetForeignKeys(), foreignKey =>
            foreignKey.Properties.Select(x => x.Name).SequenceEqual(
                ["InstitutionId", "RequestId", "SubjectVersion", "SubjectHash"]));
    }

    private static ApprovalRequest Request() => ApprovalRequest.SubmitSavingsEligibilityPublication(
        Institution, "request-1", "source-request-1", "version-1", "1", new string('a', 64),
        Maker, "correlation-1", DecisionAt.AddHours(-1));

    private static EntitlementDecisionEvidence Evidence(ActorId actor) => EntitlementDecisionEvidence.Create(
        Institution, actor, SavingsEligibilityPublicationProfile.RequiredPermission,
        "entitlement-7", DecisionAt.AddMinutes(-1), DecisionAt.AddMinutes(1));

    private static OperationsWorkflowDbContext CreateContext()
    {
        DbContextOptions<OperationsWorkflowDbContext> options = new DbContextOptionsBuilder<OperationsWorkflowDbContext>()
            .UseNpgsql("Host=127.0.0.1;Database=model_only;Username=model;Password=model").Options;
        return new OperationsWorkflowDbContext(options, new AmbientTenantContext());
    }

    private sealed class StaticPermissionSource(EntitlementSnapshot snapshot) : IEffectivePermissionSource
    {
        public EntitlementSnapshot? FindSnapshot(ActorId actorId, InstitutionId institutionId) => snapshot;
    }

    private sealed class StaticVersionSource(long version) : IEntitlementVersionSource
    {
        public long GetCurrentVersion(ActorId actorId, InstitutionId institutionId) => version;
    }
}
