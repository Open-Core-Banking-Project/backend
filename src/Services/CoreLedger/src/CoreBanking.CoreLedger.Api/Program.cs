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
using CoreBanking.BuildingBlocks.Infrastructure.Tenancy;
using CoreBanking.CoreLedger.Api.Authorisation;
using CoreBanking.CoreLedger.Application.Institutions;
using CoreBanking.CoreLedger.Application.Products;
using CoreBanking.CoreLedger.Application.Provisioning;
using CoreBanking.CoreLedger.Contracts;
using CoreBanking.CoreLedger.Domain.Institutions;
using CoreBanking.CoreLedger.Domain.Products;
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
if (builder.Environment.IsDevelopment())
{
    builder.Services.AddSingleton<LocalEntitlementStore>();
    builder.Services.AddSingleton<IEffectivePermissionSource>(provider => provider.GetRequiredService<LocalEntitlementStore>());
    builder.Services.AddSingleton<IEntitlementVersionSource>(provider => provider.GetRequiredService<LocalEntitlementStore>());
    builder.Services.AddSingleton<IActorContextResolver, DevelopmentActorContextResolver>();
}
else
{
    builder.Services.AddSingleton<EmptyEntitlementStore>();
    builder.Services.AddSingleton<IEffectivePermissionSource>(provider => provider.GetRequiredService<EmptyEntitlementStore>());
    builder.Services.AddSingleton<IEntitlementVersionSource>(provider => provider.GetRequiredService<EmptyEntitlementStore>());
    builder.Services.AddSingleton<IActorContextResolver, UnconfiguredActorContextResolver>();
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
    builder.Services.AddScoped<IOrganisationLifecycleService, OrganisationLifecycleService>();
    builder.Services.AddScoped<IBusinessCalendarService, BusinessCalendarService>();
    builder.Services.AddScoped<ISavingsProductConfigurationService, SavingsProductConfigurationService>();
    builder.Services.AddScoped<ISavingsEligibilityEvaluationService, SavingsEligibilityEvaluationService>();
}

CoreBanking.BuildingBlocks.Observability.CoreBankingObservability.AddCoreBankingObservability(
    builder.Services,
    builder.Configuration,
    builder.Environment,
    "corebanking.core-ledger.api");
WebApplication app = builder.Build();

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

static void MapSavingsProductEndpoints(WebApplication app)
{
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
    ToCodeCriterion(rules.Currencies, nameof(rules.Currencies)));

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
