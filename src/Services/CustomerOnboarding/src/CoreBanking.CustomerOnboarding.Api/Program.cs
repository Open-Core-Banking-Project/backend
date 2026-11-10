using System;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using CoreBanking.CustomerOnboarding.Application.Customers;
using CoreBanking.CustomerOnboarding.Application.Onboarding;
using CoreBanking.CustomerOnboarding.Contracts;
using CoreBanking.CustomerOnboarding.Infrastructure.Customers;
using CoreBanking.CustomerOnboarding.Infrastructure.Persistence;
using CoreBanking.CustomerOnboarding.Infrastructure.Protection;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
CoreBanking.BuildingBlocks.Observability.CoreBankingObservability.AddCoreBankingObservability(
    builder.Services,
    builder.Configuration,
    builder.Environment,
    "corebanking.customer-onboarding.api");

builder.Services.AddSingleton(TimeProvider.System);

string? connectionString = builder.Configuration.GetConnectionString("CustomerOnboarding");
bool useRelationalStore = !string.IsNullOrWhiteSpace(connectionString);
if (!useRelationalStore && !builder.Environment.IsDevelopment())
{
    throw new InvalidOperationException("ConnectionStrings:CustomerOnboarding is required outside Development.");
}

if (useRelationalStore)
{
    builder.Services.AddDbContext<CustomerOnboardingDbContext>(options => options.UseNpgsql(connectionString));
    builder.Services.AddSingleton(ProtectedValueProtectionOptions.Create(
        builder.Configuration["Privacy:Protection:EncryptionKey"],
        builder.Configuration["Privacy:Protection:LookupKey"]));
    builder.Services.AddSingleton<IProtectedValueProtector, AesGcmProtectedValueProtector>();
    builder.Services.AddScoped<ICustomerEligibilityFactsQuery, CustomerEligibilityFactsQuery>();
}
else
{
    builder.Services.AddSingleton<ICustomerEligibilityFactsQuery, UnavailableCustomerEligibilityFactsQuery>();
}

// Only allow-listed service clients may read authoritative facts. The list is deployment data; an empty
// list denies every call, so a missing configuration can never widen access. Decision: P1-E04-D17.
string[] allowedServiceClients = builder.Configuration
    .GetSection("CustomerFacts:AllowedServiceClientIds")
    .GetChildren()
    .Select(section => section.Value?.Trim())
    .Where(value => !string.IsNullOrWhiteSpace(value))
    .Select(value => value!)
    .ToArray();

IConfigurationSection keycloakSection = builder.Configuration.GetSection("Authentication:Keycloak");
string? keycloakAuthority = keycloakSection["Authority"]?.Trim().TrimEnd('/');
string? keycloakAudience = keycloakSection["Audience"]?.Trim();
bool useKeycloak = !string.IsNullOrWhiteSpace(keycloakAuthority) && !string.IsNullOrWhiteSpace(keycloakAudience);

if (useKeycloak)
{
    bool requireHttpsMetadata = keycloakSection.GetValue("RequireHttpsMetadata", true);
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
}

WebApplication app = builder.Build();
IWebHostEnvironment environment = app.Environment;

if (useKeycloak)
{
    app.UseAuthentication();
}

app.MapGet("/health/live", () => Results.Ok(new { status = "Healthy" }));
app.MapGet("/health/ready", () => Results.Ok(new { status = "Healthy" }));

// Authoritative customer facts for product and account decisions. The institution comes from the
// validated caller context, never from the path or body. Decision: P1-E04-D17.
app.MapGet(
    CustomerFactsContractV1.RoutePattern,
    async (
        string customerId,
        DateTimeOffset? asOf,
        HttpContext http,
        ICustomerEligibilityFactsQuery query,
        TimeProvider timeProvider,
        CancellationToken cancellationToken) =>
    {
        if (!useKeycloak)
        {
            return Results.Json(
                new CustomerFactsErrorV1("service_authentication_unconfigured"),
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        ServiceCallDecision decision = ServiceCallAuthorisation.Authorise(
            ReadCaller(http, environment),
            allowedServiceClients);
        if (!decision.IsAllowed)
        {
            int status = decision.Code == "service_authentication_required"
                ? StatusCodes.Status401Unauthorized
                : StatusCodes.Status403Forbidden;
            return Results.Json(new CustomerFactsErrorV1(decision.Code!), statusCode: status);
        }

        DateTimeOffset requestedAt = asOf ?? timeProvider.GetUtcNow();
        try
        {
            CustomerEligibilityFacts? facts = await query.GetAsync(
                decision.InstitutionId!,
                customerId,
                requestedAt,
                cancellationToken).ConfigureAwait(false);
            if (facts is null)
            {
                return Results.Json(
                    new CustomerFactsErrorV1("customer_facts_not_found"),
                    statusCode: StatusCodes.Status404NotFound);
            }

            return Results.Ok(new CustomerEligibilityFactsV1
            {
                InstitutionId = facts.InstitutionId,
                CustomerId = facts.CustomerId,
                CustomerStatus = facts.CustomerStatus,
                EffectiveClassificationVersion = facts.EffectiveClassificationVersion,
                KycTier = facts.KycTier,
                Segment = facts.Segment,
                RiskClass = facts.RiskClass,
                RequiresEnhancedDueDiligence = facts.RequiresEnhancedDueDiligence,
                CustomerType = facts.CustomerType,
                AgeYears = facts.AgeYears,
                FactsVersion = facts.FactsVersion,
                AsOf = facts.AsOf,
                ValidUntil = facts.ValidUntil,
            });
        }
        catch (CustomerEligibilityFactsException exception)
        {
            return Results.Json(
                new CustomerFactsErrorV1(exception.Code),
                statusCode: StatusCodes.Status409Conflict);
        }
    });

app.Run();

static ServiceCallerClaims? ReadCaller(HttpContext http, IWebHostEnvironment environment)
{
    if (http.User?.Identity?.IsAuthenticated == true)
    {
        return new ServiceCallerClaims(
            http.User.FindFirst("actor_type")?.Value,
            http.User.FindFirst("azp")?.Value ?? http.User.FindFirst("client_id")?.Value,
            http.User.FindFirst("institution_id")?.Value,
            http.User.FindAll("aud").Select(claim => claim.Value).ToArray(),
            SplitScope(http.User.FindFirst("scope")?.Value));
    }

    // Development-only synthetic service identity. It is never consulted outside Development, and it
    // still has to satisfy the allow-list, audience and capability checks.
    if (environment.IsDevelopment())
    {
        string? clientId = http.Request.Headers["X-Development-Service-Client"].FirstOrDefault()?.Trim();
        if (!string.IsNullOrWhiteSpace(clientId))
        {
            return new ServiceCallerClaims(
                ServiceCallAuthorisation.ServiceActorType,
                clientId,
                http.Request.Headers["X-Development-Institution-Id"].FirstOrDefault()?.Trim(),
                [CustomerFactsContractV1.Audience],
                SplitScope(http.Request.Headers["X-Development-Service-Scope"].FirstOrDefault()));
        }
    }

    return null;
}

static string[] SplitScope(string? scope) =>
    string.IsNullOrWhiteSpace(scope)
        ? []
        : scope.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
