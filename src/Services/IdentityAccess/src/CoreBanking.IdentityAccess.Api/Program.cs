using System;
using System.Collections.Generic;
using System.Threading;
using CoreBanking.BuildingBlocks.Application.Security;
using CoreBanking.BuildingBlocks.Application.Tenancy;
using CoreBanking.BuildingBlocks.Infrastructure.Messaging;
using CoreBanking.BuildingBlocks.Infrastructure.Tenancy;
using CoreBanking.IdentityAccess.Api;
using CoreBanking.IdentityAccess.Api.Authorisation;
using CoreBanking.IdentityAccess.Application.Authentication;
using CoreBanking.IdentityAccess.Application.Authorisation;
using CoreBanking.IdentityAccess.Application.Permissions;
using CoreBanking.IdentityAccess.Contracts;
using CoreBanking.IdentityAccess.Infrastructure.Authentication;
using CoreBanking.IdentityAccess.Infrastructure.Authorisation;
using CoreBanking.IdentityAccess.Infrastructure.Foundation;
using CoreBanking.IdentityAccess.Infrastructure.Identity;
using CoreBanking.IdentityAccess.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using Npgsql;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
CoreBanking.BuildingBlocks.Observability.CoreBankingObservability.AddCoreBankingObservability(
    builder.Services,
    builder.Configuration,
    builder.Environment,
    "corebanking.identity-access.api");

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<ITenantContext, AmbientTenantContext>();

// The selected provider never accepts a platform password grant. Development can use explicitly
// configured synthetic fixture references to exercise the adapter boundary without credentials.
if (builder.Environment.IsDevelopment())
{
    Dictionary<string, string> syntheticSubjects = builder.Configuration
        .GetSection("Authentication:DevelopmentSynthetic:Credentials")
        .Get<Dictionary<string, string>>() ?? new Dictionary<string, string>(StringComparer.Ordinal);
    builder.Services.AddSingleton<IAuthenticationProviderAdapter>(
        new SyntheticAuthenticationProviderAdapter(syntheticSubjects));
}
else
{
    builder.Services.AddSingleton<IAuthenticationProviderAdapter, KeycloakAuthenticationProviderAdapter>();
}

IConfigurationSection keycloakSection = builder.Configuration.GetSection("Authentication:Keycloak");
string? keycloakAuthority = keycloakSection["Authority"]?.Trim().TrimEnd('/');
string? keycloakAudience = keycloakSection["Audience"]?.Trim();
bool hasKeycloakAuthority = !string.IsNullOrWhiteSpace(keycloakAuthority);
bool hasKeycloakAudience = !string.IsNullOrWhiteSpace(keycloakAudience);
bool useKeycloak = hasKeycloakAuthority && hasKeycloakAudience;
bool requireHttpsMetadata = keycloakSection.GetValue("RequireHttpsMetadata", true);

if (hasKeycloakAuthority != hasKeycloakAudience)
{
    throw new InvalidOperationException(
        "Authentication:Keycloak:Authority and Authentication:Keycloak:Audience must be configured together.");
}

if (useKeycloak)
{
    if (!Uri.TryCreate(keycloakAuthority, UriKind.Absolute, out Uri? authorityUri))
    {
        throw new InvalidOperationException("Authentication:Keycloak:Authority must be an absolute URI.");
    }

    bool usesHttps = string.Equals(authorityUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
    bool permittedDevelopmentHttp = builder.Environment.IsDevelopment()
        && !requireHttpsMetadata
        && authorityUri.IsLoopback
        && string.Equals(authorityUri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase);

    if (!usesHttps && !permittedDevelopmentHttp)
    {
        throw new InvalidOperationException(
            "Keycloak authority must use HTTPS, except loopback Development with RequireHttpsMetadata disabled.");
    }

    if (!builder.Environment.IsDevelopment() && !requireHttpsMetadata)
    {
        throw new InvalidOperationException(
            "Keycloak discovery must use HTTPS and RequireHttpsMetadata must remain enabled outside Development.");
    }

    builder.Services
        .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(options =>
        {
            options.Authority = keycloakAuthority;
            options.Audience = keycloakAudience;
            options.RequireHttpsMetadata = requireHttpsMetadata;
            options.MapInboundClaims = false;
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateIssuerSigningKey = true,
                ValidateLifetime = true,
                RequireExpirationTime = true,
                RequireSignedTokens = true,
                ClockSkew = TimeSpan.FromSeconds(30),
            };
        });

    builder.Services.AddSingleton<IActorContextResolver, KeycloakActorContextResolver>();
}

// The relational entitlement store is used as soon as a connection string is configured. Without
// one, the replaceable in-memory stand-ins keep the reference flow runnable for a developer who
// has not started the infrastructure stack. Neither path reads a permission from the request.
string? connectionString = builder.Configuration.GetConnectionString("IdentityAccess");
bool useRelationalStore = !string.IsNullOrWhiteSpace(connectionString);

