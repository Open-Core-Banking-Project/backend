using System;
using CoreBanking.BuildingBlocks.Application.Security;
using CoreBanking.IdentityAccess.Api;
using CoreBanking.IdentityAccess.Api.Authorisation;
using CoreBanking.IdentityAccess.Application.Authorisation;
using CoreBanking.IdentityAccess.Application.Permissions;
using CoreBanking.IdentityAccess.Infrastructure.Authorisation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton(TimeProvider.System);

builder.Services.AddSingleton<InMemoryEntitlementStore>();
builder.Services.AddSingleton<IEffectivePermissionSource>(provider => provider.GetRequiredService<InMemoryEntitlementStore>());
builder.Services.AddSingleton<IEntitlementVersionSource>(provider => provider.GetRequiredService<InMemoryEntitlementStore>());
builder.Services.AddSingleton<InMemoryAuthorisationAuditWriter>();
builder.Services.AddSingleton<IAuthorisationAuditWriter>(provider => provider.GetRequiredService<InMemoryAuthorisationAuditWriter>());
builder.Services.AddSingleton<IActorContextResolver, DevelopmentActorContextResolver>();

builder.Services.AddSingleton(provider => new AuthorisationPolicy(
    provider.GetRequiredService<IEffectivePermissionSource>(),
    provider.GetRequiredService<IEntitlementVersionSource>(),
    provider.GetRequiredService<TimeProvider>(),
    TimeSpan.FromSeconds(60)));

builder.Services.AddSingleton<AuthorisationService>();

WebApplication app = builder.Build();

// The resolver emulates a validated access token and refuses to resolve anything outside
// Development. Without it, every protected request is an unauthenticated request.
bool developmentAuthenticationEnabled = app.Environment.IsDevelopment();
if (!developmentAuthenticationEnabled)
{
    StartupLog.DevelopmentActorResolutionDisabled(app.Logger);
}

if (developmentAuthenticationEnabled)
{
    DevelopmentSeed.SeedResult seed = DevelopmentSeed.Apply(app.Services);
    StartupLog.DevelopmentIdentitiesSeeded(
        app.Logger,
        seed.InstitutionId,
        seed.AdministratorsActorId,
        seed.AdministratorsEntitlementVersion,
        seed.OperatorActorId,
        seed.OperatorEntitlementVersion);
}

// Reference protected operation. The declared permission is the only reason this endpoint is
// reachable; the policy resolves permissions from entitlements, never from the request.
app.MapPost("/api/v1/identity/foundation-executions", (HttpContext http) =>
{
    AuthenticatedActor? actor = http.Items[ProtectedOperationFilter.ActorItemKey] as AuthenticatedActor;

    return Results.Ok(new FoundationExecutionResult(
        Decision: "allowed",
        Permission: IdentityPermissions.PlatformFoundationExecute.Value,
        ActorId: actor?.Id.Value,
        ActorType: actor?.Type.ToString(),
        InstitutionId: actor?.InstitutionId.Value,
        CorrelationId: http.Items[ProtectedOperationFilter.CorrelationItemKey] as string));
})
.AddEndpointFilter(new ProtectedOperationFilter(
    app.Services.GetRequiredService<AuthorisationService>(),
    app.Services.GetRequiredService<IActorContextResolver>(),
    PermissionRequirement.For(IdentityPermissions.PlatformFoundationExecute),
    "identity.foundation.execute"));

app.Run();

/// <summary>Result of the reference protected operation. Contains no sensitive data.</summary>
internal sealed record FoundationExecutionResult(
    string Decision,
    string Permission,
    string? ActorId,
    string? ActorType,
    string? InstitutionId,
    string? CorrelationId);
