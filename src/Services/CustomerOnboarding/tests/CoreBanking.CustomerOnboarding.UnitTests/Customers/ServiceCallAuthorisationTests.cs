using System;
using CoreBanking.CustomerOnboarding.Application.Customers;
using CoreBanking.CustomerOnboarding.Contracts;
using Xunit;

namespace CoreBanking.CustomerOnboarding.UnitTests.Customers;

/// <summary>
/// The internal facts route is reachable by a service token only when every condition holds: service
/// actor, allow-listed client, approved audience, the exact capability and a trusted institution.
/// Decision: P1-E04-D17.
/// </summary>
public sealed class ServiceCallAuthorisationTests
{
    private static readonly string[] AllowedClients = ["core-ledger"];

    [Fact]
    public void An_allow_listed_service_with_the_capability_is_allowed()
    {
        ServiceCallDecision decision = ServiceCallAuthorisation.Authorise(Claims(), AllowedClients);

        Assert.True(decision.IsAllowed);
        Assert.Equal("institution-1", decision.InstitutionId);
        Assert.Equal("core-ledger", decision.ClientId);
    }

    [Fact]
    public void An_unauthenticated_caller_is_refused()
    {
        ServiceCallDecision decision = ServiceCallAuthorisation.Authorise(null, AllowedClients);

        Assert.False(decision.IsAllowed);
        Assert.Equal("service_authentication_required", decision.Code);
    }

    [Fact]
    public void A_human_actor_cannot_use_the_service_route()
    {
        ServiceCallDecision decision = ServiceCallAuthorisation.Authorise(Claims(actorType: "human"), AllowedClients);

        Assert.Equal("service_actor_required", decision.Code);
    }

    [Fact]
    public void An_unlisted_client_is_refused_even_with_a_valid_token()
    {
        ServiceCallDecision decision = ServiceCallAuthorisation.Authorise(Claims(clientId: "other-service"), AllowedClients);

        Assert.Equal("service_client_not_allowed", decision.Code);
    }

    [Fact]
    public void An_empty_allow_list_denies_every_caller()
    {
        ServiceCallDecision decision = ServiceCallAuthorisation.Authorise(Claims(), Array.Empty<string>());

        Assert.Equal("service_client_not_allowed", decision.Code);
    }

    [Fact]
    public void A_token_for_another_audience_is_refused()
    {
        ServiceCallDecision decision = ServiceCallAuthorisation.Authorise(
            Claims(audiences: ["corebanking-identity-api"]),
            AllowedClients);

        Assert.Equal("service_audience_mismatch", decision.Code);
    }

    [Fact]
    public void A_token_without_the_capability_is_refused()
    {
        ServiceCallDecision decision = ServiceCallAuthorisation.Authorise(
            Claims(scopes: ["service.identity.entitlements_read"]),
            AllowedClients);

        Assert.Equal("service_capability_missing", decision.Code);
    }

    [Fact]
    public void A_token_without_trusted_institution_context_is_refused()
    {
        ServiceCallDecision decision = ServiceCallAuthorisation.Authorise(
            Claims(institutionId: null),
            AllowedClients);

        Assert.Equal("institution_context_missing", decision.Code);
    }

    [Fact]
    public void The_application_and_contract_constants_cannot_drift()
    {
        Assert.Equal(CustomerFactsContractV1.Capability, ServiceCallAuthorisation.RequiredCapability);
        Assert.Equal(CustomerFactsContractV1.Audience, ServiceCallAuthorisation.RequiredAudience);
        Assert.Equal("service.customer.eligibility_facts_read", CustomerFactsContractV1.Capability);
        Assert.Equal(TimeSpan.FromSeconds(60), CustomerFactsContractV1.MaximumValidity);
        Assert.Equal(
            CustomerFactsContractV1.MaximumValidity,
            CustomerEligibilityFactsPolicy.ValidityWindow);
    }

    private static ServiceCallerClaims Claims(
        string? actorType = "service",
        string? clientId = "core-ledger",
        string? institutionId = "institution-1",
        string[]? audiences = null,
        string[]? scopes = null) =>
        new(
            actorType,
            clientId,
            institutionId,
            audiences ?? [CustomerFactsContractV1.Audience],
            scopes ?? [CustomerFactsContractV1.Capability]);
}
