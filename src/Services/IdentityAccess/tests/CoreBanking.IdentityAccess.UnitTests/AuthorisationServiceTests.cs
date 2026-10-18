using System;
using System.Linq;
using System.Threading.Tasks;
using CoreBanking.BuildingBlocks.Application.Security;
using CoreBanking.BuildingBlocks.Domain.Identity;
using CoreBanking.IdentityAccess.Application.Authorisation;
using CoreBanking.IdentityAccess.Infrastructure.Authorisation;
using Xunit;

namespace CoreBanking.IdentityAccess.UnitTests;

/// <summary>
/// Every decision produces evidence, and that evidence contains identifiers and the decision
/// only: no resource identifiers, credentials or personal data.
/// Decision: ADR-0010; epic 03 "negative decisions create appropriate evidence".
/// </summary>
[Trait("Category", "Unit")]
public sealed class AuthorisationServiceTests
{
    private static readonly InstitutionId Institution = InstitutionId.From("institution-1");
    private static readonly ActorId Administrator = ActorId.From("actor-admin");
    private static readonly PermissionIdentifier Execute = PermissionIdentifier.Parse("platform.foundation.execute");
    private static readonly DateTimeOffset Now = new(2026, 10, 16, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Allowed_decision_records_evidence()
    {
        TestSetup setup = CreateSetup();
        setup.Store.Grant(Administrator, Institution, Execute, GrantScope.ForInstitution(Institution));

        await setup.Service.AuthoriseAsync(Request(setup.VersionFor(Administrator), null, null), "identity.foundation.execute", "correlation-1");

        AuthorisationAuditEntry entry = Assert.Single(setup.Audit.Entries);
        Assert.Equal("allowed", entry.Decision);
        Assert.Equal("none", entry.Reason);
        Assert.Equal(Execute.Value, entry.Permission);
        Assert.Equal("correlation-1", entry.CorrelationId);
        Assert.Equal(Administrator.Value, entry.ActorId?.Value);
        Assert.Equal(Institution.Value, entry.InstitutionId?.Value);
        Assert.Equal(Now, entry.OccurredAt);
    }

    [Fact]
    public async Task Denied_decision_records_evidence_with_a_stable_reason()
    {
        TestSetup setup = CreateSetup();

        await setup.Service.AuthoriseAsync(Request(setup.VersionFor(Administrator), null, null), "identity.foundation.execute", "correlation-2");

        AuthorisationAuditEntry entry = Assert.Single(setup.Audit.Entries);
        Assert.Equal("denied", entry.Decision);
        Assert.Equal(AuthorisationCodes.MissingPermission, entry.Reason);
    }

    [Fact]
    public async Task Evidence_never_contains_resource_identifiers()
    {
        TestSetup setup = CreateSetup();

        await setup.Service.AuthoriseAsync(
            Request(setup.VersionFor(Administrator), "customer-9182", "account-4417"),
            "identity.foundation.execute",
            "correlation-3");

        AuthorisationAuditEntry entry = Assert.Single(setup.Audit.Entries);
        string[] recordedFields =
        {
            entry.Permission,
            entry.Action,
            entry.Decision,
            entry.Reason,
            entry.Source,
            entry.CorrelationId,
            entry.PolicyVersion ?? string.Empty,
        };

        string flattened = string.Join("|", recordedFields);

        Assert.DoesNotContain("customer-9182", flattened, StringComparison.Ordinal);
        Assert.DoesNotContain("account-4417", flattened, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Missing_authentication_evidence_has_no_actor_or_institution()
    {
        TestSetup setup = CreateSetup();

        AuthorisationRequest request = new()
        {
            Requirement = PermissionRequirement.For(Execute),
            Scope = ResourceScope.ForInstitution(Institution),
            Actor = null,
        };

        await setup.Service.AuthoriseAsync(request, "identity.foundation.execute", "correlation-4");

        AuthorisationAuditEntry entry = Assert.Single(setup.Audit.Entries);
        Assert.Null(entry.ActorId);
        Assert.Null(entry.InstitutionId);
        Assert.Equal(AuthorisationCodes.MissingAuthentication, entry.Reason);
    }

    [Fact]
    public async Task Evidence_is_append_only()
    {
        TestSetup setup = CreateSetup();

        await setup.Service.AuthoriseAsync(Request(setup.VersionFor(Administrator), null, null), "identity.foundation.execute", "correlation-5");
        await setup.Service.AuthoriseAsync(Request(setup.VersionFor(Administrator), null, null), "identity.foundation.execute", "correlation-6");

        Assert.Equal(2, setup.Audit.Entries.Count);
        Assert.Equal(2, setup.Audit.Entries.Select(entry => entry.AuditEventId).Distinct(StringComparer.Ordinal).Count());
    }

    private static AuthorisationRequest Request(long entitlementVersion, string? customerId, string? accountId) => new()
    {
        Requirement = PermissionRequirement.For(Execute),
        Scope = new ResourceScope
        {
            InstitutionId = Institution,
            CustomerId = customerId,
            AccountId = accountId,
        },
        Actor = new AuthenticatedActor
        {
            Id = Administrator,
            Type = ActorType.Human,
            InstitutionId = Institution,
            SessionId = "session-1",
            EntitlementVersion = entitlementVersion,
            Strength = AuthenticationStrength.MultiFactor,
        },
    };

    private static TestSetup CreateSetup() => new();

    private sealed class TestSetup
    {
        public TestSetup()
        {
            StubTimeProvider time = new(Now);
            Store = new InMemoryEntitlementStore(time);
            Audit = new InMemoryAuthorisationAuditWriter();
            Policy = new AuthorisationPolicy(Store, Store, time, TimeSpan.FromSeconds(60));
            Service = new AuthorisationService(Policy, Audit, time, "test-policy");
        }

        public InMemoryEntitlementStore Store { get; }

        public InMemoryAuthorisationAuditWriter Audit { get; }

        public AuthorisationPolicy Policy { get; }

        public AuthorisationService Service { get; }

        public long VersionFor(ActorId actorId) => Store.FindSnapshot(actorId, Institution)?.Version ?? 0;
    }
}
