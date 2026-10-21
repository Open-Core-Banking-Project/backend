using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CoreBanking.BuildingBlocks.Application.Security;
using CoreBanking.BuildingBlocks.Application.Tenancy;
using CoreBanking.BuildingBlocks.Domain.Identity;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CoreBanking.OperationsWorkflow.Api.Authorisation;

public sealed partial class ProtectedApprovalOperationFilter : IEndpointFilter
{
    public const string ActorItemKey = "authorisation.actor";
    public const string CorrelationItemKey = "authorisation.correlationId";

    private readonly PermissionRequirement _requirement;
    private readonly string _operationName;

    public ProtectedApprovalOperationFilter(PermissionRequirement requirement, string operationName)
    {
        _requirement = requirement ?? throw new ArgumentNullException(nameof(requirement));
        ArgumentException.ThrowIfNullOrWhiteSpace(operationName);
        _operationName = operationName;
    }

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);
        HttpContext http = context.HttpContext;
        IServiceProvider services = http.RequestServices;
        ITenantContext tenantContext = services.GetRequiredService<ITenantContext>();
        string correlationId = ReadHeader(http, "X-Correlation-Id") ?? Guid.NewGuid().ToString("N");
        AuthenticatedActor? actor = services.GetRequiredService<IActorContextResolver>()
            .Resolve(ReadHeader(http, "X-Development-Actor"));

        try
        {
            if (actor is not null) tenantContext.Establish(actor.InstitutionId);
            string? institutionValue = http.Request.RouteValues["institutionId"]?.ToString();
            ResourceScope scope = string.IsNullOrWhiteSpace(institutionValue)
                ? ResourceScope.None
                : ResourceScope.ForInstitution(InstitutionId.From(institutionValue));
            AuthorisationDecision decision = services.GetRequiredService<AuthorisationPolicy>().Evaluate(new AuthorisationRequest
            {
                Requirement = _requirement,
                Scope = scope,
                Actor = actor,
                StepUpSatisfied = actor?.Strength == AuthenticationStrength.StepUp,
            });
            ILogger<ProtectedApprovalOperationFilter> logger = services.GetRequiredService<ILogger<ProtectedApprovalOperationFilter>>();
            LogDecision(logger, decision.IsAllowed ? "allowed" : "denied", actor?.Id.Value,
                _requirement.Permission.Value, decision.IsAllowed ? "none" : AuthorisationCodes.For(decision.Reason),
                correlationId, _operationName);
            if (!decision.IsAllowed)
            {
                return Results.Problem(
                    detail: decision.Detail ?? "The request was refused.",
                    statusCode: AuthorisationCodes.StatusFor(decision.Reason),
                    title: AuthorisationCodes.TitleFor(decision.Reason),
                    extensions: new Dictionary<string, object?>(StringComparer.Ordinal)
                    {
                        ["code"] = AuthorisationCodes.For(decision.Reason),
                        ["correlationId"] = correlationId,
                    });
            }

            http.Items[ActorItemKey] = actor;
            http.Items[CorrelationItemKey] = correlationId;
            return await next(context);
        }
        finally
        {
            tenantContext.Clear();
        }
    }

    private static string? ReadHeader(HttpContext http, string name)
    {
        string value = http.Request.Headers[name].ToString();
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    [LoggerMessage(
        EventId = 3200,
        Level = LogLevel.Information,
        Message = "Authorisation {Decision} for actor {ActorId}, permission {Permission}, reason {Reason}, correlation {CorrelationId}, operation {Operation}")]
    private static partial void LogDecision(
        ILogger logger,
        string decision,
        string? actorId,
        string permission,
        string reason,
        string correlationId,
        string operation);
}
