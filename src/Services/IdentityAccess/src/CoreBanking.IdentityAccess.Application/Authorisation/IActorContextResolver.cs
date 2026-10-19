using System.Security.Claims;
using CoreBanking.BuildingBlocks.Application.Security;

namespace CoreBanking.IdentityAccess.Application.Authorisation;

/// <summary>
/// Resolves the authenticated actor from validated authentication material. The production
/// implementation validates an access token issued by the identity provider; the development
/// implementation emulates that contract. Implementations must never build an actor from
/// client-supplied role, tenant or permission claims.
/// Decision: docs/adr/ADR-0008-authentication-provider-token-model-and-service-identity.md
/// </summary>
public interface IActorContextResolver
{
    /// <summary>
    /// Returns the authenticated actor, or <c>null</c> when authentication did not succeed.
    /// The principal is populated only by the configured authentication middleware. The separate
    /// development material exists solely for the Development-only reference resolver.
    /// </summary>
    AuthenticatedActor? Resolve(ClaimsPrincipal authenticatedPrincipal, string? developmentAuthenticationMaterial);
}
