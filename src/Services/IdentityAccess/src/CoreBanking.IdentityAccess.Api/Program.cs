using System;
using CoreBanking.BuildingBlocks.Application.Security;
using CoreBanking.BuildingBlocks.Application.Tenancy;
using CoreBanking.BuildingBlocks.Infrastructure.Tenancy;
using CoreBanking.IdentityAccess.Api;
using CoreBanking.IdentityAccess.Api.Authorisation;
using CoreBanking.IdentityAccess.Application.Authorisation;
using CoreBanking.IdentityAccess.Application.Permissions;
using CoreBanking.IdentityAccess.Infrastructure.Authorisation;
using CoreBanking.IdentityAccess.Infrastructure.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<ITenantContext, AmbientTenantContext>();
builder.Services.AddSingleton<IAuthorisationAuditWriter, InMemoryAuthorisationAuditWriter>();

// The relational entitlement store is used as soon as a connection string is configured. Without
// one, the replaceable in-memory stand-ins keep the reference flow runnable for a developer who
// has not started the infrastructure stack. Neither path reads a permission from the request.
string? connectionString = builder.Configuration.GetConnectionString("IdentityAccess");
bool useRelationalStore = !string.IsNullOrWhiteSpace(connectionString);

if (useRelationalStore)
{
    builder.Services.AddDbContext<IdentityAccessDbContext>(options => options.UseNpgsql(connectionString));
    builder.Services.AddScoped<RelationalEntitlementStore>();
    builder.Services.AddScoped<IEffectivePermissionSource>(provider => provider.GetRequiredService<RelationalEntitlementStore>());
    builder.Services.AddScoped<IEntitlementVersionSource>(provider => provider.GetRequiredService<RelationalEntitlementStore>());
}
else
{
    builder.Services.AddSingleton<InMemoryEntitlementStore>();
    builder.Services.AddSingleton<IEffectivePermissionSource>(provider => provider.GetRequiredService<InMemoryEntitlementStore>());
    builder.Services.AddSingleton<IEntitlementVersionSource>(provider => provider.GetRequiredService<InMemoryEntitlementStore>());
}

builder.Services.AddScoped(provider => new AuthorisationPolicy(
    provider.GetRequiredService<IEffectivePermissionSource>(),
    provider.GetRequiredService<IEntitlementVersionSource>(),
    provider.GetRequiredService<TimeProvider>(),
    TimeSpan.FromSeconds(60)));

builder.Services.AddScoped<AuthorisationService>();

// The resolver emulates a validated access token and refuses to resolve anything outside
// Development. Without it, every protected request is an unauthenticated request.
if (builder.Environment.IsDevelopment())
{
    builder.Services.AddSingleton<IActorContextResolver, DevelopmentActorContextResolver>();
}
else
{
    builder.Services.AddSingleton<IActorContextResolver, UnconfiguredActorContextResolver>();
}

WebApplication app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    StartupLog.DevelopmentActorResolutionDisabled(app.Logger);
}

if (app.Environment.IsDevelopment() && !useRelationalStore)
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

if (useRelationalStore)
{
    StartupLog.RelationalEntitlementStoreInUse(app.Logger);
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
