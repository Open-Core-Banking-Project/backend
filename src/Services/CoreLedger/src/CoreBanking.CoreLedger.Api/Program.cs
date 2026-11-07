using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CoreBanking.BuildingBlocks.Application.Security;
using CoreBanking.BuildingBlocks.Application.Tenancy;
using CoreBanking.BuildingBlocks.Domain.Identity;
using CoreBanking.BuildingBlocks.Infrastructure.Messaging;
using CoreBanking.BuildingBlocks.Infrastructure.Security;
using CoreBanking.BuildingBlocks.Infrastructure.Tenancy;
using CoreBanking.CoreLedger.Api.Authorisation;
using CoreBanking.CoreLedger.Application.Accounts;
using CoreBanking.CoreLedger.Application.Institutions;
using CoreBanking.CoreLedger.Application.Products;
using CoreBanking.CoreLedger.Application.Provisioning;
using CoreBanking.CoreLedger.Contracts;
using CoreBanking.CoreLedger.Domain.Accounts;
using CoreBanking.CoreLedger.Domain.Institutions;
using CoreBanking.CoreLedger.Domain.Products;
using CoreBanking.CoreLedger.Infrastructure.Accounts;
using CoreBanking.CoreLedger.Infrastructure.Institutions;
using CoreBanking.CoreLedger.Infrastructure.Persistence;
using CoreBanking.CoreLedger.Infrastructure.Products;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
Microsoft.Extensions.Configuration.IConfigurationSection provisioningSection =
    builder.Configuration.GetSection(TenantProvisioningPlanOptions.SectionName);
string? configuredPlanVersion = provisioningSection["PlanVersion"];
if (!int.TryParse(configuredPlanVersion, NumberStyles.None, CultureInfo.InvariantCulture, out int planVersion))
{
    throw new InvalidOperationException("Institution provisioning plan version must be a positive integer.");
}

string[] requiredServices = provisioningSection
    .GetSection("RequiredServices")
    .GetChildren()
    .Select(service => service.Value
        ?? throw new InvalidOperationException("Every required provisioning service must have an identifier."))
    .ToArray();
TenantProvisioningPlanOptions provisioningPlan = new()
{
    PlanVersion = planVersion,
    RequiredServices = requiredServices,
};
provisioningPlan.Validate();
builder.Services.AddSingleton(provisioningPlan);
builder.Services.AddSingleton<ITenantContext, AmbientTenantContext>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(ProductAccountMetrics.Shared);
builder.Services.AddSingleton<IReferenceFactTelemetry, ProductAccountTelemetry>();
RegisterWorkloadIdentity(builder.Services, builder.Configuration);

// Authoritative customer facts are read from their owner. Without a configured endpoint the source
// rejects every lookup, so an eligibility decision fails closed. Decision: P1-E04-D17.
string? customerFactsBaseUrl = builder.Configuration["CustomerFacts:BaseUrl"];
if (!string.IsNullOrWhiteSpace(customerFactsBaseUrl)
    && Uri.TryCreate(customerFactsBaseUrl, UriKind.Absolute, out Uri? customerFactsAddress))
{
    builder.Services.AddHttpClient<ICustomerEligibilityFactsSource, RemoteCustomerEligibilityFactsSource>(client =>
    {
        client.BaseAddress = customerFactsAddress;
        client.Timeout = TimeSpan.FromSeconds(2);
    }).AddHttpMessageHandler<WorkloadBearerTokenHandler>();
}
else
{
    builder.Services.AddSingleton<ICustomerEligibilityFactsSource, UnconfiguredCustomerEligibilityFactsSource>();
}

if (builder.Environment.IsDevelopment())
{
    builder.Services.AddSingleton<LocalEntitlementStore>();
    builder.Services.AddSingleton<IEffectivePermissionSource>(provider => provider.GetRequiredService<LocalEntitlementStore>());
    builder.Services.AddSingleton<IEntitlementVersionSource>(provider => provider.GetRequiredService<LocalEntitlementStore>());
    builder.Services.AddSingleton<IActorContextResolver, DevelopmentActorContextResolver>();
}
else
{
    builder.Services.AddHttpClient<IEntitlementSnapshotProvider, IdentityEntitlementSnapshotProvider>(client =>
    {
        string? baseUrl = builder.Configuration["IdentityAccess:BaseUrl"];
        if (Uri.TryCreate(baseUrl, UriKind.Absolute, out Uri? address)) client.BaseAddress = address;
        client.Timeout = TimeSpan.FromSeconds(5);
    }).AddHttpMessageHandler<WorkloadBearerTokenHandler>();
    builder.Services.AddScoped<RefreshingEntitlementStore>();
    builder.Services.AddScoped<IEffectivePermissionSource>(provider => provider.GetRequiredService<RefreshingEntitlementStore>());
    builder.Services.AddScoped<IEntitlementVersionSource>(provider => provider.GetRequiredService<RefreshingEntitlementStore>());
    builder.Services.AddSingleton<IActorContextResolver, UnconfiguredActorContextResolver>();
}

static void RegisterWorkloadIdentity(IServiceCollection services, IConfiguration configuration)
{
    IConfigurationSection section = configuration.GetSection(ClientCredentialsTokenOptions.SectionName);
    string? endpointValue = section["TokenEndpoint"]?.Trim();
    string? clientId = section["ClientId"]?.Trim();
    string? secretReference = section["ClientSecretReference"]?.Trim();
    bool anyConfigured = !string.IsNullOrWhiteSpace(endpointValue)
        || !string.IsNullOrWhiteSpace(clientId)
        || !string.IsNullOrWhiteSpace(secretReference);

    services.AddTransient<WorkloadBearerTokenHandler>();
    if (!anyConfigured)
    {
        services.AddSingleton<IWorkloadAccessTokenSource, UnconfiguredWorkloadAccessTokenSource>();
        return;
    }

    if (!Uri.TryCreate(endpointValue, UriKind.Absolute, out Uri? endpoint)
        || string.IsNullOrWhiteSpace(clientId)
        || string.IsNullOrWhiteSpace(secretReference))
    {
        throw new InvalidOperationException(
            $"{ClientCredentialsTokenOptions.SectionName} requires TokenEndpoint, ClientId and ClientSecretReference together.");
    }

    ClientCredentialsTokenOptions options = new()
    {
        TokenEndpoint = endpoint,
        ClientId = clientId,
        ClientSecretReference = secretReference,
    };
    options.Validate();
    services.AddSingleton(options);
    services.AddSingleton<ISecretProvider, EnvironmentSecretProvider>();
    services.AddHttpClient<IWorkloadAccessTokenSource, ClientCredentialsAccessTokenSource>(client =>
        client.Timeout = TimeSpan.FromSeconds(5));
}

builder.Services.AddScoped(provider => new AuthorisationPolicy(
    provider.GetRequiredService<IEffectivePermissionSource>(),
    provider.GetRequiredService<IEntitlementVersionSource>(),
    provider.GetRequiredService<TimeProvider>(),
    TimeSpan.FromSeconds(60)));

string? connectionString = builder.Configuration.GetConnectionString("CoreLedger");
bool useRelationalStore = !string.IsNullOrWhiteSpace(connectionString);
if (!useRelationalStore && !builder.Environment.IsDevelopment())
{
    throw new InvalidOperationException("ConnectionStrings:CoreLedger is required outside Development.");
}

if (useRelationalStore)
{
    builder.Services.AddDbContext<CoreLedgerDbContext>(options => options.UseNpgsql(connectionString));
    builder.Services.AddScoped<IIntegrationStore>(provider => provider.GetRequiredService<CoreLedgerDbContext>());
    builder.Services.AddScoped<IdempotencyService>();
    builder.Services.AddScoped<IInstitutionCreationService, InstitutionCreationService>();
    builder.Services.AddScoped<IInstitutionActivationService, InstitutionActivationService>();
    builder.Services.AddScoped<IInstitutionSuspensionService, InstitutionSuspensionService>();
    builder.Services.AddScoped<IInstitutionProvisioningRepairService, InstitutionProvisioningRepairService>();
    builder.Services.AddScoped<IOrganisationLifecycleService, OrganisationLifecycleService>();
    builder.Services.AddScoped<IBusinessCalendarService, BusinessCalendarService>();
    builder.Services.AddScoped<ISavingsProductConfigurationService, SavingsProductConfigurationService>();
    builder.Services.AddScoped<IInstitutionCurrencyConfigurationService, InstitutionCurrencyConfigurationService>();
    builder.Services.AddScoped<IConfigurationReconciliationService, ConfigurationReconciliationService>();
    builder.Services.AddScoped<IBranchReferenceSource, BranchReferenceSource>();
    builder.Services.AddScoped<ICurrencyReferenceSource, CurrencyReferenceSource>();
    builder.Services.AddScoped<AuthoritativeEligibilityFactsResolver>();
    builder.Services.AddScoped<ISavingsEligibilityEvaluationService, SavingsEligibilityEvaluationService>();
    builder.Services.AddScoped<IInstitutionConfigurationAreaLifecycle, InstitutionConfigurationAreaLifecycle>();
    builder.Services.AddScoped<IAccountNumberingConfigurationService, AccountNumberingConfigurationService>();
    builder.Services.AddScoped<IAccountNumberSchemeSource, AccountNumberSchemeSource>();
    builder.Services.AddScoped<IAccountNumberAllocator, AccountNumberAllocator>();
    builder.Services.AddScoped<IAccountOpeningService, AccountOpeningService>();
    builder.Services.AddScoped<IAccountNumberRevealService, AccountNumberRevealService>();
    builder.Services.AddScoped<IAccountOpeningPolicyConfigurationService, AccountOpeningPolicyConfigurationService>();
    builder.Services.AddScoped<IAccountOpeningPolicySource, AccountOpeningPolicySource>();
    builder.Services.AddScoped<IAccountHoldingsReader, AccountHoldingsReader>();
    builder.Services.AddScoped<IAccountApplicationService, AccountApplicationService>();
}

