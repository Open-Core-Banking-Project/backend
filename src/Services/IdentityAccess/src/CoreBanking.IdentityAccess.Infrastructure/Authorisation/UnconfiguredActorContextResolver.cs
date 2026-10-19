using System.Security.Claims;
using CoreBanking.BuildingBlocks.Application.Security;
using CoreBanking.IdentityAccess.Application.Authorisation;

namespace CoreBanking.IdentityAccess.Infrastructure.Authorisation;

/// <summary>
/// The resolver used when no identity provider is configured. It resolves nothing, so every
/// protected request is refused with <c>auth.missing_authentication</c>. This exists so that a
/// non-development environment fails closed instead of falling back to a development identity.
/// Decision: ADR-0008.
/// </summary>
public sealed class UnconfiguredActorContextResolver : IActorContextResolver
{
    public AuthenticatedActor? Resolve(
        ClaimsPrincipal authenticatedPrincipal,
        string? developmentAuthenticationMaterial) => null;
}
