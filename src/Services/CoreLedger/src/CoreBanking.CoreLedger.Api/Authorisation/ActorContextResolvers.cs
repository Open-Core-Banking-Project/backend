using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using CoreBanking.BuildingBlocks.Application.Security;
using CoreBanking.BuildingBlocks.Domain.Identity;

namespace CoreBanking.CoreLedger.Api.Authorisation;

public interface IActorContextResolver
{
    AuthenticatedActor? Resolve(string? authenticationMaterial);
}

public sealed class DevelopmentActorContextResolver : IActorContextResolver
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip,
        Converters = { new JsonStringEnumConverter() },
    };

    public AuthenticatedActor? Resolve(string? authenticationMaterial)
    {
        if (string.IsNullOrWhiteSpace(authenticationMaterial))
        {
            return null;
        }

        try
        {
            DevelopmentActorClaims? claims = JsonSerializer.Deserialize<DevelopmentActorClaims>(
                Convert.FromBase64String(authenticationMaterial),
                SerializerOptions);
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
        catch (JsonException)
        {
            return null;
        }
        catch (FormatException)
        {
            return null;
        }
    }

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

public sealed class UnconfiguredActorContextResolver : IActorContextResolver
{
    public AuthenticatedActor? Resolve(string? authenticationMaterial) => null;
}
