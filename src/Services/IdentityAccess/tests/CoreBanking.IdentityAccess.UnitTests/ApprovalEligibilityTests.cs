using System;
using System.Threading.Tasks;
using CoreBanking.BuildingBlocks.Application.Security;
using CoreBanking.BuildingBlocks.Domain.Identity;
using CoreBanking.IdentityAccess.Application.Approvals;
using CoreBanking.IdentityAccess.Application.Authorisation;
using CoreBanking.IdentityAccess.Infrastructure.Authorisation;
using Xunit;

namespace CoreBanking.IdentityAccess.UnitTests;

/// <summary>
/// IAM-012 and IAM-013: an approval source can ask whether the authenticated actor may decide a
/// request, and a maker can never become the approver of their own restricted action by changing
/// role or session. Decision: ADR-0009; epic 02 slice 10.
/// </summary>
[Trait("Category", "Unit")]
public sealed class ApprovalEligibilityTests
{
    private static readonly InstitutionId Institution = InstitutionId.From("institution-1");
    private static readonly InstitutionId OtherInstitution = InstitutionId.From("institution-2");
    private static readonly ActorId Approver = ActorId.From("actor-approver");
    private static readonly ActorId Maker = ActorId.From("actor-maker");
    private static readonly PermissionIdentifier Approve = PermissionIdentifier.Parse("product.version.approve");
    private static readonly DateTimeOffset Now = new(2026, 10, 28, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Approver_distinct_from_maker_is_eligible()
    {
        TestSetup setup = new();
        setup.Grant(Approver, Approve);

        ApprovalEligibilityResult result = await setup.Evaluate(
            Approver,
            "session-approver",
            Maker.Value,
            Institution,
            setup.VersionFor(Approver));

        Assert.True(result.IsEligible);
        Assert.Equal(DenialReason.None, result.Reason);
        Assert.True(result.MakerSeparated);
        Assert.Equal(Approve.Value, result.Permission);
        Assert.Equal(setup.VersionFor(Approver), result.EvaluatedEntitlementVersion);
        Assert.Equal(Now, result.EvaluatedAt);
    }

    [Fact]
    public async Task Maker_cannot_approve_own_request_through_another_session()
    {
        TestSetup setup = new();
        setup.Grant(Maker, Approve);

        ApprovalEligibilityResult result = await setup.Evaluate(
            Maker,
            "session-second",
            Maker.Value,
            Institution,
            setup.VersionFor(Maker),
            strength: AuthenticationStrength.StepUp);

        Assert.False(result.IsEligible);
        Assert.Equal(DenialReason.SegregationOfDuties, result.Reason);
        Assert.False(result.MakerSeparated);
    }

    [Fact]
    public async Task Service_identity_is_not_an_eligible_approver()
    {
        TestSetup setup = new();
        setup.Grant(Approver, Approve);

        ApprovalEligibilityResult result = await setup.Evaluate(
            Approver,
            "session-service",
            Maker.Value,
            Institution,
            setup.VersionFor(Approver),
            actorType: ActorType.Service);

        Assert.False(result.IsEligible);
        Assert.Equal(DenialReason.ServiceIdentityNotPermitted, result.Reason);
    }

    [Fact]
    public async Task Cross_institution_request_is_denied()
    {
        TestSetup setup = new();
        setup.Grant(Approver, Approve);

        ApprovalEligibilityResult result = await setup.Evaluate(
            Approver,
            "session-approver",
            Maker.Value,
            OtherInstitution,
            setup.VersionFor(Approver));

        Assert.False(result.IsEligible);
        Assert.Equal(DenialReason.WrongInstitution, result.Reason);
    }

    [Fact]
    public async Task Missing_permission_is_ineligible()
    {
        TestSetup setup = new();

        ApprovalEligibilityResult result = await setup.Evaluate(
            Approver,
            "session-approver",
            Maker.Value,
            Institution,
            setup.VersionFor(Approver));

        Assert.False(result.IsEligible);
        Assert.Equal(DenialReason.MissingPermission, result.Reason);
    }

    [Fact]
    public async Task Stale_entitlement_is_ineligible()
    {
        TestSetup setup = new();
        setup.Grant(Approver, Approve);
        long currentVersion = setup.VersionFor(Approver);

        ApprovalEligibilityResult result = await setup.Evaluate(
            Approver,
            "session-approver",
            Maker.Value,
            Institution,
            currentVersion - 1);

        Assert.False(result.IsEligible);
        Assert.Equal(DenialReason.StaleEntitlement, result.Reason);
    }

    [Fact]
    public async Task Step_up_is_not_required_to_ask_whether_an_actor_may_approve()
    {
        TestSetup setup = new();
        setup.Grant(Approver, Approve);

        ApprovalEligibilityResult result = await setup.Evaluate(
            Approver,
            "session-approver",
            Maker.Value,
            Institution,
            setup.VersionFor(Approver),
            strength: AuthenticationStrength.MultiFactor);

        Assert.True(result.IsEligible);
    }

    [Fact]
    public async Task Every_question_records_evidence_without_the_other_actor_identity()
    {
        TestSetup setup = new();
        setup.Grant(Approver, Approve);

        await setup.Evaluate(Approver, "session-approver", Maker.Value, Institution, setup.VersionFor(Approver));
        await setup.Evaluate(Approver, "session-approver", Maker.Value, Institution, setup.VersionFor(Approver));

        Assert.Equal(2, setup.Audit.Entries.Count);
        foreach (AuthorisationAuditEntry entry in setup.Audit.Entries)
        {
            Assert.Equal("allowed", entry.Decision);
            Assert.Equal(Approve.Value, entry.Permission);
            Assert.Equal("identity.approval.eligibility.review", entry.Source);
            Assert.Equal(Approver.Value, entry.ActorId?.Value);
            Assert.DoesNotContain(Maker.Value, entry.Permission + entry.Source + entry.CorrelationId, StringComparison.Ordinal);
        }
    }

    private sealed class TestSetup
    {
        public TestSetup()
        {
            StubTimeProvider time = new(Now);
            Store = new InMemoryEntitlementStore(time);
            Audit = new InMemoryAuthorisationAuditWriter();
            AuthorisationPolicy policy = new(Store, Store, time, TimeSpan.FromSeconds(60));
            Service = new ApprovalEligibilityService(new AuthorisationService(policy, Audit, time, "test-policy"), time);
        }

        public InMemoryEntitlementStore Store { get; }

        public InMemoryAuthorisationAuditWriter Audit { get; }

        public ApprovalEligibilityService Service { get; }

        public void Grant(ActorId actorId, PermissionIdentifier permission) =>
            Store.Grant(actorId, Institution, permission, GrantScope.ForInstitution(Institution));

        public long VersionFor(ActorId actorId) => Store.GetCurrentVersion(actorId, Institution);

        public Task<ApprovalEligibilityResult> Evaluate(
            ActorId actorId,
            string sessionId,
            string makerActorId,
            InstitutionId requestedInstitution,
            long entitlementVersion,
            ActorType actorType = ActorType.Human,
            AuthenticationStrength strength = AuthenticationStrength.MultiFactor) =>
            Service.EvaluateAsync(
                Approve,
                makerActorId,
                requestedInstitution,
                new AuthenticatedActor
                {
                    Id = actorId,
                    Type = actorType,
                    InstitutionId = Institution,
                    SessionId = sessionId,
                    EntitlementVersion = entitlementVersion,
                    Strength = strength,
                },
                "correlation-1");
    }
}
