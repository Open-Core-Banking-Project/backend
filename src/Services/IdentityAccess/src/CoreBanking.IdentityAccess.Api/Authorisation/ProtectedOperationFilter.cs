using System;
using System.Threading.Tasks;
using CoreBanking.BuildingBlocks.Application.Security;
using CoreBanking.BuildingBlocks.Application.Tenancy;
using CoreBanking.BuildingBlocks.Domain.Identity;
using CoreBanking.IdentityAccess.Application.Authorisation;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace CoreBanking.IdentityAccess.Api.Authorisation;

/// <summary>
/// Declares a permission for one protected endpoint and enforces it before the handler runs.
/// It resolves the actor, establishes the trusted institution context from that actor, and only
/// then evaluates the policy, so the persistence layer can rely on the context it reads. The
/// requested institution and branch come from the request as the target scope and the policy
/// refuses any mismatch, so client-supplied context can never widen access.
/// Collaborators are resolved per request because the relational entitlement store is scoped to
/// the request's database context.
/// Decision: ADR-0004, ADR-0009, epic 03 API requirements.
/// </summary>
public sealed class ProtectedOperationFilter : IEndpointFilter
{
    public const string ActorItemKey = "authorisation.actor";

    public const string CorrelationItemKey = "authorisation.correlationId";

    private const string AuthenticationHeaderName = "X-Development-Actor";
    private const string CorrelationHeaderName = "X-Correlation-Id";
    private const string InstitutionHeaderName = "X-Institution-Id";
    private const string BranchHeaderName = "X-Branch-Id";

    private readonly PermissionRequirement _requirement;
    private readonly string _operationName;

    public ProtectedOperationFilter(PermissionRequirement requirement, string operationName)
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

        string correlationId = ReadHeader(http, CorrelationHeaderName) ?? Guid.NewGuid().ToString("N");
        AuthenticatedActor? actor = services.GetRequiredService<IActorContextResolver>()
            .Resolve(ReadHeader(http, AuthenticationHeaderName));

        try
        {
            if (actor is not null)
            {
                // The institution comes from the validated authentication context, never from a request field.
                tenantContext.Establish(actor.InstitutionId);
            }

            AuthorisationRequest request = new()
            {
                Requirement = _requirement,
                Scope = BuildRequestedScope(http),
                Actor = actor,
            };

            AuthorisationDecision decision = services.GetRequiredService<AuthorisationService>()
                .Authorise(request, _operationName, correlationId);

            if (!decision.IsAllowed)
            {
                AuthorisationProblem problem = AuthorisationProblem.From(decision, correlationId);
                System.Collections.Generic.Dictionary<string, object?> extensions = new(StringComparer.Ordinal)
                {
                    ["code"] = problem.Code,
                    ["correlationId"] = problem.CorrelationId,
                };

                return Results.Problem(
                    detail: problem.Detail,
                    statusCode: problem.Status,
                    title: problem.Title,
                    extensions: extensions);
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

    private static ResourceScope BuildRequestedScope(HttpContext http)
    {
        string? institution = ReadHeader(http, InstitutionHeaderName);
        if (institution is null)
        {
            return ResourceScope.None;
        }

        return new ResourceScope
        {
            InstitutionId = InstitutionId.From(institution),
            BranchId = ReadHeader(http, BranchHeaderName),
        };
    }

    private static string? ReadHeader(HttpContext http, string name)
    {
        if (!http.Request.Headers.TryGetValue(name, out Microsoft.Extensions.Primitives.StringValues values))
        {
            return null;
        }

        string? value = values.ToString();
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
