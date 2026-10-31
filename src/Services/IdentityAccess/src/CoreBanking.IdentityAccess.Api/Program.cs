using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CoreBanking.BuildingBlocks.Application.Security;
using CoreBanking.BuildingBlocks.Application.Tenancy;
using CoreBanking.BuildingBlocks.Domain.Identity;
using CoreBanking.BuildingBlocks.Infrastructure.Messaging;
using CoreBanking.BuildingBlocks.Infrastructure.Security;
using CoreBanking.BuildingBlocks.Infrastructure.Tenancy;
using CoreBanking.IdentityAccess.Api;
using CoreBanking.IdentityAccess.Api.Authorisation;
using CoreBanking.IdentityAccess.Application.Authentication;
using CoreBanking.IdentityAccess.Application.Approvals;
using CoreBanking.IdentityAccess.Application.Authorisation;
using CoreBanking.IdentityAccess.Api.Security;
using CoreBanking.IdentityAccess.Application.Devices;
using CoreBanking.IdentityAccess.Application.Security;
using CoreBanking.IdentityAccess.Infrastructure.Entitlements;
using CoreBanking.IdentityAccess.Application.Permissions;
using CoreBanking.IdentityAccess.Contracts;
using CoreBanking.IdentityAccess.Domain.Authentication;
using CoreBanking.IdentityAccess.Domain.Security;
using CoreBanking.IdentityAccess.Infrastructure.Access;
using CoreBanking.IdentityAccess.Infrastructure.Authentication;
using CoreBanking.IdentityAccess.Infrastructure.Authorisation;
using CoreBanking.IdentityAccess.Infrastructure.Foundation;
using CoreBanking.IdentityAccess.Infrastructure.Identity;
using CoreBanking.IdentityAccess.Infrastructure.Persistence;
using CoreBanking.IdentityAccess.Infrastructure.Security;
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

// Factor verification is a provider boundary. Development may use an explicitly configured
// synthetic code; every other configuration fails closed.
string? syntheticMfaCode = builder.Configuration["Authentication:Mfa:SyntheticAcceptanceCode"];
if (builder.Environment.IsDevelopment() && !string.IsNullOrWhiteSpace(syntheticMfaCode))
{
    builder.Services.AddSingleton<IMfaFactorVerifier>(new SyntheticMfaFactorVerifier(syntheticMfaCode));
}
else
{
    builder.Services.AddSingleton<IMfaFactorVerifier, UnconfiguredMfaFactorVerifier>();
}

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

// Secret material is read through one port (ADR-0011), never from a raw configuration key at the
// point of use. Development and tests resolve it from the process environment or configuration.
builder.Services.AddSingleton<ISecretProvider, EnvironmentSecretProvider>();