if (!useRelationalStore && !builder.Environment.IsDevelopment())
{
    throw new InvalidOperationException("ConnectionStrings:IdentityAccess is required outside Development.");
}

if (useRelationalStore)
{
    builder.Services.AddDbContext<IdentityAccessDbContext>(options => options.UseNpgsql(connectionString));
    builder.Services.AddScoped<IAuthorisationAuditWriter, PersistentAuthorisationAuditWriter>();
    builder.Services.AddScoped<RelationalEntitlementStore>();
    builder.Services.AddScoped<IEffectivePermissionSource>(provider => provider.GetRequiredService<RelationalEntitlementStore>());
    builder.Services.AddScoped<IEntitlementVersionSource>(provider => provider.GetRequiredService<RelationalEntitlementStore>());

    // The messaging primitives work against the service's own context, which is what makes the
    // reference record and its outbox row commit in one transaction.
    builder.Services.AddScoped<IIntegrationStore>(provider => provider.GetRequiredService<IdentityAccessDbContext>());
    builder.Services.AddScoped<IdempotencyService>();
    builder.Services.AddScoped<FoundationRecordService>();
    builder.Services.AddScoped<IdentityLifecycleService>();
    builder.Services.AddScoped<SessionSecurityService>();
}
else
{
    builder.Services.AddSingleton<IAuthorisationAuditWriter, InMemoryAuthorisationAuditWriter>();
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

// The synthetic header resolver is retained only for local development when Keycloak has not
// been configured. A non-development host cannot start without the real provider configuration.
if (!useKeycloak && builder.Environment.IsDevelopment())
{
    builder.Services.AddSingleton<IActorContextResolver, DevelopmentActorContextResolver>();
}
else if (!useKeycloak)
{
    throw new InvalidOperationException(
        "Authentication:Keycloak:Authority and Authentication:Keycloak:Audience are required outside Development.");
}

WebApplication app = builder.Build();

if (useKeycloak)
{
    StartupLog.KeycloakAuthenticationEnabled(app.Logger, keycloakAuthority!, keycloakAudience!);
    app.UseAuthentication();
}
else if (!app.Environment.IsDevelopment())
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
    app.MapGet("/api/v1/sessions", async (
        HttpContext http,
        SessionSecurityService service,
        CancellationToken cancellationToken) =>
    {
        if (http.Items[ProtectedOperationFilter.ActorItemKey] is not AuthenticatedActor actor)
        {
            return Results.Unauthorized();
        }
        return Results.Ok(await service.ListOwnSessionsAsync(actor.Id, cancellationToken));
    })
    .AddEndpointFilter(new ProtectedOperationFilter(PermissionRequirement.For(IdentityPermissions.IdentityView), "identity.session.list.self"));

    app.MapPost("/api/v1/sessions/{sessionId}/revoke", async (
        HttpContext http,
        string sessionId,
        SessionSecurityService service,
        CancellationToken cancellationToken) =>
    {
        if (http.Items[ProtectedOperationFilter.ActorItemKey] is not AuthenticatedActor actor)
        {
            return Results.Unauthorized();
        }
        string idempotencyKey = http.Request.Headers["Idempotency-Key"].ToString();
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Idempotency key required",
                extensions: new Dictionary<string, object?>(StringComparer.Ordinal) { ["code"] = "request.idempotency_key_required" });
        }
        try
        {
            return Results.Ok(await service.RevokeOwnSessionAsync(actor.InstitutionId, actor.Id, sessionId, idempotencyKey,
                http.Items[ProtectedOperationFilter.CorrelationItemKey] as string ?? Guid.NewGuid().ToString("N"), cancellationToken));
        }
        catch (SessionSecurityException exception)
        {
            return Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Session revocation refused",
                extensions: new Dictionary<string, object?>(StringComparer.Ordinal) { ["code"] = exception.Code });
        }
    })
    .AddEndpointFilter(new ProtectedOperationFilter(PermissionRequirement.For(IdentityPermissions.IdentityView), "identity.session.revoke.self"));

    StartupLog.RelationalEntitlementStoreInUse(app.Logger);
}

app.MapGet("/health/live", () => Results.Ok(new { status = "Healthy" }));
app.MapGet("/health/ready", async (CancellationToken cancellationToken) =>
{
    if (!useRelationalStore)
    {
        return Results.Ok(new { status = "Healthy" });
    }

    try
    {
        await using AsyncServiceScope scope = app.Services.CreateAsyncScope();
        IdentityAccessDbContext context = scope.ServiceProvider.GetRequiredService<IdentityAccessDbContext>();
        return await context.Database.CanConnectAsync(cancellationToken)
            ? Results.Ok(new { status = "Healthy" })
            : Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
    }
    catch (NpgsqlException exception)
    {
        StartupLog.IdentityAccessDatabaseReadinessFailed(app.Logger, exception);
        return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
    }
});

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

