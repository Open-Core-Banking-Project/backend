using System;
using System.Linq;
using System.Text.Json;
using CoreBanking.BuildingBlocks.Application.Messaging;
using CoreBanking.IdentityAccess.Contracts;
using CoreBanking.IdentityAccess.Domain.Identities;
using CoreBanking.IdentityAccess.Infrastructure.Identity;
using Xunit;

namespace CoreBanking.IdentityAccess.UnitTests;

[Trait("Category", "Unit")]
public sealed class IdentityLifecycleRulesTests
{
    [Theory]
    [InlineData("customer", IdentityKind.Customer)]
    [InlineData("staff", IdentityKind.Staff)]
    [InlineData("agent", IdentityKind.Agent)]
    [InlineData("corporate_user", IdentityKind.CorporateUser)]
    [InlineData("administrator", IdentityKind.Administrator)]
    [InlineData("service", IdentityKind.Service)]
    public void Every_supported_actor_kind_is_explicit(string contractValue, IdentityKind expected)
    {
        Assert.Equal(expected, IdentityLifecycle.ParseKind(contractValue));
        Assert.Equal(contractValue, IdentityLifecycle.ToContractValue(expected));
    }

    [Fact]
    public void Unknown_actor_kind_is_rejected() =>
        Assert.Throws<ArgumentException>(() => IdentityLifecycle.ParseKind("human"));

    [Theory]
    [InlineData(IdentityState.Pending, IdentityState.Active)]
    [InlineData(IdentityState.Active, IdentityState.Locked)]
    [InlineData(IdentityState.Active, IdentityState.Suspended)]
    [InlineData(IdentityState.Active, IdentityState.Disabled)]
    [InlineData(IdentityState.Locked, IdentityState.Active)]
    [InlineData(IdentityState.Suspended, IdentityState.Active)]
    [InlineData(IdentityState.Disabled, IdentityState.Closed)]
    public void Approved_identity_lifecycle_transitions_are_explicit(IdentityState current, IdentityState requested) =>
        Assert.True(IdentityLifecycle.CanTransition(current, requested));

    [Theory]
    [InlineData(IdentityState.Pending, IdentityState.Closed)]
    [InlineData(IdentityState.Active, IdentityState.Closed)]
    [InlineData(IdentityState.Disabled, IdentityState.Active)]
    [InlineData(IdentityState.Closed, IdentityState.Active)]
    public void Unsupported_identity_lifecycle_transitions_are_denied(IdentityState current, IdentityState requested) =>
        Assert.False(IdentityLifecycle.CanTransition(current, requested));

    [Fact]
    public void Provider_subject_fingerprint_is_stable_and_does_not_expose_the_subject()
    {
        string fingerprint = IdentityLifecycleService.FingerprintProviderSubject(
            "Keycloak",
            "Institution-Realm",
            "provider-subject-001");

        Assert.Equal(
            fingerprint,
            IdentityLifecycleService.FingerprintProviderSubject(
                "keycloak",
                "institution-realm",
                "provider-subject-001"));
        Assert.Equal(64, fingerprint.Length);
        Assert.DoesNotContain("provider-subject-001", fingerprint, StringComparison.Ordinal);
    }

    [Fact]
    public void Registration_contract_cannot_mass_assign_tenant_identity_or_status()
    {
        string[] propertyNames = typeof(RegisterIdentityV1)
            .GetProperties()
            .Select(property => property.Name)
            .ToArray();

        Assert.Equal(["IdentityType", "DisplayName", "ProviderLink"], propertyNames);
        Assert.DoesNotContain("InstitutionId", propertyNames);
        Assert.DoesNotContain("IdentityId", propertyNames);
        Assert.DoesNotContain("Status", propertyNames);
    }

    [Fact]
    public void Registered_event_contains_no_provider_subject_or_fingerprint()
    {
        IdentityRegisteredV1 registered = new(
            "identity-001",
            "membership-001",
            "customer",
            "pending",
            true);

        using JsonDocument document = JsonDocument.Parse(EventContract.Serialize(registered));
        string[] properties = document.RootElement.EnumerateObject()
            .Select(property => property.Name)
            .ToArray();

        Assert.DoesNotContain("subject", properties);
        Assert.DoesNotContain("subjectFingerprint", properties);
        Assert.DoesNotContain("provider", properties);
    }
}