// Protected payload envelopes need a key-management boundary. Development uses a synthetic local
// adapter that performs the same RSA-OAEP unwrap; every other environment fails closed until a
// provider adapter is supplied. Decision: P1-E02-D13.
if (builder.Environment.IsDevelopment())
{
    builder.Services.AddSingleton<IKeyOperationPort, DevelopmentKeyOperationPort>();
}
else
{
    builder.Services.AddSingleton<IKeyOperationPort, UnconfiguredKeyOperationPort>();
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
    builder.Services.AddScoped<MfaService>();
    builder.Services.AddScoped<IStepUpEvidenceSource>(provider => provider.GetRequiredService<MfaService>());
    builder.Services.AddScoped<GrantAdministrationService>();
    builder.Services.AddScoped<RoleAdministrationService>();
    builder.Services.AddScoped<ServiceClientAdministrationService>();
    builder.Services.AddScoped<IServiceClientValidator>(provider => provider.GetRequiredService<ServiceClientAdministrationService>());
    builder.Services.AddScoped<EntitlementReconciliationService>();
builder.Services.AddSingleton(TrustDurationPolicy.Default);
builder.Services.AddScoped<ITrustedDeviceService, TrustedDeviceService>();
builder.Services.AddScoped<IPublicKeyMetadataService, PublicKeyMetadataService>();
builder.Services.AddScoped<IPayloadEnvelopeService, PayloadEnvelopeService>();
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
builder.Services.AddScoped<ApprovalEligibilityService>();
builder.Services.AddScoped<EntitlementDistributionService>();

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

if (app.Environment.IsDevelopment() && useRelationalStore)
{
    await PayloadKeyBootstrap.EnsureAsync(app.Services, builder.Configuration, app.Logger);
}

    // Entitlement distribution. The actor may read their own snapshot; a stale or regressed answer
    // is a denial, never an empty permission set. A service-authenticated variant for other actors
    // arrives with slice 13.
    app.MapPost("/api/v1/entitlements/snapshot", (
        HttpContext http,
        EntitlementSnapshotRequestV1 request,
        EntitlementDistributionService service) =>
    {
        if (http.Items[ProtectedOperationFilter.ActorItemKey] is not AuthenticatedActor actor)
        {
            return Results.Unauthorized();
        }

        EntitlementDistributionResult result = service.GetSnapshot(
            actor,
            ActorId.From(request.ActorId),
            InstitutionId.From(request.InstitutionId),
            request.MinimumVersion);

        if (!result.IsAllowed)
        {
            int status = result.DenialCode switch
            {
                AuthorisationCodes.MissingAuthentication => StatusCodes.Status401Unauthorized,
                EntitlementDistributionV1.VersionRegressed => StatusCodes.Status409Conflict,
                _ => StatusCodes.Status403Forbidden,
            };

            return Results.Problem(
                detail: "The entitlement snapshot is not available for this request.",
                statusCode: status,
                title: "Entitlement snapshot refused",
                extensions: new Dictionary<string, object?>(StringComparer.Ordinal) { ["code"] = result.DenialCode });
        }

        return Results.Ok(result.Snapshot);
    })
    .AddEndpointFilter(new ProtectedOperationFilter(
        PermissionRequirement.For(IdentityPermissions.IdentityView),
        "identity.identity.view"));

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

    // Public-key discovery (IAM-UC-07). A client needs the wrapping public key before it can authenticate, so this
    // route is anonymous: it establishes the tenant from the institution header and returns public parameters of
    // active versions only. Nothing here is secret and no private member exists to leak.
    // Decision: P1-E02-D13 item D13-F.
    app.MapGet("/api/v1/security/public-keys/{purpose}", async (
        HttpContext http,
        string purpose,
        IPublicKeyMetadataService keys,
        ITenantContext tenant,
        CancellationToken cancellationToken) =>
    {
        if (!string.Equals(purpose, KeyPurposes.PayloadWrapping, StringComparison.Ordinal))
        {
            return Results.Problem(
                detail: "No approved key purpose matches the request.",
                statusCode: StatusCodes.Status404NotFound,
                title: "Key purpose not found",
                extensions: new Dictionary<string, object?>(StringComparer.Ordinal) { ["code"] = "payload_key_purpose_unknown" });
        }

        string? institutionHeader = http.Request.Headers[ProtectedOperationFilter.InstitutionHeaderName].ToString();
        if (string.IsNullOrWhiteSpace(institutionHeader))
        {
            return Results.Problem(
                detail: "The institution header is required for public-key discovery.",
                statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid key discovery request",
                extensions: new Dictionary<string, object?>(StringComparer.Ordinal) { ["code"] = "request.institution_required" });
        }

        InstitutionId institution = InstitutionId.From(institutionHeader.Trim());
        tenant.Establish(institution);
        try
        {
            PublicKeySetView view = await keys.GetActiveKeysAsync(institution, purpose, cancellationToken);
            http.Response.Headers.CacheControl = "public, max-age=60";
            return Results.Ok(PayloadEnvelopeMapping.ToResponse(view));
        }
        finally
        {
            tenant.Clear();
        }
    })
    .AllowAnonymous();

    // Trusted devices (epic 02 slice 11). The registration completion consumes a purpose-bound step-up in the
    // service itself, so a missing or stale step-up is refused there with `device_step_up_required`.
    app.MapGet("/api/v1/identity/devices", async (
        HttpContext http,
        ITrustedDeviceService devices,
        CancellationToken cancellationToken) =>
    {
        if (http.Items[ProtectedOperationFilter.ActorItemKey] is not AuthenticatedActor actor)
        {
            return Results.Unauthorized();
        }

        return Results.Ok(await devices.ListAsync(
            new ListDevicesQuery(actor, actor.Id.Value, OtherIdentityRequested: false), cancellationToken));
    })
    .AddEndpointFilter(new ProtectedOperationFilter(
        PermissionRequirement.For(IdentityPermissions.DeviceSelfManage),
        "identity.device.manage"));

    app.MapGet("/api/v1/identity/devices/identities/{identityId}", async (
        HttpContext http,
        string identityId,
        ITrustedDeviceService devices,
        CancellationToken cancellationToken) =>
    {
        if (http.Items[ProtectedOperationFilter.ActorItemKey] is not AuthenticatedActor actor)
        {
            return Results.Unauthorized();
        }

        return Results.Ok(await devices.ListAsync(
            new ListDevicesQuery(actor, identityId, OtherIdentityRequested: true), cancellationToken));
    })
    .AddEndpointFilter(new ProtectedOperationFilter(
        PermissionRequirement.For(IdentityPermissions.DeviceView),
        "identity.device.view"));

    app.MapPost("/api/v1/identity/devices/registrations", async (
        HttpContext http,
        StartDeviceRegistrationRequestV1 request,
        ITrustedDeviceService devices,
        CancellationToken cancellationToken) =>
    {
        if (http.Items[ProtectedOperationFilter.ActorItemKey] is not AuthenticatedActor actor)
        {
            return Results.Unauthorized();
        }

        string correlationId = http.Items[ProtectedOperationFilter.CorrelationItemKey] as string ?? Guid.NewGuid().ToString("N");
        try
        {
            return Results.Ok(await devices.StartRegistrationAsync(
                new StartDeviceRegistrationCommand(
                    actor,
                    actor.Id.Value,
                    request.KeyThumbprint,
                    DeviceCodes.FromPlatformCode(request.Platform),
                    request.DisplayName,
                    correlationId),
                cancellationToken));
        }
        catch (TrustedDeviceException exception)
        {
            return Results.Problem(
                detail: exception.Message,
                statusCode: string.Equals(exception.Code, "device_registration_invalid", StringComparison.Ordinal)
                    ? StatusCodes.Status400BadRequest
                    : StatusCodes.Status409Conflict,
                title: "Device registration refused",
                extensions: new Dictionary<string, object?>(StringComparer.Ordinal) { ["code"] = exception.Code });
        }
    })
    .AddEndpointFilter(new ProtectedOperationFilter(
        PermissionRequirement.For(IdentityPermissions.DeviceSelfManage),
        "identity.device.manage"));

    app.MapPost("/api/v1/identity/devices/registrations/{deviceId}/complete", async (
        HttpContext http,
        string deviceId,
        ITrustedDeviceService devices,
        CancellationToken cancellationToken) =>
    {
        if (http.Items[ProtectedOperationFilter.ActorItemKey] is not AuthenticatedActor actor)
        {
            return Results.Unauthorized();
        }

        string correlationId = http.Items[ProtectedOperationFilter.CorrelationItemKey] as string ?? Guid.NewGuid().ToString("N");
        try
        {
            return Results.Ok(await devices.CompleteRegistrationAsync(
                new CompleteDeviceRegistrationCommand(actor, deviceId, correlationId),
                cancellationToken));
        }
        catch (TrustedDeviceException exception)
        {
            return Results.Problem(
                detail: exception.Message,
                statusCode: string.Equals(exception.Code, "device_not_found", StringComparison.Ordinal)
                    ? StatusCodes.Status404NotFound
                    : StatusCodes.Status403Forbidden,
                title: "Device trust refused",
                extensions: new Dictionary<string, object?>(StringComparer.Ordinal) { ["code"] = exception.Code });
        }
    })
    .AddEndpointFilter(new ProtectedOperationFilter(
        PermissionRequirement.For(IdentityPermissions.DeviceSelfManage),
        "identity.device.manage"));

    app.MapDelete("/api/v1/identity/devices/{deviceId}", async Task<IResult> (
        HttpContext http,
        string deviceId,
        string? reason,
        ITrustedDeviceService devices,
        CancellationToken cancellationToken) =>
    {
        if (http.Items[ProtectedOperationFilter.ActorItemKey] is not AuthenticatedActor actor)
        {
            return Results.Unauthorized();
        }

        string correlationId = http.Items[ProtectedOperationFilter.CorrelationItemKey] as string ?? Guid.NewGuid().ToString("N");
        return await RevokeDeviceAsync(devices, actor, deviceId, reason, administrative: false, correlationId, cancellationToken);
    })
    .AddEndpointFilter(new ProtectedOperationFilter(
        PermissionRequirement.For(IdentityPermissions.DeviceSelfManage),
        "identity.device.manage"));

    app.MapDelete("/api/v1/identity/devices/{deviceId}/administrative", async Task<IResult> (
        HttpContext http,
        string deviceId,
        string? reason,
        ITrustedDeviceService devices,
        CancellationToken cancellationToken) =>
    {
        if (http.Items[ProtectedOperationFilter.ActorItemKey] is not AuthenticatedActor actor)
        {
            return Results.Unauthorized();
        }

        string correlationId = http.Items[ProtectedOperationFilter.CorrelationItemKey] as string ?? Guid.NewGuid().ToString("N");
        return await RevokeDeviceAsync(devices, actor, deviceId, reason, administrative: true, correlationId, cancellationToken);
    })
    .AddEndpointFilter(new ProtectedOperationFilter(
        PermissionRequirement.For(IdentityPermissions.DeviceRevoke),
        "identity.device.revoke"));

    StartupLog.RelationalEntitlementStoreInUse(app.Logger);

    static async Task<IResult> RevokeDeviceAsync(
        ITrustedDeviceService devices,
        AuthenticatedActor actor,
        string deviceId,
        string? reason,
        bool administrative,
        string correlationId,
        CancellationToken cancellationToken)
    {
        if (administrative && string.IsNullOrWhiteSpace(reason))
        {
            return Results.Problem(
                detail: "An administrative device revocation requires a reason.",
                statusCode: StatusCodes.Status400BadRequest,
                title: "Device revocation refused",
                extensions: new Dictionary<string, object?>(StringComparer.Ordinal) { ["code"] = "device_revocation_reason_required" });
        }

        try
        {
            return Results.Ok(await devices.RevokeAsync(
                new RevokeDeviceCommand(actor, deviceId, reason ?? string.Empty, administrative, correlationId),
                cancellationToken));
        }
        catch (TrustedDeviceException exception)
        {
            return Results.Problem(
                detail: exception.Message,
                statusCode: string.Equals(exception.Code, "device_not_found", StringComparison.Ordinal)
                    ? StatusCodes.Status404NotFound
                    : StatusCodes.Status409Conflict,
                title: "Device revocation refused",
                extensions: new Dictionary<string, object?>(StringComparer.Ordinal) { ["code"] = exception.Code });
        }
    }

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

// Approver eligibility. An approval source asks whether the authenticated actor may decide a
// request another actor raised, and receives a stable answer instead of duplicating authorisation
// rules. Step-up stays a decision-time requirement: this query answers permission, tenant,
// freshness and segregation questions only.
app.MapPost("/api/v1/approvals/eligibility", async (
    HttpContext http,
    ApprovalEligibilityRequestV1 request,
    ApprovalEligibilityService service,
    CancellationToken cancellationToken) =>
{
    if (http.Items[ProtectedOperationFilter.ActorItemKey] is not AuthenticatedActor actor)
    {
        return Results.Unauthorized();
    }

    if (string.IsNullOrWhiteSpace(request.InstitutionId)
        || string.IsNullOrWhiteSpace(request.SourceSystem)
        || string.IsNullOrWhiteSpace(request.SourceRequestId)
        || string.IsNullOrWhiteSpace(request.MakerActorId))
    {
        return Results.Problem(
            detail: "InstitutionId, SourceSystem, SourceRequestId and MakerActorId are required.",
            statusCode: StatusCodes.Status400BadRequest,
            title: "Invalid eligibility request",
            extensions: new Dictionary<string, object?>(StringComparer.Ordinal) { ["code"] = "request.invalid_source_reference" });
    }

    PermissionIdentifier requiredPermission;
    try
    {
        requiredPermission = PermissionIdentifier.Parse(request.RequiredPermission);
    }
    catch (ArgumentException)
    {
        return Results.Problem(
            detail: "RequiredPermission must be a stable capability in domain.resource.action form.",
            statusCode: StatusCodes.Status400BadRequest,
            title: "Invalid eligibility request",
            extensions: new Dictionary<string, object?>(StringComparer.Ordinal) { ["code"] = "request.invalid_permission" });
    }

    string correlationId = http.Items[ProtectedOperationFilter.CorrelationItemKey] as string ?? Guid.NewGuid().ToString("N");

    ApprovalEligibilityResult result = await service.EvaluateAsync(
        requiredPermission,
        request.MakerActorId,
        InstitutionId.From(request.InstitutionId),
        actor,
        correlationId,
        cancellationToken);

    return Results.Ok(new ApprovalEligibilityResponseV1
    {
        Outcome = result.IsEligible ? ApprovalEligibilityOutcomeV1.Eligible : ApprovalEligibilityOutcomeV1.Ineligible,
        Code = result.IsEligible ? ApprovalEligibilityCodeV1.Eligible : AuthorisationCodes.For(result.Reason),
        RequiredPermission = result.Permission,
        SourceSystem = request.SourceSystem,
        SourceRequestId = request.SourceRequestId,
        MakerSeparated = result.MakerSeparated,
        EvaluatedEntitlementVersion = result.EvaluatedEntitlementVersion,
        EvaluatedAt = result.EvaluatedAt,
    });
})
.AddEndpointFilter(new ProtectedOperationFilter(
    PermissionRequirement.For(IdentityPermissions.ApprovalEligibilityReview),
    "identity.approval.eligibility.review"));

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

    // Approved factor enrolment (P1-E02-D08). Only the factor families named by the decision are
    // accepted, and only TOTP is available; everything else is refused with a stable code.
    app.MapPost("/api/v1/mfa/enrolments", async (
        HttpContext http,
        MfaEnrolmentRequestV1 request,
        MfaService service,
        CancellationToken cancellationToken) =>
    {
        if (http.Items[ProtectedOperationFilter.ActorItemKey] is not AuthenticatedActor actor)
        {
            return Results.Unauthorized();
        }

        MfaFactorKind? factorKind = request.FactorKind?.Trim().ToLowerInvariant() switch
        {
            MfaContractV1.Totp => MfaFactorKind.Totp,
            MfaContractV1.Passkey => MfaFactorKind.Passkey,
            MfaContractV1.Sms => MfaFactorKind.Sms,
            MfaContractV1.Push => MfaFactorKind.Push,
            _ => null,
        };

        if (factorKind is null)
        {
            return Results.Problem(
                detail: "FactorKind must be totp, passkey, sms or push.",
                statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid enrolment request",
                extensions: new Dictionary<string, object?>(StringComparer.Ordinal) { ["code"] = MfaContractV1.FactorInvalid });
        }

        try
        {
            MfaEnrolmentResultV1 result = await service.EnrolAsync(
                actor.InstitutionId,
                actor.Id,
                factorKind.Value,
                request.ProviderReference,
                cancellationToken);
            return Results.Ok(result);
        }
        catch (MfaException exception)
        {
            return Results.Problem(
                detail: exception.Message,
                statusCode: StatusCodes.Status409Conflict,
                title: "Enrolment refused",
                extensions: new Dictionary<string, object?>(StringComparer.Ordinal) { ["code"] = exception.Code });
        }
    })
    .AddEndpointFilter(new ProtectedOperationFilter(
        PermissionRequirement.For(IdentityPermissions.MfaManage),
        "identity.mfa.manage"));

    // Step-up challenge. Success writes purpose-bound, single-use evidence for five minutes; the
    // protected operation then consumes it exactly once.
    app.MapPost("/api/v1/mfa/step-up", async (
        HttpContext http,
        StepUpChallengeRequestV1 request,
        MfaService service,
        IPayloadEnvelopeService envelopes,
        CancellationToken cancellationToken) =>
    {
        if (http.Items[ProtectedOperationFilter.ActorItemKey] is not AuthenticatedActor actor)
        {
            return Results.Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request.Purpose) || request.Purpose.Length > 150
            || string.IsNullOrWhiteSpace(actor.SessionId))
        {
            return Results.Problem(
                detail: "A purpose, a factor response and a session-bound actor are required.",
                statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid step-up request",
                extensions: new Dictionary<string, object?>(StringComparer.Ordinal) { ["code"] = MfaContractV1.FactorInvalid });
        }

        string correlationId = http.Items[ProtectedOperationFilter.CorrelationItemKey] as string ?? Guid.NewGuid().ToString("N");

        // The factor response may arrive in a protected payload envelope. Identity decrypts it through the
        // KMS/HSM boundary, uses it once and never persists, logs or echoes it. Decision: P1-E02-D12 item D12-H.
        string? factorResponse = request.Response;
        if (request.ProtectedResponse is not null)
        {
            if (!string.IsNullOrWhiteSpace(request.Response))
            {
                return Results.Problem(
                    detail: "Supply either a clear-text factor response or a protected one, not both.",
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Invalid step-up request",
                    extensions: new Dictionary<string, object?>(StringComparer.Ordinal) { ["code"] = MfaContractV1.FactorInvalid });
            }

            try
            {
                byte[] unprotected = await envelopes.UnprotectAsync(
                    new PayloadEnvelopeRequest(
                        actor.InstitutionId,
                        actor.Id.Value,
                        actor.SessionId,
                        correlationId,
                        PayloadEnvelopeMapping.ToApplication(request.ProtectedResponse)),
                    PayloadFieldBindings.StepUpResponse,
                    cancellationToken);
                try
                {
                    factorResponse = Encoding.UTF8.GetString(unprotected);
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(unprotected);
                }
            }
            catch (PayloadEnvelopeException exception)
            {
                return Results.Problem(
                    detail: exception.Message,
                    statusCode: string.Equals(exception.Code, PayloadEnvelopeCodes.DecryptionUnavailable, StringComparison.Ordinal)
                        ? StatusCodes.Status503ServiceUnavailable
                        : StatusCodes.Status400BadRequest,
                    title: "Protected payload refused",
                    extensions: new Dictionary<string, object?>(StringComparer.Ordinal)
                    {
                        ["code"] = exception.Code,
                        ["correlationId"] = correlationId,
                    });
            }
        }

        if (string.IsNullOrWhiteSpace(factorResponse))
        {
            return Results.Problem(
                detail: "A purpose, a factor response and a session-bound actor are required.",
                statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid step-up request",
                extensions: new Dictionary<string, object?>(StringComparer.Ordinal) { ["code"] = MfaContractV1.FactorInvalid });
        }

        try
        {
            StepUpChallengeResultV1 result = await service.VerifyStepUpAsync(
                actor.InstitutionId,
                actor.Id,
                actor.SessionId,
                actor.DeviceId,
                request.Purpose,
                factorResponse,
                correlationId,
                cancellationToken);
            return Results.Ok(result);
        }
        catch (MfaException exception)
        {
            return Results.Problem(
                detail: exception.Message,
                statusCode: StatusCodes.Status409Conflict,
                title: "Step-up refused",
                extensions: new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["code"] = exception.Code,
                    ["correlationId"] = correlationId,
                });
        }
    })
    .AddEndpointFilter(new ProtectedOperationFilter(
        PermissionRequirement.For(IdentityPermissions.MfaManage),
        "identity.mfa.manage"));

    // Grant administration. Every mutation advances the entitlement version and writes the
    // invalidation event in the same transaction. Access changes are privileged, so they require
    // step-up, which the protected filter satisfies from fresh purpose-bound evidence.
    app.MapPost("/api/v1/grants/roles", async (
        HttpContext http,
        RoleAssignmentRequestV1 request,
        GrantAdministrationService service,
        CancellationToken cancellationToken) =>
    {
        if (http.Items[ProtectedOperationFilter.ActorItemKey] is not AuthenticatedActor actor)
        {
            return Results.Unauthorized();
        }

        string? correlationId = http.Items[ProtectedOperationFilter.CorrelationItemKey] as string;
        try
        {
            GrantResultV1 result = await service.AssignRoleAsync(
                actor.InstitutionId,
                actor.Id,
                request.IdentityId,
                request.RoleId,
                request.RegionId,
                request.BranchId,
                request.ChannelId,
                request.ValidTo,
                correlationId ?? Guid.NewGuid().ToString("N"),
                cancellationToken);
            return Results.Ok(result);
        }
        catch (GrantAdministrationException exception)
        {
            return Results.Problem(
                detail: exception.Message,
                statusCode: StatusCodes.Status409Conflict,
                title: "Grant refused",
                extensions: new Dictionary<string, object?>(StringComparer.Ordinal) { ["code"] = exception.Code });
        }
    })
    .AddEndpointFilter(new ProtectedOperationFilter(
        new PermissionRequirement
        {
            Permission = IdentityPermissions.IdentityPermissionAssign,
            RequiresStepUp = true,
        },
        "identity.permission.assign"));

    app.MapPost("/api/v1/grants/temporary", async (
        HttpContext http,
        TemporaryGrantRequestV1 request,
        GrantAdministrationService service,
        CancellationToken cancellationToken) =>
    {
        if (http.Items[ProtectedOperationFilter.ActorItemKey] is not AuthenticatedActor actor)
        {
            return Results.Unauthorized();
        }

        string? correlationId = http.Items[ProtectedOperationFilter.CorrelationItemKey] as string;
        try
        {
            GrantResultV1 result = await service.GrantTemporaryPermissionAsync(
                actor.InstitutionId,
                actor.Id,
                request.IdentityId,
                request.PermissionCode,
                request.RegionId,
                request.BranchId,
                request.ValidTo,
                request.Reason,
                correlationId ?? Guid.NewGuid().ToString("N"),
                cancellationToken);
            return Results.Ok(result);
        }
        catch (GrantAdministrationException exception)
        {
            return Results.Problem(
                detail: exception.Message,
                statusCode: StatusCodes.Status409Conflict,
                title: "Grant refused",
                extensions: new Dictionary<string, object?>(StringComparer.Ordinal) { ["code"] = exception.Code });
        }
    })
    .AddEndpointFilter(new ProtectedOperationFilter(
        new PermissionRequirement
        {
            Permission = IdentityPermissions.IdentityPermissionAssign,
            RequiresStepUp = true,
        },
        "identity.permission.assign"));

    app.MapPost("/api/v1/grants/roles/revoke", async (
        HttpContext http,
        GrantRevocationRequestV1 request,
        GrantAdministrationService service,
        CancellationToken cancellationToken) =>
    {
        if (http.Items[ProtectedOperationFilter.ActorItemKey] is not AuthenticatedActor actor)
        {
            return Results.Unauthorized();
        }

        string? correlationId = http.Items[ProtectedOperationFilter.CorrelationItemKey] as string;
        try
        {
            long version = await service.RevokeRoleAsync(
                actor.InstitutionId,
                actor.Id,
                request.IdentityId,
                request.GrantId,
                correlationId ?? Guid.NewGuid().ToString("N"),
                cancellationToken);
            return Results.Ok(new { entitlementVersion = version });
        }
        catch (GrantAdministrationException exception)
        {
            return Results.Problem(
                detail: exception.Message,
                statusCode: StatusCodes.Status409Conflict,
                title: "Revocation refused",
                extensions: new Dictionary<string, object?>(StringComparer.Ordinal) { ["code"] = exception.Code });
        }
    })
    .AddEndpointFilter(new ProtectedOperationFilter(
        new PermissionRequirement
        {
            Permission = IdentityPermissions.IdentityPermissionAssign,
            RequiresStepUp = true,
        },
        "identity.permission.assign"));

    app.MapPost("/api/v1/grants/temporary/revoke", async (
        HttpContext http,
        GrantRevocationRequestV1 request,
        GrantAdministrationService service,
        CancellationToken cancellationToken) =>
    {
        if (http.Items[ProtectedOperationFilter.ActorItemKey] is not AuthenticatedActor actor)
        {
            return Results.Unauthorized();
        }

        string? correlationId = http.Items[ProtectedOperationFilter.CorrelationItemKey] as string;
        try
        {
            long version = await service.RevokeTemporaryGrantAsync(
                actor.InstitutionId,
                actor.Id,
                request.IdentityId,
                request.GrantId,
                correlationId ?? Guid.NewGuid().ToString("N"),
                cancellationToken);
            return Results.Ok(new { entitlementVersion = version });
        }
        catch (GrantAdministrationException exception)
        {
            return Results.Problem(
                detail: exception.Message,
                statusCode: StatusCodes.Status409Conflict,
                title: "Revocation refused",
                extensions: new Dictionary<string, object?>(StringComparer.Ordinal) { ["code"] = exception.Code });
        }
    })
    .AddEndpointFilter(new ProtectedOperationFilter(
        new PermissionRequirement
        {
            Permission = IdentityPermissions.IdentityPermissionAssign,
            RequiresStepUp = true,
        },
        "identity.permission.assign"));

    // Role drafting. A draft is inert: grants accept only active roles, so nothing here grants
    // access before publication. Simulation answers catalogue and conflict questions up front.
    app.MapPost("/api/v1/roles", async (
        HttpContext http,
        RoleDraftRequestV1 request,
        RoleAdministrationService service,
        CancellationToken cancellationToken) =>
    {
        if (http.Items[ProtectedOperationFilter.ActorItemKey] is not AuthenticatedActor actor)
        {
            return Results.Unauthorized();
        }

        try
        {
            RoleDraftResultV1 result = await service.CreateDraftAsync(
                actor.InstitutionId, actor.Id, request.RoleCode, request.RoleName, request.Permissions, cancellationToken);
            return Results.Ok(result);
        }
        catch (RoleAdministrationException exception)
        {
            return Results.Problem(
                detail: exception.Message,
                statusCode: StatusCodes.Status409Conflict,
                title: "Role draft refused",
                extensions: new Dictionary<string, object?>(StringComparer.Ordinal) { ["code"] = exception.Code });
        }
    })
    .AddEndpointFilter(new ProtectedOperationFilter(
        new PermissionRequirement
        {
            Permission = IdentityPermissions.IdentityRoleCreate,
            RequiresStepUp = true,
        },
        "identity.role.create"));

    app.MapPut("/api/v1/roles/{roleId}", async (
        HttpContext http,
        string roleId,
        RoleUpdateRequestV1 request,
        RoleAdministrationService service,
        CancellationToken cancellationToken) =>
    {
        if (http.Items[ProtectedOperationFilter.ActorItemKey] is not AuthenticatedActor actor)
        {
            return Results.Unauthorized();
        }

        try
        {
            RoleDraftResultV1 result = await service.UpdateDraftAsync(
                actor.InstitutionId, roleId, request.RoleName, request.Permissions, cancellationToken);
            return Results.Ok(result);
        }
        catch (RoleAdministrationException exception)
        {
            return Results.Problem(
                detail: exception.Message,
                statusCode: StatusCodes.Status409Conflict,
                title: "Role draft refused",
                extensions: new Dictionary<string, object?>(StringComparer.Ordinal) { ["code"] = exception.Code });
        }
    })
    .AddEndpointFilter(new ProtectedOperationFilter(
        new PermissionRequirement
        {
            Permission = IdentityPermissions.IdentityRoleCreate,
            RequiresStepUp = true,
        },
        "identity.role.create"));

    app.MapGet("/api/v1/roles/{roleId}", async (
        HttpContext http,
        string roleId,
        RoleAdministrationService service,
        CancellationToken cancellationToken) =>
    {
        if (http.Items[ProtectedOperationFilter.ActorItemKey] is not AuthenticatedActor actor)
        {
            return Results.Unauthorized();
        }

        try
        {
            return Results.Ok(await service.GetAsync(actor.InstitutionId, roleId, cancellationToken));
        }
        catch (RoleAdministrationException exception)
        {
            return Results.Problem(
                detail: exception.Message,
                statusCode: StatusCodes.Status404NotFound,
                title: "Role not found",
                extensions: new Dictionary<string, object?>(StringComparer.Ordinal) { ["code"] = exception.Code });
        }
    })
    .AddEndpointFilter(new ProtectedOperationFilter(
        new PermissionRequirement
        {
            Permission = IdentityPermissions.IdentityRoleCreate,
            RequiresStepUp = true,
        },
        "identity.role.create"));

    app.MapPost("/api/v1/roles/simulate", async (
        HttpContext http,
        RoleSimulationRequestV1 request,
        RoleAdministrationService service,
        CancellationToken cancellationToken) =>
    {
        if (http.Items[ProtectedOperationFilter.ActorItemKey] is not AuthenticatedActor)
        {
            return Results.Unauthorized();
        }

        return Results.Ok(await service.SimulateAsync(request.Permissions, cancellationToken));
    })
    .AddEndpointFilter(new ProtectedOperationFilter(
        new PermissionRequirement
        {
            Permission = IdentityPermissions.IdentityRoleCreate,
            RequiresStepUp = true,
        },
        "identity.role.create"));

    // Service client administration. Only opaque credential references are stored; rotation keeps
    // the previous reference with the provider-side overlap, and revocation is explicit.
    app.MapPost("/api/v1/service-clients", async (
        HttpContext http,
        ServiceClientRegistrationRequestV1 request,
        ServiceClientAdministrationService service,
        CancellationToken cancellationToken) =>
    {
        if (http.Items[ProtectedOperationFilter.ActorItemKey] is not AuthenticatedActor actor)
        {
            return Results.Unauthorized();
        }

        try
        {
            return Results.Ok(await service.RegisterAsync(
                actor.InstitutionId,
                actor.Id,
                request.ClientName,
                request.Audience,
                request.CredentialReference,
                request.CredentialExpiresAt,
                cancellationToken));
        }
        catch (ServiceClientAdministrationException exception)
        {
            return Results.Problem(
                detail: exception.Message,
                statusCode: StatusCodes.Status409Conflict,
                title: "Service client refused",
                extensions: new Dictionary<string, object?>(StringComparer.Ordinal) { ["code"] = exception.Code });
        }
    })
    .AddEndpointFilter(new ProtectedOperationFilter(
        new PermissionRequirement
        {
            Permission = IdentityPermissions.IdentityPermissionAssign,
            RequiresStepUp = true,
        },
        "identity.permission.assign"));

    app.MapGet("/api/v1/service-clients", async (
        HttpContext http,
        ServiceClientAdministrationService service,
        CancellationToken cancellationToken) =>
    {
        if (http.Items[ProtectedOperationFilter.ActorItemKey] is not AuthenticatedActor)
        {
            return Results.Unauthorized();
        }

        return Results.Ok(await service.ListAsync(cancellationToken));
    })
    .AddEndpointFilter(new ProtectedOperationFilter(
        new PermissionRequirement
        {
            Permission = IdentityPermissions.IdentityPermissionAssign,
            RequiresStepUp = true,
        },
        "identity.permission.assign"));

    app.MapPost("/api/v1/service-clients/{serviceClientId}/rotate", async (
        HttpContext http,
        string serviceClientId,
        ServiceClientRotationRequestV1 request,
        ServiceClientAdministrationService service,
        CancellationToken cancellationToken) =>
    {
        if (http.Items[ProtectedOperationFilter.ActorItemKey] is not AuthenticatedActor actor)
        {
            return Results.Unauthorized();
        }

        try
        {
            return Results.Ok(await service.RotateAsync(
                actor.InstitutionId,
                serviceClientId,
                request.CredentialReference,
                request.CredentialExpiresAt,
                request.PreviousValidUntil,
                cancellationToken));
        }
        catch (ServiceClientAdministrationException exception)
        {
            return Results.Problem(
                detail: exception.Message,
                statusCode: StatusCodes.Status409Conflict,
                title: "Rotation refused",
                extensions: new Dictionary<string, object?>(StringComparer.Ordinal) { ["code"] = exception.Code });
        }
    })
    .AddEndpointFilter(new ProtectedOperationFilter(
        new PermissionRequirement
        {
            Permission = IdentityPermissions.IdentityPermissionAssign,
            RequiresStepUp = true,
        },
        "identity.permission.assign"));

    app.MapPost("/api/v1/service-clients/{serviceClientId}/revoke", async (
        HttpContext http,
        string serviceClientId,
        ServiceClientRevocationRequestV1 request,
        ServiceClientAdministrationService service,
        CancellationToken cancellationToken) =>
    {
        if (http.Items[ProtectedOperationFilter.ActorItemKey] is not AuthenticatedActor actor)
        {
            return Results.Unauthorized();
        }

        try
        {
            return Results.Ok(await service.RevokeAsync(
                actor.InstitutionId, serviceClientId, request.Reason, cancellationToken));
        }
        catch (ServiceClientAdministrationException exception)
        {
            return Results.Problem(
                detail: exception.Message,
                statusCode: StatusCodes.Status409Conflict,
                title: "Revocation refused",
                extensions: new Dictionary<string, object?>(StringComparer.Ordinal) { ["code"] = exception.Code });
        }
    })
    .AddEndpointFilter(new ProtectedOperationFilter(
        new PermissionRequirement
        {
            Permission = IdentityPermissions.IdentityPermissionAssign,
            RequiresStepUp = true,
        },
        "identity.permission.assign"));

    // Operator-triggered reconciliation. The same pass runs on a schedule in the worker; this
    // endpoint lets an operator confirm state after an incident or a rotation.
    app.MapPost("/api/v1/reconciliation/entitlements", async (
        HttpContext http,
        EntitlementReconciliationService service,
        TimeProvider timeProvider,
        CancellationToken cancellationToken) =>
    {
        if (http.Items[ProtectedOperationFilter.ActorItemKey] is not AuthenticatedActor)
        {
            return Results.Unauthorized();
        }

        return Results.Ok(await service.ReconcileAsync(timeProvider.GetUtcNow(), cancellationToken));
    })
    .AddEndpointFilter(new ProtectedOperationFilter(
        new PermissionRequirement
        {
            Permission = IdentityPermissions.IdentityAccessReview,
            RequiresStepUp = true,
        },
        "identity.access.review"));

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