CoreBanking.BuildingBlocks.Observability.CoreBankingObservability.AddCoreBankingObservability(
    builder.Services,
    builder.Configuration,
    builder.Environment,
    "corebanking.core-ledger.api");
WebApplication app = builder.Build();

if (useRelationalStore)
{
    InstitutionOperationalGauges.Register(
        app.Services.GetRequiredService<IServiceScopeFactory>(),
        app.Services.GetRequiredService<TimeProvider>());
    ProductAccountGauges.Register(
        app.Services.GetRequiredService<IServiceScopeFactory>(),
        app.Services.GetRequiredService<TimeProvider>());
}

if (app.Environment.IsDevelopment())
{
    DevelopmentAuthorisationSeed.Apply(app.Services);
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
        CoreLedgerDbContext context = scope.ServiceProvider.GetRequiredService<CoreLedgerDbContext>();
        return await context.Database.CanConnectAsync(cancellationToken)
            ? Results.Ok(new { status = "Healthy" })
            : Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
    }
    catch (DbException)
    {
        return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
    }
});

if (useRelationalStore)
{
    app.MapPost("/api/v1/institutions", async (
        HttpContext http,
        CreateInstitutionRequestV1 request,
        IInstitutionCreationService service,
        CancellationToken cancellationToken) =>
    {
        if (http.Items[ProtectedInstitutionOperationFilter.ActorItemKey] is not AuthenticatedActor actor)
        {
            return Results.Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request.InstitutionId)
            || string.IsNullOrWhiteSpace(request.Code)
            || string.IsNullOrWhiteSpace(request.LegalName)
            || string.IsNullOrWhiteSpace(request.CountryCode)
            || string.IsNullOrWhiteSpace(request.BaseCurrency))
        {
            return Problem(
                "request.invalid",
                "Invalid institution request",
                "Institution ID, code, legal name, country code and base currency are required.",
                StatusCodes.Status400BadRequest);
        }

        InstitutionId target = InstitutionId.From(request.InstitutionId);
        if (target != actor.InstitutionId)
        {
            return Problem(
                AuthorisationCodes.WrongInstitution,
                "Not permitted in this institution",
                "The request institution does not match the authenticated context.",
                StatusCodes.Status403Forbidden);
        }

        string? idempotencyKey = RequiredHeader(http, "Idempotency-Key");
        if (idempotencyKey is null)
        {
            return IdempotencyKeyRequired();
        }

        string correlationId = CorrelationId(http);
        try
        {
            CreateInstitutionResult result = await service.CreateAsync(
                new CreateInstitutionCommand(
                    target,
                    actor.Id,
                    idempotencyKey,
                    request.Code,
                    request.LegalName,
                    request.CountryCode,
                    request.BaseCurrency,
                    correlationId),
                cancellationToken);
            return Results.Accepted(
                $"/api/v1/institutions/{result.InstitutionId}",
                new CreateInstitutionResponseV1(
                    result.InstitutionId,
                    result.ProvisioningRunId,
                    result.EventId,
                    result.Replayed,
                    "provisioning"));
        }
        catch (InstitutionCreationConflictException exception)
        {
            return Problem(exception.Code, "Institution creation conflict", exception.Message, StatusCodes.Status409Conflict);
        }
        catch (InstitutionCreationInProgressException exception)
        {
            return Problem(
                "request.idempotency_in_progress",
                "Institution creation in progress",
                exception.Message,
                StatusCodes.Status409Conflict);
        }
        catch (ArgumentException exception)
        {
            return Problem("request.invalid", "Invalid institution request", exception.Message, StatusCodes.Status400BadRequest);
        }
    })
    .AddEndpointFilter(new ProtectedInstitutionOperationFilter(
        InstitutionPermissions.CreateRequirement,
        "institution.create"));

    app.MapPost("/api/v1/institutions/{institutionId}/activate", async (
        HttpContext http,
        string institutionId,
        IInstitutionActivationService service,
        CancellationToken cancellationToken) =>
    {
        if (http.Items[ProtectedInstitutionOperationFilter.ActorItemKey] is not AuthenticatedActor actor)
        {
            return Results.Unauthorized();
        }

        string? idempotencyKey = RequiredHeader(http, "Idempotency-Key");
        if (idempotencyKey is null)
        {
            return IdempotencyKeyRequired();
        }

        try
        {
            ActivateInstitutionResult result = await service.ActivateAsync(
                new ActivateInstitutionCommand(
                    InstitutionId.From(institutionId),
                    actor.Id,
                    idempotencyKey,
                    CorrelationId(http)),
                cancellationToken);
            return Results.Ok(new ActivateInstitutionResponseV1(
                result.InstitutionId,
                result.ProvisioningRunId,
                result.EventId,
                result.ActivatedAt,
                result.Replayed,
                "active"));
        }
        catch (InstitutionActivationRejectedException exception)
        {
            int status = string.Equals(exception.Code, "cross_tenant_access_denied", StringComparison.Ordinal)
                ? StatusCodes.Status403Forbidden
                : StatusCodes.Status409Conflict;
            return Problem(exception.Code, "Institution activation rejected", exception.Message, status);
        }
        catch (InstitutionActivationInProgressException exception)
        {
            return Problem(
                "request.idempotency_in_progress",
                "Institution activation in progress",
                exception.Message,
                StatusCodes.Status409Conflict);
        }
        catch (ArgumentException exception)
        {
            return Problem("request.invalid", "Invalid activation request", exception.Message, StatusCodes.Status400BadRequest);
        }
    })
    .AddEndpointFilter(new ProtectedInstitutionOperationFilter(
        InstitutionPermissions.ActivateRequirement,
        "institution.activate"));

    app.MapPost("/api/v1/institutions/{institutionId}/suspend", async (
        HttpContext http,
        string institutionId,
        SuspendInstitutionRequestV1 request,
        IInstitutionSuspensionService service,
        CancellationToken cancellationToken) =>
    {
        if (http.Items[ProtectedInstitutionOperationFilter.ActorItemKey] is not AuthenticatedActor actor)
        {
            return Results.Unauthorized();
        }

        string? key = RequiredHeader(http, "Idempotency-Key");
        if (key is null) return IdempotencyKeyRequired();
        try
        {
            SuspendInstitutionResult result = await service.SuspendAsync(
                new SuspendInstitutionCommand(InstitutionId.From(institutionId), actor.Id, key, request.Reason, CorrelationId(http)), cancellationToken);
            return Results.Ok(new SuspendInstitutionResponseV1(result.InstitutionId, result.EventId, result.SuspendedAt, result.Replayed, "suspended"));
        }
        catch (InstitutionSuspensionRejectedException exception)
        {
            int status = exception.Code == "cross_tenant_access_denied" ? StatusCodes.Status403Forbidden : StatusCodes.Status409Conflict;
            return Problem(exception.Code, "Institution suspension rejected", exception.Message, status);
        }
        catch (InstitutionSuspensionInProgressException exception)
        {
            return Problem("request.idempotency_in_progress", "Institution suspension in progress", exception.Message, StatusCodes.Status409Conflict);
        }
        catch (ArgumentException exception)
        {
            return Problem("request.invalid", "Invalid suspension request", exception.Message, StatusCodes.Status400BadRequest);
        }
    })
    .AddEndpointFilter(new ProtectedInstitutionOperationFilter(InstitutionPermissions.SuspendRequirement, "institution.suspend"));

    MapOrganisationLifecycleEndpoints(app);
    MapBusinessCalendarEndpoints(app);
    MapSavingsProductEndpoints(app);
    MapAccountEndpoints(app);
    MapInstitutionProvisioningRepairEndpoints(app);
}

app.Run();

static string CorrelationId(HttpContext http) =>
    http.Items[ProtectedInstitutionOperationFilter.CorrelationItemKey] as string ?? Guid.NewGuid().ToString("N");