// The epic 04 reference producer: the record and its outbox row are written in one transaction,
// and the relay in the worker host publishes the event afterwards. It exists only where the
// relational store is configured, because the in-memory stand-ins have no durable outbox.
if (useRelationalStore)
{
    app.MapPost("/api/v1/identities", async (
        HttpContext http,
        RegisterIdentityV1 request,
        IdentityLifecycleService service,
        CancellationToken cancellationToken) =>
    {
        if (http.Items[ProtectedOperationFilter.ActorItemKey] is not AuthenticatedActor actor)
        {
            return Results.Unauthorized();
        }

        string? idempotencyKey = http.Request.Headers["Idempotency-Key"].ToString();
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return Results.Problem(
                detail: "An Idempotency-Key header is required for this command.",
                statusCode: StatusCodes.Status400BadRequest,
                title: "Idempotency key required",
                extensions: new Dictionary<string, object?>(StringComparer.Ordinal) { ["code"] = "request.idempotency_key_required" });
        }

        string correlationId = http.Items[ProtectedOperationFilter.CorrelationItemKey] as string ?? Guid.NewGuid().ToString("N");
        try
        {
            IdentityRegistrationResultV1 result = await service.RegisterAsync(
                actor.InstitutionId,
                actor.Id,
                idempotencyKey,
                request,
                correlationId,
                cancellationToken);
            return Results.Ok(result);
        }
        catch (IdentityLifecycleException exception)
        {
            return Results.Problem(
                detail: exception.Message,
                statusCode: StatusCodes.Status409Conflict,
                title: "Identity registration refused",
                extensions: new Dictionary<string, object?>(StringComparer.Ordinal) { ["code"] = exception.Code });
        }
        catch (ArgumentException)
        {
            return Results.Problem(
                detail: "The identity registration request is invalid.",
                statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid identity registration",
                extensions: new Dictionary<string, object?>(StringComparer.Ordinal) { ["code"] = "identity_registration_invalid" });
        }
        catch (IdempotencyConflictException exception)
        {
            return Results.Problem(
                detail: exception.Message,
                statusCode: StatusCodes.Status409Conflict,
                title: "Idempotency key reused with a different request",
                extensions: new Dictionary<string, object?>(StringComparer.Ordinal) { ["code"] = "request.idempotency_conflict" });
        }
        catch (IdempotencyInProgressException exception)
        {
            return Results.Problem(
                detail: exception.Message,
                statusCode: StatusCodes.Status409Conflict,
                title: "An equivalent request is in progress",
                extensions: new Dictionary<string, object?>(StringComparer.Ordinal) { ["code"] = "request.idempotency_in_progress" });
        }
    })
    .AddEndpointFilter(new ProtectedOperationFilter(
        new PermissionRequirement
        {
            Permission = IdentityPermissions.IdentityCreate,
            RequiresStepUp = true,
        },
        "identity.identity.create"));

    app.MapGet("/api/v1/identities/{identityId}", async (
        HttpContext http,
        string identityId,
        IdentityLifecycleService service,
        CancellationToken cancellationToken) =>
    {
        if (http.Items[ProtectedOperationFilter.ActorItemKey] is not AuthenticatedActor actor)
        {
            return Results.Unauthorized();
        }

        IdentityDetailsV1? identity = await service.FindAsync(actor.InstitutionId, identityId, cancellationToken);
        return identity is null ? Results.NotFound() : Results.Ok(identity);
    })
    .AddEndpointFilter(new ProtectedOperationFilter(
        PermissionRequirement.For(IdentityPermissions.IdentityView),
        "identity.identity.view"));

    app.MapPut("/api/v1/identities/{identityId}/status", async (
        HttpContext http,
        string identityId,
        ChangeIdentityStatusV1 request,
        IdentityLifecycleService service,
        CancellationToken cancellationToken) =>
    {
        if (http.Items[ProtectedOperationFilter.ActorItemKey] is not AuthenticatedActor actor)
        {
            return Results.Unauthorized();
        }

        string? idempotencyKey = http.Request.Headers["Idempotency-Key"].ToString();
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return Results.Problem(
                detail: "An Idempotency-Key header is required for this command.",
                statusCode: StatusCodes.Status400BadRequest,
                title: "Idempotency key required",
                extensions: new Dictionary<string, object?>(StringComparer.Ordinal) { ["code"] = "request.idempotency_key_required" });
        }

        string correlationId = http.Items[ProtectedOperationFilter.CorrelationItemKey] as string ?? Guid.NewGuid().ToString("N");
        try
        {
            IdentityStatusChangeResultV1 result = await service.ChangeStatusAsync(
                actor.InstitutionId,
                actor.Id,
                identityId,
                idempotencyKey,
                request,
                correlationId,
                cancellationToken);
            return Results.Ok(result);
        }
        catch (IdentityLifecycleException exception)
        {
            int status = string.Equals(exception.Code, "identity_not_found", StringComparison.Ordinal)
                ? StatusCodes.Status404NotFound
                : StatusCodes.Status409Conflict;
            return Results.Problem(
                detail: exception.Message,
                statusCode: status,
                title: "Identity status change refused",
                extensions: new Dictionary<string, object?>(StringComparer.Ordinal) { ["code"] = exception.Code });
        }
        catch (ArgumentException)
        {
            return Results.Problem(
                detail: "The identity status request is invalid.",
                statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid identity status request",
                extensions: new Dictionary<string, object?>(StringComparer.Ordinal) { ["code"] = "identity_status_invalid" });
        }
        catch (IdempotencyConflictException exception)
        {
            return Results.Problem(
                detail: exception.Message,
                statusCode: StatusCodes.Status409Conflict,
                title: "Idempotency key reused with a different request",
                extensions: new Dictionary<string, object?>(StringComparer.Ordinal) { ["code"] = "request.idempotency_conflict" });
        }
        catch (IdempotencyInProgressException exception)
        {
            return Results.Problem(
                detail: exception.Message,
                statusCode: StatusCodes.Status409Conflict,
                title: "An equivalent request is in progress",
                extensions: new Dictionary<string, object?>(StringComparer.Ordinal) { ["code"] = "request.idempotency_in_progress" });
        }
    })
    .AddEndpointFilter(new ProtectedOperationFilter(
        new PermissionRequirement
        {
            Permission = IdentityPermissions.IdentityUpdate,
            RequiresStepUp = true,
        },
        "identity.identity.update"));

    app.MapPost("/api/v1/identity/foundation-records", async (
        HttpContext http,
        FoundationRecordRequest request,
        FoundationRecordService service,
        CancellationToken cancellationToken) =>
    {
        if (http.Items[ProtectedOperationFilter.ActorItemKey] is not AuthenticatedActor actor)
        {
            return Results.Unauthorized();
        }

        string? idempotencyKey = http.Request.Headers["Idempotency-Key"].ToString();
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return Results.Problem(
                detail: "An Idempotency-Key header is required for this command.",
                statusCode: StatusCodes.Status400BadRequest,
                title: "Idempotency key required",
                extensions: new Dictionary<string, object?>(StringComparer.Ordinal) { ["code"] = "request.idempotency_key_required" });
        }

        string correlationId = http.Items[ProtectedOperationFilter.CorrelationItemKey] as string ?? Guid.NewGuid().ToString("N");

        try
        {
            FoundationRecordResult result = await service.CreateAsync(
                actor.InstitutionId,
                actor.Id,
                idempotencyKey,
                request.Description ?? string.Empty,
                correlationId,
                cancellationToken);

            return Results.Ok(new FoundationRecordResponse(result.FoundationRecordId, result.EventId, result.Replayed));
        }
        catch (IdempotencyConflictException exception)
        {
            return Results.Problem(
                detail: exception.Message,
                statusCode: StatusCodes.Status409Conflict,
                title: "Idempotency key reused with a different request",
                extensions: new Dictionary<string, object?>(StringComparer.Ordinal) { ["code"] = "request.idempotency_conflict" });
        }
        catch (IdempotencyInProgressException exception)
        {
            return Results.Problem(
                detail: exception.Message,
                statusCode: StatusCodes.Status409Conflict,
                title: "An equivalent request is in progress",
                extensions: new Dictionary<string, object?>(StringComparer.Ordinal) { ["code"] = "request.idempotency_in_progress" });
        }
    })
    .AddEndpointFilter(new ProtectedOperationFilter(
        PermissionRequirement.For(IdentityPermissions.PlatformFoundationExecute),
        "identity.foundation.record.create"));

    StartupLog.ReferenceProducerEnabled(app.Logger);
}

app.Run();

/// <summary>Result of the reference protected operation. Contains no sensitive data.</summary>
internal sealed record FoundationExecutionResult(
    string Decision,
    string Permission,
    string? ActorId,
    string? ActorType,
    string? InstitutionId,
    string? CorrelationId);

/// <summary>The reference command body. Contains no sensitive data.</summary>
internal sealed record FoundationRecordRequest(string? Description);

/// <summary>The reference command outcome, including whether the result was replayed.</summary>
internal sealed record FoundationRecordResponse(string FoundationRecordId, string EventId, bool Replayed);
