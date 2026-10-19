using System;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using CoreBanking.BuildingBlocks.Application.Security;
using CoreBanking.BuildingBlocks.Domain.Identity;
using CoreBanking.IdentityAccess.Application.Authorisation;

namespace CoreBanking.IdentityAccess.Infrastructure.Authorisation;

/// <summary>
/// Development-only stand-in for access-token validation. It decodes the
/// <c>X-Development-Actor</c> header, which emulates the claims of a token the identity
/// provider would have signed, and is registered only when the environment is Development.
///
/// Security properties required by the epic:
///   - the header is not trusted in any other environment;
///   - a malformed or absent header yields no actor, which becomes HTTP 401;
///   - any permission, role or grant claim in the payload is ignored, because permissions are
///     resolved from the entitlement snapshot and never from client input.
///
/// It lives beside the other replaceable development stand-ins so the API project stays a
/// composition root. Decision: docs/adr/ADR-0008-authentication-provider-token-model-and-service-identity.md
/// </summary>
public sealed class DevelopmentActorContextResolver : IActorContextResolver
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip,
        // Enumerated claims are written by name so the development material stays readable.
        Converters = { new JsonStringEnumConverter() },
    };

    public AuthenticatedActor? Resolve(
        ClaimsPrincipal authenticatedPrincipal,
        string? developmentAuthenticationMaterial)
    {
        ArgumentNullException.ThrowIfNull(authenticatedPrincipal);

        if (string.IsNullOrWhiteSpace(developmentAuthenticationMaterial))
        {
            return null;
        }

        DevelopmentActorClaims? claims;
        try
        {
            byte[] payload = Convert.FromBase64String(developmentAuthenticationMaterial);
            claims = JsonSerializer.Deserialize<DevelopmentActorClaims>(payload, SerializerOptions);
        }
        catch (JsonException)
        {
            return null;
        }
        catch (FormatException)
        {
            return null;
        }

        if (claims is null
            || string.IsNullOrWhiteSpace(claims.ActorId)
            || string.IsNullOrWhiteSpace(claims.InstitutionId))
        {
            return null;
        }

        return new AuthenticatedActor
        {
            Id = ActorId.From(claims.ActorId),
            Type = claims.ActorType,
            InstitutionId = InstitutionId.From(claims.InstitutionId),
            SessionId = claims.SessionId,
            DeviceId = claims.DeviceId,
            EntitlementVersion = claims.EntitlementVersion,
            Strength = claims.AuthenticationStrength,
        };
    }

    /// <summary>
    /// The claims this stand-in accepts. Unknown members, including any permission or role
    /// claim, are skipped rather than honoured.
    /// </summary>
    private sealed record DevelopmentActorClaims
    {
        public string? ActorId { get; init; }

        public ActorType ActorType { get; init; }

        public string? InstitutionId { get; init; }

        public string? SessionId { get; init; }

        public string? DeviceId { get; init; }

        public long EntitlementVersion { get; init; }

        public AuthenticationStrength AuthenticationStrength { get; init; }
    }
}