static string? RequiredHeader(HttpContext http, string name)
{
    string value = http.Request.Headers[name].ToString();
    return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

static IResult IdempotencyKeyRequired() => Problem(
    "request.idempotency_key_required",
    "Idempotency key required",
    "An Idempotency-Key header is required for this command.",
    StatusCodes.Status400BadRequest);

static IResult Problem(string code, string title, string detail, int status) =>
    Results.Problem(
        detail: detail,
        statusCode: status,
        title: title,
        extensions: new Dictionary<string, object?>(StringComparer.Ordinal) { ["code"] = code });

static void MapInstitutionProvisioningRepairEndpoints(WebApplication app)
{
    app.MapGet("/api/v1/institutions/{institutionId}/provisioning-runs/{provisioningRunId}", async (
        string institutionId,
        string provisioningRunId,
        IInstitutionProvisioningRepairService service,
        CancellationToken cancellationToken) =>
    {
        try
        {
            InstitutionProvisioningStatusResult result = await service.GetStatusAsync(
                InstitutionId.From(institutionId),
                provisioningRunId,
                cancellationToken);
            return Results.Ok(new InstitutionProvisioningStatusResponseV1(
                result.InstitutionId,
                result.ProvisioningRunId,
                result.PlanVersion,
                result.Status,
                result.StartedAt,
                result.UpdatedAt,
                result.Steps.Select(step => new InstitutionProvisioningStepStatusV1(
                    step.ServiceId,
                    step.Status,
                    step.FailureCode,
                    step.UpdatedAt)).ToArray()));
        }
        catch (InstitutionProvisioningRepairRejectedException exception)
        {
            int status = exception.Code == "cross_tenant_access_denied"
                ? StatusCodes.Status403Forbidden
                : StatusCodes.Status404NotFound;
            return Problem(exception.Code, "Provisioning status unavailable", exception.Message, status);
        }
        catch (ArgumentException exception)
        {
            return Problem("request.invalid", "Invalid provisioning status request", exception.Message, StatusCodes.Status400BadRequest);
        }
    })
    .AddEndpointFilter(new ProtectedInstitutionOperationFilter(
        InstitutionPermissions.ProvisioningRepairRequirement,
        "institution.provisioning.status.view"));

    app.MapPost("/api/v1/institutions/{institutionId}/provisioning-runs/{provisioningRunId}/repair", async (
        HttpContext http,
        string institutionId,
        string provisioningRunId,
        RepairInstitutionProvisioningRequestV1 request,
        IInstitutionProvisioningRepairService service,
        CancellationToken cancellationToken) =>
    {
        string? key = RequiredHeader(http, "Idempotency-Key");
        if (key is null) return IdempotencyKeyRequired();
        try
        {
            RepairInstitutionProvisioningResult result = await service.RepairAsync(
                new RepairInstitutionProvisioningCommand(
                    InstitutionId.From(institutionId),
                    Actor(http).Id,
                    key,
                    provisioningRunId,
                    request.Reason,
                    CorrelationId(http)),
                cancellationToken);
            return Results.Accepted(value: new RepairInstitutionProvisioningResponseV1(
                result.InstitutionId,
                result.ProvisioningRunId,
                result.SourceEventId,
                result.AuditEventId,
                result.RequestedAt,
                result.Replayed,
                "replay_queued"));
        }
        catch (InstitutionProvisioningRepairRejectedException exception)
        {
            int status = exception.Code switch
            {
                "cross_tenant_access_denied" => StatusCodes.Status403Forbidden,
                "provisioning_run_not_found" => StatusCodes.Status404NotFound,
                _ => StatusCodes.Status409Conflict,
            };
            return Problem(exception.Code, "Provisioning repair rejected", exception.Message, status);
        }
        catch (InstitutionProvisioningRepairInProgressException exception)
        {
            return Problem(
                "request.idempotency_in_progress",
                "Provisioning repair in progress",
                exception.Message,
                StatusCodes.Status409Conflict);
        }
        catch (ArgumentException exception)
        {
            return Problem("request.invalid", "Invalid provisioning repair", exception.Message, StatusCodes.Status400BadRequest);
        }
    })
    .AddEndpointFilter(new ProtectedInstitutionOperationFilter(
        InstitutionPermissions.ProvisioningRepairRequirement,
        "institution.provisioning.repair"));
}

static void MapSavingsProductEndpoints(WebApplication app)
{
    // Institution currency configuration lifecycle (area institution.currencies).
    // Decision: P1-E04-D17.
    app.MapPost("/api/v1/institutions/{institutionId}/configurations/institution.currencies/versions", async (
        HttpContext http,
        string institutionId,
        CreateCurrencyConfigurationRequestV1 request,
        IInstitutionCurrencyConfigurationService service,
        CancellationToken cancellationToken) =>
    {
        string? key = RequiredHeader(http, "Idempotency-Key");
        if (key is null) return IdempotencyKeyRequired();
        try
        {
            CurrencyDraftResult result = await service.CreateDraftAsync(
                new CreateCurrencyDraftCommand(
                    InstitutionId.From(institutionId),
                    Actor(http).Id,
                    key,
                    request.VersionId,
                    request.BaseCurrency,
                    request.SupportedCurrencies,
                    CorrelationId(http)),
                cancellationToken);
            return Results.Ok(result);
        }
        catch (InstitutionCurrencyConfigurationRejectedException exception)
        {
            return Problem(exception.Code, "Currency configuration rejected", exception.Message, StatusCodes.Status409Conflict);
        }
        catch (ArgumentException exception)
        {
            return Problem("request.invalid", "Invalid currency configuration", exception.Message, StatusCodes.Status400BadRequest);
        }
    })
    .AddEndpointFilter(new ProtectedInstitutionOperationFilter(
        InstitutionPermissions.ConfigurationCreateRequirement,
        "institution.configuration.create"));

    app.MapPost("/api/v1/configurations/{versionId}/simulate", async (
        HttpContext http,
        string versionId,
        SimulateCurrencyConfigurationRequestV1 request,
        IInstitutionCurrencyConfigurationService service,
        CancellationToken cancellationToken) =>
    {
        string? key = RequiredHeader(http, "Idempotency-Key");
        if (key is null) return IdempotencyKeyRequired();
        try
        {
            CurrencySimulationResult result = await service.SimulateAsync(
                new SimulateCurrencyVersionCommand(
                    Actor(http).InstitutionId,
                    Actor(http).Id,
                    key,
                    versionId,
                    request.SimulationId,
                    request.ContentHash,
                    CorrelationId(http)),
                cancellationToken);
            return Results.Ok(result);
        }
        catch (InstitutionCurrencyConfigurationRejectedException exception)
        {
            return Problem(exception.Code, "Currency simulation rejected", exception.Message, StatusCodes.Status409Conflict);
        }
        catch (ArgumentException exception)
        {
            return Problem("request.invalid", "Invalid currency simulation", exception.Message, StatusCodes.Status400BadRequest);
        }
    })
    .AddEndpointFilter(new ProtectedInstitutionOperationFilter(
        InstitutionPermissions.ConfigurationSimulateRequirement,
        "institution.configuration.simulate"));

    app.MapPost("/api/v1/configurations/{versionId}/submit", async (
        HttpContext http,
        string versionId,
        SubmitCurrencyConfigurationRequestV1 request,
        IInstitutionCurrencyConfigurationService service,
        CancellationToken cancellationToken) =>
    {
        string? key = RequiredHeader(http, "Idempotency-Key");
        if (key is null) return IdempotencyKeyRequired();
        try
        {
            CurrencySubmissionResult result = await service.SubmitForApprovalAsync(
                new SubmitCurrencyVersionCommand(
                    Actor(http).InstitutionId,
                    Actor(http).Id,
                    key,
                    request.RequestId,
                    request.SourceRequestKey,
                    versionId,
                    request.ContentHash,
                    CorrelationId(http)),
                cancellationToken);
            return Results.Ok(result);
        }
        catch (InstitutionCurrencyConfigurationRejectedException exception)
        {
            return Problem(exception.Code, "Currency submission rejected", exception.Message, StatusCodes.Status409Conflict);
        }
        catch (ArgumentException exception)
        {
            return Problem("request.invalid", "Invalid currency submission", exception.Message, StatusCodes.Status400BadRequest);
        }
    })
    .AddEndpointFilter(new ProtectedInstitutionOperationFilter(
        InstitutionPermissions.ConfigurationSubmitRequirement,
        "institution.configuration.submit"));

    app.MapPost("/api/v1/configurations/{versionId}/publish", async (
        HttpContext http,
        string versionId,
        PublishCurrencyConfigurationRequestV1 request,
        IInstitutionCurrencyConfigurationService service,
        CancellationToken cancellationToken) =>
    {
        string? key = RequiredHeader(http, "Idempotency-Key");
        if (key is null) return IdempotencyKeyRequired();
        try
        {
            CurrencyPublicationResult result = await service.PublishAsync(
                new PublishCurrencyVersionCommand(
                    Actor(http).InstitutionId,
                    Actor(http).Id,
                    key,
                    request.PublicationId,
                    versionId,
                    request.ContentHash,
                    request.EffectiveFrom,
                    request.EffectiveTo,
                    CorrelationId(http)),
                cancellationToken);
            return Results.Ok(result);
        }
        catch (InstitutionCurrencyConfigurationRejectedException exception)
        {
            return Problem(exception.Code, "Currency publication rejected", exception.Message, StatusCodes.Status409Conflict);
        }
        catch (ArgumentException exception)
        {
            return Problem("request.invalid", "Invalid currency publication", exception.Message, StatusCodes.Status400BadRequest);
        }
    })
    .AddEndpointFilter(new ProtectedInstitutionOperationFilter(
        InstitutionPermissions.ConfigurationPublishRequirement,
        "institution.configuration.publish"));

    // On-demand reconciliation of the product and currency configuration areas.
    // Decision: P1-E04 slice 14.
    app.MapGet("/internal/v1/reconciliation/product-accounts", async (
        IConfigurationReconciliationService service,
        CancellationToken cancellationToken) =>
    {
        try
        {
            ConfigurationReconciliationReport report = await service.ReconcileAsync(cancellationToken);
            return Results.Ok(report);
        }
        catch (InvalidOperationException exception)
        {
            return Problem("reconciliation.context_required", "Reconciliation unavailable", exception.Message, StatusCodes.Status409Conflict);
        }
    })
    .AddEndpointFilter(new ProtectedInstitutionOperationFilter(
        InstitutionPermissions.ConfigurationAuditViewRequirement,
        "institution.configuration.audit"));

    app.MapPost("/api/v1/institutions/{institutionId}/products/{productId}/savings-eligibility/versions", async (
        HttpContext http,
        string institutionId,
        string productId,
        CreateSavingsEligibilityDraftRequestV1 request,
        ISavingsProductConfigurationService service,
        CancellationToken cancellationToken) =>
    {
        string? key = RequiredHeader(http, "Idempotency-Key");
        if (key is null) return IdempotencyKeyRequired();
        try
        {
            ArgumentNullException.ThrowIfNull(request.Rules);
            SavingsEligibilityDraftResult result = await service.CreateDraftAsync(
                new CreateSavingsEligibilityDraftCommand(
                    InstitutionId.From(institutionId),
                    Actor(http).Id,
                    key,
                    productId,
                    request.ProductCode,
                    request.VersionId,
                    request.VersionNumber,
                    ToRules(request.Rules),
                    CorrelationId(http)),
                cancellationToken);
            return Results.Ok(new SavingsEligibilityDraftResponseV1(
                result.InstitutionId,
                result.ProductId,
                result.ProductCode,
                result.VersionId,
                result.VersionNumber,
                result.ContentHash,
                result.Status,
                result.ChangedAt,
                result.Replayed));
        }
        catch (SavingsProductConfigurationRejectedException exception)
        {
            return SavingsProductProblem(exception, "Product draft rejected");
        }
        catch (SavingsProductConfigurationInProgressException exception)
        {
            return SavingsProductInProgress(exception);
        }
        catch (ArgumentException exception)
        {
            return Problem("request.invalid", "Invalid product draft", exception.Message, StatusCodes.Status400BadRequest);
        }
    })
    .AddEndpointFilter(new ProtectedInstitutionOperationFilter(
        ProductPermissions.UpdateRequirement,
        "product.savings.eligibility.draft.create"));

    app.MapPost("/api/v1/institutions/{institutionId}/product-versions/{versionId}/simulations", async (
        HttpContext http,
        string institutionId,
        string versionId,
        SimulateSavingsEligibilityRequestV1 request,
        ISavingsProductConfigurationService service,
        CancellationToken cancellationToken) =>
    {
        string? key = RequiredHeader(http, "Idempotency-Key");
        if (key is null) return IdempotencyKeyRequired();
        try
        {
            ArgumentNullException.ThrowIfNull(request.Scenarios);
            SavingsEligibilitySimulationCommandResult result = await service.SimulateAsync(
                new SimulateSavingsEligibilityCommand(
                    InstitutionId.From(institutionId),
                    Actor(http).Id,
                    key,
                    versionId,
                    request.ContentHash,
                    request.SimulationId,
                    request.Scenarios.Select(ToScenario).ToArray(),
                    CorrelationId(http)),
                cancellationToken);
            return Results.Ok(new SavingsEligibilitySimulationResponseV1(
                result.InstitutionId,
                result.VersionId,
                result.SimulationId,
                result.ContentHash,
                result.InputHash,
                result.OutputHash,
                result.Outcome,
                result.SimulatedAt,
                result.Replayed));
        }
        catch (SavingsProductConfigurationRejectedException exception)
        {
            return SavingsProductProblem(exception, "Product simulation rejected");
        }
        catch (SavingsProductConfigurationInProgressException exception)
        {
            return SavingsProductInProgress(exception);
        }
        catch (ArgumentException exception)
        {
            return Problem("request.invalid", "Invalid product simulation", exception.Message, StatusCodes.Status400BadRequest);
        }
    })
    .AddEndpointFilter(new ProtectedInstitutionOperationFilter(
        ProductPermissions.SimulateRequirement,
        "product.savings.eligibility.simulate"));

    app.MapPost("/api/v1/institutions/{institutionId}/product-versions/{versionId}/submit", async (
        HttpContext http,
        string institutionId,
        string versionId,
        SubmitSavingsEligibilityRequestV1 request,
        ISavingsProductConfigurationService service,
        CancellationToken cancellationToken) =>
    {
        string? key = RequiredHeader(http, "Idempotency-Key");
        if (key is null) return IdempotencyKeyRequired();
        try
        {
            SavingsEligibilityApprovalSubmissionResult result = await service.SubmitForApprovalAsync(
                new SubmitSavingsEligibilityForApprovalCommand(
                    InstitutionId.From(institutionId),
                    Actor(http).Id,
                    key,
                    request.RequestId,
                    request.SourceRequestKey,
                    versionId,
                    request.ContentHash,
                    CorrelationId(http)),
                cancellationToken);
            return Results.Accepted(
                value: new SavingsEligibilityApprovalSubmissionResponseV1(
                    result.InstitutionId,
                    result.RequestId,
                    result.VersionId,
                    result.ContentHash,
                    result.EventId,
                    result.Status,
                    result.SubmittedAt,
                    result.Replayed));
        }
        catch (SavingsProductConfigurationRejectedException exception)
        {
            return SavingsProductProblem(exception, "Product approval submission rejected");
        }
        catch (SavingsProductConfigurationInProgressException exception)
        {
            return SavingsProductInProgress(exception);
        }
        catch (ArgumentException exception)
        {
            return Problem("request.invalid", "Invalid product approval submission", exception.Message, StatusCodes.Status400BadRequest);
        }
    })
    .AddEndpointFilter(new ProtectedInstitutionOperationFilter(
        ProductPermissions.UpdateRequirement,
        "product.savings.eligibility.approval.submit"));

    app.MapPost("/api/v1/institutions/{institutionId}/product-versions/{versionId}/publish", async (
        HttpContext http,
        string institutionId,
        string versionId,
        PublishSavingsEligibilityRequestV1 request,
        ISavingsProductConfigurationService service,
        CancellationToken cancellationToken) =>
    {
        string? key = RequiredHeader(http, "Idempotency-Key");
        if (key is null) return IdempotencyKeyRequired();
        try
        {
            SavingsEligibilityPublicationResult result = await service.PublishAsync(
                new PublishSavingsEligibilityCommand(
                    InstitutionId.From(institutionId),
                    Actor(http).Id,
                    key,
                    request.PublicationId,
                    versionId,
                    request.ContentHash,
                    request.EffectiveFrom,
                    request.EffectiveTo,
                    CorrelationId(http)),
                cancellationToken);
            return Results.Ok(new SavingsEligibilityPublicationResponseV1(
                result.InstitutionId,
                result.PublicationId,
                result.ProductId,
                result.VersionId,
                result.ContentHash,
                result.EventId,
                result.Status,
                result.EffectiveFrom,
                result.EffectiveTo,
                result.PublishedAt,
                result.Replayed));
        }
        catch (SavingsProductConfigurationRejectedException exception)
        {
            return SavingsProductProblem(exception, "Product publication rejected");
        }
        catch (SavingsProductConfigurationInProgressException exception)
        {
            return SavingsProductInProgress(exception);
        }
        catch (ArgumentException exception)
        {
            return Problem("request.invalid", "Invalid product publication", exception.Message, StatusCodes.Status400BadRequest);
        }
    })
    .AddEndpointFilter(new ProtectedInstitutionOperationFilter(
        ProductPermissions.PublishRequirement,
        "product.savings.eligibility.publish"));
}

static void MapAccountEndpoints(WebApplication app)
{
    // Institution account-opening policy lifecycle (area institution.account-opening-policy).
    // Decision: P1-E04-D07.
    app.MapPost("/api/v1/institutions/{institutionId}/configurations/institution.account-opening-policy/versions", async (
        HttpContext http,
        string institutionId,
        CreateAccountOpeningPolicyDraftRequestV1 request,
        IAccountOpeningPolicyConfigurationService service,
        CancellationToken cancellationToken) =>
    {
        string? key = RequiredHeader(http, "Idempotency-Key");
        if (key is null) return IdempotencyKeyRequired();
        try
        {
            ConfigurationDraftOutcome result = await service.CreateDraftAsync(
                new CreateAccountOpeningPolicyDraftCommand(
                    InstitutionId.From(institutionId),
                    Actor(http).Id,
                    key,
                    request.VersionId,
                    AccountOpeningPolicy.ParseLimitKey(request.LimitKey),
                    request.MaximumOpenAccounts,
                    request.OverrideAdditionalAccounts,
                    CorrelationId(http)),
                cancellationToken);
            return Results.Ok(result);
        }
        catch (InstitutionConfigurationRejectedException exception)
        {
            return Problem(exception.Code, "Account-opening policy rejected", exception.Message, StatusCodes.Status409Conflict);
        }
        catch (ArgumentException exception)
        {
            return Problem("request.invalid", "Invalid account-opening policy", exception.Message, StatusCodes.Status400BadRequest);
        }
    })
    .AddEndpointFilter(new ProtectedInstitutionOperationFilter(
        InstitutionPermissions.ConfigurationCreateRequirement,
        "institution.configuration.create"));

    app.MapPost("/api/v1/configurations/account-opening-policy/{versionId}/simulate", async (
        HttpContext http,
        string versionId,
        SimulateAccountOpeningPolicyRequestV1 request,
        IAccountOpeningPolicyConfigurationService service,
        CancellationToken cancellationToken) =>
    {
        string? key = RequiredHeader(http, "Idempotency-Key");
        if (key is null) return IdempotencyKeyRequired();
        try
        {
            ConfigurationSimulationOutcome result = await service.SimulateAsync(
                new SimulateAccountOpeningPolicyCommand(
                    Actor(http).InstitutionId,
                    Actor(http).Id,
                    key,
                    versionId,
                    request.SimulationId,
                    request.ContentHash,
                    CorrelationId(http)),
                cancellationToken);
            return Results.Ok(result);
        }
        catch (InstitutionConfigurationRejectedException exception)
        {
            return Problem(exception.Code, "Account-opening policy simulation rejected", exception.Message, StatusCodes.Status409Conflict);
        }
        catch (ArgumentException exception)
        {
            return Problem("request.invalid", "Invalid account-opening policy simulation", exception.Message, StatusCodes.Status400BadRequest);
        }
    })
    .AddEndpointFilter(new ProtectedInstitutionOperationFilter(
        InstitutionPermissions.ConfigurationSimulateRequirement,
        "institution.configuration.simulate"));

    app.MapPost("/api/v1/configurations/account-opening-policy/{versionId}/submit", async (
        HttpContext http,
        string versionId,
        SubmitAccountOpeningPolicyRequestV1 request,
        IAccountOpeningPolicyConfigurationService service,
        CancellationToken cancellationToken) =>
    {
        string? key = RequiredHeader(http, "Idempotency-Key");
        if (key is null) return IdempotencyKeyRequired();
        try
        {
            ConfigurationSubmissionOutcome result = await service.SubmitForApprovalAsync(
                new SubmitAccountOpeningPolicyCommand(
                    Actor(http).InstitutionId,
                    Actor(http).Id,
                    key,
                    request.RequestId,
                    request.SourceRequestKey,
                    versionId,
                    request.ContentHash,
                    CorrelationId(http)),
                cancellationToken);
            return Results.Ok(result);
        }
        catch (InstitutionConfigurationRejectedException exception)
        {
            return Problem(exception.Code, "Account-opening policy submission rejected", exception.Message, StatusCodes.Status409Conflict);
        }
        catch (ArgumentException exception)
        {
            return Problem("request.invalid", "Invalid account-opening policy submission", exception.Message, StatusCodes.Status400BadRequest);
        }
    })
    .AddEndpointFilter(new ProtectedInstitutionOperationFilter(
        InstitutionPermissions.ConfigurationSubmitRequirement,
        "institution.configuration.submit"));

    app.MapPost("/api/v1/configurations/account-opening-policy/{versionId}/publish", async (
        HttpContext http,
        string versionId,
        PublishAccountOpeningPolicyRequestV1 request,
        IAccountOpeningPolicyConfigurationService service,
        CancellationToken cancellationToken) =>
    {
        string? key = RequiredHeader(http, "Idempotency-Key");
        if (key is null) return IdempotencyKeyRequired();
        try
        {
            ConfigurationPublicationOutcome result = await service.PublishAsync(
                new PublishAccountOpeningPolicyCommand(
                    Actor(http).InstitutionId,
                    Actor(http).Id,
                    key,
                    request.PublicationId,
                    versionId,
                    request.ContentHash,
                    request.EffectiveFrom,
                    request.EffectiveTo,
                    CorrelationId(http)),
                cancellationToken);
            return Results.Ok(result);
        }
        catch (InstitutionConfigurationRejectedException exception)
        {
            return Problem(exception.Code, "Account-opening policy publication rejected", exception.Message, StatusCodes.Status409Conflict);
        }
        catch (ArgumentException exception)
        {
            return Problem("request.invalid", "Invalid account-opening policy publication", exception.Message, StatusCodes.Status400BadRequest);
        }
    })
    .AddEndpointFilter(new ProtectedInstitutionOperationFilter(
        InstitutionPermissions.ConfigurationPublishRequirement,
        "institution.configuration.publish"));

    // Account applications: create a draft, then record one authoritative eligibility evaluation.
    // Decisions: P1-E04-D07, P1-E04-D09, P1-E04-D17.
    app.MapPost("/api/v1/institutions/{institutionId}/account-applications", async (
        HttpContext http,
        string institutionId,
        CreateAccountApplicationRequestV1 request,
        IAccountApplicationService service,
        CancellationToken cancellationToken) =>
    {
        string? key = RequiredHeader(http, "Idempotency-Key");
        if (key is null) return IdempotencyKeyRequired();
        try
        {
            AccountApplicationCreatedResult result = await service.CreateAsync(
                new CreateAccountApplicationCommand(
                    InstitutionId.From(institutionId),
                    Actor(http).Id,
                    key,
                    Guid.NewGuid().ToString("N"),
                    request.RequestId,
                    request.CustomerId,
                    request.ProductId,
                    request.BranchId,
                    request.Currency,
                    request.AccountPurposeCode,
                    request.OwnershipTypeCode,
                    CorrelationId(http)),
                cancellationToken);
            return Results.Ok(new AccountApplicationResponseV1(
                institutionId,
                result.ApplicationId,
                result.RequestId,
                result.CustomerId,
                result.ProductId,
                result.ProductVersionId,
                result.BranchId,
                result.CurrencyCode,
                result.Status.ToString(),
                string.Empty,
                result.Version,
                result.CreatedAt,
                result.Replayed));
        }
        catch (AccountApplicationRejectedException exception)
        {
            return Problem(exception.Code, "Account application rejected", exception.Message, StatusCodes.Status409Conflict);
        }
        catch (ArgumentException exception)
        {
            return Problem("request.invalid", "Invalid account application", exception.Message, StatusCodes.Status400BadRequest);
        }
    })
    .AddEndpointFilter(new ProtectedInstitutionOperationFilter(
        AccountPermissions.ApplicationCreateRequirement,
        "account.application.create"));

    app.MapPost("/api/v1/institutions/{institutionId}/account-applications/{applicationId}/submit", async (
        HttpContext http,
        string institutionId,
        string applicationId,
        SubmitAccountApplicationRequestV1 request,
        IAccountApplicationService service,
        CancellationToken cancellationToken) =>
    {
        string? key = RequiredHeader(http, "Idempotency-Key");
        if (key is null) return IdempotencyKeyRequired();
        try
        {
            AccountEligibilityResult result = await service.EvaluateAsync(
                new EvaluateAccountEligibilityCommand(
                    InstitutionId.From(institutionId),
                    Actor(http).Id,
                    key,
                    applicationId,
                    request.ExpectedVersion,
                    request.ReasonCode,
                    CorrelationId(http)),
                cancellationToken);
            return Results.Ok(ToEvaluationResponse(institutionId, result));
        }
        catch (AccountApplicationRejectedException exception)
        {
            return Problem(exception.Code, "Account eligibility rejected", exception.Message, StatusCodes.Status409Conflict);
        }
        catch (ArgumentException exception)
        {
            return Problem("request.invalid", "Invalid account submission", exception.Message, StatusCodes.Status400BadRequest);
        }
    })
    .AddEndpointFilter(new ProtectedInstitutionOperationFilter(
        AccountPermissions.ApplicationCreateRequirement,
        "account.application.create"));

    app.MapGet("/api/v1/institutions/{institutionId}/account-applications/{applicationId}/eligibility", async (
        HttpContext http,
        string institutionId,
        string applicationId,
        IAccountApplicationService service,
        CancellationToken cancellationToken) =>
    {
        try
        {
            AccountEligibilityResult? result = await service.GetLatestEvaluationAsync(
                new GetAccountApplicationQuery(InstitutionId.From(institutionId), applicationId),
                cancellationToken);
            if (result is null)
            {
                return Problem(
                    "account_evaluation_not_found",
                    "Eligibility evidence not found",
                    "The application has no recorded eligibility evaluation.",
                    StatusCodes.Status404NotFound);
            }

            return Results.Ok(ToEvaluationResponse(institutionId, result));
        }
        catch (AccountApplicationRejectedException exception)
        {
            return Problem(exception.Code, "Eligibility evidence rejected", exception.Message, StatusCodes.Status409Conflict);
        }
        catch (ArgumentException exception)
        {
            return Problem("request.invalid", "Invalid eligibility query", exception.Message, StatusCodes.Status400BadRequest);
        }
    })
    .AddEndpointFilter(new ProtectedInstitutionOperationFilter(
        AccountPermissions.ApplicationViewRequirement,
        "account.application.view"));

    // Institution account-numbering lifecycle (area institution.account-numbering).
    // Decision: P1-E04-D06.
    app.MapPost("/api/v1/institutions/{institutionId}/configurations/institution.account-numbering/versions", async (
        HttpContext http,
        string institutionId,
        CreateAccountNumberingDraftRequestV1 request,
        IAccountNumberingConfigurationService service,
        CancellationToken cancellationToken) =>
    {
        string? key = RequiredHeader(http, "Idempotency-Key");
        if (key is null) return IdempotencyKeyRequired();
        try
        {
            ConfigurationDraftOutcome result = await service.CreateDraftAsync(
                new CreateAccountNumberingDraftCommand(
                    InstitutionId.From(institutionId),
                    Actor(http).Id,
                    key,
                    request.VersionId,
                    request.SchemeId,
                    request.SchemeVersion,
                    request.Prefix,
                    request.ProductCodeWidth,
                    request.BranchCodeWidth,
                    request.SequenceWidth,
                    request.CheckDigit,
                    CorrelationId(http)),
                cancellationToken);
            return Results.Ok(result);
        }
        catch (InstitutionConfigurationRejectedException exception)
        {
            return Problem(exception.Code, "Account-numbering scheme rejected", exception.Message, StatusCodes.Status409Conflict);
        }
        catch (ArgumentException exception)
        {
            return Problem("request.invalid", "Invalid account-numbering scheme", exception.Message, StatusCodes.Status400BadRequest);
        }
    })
    .AddEndpointFilter(new ProtectedInstitutionOperationFilter(
        InstitutionPermissions.ConfigurationCreateRequirement,
        "institution.configuration.create"));

    app.MapPost("/api/v1/configurations/account-numbering/{versionId}/simulate", async (
        HttpContext http,
        string versionId,
        SimulateAccountNumberingRequestV1 request,
        IAccountNumberingConfigurationService service,
        CancellationToken cancellationToken) =>
    {
        string? key = RequiredHeader(http, "Idempotency-Key");
        if (key is null) return IdempotencyKeyRequired();
        try
        {
            ConfigurationSimulationOutcome result = await service.SimulateAsync(
                new SimulateAccountNumberingCommand(
                    Actor(http).InstitutionId,
                    Actor(http).Id,
                    key,
                    versionId,
                    request.SimulationId,
                    request.ContentHash,
                    CorrelationId(http)),
                cancellationToken);
            return Results.Ok(result);
        }
        catch (InstitutionConfigurationRejectedException exception)
        {
            return Problem(exception.Code, "Account-numbering simulation rejected", exception.Message, StatusCodes.Status409Conflict);
        }
        catch (ArgumentException exception)
        {
            return Problem("request.invalid", "Invalid account-numbering simulation", exception.Message, StatusCodes.Status400BadRequest);
        }
    })
    .AddEndpointFilter(new ProtectedInstitutionOperationFilter(
        InstitutionPermissions.ConfigurationSimulateRequirement,
        "institution.configuration.simulate"));

    app.MapPost("/api/v1/configurations/account-numbering/{versionId}/submit", async (
        HttpContext http,
        string versionId,
        SubmitAccountNumberingRequestV1 request,
        IAccountNumberingConfigurationService service,
        CancellationToken cancellationToken) =>
    {
        string? key = RequiredHeader(http, "Idempotency-Key");
        if (key is null) return IdempotencyKeyRequired();
        try
        {
            ConfigurationSubmissionOutcome result = await service.SubmitForApprovalAsync(
                new SubmitAccountNumberingCommand(
                    Actor(http).InstitutionId,
                    Actor(http).Id,
                    key,
                    request.RequestId,
                    request.SourceRequestKey,
                    versionId,
                    request.ContentHash,
                    CorrelationId(http)),
                cancellationToken);
            return Results.Ok(result);
        }
        catch (InstitutionConfigurationRejectedException exception)
        {
            return Problem(exception.Code, "Account-numbering submission rejected", exception.Message, StatusCodes.Status409Conflict);
        }
        catch (ArgumentException exception)
        {
            return Problem("request.invalid", "Invalid account-numbering submission", exception.Message, StatusCodes.Status400BadRequest);
        }
    })
    .AddEndpointFilter(new ProtectedInstitutionOperationFilter(
        InstitutionPermissions.ConfigurationSubmitRequirement,
        "institution.configuration.submit"));

    app.MapPost("/api/v1/configurations/account-numbering/{versionId}/publish", async (
        HttpContext http,
        string versionId,
        PublishAccountNumberingRequestV1 request,
        IAccountNumberingConfigurationService service,
        CancellationToken cancellationToken) =>
    {
        string? key = RequiredHeader(http, "Idempotency-Key");
        if (key is null) return IdempotencyKeyRequired();
        try
        {
            ConfigurationPublicationOutcome result = await service.PublishAsync(
                new PublishAccountNumberingCommand(
                    Actor(http).InstitutionId,
                    Actor(http).Id,
                    key,
                    request.PublicationId,
                    versionId,
                    request.ContentHash,
                    request.EffectiveFrom,
                    request.EffectiveTo,
                    CorrelationId(http)),
                cancellationToken);
            return Results.Ok(result);
        }
        catch (InstitutionConfigurationRejectedException exception)
        {
            return Problem(exception.Code, "Account-numbering publication rejected", exception.Message, StatusCodes.Status409Conflict);
        }
        catch (ArgumentException exception)
        {
            return Problem("request.invalid", "Invalid account-numbering publication", exception.Message, StatusCodes.Status400BadRequest);
        }
    })
    .AddEndpointFilter(new ProtectedInstitutionOperationFilter(
        InstitutionPermissions.ConfigurationPublishRequirement,
        "institution.configuration.publish"));

    // Opening an approved application: one atomic reservation and account creation.
    // Decisions: P1-E04-D06, P1-E04-D09.
    app.MapPost("/api/v1/institutions/{institutionId}/account-applications/{applicationId}/open", async (
        HttpContext http,
        string institutionId,
        string applicationId,
        OpenAccountRequestV1 request,
        IAccountOpeningService service,
        CancellationToken cancellationToken) =>
    {
        string? key = RequiredHeader(http, "Idempotency-Key");
        if (key is null) return IdempotencyKeyRequired();
        try
        {
            AccountOpenedResult result = await service.OpenAsync(
                new OpenAccountCommand(
                    InstitutionId.From(institutionId),
                    Actor(http).Id,
                    key,
                    applicationId,
                    request.ExpectedVersion,
                    CorrelationId(http)),
                cancellationToken);
            return Results.Ok(new AccountResponseV1(
                institutionId,
                result.AccountId,
                result.MaskedAccountNumber,
                result.CustomerId,
                result.ProductId,
                result.ProductVersionId,
                result.BranchId,
                result.CurrencyCode,
                result.Status,
                result.LedgerInitialisationStatus,
                result.Version,
                result.OpenedAt,
                result.Replayed));
        }
        catch (AccountApplicationRejectedException exception)
        {
            return Problem(exception.Code, "Account opening rejected", exception.Message, StatusCodes.Status409Conflict);
        }
        catch (ArgumentException exception)
        {
            return Problem("request.invalid", "Invalid account opening", exception.Message, StatusCodes.Status400BadRequest);
        }
    })
    .AddEndpointFilter(new ProtectedInstitutionOperationFilter(
        AccountPermissions.ApplicationApproveRequirement,
        "account.application.approve"));

    app.MapPost("/api/v1/institutions/{institutionId}/accounts/{accountId}/number-reveal", async (
        HttpContext http,
        string institutionId,
        string accountId,
        RevealAccountNumberRequestV1 request,
        IAccountNumberRevealService service,
        CancellationToken cancellationToken) =>
    {
        try
        {
            AccountNumberRevealResult result = await service.RevealAsync(
                new RevealAccountNumberCommand(
                    InstitutionId.From(institutionId),
                    Actor(http).Id,
                    accountId,
                    request.Reason,
                    CorrelationId(http)),
                cancellationToken);
            return Results.Ok(new AccountNumberRevealResponseV1(
                institutionId, result.AccountId, result.AccountNumber, result.RevealedAt));
        }
        catch (AccountApplicationRejectedException exception)
        {
            int status = exception.Code == "account_not_found"
                ? StatusCodes.Status404NotFound
                : StatusCodes.Status409Conflict;
            return Problem(exception.Code, "Account number reveal rejected", exception.Message, status);
        }
        catch (ArgumentException exception)
        {
            return Problem("request.invalid", "Invalid account number reveal", exception.Message, StatusCodes.Status400BadRequest);
        }
    })
    .AddEndpointFilter(new ProtectedInstitutionOperationFilter(
        AccountPermissions.NumberRevealRequirement,
        "account.number.reveal"));
}

static AccountEligibilityEvaluationResponseV1 ToEvaluationResponse(string institutionId, AccountEligibilityResult result) =>
    new(
        institutionId,
        result.ApplicationId,
        result.EvaluationId,
        result.ProductVersionId,
        result.ProductContentHash,
        result.CustomerFactsVersion,
        result.BranchReferenceVersion,
        result.CurrencyReferenceVersion,
        result.Eligible,
        result.FactsCurrent,
        result.DecisionCode,
        result.Limit?.LimitKey,
        result.Limit?.Counted,
        result.Limit?.EffectiveMaximum,
        result.Limit?.CountedHoldingIds ?? [],
        result.Trace.Select(trace => new EligibilityRuleTraceV1(trace.Rule, trace.Input, trace.Passed)).ToArray(),
        result.Status.ToString(),
        result.Version,
        result.EvaluatedAt,
        result.Replayed);

static ProductEligibilityRules ToRules(SavingsEligibilityRulesV1 rules) => ProductEligibilityRules.Create(
    rules.RuleSetId,
    rules.Version,
    ToCodeCriterion(rules.CustomerTypes, nameof(rules.CustomerTypes)),
    ToCodeCriterion(rules.KycTiers, nameof(rules.KycTiers)),
    ToCodeCriterion(rules.Segments, nameof(rules.Segments)),
    ToCodeCriterion(rules.RiskRatings, nameof(rules.RiskRatings)),
    ToAgeCriterion(rules.Ages),
    ToCodeCriterion(rules.Branches, nameof(rules.Branches)),
    ToCodeCriterion(rules.ResidencyCountries, nameof(rules.ResidencyCountries)),
    ToCodeCriterion(rules.Currencies, nameof(rules.Currencies)),
    ToExistingAccountsCriterion(rules.ExistingAccounts));

static ExistingAccountsCriterion ToExistingAccountsCriterion(ExistingAccountsCriterionV1? criterion)
{
    if (criterion is null)
    {
        return ExistingAccountsCriterion.Any();
    }

    return criterion.Mode?.Trim().ToLowerInvariant() switch
    {
        "any" => ExistingAccountsCriterion.Any(),
        "limit" => ExistingAccountsCriterion.Limit(
            criterion.MaximumAccounts
                ?? throw new ArgumentException("A multiple-account limit requires maximumAccounts.", nameof(criterion)),
            AccountOpeningPolicy.ParseLimitKey(criterion.LimitKey)),
        _ => throw new ArgumentException("existingAccounts must use mode 'any' or mode 'limit'.", nameof(criterion)),
    };
}

static EligibilityCodeCriterion ToCodeCriterion(EligibilityCodeCriterionV1 criterion, string name)
{
    ArgumentNullException.ThrowIfNull(criterion, name);
    ArgumentNullException.ThrowIfNull(criterion.Values, name);
    return criterion.Mode?.Trim().ToLowerInvariant() switch
    {
        "any" when criterion.Values.Count == 0 => EligibilityCodeCriterion.Any(),
        "allow" => EligibilityCodeCriterion.Only(criterion.Values),
        _ => throw new ArgumentException($"{name} must use mode 'any' with no values or mode 'allow' with values.", name),
    };
}

static EligibilityAgeCriterion ToAgeCriterion(EligibilityAgeCriterionV1 criterion)
{
    ArgumentNullException.ThrowIfNull(criterion);
    return criterion.Mode?.Trim().ToLowerInvariant() switch
    {
        "any" when criterion.MinimumYears is null && criterion.MaximumYears is null => EligibilityAgeCriterion.Any(),
        "range" when criterion.MinimumYears is int minimum && criterion.MaximumYears is int maximum =>
            EligibilityAgeCriterion.Between(minimum, maximum),
        _ => throw new ArgumentException("Ages must use mode 'any' with no bounds or mode 'range' with both bounds.", nameof(criterion)),
    };
}

static ProductEligibilitySimulationInput ToScenario(ProductEligibilitySimulationScenarioV1 scenario)
{
    ArgumentNullException.ThrowIfNull(scenario);
    return new ProductEligibilitySimulationInput(
        scenario.ScenarioId,
        scenario.DatasetVersion,
        scenario.CustomerFactsVersion,
        scenario.BranchReferenceVersion,
        scenario.CurrencyReferenceVersion,
        scenario.CustomerType,
        scenario.KycTier,
        scenario.Segment,
        scenario.RiskRating,
        scenario.AgeYears,
        scenario.BranchCode,
        scenario.ResidencyCountryCode,
        scenario.CurrencyCode,
        scenario.ExpectedEligible);
}

static IResult SavingsProductProblem(SavingsProductConfigurationRejectedException exception, string title)
{
    int status = exception.Code switch
    {
        "cross_tenant_access_denied" or "product_self_publication" => StatusCodes.Status403Forbidden,
        "product_version_not_found" => StatusCodes.Status404NotFound,
        _ => StatusCodes.Status409Conflict,
    };
    return Problem(exception.Code, title, exception.Message, status);
}

static IResult SavingsProductInProgress(SavingsProductConfigurationInProgressException exception) => Problem(
    "request.idempotency_in_progress",
    "Product configuration request in progress",
    exception.Message,
    StatusCodes.Status409Conflict);

static void MapOrganisationLifecycleEndpoints(WebApplication app)
{
    app.MapPost("/api/v1/institutions/{institutionId}/regions", async (HttpContext http, string institutionId, CreateRegionRequestV1 request, IOrganisationLifecycleService service, CancellationToken token) =>
        await ExecuteOrganisationCommand(http, key => service.CreateRegionAsync(
            new CreateRegionCommand(InstitutionId.From(institutionId), Actor(http).Id, key, request.RegionId, request.Code, request.Name, CorrelationId(http)), token)))
        .AddEndpointFilter(new ProtectedInstitutionOperationFilter(InstitutionPermissions.RegionManageRequirement, "institution.region.create"));

    app.MapPost("/api/v1/institutions/{institutionId}/regions/{regionId}/{targetStatus}", async (HttpContext http, string institutionId, string regionId, string targetStatus, ChangeOrganisationStatusRequestV1 request, IOrganisationLifecycleService service, CancellationToken token) =>
        await ExecuteOrganisationCommand(http, key => service.ChangeRegionStatusAsync(
            new ChangeOrganisationStatusCommand(InstitutionId.From(institutionId), Actor(http).Id, key, regionId, targetStatus.ToLowerInvariant(), request.Reason, CorrelationId(http)), token)))
        .AddEndpointFilter(new ProtectedInstitutionOperationFilter(InstitutionPermissions.RegionManageRequirement, "institution.region.transition"));

    app.MapPost("/api/v1/institutions/{institutionId}/branches", async (HttpContext http, string institutionId, CreateBranchRequestV1 request, IOrganisationLifecycleService service, CancellationToken token) =>
        await ExecuteOrganisationCommand(http, key => service.CreateBranchAsync(
            new CreateBranchCommand(InstitutionId.From(institutionId), Actor(http).Id, key, request.BranchId, request.RegionId, request.Code, request.Name, CorrelationId(http)), token)))
        .AddEndpointFilter(new ProtectedInstitutionOperationFilter(InstitutionPermissions.BranchCreateRequirement, "institution.branch.create"));

    app.MapPost("/api/v1/institutions/{institutionId}/branches/{branchId}/activate", async (HttpContext http, string institutionId, string branchId, ChangeOrganisationStatusRequestV1 request, IOrganisationLifecycleService service, CancellationToken token) =>
        await ExecuteOrganisationCommand(http, key => service.ChangeBranchStatusAsync(
            new ChangeOrganisationStatusCommand(InstitutionId.From(institutionId), Actor(http).Id, key, branchId, "active", request.Reason, CorrelationId(http)), token)))
        .AddEndpointFilter(new ProtectedInstitutionOperationFilter(InstitutionPermissions.BranchActivateRequirement, "institution.branch.activate"));

    app.MapPost("/api/v1/institutions/{institutionId}/branches/{branchId}/suspend", async (HttpContext http, string institutionId, string branchId, ChangeOrganisationStatusRequestV1 request, IOrganisationLifecycleService service, CancellationToken token) =>
        await ExecuteOrganisationCommand(http, key => service.ChangeBranchStatusAsync(
            new ChangeOrganisationStatusCommand(InstitutionId.From(institutionId), Actor(http).Id, key, branchId, "suspended", request.Reason, CorrelationId(http)), token)))
        .AddEndpointFilter(new ProtectedInstitutionOperationFilter(InstitutionPermissions.BranchSuspendRequirement, "institution.branch.suspend"));

    app.MapPost("/api/v1/institutions/{institutionId}/branches/{branchId}/close", async (HttpContext http, string institutionId, string branchId, ChangeOrganisationStatusRequestV1 request, IOrganisationLifecycleService service, CancellationToken token) =>
        await ExecuteOrganisationCommand(http, key => service.ChangeBranchStatusAsync(
            new ChangeOrganisationStatusCommand(InstitutionId.From(institutionId), Actor(http).Id, key, branchId, "closed", request.Reason, CorrelationId(http)), token)))
        .AddEndpointFilter(new ProtectedInstitutionOperationFilter(InstitutionPermissions.BranchSuspendRequirement, "institution.branch.close"));
}

static AuthenticatedActor Actor(HttpContext http) =>
    http.Items[ProtectedInstitutionOperationFilter.ActorItemKey] as AuthenticatedActor
    ?? throw new InvalidOperationException("The protected operation has no authenticated actor.");

static async Task<IResult> ExecuteOrganisationCommand(HttpContext http, Func<string, Task<OrganisationLifecycleResult>> command)
{
    if (http.Items[ProtectedInstitutionOperationFilter.ActorItemKey] is not AuthenticatedActor) return Results.Unauthorized();
    string? key = RequiredHeader(http, "Idempotency-Key");
    if (key is null) return IdempotencyKeyRequired();
    try
    {
        OrganisationLifecycleResult result = await command(key);
        return Results.Ok(new OrganisationLifecycleResponseV1(result.InstitutionId, result.ResourceId, result.EventId, result.Status, result.ChangedAt, result.Replayed));
    }
    catch (OrganisationLifecycleRejectedException exception)
    {
        int status = exception.Code == "cross_tenant_access_denied" ? StatusCodes.Status403Forbidden : StatusCodes.Status409Conflict;
        return Problem(exception.Code, "Organisation lifecycle request rejected", exception.Message, status);
    }
    catch (OrganisationLifecycleInProgressException exception)
    {
        return Problem("request.idempotency_in_progress", "Organisation lifecycle request in progress", exception.Message, StatusCodes.Status409Conflict);
    }
    catch (ArgumentException exception)
    {
        return Problem("request.invalid", "Invalid organisation lifecycle request", exception.Message, StatusCodes.Status400BadRequest);
    }
}

static void MapBusinessCalendarEndpoints(WebApplication app)
{
    app.MapPost("/api/v1/institutions/{institutionId}/calendars/versions", async (
        HttpContext http, string institutionId, CreateCalendarVersionRequestV1 request,
        IBusinessCalendarService service, CancellationToken token) =>
        await ExecuteCalendarCommand(http, key => service.CreateVersionAsync(
            new CreateCalendarVersionCommand(
                InstitutionId.From(institutionId), Actor(http).Id, key, request.CalendarId, request.VersionId,
                request.VersionNumber, request.TimeZoneId, request.EffectiveFrom, request.EffectiveTo,
                request.InitialBusinessDate,
                request.Days.Select(day => new CalendarDayDefinition(day.Date, ParseDayClassification(day.Classification), day.IsProcessingDay)).ToArray(),
                CorrelationId(http)), token)))
        .AddEndpointFilter(new ProtectedInstitutionOperationFilter(InstitutionPermissions.CalendarManageRequirement, "institution.calendar.version.create"));

    app.MapPost("/api/v1/institutions/{institutionId}/calendars/versions/{versionId}/publish", async (
        HttpContext http, string institutionId, string versionId, PublishCalendarVersionRequestV1 request,
        IBusinessCalendarService service, CancellationToken token) =>
        await ExecuteCalendarCommand(http, key => service.PublishVersionAsync(
            new PublishCalendarVersionCommand(
                InstitutionId.From(institutionId), Actor(http).Id, key, versionId,
                request.ApprovalReference, request.Reason, CorrelationId(http)), token)))
        .AddEndpointFilter(new ProtectedInstitutionOperationFilter(InstitutionPermissions.CalendarPublishRequirement, "institution.calendar.version.publish"));

    app.MapPost("/api/v1/institutions/{institutionId}/business-date/advance-requests", async (
        HttpContext http, string institutionId, RequestBusinessDateAdvanceV1 request,
        IBusinessCalendarService service, CancellationToken token) =>
        await ExecuteBusinessDateCommand(http, key => service.RequestAdvanceAsync(
            new RequestBusinessDateAdvanceCommand(
                InstitutionId.From(institutionId), Actor(http).Id, key, request.RequestId,
                request.ExpectedCurrentDate, request.NewBusinessDate, request.CalendarVersionId,
                request.ProcessingCheckpointId, request.Reason, CorrelationId(http)), token)))
        .AddEndpointFilter(new ProtectedInstitutionOperationFilter(InstitutionPermissions.BusinessDateAdvanceRequirement, "institution.business_date.advance.request"));

    app.MapPost("/api/v1/institutions/{institutionId}/business-date/advance-requests/{requestId}/approve", async (
        HttpContext http, string institutionId, string requestId, ApproveBusinessDateAdvanceV1 request,
        IBusinessCalendarService service, CancellationToken token) =>
        await ExecuteBusinessDateCommand(http, key => service.ApproveAdvanceAsync(
            new ApproveBusinessDateAdvanceCommand(
                InstitutionId.From(institutionId), Actor(http).Id, key, requestId,
                request.ApprovalReference, CorrelationId(http)), token)))
        .AddEndpointFilter(new ProtectedInstitutionOperationFilter(InstitutionPermissions.BusinessDateAdvanceRequirement, "institution.business_date.advance.approve"));
}

static CalendarDayClassification ParseDayClassification(string value) => value?.Trim().ToLowerInvariant() switch
{
    "working_day" => CalendarDayClassification.WorkingDay,
    "holiday" => CalendarDayClassification.Holiday,
    "non_working_day" => CalendarDayClassification.NonWorkingDay,
    _ => throw new ArgumentException("Calendar day classification must be working_day, holiday or non_working_day."),
};

static async Task<IResult> ExecuteCalendarCommand(HttpContext http, Func<string, Task<CalendarCommandResult>> command)
{
    string? key = RequiredHeader(http, "Idempotency-Key");
    if (key is null) return IdempotencyKeyRequired();
    try
    {
        CalendarCommandResult result = await command(key);
        return Results.Ok(new CalendarCommandResponseV1(result.InstitutionId, result.ResourceId, result.EventId, result.Status, result.ChangedAt, result.Replayed));
    }
    catch (BusinessCalendarRejectedException exception)
    {
        int status = exception.Code == "cross_tenant_access_denied" ? StatusCodes.Status403Forbidden : StatusCodes.Status409Conflict;
        return Problem(exception.Code, "Calendar request rejected", exception.Message, status);
    }
    catch (BusinessCalendarInProgressException exception)
    {
        return Problem("request.idempotency_in_progress", "Calendar request in progress", exception.Message, StatusCodes.Status409Conflict);
    }
    catch (ArgumentException exception)
    {
        return Problem("request.invalid", "Invalid calendar request", exception.Message, StatusCodes.Status400BadRequest);
    }
}

static async Task<IResult> ExecuteBusinessDateCommand(HttpContext http, Func<string, Task<BusinessDateAdvanceResult>> command)
{
    string? key = RequiredHeader(http, "Idempotency-Key");
    if (key is null) return IdempotencyKeyRequired();
    try
    {
        BusinessDateAdvanceResult result = await command(key);
        return Results.Ok(new BusinessDateAdvanceResponseV1(result.InstitutionId, result.RequestId, result.BusinessDate, result.EventId, result.Status, result.ChangedAt, result.Replayed));
    }
    catch (BusinessCalendarRejectedException exception)
    {
        int status = exception.Code == "cross_tenant_access_denied" ? StatusCodes.Status403Forbidden : StatusCodes.Status409Conflict;
        return Problem(exception.Code, "Business-date request rejected", exception.Message, status);
    }
    catch (BusinessCalendarInProgressException exception)
    {
        return Problem("request.idempotency_in_progress", "Business-date request in progress", exception.Message, StatusCodes.Status409Conflict);
    }
    catch (ArgumentException exception)
    {
        return Problem("request.invalid", "Invalid business-date request", exception.Message, StatusCodes.Status400BadRequest);
    }
}
