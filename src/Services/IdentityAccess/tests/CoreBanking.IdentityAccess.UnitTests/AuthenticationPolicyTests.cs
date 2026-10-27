using System;
using System.Collections.Generic;
using System.Threading;
using CoreBanking.IdentityAccess.Application.Authentication;
using CoreBanking.IdentityAccess.Domain.Authentication;
using CoreBanking.IdentityAccess.Infrastructure.Authentication;
using Xunit;

namespace CoreBanking.IdentityAccess.UnitTests;

public sealed class AuthenticationPolicyTests
{
    [Fact]
    public void Active_policy_is_effective_only_inside_its_interval()
    {
        var policy = new AuthenticationPolicyVersion(
            "policy-v1",
            AuthenticationChannel.Web,
            "keycloak",
            AuthenticationPolicyStatus.Active,
            new DateTimeOffset(2026, 10, 27, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 10, 28, 0, 0, 0, TimeSpan.Zero));

        Assert.True(policy.IsEffectiveAt(new DateTimeOffset(2026, 10, 27, 12, 0, 0, TimeSpan.Zero)));
        Assert.False(policy.IsEffectiveAt(new DateTimeOffset(2026, 10, 28, 0, 0, 0, TimeSpan.Zero)));
    }

    [Fact]
    public void Policy_rejects_an_invalid_effective_interval()
    {
        var policy = new AuthenticationPolicyVersion(
            "policy-v1", AuthenticationChannel.Web, "keycloak", AuthenticationPolicyStatus.Draft,
            new DateTimeOffset(2026, 10, 28, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 10, 27, 0, 0, 0, TimeSpan.Zero));

        Assert.Throws<ArgumentException>(policy.Validate);
    }

    [Fact]
    public async System.Threading.Tasks.Task Synthetic_adapter_returns_only_a_provider_subject()
    {
        var adapter = new SyntheticAuthenticationProviderAdapter(new Dictionary<string, string>
        {
            ["synthetic.fixture.customer-1"] = "synthetic-subject-customer-1",
        });

        ProviderAuthenticationResult result = await adapter.AuthenticateSyntheticAsync(
            new SyntheticAuthenticationRequest("synthetic.fixture.customer-1", "web"),
            CancellationToken.None);

        Assert.Equal("authenticated", result.Outcome);
        Assert.Equal("synthetic-subject-customer-1", result.ProviderSubject);
    }

    [Fact]
    public async System.Threading.Tasks.Task Production_adapter_rejects_direct_credential_authentication()
    {
        var adapter = new KeycloakAuthenticationProviderAdapter();
        ProviderAuthenticationResult result = await adapter.AuthenticateSyntheticAsync(
            new SyntheticAuthenticationRequest("anything", "web"), CancellationToken.None);

        Assert.Equal("unsupported", result.Outcome);
        Assert.Null(result.ProviderSubject);
    }
}
