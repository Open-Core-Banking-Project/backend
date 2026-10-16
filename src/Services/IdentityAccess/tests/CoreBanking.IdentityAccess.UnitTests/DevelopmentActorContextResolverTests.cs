using System;
using System.Text;
using System.Text.Json;
using CoreBanking.BuildingBlocks.Application.Security;
using CoreBanking.IdentityAccess.Infrastructure.Authorisation;
using Xunit;

namespace CoreBanking.IdentityAccess.UnitTests;

/// <summary>
/// The development resolver emulates validated token claims. These tests prove that it resolves
/// only the documented claims, that malformed material yields no actor, and that a client cannot
/// smuggle permissions or roles through the payload.
/// Decision: ADR-0008; epic 03 prohibited assumptions.
/// </summary>
[Trait("Category", "Unit")]
public sealed class DevelopmentActorContextResolverTests
{
    private static readonly string[] PermissionClaims = ["platform.foundation.execute", "identity.role.create"];

    private static readonly string[] RoleClaims = ["platform-administrator"];

    private readonly DevelopmentActorContextResolver _resolver = new();

    [Fact]
    public void Valid_material_resolves_the_actor()
    {
        string material = Material(new
        {
            actorId = "actor-1",
            actorType = "Human",
            institutionId = "institution-1",
            sessionId = "session-1",
            deviceId = "device-1",
            entitlementVersion = 7,
            authenticationStrength = "MultiFactor",
        });

        AuthenticatedActor? actor = _resolver.Resolve(material);

        Assert.NotNull(actor);
        Assert.Equal("actor-1", actor!.Id.Value);
        Assert.Equal("institution-1", actor.InstitutionId.Value);
        Assert.Equal(7, actor.EntitlementVersion);
        Assert.Equal(ActorType.Human, actor.Type);
        Assert.Equal(AuthenticationStrength.MultiFactor, actor.Strength);
    }

    [Fact]
    public void Missing_material_yields_no_actor()
    {
        Assert.Null(_resolver.Resolve(null));
        Assert.Null(_resolver.Resolve("   "));
    }

    [Fact]
    public void Malformed_material_yields_no_actor()
    {
        Assert.Null(_resolver.Resolve("not-base64"));
        Assert.Null(_resolver.Resolve(Convert.ToBase64String(Encoding.UTF8.GetBytes("not json"))));
    }

    [Fact]
    public void Material_without_actor_or_institution_yields_no_actor()
    {
        Assert.Null(_resolver.Resolve(Material(new { actorType = "Human" })));
        Assert.Null(_resolver.Resolve(Material(new { actorId = "actor-1" })));
    }

    [Fact]
    public void Permission_and_role_claims_in_the_payload_are_ignored()
    {
        string material = Material(new
        {
            actorId = "actor-1",
            actorType = "Human",
            institutionId = "institution-1",
            entitlementVersion = 1,
            authenticationStrength = "SingleFactor",
            permissions = PermissionClaims,
            roles = RoleClaims,
            isSuperUser = true,
        });

        AuthenticatedActor? actor = _resolver.Resolve(material);

        Assert.NotNull(actor);
        // The actor carries no permission, role or context claim from the request.
        Assert.Empty(actor!.ValidatedContext);
        Assert.Equal(ActorType.Human, actor.Type);
    }

    private static string Material(object payload) =>
        Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(payload));
}
