using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using CoreBanking.BuildingBlocks.Application.Security;
using CoreBanking.BuildingBlocks.Application.Tenancy;
using CoreBanking.BuildingBlocks.Domain.Identity;
using CoreBanking.IntegrationHub.Application.ProviderAdministration;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace CoreBanking.IntegrationHub.Api.Authorisation;

public interface IActorContextResolver { AuthenticatedActor? Resolve(string? authenticationMaterial); }

public sealed class DevelopmentActorContextResolver : IActorContextResolver
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip,
        Converters = { new JsonStringEnumConverter() },
    };

    public AuthenticatedActor? Resolve(string? authenticationMaterial)
    {
        if (string.IsNullOrWhiteSpace(authenticationMaterial)) return null;
        try
        {
            Claims? claims = JsonSerializer.Deserialize<Claims>(Convert.FromBase64String(authenticationMaterial), Options);
            if (claims is null || string.IsNullOrWhiteSpace(claims.ActorId) || string.IsNullOrWhiteSpace(claims.InstitutionId)) return null;
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
        catch (JsonException) { return null; }
        catch (FormatException) { return null; }
    }

    private sealed record Claims(string? ActorId, ActorType ActorType, string? InstitutionId, string? SessionId, string? DeviceId, long EntitlementVersion, AuthenticationStrength AuthenticationStrength);
}

public sealed class UnconfiguredActorContextResolver : IActorContextResolver { public AuthenticatedActor? Resolve(string? authenticationMaterial) => null; }

public sealed class LocalEntitlementStore(TimeProvider timeProvider) : IEffectivePermissionSource, IEntitlementVersionSource
{
    private readonly Dictionary<string, EntitlementSnapshot> _snapshots = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    public EntitlementSnapshot? FindSnapshot(ActorId actorId, InstitutionId institutionId) { lock (_gate) return _snapshots.GetValueOrDefault(Key(actorId, institutionId)); }
    public long GetCurrentVersion(ActorId actorId, InstitutionId institutionId) { lock (_gate) return _snapshots.GetValueOrDefault(Key(actorId, institutionId))?.Version ?? 0; }
    public void Grant(ActorId actorId, InstitutionId institutionId, PermissionIdentifier permission)
    {
        lock (_gate)
        {
            string key = Key(actorId, institutionId);
            EntitlementSnapshot? existing = _snapshots.GetValueOrDefault(key);
            HashSet<PermissionGrant> grants = existing is null ? [] : new(existing.Grants);
            grants.RemoveWhere(grant => grant.Permission == permission);
            grants.Add(new PermissionGrant { Permission = permission, Scope = GrantScope.ForInstitution(institutionId) });
            _snapshots[key] = new EntitlementSnapshot { ActorId = actorId, InstitutionId = institutionId, Version = (existing?.Version ?? 0) + 1, Grants = grants, CapturedAt = timeProvider.GetUtcNow() };
        }
    }
    private static string Key(ActorId actorId, InstitutionId institutionId) => actorId.Value + "|" + institutionId.Value;
}

public sealed class EmptyEntitlementStore : IEffectivePermissionSource, IEntitlementVersionSource
{
    public EntitlementSnapshot? FindSnapshot(ActorId actorId, InstitutionId institutionId) => null;
    public long GetCurrentVersion(ActorId actorId, InstitutionId institutionId) => 0;
}

public sealed class ProtectedIntegrationOperationFilter(PermissionRequirement requirement) : IEndpointFilter
{
    public const string ActorItemKey = "authorisation.actor";
    public const string CorrelationItemKey = "authorisation.correlationId";

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        HttpContext http = context.HttpContext;
        ITenantContext tenant = http.RequestServices.GetRequiredService<ITenantContext>();
        string correlationId = Header(http, "X-Correlation-Id") ?? Guid.NewGuid().ToString("N");
        AuthenticatedActor? actor = http.RequestServices.GetRequiredService<IActorContextResolver>().Resolve(Header(http, "X-Development-Actor"));
        try
        {
            if (actor is not null) tenant.Establish(actor.InstitutionId);
            string? routeInstitution = http.Request.RouteValues["institutionId"]?.ToString();
            ResourceScope scope = string.IsNullOrWhiteSpace(routeInstitution) ? ResourceScope.None : ResourceScope.ForInstitution(InstitutionId.From(routeInstitution));
            AuthorisationDecision decision = http.RequestServices.GetRequiredService<AuthorisationPolicy>().Evaluate(new AuthorisationRequest
            {
                Requirement = requirement,
                Scope = scope,
                Actor = actor,
                StepUpSatisfied = actor?.Strength == AuthenticationStrength.StepUp,
            });
            if (!decision.IsAllowed)
                return Results.Problem(statusCode: AuthorisationCodes.StatusFor(decision.Reason), title: AuthorisationCodes.TitleFor(decision.Reason), detail: decision.Detail ?? "The request was refused.",
                    extensions: new Dictionary<string, object?> { ["code"] = AuthorisationCodes.For(decision.Reason), ["correlationId"] = correlationId });
            http.Items[ActorItemKey] = actor;
            http.Items[CorrelationItemKey] = correlationId;
            return await next(context);
        }
        finally { tenant.Clear(); }
    }

    private static string? Header(HttpContext http, string name) => string.IsNullOrWhiteSpace(http.Request.Headers[name]) ? null : http.Request.Headers[name].ToString().Trim();
}

public static class DevelopmentAuthorisationSeed
{
    public const string Institution = "dev-institution";
    public const string Actor = "dev-integration-security-admin";
    public static void Apply(IServiceProvider services)
    {
        LocalEntitlementStore store = services.GetRequiredService<LocalEntitlementStore>();
        ActorId actor = ActorId.From(Actor);
        InstitutionId institution = InstitutionId.From(Institution);
        store.Grant(actor, institution, ProviderAdministrationPermissions.CredentialReferenceManage);
        store.Grant(actor, institution, ProviderAdministrationPermissions.ApiClientManage);
    }
}
