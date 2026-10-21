using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CoreBanking.BuildingBlocks.Application.Security;
using CoreBanking.BuildingBlocks.Application.Tenancy;
using CoreBanking.BuildingBlocks.Domain.Identity;
using CoreBanking.BuildingBlocks.Infrastructure.Tenancy;
using CoreBanking.OperationsWorkflow.Api.Authorisation;
using CoreBanking.OperationsWorkflow.Application.Approvals;
using CoreBanking.OperationsWorkflow.Contracts;
using CoreBanking.OperationsWorkflow.Domain.Approvals;
using CoreBanking.OperationsWorkflow.Infrastructure.Approvals;
using CoreBanking.OperationsWorkflow.Infrastructure.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
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
builder.Services.AddScoped<IApprovalEntitlementVerifier>(provider => new CurrentEntitlementVerifier(
    provider.GetRequiredService<IEffectivePermissionSource>(),
    provider.GetRequiredService<IEntitlementVersionSource>(),
    TimeSpan.FromSeconds(60)));

string? connectionString = builder.Configuration.GetConnectionString("OperationsWorkflow");
bool useRelationalStore = !string.IsNullOrWhiteSpace(connectionString);
if (!useRelationalStore && !builder.Environment.IsDevelopment())
{
    throw new InvalidOperationException("ConnectionStrings:OperationsWorkflow is required outside Development.");
}

if (useRelationalStore)
{
    builder.Services.AddDbContext<OperationsWorkflowDbContext>(options => options.UseNpgsql(connectionString));
    builder.Services.AddScoped<IApprovalWorkflowService, ApprovalWorkflowService>();
}

CoreBanking.BuildingBlocks.Observability.CoreBankingObservability.AddCoreBankingObservability(
    builder.Services,
    builder.Configuration,
    builder.Environment,
    "corebanking.operations-workflow.api");
WebApplication app = builder.Build();

if (app.Environment.IsDevelopment())
{
    DevelopmentAuthorisationSeed.Apply(app.Services);
}

app.MapGet("/health/live", () => Results.Ok(new { status = "Healthy" }));
app.MapGet("/health/ready", async (CancellationToken cancellationToken) =>
{
    if (!useRelationalStore) return Results.Ok(new { status = "Healthy" });
    try
    {
        await using AsyncServiceScope scope = app.Services.CreateAsyncScope();
        OperationsWorkflowDbContext context = scope.ServiceProvider.GetRequiredService<OperationsWorkflowDbContext>();
        return await context.Database.CanConnectAsync(cancellationToken)
            ? Results.Ok(new { status = "Healthy" })
            : Results.Json(new { status = "Unhealthy" }, statusCode: StatusCodes.Status503ServiceUnavailable);
    }
    catch (Exception)
    {
        return Results.Json(new { status = "Unhealthy" }, statusCode: StatusCodes.Status503ServiceUnavailable);
    }
});

if (useRelationalStore)
{
    app.MapPost("/api/v1/institutions/{institutionId}/approval-requests/{requestId}/decision", async (
        HttpContext http,
        string institutionId,
        string requestId,
        DecideApprovalRequestV1 request,
        IApprovalWorkflowService service,
        CancellationToken cancellationToken) =>
    {
        if (http.Items[ProtectedApprovalOperationFilter.ActorItemKey] is not AuthenticatedActor actor)
            return Results.Unauthorized();

        string decisionId = http.Request.Headers["Idempotency-Key"].ToString().Trim();
        if (string.IsNullOrWhiteSpace(decisionId))
        {
            return Problem(
                "request.idempotency_key_required",
                "Idempotency key required",
                "An Idempotency-Key header is required for this command.",
                StatusCodes.Status400BadRequest);
        }

        if (!TryParseDecision(request.Decision, out ApprovalDecisionType decision))
        {
            return Problem(
                "request.invalid_decision",
                "Invalid approval decision",
                "Decision must be either approve or reject.",
                StatusCodes.Status400BadRequest);
        }

        try
        {
            ApprovalDecisionResult result = await service.DecideAsync(
                new DecideApprovalCommand(
                    InstitutionId.From(institutionId),
                    actor.Id,
                    requestId,
                    decisionId,
                    decision,
                    request.Reason,
                    CorrelationId(http)),
                cancellationToken);
            return Results.Ok(new ApprovalDecisionResponseV1(
                result.InstitutionId,
                result.RequestId,
                result.DecisionId,
                result.Decision,
                result.SubjectVersion,
                result.SubjectHash,
                result.EventId,
                result.DecidedAt,
                result.Replayed));
        }
        catch (ApprovalWorkflowRejectedException exception)
        {
            return ApprovalProblem(exception);
        }
        catch (ArgumentException exception)
        {
            return Problem("request.invalid", "Invalid approval request", exception.Message, StatusCodes.Status400BadRequest);
        }
    })
    .AddEndpointFilter(new ProtectedApprovalOperationFilter(
        ApprovalPermissions.DecideRequirement,
        "approval.request.decide"));
}

app.Run();

static bool TryParseDecision(string? value, out ApprovalDecisionType decision)
{
    switch (value?.Trim().ToLowerInvariant())
    {
        case "approve":
            decision = ApprovalDecisionType.Approve;
            return true;
        case "reject":
            decision = ApprovalDecisionType.Reject;
            return true;
        default:
            decision = default;
            return false;
    }
}

static string CorrelationId(HttpContext http) =>
    http.Items[ProtectedApprovalOperationFilter.CorrelationItemKey] as string ?? Guid.NewGuid().ToString("N");

static IResult ApprovalProblem(ApprovalWorkflowRejectedException exception)
{
    int status = exception.Code switch
    {
        "approval_request_not_found" => StatusCodes.Status404NotFound,
        "cross_tenant_access_denied" or "approval_permission_missing" or
            "approval_entitlement_wrong_institution" or "approval_entitlement_stale" or
            "maker_cannot_approve_own_request" => StatusCodes.Status403Forbidden,
        _ => StatusCodes.Status409Conflict,
    };
    return Problem(exception.Code, "Approval decision rejected", exception.Message, status);
}

static IResult Problem(string code, string title, string detail, int status) =>
    Results.Problem(
        detail: detail,
        statusCode: status,
        title: title,
        extensions: new Dictionary<string, object?>(StringComparer.Ordinal) { ["code"] = code });
