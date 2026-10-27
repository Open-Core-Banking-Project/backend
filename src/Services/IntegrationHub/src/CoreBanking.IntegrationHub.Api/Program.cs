using System;
using System.Threading;
using CoreBanking.BuildingBlocks.Application.Security;
using CoreBanking.BuildingBlocks.Application.Tenancy;
using CoreBanking.BuildingBlocks.Domain.Identity;
using CoreBanking.BuildingBlocks.Infrastructure.Tenancy;
using CoreBanking.IntegrationHub.Api.Authorisation;
using CoreBanking.IntegrationHub.Application.ProviderAdministration;
using CoreBanking.IntegrationHub.Contracts;
using CoreBanking.IntegrationHub.Domain.ProviderAdministration;
using CoreBanking.IntegrationHub.Infrastructure.Persistence;
using CoreBanking.IntegrationHub.Infrastructure.ProviderAdministration;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
CoreBanking.BuildingBlocks.Observability.CoreBankingObservability.AddCoreBankingObservability(
    builder.Services, builder.Configuration, builder.Environment, "corebanking.integration-hub.api");
builder.Services.AddSingleton<ITenantContext, AmbientTenantContext>();
builder.Services.AddSingleton(TimeProvider.System);
if (builder.Environment.IsDevelopment())
{
    builder.Services.AddSingleton<LocalEntitlementStore>();
    builder.Services.AddSingleton<IEffectivePermissionSource>(services => services.GetRequiredService<LocalEntitlementStore>());
    builder.Services.AddSingleton<IEntitlementVersionSource>(services => services.GetRequiredService<LocalEntitlementStore>());
    builder.Services.AddSingleton<IActorContextResolver, DevelopmentActorContextResolver>();
}
else
{
    builder.Services.AddSingleton<EmptyEntitlementStore>();
    builder.Services.AddSingleton<IEffectivePermissionSource>(services => services.GetRequiredService<EmptyEntitlementStore>());
    builder.Services.AddSingleton<IEntitlementVersionSource>(services => services.GetRequiredService<EmptyEntitlementStore>());
    builder.Services.AddSingleton<IActorContextResolver, UnconfiguredActorContextResolver>();
}
builder.Services.AddScoped(services => new AuthorisationPolicy(
    services.GetRequiredService<IEffectivePermissionSource>(),
    services.GetRequiredService<IEntitlementVersionSource>(),
    services.GetRequiredService<TimeProvider>(),
    TimeSpan.FromSeconds(60)));

string? connectionString = builder.Configuration.GetConnectionString("IntegrationHub");
if (!string.IsNullOrWhiteSpace(connectionString))
{
    builder.Services.AddDbContext<IntegrationHubDbContext>(options => options.UseNpgsql(connectionString));
    builder.Services.AddScoped<IProviderAdministrationService, ProviderAdministrationService>();
}

WebApplication app = builder.Build();
if (app.Environment.IsDevelopment()) DevelopmentAuthorisationSeed.Apply(app.Services);

app.MapGet("/health/live", () => Results.Ok(new { status = "Healthy" }));
app.MapGet("/health/ready", async (IServiceProvider services, CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(connectionString)) return Results.Problem(statusCode: 503, title: "Database configuration unavailable");
    await using AsyncServiceScope scope = services.CreateAsyncScope();
    bool ready = await scope.ServiceProvider.GetRequiredService<IntegrationHubDbContext>().Database.CanConnectAsync(cancellationToken);
    return ready ? Results.Ok(new { status = "Healthy" }) : Results.Problem(statusCode: 503, title: "Database unavailable");
});

if (!string.IsNullOrWhiteSpace(connectionString)) MapProviderAdministration(app);
app.Run();

static void MapProviderAdministration(WebApplication app)
{
    app.MapPost("/api/v1/institutions/{institutionId}/integration/credential-references", async (
        HttpContext http, string institutionId, RegisterCredentialReferenceRequestV1 request, IProviderAdministrationService service, CancellationToken token) =>
    {
        AuthenticatedActor actor = RequireActor(http);
        CredentialReferenceResult result = await service.RegisterCredentialReferenceAsync(new(
            institutionId, request.Environment, request.ProviderCode, Enum.Parse<CredentialReferenceKind>(request.Kind.ToString()), request.ReferenceName, request.Owner, request.ExpiresAt,
            actor.Id.Value, request.Reason, Correlation(http)), token);
        return Results.Created($"/api/v1/institutions/{institutionId}/integration/credential-references/{result.Id}", result);
    }).AddEndpointFilter(new ProtectedIntegrationOperationFilter(ProviderAdministrationPermissions.CredentialReferenceManageRequirement));

    app.MapPost("/api/v1/institutions/{institutionId}/integration/credential-references/{credentialReferenceId}/rotations", async (
        HttpContext http, string institutionId, string credentialReferenceId, RotateCredentialReferenceRequestV1 request, IProviderAdministrationService service, CancellationToken token) =>
    {
        AuthenticatedActor actor = RequireActor(http);
        return Results.Ok(await service.RotateCredentialReferenceAsync(new(institutionId, credentialReferenceId, request.ReferenceName, request.ExpiresAt,
            actor.Id.Value, request.Reason, Correlation(http)), token));
    }).AddEndpointFilter(new ProtectedIntegrationOperationFilter(ProviderAdministrationPermissions.CredentialReferenceManageRequirement));

    app.MapPost("/api/v1/institutions/{institutionId}/integration/api-clients", async (
        HttpContext http, string institutionId, RegisterApiClientRequestV1 request, IProviderAdministrationService service, CancellationToken token) =>
    {
        AuthenticatedActor actor = RequireActor(http);
        ApiClientResult result = await service.RegisterApiClientAsync(new(institutionId, request.Environment, request.ClientIdentifier, request.DisplayName,
            request.AllowedScopes, request.AllowedNetworkRanges, actor.Id.Value, request.Reason, Correlation(http)), token);
        return Results.Created($"/api/v1/institutions/{institutionId}/integration/api-clients/{result.Id}", result);
    }).AddEndpointFilter(new ProtectedIntegrationOperationFilter(ProviderAdministrationPermissions.ApiClientManageRequirement));

    app.MapPost("/api/v1/institutions/{institutionId}/integration/api-clients/{apiClientId}/revoke", async (
        HttpContext http, string institutionId, string apiClientId, RevokeApiClientRequestV1 request, IProviderAdministrationService service, CancellationToken token) =>
    {
        AuthenticatedActor actor = RequireActor(http);
        return Results.Ok(await service.RevokeApiClientAsync(new(institutionId, apiClientId, actor.Id.Value, request.Reason, Correlation(http)), token));
    }).AddEndpointFilter(new ProtectedIntegrationOperationFilter(ProviderAdministrationPermissions.ApiClientManageRequirement));
}

static AuthenticatedActor RequireActor(HttpContext http) =>
    http.Items[ProtectedIntegrationOperationFilter.ActorItemKey] as AuthenticatedActor ?? throw new InvalidOperationException("authenticated_actor_missing");

static string Correlation(HttpContext http) =>
    http.Items[ProtectedIntegrationOperationFilter.CorrelationItemKey] as string ?? Guid.NewGuid().ToString("N");

public partial class Program;
