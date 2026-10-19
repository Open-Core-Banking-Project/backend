using System.Collections.Generic;
using System.Security.Claims;
using CoreBanking.BuildingBlocks.Application.Security;
using CoreBanking.IdentityAccess.Infrastructure.Authorisation;
using Xunit;

namespace CoreBanking.IdentityAccess.UnitTests;

/// <summary>
/// Security evidence for IAM-001 and SEC-002: only an authenticated principal with the complete,
/// unambiguous provider claim contract can establish an actor and institution boundary.
/// </summary>
[Trait("Category", "Unit")]
public sealed class KeycloakActorContextResolverTests
{
    private readonly KeycloakActorContextResolver _resolver = new();

    [Fact]
    public void Validated_keycloak_claims_resolve_an_actor()
    {
        AuthenticatedActor? actor = _resolver.Resolve(Principal(ValidClaims()), null);

        Assert.NotNull(actor);
        Assert.Equal("actor-1", actor!.Id.Value);
        Assert.Equal(ActorType.Human, actor.Type);
        Assert.Equal("institution-1", actor.InstitutionId.Value);
        Assert.Equal("session-1", actor.SessionId);
        Assert.Equal("device-1", actor.DeviceId);
        Assert.Equal(7, actor.EntitlementVersion);
        Assert.Equal(AuthenticationStrength.MultiFactor, actor.Strength);
    }

    [Fact]
    public void Unauthenticated_principal_is_rejected()
    {
        Assert.Null(_resolver.Resolve(Principal(ValidClaims(), authenticated: false), null));
    }

    [Theory]
    [InlineData("sub")]
    [InlineData("actor_type")]
    [InlineData("institution_id")]
    [InlineData("sid")]
    [InlineData("entitlement_version")]
    [InlineData("authentication_strength")]
    public void Missing_required_claim_is_rejected(string missingClaim)
    {
        List<Claim> claims = ValidClaims();
        claims.RemoveAll(claim => claim.Type == missingClaim);

        Assert.Null(_resolver.Resolve(Principal(claims), null));
    }

    [Theory]
    [InlineData("actor_type", "administrator")]
    [InlineData("entitlement_version", "-1")]
    [InlineData("entitlement_version", "not-a-number")]
    [InlineData("authentication_strength", "unknown")]
    [InlineData("authentication_strength", "None")]
    public void Invalid_security_claim_is_rejected(string claimType, string invalidValue)
    {
        List<Claim> claims = ValidClaims();
        claims.RemoveAll(claim => claim.Type == claimType);
        claims.Add(new Claim(claimType, invalidValue));

        Assert.Null(_resolver.Resolve(Principal(claims), null));
    }

    [Fact]
    public void Conflicting_duplicate_claim_is_rejected()
    {
        List<Claim> claims = ValidClaims();
        claims.Add(new Claim("institution_id", "institution-2"));

        Assert.Null(_resolver.Resolve(Principal(claims), null));
    }

    [Fact]
    public void Keycloak_roles_and_permissions_are_not_projected()
    {
        List<Claim> claims = ValidClaims();
        claims.Add(new Claim("realm_access", "realm-admin"));
        claims.Add(new Claim("permissions", "platform.foundation.execute"));

        AuthenticatedActor? actor = _resolver.Resolve(Principal(claims), null);

        Assert.NotNull(actor);
        Assert.Empty(actor!.ValidatedContext);
    }

    private static ClaimsPrincipal Principal(IEnumerable<Claim> claims, bool authenticated = true)
    {
        ClaimsIdentity identity = new(claims, authenticated ? "Bearer" : null);
        return new ClaimsPrincipal(identity);
    }

    private static List<Claim> ValidClaims() =>
    [
        new Claim("sub", "actor-1"),
        new Claim("actor_type", "human"),
        new Claim("institution_id", "institution-1"),
        new Claim("sid", "session-1"),
        new Claim("device_id", "device-1"),
        new Claim("entitlement_version", "7"),
        new Claim("authentication_strength", "MultiFactor"),
    ];
}
