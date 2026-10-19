using System;
using System.Globalization;
using System.Linq;
using System.Security.Claims;
using CoreBanking.BuildingBlocks.Application.Security;
using CoreBanking.BuildingBlocks.Domain.Identity;
using CoreBanking.IdentityAccess.Application.Authorisation;

namespace CoreBanking.IdentityAccess.Infrastructure.Authorisation;

/// <summary>
/// Projects a principal already validated by ASP.NET Core JWT bearer authentication into the
/// provider-neutral actor used by application authorisation. Keycloak roles and permissions are
/// deliberately ignored; Identity and Access remains authoritative for effective entitlements.
/// Decision: ADR-0008 and ADR-0009.
/// </summary>
public sealed class KeycloakActorContextResolver : IActorContextResolver
{
    public const string ActorTypeClaim = "actor_type";
    public const string InstitutionIdClaim = "institution_id";
    public const string DeviceIdClaim = "device_id";
    public const string EntitlementVersionClaim = "entitlement_version";
    public const string AuthenticationStrengthClaim = "authentication_strength";

    public AuthenticatedActor? Resolve(
        ClaimsPrincipal authenticatedPrincipal,
        string? developmentAuthenticationMaterial)
    {
        ArgumentNullException.ThrowIfNull(authenticatedPrincipal);

        if (authenticatedPrincipal.Identity?.IsAuthenticated != true
            || !TryReadRequiredClaim(authenticatedPrincipal, "sub", out string? subject)
            || !TryReadRequiredClaim(authenticatedPrincipal, ActorTypeClaim, out string? actorTypeValue)
            || !TryReadRequiredClaim(authenticatedPrincipal, InstitutionIdClaim, out string? institutionId)
            || !TryReadRequiredClaim(authenticatedPrincipal, "sid", out string? sessionId)
            || !TryReadRequiredClaim(authenticatedPrincipal, EntitlementVersionClaim, out string? entitlementVersionValue)
            || !TryReadRequiredClaim(authenticatedPrincipal, AuthenticationStrengthClaim, out string? strengthValue)
            || !Enum.TryParse(actorTypeValue, ignoreCase: true, out ActorType actorType)
            || !Enum.IsDefined(actorType)
            || !long.TryParse(entitlementVersionValue, NumberStyles.None, CultureInfo.InvariantCulture, out long entitlementVersion)
            || entitlementVersion < 0
            || !Enum.TryParse(strengthValue, ignoreCase: true, out AuthenticationStrength strength)
            || !Enum.IsDefined(strength)
            || strength == AuthenticationStrength.None
            || !TryReadOptionalClaim(authenticatedPrincipal, DeviceIdClaim, out string? deviceId))
        {
            return null;
        }

        return new AuthenticatedActor
        {
            Id = ActorId.From(subject!),
            Type = actorType,
            InstitutionId = InstitutionId.From(institutionId!),
            SessionId = sessionId,
            DeviceId = deviceId,
            EntitlementVersion = entitlementVersion,
            Strength = strength,
        };
    }

    private static bool TryReadRequiredClaim(ClaimsPrincipal principal, string claimType, out string? value)
    {
        bool found = TryReadOptionalClaim(principal, claimType, out value);
        return found && !string.IsNullOrWhiteSpace(value);
    }

    private static bool TryReadOptionalClaim(ClaimsPrincipal principal, string claimType, out string? value)
    {
        string[] values = principal.FindAll(claimType)
            .Select(claim => claim.Value.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (values.Length > 1)
        {
            value = null;
            return false;
        }

        value = values.SingleOrDefault();
        return true;
    }
}
